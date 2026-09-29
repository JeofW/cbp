using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;
using DataKind = WholesomeAQ.ObjectiveType;
using DataType = WholesomeAQ.QuestObjectType;

// Exact-row companion to the4335 sweep. References supply controlled identities;
// current-actor/slot/credit observations remain explicit simulation assumptions.
internal static class QuestObservedDatasetRoutesRegressionTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 11, 30, 0, DateTimeKind.Utc);
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }
    private sealed class Row
    {
        public string route_type { get; set; } = "";
        public int quest_id { get; set; }
        public string pointer { get; set; } = "";
        public string dataset_sha256 { get; set; } = "";
        public int passed_cases { get; set; }
        public int failed_cases { get; set; }
        public string route_result { get; set; } = "NOT-RUN";
        public bool controlled_pipeline_passed { get; set; }
        public object? source_observations { get; set; }
        public List<object> cases { get; } = new();
        public List<string> limitations { get; } = new() { "Supplied observations are not a live realm sample.", "No native gameplay or server reward was performed." };
    }

    internal static void Run()
    {
        string path = Environment.GetEnvironmentVariable("CB_QUEST_SIM_DATASET") ?? throw new InvalidOperationException("CB_QUEST_SIM_DATASET required");
        string casesPath = Environment.GetEnvironmentVariable("CB_QUEST_ROUTE_CASES") ?? throw new InvalidOperationException("CB_QUEST_ROUTE_CASES required");
        string output = Environment.GetEnvironmentVariable("CB_QUEST_ROUTE_OUTPUT") ?? throw new InvalidOperationException("CB_QUEST_ROUTE_OUTPUT required");
        byte[] bytes = File.ReadAllBytes(path); string hash = Hash(bytes);
        var full = JsonSerializer.Deserialize<QuestDatabase>(bytes, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })!;
        using var document = JsonDocument.Parse(File.ReadAllBytes(casesPath));
        var root = document.RootElement;
        Check(root.GetProperty("dataset_sha256").GetString() == hash, "route cases are bound to a different dataset");
        Check(root.GetProperty("items").GetArrayLength() == 7 && root.GetProperty("credits").GetArrayLength() == 124, "target row coverage changed");
        using var fixture = new QuestDatasetObservationFixture();
        using var lua = new RewardLua51Boundary.StockLua51(Checkout());
        using var writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var rows = new List<Row>();
        foreach (var source in root.GetProperty("items").EnumerateArray())
        {
            var row = new Row { route_type = "item-starter", quest_id = source.GetProperty("quest_id").GetInt32(), dataset_sha256 = hash, source_observations = source.Clone() };
            var c = new Context(full, row.quest_id, source.GetProperty("observation"));
            int itemId = source.GetProperty("item_ids")[0].GetInt32();
            QuestItemStarterObservation Starter(bool active = false, DateTime? time = null) => new() { QuestId = row.quest_id, ItemEntry = itemId,
                ItemGuid = 99000001, PlayerGuid = 123, MapId = c.Origin.Map, ObservedUtc = time ?? Now, IsActive = active, Name = "Observed item" };
            var items = new Dictionary<int,long> { [itemId] = 1 };
            var admitted = c.Plan(starters: new[] { Starter() }, items: items);
            Case(row, "fresh-carried-starter-admitted", () => Check(c.Pickup(admitted), "fresh observed starter was rejected"));
            Case(row, "missing-item-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() })), "missing item authorized pickup"));
            Case(row, "stale-observation-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter(time: Now.AddSeconds(-1)) }, items: items)), "stale sample admitted"));
            Case(row, "active-item-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter(active: true) }, items: items)), "active item admitted"));
            Case(row, "history-unknown-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, authority: false)), "unknown history admitted"));
            Case(row, "raw-log-incomplete-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, completeLog: false)), "incomplete log admitted"));
            Case(row, "recovery-block-retained", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, blocked: true)), "recovery block bypassed"));
            Case(row, "below-MinLevel-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, level: c.Quest.MinLevel - 1)), "minimum level bypassed"));
            Case(row, "rewarded-item-quest-not-admitted", () => Check(!c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, rewarded: true)), "rewarded quest readmitted"));
            Case(row, "generated-profile-to-existing-item-behavior", () =>
            {
                var plans = admitted.Plan.Where(value => value.Quest.Id == row.quest_id && value.Stage == QuestWorkStage.Pickup).ToArray();
                Check(plans.Length > 0, "no item plan");
                string xml = new ProfileBuilder().BuildProfileXml(plans, c.Database, "Observed route", "Fixture", c.Quest.MinLevel);
                var element = XDocument.Parse(xml).Descendants("PickUp").Single();
                var node = PickUpNode.FromXml(element);
                Check(node.GiverType == Styx.Logic.Profiles.Quest.QuestObjectType.Item && node.GiverId == itemId, "generated profile lost item type");
                fixture.SetQuest((uint)row.quest_id, c.Quest.Name, c.Level, c.NormalIds, c.NormalCounts, c.ItemIds, c.ItemCounts);
                fixture.SetAccepted(false); fixture.SetHistory(c.History); fixture.SetInventory(items); fixture.LoadProfile(xml);
                var owner = (ForcedQuestPickUp)typeof(Bots.Quest.Actions.ForcedBehaviorExecutor).GetMethod("CreateQuestPickUp", Hidden)!.Invoke(null, new object[] { node })!;
                Check(owner != null && !owner.IsDone, "item pickup acknowledged before acceptance");
                string script = (string)typeof(WoWItem).GetMethod("BuildValidatedQuestStartingItemLua", Hidden)!.Invoke(null,
                    new object[] { 0, 1, (uint)itemId, (uint)row.quest_id, 123UL })!;
                string setup = $"clicks=0; function UnitGUID(u) return '0x000000000000007B' end; function GetContainerItemLink(b,s) return '|Hitem:{itemId}:0|h[Item]|h' end; " +
                    $"function GetContainerItemQuestInfo(b,s) return nil,{row.quest_id},false end; function GetNumQuestLogEntries() return 0,0 end; function UseContainerItem(b,s) clicks=clicks+1 end";
                var receipt = lua.Execute(script, (uint)Encoding.UTF8.GetByteCount(script), "valid", new[] { "", "" }, setup);
                Check(receipt.Load == 0 && receipt.Call == 0 && receipt.Clicks == 1 && receipt.Values.SequenceEqual(new[] { "1" }), "actual item-use Lua refused supplied observations");
                fixture.SetAccepted(true);
                Check(owner.IsDone, "accepted log did not acknowledge item pickup");
                if (c.Quest.Objectives.Any(value => value.Type != DataKind.TurnInOnly))
                {
                    row.limitations.Add("Item acquisition is covered; remaining nontrivial objectives keep their dataset/source obligations.");
                    return;
                }
                var ender = c.Database.QuestEnders.FirstOrDefault(value => c.Spawns(value.EnderId, value.EnderType).Any());
                Check(ender != null, "turn-in-only item quest lacks a represented ender");
                var point = c.Spawns(ender!.EnderId, ender.EnderType).First();
                var ending = c.Plan(accepted: true, completed: true, origin: point);
                var endPlan = ending.Plan.Where(value => value.Quest.Id == row.quest_id && value.Stage == QuestWorkStage.TurnIn).ToArray();
                Check(endPlan.Length > 0, "supplied server-completed flag did not schedule turn-in");
                string endingXml = new ProfileBuilder().BuildProfileXml(endPlan, c.Database, "Observed route", "Fixture", c.Level);
                fixture.LoadProfile(endingXml); fixture.SetAccepted(true, complete: true);
                var endOwner = new ForcedQuestTurnIn((uint)row.quest_id, c.Quest.Name, (uint)ender.EnderId, ender.EnderName,
                    new Styx.Logic.Pathing.WoWPoint(point.X, point.Y, point.Z));
                Check(!endOwner.IsDone, "turn-in completed before reward removal");
                fixture.SetAccepted(false); fixture.SetHistory(c.History.Concat(new[] { (uint)row.quest_id }));
                Check(endOwner.IsDone && !c.Pickup(c.Plan(starters: new[] { Starter() }, items: items, rewarded: true)), "reward acknowledgement/next scheduling failed");
                row.controlled_pipeline_passed = true;
            });
            row.route_result = row.failed_cases == 0 ? "OBSERVED-ITEM-PICKUP-PROVEN" : "FAIL";
            rows.Add(row); writer.WriteLine(JsonSerializer.Serialize(row)); writer.Flush();
        }
        foreach (var source in root.GetProperty("credits").EnumerateArray())
        {
            var row = new Row { route_type = "creature-credit", quest_id = source.GetProperty("quest_id").GetInt32(),
                pointer = source.GetProperty("pointer").GetString()!, dataset_sha256 = hash, source_observations = source.Clone() };
            var c = new Context(full, row.quest_id, source.GetProperty("observation"));
            var objective = c.Quest.Objectives[source.GetProperty("objective_ordinal").GetInt32()];
            int creditId = source.GetProperty("target_entry").GetInt32();
            var aliases = source.GetProperty("aliases").EnumerateArray().ToArray();
            var matching = Enumerable.Range(0,4).Where(index => c.NormalIds[index] == creditId).ToArray();
            bool matches = matching.Length == 1 && c.NormalCounts[matching[0]] == objective.KillCount && (c.Quest.SpecialFlags & 32) == 0;
            bool Scheduled(QuestScheduleResult value) => value.Plan.Any(plan => plan.Quest.Id == row.quest_id && plan.ObjectiveIndex == objective.Index &&
                (plan.Stage == QuestWorkStage.Objective || plan.Stage == QuestWorkStage.AncestorCorrection));
            Case(row, "missing-static-target-is-not-fabricated", () => Check(!Scheduled(c.Plan(accepted: true)), "missing target acquired an invented route"));
            foreach (var alias in aliases)
            {
                int actor = alias.GetProperty("actor_entry").GetInt32();
                QuestCreatureCreditObservation Credit(bool attackable = true, bool stale = false) => new() { Entry = actor, Credit1 = creditId,
                    Guid = 99001001, PlayerGuid = 123, ObservedUtc = stale ? Now.AddSeconds(-1) : Now, MapId = c.Origin.Map,
                    X = c.Origin.X + 2, Y = c.Origin.Y, Z = c.Origin.Z, AliveAttackableSelectable = attackable };
                var current = new[] { Credit() };
                Case(row, "matching-runtime-alias=" + actor, () => Check(Scheduled(c.Plan(accepted: true, credits: current)) == matches, "alias admission disagreed with runtime identity"));
                Case(row, "dead-or-unattackable-alias=" + actor, () => Check(!Scheduled(c.Plan(accepted: true, credits: new[] { Credit(attackable: false) })), "friendly/dead actor became killing"));
                Case(row, "stale-alias=" + actor, () => Check(!Scheduled(c.Plan(accepted: true, credits: new[] { Credit(stale: true) })), "stale alias admitted"));
                Case(row, "quarantined-alias=" + actor, () => Check(!Scheduled(c.Plan(accepted: true, credits: current, blocked: true)), "recovery bypassed"));
                Case(row, "unsafe-alias=" + actor, () => Check(!Scheduled(c.Plan(accepted: true, credits: current, safe: false)), "safety bypassed"));
                if (matches)
                    Case(row, "profile-owner-target-and-progress=" + actor, () =>
                    {
                        var plan = c.Plan(accepted: true, credits: current).Plan.Where(value => value.Quest.Id == row.quest_id && value.ObjectiveIndex == objective.Index).ToArray();
                        string xml = new ProfileBuilder().BuildProfileXml(plan, c.Database, "Observed alias", "Fixture", c.Level);
                        var element = XDocument.Parse(xml).Descendants("QuestOrder").Descendants("Objective").Single();
                        fixture.SetQuest((uint)row.quest_id, c.Quest.Name, c.Level, c.NormalIds, c.NormalCounts, c.ItemIds, c.ItemCounts);
                        fixture.LoadProfile(xml);
                        var owner = fixture.CreateObjective(ObjectiveNode.FromXml(element));
                        Check(owner.Objective is GrindObjective, "ordinary credit did not produce the generic kill owner");
                        Check(!owner.IsDone, "zero progress was completed");
                        CheckActualAliasTarget((GrindObjective)owner.Objective, actor, creditId);
                        var progress = new int[4]; progress[matching[0]] = objective.KillCount;
                        fixture.SetProgress(progress); Check(owner.IsDone, "actual owner failed to acknowledge credit");
                        Check(!Scheduled(c.Plan(accepted: true, credits: current, progress: progress)), "scheduler repeated acknowledged credit");
                        fixture.ReleaseOwners();
                    });
            }
            if (aliases.Length == 0)
            {
                row.route_result = "SCRIPT-ONLY-NO-ACTION-INVENTED";
                row.limitations.Add("Referenced script gives kill credit; its trigger, target and any explicit action recipe require separate validation.");
            }
            else if (!matches)
            {
                row.route_result = "RUNTIME-CREDIT-CONFLICT-DEFERRED";
                row.limitations.Add("Reference normal-objective identity/count does not uniquely match this bot row.");
            }
            else
            {
                row.route_result = "OBSERVED-ORDINARY-ALIAS-PROVEN";
                row.limitations.Add("The alive/attackable/selectable actor and cache alias are supplied observations. They are not assumed from secondary SQL, and no whole-quest completion is claimed.");
            }
            if (row.failed_cases != 0) row.route_result = "FAIL";
            rows.Add(row); writer.WriteLine(JsonSerializer.Serialize(row)); writer.Flush();
        }
        var summary = new { dataset_sha256 = hash, route_rows = rows.Count, item_quest_rows = rows.Count(row => row.route_type == "item-starter"),
            credit_objective_rows = rows.Count(row => row.route_type == "creature-credit"), passed_cases = rows.Sum(row => row.passed_cases),
            failed_cases = rows.Sum(row => row.failed_cases), item_pipelines = rows.Count(row => row.controlled_pipeline_passed),
            route_results = rows.GroupBy(row => row.route_result).ToDictionary(group => group.Key, group => group.Count()),
            input_cases_sha256 = Hash(File.ReadAllBytes(casesPath)), no_game_attached = true };
        File.WriteAllText(output + ".summary.json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(summary));
        if (rows.Any(row => row.failed_cases != 0)) throw new InvalidOperationException("Observed dataset routes contain failing cases: " + output);
    }

    private sealed class Context
    {
        internal QuestEntry Quest; internal QuestDatabase Database; internal uint[] History; internal QuestSchedulerAcceptedQuest[] Active;
        internal SpawnPoint Origin; internal int Level, Race; internal int[] NormalIds, NormalCounts, ItemIds, ItemCounts;
        internal Context(QuestDatabase db, int id, JsonElement evidence)
        {
            Quest = db.Quests.Single(value => value.Id == id);
            // Reuse the same actual prerequisite closure/observation preparation as the full dataset sweep.
            var prepared = typeof(QuestDatasetSimulationRegressionTests).GetMethod("BuildContext", Hidden)!.Invoke(null, new object[] { db, Quest, evidence })!;
            object Field(string name) => prepared.GetType().GetField(name, Hidden)!.GetValue(prepared)!;
            Database = (QuestDatabase)Field("Database"); History = (uint[])Field("Completed"); Active = (QuestSchedulerAcceptedQuest[])Field("ActiveParents");
            Origin = (SpawnPoint)Field("Origin"); Level = (int)Field("Level"); Race = (int)Field("Race");
            NormalIds = (int[])Field("NormalIds"); NormalCounts = (int[])Field("NormalCounts"); ItemIds = (int[])Field("ItemIds"); ItemCounts = (int[])Field("ItemCounts");
        }
        internal bool Pickup(QuestScheduleResult result) => result.Plan.Any(value => value.Quest.Id == Quest.Id && value.Stage == QuestWorkStage.Pickup);
        internal IEnumerable<SpawnPoint> Spawns(int entry, DataType type) =>
            (type == DataType.Creature ? Database.CreatureSpawns : Database.GameObjectSpawns).TryGetValue(entry.ToString(CultureInfo.InvariantCulture), out var values) ? values : Enumerable.Empty<SpawnPoint>();
        internal QuestScheduleResult Plan(QuestItemStarterObservation[]? starters = null, QuestCreatureCreditObservation[]? credits = null,
            Dictionary<int,long>? items = null, bool accepted = false, bool completed = false, bool authority = true,
            bool completeLog = true, bool blocked = false, int? level = null, bool rewarded = false, bool? safe = true,
            SpawnPoint? origin = null, int[]? progress = null)
        {
            var point = origin ?? Origin;
            return QuestScheduler.MaterializeSchedule(Database, new QuestSchedulerSnapshot
            {
                UtcNow = Now, PlayerGuid = 123, PlayerLevel = level ?? Level, PlayerRaceId = Race, PlayerClassId = 2,
                MapId = point.Map, X = point.X, Y = point.Y, Z = point.Z, ItemStarters = starters ?? Array.Empty<QuestItemStarterObservation>(),
                CreatureCredits = credits ?? Array.Empty<QuestCreatureCreditObservation>(), CarriedItemCounts = items ?? new Dictionary<int,long>(),
                HasAuthoritativeCompletions = authority, HasCompleteQuestLog = completeLog,
                CompletedQuestIds = rewarded ? History.Concat(new[] { (uint)Quest.Id }).ToArray() : History,
                AcceptedQuests = accepted ? Active.Concat(new[] { new QuestSchedulerAcceptedQuest { QuestId = (uint)Quest.Id, IsCompleted = completed,
                    ObjectiveCounts = progress ?? new int[4], NormalObjectiveIds = NormalIds, NormalObjectiveRequiredCounts = NormalCounts } }).ToArray() : Active
            }, _ => new QuestRecoveryDecision { State = blocked ? QuestRecoveryState.Quarantined : QuestRecoveryState.Eligible, MayAttempt = !blocked,
                RetryUtc = blocked ? Now.AddMinutes(5) : null }, 50, 250, 80,
                // A known blackspot is independent of the shared cell path-query
                // budget. An unqueried neighbor remains unknown under that budget.
                isKnownUnsafe: safe == false ? _ => true : null,
                navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = safe, IsKnownReachable = true });
        }
    }
    private static void CheckActualAliasTarget(GrindObjective owner, int actor, int credit)
    {
        IntPtr storage = Marshal.AllocHGlobal(4096);
        try
        {
            // This allocation can reuse a prior actor's virtual address. Supply
            // current bytes rather than a previous fixture's cached observation.
            using var cacheState = Styx.WoWInternals.ObjectManager.Wow.TemporaryCacheState(false);
            Marshal.Copy(new byte[4096],0,storage,4096);
            uint address = unchecked((uint)storage.ToInt32());
            void Word(int offset,uint value) => Marshal.WriteInt32(storage,offset,unchecked((int)value));
            Word(8,address+3072); Word(0x14,3); Word(0x30,99001001); Word(3072,99001001); Word(3072+12,(uint)actor);
            Word(2404,address+256); Word(256+28,(uint)credit); Word(256+32,0);
            var unit = new WoWUnit(address);
            Check(unit.GetCachedInfo(out var cache) && cache.GroupID == credit, "actual client cache reader lost alias");
            Check((bool)typeof(GrindObjective).GetMethod("IsMobObjective", Hidden)!.Invoke(owner,new object[] {unit})!, "generic host failed to select the alias actor");
        }
        finally { Marshal.FreeHGlobal(storage); }
    }
    private static void Case(Row row,string name,Action action)
    {
        try { action(); row.passed_cases++; row.cases.Add(new {name,status="PASS"}); }
        catch(Exception error) { row.failed_cases++; row.cases.Add(new {name,status="FAIL",error=error.ToString()}); Console.Error.WriteLine($"FAIL route {row.quest_id} {name}: {error.Message}"); }
    }
    private static string Checkout()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory); directory!=null; directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Checkout required");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool value,string reason) {if(!value)throw new Failure(reason);}
}
