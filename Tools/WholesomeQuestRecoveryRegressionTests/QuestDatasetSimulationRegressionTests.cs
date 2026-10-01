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
        public string repair_sha256 { get; set; } = "absent";
        public string execution_fingerprint { get; set; } = "legacy-direct-model";
        public object? availability_condition_validation { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? required_stock_validation { get; set; }
    }

    private sealed class Context
    {
        internal QuestEntry Quest = null!;
        internal QuestDatabase Database = null!;
        internal uint[] Completed = Array.Empty<uint>();
        internal QuestSchedulerAcceptedQuest[] ActiveParents = Array.Empty<QuestSchedulerAcceptedQuest>();
        internal SpawnPoint Origin = new() { Map = 530, X = 0, Y = 0, Z = 0 };
        internal int Level, Race, ClassId = 2;
        internal int? Area;
        internal uint[]? DailyIds;
        internal uint[]? ConfirmedSpells;
        internal bool EffectiveModel;
        internal IReadOnlyDictionary<int,int>? Skills, Reputations;
        internal Dictionary<uint,int>? AvailabilityFixtureStates;
        internal Dictionary<int,long>? AvailabilityItemCounts;
        internal bool AvailabilityFixtureSatisfiable;
        internal int[] NormalIds = new int[4], NormalCounts = new int[4], ItemIds = new int[6], ItemCounts = new int[6];

        internal QuestScheduleResult Schedule(int? level = null, int? race = null, bool accepted = false, bool failed = false,
            bool complete = false, bool authority = true, bool logComplete = true, bool logFull = false, bool rewarded = false,
            int[]? progress = null, Dictionary<int, long>? items = null, SpawnPoint? origin = null,
            QuestRecoveryState state = QuestRecoveryState.Eligible, bool mayAttempt = true, bool? safe = true, bool? reachable = true,
            Action<string>? log = null, bool cancelNavigation = false, DateTime? now = null, bool metadataKnown = true,
            HashSet<string>? assessed = null, int? classId = null, uint[]? history = null,
            QuestSchedulerAcceptedQuest[]? activeParents = null, bool omitSkills = false, bool omitReputations = false,
            IReadOnlyDictionary<int,int>? skillOverride = null, IReadOnlyDictionary<int,int>? reputationOverride = null,
            bool omitInventory = false,
            int? areaOverride = null, bool omitArea = false, uint[]? dailyOverride = null, bool omitDaily = false,
            uint[]? spellOverride = null, bool omitSpells = false)
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
                UtcNow = now ?? Now, PlayerLevel = level ?? Level, PlayerRaceId = race ?? Race, PlayerClassId = classId ?? ClassId,
                PlayerAreaId = omitArea ? null : areaOverride ?? Area,
                DailyQuestIds = omitDaily ? null : dailyOverride ?? DailyIds,
                ConfirmedSpellIds = omitSpells ? null : spellOverride ?? ConfirmedSpells,
                PlayerGuid = 123, MapId = point.Map, X = point.X, Y = point.Y, Z = point.Z,
                AcceptedQuests = observations, CompletedQuestIds = rewarded ? (history ?? Completed).Concat(new[] { (uint)Quest.Id }).ToArray() : history ?? Completed,
                HasAuthoritativeCompletions = authority, HasCompleteQuestLog = logComplete,
                RawQuestStates = QuestAvailabilityPolicy.FromRawFlags(observations.Select(q => q.QuestId),
                    observations.Where(q => q.IsCompleted).Select(q => q.QuestId), observations.Where(q => q.IsFailed).Select(q => q.QuestId)),
                SkillValues = omitSkills ? null : skillOverride ?? Skills,
                ReputationValues = omitReputations ? null : reputationOverride ?? Reputations,
                CarriedItemCounts = omitInventory ? null : items ?? InitialCarriedCounts()
            }, key => new QuestRecoveryDecision { State = state, MayAttempt = mayAttempt,
                RetryUtc = mayAttempt ? null : Now.AddMinutes(5), Status = "controlled " + state },
                50, 250, 80, log: log, navigationAssessment: point =>
                {
                    if (cancelNavigation) throw new OperationCanceledException("controlled stop/cancellation");
                    assessed?.Add(PointKey(point));
                    return new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = reachable };
                });
        }
        internal Dictionary<int,long> InitialCarriedCounts()
        {
            var counts = ItemIds.Where(id => id > 0).Distinct().ToDictionary(id => id, _ => 0L);
            if (AvailabilityItemCounts != null) foreach (var item in AvailabilityItemCounts) counts[item.Key] = item.Value;
            // Explicit controlled starting inventory, never an acquisition or
            // source-supply claim. Production only reads actual carried stock.
            if (Quest.RequiredStockItems != null) foreach (var item in Quest.RequiredStockItems) counts[item.ItemId] = item.Count;
            return counts;
        }
        internal bool Pickup(QuestScheduleResult result) => result.Plan.Any(value => value.Quest.Id == Quest.Id && value.Stage == QuestWorkStage.Pickup);
        internal bool AnyWork(QuestScheduleResult result) => result.Plan.Any(value => value.Quest.Id == Quest.Id);
        internal IEnumerable<SpawnPoint> Spawns(int entry, DataType type) =>
            (type == DataType.Creature ? Database.CreatureSpawns : Database.GameObjectSpawns).TryGetValue(entry.ToString(), out var points)
                ? points.Where(Valid) : Enumerable.Empty<SpawnPoint>();
        internal IEnumerable<SpawnPoint> ObjectiveSpawns(DataObjective objective) =>
            Spawns(objective.Type == DataKind.CollectFromGameObject ? objective.GameObjectId : objective.MobId,
                objective.Type == DataKind.CollectFromGameObject ? DataType.GameObject : DataType.Creature)
            .Concat(QuestCreditSourceCatalog.Locations(Quest, objective, Database)).Where(Valid);
    }

    private static string SerializeEffectiveModel(QuestDatabase database)
    {
        // The base schema deliberately ignores validated catalog metadata. The
        // audit exporter includes it explicitly without enabling JSON injection.
        var conditions = database.Quests.Where(quest => quest.AvailabilityConditions != null).Select(quest => quest.AvailabilityConditions).ToArray();
        var stock = database.Quests.Where(quest => quest.RequiredStockItems != null).ToDictionary(quest => quest.Id, quest => quest.RequiredStockItems);
        if (database.ObjectiveCreditSources.Count == 0 && conditions.Length == 0 && stock.Count == 0) return JsonSerializer.Serialize(database, Json);
        var model = JsonSerializer.SerializeToNode(database, Json)!.AsObject();
        model["ObjectiveCreditSources"] = JsonSerializer.SerializeToNode(database.ObjectiveCreditSources, Json);
        if (conditions.Length != 0) model["QuestAvailabilityConditions"] = JsonSerializer.SerializeToNode(conditions, Json);
        foreach (var quest in model["Quests"]!.AsArray())
            if (stock.TryGetValue(quest!["Id"]!.GetValue<int>(), out var requirements))
                quest["RequiredStockItems"] = JsonSerializer.SerializeToNode(requirements, Json);
        return model.ToJsonString(Json);
    }

    internal static void Run()
    {
        string dataset = Environment.GetEnvironmentVariable("CB_QUEST_SIM_DATASET") ?? throw new InvalidOperationException("CB_QUEST_SIM_DATASET required");
        string observationsPath = Environment.GetEnvironmentVariable("CB_QUEST_SIM_OBSERVATIONS") ?? throw new InvalidOperationException("CB_QUEST_SIM_OBSERVATIONS required");
        string output = Environment.GetEnvironmentVariable("CB_QUEST_SIM_OUTPUT") ?? throw new InvalidOperationException("CB_QUEST_SIM_OUTPUT required");
        int limit = int.TryParse(Environment.GetEnvironmentVariable("CB_QUEST_SIM_LIMIT"), out int size) ? size : int.MaxValue;
        if (File.Exists(output)) throw new IOException("Simulation evidence is create-only");
        byte[] bytes = File.ReadAllBytes(dataset); string hash = Hash(bytes);
        bool effectiveModel = Environment.GetEnvironmentVariable("CB_QUEST_SIM_USE_DATA_LOADER") == "1";
        var loader = effectiveModel ? new DataLoader(dataset) : null;
        var db = effectiveModel ? loader!.Load() : JsonSerializer.Deserialize<QuestDatabase>(bytes, Json);
        if (db == null) throw new InvalidDataException("Empty dataset");
        string repairPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataset))!, "quest_data.repairs.json");
        string repairSha = effectiveModel && File.Exists(repairPath) ? Hash(File.ReadAllBytes(repairPath)) : "absent";
        var observations = File.ReadLines(observationsPath).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToDictionary(row => row.GetProperty("quest_id").GetInt32());
        if (observations.Count != db.Quests.Count || db.Quests.Any(quest => !observations.ContainsKey(quest.Id)) ||
            observations.Values.Any(value => value.GetProperty("dataset_sha256").GetString() != hash))
            throw new InvalidDataException("Observations are not bound one-to-one to this exact dataset");
        if (effectiveModel)
        {
            using var effective = new FileStream(output + ".effective-model.json", FileMode.CreateNew, FileAccess.Write);
            byte[] modelBytes = System.Text.Encoding.UTF8.GetBytes(SerializeEffectiveModel(db));
            effective.Write(modelBytes);
            using var dependencies = new FileStream(output + ".dependency-metadata.json", FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(dependencies, db.DependencyMetadata, Json);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        using var fixture = new QuestDatasetObservationFixture();
        int rows = 0, passed = 0, failed = 0, pipelines = 0;
        var selected = db.Quests.OrderBy(quest => quest.Id).Take(limit).ToArray();
        foreach (QuestEntry quest in selected)
        {
            JsonElement evidence = observations[quest.Id];
            var record = new Record { quest_id = quest.Id, dataset_sha256 = hash,
                structural_classification = evidence.GetProperty("structural_classification").GetString()!,
                repair_sha256 = repairSha, execution_fingerprint = loader?.ExecutionFingerprint ?? "legacy-direct-model" };
            try
            {
                var context = BuildContext(db, quest, evidence, effectiveModel);
                record.observations = new { level = context.Level, race_id = context.Race, class_id = context.ClassId,
                    origin = context.Origin, authoritative_completed_prerequisites = context.Completed,
                    skill_values = context.Skills, reputation_values = context.Reputations,
                    area_id = context.Area,
                    active_parent_ids = context.ActiveParents.Select(value => value.QuestId).ToArray(),
                    availability_fixture_states = context.AvailabilityFixtureStates,
                    availability_fixture_satisfiable = context.AvailabilityFixtureSatisfiable,
                    reference = evidence, normal_slot_encoding = "original-client high-bit GO identities; four physical counters" };
                record.production_owners.Add("QuestScheduler.MaterializeSchedule");
                if (quest.AvailabilityConditions != null) AvailabilityCases(context, record);
                if (quest.RequiredStockItems != null) RequiredStockCases(context, record, fixture);
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
                        else if (baseline && !(quest.MaxLevel > 0 && level > quest.MaxLevel))
                        {
                            bool sourceAllows = quest.AvailabilityConditions == null ||
                                SourceAvailabilityExpected(quest.AvailabilityConditions, context.AvailabilityFixtureStates!, level, context.Area,
                                    context.DailyIds, context.InitialCarriedCounts(), context.ConfirmedSpells);
                            Check(actual == sourceAllows, "level admission differs from the explicit source requirement");
                        }
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
                if (quest.StartItem > 0 && !QuestDeliveryPolicy.HasContract(quest))
                {
                    Case(record, "provided-StartItem-presence-does-not-invent-pickup-recipe", () =>
                    {
                        // Change only the source item being tested. Keep other
                        // observed availability/required-stock facts unchanged.
                        var held = context.InitialCarriedCounts(); held[quest.StartItem] = 1;
                        Check(context.Pickup(context.Schedule(items: held)) == baseline,
                            "a provided-on-acceptance item changed pickup policy");
                    });
                    if (quest.RequiredStockItems != null)
                        Case(record, "provided-StartItem-cannot-replace-required-stock", () =>
                            Check(!context.Pickup(context.Schedule(items: new Dictionary<int,long> { [quest.StartItem] = 1 })),
                                "a source item replaced unrelated required carried materials"));
                }
                if (effectiveModel)
                {
                    if (quest.RequiredSkillID > 0 && quest.RequiredSkillPoints > 0)
                    {
                        Case(record, "required-skill-unknown-defers", () => Check(!context.Pickup(context.Schedule(omitSkills:true)), "unknown skill was treated as sufficient"));
                        Case(record, "required-skill-below-threshold-defers", () => Check(!context.Pickup(context.Schedule(skillOverride:new Dictionary<int,int>
                            { [quest.RequiredSkillID.Value] = quest.RequiredSkillPoints.Value - 1 })), "below-threshold skill admitted"));
                    }
                    if (quest.RequiredMinRepFaction > 0 || quest.RequiredMaxRepFaction > 0 || quest.RequiredFactionId1 > 0 || quest.RequiredFactionId2 > 0)
                        Case(record, "required-reputation-unknown-defers", () => Check(!context.Pickup(context.Schedule(omitReputations:true)), "unknown reputation admitted"));
                    if (quest.RequiredMinRepFaction > 0 && quest.RequiredMinRepValue > int.MinValue)
                    {
                        var values=new Dictionary<int,int>(context.Reputations ?? new Dictionary<int,int>())
                            { [quest.RequiredMinRepFaction.Value] = quest.RequiredMinRepValue.Value - 1 };
                        Case(record,"minimum-reputation-boundary-defers",()=>Check(!context.Pickup(context.Schedule(reputationOverride:values)),"minimum reputation bypassed"));
                    }
                    if (quest.RequiredMaxRepFaction > 0 && quest.RequiredMaxRepValue.HasValue)
                    {
                        var values=new Dictionary<int,int>(context.Reputations ?? new Dictionary<int,int>())
                            { [quest.RequiredMaxRepFaction.Value] = quest.RequiredMaxRepValue.Value };
                        Case(record,"exclusive-maximum-reputation-boundary-defers",()=>Check(!context.Pickup(context.Schedule(reputationOverride:values)),"maximum reputation bypassed"));
                    }
                }
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
            model_mode = effectiveModel ? "actual-DataLoader-with-source-bound-repairs" : "legacy-direct-model",
            repair_sha256 = repairSha, execution_fingerprint = loader?.ExecutionFingerprint ?? "legacy-direct-model",
            host_binary_sha256 = Hash(File.ReadAllBytes(typeof(StyxWoW).Assembly.Location)) }, new JsonSerializerOptions { WriteIndented = true }));
        if (failed != 0) throw new InvalidOperationException($"Dataset simulation has {failed} failed cases across {rows} rows; retained {output}");
    }

    private static Context BuildContext(QuestDatabase original, QuestEntry quest, JsonElement observations, bool effectiveModel = false)
    {
        var byId = original.Quests.ToDictionary(value => value.Id);
        var dependencyLookup = effectiveModel ? QuestDependencyCatalog.CreateLookup(original).ToDictionary(p => (int)p.Key, p => p.Value) : byId;
        var closure = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(quest.Id);
        while (queue.Count != 0)
        {
            int id = queue.Dequeue(); if (!closure.Add(id) || !dependencyLookup.TryGetValue(id, out var value)) continue;
            foreach (int parent in new[] { Math.Abs(value.PrevQuestID) }.Concat(value.PreviousQuestsIds ?? new()).Where(value => value > 0))
            {
                queue.Enqueue(parent);
                if (dependencyLookup.TryGetValue(parent, out var prior) && prior.ExclusiveGroup < 0)
                    foreach (var member in dependencyLookup.Values.Where(item => item.ExclusiveGroup == prior.ExclusiveGroup)) queue.Enqueue(member.Id);
            }
        }
        var db = new QuestDatabase { Quests = closure.Where(byId.ContainsKey).Select(id => byId[id]).ToList(),
            QuestGivers = original.QuestGivers.Where(value => value.QuestId == quest.Id).ToList(),
            QuestEnders = original.QuestEnders.Where(value => value.QuestId == quest.Id).ToList(),
            CreatureSpawns = original.CreatureSpawns, GameObjectSpawns = original.GameObjectSpawns,
            DependencyMetadata = original.DependencyMetadata, ObjectiveCreditSources = original.ObjectiveCreditSources };
        int active = quest.PrevQuestID < 0 ? -quest.PrevQuestID : 0;
        var context = new Context { Quest = quest, Database = db, Level = Math.Clamp(quest.MinLevel, 1, 80), EffectiveModel=effectiveModel,
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
        if (effectiveModel)
        {
            context.ClassId = new[] { 2, 1, 3, 4, 5, 6, 7, 8, 9, 11 }.FirstOrDefault(id => !quest.AllowableClasses.HasValue ||
                quest.AllowableClasses == 0 || quest.AllowableClasses == -1 || (quest.AllowableClasses.Value & (1 << (id-1))) != 0);
            if (observations.TryGetProperty("playable_actor_pairs", out JsonElement pairs))
            {
                if (!TrySelectActorPair(quest, pairs, out int legalRace, out int legalClass))
                    throw new InvalidDataException("No source-declared playable race/class pair satisfies the quest eligibility contract");
                context.Race = legalRace; context.ClassId = legalClass;
            }
            var skills = new Dictionary<int,int>(); var reputation = new Dictionary<int,int>();
            if (quest.RequiredSkillID > 0 && quest.RequiredSkillPoints >= 0) skills[quest.RequiredSkillID.Value] = quest.RequiredSkillPoints.Value;
            void Rep(int id,int? value) { if(id>0 && value.HasValue)reputation[id] = Math.Max(reputation.TryGetValue(id,out int old)?old:int.MinValue,value.Value); }
            Rep(quest.RequiredMinRepFaction ?? 0,quest.RequiredMinRepValue);
            Rep(quest.RequiredFactionId1,quest.RequiredFactionValue1); Rep(quest.RequiredFactionId2,quest.RequiredFactionValue2);
            if(quest.RequiredMaxRepFaction>0 && quest.RequiredMaxRepValue>int.MinValue && !reputation.ContainsKey(quest.RequiredMaxRepFaction.Value))
                reputation[quest.RequiredMaxRepFaction.Value] = quest.RequiredMaxRepValue.Value-1;
            context.Skills=skills;context.Reputations=reputation;
            if (quest.AvailabilityConditions != null) SeedAvailability(context);
        }
        return context;
    }

    // Controlled test inputs are selected from the pinned server's status truth
    // table, not by asking the production evaluator which inputs make it pass.
    // Existing prerequisite/history requirements remain hard constraints.
    private static int SourceStatusMask(QuestAvailabilityPredicate condition)
    {
        int mask = condition.Type switch { 8 => 64, 9 => 8, 14 => 1, 28 => 2, 47 => condition.Value2,
            _ => throw new InvalidDataException("Unsupported source condition in test fixture") };
        return condition.Negative ? 107 ^ mask : mask;
    }

    private static bool SourceLevel(QuestAvailabilityPredicate condition, int observed)
    {
        if (observed <= 0) return false; // No predicate, including negation, proves an unknown observation.
        bool comparison = condition.Value2 switch { 0 => observed == condition.Value1, 1 => observed > condition.Value1,
            2 => observed < condition.Value1, 3 => observed >= condition.Value1, 4 => observed <= condition.Value1,
            _ => throw new InvalidDataException("Unsupported source level comparison") };
        return condition.Negative ? !comparison : comparison;
    }

    private static bool SourceArea(QuestAvailabilityPredicate predicate, int? area) =>
        area > 0 && (predicate.Negative ? area != predicate.Value1 : area == predicate.Value1);

    private static bool SourceDaily(QuestAvailabilityPredicate predicate, IReadOnlyCollection<uint>? daily) =>
        daily != null && (predicate.Negative ? !daily.Contains((uint)predicate.Value1) : daily.Contains((uint)predicate.Value1));

    private static bool SourceItem(QuestAvailabilityPredicate predicate, IReadOnlyDictionary<int,long>? items)
    {
        if (items == null || items.Any(pair => pair.Key <= 0 || pair.Value < 0)) return false;
        long count = items.TryGetValue(predicate.Value1, out long held) ? held : 0;
        return predicate.Negative ? count < predicate.Value2 : count >= predicate.Value2;
    }

    private static bool SourceAvailabilityExpected(QuestAvailabilityContract contract, IReadOnlyDictionary<uint,int> states, int level, int? area,
        IReadOnlyCollection<uint>? daily, IReadOnlyDictionary<int,long>? items, IReadOnlyCollection<uint>? spells) =>
        contract.Groups.Any(group => group.Conditions.All(predicate => predicate.Type == 27 ? SourceLevel(predicate, level)
            : predicate.Type == 23 ? SourceArea(predicate, area)
            : predicate.Type == 43 ? SourceDaily(predicate, daily)
            : predicate.Type == 2 ? SourceItem(predicate, items)
            : predicate.Type == 25 ? !predicate.Negative && spells?.Contains((uint)predicate.Value1) == true
            : (SourceStatusMask(predicate) & (1 << states[(uint)predicate.Value1])) != 0));

    private static void SeedAvailability(Context context)
    {
        var contract = context.Quest.AvailabilityConditions!;
        var history = new HashSet<uint>(context.Completed);
        var active = context.ActiveParents.ToDictionary(q => q.QuestId, q => q.IsFailed ? 5 : q.IsCompleted ? 1 : 3);
        foreach (var group in contract.Groups)
        {
            var quantities = new Dictionary<int,long>(); bool possibleItems = true;
            foreach (var predicates in group.Conditions.Where(p => p.Type == 2).GroupBy(p => p.Value1))
            {
                long minimum = predicates.Where(p => !p.Negative).Select(p => (long)p.Value2).DefaultIfEmpty(0).Max();
                long maximum = predicates.Where(p => p.Negative).Select(p => (long)p.Value2 - 1).DefaultIfEmpty(long.MaxValue).Min();
                if (minimum > maximum) { possibleItems = false; break; }
                quantities[predicates.Key] = minimum;
            }
            if (!possibleItems) continue;
            int[] legalLevels = Enumerable.Range(1, 80).Where(level => level >= context.Quest.MinLevel &&
                !(context.Quest.MaxLevel > 0 && level > context.Quest.MaxLevel) &&
                group.Conditions.Where(p => p.Type == 27).All(p => SourceLevel(p, level)))
                .OrderBy(level => level == context.Level ? 0 : 1).ThenBy(level => level).ToArray();
            if (legalLevels.Length == 0) continue;
            var areaPredicates = group.Conditions.Where(p => p.Type == 23).ToArray();
            // Controlled source assignments only. These values never populate a
            // production snapshot or claim that a live player is in that area.
            var areaDomain = new List<int> { 1 };
            areaDomain.AddRange(areaPredicates.Select(p => p.Value1));
            areaDomain.AddRange(areaPredicates.Select(p => p.Value1 == int.MaxValue ? 1 : p.Value1 + 1));
            int? selectedArea = areaPredicates.Length == 0 ? null : areaDomain.Distinct()
                .Where(area => areaPredicates.All(p => SourceArea(p, area))).Select(area => (int?)area).FirstOrDefault();
            if (areaPredicates.Length != 0 && !selectedArea.HasValue) continue;
            var dailyGroups = group.Conditions.Where(p => p.Type == 43).GroupBy(p => (uint)p.Value1).ToArray();
            if (dailyGroups.Any(g => g.Select(p => p.Negative).Distinct().Count() > 1)) continue;
            var selectedDaily = dailyGroups.Where(g => !g.First().Negative).Select(g => g.Key).OrderBy(id => id).ToArray();
            if (selectedDaily.Length > 25) continue;
            var spellPredicates = group.Conditions.Where(p => p.Type == 25).ToArray();
            if (spellPredicates.Any(p => p.Negative)) continue;
            var selectedSpells = spellPredicates.Select(p => (uint)p.Value1).Distinct().OrderBy(id => id).ToArray();
            if (selectedSpells.Length > 64) continue;
            var choice = new Dictionary<uint,int>(); bool possible = true;
            foreach (var predicates in group.Conditions.Where(p => p.Type is not (2 or 23 or 25 or 27 or 43)).GroupBy(p => (uint)p.Value1))
            {
                int mask = predicates.Aggregate(107, (allowed, predicate) => allowed & SourceStatusMask(predicate));
                IEnumerable<int> domain = predicates.Key == (uint)context.Quest.Id ? new[] { 0 }
                    : active.TryGetValue(predicates.Key, out int accepted) ? new[] { accepted }
                    : history.Contains(predicates.Key) ? new[] { 6 } : new[] { 0, 1, 3, 5, 6 };
                int[] options = domain.Where(state => (mask & (1 << state)) != 0).ToArray();
                if (options.Length == 0) { possible = false; break; }
                choice[predicates.Key] = options[0];
            }
            if (!possible) continue;
            var selectedHistory = new HashSet<uint>(history); var selectedActive = new Dictionary<uint,int>(active);
            foreach (var pair in choice)
            {
                selectedHistory.Remove(pair.Key); selectedActive.Remove(pair.Key);
                if (pair.Value == 6) selectedHistory.Add(pair.Key);
                else if (pair.Value is 1 or 3 or 5) selectedActive[pair.Key] = pair.Value;
            }
            if (selectedActive.Count >= 25) continue;
            context.Level = legalLevels[0];
            context.Area = selectedArea;
            context.DailyIds = contract.Groups.SelectMany(g => g.Conditions).Any(p => p.Type == 43) ? selectedDaily : null;
            context.ConfirmedSpells = contract.Groups.SelectMany(g => g.Conditions).Any(p => p.Type == 25) ? selectedSpells : null;
            context.Completed = selectedHistory.OrderBy(id => id).ToArray();
            context.AvailabilityItemCounts = contract.Groups.SelectMany(g => g.Conditions).Any(p => p.Type == 2) ? quantities : null;
            context.ActiveParents = selectedActive.OrderBy(row => row.Key).Select(row => new QuestSchedulerAcceptedQuest
                { QuestId = row.Key, IsCompleted = row.Value == 1, IsFailed = row.Value == 5,
                  ObjectiveCounts = new int[4], NormalObjectiveIds = new int[4], NormalObjectiveRequiredCounts = new int[4] }).ToArray();
            context.AvailabilityFixtureSatisfiable = true;
            break;
        }
        var stateReferences = contract.Groups.SelectMany(g => g.Conditions).Where(p => p.Type is not (2 or 23 or 25 or 27 or 43)).Select(p => p.Value1).ToHashSet();
        context.AvailabilityFixtureStates = contract.ReferencedQuests.Where(r => stateReferences.Contains(r.QuestId)).ToDictionary(r => (uint)r.QuestId, r =>
        {
            var accepted = context.ActiveParents.FirstOrDefault(a => a.QuestId == (uint)r.QuestId);
            return accepted != null ? accepted.IsFailed ? 5 : accepted.IsCompleted ? 1 : 3 : context.Completed.Contains((uint)r.QuestId) ? 6 : 0;
        });
    }

    private static void AvailabilityCases(Context context, Record record)
    {
        QuestAvailabilityContract contract = context.Quest.AvailabilityConditions!;
        int startingPasses = record.passed_cases, startingFailures = record.failed_cases;
        var states = context.AvailabilityFixtureStates!;
        var plan = new[] { new QuestPlanEntry { Quest = context.Quest, Stage = QuestWorkStage.Pickup } };
        QuestSchedulerSnapshot Observe(IReadOnlyDictionary<uint,int> values, bool history = true, bool raw = true, int? level = null,
            int? area = null, bool unknownArea = false, uint[]? daily = null, bool unknownDaily = false,
            Dictionary<int,long>? items = null, bool unknownItems = false, uint[]? spells = null, bool unknownSpells = false) => new()
        {
            PlayerGuid = 123, PlayerLevel = level ?? context.Level, UtcNow = Now, HasCompleteQuestLog = true, HasAuthoritativeCompletions = history,
            PlayerAreaId = unknownArea ? null : area ?? context.Area,
            DailyQuestIds = unknownDaily ? null : daily ?? context.DailyIds,
            ConfirmedSpellIds = unknownSpells ? null : spells ?? context.ConfirmedSpells,
            CompletedQuestIds = values.Where(pair => pair.Value == 6).Select(pair => pair.Key).ToArray(),
            RawQuestStates = raw ? values.Where(pair => pair.Value is 1 or 3 or 5).ToDictionary(pair => pair.Key, pair => pair.Value) : null,
            CarriedItemCounts = unknownItems ? null : items ?? context.InitialCarriedCounts()
        };
        bool Expected(IReadOnlyDictionary<uint,int> values, IReadOnlyDictionary<int,long>? items) =>
            SourceAvailabilityExpected(contract, values, context.Level, context.Area, context.DailyIds, items, context.ConfirmedSpells);
        foreach (uint referenced in states.Keys.OrderBy(id => id))
            foreach (int state in new[] { 0, 1, 3, 5, 6 })
            {
                uint capturedId = referenced; int capturedState = state;
                Case(record, $"availability-reference={capturedId}:state={capturedState}", () =>
                {
                    var changed = new Dictionary<uint,int>(states) { [capturedId] = capturedState };
                    bool expected = Expected(changed, context.InitialCarriedCounts()); var observation = Observe(changed);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observation).Rejection == null) == expected,
                        "condition policy differs from pinned status/group/negation truth table");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observation) == expected,
                        "publication condition gate retained the previous state");
                });
            }
        foreach (var item in contract.Groups.SelectMany(g => g.Conditions).Where(p => p.Type == 2).GroupBy(p => p.Value1).OrderBy(g => g.Key))
        {
            int itemId = item.Key;
            var cases = item.SelectMany(p => new[] { 0L, (long)p.Value2 - 1, (long)p.Value2, (long)p.Value2 + 1 })
                .Distinct().OrderBy(value => value).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Concat(new[] { "unknown", "lost" });
            foreach (string scenario in cases)
            {
                string captured = scenario;
                Case(record, $"availability-item={itemId}:observed={captured}", () =>
                {
                    var counts = context.InitialCarriedCounts(); bool unknown = captured == "unknown";
                    if (captured == "lost")
                    {
                        _ = QuestAvailabilityPolicy.RequirementsCurrent(plan, Observe(states));
                        counts.Remove(itemId);
                    }
                    else if (!unknown) counts[itemId] = long.Parse(captured, System.Globalization.CultureInfo.InvariantCulture);
                    bool expected = Expected(states, unknown ? null : counts);
                    var observed = Observe(states, items: counts, unknownItems: unknown);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observed).Rejection == null) == expected,
                        "item availability differs from the source quantity threshold/negation");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observed) == expected,
                        "item availability publication kept stale or unobserved stock");
                    if (!expected) Check(!context.Pickup(context.Schedule(items: counts, omitInventory: unknown)),
                        "actual scheduler admitted an unsatisfied item condition");
                });
            }
        }
        foreach (int threshold in contract.Groups.SelectMany(group => group.Conditions).Where(p => p.Type == 27).Select(p => p.Value1).Distinct().OrderBy(x => x))
            foreach (int observed in new[] { 0, Math.Max(1, threshold - 1), threshold, threshold == int.MaxValue ? threshold : threshold + 1 }.Distinct())
            {
                int captured = observed;
                Case(record, $"availability-level={threshold}:observed={captured}", () =>
                {
                    var observation = Observe(states, level: captured);
                    bool expected = SourceAvailabilityExpected(contract, states, captured, context.Area, context.DailyIds, context.InitialCarriedCounts(), context.ConfirmedSpells);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observation).Rejection == null) == expected,
                        "level condition differs from pinned comparison and negation");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observation) == expected,
                        "current-level publication guard retained an earlier observation");
                    if (!expected) Check(!context.Pickup(context.Schedule(level: captured)), "actual scheduler ignored a rejected level condition");
                });
            }
        foreach (int requiredArea in contract.Groups.SelectMany(g => g.Conditions).Where(p => p.Type == 23).Select(p => p.Value1).Distinct().OrderBy(x => x))
            foreach (int? observed in new int?[] { null, requiredArea, requiredArea == int.MaxValue ? 1 : requiredArea + 1 })
            {
                int? captured = observed;
                Case(record, $"availability-area={requiredArea}:observed={captured?.ToString() ?? "unknown"}", () =>
                {
                    var observation = Observe(states, area: captured, unknownArea: !captured.HasValue);
                    bool expected = SourceAvailabilityExpected(contract, states, context.Level, captured, context.DailyIds, context.InitialCarriedCounts(), context.ConfirmedSpells);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observation).Rejection == null) == expected,
                        "area predicate differs from primary equality and unknown-preserving negation");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observation) == expected,
                        "current-area publication guard retained an earlier observation");
                    if (!expected) Check(!context.Pickup(context.Schedule(areaOverride: captured, omitArea: !captured.HasValue)),
                        "actual scheduler ignored an unknown or rejected area");
                });
            }
        foreach (uint dailyId in contract.Groups.SelectMany(g => g.Conditions).Where(p => p.Type == 43).Select(p => (uint)p.Value1).Distinct().OrderBy(x => x))
            foreach (string scenario in new[] { "unknown", "absent", "present", "reset" })
            {
                uint capturedId = dailyId; string captured = scenario;
                Case(record, $"availability-daily={capturedId}:observed={captured}", () =>
                {
                    var daily = new HashSet<uint>(context.DailyIds ?? Array.Empty<uint>());
                    if (captured == "present") daily.Add(capturedId); else daily.Remove(capturedId);
                    if (captured == "reset")
                    {
                        // Exercise an earlier observation before the reset; the
                        // current gate must evaluate the replacement list itself.
                        _ = QuestAvailabilityPolicy.RequirementsCurrent(plan, Observe(states, daily: new[] { capturedId }));
                        daily.Clear();
                    }
                    bool unknown = captured == "unknown";
                    var observation = Observe(states, daily: daily.ToArray(), unknownDaily: unknown);
                    bool expected = SourceAvailabilityExpected(contract, states, context.Level, context.Area, unknown ? null : daily, context.InitialCarriedCounts(), context.ConfirmedSpells);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observation).Rejection == null) == expected,
                        "daily predicate differs from current per-ID membership and unknown-preserving negation");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observation) == expected,
                        "daily publication guard retained old membership after reset or uncertainty");
                    if (!expected) Check(!context.Pickup(context.Schedule(dailyOverride: daily.ToArray(), omitDaily: unknown)),
                        "actual scheduler ignored an unavailable or unsatisfied daily condition");
                });
            }
        foreach (uint spell in contract.Groups.SelectMany(g => g.Conditions).Where(p => p.Type == 25).Select(p => (uint)p.Value1).Distinct().OrderBy(id => id))
            foreach (string scenario in new[] { "unknown", "empty", "other", "known", "lost" })
            {
                uint capturedId = spell; string captured = scenario;
                Case(record, $"availability-spell={capturedId}:observed={captured}", () =>
                {
                    var confirmed = new HashSet<uint>(context.ConfirmedSpells ?? Array.Empty<uint>());
                    if (captured == "known") confirmed.Add(capturedId);
                    else if (captured == "lost")
                    {
                        _ = QuestAvailabilityPolicy.RequirementsCurrent(plan, Observe(states, spells: new[] { capturedId }));
                        confirmed.Remove(capturedId);
                    }
                    else confirmed.Clear();
                    if (captured == "other") confirmed.Add(capturedId == int.MaxValue ? 1 : capturedId + 1);
                    bool unknown = captured == "unknown";
                    var observation = Observe(states, spells: confirmed.ToArray(), unknownSpells: unknown);
                    bool expected = SourceAvailabilityExpected(contract, states, context.Level, context.Area, context.DailyIds,
                        context.InitialCarriedCounts(), unknown ? null : confirmed);
                    Check((QuestAvailabilityPolicy.Evaluate(context.Quest, observation).Rejection == null) == expected,
                        "positive spell predicate borrowed unknown, absent or another spell's evidence");
                    Check(QuestAvailabilityPolicy.RequirementsCurrent(plan, observation) == expected,
                        "spell publication guard retained a stale positive result");
                    if (!expected) Check(!context.Pickup(context.Schedule(spellOverride: confirmed.ToArray(), omitSpells: unknown)),
                        "actual scheduler ignored an unconfirmed spell condition");
                });
            }
        Case(record, "availability-missing-observation-revokes-publication", () =>
            Check(!QuestAvailabilityPolicy.RequirementsCurrent(plan, null!), "missing current observation retained pickup"));
        Case(record, "availability-fixture-constrained-by-source-and-prerequisites", () =>
            Check(!context.AvailabilityFixtureSatisfiable || Expected(states, context.InitialCarriedCounts()), "fixture claims a satisfying source assignment without one"));
        record.availability_condition_validation = new { contract, passed_cases = record.passed_cases - startingPasses,
            failed_cases = record.failed_cases - startingFailures, fixture_satisfiable = context.AvailabilityFixtureSatisfiable,
            observed_reference_states = states, observed_daily_ids = context.DailyIds,
            observed_carried_item_counts = context.AvailabilityItemCounts,
            observed_positive_spell_ids = context.ConfirmedSpells,
            source_oracle = "TC335 ConditionMgr.cpp predicates 2/8/9/14/23/25/27/28/43/47 and Util.h comparisons; separate complete carried quantities, permanent reward history, ordinary raw states, current daily IDs, positive-only self spells, level and nullable area; OR-of-AND groups",
            live_completion_proven = false };
        record.production_owners.Add("QuestAvailabilityPolicy.Evaluate and RequirementsCurrent with each source reference in all five original quest states");
    }

    private static void RequiredStockCases(Context context, Record record, QuestDatasetObservationFixture fixture)
    {
        var quest = context.Quest; var requirements = quest.RequiredStockItems!;
        int startingPasses = record.passed_cases, startingFailures = record.failed_cases;
        fixture.SetQuest((uint)quest.Id, quest.Name, context.Level, context.NormalIds, context.NormalCounts, context.ItemIds, context.ItemCounts);
        fixture.SetRaceClass(context.Race, context.ClassId); fixture.SetAccepted(false); fixture.SetHistory(context.Completed);
        var initial = context.InitialCarriedCounts();
        bool baseline = context.Pickup(context.Schedule(items: initial));
        Dictionary<int,long> ReadCarried() => fixture.Player.CarriedItems.GroupBy(item => (int)item.Entry)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.StackCount));
        foreach (var requirement in requirements)
        {
            int item = requirement.ItemId, needed = requirement.Count;
            foreach (string state in new[] { "unknown", "zero", "partial", "full", "lost" })
            {
                string observedState = state;
                Case(record, "required-stock-item=" + item + ":observed=" + observedState, () =>
                {
                    if (observedState == "unknown")
                    {
                        Check(QuestRequiredStockPolicy.Rejection(quest, null) != null && !context.Pickup(context.Schedule(omitInventory: true)),
                            "unknown inventory satisfied a required-stock contract");
                        return;
                    }
                    var supplied = new Dictionary<int,long>(initial);
                    if (observedState == "lost") { fixture.SetInventory(supplied); supplied.Remove(item); }
                    else supplied[item] = observedState == "full" ? needed : observedState == "partial" ? needed - 1 : 0;
                    fixture.SetInventory(supplied); var actual = ReadCarried();
                    if (observedState == "full")
                    {
                        Check(QuestRequiredStockPolicy.Rejection(quest, actual) == null,
                            "real inventory reader failed the declared stock quantity");
                        Check(context.Pickup(context.Schedule(items: actual)) == baseline,
                            "actual stock receipt changed otherwise identical pickup admission");
                    }
                    else
                    {
                        Check(QuestRequiredStockPolicy.Rejection(quest, actual) != null && !context.Pickup(context.Schedule(items: actual)),
                            "missing or partial required stock authorized pickup");
                        Check(!context.Schedule(accepted: true, complete: true, items: actual).Plan.Any(entry => entry.Quest.Id == quest.Id && entry.Stage == QuestWorkStage.TurnIn),
                            "server-ready status replaced a required material receipt");
                    }
                });
            }
        }
        Case(record, "required-stock-missing-observation-withholds-pickup", () =>
            Check(!context.Pickup(context.Schedule(omitInventory: true)), "unknown inventory authorized stock-sensitive pickup"));
        Case(record, "required-stock-held-items-not-server-ready", () =>
        {
            fixture.SetInventory(initial); var actual = ReadCarried();
            Check(QuestRequiredStockPolicy.Rejection(quest, actual) == null &&
                !context.Schedule(accepted: true, items: actual).Plan.Any(entry => entry.Quest.Id == quest.Id && entry.Stage == QuestWorkStage.TurnIn),
                "observed starting materials fabricated completion of ordinary objectives");
        });
        Case(record, "required-stock-keeps-ordinary-objectives", () =>
            Check(quest.Objectives.Any(objective => objective.Type != DataKind.TurnInOnly) &&
                requirements.All(item => item.ItemId != quest.StartItem && item.ItemId != quest.SupplementalSupply?.ItemId &&
                    quest.Objectives.All(objective => objective.ItemId != item.ItemId)),
                "required stock replaced an ordinary or supplied-item owner"));
        record.required_stock_validation = new { quest_id = quest.Id, items = requirements,
            passed_cases = record.passed_cases - startingPasses, failed_cases = record.failed_cases - startingFailures,
            acquisition_proven = false, observation_limit = "Explicit starting inventory in the test process; acquisition remains unmodeled." };
        record.production_owners.Add("QuestRequiredStockPolicy and actual inventory readers -> scheduler pickup/turn-in stock gates; acquisition unresolved");
    }

    private static bool TrySelectActorPair(QuestEntry quest, JsonElement pairs, out int race, out int playerClass)
    {
        race = 0; playerClass = 0;
        if (pairs.ValueKind != JsonValueKind.Array) return false;
        var choices = new List<(int Race, int Class)>();
        foreach (JsonElement pair in pairs.EnumerateArray())
        {
            if (!pair.TryGetProperty("race", out var r) || !pair.TryGetProperty("class", out var c) ||
                !r.TryGetInt32(out int candidateRace) || !c.TryGetInt32(out int candidateClass) ||
                candidateRace < 1 || candidateRace > 11 || candidateClass < 1 || candidateClass > 11)
                return false;
            if (quest.AllowableRaces != 0 && quest.AllowableRaces != -1 && (quest.AllowableRaces & (1 << (candidateRace-1))) == 0) continue;
            if (quest.AllowableClasses.HasValue && quest.AllowableClasses != 0 && quest.AllowableClasses != -1 &&
                (quest.AllowableClasses.Value & (1 << (candidateClass-1))) == 0) continue;
            choices.Add((candidateRace,candidateClass));
        }
        if (choices.Count == 0) return false;
        var selected = choices.OrderBy(p => p.Class == 2 ? 0 : 1).ThenBy(p => p.Race == 10 ? 0 : 1).ThenBy(p => p.Race).ThenBy(p => p.Class).First();
        race = selected.Race; playerClass = selected.Class;
        return true;
    }

    private static void Pipeline(Context context, Record record, QuestDatasetObservationFixture fixture)
    {
        var quest = context.Quest; uint id = (uint)quest.Id;
        var pickup = context.Schedule();
        var pickupPlan = pickup.Plan.Where(value => value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.Pickup).ToArray();
        if (pickupPlan.Length == 0) { record.pipeline_blocks.Add("scheduler-pickup-not-admitted:" + pickup.Status); return; }
        fixture.SetQuest(id, quest.Name, context.Level, context.NormalIds, context.NormalCounts, context.ItemIds, context.ItemCounts);
        if(context.EffectiveModel)fixture.SetRaceClass(context.Race,context.ClassId);
        fixture.SetAccepted(false); fixture.SetHistory(context.Completed);
        if (quest.RequiredStockItems != null)
            Case(record, "pipeline-required-stock-observed-before-pickup", () =>
            {
                fixture.SetInventory(context.InitialCarriedCounts());
                var observed = fixture.Player.CarriedItems.GroupBy(item => (int)item.Entry)
                    .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.StackCount));
                Check(context.Pickup(context.Schedule(items: observed)), "required starting materials did not reach actual pickup admission");
            });
        if (quest.AvailabilityConditions?.Groups.SelectMany(g => g.Conditions).Any(p => p.Type == 2) == true)
            Case(record, "pipeline-carried-availability-observation", () =>
            {
                fixture.SetInventory(context.InitialCarriedCounts());
                var observed = fixture.Player.CarriedItems.GroupBy(item => (int)item.Entry)
                    .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.StackCount));
                Check(context.Pickup(context.Schedule(items: observed)),
                    "actual controlled inventory reader did not preserve the source-constrained pickup");
            });
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
        if (quest.RequiredStockItems != null) foreach (var item in quest.RequiredStockItems) carried[item.ItemId] = item.Count;
        int startingFailures = record.failed_cases;
        if (context.EffectiveModel && quest.SupplementalSupply != null)
        {
            var supply = quest.SupplementalSupply;
            Case(record,"pipeline-supplemental-supply-is-not-an-inventory-receipt",()=>
                Check(!context.Schedule(accepted:true,complete:true,items:carried).Plan.Any(p=>p.Quest.Id==quest.Id && p.Stage==QuestWorkStage.TurnIn),
                    "source promise authorized turn-in without an observed required item"));
            Case(record,"pipeline-supplemental-source-item-observed-after-acceptance",()=>
            {
                carried[supply.ItemId]=supply.ProvidedCount;
                fixture.SetInventory(carried);
                Check(fixture.Player.CarriedItems.Where(item=>item.Entry==supply.ItemId).Sum(item=>(long)item.StackCount)>=supply.RequiredCount,
                    "actual inventory owner did not acknowledge the supplied return item");
            });
            record.production_owners.Add("supplemental source supply -> actual carried-item observation; no item-use action inferred");
        }
        if (QuestDeliveryPolicy.HasContract(quest))
        {
            var expected = quest.DeliveryItems!.ToDictionary(item=>item.ItemId,item=>(long)item.Count);
            var supplied = quest.AcceptanceSupplies!.ToDictionary(item=>item.ItemId,item=>(long)item.Count);
            Case(record,"pipeline-delivery-source-promise-is-not-receipt",()=>
            {
                fixture.SetInventory(new());
                Check(!context.Schedule(accepted:true,complete:true,items:new()).Plan.Any(p=>p.Quest.Id==quest.Id && p.Stage==QuestWorkStage.TurnIn),"unobserved promised supply authorized turn-in");
            });
            foreach(var item in expected)
            {
                var partial=new Dictionary<int,long>(expected){[item.Key]=Math.Max(0,item.Value-1)};
                Case(record,"pipeline-delivery-partial-receipt="+item.Key,()=>Check(!context.Schedule(accepted:true,complete:true,items:partial)
                    .Plan.Any(p=>p.Quest.Id==quest.Id && p.Stage==QuestWorkStage.TurnIn),"partial delivery receipt authorized turn-in"));
            }
            Case(record,"pipeline-delivery-actual-inventory-acknowledgement",()=>
            {
                fixture.SetInventory(expected);
                var observed=fixture.Player.CarriedItems.GroupBy(item=>(int)item.Entry).ToDictionary(g=>g.Key,g=>g.Sum(item=>(long)item.StackCount));
                Check(QuestDeliveryPolicy.TurnInRejection(quest,observed)==null,"real inventory reader failed delivery acknowledgement");
                Check(!context.Schedule(accepted:true,items:observed).Plan.Any(p=>p.Quest.Id==quest.Id && p.Stage==QuestWorkStage.TurnIn),"item receipt fabricated server flag");
            });
            carried=expected;
            record.production_owners.Add("QuestDeliveryPolicy source contract -> actual carried inventory -> independent server completed flag");
        }
        var provedItems = new HashSet<int>();
        IEnumerable<DataObjective> pipelineObjectives = quest.Objectives.Where(value => value.Type != DataKind.TurnInOnly);
        if (context.EffectiveModel)
            pipelineObjectives = pipelineObjectives.OrderBy(objective =>
                context.ObjectiveSpawns(objective).Any() ? 0 : 1)
                .ThenBy(objective => objective.Index);
        foreach (DataObjective objective in pipelineObjectives)
        {
            bool itemObjective = objective.ItemId > 0 && (objective.Type == DataKind.CollectItem || objective.Type == DataKind.CollectFromGameObject);
            if (context.EffectiveModel && itemObjective && provedItems.Contains(objective.ItemId) &&
                carried.TryGetValue(objective.ItemId, out long held) && held >= objective.CollectCount &&
                Enumerable.Range(0, context.ItemIds.Length).Count(index => context.ItemIds[index] == objective.ItemId && context.ItemCounts[index] == objective.CollectCount) == 1)
            {
                Case(record, "pipeline-completed-item-suppresses-alternative=" + objective.Index, () =>
                {
                    var observed = fixture.Player.CarriedItems.Where(item => item.Entry == objective.ItemId).Sum(item => (long)item.StackCount);
                    Check(observed >= objective.CollectCount, "the previously proved route no longer has a real carried receipt");
                    Check(!context.Schedule(accepted:true, progress:completedCounts, items:carried).Plan.Any(plan => plan.Quest.Id == quest.Id && plan.ObjectiveIndex == objective.Index),
                        "completed item was rescheduled through another acquisition source");
                });
                continue;
            }
            var targetType = objective.Type == DataKind.CollectFromGameObject ? DataType.GameObject : DataType.Creature;
            int entry = objective.Type == DataKind.CollectFromGameObject ? objective.GameObjectId : objective.MobId;
            SpawnPoint? endpoint = context.ObjectiveSpawns(objective).FirstOrDefault();
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
            if (item)
                Case(record, "pipeline-collection-source-handoff:index=" + objective.Index, () =>
                {
                    var definition = Styx.Logic.Profiles.ProfileManager.CurrentProfile.FindQuest(id)?.FindCollectItem((uint)objective.ItemId);
                    bool bound = targetType == DataType.GameObject
                        ? definition?.OverridedCollectFrom?.ContainsGameObject((uint)entry) == true
                        : definition?.OverridedCollectFrom?.ContainsMob((uint)entry) == true;
                    Check(bound, "the generated runtime item definition lost the selected collection source");
                });
            record.production_owners.Add("ForcedBehaviorExecutor.ResolveQuestObjectiveIndex -> QuestManager.CreateQuestObjective -> ForcedQuestObjective.IsDone:" + objective.Type);
            foreach (QuestCreditSource source in QuestCreditSourceCatalog.ForObjective(quest, objective, context.Database))
            {
                QuestCreditSource current = source;
                Case(record, "pipeline-original-client-credit-producer=" + current.CreatureId, () =>
                {
                    Check(owner.Objective is Bots.Quest.Objectives.GrindObjective, "credit hint did not retain the ordinary kill owner");
                    typeof(QuestObservedDatasetRoutesRegressionTests).GetMethod("CheckActualAliasTarget", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, new object[] { (Bots.Quest.Objectives.GrindObjective)owner.Objective, current.CreatureId, current.CreditId });
                    Check(!owner.IsDone, "native credit identity or source location fabricated progress");
                });
            }
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
            if (context.EffectiveModel && itemObjective && record.failed_cases == startingFailures &&
                Enumerable.Range(0, context.ItemIds.Length).Count(index => context.ItemIds[index] == objective.ItemId && context.ItemCounts[index] == objective.CollectCount) == 1)
                provedItems.Add(objective.ItemId);
            fixture.ReleaseOwners();
        }
        if (record.pipeline_blocks.Count != 0) return;
        if (context.EffectiveModel)
        {
            // A positive terminal state must come from the exercised objective
            // or explicitly observed delivery path, never from filling omitted
            // requirements merely to reach the turn-in test.
            foreach (int index in Enumerable.Range(0, 6).Where(index => context.ItemIds[index] > 0))
                if (!carried.TryGetValue(context.ItemIds[index], out long count) || count < context.ItemCounts[index])
                    record.pipeline_blocks.Add("unexercised-required-item:" + context.ItemIds[index]);
            for (int index = 0; index < 4; index++)
                if (context.NormalIds[index] != 0 && completedCounts[index] < context.NormalCounts[index])
                    record.pipeline_blocks.Add("unexercised-required-normal-slot:" + index);
            if (record.pipeline_blocks.Count != 0) return;
        }
        else
        {
            // Preserve the historical fixture's supplied-terminal-observation
            // scope; the opt-in effective-data pipeline above is stricter.
            foreach (int index in Enumerable.Range(0, 6).Where(index => context.ItemIds[index] > 0)) carried[context.ItemIds[index]] = context.ItemCounts[index];
            completedCounts = context.NormalCounts.ToArray();
        }
        Case(record, "pipeline-counts-do-not-invent-server-complete-flag", () =>
            Check(!context.Schedule(accepted: true, progress: completedCounts, items: carried).Plan.Any(value => value.Quest.Id == quest.Id && value.Stage == QuestWorkStage.TurnIn), "counts invented server completion"));
        var enderPoint = context.Database.QuestEnders.SelectMany(ender => context.Spawns(ender.EnderId, ender.EnderType)).FirstOrDefault();
        if (enderPoint == null) { record.pipeline_blocks.Add("turn-in-endpoint-unrepresented"); return; }
        var turnIn = context.Schedule(accepted: true, complete: true, progress: completedCounts, items: carried, origin: enderPoint);
        if (context.EffectiveModel && quest.SupplementalSupply != null)
            Case(record,"pipeline-supplemental-item-loss-revokes-turn-in",()=>
            {
                var missing=new Dictionary<int,long>(carried){[quest.SupplementalSupply.ItemId]=0};
                Check(!context.Schedule(accepted:true,complete:true,items:missing,origin:enderPoint).Plan.Any(p=>p.Quest.Id==quest.Id && p.Stage==QuestWorkStage.TurnIn),
                    "normal objective completion bypassed a lost required source-item receipt");
            });
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
