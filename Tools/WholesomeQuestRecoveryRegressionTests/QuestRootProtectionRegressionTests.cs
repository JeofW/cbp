using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

// Extend the retained actual root fixture, not a synthetic replacement scheduler.
// Pet identity/health/combat are read from test-process memory through ObjectManager.
// The support effect remains a counted leaf, not a native combat action.
internal static class QuestRootProtectionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Root protection tests require Windows x86.");
        var cases = new List<(string Name, System.Action Test)>
        {
            ("real death arrival stops the quest before death support", () => DeathCase(0, false)),
            ("real ghost arrival stops the quest before death support", () => DeathCase(1, false)),
            ("stale publication cannot block death support", () => DeathCase(0, true)),
            ("alive pet combat without player combat preempts the running quest", () => PetCase(true, true, false)),
            ("stale publication retains pet combat protection", () => PetCase(true, true, true)),
            ("an idle alive pet does not restart the running quest", () => PetCase(true, false, false)),
            ("a dead pet combat flag cannot preempt a valid quest", () => PetCase(false, true, false)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS root protection: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL root protection assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR root protection fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Root protection scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual root and raw player/pet observations; controlled support; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Root protection regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void DeathCase(uint health, bool stale)
    {
        using var c = new Harness(); c.StartQuest();
        c.SetHealth(health); if (health == 1) c.SetGhostFlag(); Check(health == 0 ? c.Player.Dead : c.Player.IsGhost, "actual death/ghost observation absent");
        if (stale) c.RawProgress();
        object support = c.Composition.Children[0]; Set(support, "Status", RunStatus.Success);
        c.ExpectSupport(support, "death");
    }

    private static void PetCase(bool alive, bool combat, bool stale)
    {
        using var c = new Harness(); c.AttachPet(alive); c.StartQuest(); c.SetPetCombat(combat);
        Check(!c.Player.Combat && c.Player.GotAlivePet == alive && c.Player.Pet?.Combat == combat,
            "actual pet observations did not reach the controlled object registry");
        if (stale) c.RawProgress();
        object support = Get(c.Source, "Combat"); Set(support, "Status", RunStatus.Success);
        if (alive && combat) c.ExpectSupport(support, "combat");
        else
        {
            c.Step(); Check(c.Effects == 2 && c.Starts == 1 && c.Cleanups == 0 && (int)Get(support, "Effects") == 0,
                "non-combat pet state disturbed a valid running quest");
        }
    }

    private sealed class Harness : IDisposable
    {
        internal readonly object Source;
        private readonly object rawFixture;
        private readonly uint descriptor;
        private readonly object body;
        private readonly GroupComposite root;
        private readonly object context;
        private readonly List<string> events;
        internal readonly PrioritySelector Composition;
        private IntPtr petStorage;
        private uint petDescriptor;
        private const ulong PetGuid = 456789123;
        private Dictionary<ulong, WoWObject>? registry;
        private WoWObject? priorPet;
        private bool hadPet;
        private ulong previousSummon;
        private bool petInstalled;
        internal LocalPlayer Player => (LocalPlayer)rawFixture.GetType().GetField("Player", Hidden)!.GetValue(rawFixture)!;
        internal int Effects => (int)Get(body, "Effects");
        internal int Starts => (int)Get(body, "Starts");
        internal int Cleanups => (int)Get(body, "Cleanups");
        internal Harness()
        {
            Source = Activator.CreateInstance(typeof(QuestRootPreemptionRegressionTests).GetNestedType("Case", BindingFlags.NonPublic)!, true)!;
            rawFixture = Get(Source, "fixture"); descriptor = (uint)Get(rawFixture, "descriptor");
            body = Get(Get(Source, "Behavior"), "Body"); root = (GroupComposite)Get(Source, "Root"); context = Get(Source, "Context");
            events = (List<string>)Get(Source, "Events");
            Composition = All(root).OfType<PrioritySelector>().Single(x => x.Children.Count == 7);
            Check(Player.IsAlive && !Player.IsGhost, "root control was not initialized alive");
        }
        internal void StartQuest() => Call(Source, "StartQuest");
        internal RunStatus Step() => (RunStatus)Call(Source, "Step")!;
        internal void RawProgress() => Call(Source, "RawProgress", 1u);
        private uint Field(string name) => Convert.ToUInt32(Enum.Parse(typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFields"), name)) * 4;
        private void Write(uint address, uint value) => Call(Source, "Write", address, value);
        private void Write64(uint address, ulong value) { Write(address, (uint)value); Write(address + 4, (uint)(value >> 32)); }
        internal void SetHealth(uint health) => Write(descriptor + Field("Health"), health);
        internal void SetGhostFlag()
        {
            // WoWPlayer hides WoWUnit.IsGhost with the actual player-flag contract.
            uint field = (uint)typeof(WoWPlayer).GetField("DescPlayerFlags", StaticHidden)!.GetRawConstantValue()!;
            uint address = descriptor + field * 4;
            uint flags = unchecked((uint)Marshal.ReadInt32(new IntPtr(unchecked((int)address))));
            Write(address, flags | 16u);
        }
        internal void AttachPet(bool alive)
        {
            petStorage = Marshal.AllocHGlobal(8192); Marshal.Copy(new byte[8192], 0, petStorage, 8192);
            uint address = unchecked((uint)petStorage.ToInt32()); petDescriptor = address + 4096;
            Write(address + 8, petDescriptor); Write(address + 20, 3); Write64(address + 48, PetGuid); Write64(petDescriptor, PetGuid);
            Write(petDescriptor + Field("Health"), alive ? 100u : 0u);
            previousSummon = unchecked((ulong)Marshal.ReadInt64(new IntPtr(unchecked((int)(descriptor + Field("Summon"))))));
            registry = (Dictionary<ulong, WoWObject>)typeof(ObjectManager).GetField("_objectList", StaticHidden)!.GetValue(null)!;
            lock (typeof(ObjectManager).GetField("_updateLock", StaticHidden)!.GetValue(null)!)
            { hadPet = registry.TryGetValue(PetGuid, out priorPet); registry[PetGuid] = new WoWUnit(address); }
            petInstalled = true; Write64(descriptor + Field("Summon"), PetGuid);
            Check(Player.Pet != null && Player.Pet.Guid == PetGuid && Player.Pet.IsValid, "pet registry identity setup failed");
        }
        internal void SetPetCombat(bool active)
        {
            Type flags = typeof(WoWUnit).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "UnitFlags");
            Write(petDescriptor + Field("Flags"), active ? Convert.ToUInt32(Enum.Parse(flags, "InCombat")) : 0u);
        }
        internal void ExpectSupport(object support, string name)
        {
            int from = events.Count; int effects = Effects; Step();
            Check(Effects == effects && Cleanups == 1 && (int)Get(support, "Effects") == 1,
                "running quest was not stopped before eligible " + name + " support");
            Check(events.Skip(from).Take(2).SequenceEqual(new[] { "cleanup", name }), "protective effect preceded owned cleanup");
        }
        public void Dispose()
        {
            try { root.Stop(context); }
            finally
            {
                if (petInstalled)
                {
                    Write64(descriptor + Field("Summon"), previousSummon);
                    lock (typeof(ObjectManager).GetField("_updateLock", StaticHidden)!.GetValue(null)!)
                    { if (hadPet) registry![PetGuid] = priorPet!; else registry!.Remove(PetGuid); }
                }
                try { ((IDisposable)Source).Dispose(); }
                finally { if (petStorage != IntPtr.Zero) Marshal.FreeHGlobal(petStorage); }
            }
        }
    }
    private static IEnumerable<Composite> All(Composite root)
    { yield return root; if (root is GroupComposite group) foreach (var child in group.Children) foreach (var node in All(child)) yield return node; }
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden)!.SetValue(owner, value);
    private static object? Call(object owner, string name, params object[] args)
    {
        try { return owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
