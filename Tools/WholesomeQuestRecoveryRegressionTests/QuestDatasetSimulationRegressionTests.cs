using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Bots.Quest.Actions;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;
using DataObjective = WholesomeAQ.QuestObjective;
using DataKind = WholesomeAQ.ObjectiveType;
using DataType = WholesomeAQ.QuestObjectType;

// Explicitly invoked dataset suite. Input/output paths must be supplied; it is
// not a module initializer and does not silently run a different fixture dataset.
internal static class QuestDatasetSimulationRegressionTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 8, 28, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private sealed class Failure(string message) : Exception(message) { }
    private sealed class NotApplicable(string message) : Exception(message) { }
    private sealed class Record
    {
        public int quest_id { get; set; }
        public string dataset_sha256 { get; set; } = "";
        public string status { get; set; } = "RUN";
        public string pipeline_status { get; set; } = "NOT-RUN";
        public string pipeline_limit { get; set; } = "Controlled authoritative observations; no live quest, native interaction, combat, path travel or realm completion is claimed.";
        public List<object> cases { get; } = new();
        public int passed_cases { get; set; }
        public int failed_cases { get; set; }
        public int not_applicable_cases { get; set; }
        public List<string> production_owners { get; } = new();
        public List<string> pipeline_blocks { get; } = new();
        public List<string> profile_sha256 { get; } = new();
        public object? observations { get; set; }
        public string[] shared_scenario_groups { get; } = new[] { "QuestRootPreemptionRegressionTests", "QuestRootProtectionRegressionTests", "QuestRootOwnerBoundaryRegressionTests", "QuestObjectiveRestartRegressionTests", "QuestLiveGiverGeometryRegressionTests", "QuestEligibilityRequirementRegressionTests" };
        public string structural_classification { get; set; } = "";
    }

    private sealed class Context
    {
        internal QuestEntry Quest = null!;
        internal QuestDatabase Database = null!;
        internal uint[] Completed = Array.Empty<uint>();
        internal QuestSchedulerAcceptedQuest[] ActiveParents = Array.Empty<QuestSchedulerAcceptedQuest>();
        internal SpawnPoint Origin = new() { Map = 530, X = 0, Y = 0, Z = 0 };
        internal int Level, Race;
        internal int[] NormalIds = new int[4], NormalCounts = new int[4], ItemIds = new int[6], ItemCounts = new int[6];

        internal QuestScheduleResult Schedule(int? level = null, int? race = null, bool accepted = false, bool failed = false,
            bool complete = false, bool authority = true, bool logComplete = true, bool logFull = false, bool rewarded = false,
            int[]? progress = null, Dictionary<int, long>? items = null, SpawnPoint? origin = null,
            QuestRecoveryState state = QuestRecoveryState.Eligible, bool mayAttempt = true, bool? safe = true, bool? reachable = true,
            Action<string>? log = null, bool cancelNavigation = false, DateTime? now = null, bool metadataKnown = true,
            HashSet<string>? assessed = null, int classId = 2, uint[]? history = null,
            QuestSchedulerAcceptedQuest[]? activeParents = null)
        {
            var observations = new List<QuestSchedulerAcceptedQuest>(activeParents ?? ActiveParents);
            if (accepted) observations.Add(new QuestSchedulerAcceptedQuest
            {
                QuestId = (uint)Quest.Id, IsFailed = failed, IsCompleted = complete, ObjectiveCounts = progress ?? new int[4],
                NormalObjectiveIds = metadataKnown ? NormalIds : Array.Empty<int>(),
                NormalObjectiveRequiredCounts = metadataKnown ? NormalCounts : Array.Empty<int>()
            });
            if (logFull)
                for (uint index = 0; observations.Count < 25; index++) observations.Add(new QuestSchedulerAcceptedQuest { QuestId = 6000000 + index });
            var point = origin ?? Origin;
            return QuestScheduler.MaterializeSchedule(Database, new QuestSchedulerSnapshot
            {
                UtcNow = now ?? Now, PlayerLevel = level ?? Level, PlayerRaceId = race ?? Race, PlayerClassId = classId,
                PlayerGuid = 123, MapId = point.Map, X = point.X, Y = point.Y, Z = point.Z,
                AcceptedQuests = observations, CompletedQuestIds = rewarded ? (history ?? Completed).Concat(new[] { (uint)Quest.Id }).ToArray() : history ?? Completed,
                HasAuthoritativeCompletions = authority, HasCompleteQuestLog = logComplete,
                CarriedItemCounts = items ?? ItemIds.Where(id => id > 0).Distinct().ToDictionary(id => id, _ => 0L)
            }, key => new QuestRecoveryDecision { State = state, MayAttempt = mayAttempt,
                RetryUtc = mayAttempt ? null : Now.AddMinutes(5), Status = "controlled " + state },
                50, 250, 80, log: log, navigationAssessment: point =>
                {
                    if (cancelNavigation) throw new OperationCanceledException("controlled stop/cancellation");
                    assessed?.Add(PointKey(point));
                    return new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = reachable };
                });
        }
        internal bool Pickup(QuestScheduleResult result) => result.Plan.Any(value => value.Quest.Id == Quest.Id && value.Stage == QuestWorkStage.Pickup);
        internal bool AnyWork(QuestScheduleResult result) => result.Plan.Any(value => value.Quest.Id == Quest.Id);
        internal IEnumerable<SpawnPoint> Spawns(int entry, DataType type) =>
            (type == DataType.Creature ? Database.CreatureSpawns : Database.GameObjectSpawns).TryGetValue(entry.ToString(), out var points)
                ? points.Where(Valid) : Enumerable.Empty<SpawnPoint>();
    }

    internal static void Run()
    {
        string dataset = Environment.GetEnvironmentVariable("CB_QUEST_SIM_DATASET") ?? throw new InvalidOperationException("CB_QUEST_SIM_DATASET required");
        string observationsPath = Environment.GetEnvironmentVariable("CB_QUEST_SIM_OBSERVATIONS") ?? throw new InvalidOperationException("CB_QUEST_SIM_OBSERVATIONS required");
        string output = Environment.GetEnvironmentVariable("CB_QUEST_SIM_OUTPUT") ?? throw new InvalidOperationException("CB_QUEST_SIM_OUTPUT required");
        int limit = int.TryParse(Environment.GetEnvironmentVariable("CB_QUEST_SIM_LIMIT"), out int size) ? size : int.MaxValue;
        if (File.Exists(output)) throw new IOException("Simulation evidence is create-only");
        byte[] bytes = File.ReadAllBytes(dataset); string hash = Hash(bytes);
        var db = JsonSerializer.Deserialize<QuestDatabase>(bytes, Json) ?? throw new InvalidDataException("Empty dataset");
        var observations = File.ReadLines(observationsPath).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToDictionary(row => row.GetProperty("quest_id").GetInt32());
        if (observations.Count != db.Quests.Count || db.Quests.Any(quest => !observations.ContainsKey(quest.Id)) ||
            observations.Values.Any(value => value.GetProperty("dataset_sha256").GetString() != hash))
            throw new InvalidDataException("Observations are not bound one-to-one to this exact dataset");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        using var fixture = new QuestDatasetObservationFixture();
        int rows = 0, passed = 0, failed = 0, pipelines = 0;
        var selected = db.Quests.OrderBy(quest => quest.Id).Take(limit).ToArray();
        foreach (QuestEntry quest in selected)
        {
            JsonElement evidence = observations[quest.Id];
            var record = new Record { quest_id = quest.Id, dataset_sha256 = hash,
                structural_classification = evidence.GetProperty("structural_classification").GetString()! };
            try
            {
                var context = BuildContext(db, quest, evidence);
                record.observations = new { level = context.Level, race_id = context.Race, class_id = 2,
                    origin = context.Origin, authoritative_completed_prerequisites = context.Completed,
                    active_parent_ids = context.ActiveParents.Select(value => value.QuestId).ToArray(),
                    reference = evidence, normal_slot_encoding = "original-client high-bit GO identities; four physical counters" };
                record.production_owners.Add("QuestScheduler.MaterializeSchedule");
                bool baseline = context.Pickup(context.Schedule());
                Case(record, "repeat-same-observations-stable-plan", () => Check(Signature(context.Schedule()) == Signature(context.Schedule()), "same observations changed the selected plan"));
                Case(record, "incomplete-log-withholds-work", () => Check(!context.AnyWork(context.Schedule(logComplete: false)), "incomplete log published work"));
                Case(record, "unknown-history-withholds-new-pickup", () => Check(!context.Pickup(context.Schedule(authority: false)), "unknown history authorized pickup"));
                Case(record, "full-25-slot-log-withholds-new-pickup", () => Check(!context.Pickup(context.Schedule(logFull: true)), "full quest log authorized pickup"));
                Case(record, "rewarded-quest-not-rescheduled", () => Check(!context.AnyWork(context.Schedule(rewarded: true)), "rewarded quest was republished"));
                Case(record, "accepted-failed-quest-no-work", () => Check(!context.AnyWork(context.Schedule(accepted: true, failed: true)), "failed quest was scheduled"));
                Case(record, "failed-and-completed-flags-no-work", () => Check(!context.AnyWork(context.Schedule(accepted: true, failed: true, complete: true)), "failed quest reached turn-in"));
                if (quest.MinLevel > 1)
                    Case(record, "below-MinLevel=" + (quest.MinLevel - 1), () => Check(!context.Pickup(context.Schedule(level: quest.MinLevel - 1)), "below-minimum pickup"));
                foreach (int level in new[] { quest.MinLevel, quest.MinLevel + 1, quest.QuestLevel - 1, quest.QuestLevel, quest.QuestLevel + 1, 60 }
                    .Where(value => value >= 1 && value <= 80).Distinct())
                    Case(record, "level-boundary=" + level, () =>
                    {
                        bool actual = context.Pickup(context.Schedule(level: level));
                        if (level < quest.MinLevel) Check(!actual, "minimum-level barrier failed");
                        else if (baseline && !(quest.MaxLevel > 0 && level > quest.MaxLevel)) Check(actual, "QuestLevel or another level boundary changed eligible pickup");
                    });
                foreach (int race in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 })
                    Case(record, "race=" + race, () =>
                    {
                        bool allowed = quest.AllowableRaces == 0 || quest.AllowableRaces == -1 || (quest.AllowableRaces & (1 << (race - 1))) != 0;
                        bool actual = context.Pickup(context.Schedule(race: race));
                        Check(!actual || allowed, "disallowed race admitted");
                        if (baseline && allowed) Check(actual, "allowed race lost baseline pickup");
                    });
                foreach (var state in new[] { QuestRecoveryState.CoolingDown, QuestRecoveryState.Quarantined })
                    Case(record, "recovery-block=" + state, () => Check(!context.AnyWork(context.Schedule(state: state, mayAttempt: false)), "recovery block bypassed"));
                Case(record, "recovery-half-open-single-probe-readmission", () =>
                {
                    var ordinary = context.Schedule();
                    var probe = context.Schedule(state: QuestRecoveryState.HalfOpen);
                    Check(probe.Selected.Count <= 1, "half-open exceeded the one-probe gate");
                    if (ordinary.Selected.Count > 0)
                    {
                        Check(probe.Selected.Count == 1, "eligible work lost every half-open probe");
                        Check(ordinary.Selected.Any(value => value.QuestId == probe.Selected[0].QuestId), "probe selected an ineligible quest");
                    }
                    // A negative-PrevQuestID child can compete with its active
                    // parent. The existing one-probe policy selects only one.
                });
                foreach (int classId in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11 })
                    Case(record, "declared-class-boundary=" + classId, () =>
                    {
                        bool allowed = !quest.AllowableClasses.HasValue || quest.AllowableClasses == 0 ||
                            (quest.AllowableClasses.Value & (1 << (classId - 1))) != 0;
                        bool actual = context.Pickup(context.Schedule(classId: classId));
                        Check(!actual || allowed, "explicit class restriction was bypassed");
                        if (!quest.AllowableClasses.HasValue) Check(actual == baseline, "absent class metadata invented a restriction");
                    });
                if (quest.PrevQuestID > 0)
                    Case(record, "positive-prerequisite-absent-withholds-pickup", () =>
                        Check(!context.Pickup(context.Schedule(history: Array.Empty<uint>())), "missing rewarded prerequisite admitted pickup"));
                if (quest.PrevQuestID < 0)
                    foreach (bool failedParent in new[] { false, true })
                        Case(record, "active-parent-unavailable:failed=" + failedParent, () =>
                        {
                            var unavailable = failedParent ? context.ActiveParents.Select(parent => new QuestSchedulerAcceptedQuest
                                { QuestId = parent.QuestId, IsFailed = true }).ToArray() : Array.Empty<QuestSchedulerAcceptedQuest>();
                            Check(!context.Pickup(context.Schedule(activeParents: unavailable)), "missing/failed active parent admitted pickup");
                        });
                if (quest.StartItem > 0)
                    Case(record, "provided-StartItem-presence-does-not-invent-pickup-recipe", () =>
                        Check(context.Pickup(context.Schedule(items: new Dictionary<int, long> { [quest.StartItem] = 1 })) == baseline,
                            "a provided-on-acceptance item changed pickup policy"));
                Case(record, "explicitly-unreachable-destinations-never-selected", () =>
                {
                    var assessed = new HashSet<string>();
                    var result = context.Schedule(reachable: false, assessed: assessed);
                    Check(result.Plan.SelectMany(value => value.Hotspots).All(point => !assessed.Contains(PointKey(point))),
                        "an explicitly unreachable destination was selected; unprobed neighbors remain unknown");
                });
                Case(record, "explicitly-unsafe-destinations-never-selected", () =>
                {
                    var assessed = new HashSet<string>();
                    var result = context.Schedule(safe: false, assessed: assessed);
                    Check(result.Plan.SelectMany(value => value.Hotspots).All(point => !assessed.Contains(PointKey(point))),
                        "an explicitly unsafe destination was selected; unprobed neighbors remain unknown");
                });
                Case(record, "unknown-navigation-does-not-invent-confirmed-safety", () =>
                    Check(context.Schedule(safe: null, reachable: null).Plan.Where(value => value.Quest.Id == quest.Id)
                        .SelectMany(value => value.Hotspots).All(point => point.IsKnownSafe != true && point.IsKnownReachable != true), "unknown navigation was upgraded to proven"));
                Case(record, "different-map-no-pickup", () => Check(!context.Pickup(context.Schedule(origin: new SpawnPoint { Map = int.MaxValue })), "wrong-map endpoint selected"));
                Case(record, "changed-time-same-observations-stable", () => Check(Signature(context.Schedule(now: Now.AddMinutes(1))) == Signature(context.Schedule()), "timestamp alone changed work"));
                Case(record, "stop-cancellation-propagates-at-navigation", () =>
                {
                    if (!baseline) throw new NotApplicable("no admissible route reaches navigation");
                    try { context.Schedule(cancelNavigation: true); }
                    catch (OperationCanceledException) { return; }
                    throw new Failure("cancellation was swallowed");
                });
                foreach (var point in db.QuestGivers.Where(giver => giver.QuestId == quest.Id)
                    .SelectMany(giver => context.Spawns(giver.GiverId, giver.GiverType))
                    .GroupBy(point => point.Map).SelectMany(group => new[] { group.First(), group.OrderBy(value => value.Z).First(), group.OrderByDescending(value => value.Z).First() }).Distinct())
                    Case(record, $"giver-map-Z={point.Map}:{point.Z:R}", () =>
                        Check(context.Schedule(origin: point).Plan.Where(value => value.Quest.Id == quest.Id)
                            .SelectMany(value => value.Hotspots).All(value => value.Map == point.Map), "a selected giver moved to another map"));
                if (!evidence.GetProperty("reference_found").GetBoolean()) record.pipeline_blocks.Add("secondary-reference-cache-unavailable");
                else Pipeline(context, record, fixture);
                if (record.pipeline_status == "NOT-RUN") record.pipeline_status = "BLOCKED";
            }
            catch (Exception error)
            {
                record.failed_cases++; record.cases.Add(new { name = "dataset-row-exception", status = "FAIL", error = error.ToString() });
                record.pipeline_status = "FAIL";
            }
            fixture.ReleaseOwners();
            stream.WriteLine(JsonSerializer.Serialize(record)); stream.Flush();
            rows++; passed += record.passed_cases; failed += record.failed_cases; if (record.pipeline_status == "PASS") pipelines++;
            if (record.failed_cases != 0 && failed < 40) Console.Error.WriteLine($"FAIL dataset quest {quest.Id}: {record.failed_cases} cases; blocks={string.Join(",", record.pipeline_blocks)}");
            if (rows % 100 == 0 || rows == selected.Length) Console.WriteLine($"Dataset progress: rows={rows}/{selected.Length}; passed-cases={passed}; failed-cases={failed}; pipeline-passes={pipelines}");
        }
        File.WriteAllText(output + ".summary.json", JsonSerializer.Serialize(new { dataset_sha256 = hash, quest_count = rows,
            dataset_total = db.Quests.Count, passed_cases = passed, failed_cases = failed, pipeline_passes = pipelines,
            simulation_scope = "actual production owners with controlled observations; no game attached",
            observation_file_sha256 = Hash(File.ReadAllBytes(observationsPath)),
            host_binary_sha256 = Hash(File.ReadAllBytes(typeof(StyxWoW).Assembly.Location)) }, new JsonSerializerOptions { WriteIndented = true }));
        if (failed != 0) throw new InvalidOperationException($"Dataset simulation has {failed} failed cases across {rows} rows; retained {output}");
    }

    private static Context BuildContext(QuestDatabase original, QuestEntry quest, JsonElement observations)
    {
        var byId = original.Quests.ToDictionary(value => value.Id);
        var closure = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(quest.Id);
        while (queue.Count != 0)
        {
            int id = queue.Dequeue(); if (!closure.Add(id) || !byId.TryGetValue(id, out var value)) continue;
            foreach (int parent in new[] { Math.Abs(value.PrevQuestID) }.Concat(value.PreviousQuestsIds ?? new()).Where(value => value > 0))
            {
                queue.Enqueue(parent);
                if (byId.TryGetValue(parent, out var prior) && prior.ExclusiveGroup < 0)
                    foreach (var member in original.Quests.Where(item => item.ExclusiveGroup == prior.ExclusiveGroup)) queue.Enqueue(member.Id);
            }
        }
        var db = new QuestDatabase { Quests = closure.Where(byId.ContainsKey).Select(id => byId[id]).ToList(),
            QuestGivers = original.QuestGivers.Where(value => value.QuestId == quest.Id).ToList(),
            QuestEnders = original.QuestEnders.Where(value => value.QuestId == quest.Id).ToList(),
            CreatureSpawns = original.CreatureSpawns, GameObjectSpawns = original.GameObjectSpawns };
        int active = quest.PrevQuestID < 0 ? -quest.PrevQuestID : 0;
        var context = new Context { Quest = quest, Database = db, Level = Math.Clamp(quest.MinLevel, 1, 80),
            Race = new[] { 10, 1, 2, 3, 4, 5, 6, 7, 8, 11 }.FirstOrDefault(race => quest.AllowableRaces == 0 || quest.AllowableRaces == -1 || (quest.AllowableRaces & (1 << (race - 1))) != 0),
            Completed = closure.Where(id => id != quest.Id && id != active).Select(id => (uint)id).ToArray(),
            ActiveParents = active > 0 ? new[] { new QuestSchedulerAcceptedQuest { QuestId = (uint)active,
                ObjectiveCounts = new int[4], NormalObjectiveIds = new int[4], NormalObjectiveRequiredCounts = new int[4] } } : Array.Empty<QuestSchedulerAcceptedQuest>() };
        context.Origin = db.QuestGivers.SelectMany(giver => context.Spawns(giver.GiverId, giver.GiverType)).FirstOrDefault() ?? context.Origin;
        if (observations.GetProperty("reference_found").GetBoolean())
        {
            int[] Array(string name) => observations.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();
            context.NormalIds = Array("normal_ids"); context.NormalCounts = Array("normal_counts");
            context.ItemIds = Array("item_ids"); context.ItemCounts = Array("item_counts");
        }
        return context;
    }

    private static void Pipeline(Context context, Record record, QuestDatasetObservationFixture fixture)
    {
        var quest = context.Quest; uint id = (uint)quest.Id;
        var pickup = context.Schedule();
        var pickupPlan = pickup.Plan.Where(value => value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.Pickup).ToArray();
        if (pickupPlan.Length == 0) { record.pipeline_blocks.Add("scheduler-pickup-not-admitted:" + pickup.Status); return; }
        fixture.SetQuest(id, quest.Name, context.Level, context.NormalIds, context.NormalCounts, context.ItemIds, context.ItemCounts);
        fixture.SetAccepted(false); fixture.SetHistory(context.Completed);
        var builder = new ProfileBuilder();
        string Xml(IReadOnlyList<QuestPlanEntry> plan)
        {
            string xml = builder.BuildProfileXml(plan, context.Database, "Controlled dataset observation", "Fixture", context.Level);
            record.profile_sha256.Add(Hash(System.Text.Encoding.UTF8.GetBytes(xml))); fixture.LoadProfile(xml); return xml;
        }
        string pickupXml = Xml(pickupPlan);
        Case(record, "pipeline-generated-pickup-profile", () => Check(XDocument.Parse(pickupXml).Descendants("PickUp").Any(), "no pickup node in generated profile"));
        var giver = pickupPlan[0].Giver;
        var point = pickupPlan[0].Hotspots[0];
        // The root's actual combat/death/rest/service observations are covered by
        // the shared root groups. Here each generated quest runs the real executor
        // across revocation/restart without native dispatch or profile advancement.
        foreach (string interruption in new[] { "stop", "death", "combat", "rest", "vendor", "observation-changed" })
            Case(record, "pipeline-executor-revocation=" + interruption, () =>
            {
                var nodes = OrderNodeCollection.FromXml(XDocument.Parse(pickupXml).Descendants("QuestOrder").Single());
                var order = new QuestOrder(nodes);
                int before = order.Nodes.Count;
                var executor = new ForcedBehaviorExecutor(order, () => false);
                executor.Start(null);
                Check(executor.Tick(null) == TreeSharp.RunStatus.Failure && order.Nodes.Count == before && order.CurrentBehavior == null,
                    "revoked executor created an effect or advanced its generated profile");
                executor.Stop(null);
                executor.Start(null);
                Check(executor.Tick(null) == TreeSharp.RunStatus.Failure && order.Nodes.Count == before, "restart reused revoked work");
                executor.Stop(null);
            });
        record.production_owners.Add("ForcedBehaviorExecutor revocation and stop/restart on each generated pickup profile; root causes tested in shared root groups");
        var pickupOwner = new ForcedQuestPickUp(id, quest.Name, (uint)giver.GiverId, giver.GiverName,
            new WoWPoint(point.X, point.Y, point.Z), giver.GiverType == DataType.Creature ? Styx.Logic.Profiles.Quest.QuestObjectType.Npc : Styx.Logic.Profiles.Quest.QuestObjectType.GameObject);
        record.production_owners.Add("ProfileBuilder.BuildProfileXml -> Profile constructor -> ForcedQuestPickUp.IsDone");
        Case(record, "pipeline-pickup-awaits-acceptance", () => Check(!pickupOwner.IsDone && !pickupOwner.IsExecutionDeferred, "pickup acknowledged before server acceptance"));
        fixture.SetAccepted(true);
        Case(record, "pipeline-pickup-acknowledges-accepted-log", () => Check(pickupOwner.IsDone, "accepted quest did not finish pickup"));
        var completedCounts = new int[4]; var carried = context.ItemIds.Where(value => value > 0).Distinct().ToDictionary(value => value, _ => 0L);
        int startingFailures = record.failed_cases;
        foreach (DataObjective objective in quest.Objectives.Where(value => value.Type != DataKind.TurnInOnly))
        {
            var targetType = objective.Type == DataKind.CollectFromGameObject ? DataType.GameObject : DataType.Creature;
            int entry = objective.Type == DataKind.CollectFromGameObject ? objective.GameObjectId : objective.MobId;
            SpawnPoint? endpoint = context.Spawns(entry, targetType).FirstOrDefault();
            if (endpoint == null) { record.pipeline_blocks.Add("objective-static-spawn-missing:index=" + objective.Index); continue; }
            var active = context.Schedule(accepted: true, progress: completedCounts, items: carried, origin: endpoint);
            var plan = active.Plan.Where(value => value.Quest.Id == quest.Id && value.ObjectiveIndex == objective.Index &&
                (value.Stage == QuestWorkStage.Objective || value.Stage == QuestWorkStage.AncestorCorrection)).ToArray();
            if (plan.Length == 0)
            {
                record.pipeline_blocks.Add("objective-not-materialized:index=" + objective.Index + ";" + active.Status); continue;
            }
            string xml = Xml(plan);
            var element = XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").FirstOrDefault();
            if (element == null) { record.pipeline_blocks.Add("objective-behavior-not-generated:index=" + objective.Index); continue; }
            bool item = objective.Type == DataKind.CollectItem || (objective.Type == DataKind.CollectFromGameObject && objective.ItemId > 0);
            int required = objective.Type == DataKind.KillMob ? objective.KillCount : objective.CollectCount;
            int rawId = targetType == DataType.GameObject ? unchecked((int)0x80000000) | entry : entry;
            int[] slots = Enumerable.Range(0, 4).Where(index => context.NormalIds[index] == rawId && context.NormalCounts[index] == required).ToArray();
            if ((!item && slots.Length != 1) || (item && !Enumerable.Range(0, 6).Any(index => context.ItemIds[index] == objective.ItemId && context.ItemCounts[index] == required)))
            { record.pipeline_blocks.Add("runtime-objective-identity-not-matched:index=" + objective.Index); continue; }
            fixture.SetAccepted(true); fixture.SetProgress(completedCounts); fixture.SetInventory(carried);
            var owner = fixture.CreateObjective(ObjectiveNode.FromXml(element));
            record.production_owners.Add("ForcedBehaviorExecutor.ResolveQuestObjectiveIndex -> QuestManager.CreateQuestObjective -> ForcedQuestObjective.IsDone:" + objective.Type);
            void Progress(int count)
            {
                if (item) carried[objective.ItemId] = count; else completedCounts[slots[0]] = count;
                fixture.SetProgress(completedCounts); fixture.SetInventory(carried);
            }
            Case(record, "pipeline-objective-zero:index=" + objective.Index, () => { Progress(0); Check(!owner.IsDone, "zero progress acknowledged"); });
            Case(record, "pipeline-objective-partial:index=" + objective.Index, () => { Progress(Math.Max(0, required - 1)); Check(!owner.IsDone, "partial progress acknowledged"); });
            Case(record, "pipeline-objective-complete:index=" + objective.Index, () => { Progress(required); Check(owner.IsDone, "actual generic owner failed to acknowledge matched progress"); });
            Case(record, "pipeline-scheduler-acknowledges-objective:index=" + objective.Index, () =>
                Check(!context.Schedule(accepted: true, progress: completedCounts, items: carried, origin: endpoint).Plan.Any(value =>
                    value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.Objective && value.ObjectiveIndex == objective.Index), "scheduler repeated a completed objective"));
            Case(record, "pipeline-observation-regression-resumes-objective:index=" + objective.Index, () =>
            {
                Progress(0); Check(!owner.IsDone, "stale completion survived regressed observations"); Progress(required);
            });
            fixture.ReleaseOwners();
        }
        if (record.pipeline_blocks.Count != 0) return;
        foreach (int index in Enumerable.Range(0, 6).Where(index => context.ItemIds[index] > 0)) carried[context.ItemIds[index]] = context.ItemCounts[index];
        completedCounts = context.NormalCounts.ToArray();
        Case(record, "pipeline-counts-do-not-invent-server-complete-flag", () =>
            Check(!context.Schedule(accepted: true, progress: completedCounts, items: carried).Plan.Any(value => value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.TurnIn), "counts invented server completion"));
        var enderPoint = context.Database.QuestEnders.SelectMany(ender => context.Spawns(ender.EnderId, ender.EnderType)).FirstOrDefault();
        if (enderPoint == null) { record.pipeline_blocks.Add("turn-in-endpoint-unrepresented"); return; }
        var turnIn = context.Schedule(accepted: true, complete: true, progress: completedCounts, items: carried, origin: enderPoint);
        var endPlans = turnIn.Plan.Where(value => value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.TurnIn).ToArray();
        if (endPlans.Length == 0) { record.pipeline_blocks.Add("server-complete-not-scheduled-for-turn-in:" + turnIn.Status); return; }
        string turnInXml = Xml(endPlans);
        Case(record, "pipeline-generated-turn-in-profile", () => Check(XDocument.Parse(turnInXml).Descendants("TurnIn").Any(), "no turn-in node"));
        var ender = endPlans[0].Ender; var endingPoint = endPlans[0].Hotspots[0];
        var endOwner = new ForcedQuestTurnIn(id, quest.Name, (uint)ender.EnderId, ender.EnderName,
            new WoWPoint(endingPoint.X, endingPoint.Y, endingPoint.Z), ender.EnderType == DataType.Creature ? Styx.Logic.Profiles.Quest.QuestObjectType.Npc : Styx.Logic.Profiles.Quest.QuestObjectType.GameObject);
        record.production_owners.Add("ForcedQuestTurnIn.IsDone -> accepted-log removal + authoritative reward history -> next scheduler scan");
        fixture.SetAccepted(true, complete: true);
        Case(record, "pipeline-turn-in-awaits-reward", () => Check(!endOwner.IsDone, "turn-in finished before the accepted quest left the log"));
        fixture.SetAccepted(false); fixture.SetHistory(context.Completed.Concat(new[] { id }));
        Case(record, "pipeline-turn-in-acknowledges-rewarded-removal", () => Check(endOwner.IsDone, "turn-in did not acknowledge removal"));
        Case(record, "pipeline-next-scheduling-retains-rewarded-history", () => Check(!context.AnyWork(context.Schedule(rewarded: true)), "rewarded quest scheduled again"));
        record.pipeline_status = record.failed_cases == startingFailures && startingFailures == 0 ? "PASS" : "FAIL";
    }

    private static void Case(Record record, string name, Action action)
    {
        try { action(); record.passed_cases++; record.cases.Add(new { name, status = "PASS" }); }
        catch (NotApplicable reason) { record.not_applicable_cases++; record.cases.Add(new { name, status = "NOT-APPLICABLE", reason = reason.Message }); }
        catch (Exception error) { record.failed_cases++; record.cases.Add(new { name, status = "FAIL", error = error.ToString() }); }
    }
    private static string Signature(QuestScheduleResult value) => string.Join("|", value.Plan.Select(entry =>
        $"{entry.Quest.Id}:{entry.Stage}:{entry.ObjectiveIndex}:" + string.Join(",", entry.Hotspots.Select(point => $"{point.Map}:{point.X:R}:{point.Y:R}:{point.Z:R}"))));
    private static bool Valid(SpawnPoint point) => point != null && point.Map >= 0 && double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string PointKey(SpawnPoint point) => $"{point.Map}:{point.X:R}:{point.Y:R}:{point.Z:R}";
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
}
