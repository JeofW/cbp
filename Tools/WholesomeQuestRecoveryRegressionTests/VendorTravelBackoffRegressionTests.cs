using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

internal static class VendorTravelBackoffRegressionTests
{
    private const int Entry = 991701;
    private static readonly WoWPoint Destination = new(100, 100, 30);
    private static readonly DateTime Now = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Run)>
        {
            ("stationary Pulse cannot persist a vendor blacklist", NoPersistenceFromPulse),
            ("fresh failed service travel gets a two-minute endpoint retry", Defers),
            ("duplicate observations cannot extend a retry", Duplicate),
            ("backoff distinguishes map, entry and floor", Identity),
            ("exact deadline and lifecycle reset restore eligibility", ExpiryAndReset),
            ("one owner cannot clear another owner's travel backoff", OwnerIsolation),
            ("cache remains bounded across many failed endpoints", Bounded),
            ("clock rollback cannot retain a future observation", ClockRollback),
            ("explicit manual exclusions remain authoritative", Manual),
            ("core profile and automatic-fallback predicate sees temporary backoff", CoreSelection),
            ("Wholesome filtering precedes nearest-candidate truncation", WholesomeSelection),
            ("read-only filtering never mutates the saved blacklist", ManualUnchanged)
        };
        foreach (string flag in new[] { "IsPaused", "IsCombat", "IsDead", "IsResting", "IsOnTaxi", "IsOnTransport", "IsElevatorTransit", "HasActivePath", "IsServiceFrameOpen" })
        {
            string captured = flag;
            cases.Add(("suspend travel penalties while " + captured, () => RejectSample(captured, true)));
        }
        cases.AddRange(new (string, Action)[]
        {
            ("unattached and non-service work is not a failed vendor trip", InactiveAndOtherPoi),
            ("stale, future and mismatched move results are not current evidence", StaleEvidence),
            ("arrival, recent motion and successful movement prevent backoff", MotionControls),
            ("non-finite coordinates never create endpoint authority", NonFinite)
        });
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS vendor travel: " + test.Name); }
            catch (Exception error)
            {
                while (error is TargetInvocationException wrapped && wrapped.InnerException != null) error = wrapped.InnerException;
                failures.Add(test.Name + ": " + error.Message);
                Console.Error.WriteLine("FAIL vendor travel: " + failures[^1]);
            }
        }
        Console.WriteLine($"Vendor travel scenarios: {cases.Count - failures.Count}/{cases.Count}; actual policy/selection plus structural Pulse guard, no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static object Travel => typeof(VendorSafetyPolicy).GetProperty("Travel")?.GetValue(null)
        ?? throw new InvalidOperationException("Shared temporary vendor travel policy is missing.");
    private static object Sample(params (string Name, object Value)[] changes)
    {
        var type = typeof(VendorSafetyPolicy).Assembly.GetType("Styx.Logic.Profiles.VendorTravelObservation")
            ?? throw new InvalidOperationException("The current service-travel observation contract is missing.");
        var sample = Activator.CreateInstance(type)!;
        var values = new Dictionary<string, object>
        {
            ["NowUtc"] = Now, ["LastMoveAttemptUtc"] = Now, ["StationaryFor"] = TimeSpan.FromSeconds(31),
            ["MapId"] = (uint)0, ["Entry"] = Entry, ["Type"] = PoiType.Repair,
            ["PlayerLocation"] = new WoWPoint(1, 1, 1), ["Destination"] = Destination,
            ["LastMoveDestination"] = Destination, ["LastMoveResult"] = MoveResult.Failed, ["IsActiveWorld"] = true
        };
        foreach (var change in changes) values[change.Name] = change.Value;
        foreach (var value in values) type.GetProperty(value.Key)!.SetValue(sample, value.Value);
        return sample;
    }
    private static bool Defer(Guid owner, object sample) => (bool)Travel.GetType().GetMethod("TryDefer")!.Invoke(Travel, new[] { (object)owner, sample })!;
    private static bool Deferred(uint map = 0, int entry = Entry, WoWPoint? destination = null, DateTime? now = null) =>
        (bool)Travel.GetType().GetMethod("IsDeferred")!.Invoke(Travel, new object[] { map, entry, destination ?? Destination, now ?? Now })!;
    private static bool Expire(Guid owner, DateTime now) => (bool)Travel.GetType().GetMethod("ReleaseExpired")!.Invoke(Travel, new object[] { owner, now })!;
    private static void Reset(Guid owner) => Travel.GetType().GetMethod("Reset")!.Invoke(Travel, new object[] { owner });
    private static void WithOwner(Action<Guid> action)
    {
        Guid owner = Guid.NewGuid();
        try { action(owner); } finally { if (typeof(VendorSafetyPolicy).GetProperty("Travel") != null) Reset(owner); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void Defers() => WithOwner(owner =>
    {
        Check(Defer(owner, Sample()) && Deferred(now: Now.AddSeconds(119.999)), "fresh stationary failed travel must defer this endpoint");
        Check(!Deferred(now: Now.AddSeconds(120)), "two minutes is a retry deadline, not a permanent exclusion");
        Check(!VendorSafetyPolicy.IsRejected(Entry), "travel failure must not call permanent/session Reject");
    });
    private static void Duplicate() => WithOwner(owner =>
    {
        Defer(owner, Sample());
        Check(!Defer(owner, Sample(("NowUtc", Now.AddSeconds(60)), ("LastMoveAttemptUtc", Now.AddSeconds(60)))), "duplicate should not acquire another lease");
        Check(!Deferred(now: Now.AddSeconds(120)), "duplicate extended original deadline");
    });
    private static void Identity() => WithOwner(owner =>
    {
        Defer(owner, Sample());
        Check(Deferred() && !Deferred(map: 1) && !Deferred(entry: Entry + 1), "cache identity crossed map or entry");
        Check(!Deferred(destination: new WoWPoint(100, 100, 34)) && !Deferred(destination: new WoWPoint(110, 100, 30)), "another floor or endpoint inherited failure");
    });
    private static void ExpiryAndReset() => WithOwner(owner =>
    {
        Defer(owner, Sample());
        Check(!Expire(owner, Now.AddSeconds(119)) && Expire(owner, Now.AddSeconds(120)) && !Expire(owner, Now.AddSeconds(120)), "expiry must request exactly one refresh");
        Check(Defer(owner, Sample()), "expired record blocked a fresh attempt"); Reset(owner);
        Check(!Deferred(), "lifecycle reset retained old temporary state");
    });
    private static void OwnerIsolation() => WithOwner(a => WithOwner(b =>
    {
        Defer(a, Sample()); Defer(b, Sample(("Entry", Entry + 1))); Reset(a);
        Check(!Deferred() && Deferred(entry: Entry + 1), "one owner cleared another's unrelated retry");
    }));
    private static void Bounded() => WithOwner(owner =>
    {
        for (int i = 0; i < 600; i++) Defer(owner, Sample(("Entry", Entry + i)));
        int count = (int)Travel.GetType().GetProperty("Count")!.GetValue(Travel)!;
        Check(count <= 128, "one owner grew an unbounded process-wide blacklist");
    });
    private static void ClockRollback() => WithOwner(owner =>
    {
        Defer(owner, Sample()); Check(!Deferred(now: Now.AddSeconds(-1)), "a future observation authorized backoff");
        Check(Expire(owner, Now.AddSeconds(-1)), "clock rollback must release stale temporal authority");
    });
    private static void RejectSample(string name, object value) => WithOwner(owner => Check(!Defer(owner, Sample((name, value))) && !Deferred(), "excluded activity created travel failure"));
    private static void InactiveAndOtherPoi()
    {
        RejectSample("IsActiveWorld", false); RejectSample("Type", PoiType.Quest); RejectSample("Entry", 0);
        Check(!Defer(Guid.Empty, Sample()), "an absent owner acquired authority");
    }
    private static void StaleEvidence()
    {
        RejectSample("LastMoveAttemptUtc", Now.AddSeconds(-4)); RejectSample("LastMoveAttemptUtc", Now.AddSeconds(1));
        RejectSample("LastMoveDestination", new WoWPoint(100, 100, 40));
    }
    private static void MotionControls()
    {
        RejectSample("PlayerLocation", Destination); RejectSample("StationaryFor", TimeSpan.FromSeconds(29));
        RejectSample("LastMoveResult", MoveResult.Moved); RejectSample("LastMoveResult", MoveResult.ReachedDestination);
    }
    private static void NonFinite()
    {
        foreach (string field in new[] { "PlayerLocation", "Destination", "LastMoveDestination" })
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                RejectSample(field, new WoWPoint(bad, 100, 30));
    }
    private static void Manual() => WithOwner(owner =>
    {
        const int manual = 991699; VendorSafetyPolicy.Reject(manual);
        Defer(owner, Sample(("Entry", manual))); Reset(owner);
        Check(VendorSafetyPolicy.IsRejected(manual), "temporary reset erased explicit exclusion");
    });
    private static void CoreSelection() => WithOwner(owner =>
    {
        var original = ObjectManager.Me;
        try
        {
            ObjectManager.Me = new LocalPlayer(0);
            DateTime now = DateTime.UtcNow;
            Defer(owner, Sample(("NowUtc", now), ("LastMoveAttemptUtc", now)));
            var manager = new VendorManager();
            var blocked = new Vendor(Entry, "Deferred", Vendor.VendorType.Repair, Destination);
            var available = new Vendor(Entry + 1, "Available", Vendor.VendorType.Repair, new WoWPoint(110, 100, 30));
            manager.AllVendors.AddRange(new[] { blocked, available });
            Check(manager.IsBlacklisted(blocked) && !manager.IsBlacklisted(available), "core fallback exclusion predicate ignored endpoint backoff");
            Check(manager.GetEligibleVendors(Vendor.VendorType.Repair, WoWClass.None).Single() == available, "profile selection bypassed temporary exclusions");
        }
        finally { ObjectManager.Me = original; }
    });
    private static void WholesomeSelection() => WithOwner(owner =>
    {
        DateTime now = DateTime.UtcNow;
        Defer(owner, Sample(("NowUtc", now), ("LastMoveAttemptUtc", now)));
        var db = new VendorDatabase();
        db.Vendors.AddRange(new[]
        {
            new VendorEntry { Map=0, Entry=Entry, Type="Repair", X=100, Y=100, Z=30 },
            new VendorEntry { Map=0, Entry=Entry+1, Type="Repair", X=110, Y=100, Z=30 },
            new VendorEntry { Map=0, Entry=Entry+2, Type="Repair", X=120, Y=100, Z=30 }
        });
        var loader = new VendorDataLoader();
        typeof(VendorDataLoader).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(loader, db);
        var select = typeof(VendorDataLoader).GetMethod("SelectNearestVendors", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Snapshot-based vendor selector is missing.");
        var result = (List<VendorEntry>)select.Invoke(loader, new object[] { (uint)0, WoWClass.None, Destination, "Repair", 1, new HashSet<int>() })!;
        Check(result.Count == 1 && result[0].Entry == Entry + 1, "Take(count) ran before temporary filtering, hiding the available candidate");
    });
    private static void ManualUnchanged() => WithOwner(owner =>
    {
        var manual = new HashSet<int> { Entry + 1 };
        DateTime now = DateTime.UtcNow;
        Defer(owner, Sample(("NowUtc", now), ("LastMoveAttemptUtc", now)));
        var db = new VendorDatabase();
        for (int i = 0; i < 3; i++)
            db.Vendors.Add(new VendorEntry { Map=0, Entry=Entry+i, Type="Repair", X=100+10*i, Y=100, Z=30 });
        var loader = new VendorDataLoader();
        typeof(VendorDataLoader).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(loader, db);
        var select = typeof(VendorDataLoader).GetMethod("SelectNearestVendors", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Snapshot-based vendor selector is missing.");
        var result = (List<VendorEntry>)select.Invoke(loader, new object[] { (uint)0, WoWClass.None, Destination, "Repair", 1, manual })!;
        Check(result.Single().Entry == Entry + 2 && manual.SetEquals(new[] { Entry + 1 }) && !VendorSafetyPolicy.IsRejected(Entry),
            "temporary and manual filters must both apply without persisting temporary state");
    });

    // Structural regression, not a claim of unattached client Pulse execution.
    private static void NoPersistenceFromPulse()
    {
        MethodInfo pulse = typeof(WholesomeAutoQuest).GetMethod("Pulse")!;
        Check(!Calls(pulse).Any(method => method.DeclaringType == typeof(WholesomeAutoQuest) && method.Name == "SaveVendorBlacklist"),
            "Pulse contains a direct timeout-driven SaveVendorBlacklist call; idle service travel must not persist a ban");
    }
    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).GroupBy(code => unchecked((ushort)code.Value)).ToDictionary(group => group.Key, group => group.First());
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            ushort value = il[offset++]; if (value == 0xfe) value = (ushort)(0xfe00 | il[offset++]);
            OpCode op = codes[value];
            if (op.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, offset))!;
            offset += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
        }
    }
}
