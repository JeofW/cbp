using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Patchables;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual compiled Singular Group.MeIsTank and host aura/role readers. Controlled
// descriptors and spell names are observations, not a replacement role policy.
// Explicit server-assigned Tank roles and native raid/LFG calls are not simulated.
internal static class PaladinTankRoleRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why) : base(why) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        using var f = new Fixture();
        var cases = new List<(string Name, Action Test)>();
        foreach (bool value in new[] { false, true })
        {
            bool party = value;
            void Add(string label, Action body) => cases.Add(((party ? "party" : "solo") + ": " + label, () => { f.Reset(party); body(); }));
            Add("Ret does not become tank from leftover Fury", () => { f.Spec("RetributionPaladin"); f.Aura("Righteous Fury"); f.Expect(false); });
            Add("Holy does not become tank from leftover Fury", () => { f.Spec("HolyPaladin"); f.Aura("Righteous Fury"); f.Expect(false); });
            Add("Ret without Fury remains damage", () => { f.Spec("RetributionPaladin"); f.Expect(false); });
            Add("Holy without Fury remains non-tank", () => { f.Spec("HolyPaladin"); f.Expect(false); });
            Add("Protection with Fury retains tank fallback", () => { f.Spec("ProtectionPaladin"); f.Aura("Righteous Fury"); f.Expect(true); });
            Add("Protection without Fury retains tank fallback", () => { f.Spec("ProtectionPaladin"); f.Expect(true); });
            Add("unknown low-level spec with Fury retains existing fallback", () => { f.Spec("Lowbie"); f.Aura("Righteous Fury"); f.Expect(true); });
            Add("unknown low-level spec without Fury stays non-tank", () => { f.Spec("Lowbie"); f.Expect(false); });
            Add("an unrelated blessing is not a tank signal", () => { f.Spec("RetributionPaladin"); f.Aura("Blessing of Might"); f.Expect(false); });
            Add("Protection to Ret transition revokes tank inference", () => { f.Spec("ProtectionPaladin"); f.Aura("Righteous Fury"); f.Expect(true); f.Spec("RetributionPaladin"); f.Expect(false); });
            Add("Ret to Protection transition preserves tank eligibility", () => { f.Spec("RetributionPaladin"); f.Aura("Righteous Fury"); f.Expect(false); f.Spec("ProtectionPaladin"); f.Expect(true); });
            Add("Holy to Protection transition preserves tank eligibility", () => { f.Spec("HolyPaladin"); f.Aura("Righteous Fury"); f.Expect(false); f.Spec("ProtectionPaladin"); f.Expect(true); });
            Add("removing low-level Fury revokes the aura fallback", () => { f.Spec("Lowbie"); f.Aura("Righteous Fury"); f.Expect(true); f.Aura(null); f.Expect(false); });
            Add("adding Fury to already-observed Ret cannot change its role", () => { f.Spec("RetributionPaladin"); f.Expect(false); f.Aura("Righteous Fury"); f.Expect(false); });
        }
        int passed=0, assertions=0, unexpected=0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS Paladin tank role: " + item.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Paladin tank role assertion: " + item.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR Paladin tank role fixture/owner: " + item.Name + ": " + e); }
        }
        Console.WriteLine($"Paladin tank-role scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual full-source Group predicate and host aura readers; no game attached.");
        if (assertions+unexpected!=0) throw new InvalidOperationException($"Paladin tank-role regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable rows;
        private IDisposable? compiled;
        private readonly FieldInfo namesField=typeof(SpellDb).GetField("_spells",Hidden)!;
        private readonly FieldInfo namesReadyField=typeof(SpellDb).GetField("_initialized",Hidden)!;
        private readonly FieldInfo eventsField=typeof(Lua).GetField("_events",Hidden)!;
        private readonly object? oldNames, oldEvents;
        private readonly bool oldNamesReady;
        private readonly Dictionary<int,SpellDb.SpellData> names=new();
        private readonly LuaEvents isolatedEvents;
        private readonly LocalPlayer player;
        private readonly Memory memory;
        private readonly ThreadLocal<Dictionary<IntPtr,byte[]>> cache;
        private readonly PropertyInfo tank, spec;
        private readonly Type specType;
        private readonly int spellId;

        internal Fixture()
        {
            rows=(IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            player=ObjectManager.Me!; memory=ObjectManager.Wow!;
            cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!;
            oldNames=namesField.GetValue(null); oldNamesReady=(bool)namesReadyField.GetValue(null)!; oldEvents=eventsField.GetValue(null);
            isolatedEvents=(LuaEvents)Activator.CreateInstance(typeof(LuaEvents),true)!;
            GC.SuppressFinalize(isolatedEvents);
            try
            {
                eventsField.SetValue(null,isolatedEvents); namesField.SetValue(null,names); namesReadyField.SetValue(null,true);
                spellId=(int)rows.GetType().GetField("Id",Hidden)!.GetValue(rows)!;
                Invoke(rows.GetType().GetMethod("Publish",Hidden)!,rows,new object[]{0,20u});
                uint descriptor=memory.Read<uint>(player.BaseAddress+8);
                var field=descriptor+(uint)UnitFields.Bytes0*4;
                Marshal.WriteInt32(Pointer(field),0x0201); cache.Value!.Remove(Pointer(field));
                Reset(false);
                cache.Value![Pointer((uint)GlobalOffsets.LuaState)]=BitConverter.GetBytes(0u);
                var fixtureType=typeof(SingularBehaviorCountRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!;
                compiled=(IDisposable)Activator.CreateInstance(fixtureType,true)!;
                var assembly=((Type)fixtureType.GetField("factories",Hidden)!.GetValue(compiled)!).Assembly;
                tank=assembly.GetType("Singular.Helpers.Group",true)!.GetProperty("MeIsTank",Hidden)!;
                spec=assembly.GetType("Singular.Managers.TalentManager",true)!.GetProperty("CurrentSpec",Hidden)!;
                specType=spec.PropertyType;
                if (player.Class!=WoWClass.Paladin || ObjectManager.Executor!=null)
                    throw new InvalidOperationException("Actual Paladin descriptor or no-executor boundary was not established.");
            }
            catch { Dispose(); throw; }
        }
        internal void Reset(bool party)
        {
            foreach(uint address in new[]{12392776u,12392784u,12392792u,12392800u})
                cache.Value![Pointer(address)]=BitConverter.GetBytes(party && address==12392776u ? 999ul : 0ul);
            cache.Value![Pointer(12498440u)]=BitConverter.GetBytes(0);
            Aura(null);
            if (player.IsInParty!=party || player.IsInRaid || player.Role!=WoWPartyMember.GroupRole.None)
                throw new InvalidOperationException("Controlled ordinary-party/solo role observation was not established.");
        }
        internal void Spec(string name)
        {
            Invoke(spec.GetSetMethod(true)!,null,new[]{Enum.Parse(specType,name)});
        }
        internal void Aura(string? name)
        {
            uint count=player.BaseAddress+3536;
            Marshal.WriteInt32(Pointer(count),name==null ? 0 : 1); cache.Value!.Remove(Pointer(count));
            names.Clear();
            if(name!=null)
            {
                names[spellId]=new SpellDb.SpellData{Id=spellId,Name=name};
                var bytes=new byte[24]; BitConverter.GetBytes(player.Guid).CopyTo(bytes,0); BitConverter.GetBytes(spellId).CopyTo(bytes,8);
                bytes[12]=49; bytes[13]=20; bytes[14]=1;
                Marshal.Copy(bytes,0,Pointer(player.BaseAddress+3152),bytes.Length);
            }
            if ((name!=null && !player.HasAura(name)) || (name==null && player.GetAllAuras().Count!=0))
                throw new InvalidOperationException("Actual aura reader did not observe the controlled aura.");
        }
        internal void Expect(bool expected)
        {
            bool actual=(bool)Invoke(tank.GetGetMethod(true)!,null,Array.Empty<object>())!;
            if (ObjectManager.Executor!=null || !ReferenceEquals(ObjectManager.Me,player) || !ReferenceEquals(ObjectManager.Wow,memory))
                throw new InvalidOperationException("Predicate changed world ownership or created a native executor.");
            if(actual!=expected) throw new AssertionFailure($"expected tank={expected}, observed {actual} for {spec.GetValue(null)}; Fury={player.HasAura("Righteous Fury")}");
        }
        public void Dispose()
        {
            compiled?.Dispose();
            eventsField.SetValue(null,oldEvents); namesField.SetValue(null,oldNames); namesReadyField.SetValue(null,oldNamesReady);
            rows.Dispose();
        }
        private static IntPtr Pointer(uint address)=>new(unchecked((int)address));
    }
    private static object? Invoke(MethodInfo method,object? target,object[] arguments)
    {
        try { return method.Invoke(target,arguments); }
        catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
}
