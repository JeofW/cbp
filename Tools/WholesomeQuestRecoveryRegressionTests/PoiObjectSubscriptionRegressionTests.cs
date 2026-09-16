using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual BotPoi getters and WoWObject event dispatch with real descriptor-backed
// wrappers. Subscription counts are deterministic ownership assertions, not a
// claim about measured GC pressure or historical pulse latency.
internal static class PoiObjectSubscriptionRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const uint Entry = 54321;
    private static readonly WoWPoint Location = new(40, 50, 60);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("POI object subscriptions require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (bool gameObject in new[] { false, true })
            foreach (bool direct in new[] { false, true })
            {
                bool obj = gameObject, construct = direct;
                string label = (construct ? "direct" : "lazy") + (obj ? " object" : " NPC");
                cases.Add((label + " first acquisition owns exactly one subscription", f => f.First(obj, construct)));
                cases.Add((label + " repeated object typed and location reads do not multiply subscriptions", f => f.Repeated(obj, construct)));
                cases.Add((label + " invalidation releases the entire owned subscription", f => f.Invalidate(obj, construct, false)));
                cases.Add((label + " invalidation retains unrelated subscriber exactly once", f => f.Invalidate(obj, construct, true)));
            }
        cases.Add(("invalid wrapper without replacement releases its old event owner", f => f.NoReplacement()));
        cases.Add(("invalid wrapper replacement detaches old and binds new exactly once", f => f.Replace(false, false)));
        cases.Add(("late old event cannot revoke the current replacement", f => f.Replace(true, false)));
        cases.Add(("captured old multicast delegate cannot revoke replacement", f => f.Replace(false, true)));
        cases.Add(("earlier invalidation subscriber replacement survives the old multicast tail", f => f.ReentrantDispatch()));
        cases.Add(("two POIs own independent bounded subscriptions", f => f.TwoObservers()));
        cases.Add(("missing object does not become a permanent null cache", f => f.LateArrival()));
        cases.Add(("twenty replacement lifetimes leave no subscriptions on retired wrappers", f => f.ManyReplacements()));
        cases.Add(("null object construction never registers an event", _ =>
        {
            var poi = new BotPoi((WoWObject)null!, PoiType.QuestPickUp);
            Check(poi.Type == PoiType.None && poi.AsObject == null, "null constructor changed");
        }));
        cases.Add(("invalid object construction never registers an event", f => f.InvalidConstruction()));
        cases.Add(("single-read invalidation retains ordinary release behavior", f => f.SingleInvalidation()));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS POI object subscription: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL POI object subscription assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR POI object subscription fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"POI object-subscription scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual POI getters and invalidation owner; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"POI object-subscription regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static BotPoi Lazy(bool obj) => new(new PickUpNode(Location, Entry, "target", obj ? QuestObjectType.GameObject : QuestObjectType.Npc, 867, "quest"));
    private static ObjectInvalidateDelegate? Handlers(WoWObject obj) =>
        (ObjectInvalidateDelegate?)typeof(WoWObject).GetField("_onInvalidate", Hidden)!.GetValue(obj);
    private static int Count(WoWObject obj) => Handlers(obj)?.GetInvocationList().Length ?? 0;
    private static WoWObject? Cached(BotPoi poi) => (WoWObject?)typeof(BotPoi).GetField("_asObject", Hidden)!.GetValue(poi);
    private static void Dispatch(WoWObject obj) => Invoke(typeof(WoWObject), obj, "OnInvalidated");
    private static object? Invoke(Type type, object target, string name, params object[] args)
    {
        var method = type.GetMethod(name, Hidden) ?? throw new InvalidOperationException("Missing actual fixture/owner method " + name);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly object source = Activator.CreateInstance(typeof(QuestPoiTargetIdentityRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
        private WoWObject Add(bool obj) => (WoWObject)Invoke(source.GetType(), source, "Add", obj, Entry)!;
        private void Retire(WoWObject obj)
        {
            ulong guid = obj.Guid;
            var registry = (Dictionary<ulong, WoWObject>)source.GetType().GetField("registry", Hidden)!.GetValue(source)!;
            var sync = source.GetType().GetField("registryLock", Hidden)!.GetValue(source)!;
            lock (sync) registry.Remove(guid);
            Invoke(typeof(WoWObject), obj, "UpdateBaseAddress", 0U);
            typeof(ObjectManager).GetMethod("ResetCaches", StaticHidden)!.Invoke(null, null);
            if (obj.IsValid) throw new InvalidOperationException("Retired wrapper remained valid");
        }
        private BotPoi Acquire(WoWObject obj, bool direct)
        {
            var poi = direct ? new BotPoi(obj, PoiType.QuestPickUp) : Lazy(obj is WoWGameObject);
            if (!ReferenceEquals(poi.AsObject, obj)) throw new InvalidOperationException("Actual lookup did not find the intended wrapper");
            return poi;
        }
        internal void First(bool obj, bool direct)
        {
            var target = Add(obj);
            var poi = direct ? new BotPoi(target, PoiType.QuestPickUp) : Lazy(obj);
            if (!direct) Check(ReferenceEquals(poi.AsObject, target), "first lazy lookup did not bind");
            Check(Count(target) == 1, "first acquisition did not own one subscription");
        }
        internal void Repeated(bool obj, bool direct)
        {
            var target = Add(obj); var poi = Acquire(target, direct);
            for (int i = 0; i < 50; i++)
            {
                Check(ReferenceEquals(poi.AsObject, target), "object getter changed identity");
                Check(ReferenceEquals(obj ? (WoWObject?)poi.AsGameObject : poi.AsUnit, target), "typed getter changed identity");
                _ = poi.Location;
            }
            Check(Count(target) == 1, "reads grew the owned event subscription to " + Count(target));
        }
        internal void Invalidate(bool obj, bool direct, bool external)
        {
            var target = Add(obj); int externalCalls = 0;
            ObjectInvalidateDelegate listener = () => externalCalls++;
            if (external) target.OnInvalidate += listener;
            try
            {
                var poi = Acquire(target, direct);
                for (int i = 0; i < 20; i++) _ = poi.AsObject;
                Dispatch(target);
                Check(Cached(poi) == null, "invalidation left its own cached wrapper");
                Check(Count(target) == (external ? 1 : 0), "owned handlers remain after invalidation");
                Check(externalCalls == (external ? 1 : 0), "unrelated listener was consumed or repeated");
            }
            finally { if (external) target.OnInvalidate -= listener; }
        }
        internal void NoReplacement()
        {
            var old = Add(false); var poi = Acquire(old, false); Retire(old);
            Check(poi.AsObject == null && Cached(poi) == null, "missing replacement was fabricated");
            Check(Count(old) == 0, "invalid cached wrapper retained subscription after missing lookup");
        }
        internal void Replace(bool dispatchOld, bool captured)
        {
            var old = Add(false); var poi = Acquire(old, false); var oldInvocation = Handlers(old)!;
            Retire(old); var next = Add(false);
            Check(ReferenceEquals(poi.AsObject, next), "replacement was not acquired");
            if (dispatchOld) Dispatch(old);
            if (captured) oldInvocation();
            Check(ReferenceEquals(Cached(poi), next), "obsolete invalidation revoked replacement cache");
            Check(Count(old) == 0 && Count(next) == 1, "replacement event owners were not isolated");
        }
        internal void ReentrantDispatch()
        {
            var old = Add(false); BotPoi? poi = null; WoWObject? next = null; Exception? fixtureError = null; int calls = 0;
            ObjectInvalidateDelegate earlier = () =>
            {
                calls++;
                try { Retire(old); next = Add(false); if (!ReferenceEquals(poi!.AsObject, next)) throw new InvalidOperationException("Nested lookup failed"); }
                catch (Exception error) { fixtureError = error; throw; }
            };
            old.OnInvalidate += earlier;
            try
            {
                poi = Acquire(old, false); Dispatch(old);
                if (fixtureError != null) throw new InvalidOperationException("Nested fixture failed", fixtureError);
                Check(calls == 1 && next != null && ReferenceEquals(Cached(poi), next), "old multicast tail revoked nested replacement");
                Check(Count(old) == 1 && Count(next!) == 1, "old ownership remained attached or replacement was detached");
            }
            finally { old.OnInvalidate -= earlier; }
        }
        internal void TwoObservers()
        {
            var target = Add(false); var a = Acquire(target, false); var b = Acquire(target, false);
            for (int i = 0; i < 20; i++) { _ = a.AsObject; _ = b.AsObject; }
            Check(Count(target) == 2, "independent POIs accumulated duplicate registrations");
            Dispatch(target); Check(Cached(a) == null && Cached(b) == null && Count(target) == 0, "event did not release both owners");
        }
        internal void LateArrival()
        {
            var poi = Lazy(false); Check(poi.AsObject == null, "empty registry resolved an object");
            var arrived = Add(false); Check(ReferenceEquals(poi.AsObject, arrived) && Count(arrived) == 1, "late arrival was not observed exactly once");
        }
        internal void ManyReplacements()
        {
            var current = Add(false); var poi = Acquire(current, false); var retired = new List<WoWObject>();
            for (int i = 0; i < 20; i++)
            {
                retired.Add(current); Retire(current); current = Add(false);
                Check(ReferenceEquals(poi.AsObject, current), "replacement identity failed");
            }
            Check(retired.All(o => Count(o) == 0) && Count(current) == 1, "retired lifetimes retain POI registrations");
        }
        internal void InvalidConstruction()
        {
            var target = Add(false); Retire(target); var poi = new BotPoi(target, PoiType.QuestPickUp);
            Check(poi.Type == PoiType.None && poi.AsObject == null && Count(target) == 0, "invalid constructor registered a handler");
        }
        internal void SingleInvalidation()
        {
            var target = Add(false); var poi = Acquire(target, false); Dispatch(target);
            Check(Cached(poi) == null && Count(target) == 0, "ordinary single subscription was not released");
        }
        public void Dispose() => ((IDisposable)source).Dispose();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
