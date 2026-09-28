using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Bots.Gatherbuddy;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

// Actual compiled gather builder, wait and dismount-admission predicate. Only
// movement/dismount command leaves are recorded; flags are in this test process.
// No executor, game, real landing, geometry or native command success is supplied.
internal static class GatherbuddyLandingRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4) throw new PlatformNotSupportedException("Landing tests require Windows x86.");
        var tests = new List<(string Name, System.Action<Fixture> Test)>
        {
            ("expired airborne descent never admits dismount", f =>
            {
                f.Wait.Timeout = TimeSpan.Zero;
                Check(f.Tick() == RunStatus.Success, "bounded airborne phase did not release for another tick");
                Check(f.Removals == 0 && f.Stops == 1, "timeout removed flight or retained descent");
            }),
            ("repeated airborne timeouts retain flight", f =>
            {
                f.Wait.Timeout = TimeSpan.Zero;
                for (int i=0;i<3;i++) { Check(f.Tick() == RunStatus.Success, "timeout did not return"); f.Restart(); }
                Check(f.Removals == 0 && f.Stops == 3, "retry removed flight or failed to stop its descent");
            }),
            ("landing outside interaction range admits one dismount", f =>
            {
                f.BeginLanding();
                Check(f.Tick() == RunStatus.Success && f.Removals == 1 && f.Stops == 1, "valid landed dismount was lost");
            }),
            ("landing inside interaction range preserves current form", f =>
            {
                f.Node.Position = f.Player.Location;
                f.BeginLanding();
                Check(f.Tick() == RunStatus.Success && f.Removals == 0 && f.Stops == 1, "in-range form compatibility changed");
            }),
            ("ground observation skips the airborne phase", f =>
            {
                f.Flags(0);
                Check(f.Tick() == RunStatus.Failure && f.Starts == 0 && f.Removals == 0, "ground actor entered descent");
            })
        };
        foreach (string state in new[] { "short-fall", "long-fall", "both-fall-flags", "transport", "missing-movement", "unreadable-movement", "overflow-movement", "dead", "player-reference", "player-guid", "node-reference", "node-guid", "missing-node", "raw-guid-reuse", "stale-pointer-cache", "stale-airborne-cache", "stale-transport-cache" })
        {
            string change = state;
            tests.Add(("landing revalidates " + change, f =>
            {
                f.BeginLanding(); f.Change(change);
                Check(f.Tick() == RunStatus.Success && f.Removals == 0, "unsafe or stale landing admitted dismount");
            }));
        }
        tests.Add(("flight resuming during cleanup revokes dismount", f =>
        {
            f.BeginLanding(); f.AfterStop = () => f.Flags(0x02000000);
            Check(f.Tick() == RunStatus.Success && f.Removals == 0, "late airborne observation was ignored");
        }));
        tests.Add(("observation preserves airborne flags and transport identity", f =>
        {
            f.Change("transport");
            Check(f.Player.TryGetMovementState(out uint flags,out ulong transport) && flags==0x02000000U && transport==99,
                "movement observation changed the raw values into policy");
            Check(ObjectManager.Wow!.CacheEnabled,"successful observation did not restore enabled cache");
        }));
        tests.Add(("complete zero flags remain an available observation", f =>
        {
            f.Flags(0);
            Check(f.Player.TryGetMovementState(out uint flags,out ulong transport) && flags==0 && transport==0,
                "valid stationary observation was confused with read failure");
        }));
        tests.Add(("failed uncached observation clears outputs and restores cache", f =>
        {
            f.Change("stale-pointer-cache");
            uint flags=uint.MaxValue; ulong transport=ulong.MaxValue;
            Check(!f.Player.TryGetMovementState(out flags,out transport) && flags==0 && transport==0,
                "failed read returned available or retained output values");
            Check(ObjectManager.Wow!.CacheEnabled,"failed observation did not restore enabled cache");
        }));
        tests.Add(("previously disabled cache stays disabled after success and failure", f =>
        {
            ObjectManager.Wow!.DisableCache();
            Check(f.Player.TryGetMovementState(out _,out _) && !ObjectManager.Wow.CacheEnabled,"success enabled a previously disabled cache");
            f.Change("raw-guid-reuse"); ObjectManager.Wow.DisableCache();
            Check(!f.Player.TryGetMovementState(out _,out _) && !ObjectManager.Wow.CacheEnabled,"failure enabled a previously disabled cache");
        }));
        tests.Add(("root alone is not a fall during landing", f =>
        {
            f.BeginLanding(); f.Flags(0x800);
            Check(f.Tick()==RunStatus.Success && f.Removals==1,"root flag was mistaken for an airborne actor");
        }));
        foreach (uint rawFlags in new uint[] { 0, 0x800, 0x1000, 0x2000, 0x3000, 0x1800 })
        {
            uint expected=rawFlags;
            tests.Add(($"movement-info falling getters use build12340 flags/{expected:x}", f =>
            {
                f.Flags(expected);
                Check(f.Movement.IsFalling==((expected&0x1000)!=0),"movement-info falling bit differs from original-client layout");
                Check(f.Movement.JumpingOrShortFalling==((expected&0x1000)!=0),"short-fall bit differs from original-client layout");
                // The unit-level API deliberately retains the native IsFalling
                // root exclusion; it is distinct from the raw movement-info bit.
                Check(f.Player.IsFalling==((expected&0x1000)!=0 && (expected&0x800)==0),"native-compatible unit falling semantics changed");
            }));
        }
        int passed=0, assertions=0, unexpected=0;
        foreach (var test in tests)
        {
            try { using var f=new Fixture(); test.Test(f); passed++; Console.WriteLine("PASS GatherBuddy landing: " + test.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL GatherBuddy landing: " + test.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR GatherBuddy landing: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"GatherBuddy landing scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual compiled builder/wait/admission, controlled native leaves and owned process data; no game attached.");
        if (assertions+unexpected != 0) throw new InvalidOperationException($"GatherBuddy landing regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class ObservedPlayer : LocalPlayer
    {
        internal bool Alive=true;
        internal ulong Identity=123;
        internal ObservedPlayer(uint address) : base(address) { }
        public override ulong Guid => Identity;
        public override bool IsAlive => Alive;
        public override bool IsGhost => false;
        public override bool Mounted => true;
        public override WoWPoint Location => new WoWPoint(10,10,10);
    }
    private sealed class ObservedNode : WoWObject
    {
        internal WoWPoint Position=new WoWPoint(40,10,10);
        internal ulong Identity=999;
        internal ObservedNode(uint address) : base(address) { }
        public override WoWPoint Location => Position;
        public override ulong Guid => Identity;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable? world;
        private readonly object bot;
        private readonly GroupComposite? root;
        private readonly IntPtr movement = Marshal.AllocHGlobal(512);
        private readonly FieldInfo nodeField=typeof(GatherbuddyBot).GetField("_currentNode",BindingFlags.Static|BindingFlags.NonPublic)!;
        private readonly FieldInfo approachField=typeof(GatherbuddyBot).GetField("_approachPoint",BindingFlags.Static|BindingFlags.NonPublic)!;
        private readonly object? previousNode, previousApproach;
        internal readonly ObservedPlayer Player;
        internal readonly ObservedNode Node;
        internal readonly WaitContinue Wait;
        internal int Starts, Stops, Removals;
        internal System.Action? AfterStop;
        internal Fixture()
        {
          previousNode=nodeField.GetValue(null); previousApproach=approachField.GetValue(null);
          try
          {
            world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            Player=new ObservedPlayer(ObjectManager.Me.BaseAddress); ObjectManager.Me=Player;
            Node=new ObservedNode(Player.BaseAddress);
            Marshal.Copy(new byte[512],0,movement,512);
            Cache(Player.BaseAddress+216,BitConverter.GetBytes(unchecked((uint)movement.ToInt32())));
            Cache(unchecked((uint)movement.ToInt32())+8,BitConverter.GetBytes(0UL));
            Flags(0x02000000);
            bot=RuntimeHelpers.GetUninitializedObject(typeof(GatherbuddyBot));
            SetNode(Node);
            var group=(GroupComposite)typeof(GatherbuddyBot).GetMethod("CreateGatherBehavior",Hidden)!.Invoke(bot,null)!;
            root=(GroupComposite)group.Children[4];
            if (root is not Decorator || root.Children.Count!=1 || root.Children[0] is not Sequence phase || phase.Children.Count!=6
                || phase.Children[2] is not WaitContinue wait || phase.Children[4] is not DecoratorContinue remove || remove.Children.Count!=1)
                throw new InvalidOperationException("Actual gather landing phase changed; review command boundaries.");
            Wait=wait;
            phase.Children[1]=new TreeSharp.Action(_ => { Starts++; return RunStatus.Success; }) {Parent=phase};
            phase.Children[3]=new TreeSharp.Action(_ => { Stops++; AfterStop?.Invoke(); return RunStatus.Success; }) {Parent=phase};
            phase.Children[5]=new TreeSharp.Action(_ => RunStatus.Success) {Parent=phase};
            remove.Children[0]=new TreeSharp.Action(_ => { Removals++; return RunStatus.Success; }) {Parent=remove};
            Check(ObjectManager.Executor==null && Player.IsValid && Player.IsFlying, "controlled movement fixture invalid");
            root.Start(null!);
          }
          catch { Dispose(); throw; }
        }
        private void Cache(uint address, byte[] bytes)
        {
            // Mirror healthy fixture values in its owned allocations so both
            // cached and deliberately uncached production reads see real data.
            bool ownedUnit = address >= Player.BaseAddress && (ulong)address + (uint)bytes.Length <= (ulong)Player.BaseAddress + 65536;
            uint movementBase=unchecked((uint)movement.ToInt32());
            bool ownedMovement = address >= movementBase && (ulong)address + (uint)bytes.Length <= (ulong)movementBase + 512;
            if (!ownedUnit && !ownedMovement) throw new InvalidOperationException("Fixture write outside owned allocation.");
            Marshal.Copy(bytes,0,new IntPtr(unchecked((int)address)),bytes.Length);
            var cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)ObjectManager.Wow!.GetType().GetField("_cache",Hidden)!.GetValue(ObjectManager.Wow)!;
            cache.Value![new IntPtr(unchecked((int)address))]=bytes;
        }
        internal void Flags(uint value) => Cache(unchecked((uint)movement.ToInt32())+68,BitConverter.GetBytes(value));
        internal WoWMovementInfo Movement => new WoWMovementInfo(unchecked((uint)movement.ToInt32()));
        internal RunStatus Tick()
        {
            bool cache=ObjectManager.Wow!.CacheEnabled;
            var status=root!.Tick(null!);
            Check(ObjectManager.Wow!.CacheEnabled==cache,"landing phase leaked its observation cache setting");
            return status;
        }
        internal void Restart() { root!.Stop(null!); root.Start(null!); }
        internal void BeginLanding()
        {
            Check(Tick()==RunStatus.Running && Starts==1 && Removals==0, "did not wait for a landing observation");
            Flags(0);
        }
        private void SetNode(WoWObject? value) => nodeField.SetValue(null,value);
        internal void Change(string state)
        {
            switch(state)
            {
                case "short-fall": Flags(0x1000); break;
                case "long-fall": Flags(0x2000); break;
                case "both-fall-flags": Flags(0x3000); break;
                case "transport": Cache(unchecked((uint)movement.ToInt32())+8,BitConverter.GetBytes(99UL)); break;
                case "missing-movement": Cache(Player.BaseAddress+216,BitConverter.GetBytes(0u)); break;
                case "unreadable-movement": Cache(Player.BaseAddress+216,BitConverter.GetBytes(1u)); break;
                case "overflow-movement": Cache(Player.BaseAddress+216,BitConverter.GetBytes(uint.MaxValue)); break;
                case "dead": Player.Alive=false; break;
                case "player-reference": ObjectManager.Me=new ObservedPlayer(Player.BaseAddress); break;
                case "player-guid": Player.Identity++; break;
                case "node-reference": SetNode(new ObservedNode(Player.BaseAddress)); break;
                case "node-guid": Node.Identity++; break;
                case "missing-node": SetNode(null); break;
                case "raw-guid-reuse": Marshal.WriteInt64(new IntPtr(unchecked((int)Player.BaseAddress)),48,567); break;
                case "stale-pointer-cache": Marshal.WriteInt32(new IntPtr(unchecked((int)Player.BaseAddress)),216,1); break;
                case "stale-airborne-cache": Marshal.WriteInt32(movement,68,0x02000000); break;
                case "stale-transport-cache": Marshal.WriteInt64(movement,8,99); break;
            }
        }
        public void Dispose()
        {
            root?.Stop(null!);
            nodeField.SetValue(null,previousNode); approachField.SetValue(null,previousApproach);
            world?.Dispose(); Marshal.FreeHGlobal(movement);
        }
    }
    private static void Check(bool condition,string why) { if(!condition) throw new AssertionFailure(why); }
}
