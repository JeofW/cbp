using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Actual source-bound loader, scheduler and materializer contracts. Controlled
// files/snapshots/XML only; no generated profile or game is executed.
internal static class QuestStrategyExecutionRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>
        {
            ("missing strategy pack preserves legacy execution identity", () =>
            {
                using var fixture = new LoaderFixture(includePack:false);
                fixture.Loader.Load();
                Check(ReadString(fixture.Loader, "ExecutionFingerprint") == fixture.Loader.DatasetFingerprint,
                    "missing pack changed legacy execution identity");
                Check(ReadPackStatus(fixture.Loader) == "Missing", "missing pack was not retained explicitly");
            }),
            ("bound pack contributes exact bytes to execution identity without changing dataset identity", () =>
            {
                using var fixture = new LoaderFixture(includePack:true);
                fixture.Loader.Load();
                string dataset = fixture.Loader.DatasetFingerprint;
                string execution = ReadString(fixture.Loader, "ExecutionFingerprint");
                Check(dataset != "unknown" && execution != "unknown" && execution != dataset,
                    "bound strategy bytes did not enter runtime identity");
                string before = dataset;
                File.WriteAllText(fixture.StrategyPath, fixture.CreatePack("controlled-revision-2"), Encoding.UTF8);
                var second = new DataLoader(fixture.DataPath);
                second.Load();
                Check(second.DatasetFingerprint == before,
                    "strategy bytes changed the legacy dataset fingerprint");
                Check(ReadString(second, "ExecutionFingerprint") != execution,
                    "changed strategy bytes did not change execution identity");
            }),
            ("invalid bound pack fails closed before database publication and can be retried", () =>
            {
                using var fixture = new LoaderFixture(includePack:false);
                File.WriteAllText(fixture.StrategyPath,
                    fixture.CreatePack("bad", questSha:new string('0',64)), Encoding.UTF8);
                Throws<InvalidDataException>(() => fixture.Loader.Load(),
                    "mismatched strategy pack was accepted");
                File.WriteAllText(fixture.StrategyPath, fixture.CreatePack("repaired"), Encoding.UTF8);
                Check(fixture.Loader.Load() != null, "loader did not retry after repaired strategy pack");
                Check(ReadPackStatus(fixture.Loader) == "DeclaredAndBound",
                    "repaired pack did not become the published strategy owner");
            }),
            ("UseItemOn recipe replaces generic objective order with authoritative custom behavior", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                string xml = BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack);
                Check(xml.Contains("File=\"UseItemOn\"", StringComparison.Ordinal),
                    "bound UseItemOn recipe did not emit the custom behavior");
                Check(!xml.Contains("Type=\"KillMob\"", StringComparison.Ordinal),
                    "generic kill objective remained executable beside owned UseItemOn recipe");
                Check(xml.Contains("QuestId=\"2118\"", StringComparison.Ordinal)
                    && xml.Contains("ObjectiveIndex=\"0\"", StringComparison.Ordinal)
                    && xml.Contains("ItemId=\"7586\"", StringComparison.Ordinal)
                    && xml.Contains("MobId=\"2164\"", StringComparison.Ordinal)
                    && xml.Contains("SuccessEvidence=\"ObjectiveProgress\"", StringComparison.Ordinal)
                    && xml.Contains("MaxAttempts=\"3\"", StringComparison.Ordinal)
                    && xml.Contains("MobType=\"Npc\"", StringComparison.Ordinal)
                    && xml.Contains("Range=\"5\"", StringComparison.Ordinal)
                    && xml.Contains("RequireLos=\"true\"", StringComparison.Ordinal),
                    "generated behavior lost authoritative recipe identity");
            }),
            ("GameObject item action cannot borrow a creature anchor", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.GameObject);
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "unimplemented GameObject item action borrowed a creature hotspot with the same numeric ID");
            }),
            ("missing strategy pack keeps legacy profile output byte-for-byte", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                string legacy = scenario.Builder.BuildProfileXml(
                    scenario.Plan, scenario.Database, "zone", "player", 20, null);
                string wired = BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, new QuestStrategyPack());
                Check(wired == legacy, "missing strategy pack changed legacy profile XML");
            }),
            ("declared Escort is rejected rather than silently becoming ordinary work", () =>
            {
                var scenario = Scenario(QuestStrategyKind.Escort, QuestStrategyTargetType.Creature);
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "declared unimplemented Escort silently became a KillMob objective");
            }),
            ("recipe ownership is exact quest and objective", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                scenario.Pack.Recipes[0] = CopyRecipe(scenario.Pack.Recipes[0], questId:9999);
                string legacy = scenario.Builder.BuildProfileXml(
                    scenario.Plan, scenario.Database, "zone", "player", 20, null);
                string wired = BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack);
                Check(wired == legacy, "recipe for another quest captured this objective");
            }),
            ("generated behavior carries a finite acknowledgement window", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                string xml = BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack);
                Check(xml.Contains("AcknowledgementTimeout=\"5000\"", StringComparison.Ordinal),
                    "generated authoritative behavior omitted bounded post-submission acknowledgement");
            }),
            ("UseItemOn cannot borrow another creature's objective anchor", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                scenario.Pack.Recipes[0] = CopyRecipe(scenario.Pack.Recipes[0], targetId:9999);
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "item target borrowed an unrelated source objective's hotspot");
            }),
            ("even matching GameObject metadata is not an implemented item protocol", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.GameObject);
                var objective = scenario.Database.Quests[0].Objectives[0];
                objective.Type = ObjectiveType.CollectFromGameObject;
                objective.MobId = 0;
                objective.GameObjectId = 2164;
                objective.ItemId = 12345;
                objective.CollectCount = 3;
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "GameObject item/ground-cursor semantics were inferred from matching target metadata");
            }),
            ("undefined programmatic kind cannot bypass the loader's validation", () =>
            {
                var scenario = Scenario((QuestStrategyKind)98765, QuestStrategyTargetType.Creature);
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "undefined in-memory kind silently became ordinary work");
            }),
            ("cross-kind duplicate owners are rejected at the public materializer", () =>
            {
                var scenario = Scenario(QuestStrategyKind.UseItemOn, QuestStrategyTargetType.Creature);
                scenario.Pack.Recipes.Add(Scenario(QuestStrategyKind.GossipEvent, QuestStrategyTargetType.Creature).Pack.Recipes[0]);
                Throws<InvalidDataException>(() => BuildWithStrategies(scenario.Builder, scenario.Plan, scenario.Database, scenario.Pack),
                    "materializer silently selected one of two recipe owners");
            }),
            ("real loader and scheduler cannot turn a declared Escort into kill XML", () =>
            {
                using var fixture = new LoaderFixture(includePack:false);
                fixture.WritePlanningInput(includeEscort:true, castCredit:false);
                QuestDatabase db = fixture.Loader.Load();
                QuestScheduleResult schedule = Schedule(db);
                Check(schedule.Plan.Count == 1, "controlled ordinary-shaped objective did not reach materialization");
                Throws<InvalidDataException>(() => new ProfileBuilder().BuildProfileXml(
                    schedule.Plan, db, "zone", "player", 20, null, fixture.Loader.StrategyPack),
                    "the actual loader/scheduler/materializer pipeline produced ordinary work for declared Escort");
            }),
            ("real no-pack scheduler and XML retain ordinary work", () =>
            {
                using var fixture = new LoaderFixture(includePack:false);
                fixture.WritePlanningInput(includeEscort:false, castCredit:false);
                QuestDatabase db = fixture.Loader.Load();
                QuestScheduleResult schedule = Schedule(db);
                Check(schedule.Plan.Count == 1, "ordinary control was not scheduled");
                var builder = new ProfileBuilder();
                string xml = builder.BuildProfileXml(schedule.Plan, db, "zone", "player", 20, null, fixture.Loader.StrategyPack);
                Check(xml == builder.BuildProfileXml(schedule.Plan, db, "zone", "player", 20, null)
                    && xml.Contains("Type=\"KillMob\"", StringComparison.Ordinal),
                    "safe ordinary no-pack scheduling/materialization changed");
            }),
            ("unsupported CAST work remains excluded without ordinary killing", () =>
            {
                using var fixture = new LoaderFixture(includePack:false);
                fixture.WritePlanningInput(includeEscort:true, castCredit:true);
                QuestDatabase db = fixture.Loader.Load();
                Check(Schedule(db).Plan.Count == 0,
                    "unsupported cast-credit objective became ordinary killing");
            })
        };

        int passed=0, assertions=0, unexpected=0;
        foreach(var c in cases)
        {
            try { c.Test(); passed++; Console.WriteLine("PASS quest strategy execution: "+c.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL quest strategy execution assertion: "+c.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR quest strategy execution fixture/owner: "+c.Name+": "+e); }
        }
        Console.WriteLine($"Quest strategy execution scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; controlled actual loader/scheduler/XML; no profile/client/server execution.");
        if(assertions+unexpected!=0) throw new InvalidOperationException("Quest strategy execution regression");
    }

    private static QuestScheduleResult Schedule(QuestDatabase db) => QuestScheduler.MaterializeSchedule(
        db,
        new QuestSchedulerSnapshot
        {
            UtcNow = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            PlayerLevel = 20, PlayerRaceId = 1, MapId = 1, X = 10, Y = 20,
            HasCompleteQuestLog = true, HasAuthoritativeCompletions = true,
            AcceptedQuests = new[] { new QuestSchedulerAcceptedQuest { QuestId = 2118, ObjectiveCounts = new[] { 0 } } },
            CarriedItemCounts = new Dictionary<int, long>()
        },
        _ => new QuestRecoveryDecision { MayAttempt = true },
        maximum: 5, scanThreshold: 500, minQuestLevelOffset: 10);

    private sealed class LoaderFixture : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"cb-strategy-exec-"+Guid.NewGuid().ToString("N"));
        internal readonly string DataPath;
        internal readonly string StrategyPath;
        internal readonly string DataText;
        internal readonly DataLoader Loader;

        internal LoaderFixture(bool includePack)
        {
            Directory.CreateDirectory(Root);
            DataPath=Path.Combine(Root,"quest_data.json");
            StrategyPath=Path.Combine(Root,"quest_strategies.json");
            DataText="{\"Quests\":[],\"QuestGivers\":[],\"QuestEnders\":[],\"CreatureSpawns\":{},\"GameObjectSpawns\":{}}";
            File.WriteAllText(DataPath,DataText,Encoding.UTF8);
            if(includePack) File.WriteAllText(StrategyPath,CreatePack("controlled-revision"),Encoding.UTF8);
            Loader=new DataLoader(DataPath);
        }

        internal string CreatePack(string revision,string? questSha=null)
        {
            string sha=questSha ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DataPath))).ToLowerInvariant();
            return "{"+
                "\"Schema\":\"quest-strategy-pack-335-v1\","+
                "\"ClientBuild\":12340,"+
                "\"QuestDataSha256\":\""+sha+"\","+
                "\"SourceKind\":\"curated-profile\","+
                "\"SourceRevision\":\""+revision+"\","+
                "\"Recipes\":[]}";
        }

        internal void WritePlanningInput(bool includeEscort, bool castCredit)
        {
            var scenario = Scenario(QuestStrategyKind.Escort, QuestStrategyTargetType.Creature);
            scenario.Database.Quests[0].SpecialFlags = castCredit ? 0x20 : 0;
            scenario.Database.CreatureSpawns["2164"] = new List<SpawnPoint>
            {
                new SpawnPoint { Map = 1, X = 10, Y = 20, Z = 30, IsKnownReachable = true, IsKnownSafe = true }
            };
            File.WriteAllText(DataPath, JsonSerializer.Serialize(scenario.Database), Encoding.UTF8);
            if (!includeEscort) return;
            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DataPath))).ToLowerInvariant();
            File.WriteAllText(StrategyPath, JsonSerializer.Serialize(new
            {
                Schema = "quest-strategy-pack-335-v1", ClientBuild = 12340,
                QuestDataSha256 = sha, SourceKind = "curated-profile", SourceRevision = "controlled-pipeline",
                Recipes = new[] { new {
                    QuestId = 2118, ObjectiveIndex = 0, Kind = "Escort", SourceRef = "controlled://escort",
                    TargetType = "Creature", TargetId = 2164, Range = 5, RequireLos = true,
                    MaxAttempts = 3, SuccessEvidence = "QuestComplete"
                } }
            }), Encoding.UTF8);
        }

        public void Dispose()
        {
            QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
            Directory.Delete(Root,true);
        }
    }

    private sealed record StrategyScenario(
        ProfileBuilder Builder,
        List<QuestPlanEntry> Plan,
        QuestDatabase Database,
        QuestStrategyPack Pack);

    private static StrategyScenario Scenario(QuestStrategyKind kind, QuestStrategyTargetType targetType)
    {
        var quest=new QuestEntry
        {
            Id=2118,
            Name="Controlled",
            Objectives=new List<QuestObjective>
            {
                new QuestObjective{Index=0,Type=ObjectiveType.KillMob,MobId=2164,KillCount=1}
            }
        };
        var plan=new List<QuestPlanEntry>
        {
            new QuestPlanEntry
            {
                Quest=quest,
                Stage=QuestWorkStage.Objective,
                ObjectiveIndex=0,
                Hotspots=new[]{new SpawnPoint{Map=1,X=10,Y=20,Z=30}}
            }
        };
        var db=new QuestDatabase{Quests=new List<QuestEntry>{quest}};
        var recipe=new QuestStrategyRecipe
        {
            QuestId=2118,
            ObjectiveIndex=0,
            Kind=kind,
            SourceRef="controlled://strategy/2118/0",
            ItemId=7586,
            TargetType=targetType,
            TargetId=2164,
            TargetState=QuestStrategyTargetState.Alive,
            Range=5,
            RequireLos=true,
            MaxAttempts=3,
            GossipOptionIndex=1,
            SuccessEvidence=QuestStrategySuccessEvidence.ObjectiveProgress
        };
        var pack=new QuestStrategyPack
        {
            Status=QuestStrategyPackStatus.DeclaredAndBound,
            ClientBuild=12340,
            QuestDataSha256=new string('a',64),
            SourceKind="curated-profile",
            SourceRevision="controlled",
            Recipes=new List<QuestStrategyRecipe>{recipe}
        };
        return new StrategyScenario(new ProfileBuilder(),plan,db,pack);
    }

    private static QuestStrategyRecipe CopyRecipe(QuestStrategyRecipe value,int? questId=null,int? targetId=null) =>
        new QuestStrategyRecipe
        {
            QuestId=questId ?? value.QuestId,
            ObjectiveIndex=value.ObjectiveIndex,
            Kind=value.Kind,
            SourceRef=value.SourceRef,
            ItemId=value.ItemId,
            TargetType=value.TargetType,
            TargetId=targetId ?? value.TargetId,
            TargetState=value.TargetState,
            Range=value.Range,
            RequireLos=value.RequireLos,
            MaxAttempts=value.MaxAttempts,
            GossipOptionIndex=value.GossipOptionIndex,
            SuccessEvidence=value.SuccessEvidence
        };

    private static string BuildWithStrategies(
        ProfileBuilder builder,
        IReadOnlyList<QuestPlanEntry> plan,
        QuestDatabase db,
        QuestStrategyPack pack)
    {
        MethodInfo? method=typeof(ProfileBuilder).GetMethods(BindingFlags.Public|BindingFlags.Instance)
            .SingleOrDefault(m=>m.Name=="BuildProfileXml" && m.GetParameters().Length==7);
        if(method==null) throw new AssertionFailure("strategy-aware BuildProfileXml overload is missing");
        try
        {
            return (string)(method.Invoke(builder,new object?[]{plan,db,"zone","player",20,null,pack})
                ?? throw new AssertionFailure("strategy-aware profile builder returned null"));
        }
        catch(TargetInvocationException e) when(e.InnerException!=null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    private static string ReadString(object owner,string property)
    {
        PropertyInfo? info=owner.GetType().GetProperty(property,BindingFlags.Public|BindingFlags.Instance);
        if(info==null) throw new AssertionFailure(property+" property is missing");
        return Convert.ToString(info.GetValue(owner),System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static string ReadPackStatus(DataLoader loader)
    {
        PropertyInfo? info=loader.GetType().GetProperty("StrategyPack",BindingFlags.Public|BindingFlags.Instance);
        if(info==null) throw new AssertionFailure("StrategyPack property is missing");
        object? pack=info.GetValue(loader);
        if(pack==null) throw new AssertionFailure("StrategyPack is null");
        return Convert.ToString(pack.GetType().GetProperty("Status")?.GetValue(pack)) ?? "";
    }

    private static void Throws<T>(Action action,string reason) where T:Exception
    {
        try { action(); } catch(T) { return; }
        throw new AssertionFailure(reason);
    }

    private static void Check(bool ok,string why)
    {
        if(!ok) throw new AssertionFailure(why);
    }
}
