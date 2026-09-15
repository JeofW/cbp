using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

// Actual StartSellSession callback and protection-publication owner. Invoking
// that method does not sell an item or enter MerchantFrame/Lua. The test fixture
// supplies an empty bag observation and restores every changed static owner.
internal static class VendorSessionBoundaryRegressionTests
{
    private const uint Item = 190010221;
    private const string Name = "w42-vendor-owner-190010221";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Vendor session owner tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("ordinary start publishes one session without dispatching a sale", f =>
            { Check(f.Start() && f.Active, "normal session did not start"); Check(f.Ids.Contains(Item + 2) && f.Ids.Contains(Item + 3), "configured supply protection changed"); }),
            ("successful callback exclusions accumulate with clean argument lists", f =>
            {
                int first = 0, second = 0;
                Vendors.OnVendorItems = args => { first++; args.IdExceptions.Add(Item); args.NameExceptions.Add(Name); };
                Vendors.OnVendorItems += args => { second++; Check(args.IdExceptions.Count == 0 && args.NameExceptions.Count == 0, "callback arguments leaked"); args.IdExceptions.Add(Item + 1); };
                Check(f.Start() && first == 1 && second == 1 && f.Ids.Contains(Item) && f.Ids.Contains(Item + 1) && f.Names.Contains(Name), "callback exclusion control failed");
            }),
            ("ordinary callback failure retains later handlers and discards partial exclusions", f =>
            {
                int first = 0, second = 0;
                Vendors.OnVendorItems = args => { first++; args.IdExceptions.Add(Item); throw new InvalidOperationException("ordinary plugin failure"); };
                Vendors.OnVendorItems += args => { second++; Check(args.IdExceptions.Count == 0, "failed callback exclusions leaked"); args.IdExceptions.Add(Item + 1); };
                Check(f.Start() && first == 1 && second == 1 && !f.Ids.Contains(Item) && f.Ids.Contains(Item + 1), "ordinary failure compatibility changed");
            }),
            ("first callback cancellation propagates exactly and publishes no session", f => Stop(f, new OperationCanceledException("vendor stop"), false)),
            ("first callback interruption propagates exactly and publishes no session", f => Stop(f, new ThreadInterruptedException("vendor stop"), false)),
            ("cancellation after a successful callback cannot publish partial exclusions", f => Stop(f, new OperationCanceledException("vendor stop"), true)),
            ("interruption after a successful callback cannot publish partial exclusions", f => Stop(f, new ThreadInterruptedException("vendor stop"), true)),
            ("ordinary earlier error does not swallow later cancellation", f => StopAfterOrdinary(f, new OperationCanceledException("later stop"))),
            ("ordinary earlier error does not swallow later interruption", f => StopAfterOrdinary(f, new ThreadInterruptedException("later stop"))),
            ("manual protection added by a callback reaches the published session", f =>
            {
                int calls = 0; Vendors.OnVendorItems = _ => { calls++; ProtectedItemsManager.Add(Item); };
                try { Check(f.Start() && calls == 1 && f.Ids.Contains(Item), "new manual protection was absent from the session"); }
                finally { ProtectedItemsManager.Remove(Item); }
            }),
            ("named protection added by a callback reaches the published session", f =>
            {
                int calls = 0; Vendors.OnVendorItems = _ => { calls++; ProtectedItemsManager.Add(Name); };
                try { Check(f.Start() && calls == 1 && f.Names.Contains(Name), "new named protection was absent from the session"); }
                finally { ProtectedItemsManager.Remove(Name); }
            }),
            ("leased protection acquired by a callback reaches the published session", f =>
            {
                IDisposable? owner = null; int calls = 0;
                Vendors.OnVendorItems = _ => { calls++; owner = ProtectedItemsManager.Acquire(Item); };
                try { Check(f.Start() && calls == 1 && f.Ids.Contains(Item), "new leased protection was absent from the session"); }
                finally { owner?.Dispose(); }
            }),
            ("a callback release cannot remove already captured conservative protection", f =>
            {
                using var owner = ProtectedItemsManager.Acquire(Item); int calls = 0;
                Vendors.OnVendorItems = _ => { calls++; owner.Dispose(); };
                Check(f.Start() && calls == 1 && f.Ids.Contains(Item), "release unexpectedly removed captured protection");
            }),
            ("profile replacement during a callback invalidates the old session candidate", f =>
            {
                int calls = 0; Vendors.OnVendorItems = _ => { calls++; f.Activate(new Profile()); };
                bool result = f.Start(); Check(calls == 1 && !result && f.InactiveRetained(), "old candidate published across a profile replacement");
            }),
            ("missing profile rejects before invoking callbacks", f =>
            {
                int calls = 0; Vendors.OnVendorItems = _ => calls++; f.Activate(null);
                Check(!f.Start() && calls == 0 && f.InactiveRetained(), "missing-profile control failed");
            }),
            ("a cancelled candidate does not prevent a later fresh successful start", f =>
            {
                Stop(f, new OperationCanceledException("first attempt"), false);
                Vendors.OnVendorItems = args => args.IdExceptions.Add(Item);
                Check(f.Start() && f.Active && f.Ids.Contains(Item), "fresh attempt retained cancelled candidate state");
            }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS vendor-session boundary: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL vendor-session boundary assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR vendor-session boundary fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Vendor-session boundary scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual callback and publication owner; no sale/Lua dispatch or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Vendor-session boundary regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void Stop(Fixture f, Exception signal, bool successfulFirst)
    {
        int earlier = 0, stopping = 0, later = 0;
        Vendors.OnVendorItems = successfulFirst ? args => { earlier++; args.IdExceptions.Add(Item); } : null;
        Vendors.OnVendorItems += args => { stopping++; args.NameExceptions.Add(Name); throw signal; };
        Vendors.OnVendorItems += _ => later++;
        Exception? caught = null; try { f.Start(); } catch (Exception error) { caught = error; }
        Check(earlier == (successfulFirst ? 1 : 0) && stopping == 1 && later == 0 && ReferenceEquals(caught, signal) && f.InactiveRetained(),
            $"stopping={stopping}; later={later}; exact-signal={ReferenceEquals(caught, signal)}; candidate-retained={f.InactiveRetained()}");
    }
    private static void StopAfterOrdinary(Fixture f, Exception signal)
    {
        int ordinary = 0, stopping = 0, later = 0;
        Vendors.OnVendorItems = _ => { ordinary++; throw new InvalidOperationException("ordinary plugin failure"); };
        Vendors.OnVendorItems += _ => { stopping++; throw signal; };
        Vendors.OnVendorItems += _ => later++;
        Exception? caught = null; try { f.Start(); } catch (Exception error) { caught = error; }
        Check(ordinary == 1 && stopping == 1 && later == 0 && ReferenceEquals(caught, signal) && f.InactiveRetained(), "later stop was swallowed, replaced or published");
    }
    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly object world;
        private readonly CharacterSettings? settings = CharacterSettings.Instance;
        private readonly VendorItemsEventHandler? handlers = Vendors.OnVendorItems;
        private readonly bool forceSell = Vendors.ForceSell;
        private readonly Dictionary<FieldInfo, object?> saved = typeof(Vendors).GetFields(Hidden).Where(f => f.Name.StartsWith("_sellSession", StringComparison.Ordinal)).ToDictionary(f => f, f => f.GetValue(null));
        private readonly FieldInfo activeProfile = typeof(ProfileManager).GetField("_currentProfile", Hidden)!;
        private readonly FieldInfo profileless = typeof(ProfileManager).GetField("_profileless", Hidden)!;
        private readonly List<uint> retainedIds = new() { Item + 10 };
        private readonly List<string> retainedNames = new() { "retained vendor candidate" };
        internal Fixture()
        {
            world = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                var player = ObjectManager.Me;
                Check(player != null, "controlled player was not installed");
                // Inventory items belong to the fixture's allocated 64-KiB block.
                // Seed the native bag-GUID globals in its memory cache as well;
                // an unreadable global must not masquerade as an empty inventory.
                uint inventory = player!.BaseAddress + 6384U;
                uint items = player.BaseAddress + 55000U;
                byte[] bag = new byte[17];
                BitConverter.GetBytes(150U).CopyTo(bag, 0);
                BitConverter.GetBytes(items).CopyTo(bag, 4); bag[16] = 1;
                Marshal.Copy(bag, 0, new IntPtr(unchecked((int)inventory)), bag.Length);
                var bytes = world.GetType().GetMethod("Bytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
                bytes.Invoke(world, new object[] { inventory, bag });
                for (uint i = 0; i < 4; i++)
                {
                    bytes.Invoke(world, new object[] { 12727616U + 8U * i, new byte[8] });
                    Check(player.GetBagGuidAtIndex(i) == 0, "controlled bag GUID was not empty");
                }
                var backpack = player.Inventory.Backpack.ItemGuids;
                Check(backpack.Length == 16 && backpack.All(guid => guid == 0) && player.BagItems.Count == 0,
                    "fixture requires 16 observed empty backpack slots and no equipped bags");
                var next = (CharacterSettings)RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings));
                typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, next);
                next.FoodName = (Item + 2).ToString(); next.DrinkName = (Item + 3).ToString();
                profileless.SetValue(null, true); Activate(new Profile());
                Check(!ProtectedItemsManager.Contains(Item) && !ProtectedItemsManager.Contains(Item + 1) && !ProtectedItemsManager.Contains(Name), "unique test protection was already owned");
                Vendors.OnVendorItems = null; Vendors.ForceSell = true;
                Set("_sellSessionActive", false); Set("_sellSessionProtectedIds", retainedIds); Set("_sellSessionProtectedNames", retainedNames); Set("_sellSessionStackCount", 17);
            }
            catch { Dispose(); throw; }
        }
        internal bool Start()
        {
            try { return (bool)typeof(Vendors).GetMethod("StartSellSession", Hidden)!.Invoke(null, null)!; }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        internal bool Active => (bool)Get("_sellSessionActive")!;
        internal List<uint> Ids => (List<uint>)Get("_sellSessionProtectedIds")!;
        internal List<string> Names => (List<string>)Get("_sellSessionProtectedNames")!;
        internal bool InactiveRetained() => !Active && ReferenceEquals(Ids, retainedIds) && ReferenceEquals(Names, retainedNames) && (int)Get("_sellSessionStackCount")! == 17 && Vendors.ForceSell;
        internal void Activate(Profile? profile) => activeProfile.SetValue(null, profile);
        private static object? Get(string name) => typeof(Vendors).GetField(name, Hidden)!.GetValue(null);
        private static void Set(string name, object value) => typeof(Vendors).GetField(name, Hidden)!.SetValue(null, value);
        public void Dispose()
        {
            try
            {
                Vendors.OnVendorItems = handlers; Vendors.ForceSell = forceSell;
                foreach (var item in saved) item.Key.SetValue(null, item.Value);
                typeof(CharacterSettings).GetProperty("Instance")!.SetValue(null, settings);
            }
            finally { ((IDisposable)world).Dispose(); }
        }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
