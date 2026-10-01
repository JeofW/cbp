using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

// Actual shipped scheduler/profile/objective/native-credit owners connect to the
// complete direct-use state machine, then actual turn-in/stock Lua5.1 owners.
// Native world/collision requests and separately supplied server replies remain
// controlled. Exact member traces are emitted; no family is promoted by counts.
internal static class DirectGameObjectPipelineRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Probe : IDisposable
    {
        private readonly Type driver;
        internal Probe() => driver = DirectGameObjectExecutionRegressionTests.CompileProbe(false).GetType("DirectGoCoupledDriver", true)!;
        internal object? Call(string method, params object[] args)
        {
            try { return driver.GetMethod(method)!.Invoke(null, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        internal int Count(string name) => (int)Call(name)!;
        internal bool Done => (bool)Call("Done")!;
        public void Dispose() => Call("Stop");
    }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string sourcePath = Path.Combine(root, "docs/audit/2026-10-01/direct-gameobject/family-source.json");
        using var source = JsonDocument.Parse(File.ReadAllBytes(sourcePath));
        var candidates = source.RootElement.GetProperty("candidate_replay_ids").EnumerateArray().Select(row => row.GetUInt32()).ToArray();
        Check(candidates.SequenceEqual(new uint[] { 953, 1043, 2988, 8345, 10874, 11900, 11965, 12559, 12613, 13034, 13084 }),
            "Source-bound candidate membership changed; review exact family obligations before extending this replay");
        string dataDirectory = Path.Combine(root, "runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data");
        string dataFile = Path.Combine(dataDirectory, "quest_data.json");
        Check(Hash(dataFile) == source.RootElement.GetProperty("shipped_dataset_sha256").GetString(), "Shipped data differs from the primitive receipt");
        Console.WriteLine("DIRECT_PIPELINE_SOURCE " + Hash(sourcePath));
        foreach (string name in new[] { "quest_data.json", "quest_data.repairs.json", "quest_strategies.json", "quest_knowledge_manifest.json" })
            Console.WriteLine("DIRECT_PIPELINE_INPUT " + name + " sha256=" + Hash(Path.Combine(dataDirectory, name)));
        using var probe = new Probe();
        using var turnin = new QuestTurnInExecutionRegressionTests.TurnInExecutionFixture();
        int quests = 0, objectiveCount = 0, dispatchCount = 0;
        foreach (uint id in candidates)
        {
            var primary = source.RootElement.GetProperty("quests").EnumerateArray().Single(row => row.GetProperty("quest_id").GetUInt32() == id);
            Check(primary.GetProperty("additional_source_obligations").GetArrayLength() == 0 && primary.GetProperty("replay_candidate").GetBoolean(),
                "An unresolved script or extra primitive was silently added to the ordinary-use replay");
            var result = RunQuest(id, primary, dataFile, probe, turnin);
            quests++; objectiveCount += result.Objectives; dispatchCount += result.Dispatches;
        }
        Console.WriteLine($"Direct GameObject coupled pipeline: {quests}/{candidates.Length} exact shipped quests; {objectiveCount} source-matched objective owners; {dispatchCount} owned requests with separate native-credit observations, full turn-in acknowledgement and next scheduling. World/collision/server observations controlled; no live acceptance or automatic execution-ledger promotion.");
    }

    private static (int Objectives, int Dispatches) RunQuest(uint id, JsonElement primary, string dataFile, Probe probe,
        QuestTurnInExecutionRegressionTests.TurnInExecutionFixture turnin)
    {
        using var observations = new QuestInventorySnapshotRegressionTests.Fixture();
        var game = observations.NativeFixture;
        var database = new DataLoader(dataFile).Load();
        var quest = database.Quests.Single(row => row.Id == id);
        database.Quests = new() { quest };
        int[] normal = new int[4], required = new int[4], progress = new int[4];
        foreach (var row in primary.GetProperty("normal_objectives").EnumerateArray())
        {
            int slot = row.GetProperty("slot").GetInt32(), entry = row.GetProperty("typed_entry").GetInt32();
            Check(entry < 0, "Ordinary direct-use fixture cannot borrow creature credit");
            normal[slot] = unchecked((int)0x80000000) | -entry;
            required[slot] = row.GetProperty("count").GetInt32();
        }
        game.SetQuest(id, quest.Name, 80, normal, required, new int[6], new int[6]);
        var first = quest.Objectives.First();
        var start = database.GameObjectSpawns[first.GameObjectId.ToString()].First();
        uint map = (uint)start.Map;
        double x = start.X, y = start.Y, z = start.Z;
        var settings = new WholesomeAQSettings();
        var scheduler = new QuestScheduler(new DataLoader(dataFile), new ProfileBuilder(), settings);
        var events = new List<object>(); int sequence = 0, totalRequests = 0, completedObjectives = 0;
        void Record(string stage, int? entry = null, int? before = null, int? after = null, string? action = null,
            string? detail = null) => events.Add(new { sequence = sequence++, quest_id = id, stage, entry,
                before, after, action_id = action, detail, observed_map = map, scheduler_snapshot_coordinates = new[] { x, y, z } });
        QuestScheduleResult Schedule()
        {
            var inventory = (QuestInventorySnapshot)observations.Capture();
            Check(inventory.IsComplete && inventory.IsCurrent(), "Actual inventory observation is unavailable");
            bool accepted = observations.Player.QuestLog.ContainsQuest(id);
            QuestDescriptorData state = default;
            if (accepted) Check(((Quest)game.Quest).GetData(out state), "Actual native descriptor observation is missing");
            Check(observations.Player.QuestLog.TryGetAuthoritativeCompletedQuests(out var history), "Actual rewarded-history observation is unavailable");
            return QuestScheduler.MaterializeSchedule(database, new QuestSchedulerSnapshot {
                UtcNow = DateTime.UtcNow, PlayerGuid = observations.Player.Guid, PlayerLevel = 80, PlayerRaceId = 10,
                PlayerClassId = 2, MapId = (int)map, X = x, Y = y, Z = z, HasCompleteQuestLog = true,
                HasAuthoritativeCompletions = true, CompletedQuestIds = history.ToArray(),
                AcceptedQuests = accepted ? new[] { new QuestSchedulerAcceptedQuest { QuestId = id,
                    IsCompleted = state.IsCompleted, IsFailed = state.IsFailed,
                    ObjectiveCounts = state.ObjectivesDone.Select(value => (int)value).ToArray(),
                    NormalObjectiveIds = normal, NormalObjectiveRequiredCounts = required } } : Array.Empty<QuestSchedulerAcceptedQuest>(),
                CarriedItemCounts = inventory.ItemCounts, InventoryObservationStatus = inventory.Status,
                RawQuestStates = new Dictionary<uint, int> { [id] = !accepted ? 0 : state.IsCompleted ? 1 : 3 },
                SkillValues = new Dictionary<int, int>(), ReputationValues = new Dictionary<int, int>() },
                _ => new QuestRecoveryDecision { State = QuestRecoveryState.Eligible, MayAttempt = true },
                50, scheduler.ScanThreshold, 80,
                navigationAssessment: _ => new SpawnNavigationAssessment { IsKnownSafe = true, IsKnownReachable = true });
        }
        QuestScheduleResult ExpandedSchedule()
        {
            var value = Schedule();
            for (int attempts = 0; value.Plan.Count == 0 && scheduler.ScanThreshold < settings.ScanMaxDistance && attempts < 16; attempts++)
            {
                int old = scheduler.ScanThreshold;
                typeof(QuestScheduler).GetMethod("ApplyScanExpansionBeforeFallback", Hidden)!.Invoke(scheduler, new object[] { value });
                if (scheduler.ScanThreshold == old) break;
                Record("bounded-scan-expansion", detail: old + "->" + scheduler.ScanThreshold);
                value = Schedule();
            }
            return value;
        }
        string Profile(QuestScheduleResult schedule) => new ProfileBuilder().BuildProfileXml(schedule.Plan, database,
            "Pinned direct-object replay", "Controlled observations", 80);
        Record("admission", detail: "existing accepted quest and separately observed native metadata");
        int expectedObjectives = normal.Count(value => value != 0);
        var handled = new HashSet<int>();
        for (int objectiveAttempt = 0; handled.Count < expectedObjectives && objectiveAttempt < 8; objectiveAttempt++)
        {
            var schedule = ExpandedSchedule();
            Check(schedule.Plan.Any(row => row.Quest.Id == id && row.Stage == QuestWorkStage.Objective),
                "No executable objective was scheduled for quest" + id + ": " + schedule.Status);
            string profile = Profile(schedule); game.LoadProfile(profile);
            var generated = XDocument.Parse(profile).Descendants("QuestOrder").Single().Descendants("Objective").ToArray();
            Check(generated.Length > 0, "The generated plan contains no executable objective");
            ForcedQuestObjective? behavior = null; UseGameObjectObjective? use = null;
            foreach (var element in generated)
            {
                var node = OrderNodeCollection.FromXml(new XElement("QuestOrder", new XElement(element))).OfType<ObjectiveNode>().Single();
                var candidate = game.CreateObjective(node);
                Check(candidate.Objective is UseGameObjectObjective, "The generated action is not an actual direct GameObject owner");
                var typed = (UseGameObjectObjective)candidate.Objective;
                if (!handled.Contains(typed.Objective.ID) && !candidate.IsDone) { behavior = candidate; use = typed; break; }
                candidate.Dispose();
            }
            Check(behavior != null && use != null, "Generated work contains no remaining matching typed objective");
            using (behavior!)
            {
                int entry = use!.Objective.ID;
                int slot = Array.IndexOf(normal, unchecked((int)0x80000000) | entry);
                Check(slot >= 0 && use.Objective.Count == required[slot], "Generated objective differs from pinned entry/count semantics");
                var point = database.GameObjectSpawns[entry.ToString()].First(p => p.Map == map);
                Check(primary.GetProperty("primary_spawns").EnumerateArray().Any(p => p.GetProperty("id").GetInt32() == entry
                    && p.GetProperty("map").GetInt32() == map
                    && Math.Abs(p.GetProperty("position_x").GetDouble() - point.X) < 0.05
                    && Math.Abs(p.GetProperty("position_y").GetDouble() - point.Y) < 0.05
                    && Math.Abs(p.GetProperty("position_z").GetDouble() - point.Z) < 0.05), "Runtime point differs from pinned reference geometry");
                Record("profile-action", entry, detail: "actual generated UseGameObjectObjective; slot=" + slot);
                string? issued = null; int attempted = 0;
                using var loaded = new LoadedObject(use, observations, new WoWPoint((float)point.X, (float)point.Y, (float)point.Z));
                int? ReadProgress() => QuestObjectiveCompletion.TryReadTypedNormalObjectiveProgress(game.Quest,
                    normal[slot], required[slot], out int count) ? count : null;
                void DispatchEvent(string request)
                {
                    Check(request.StartsWith("interaction:", StringComparison.Ordinal), "Unexpected primitive request");
                    ulong guid = ulong.Parse(request.Substring("interaction:".Length));
                    Check(loaded.Matches(entry, guid), "Actual allocated-descriptor source predicate rejected the selected object");
                    attempted++; totalRequests++; issued = id + ":" + slot + ":" + attempted + ":" + guid;
                    Record("action-dispatch", entry, ReadProgress(), action: issued, detail: "local native boundary request; no progress reply yet");
                }
                ulong firstGuid = ((ulong)id << 32) | ((uint)slot << 16) | 1u;
                // A map number does not establish riding skill or local flight
                // permission. Keep source-coordinate chains grounded; separate
                // adversarial owner cases supply explicit flying observations.
                probe.Call("Begin", id, entry, required[slot], slot, map, point.X, point.Y, point.Z,
                    firstGuid, (Func<int?>)ReadProgress, (Action<string>)DispatchEvent, false);
                probe.Call("Tick");
                Check(probe.Count("GroundMoves") == 1 && attempted == 0 && !behavior!.IsDone,
                    "Travel generated interaction or quest progress");
                Record("travel", entry, detail: "actual ground-approach owner issued navigation; no reply or credit yet");
                Check(probe.Count("Flights") == 0 && probe.Count("Descents") == 0,
                    "Ground source-chain travel requested an unobserved flight capability");
                probe.Call("GroundReply");
                for (int count = 0; count < required[slot]; count++)
                {
                    if (count > 0) probe.Call("Reacquire", firstGuid + (ulong)count);
                    Record("live-acquisition-safe-approach", entry, detail: "source-matched candidate and separately supplied ground/unmounted observation");
                    probe.Call("Tick");
                    Record("owner-observation", entry, detail: (string)probe.Call("Snapshot")!);
                    Check(attempted == count + 1 && ReadProgress() == count && !behavior!.IsDone,
                        "Action/credit mismatch: quest=" + id + ",entry=" + entry + ",expectedRequests=" + (count + 1)
                        + ",actualRequests=" + attempted + ",progress=" + ReadProgress() + ",forcedDone=" + behavior!.IsDone
                        + ",owner=" + probe.Call("Snapshot"));
                    for (int wait = 0; wait < 2; wait++) { probe.Call("Advance", 1.0); probe.Call("Tick"); }
                    Check(attempted == count + 1 && !probe.Done && !behavior.IsDone,
                        "Pending use repeated or acknowledged itself");
                    Check(Schedule().Plan.Any(row => row.Stage == QuestWorkStage.Objective), "Unacknowledged use removed remaining work");
                    Record("waiting-for-acknowledgement", entry, count, count, issued);
                    // This is a separate externally supplied server-credit reply,
                    // delivered only after the actual owner request was observed.
                    progress[slot] = count + 1; game.SetProgress(progress);
                    Check(ReadProgress() == count + 1 && behavior.IsDone == (count + 1 == required[slot]),
                        "Actual native credit/forced objective did not consume the independent reply");
                    Record("authoritative-progress", entry, count, count + 1, issued,
                        "actual original-client typed counter reader; controlled server reply");
                    probe.Call("Tick");
                    Check(probe.Done == behavior.IsDone, "Complete direct owner disagrees with actual forced/native objective completion");
                }
                Record("objective-completion", entry, required[slot], required[slot], issued);
                probe.Call("Stop");handled.Add(entry);completedObjectives++;
                x = point.X; y = point.Y; z = point.Z;
                Check(!Schedule().Plan.Any(row => row.Stage == QuestWorkStage.TurnIn), "Normal counters invented the separate server-completed flag");
            }
        }
        Check(handled.Count == expectedObjectives, "Not all required direct-use primitives executed");
        game.SetAccepted(true, complete: true);Record("server-completed-state", detail: "independent server-ready observation");
        var complete = ExpandedSchedule();
        Check(complete.Plan.Any(row => row.Stage == QuestWorkStage.TurnIn) && !complete.Plan.Any(row => row.Stage == QuestWorkStage.Objective),
            "Actual completed quest did not schedule its ender: " + complete.Status);
        var generatedTurnin = XDocument.Parse(Profile(complete)).Descendants("TurnIn").Single(row => (uint)row.Attribute("QuestId")! == id);
        uint ender = (uint)generatedTurnin.Attribute("TurnInId")!;
        Check(primary.GetProperty("primary_enders").EnumerateArray().Any(row => row.GetProperty("type").GetString() == "Creature"
            && row.GetProperty("id").GetUInt32() == ender), "Generated ender is not source-backed");
        Record("ender-plan", detail: generatedTurnin.ToString(SaveOptions.DisableFormatting));
        turnin.ExecuteBound(generatedTurnin, false, id, ender,
            () => { game.SetAccepted(false); game.SetHistory(new[] { id }); Record("turnin-acknowledgement", detail: "independent accepted-log removal and authoritative rewarded history"); },
            () => { Check(observations.Player.QuestLog.ContainsQuest(id) && Schedule().Plan.Any(row => row.Stage == QuestWorkStage.TurnIn),
                        "Reward submission acknowledged itself"); Record("turnin-dispatch", detail: "real turn-in/frame/Lua owners submitted once; history unchanged"); }, map);
        Check(observations.Player.QuestLog.TryGetAuthoritativeCompletedQuests(out var historyIds) && historyIds.Contains(id),
            "Reward acknowledgement did not reach the actual history reader");
        Check(!Schedule().Plan.Any(row => row.Quest.Id == id), "Acknowledged quest was rescheduled");
        Record("next-scheduling", detail: "rewarded quest no longer scheduled");
        Console.WriteLine("DIRECT_PIPELINE_TRACE " + JsonSerializer.Serialize(new { quest_id = id, source_bound = true,
            required_objectives = completedObjectives, actual_requests = totalRequests, events,
            collision_and_native_requests_controlled = true, server_replies_separate = true, live_completion_proven = false }));
        return (completedObjectives, totalRequests);
    }

    private sealed class LoadedObject : IDisposable
    {
        private readonly IntPtr memory = Marshal.AllocHGlobal(4096);
        private readonly UseGameObjectObjective owner;
        private readonly QuestInventorySnapshotRegressionTests.Fixture fixture;
        private readonly WoWPoint location;
        private readonly List<ulong> added = new();
        internal LoadedObject(UseGameObjectObjective value, QuestInventorySnapshotRegressionTests.Fixture live, WoWPoint point)
        {
            owner=value;fixture=live;location=point;Marshal.Copy(new byte[4096],0,memory,4096);
            Marshal.WriteInt32(memory,8,unchecked((int)((uint)memory.ToInt32()+512)));
            Marshal.WriteInt32(memory,20,5);
            // GetObjectsOfType<WoWGameObject>() intentionally excludes subclasses.
            // Populate the actual position fields rather than overriding Location
            // on a synthetic subtype that the real object manager cannot select.
            foreach (var axis in new[] { ("X",point.X), ("Y",point.Y), ("Z",point.Z) })
            {
                var field=typeof(WoWGameObject).GetField("GO_POSITION_"+axis.Item1+"_OFFSET",BindingFlags.Static|BindingFlags.NonPublic)!;
                int offset=Convert.ToInt32(field.GetRawConstantValue());
                Marshal.WriteInt32(memory,offset,BitConverter.SingleToInt32Bits(axis.Item2));
            }
        }
        internal bool Matches(int entry, ulong guid)
        {
            foreach(ulong previous in added)fixture.Objects.Remove(previous);added.Clear();
            Marshal.WriteInt64(memory,48,unchecked((long)guid));Marshal.WriteInt64(memory,512,unchecked((long)guid));
            Marshal.WriteInt32(memory,512+12,entry);
            using(ObjectManager.Wow!.TemporaryCacheState(false))
            {
                var subject=new WoWGameObject(unchecked((uint)memory.ToInt32()));
                Check(subject.Location==location,"Actual object position fields differ from the source geometry");
                fixture.Objects[guid]=subject;added.Add(guid);
                return (bool)typeof(UseGameObjectObjective).GetMethod("IsCurrentGameObject",Hidden)!.Invoke(owner,new object[]{subject})!;
            }
        }
        public void Dispose(){foreach(ulong guid in added)fixture.Objects.Remove(guid);Marshal.FreeHGlobal(memory);}
    }
    private static string Root(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;throw new InvalidOperationException("Tracked checkout required");}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
