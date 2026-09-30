using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Profiles;
using WholesomeAQ;

// Actual allocated client observations -> scan -> profile load -> execution lease.
// No production reader or decision method is replaced by these tests.
internal static class QuestAvailabilityObservationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action<Probe> Body)>
        {
            ("current raw/history observations publish a real pickup", p =>
            {
                Check(p.Scan(), "satisfied pickup did not publish: " + p.Scheduler.LastStatus);
                Check(p.Scheduler.CaptureExecutionPermission()?.Invoke() == true, "loaded pickup lacked current execution permission");
            }),
            ("reward loss revokes an already-published profile", p =>
            {
                Check(p.Scan(), "initial pickup failed"); var permission = p.Scheduler.CaptureExecutionPermission();
                Check(permission?.Invoke() == true, "initial permission absent"); p.World.SetHistory(Array.Empty<uint>());
                Check(permission!() == false && p.Scheduler.CurrentProfilePath == null, "changed reward kept old profile authorized");
            }),
            ("unknown history revokes an already-published profile", p =>
            {
                Check(p.Scan(), "initial pickup failed"); var permission = p.Scheduler.CaptureExecutionPermission();
                p.World.SetHistory(new uint[] { 1234 }, authoritative: false);
                Check(permission?.Invoke() == false, "unknown history retained execution authority");
            }),
            ("history change during profile load cannot authorize the candidate", p =>
            {
                int loads = 0;
                Check(!p.Scan(path => { loads++; bool accepted = ProfileManager.TryLoadNew(path, false); p.World.SetHistory(Array.Empty<uint>()); return accepted; }),
                    "changed load-time history still published");
                Check(loads == 1 && p.Scheduler.CaptureExecutionPermission() == null, "actual load gate was not exercised or authority survived");
            }),
            ("unmet source condition prevents generated pickup output", p =>
            {
                p.World.SetHistory(Array.Empty<uint>());
                Check(!p.Scan() && !File.Exists(p.Output), "unmet condition wrote executable pickup output");
            }),
            ("raw failed flag dominates a retained complete flag", p =>
            {
                p.World.SetAccepted(true, complete: true, failed: true);
                var raw = QuestAvailabilityPolicy.RawStates(p.World.Player.QuestLog.CaptureSnapshot());
                Check(raw != null && raw.TryGetValue(867, out int value) && value == 5, "failed raw state was reported complete");
            }),
            ("new valid scan can recover after revoked history", p =>
            {
                Check(p.Scan(), "initial pickup failed"); var permission = p.Scheduler.CaptureExecutionPermission();
                p.World.SetHistory(Array.Empty<uint>()); Check(permission?.Invoke() == false, "old pickup was not revoked");
                p.World.SetHistory(new uint[] { 1234 }); Check(p.Scan(), "new valid observation did not recover pickup");
                Check(p.Scheduler.CaptureExecutionPermission()?.Invoke() == true, "replacement pickup lacked its own permission");
            })
        };
        int passed = 0, assertions = 0, errors = 0;
        foreach (var test in tests)
        {
            try { using var probe = new Probe(); test.Body(probe); passed++; Console.WriteLine("PASS availability observations: " + test.Name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL availability observations: " + test.Name + ": " + e.Message); }
            catch (Exception e) { errors++; Console.Error.WriteLine("ERROR availability observations: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Availability observation scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={errors}; actual memory/history/scan/profile/publication; no game.");
        if (assertions + errors != 0) throw new InvalidOperationException("Availability observation regressions");
    }

    private sealed class Probe : IDisposable
    {
        internal readonly QuestDatasetObservationFixture World = new();
        internal readonly QuestScheduler Scheduler;
        internal readonly string Output;
        internal Probe()
        {
            World.SetQuest(867, "Controlled availability", 20, new int[4], new int[4], new int[6], new int[6]);
            World.SetAccepted(false); World.SetHistory(new uint[] { 1234 });
            object baseline = typeof(QuestDatasetObservationFixture).GetField("baseline", Hidden)!.GetValue(World)!;
            var database = (QuestDatabase)baseline.GetType().GetField("Database", Hidden)!.GetValue(baseline)!;
            database.QuestGivers.Add(new QuestGiverEntry { QuestId = 867, GiverId = 77, GiverType = QuestObjectType.Creature, GiverName = "Controlled giver" });
            database.Quests[0].AvailabilityConditions = new QuestAvailabilityContract { QuestId = 867, SourceRef = "controlled://availability/867",
                ReferencedQuests = new[] { new QuestAvailabilityReference { QuestId = 1234, QuestType = 2, SourceRef = "controlled://reference/1234" } },
                Groups = new[] { new QuestAvailabilityGroup { ElseGroup = 0, Conditions = new[] {
                    new QuestAvailabilityPredicate { Type = 8, Value1 = 1234, SourceRef = "controlled://condition/reward" } } } } };
            Output = (string)baseline.GetType().GetProperty("Output", Hidden)!.GetValue(baseline)!;
            var loader = new DataLoader(); typeof(DataLoader).GetField("_database", Hidden)!.SetValue(loader, database);
            Scheduler = new QuestScheduler(loader, new ProfileBuilder(Output), new WholesomeAQSettings());
        }
        internal bool Scan(Func<string,bool>? load = null) => Scheduler.ScanAndRefreshOwned(World.Player, null!,
            apply => { apply(); return true; }, load ?? (path => ProfileManager.TryLoadNew(path, false)), () => true);
        public void Dispose() => World.Dispose();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Failure(message); }
}
