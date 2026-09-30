using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Bots.Quest.QuestOrder;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Explicitly invoked dataset test, not an aggregate initializer: the source pack
// and its exact primary observations must be supplied. Real behavior lifecycle
// and raw progress readers run; native item use and server effects do not.
internal static class QuestVettedStrategyPipelineRegressionTests
{
    private const BindingFlags H=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private sealed class Failure(string message):Exception(message) { }
    internal static void Run()=>QuestClosureIsolation.Run(typeof(QuestVettedStrategyPipelineRegressionTests),nameof(RunIsolated));
    internal static void RunIsolated()
    {
        string data=Environment.GetEnvironmentVariable("CB_QUEST_SIM_DATASET") ?? throw new InvalidOperationException("dataset required");
        string observations=Environment.GetEnvironmentVariable("CB_QUEST_SIM_OBSERVATIONS") ?? throw new InvalidOperationException("primary observations required");
        string output=Environment.GetEnvironmentVariable("CB_QUEST_STRATEGY_SIM_OUTPUT") ?? throw new InvalidOperationException("output required");
        if(File.Exists(output))throw new IOException("Strategy evidence is create-only");
        using var scope=new QuestDataRepairPackRegressionTests.Fixture();
        var loader=new DataLoader(data);var original=loader.Load();var pack=loader.StrategyPack;
        if(pack.SchemaVersion!=2 || pack.Status!=QuestStrategyPackStatus.DeclaredAndBound || pack.Recipes.Count==0)throw new InvalidDataException("Bound v2 source recipes required");
        var inputs=File.ReadLines(observations).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).ToDictionary(r=>r.GetProperty("quest_id").GetInt32());
        string sourceRoot=QuestTypedStrategyBehaviorRegressionTests.Root(),temporary=Path.Combine(Path.GetTempPath(),"cb-vetted-strategy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        bool logging=Styx.Helpers.Logging.FileLogging;Styx.Helpers.Logging.FileLogging=false;
        var records=new List<object>();int total=0,failed=0;
        try
        {
            File.Copy(Path.Combine(sourceRoot,"runtime-snapshot/Quest Behaviors/UseItemOn.cs"),Path.Combine(temporary,"UseItemOn.cs"));
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{temporary})!;
            var compilation=(CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=compilation.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Real source behavior compilation: "+string.Join(";",errors.Select(e=>e.ToString())));
            Type behavior=((Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!).GetType("Styx.Bot.Quest_Behaviors.UseItemOn.UseItemOn",true)!;
            foreach(int id in pack.Recipes.Select(r=>r.QuestId).Distinct().OrderBy(x=>x))
            {
                int passed=0,errorsForQuest=0;var cases=new List<object>();var profiles=new List<string>();
                void Case(string name,Action action)
                {
                    total++;try{action();passed++;cases.Add(new{name,status="PASS"});Console.WriteLine($"PASS vetted strategy {id}: {name}");}
                    catch(Exception error){errorsForQuest++;failed++;cases.Add(new{name,status="FAIL",error=error.ToString()});Console.Error.WriteLine($"FAIL vetted strategy {id}: {name}: {error}");}
                }
                var q=original.Quests.Single(x=>x.Id==id);JsonElement raw=inputs[id];
                object context=typeof(QuestDatasetSimulationRegressionTests).GetMethod("BuildContext",H)!.Invoke(null,new object[]{original,q,raw,true})!;
                T Field<T>(string name)=>(T)context.GetType().GetField(name,H)!.GetValue(context)!;
                var db=Field<QuestDatabase>("Database");var origin=Field<SpawnPoint>("Origin");var progress=new int[4];var counts=Field<int[]>("NormalCounts");var ids=Field<int[]>("NormalIds");
                var stock=new Dictionary<int,long>();
                foreach(var r in pack.Recipes.Where(r=>r.QuestId==id))stock[r.ItemId]=Math.Max(stock.TryGetValue(r.ItemId,out long old)?old:0,1);
                if(q.SupplementalSupply!=null)stock[q.SupplementalSupply.ItemId]=q.SupplementalSupply.ProvidedCount;
                using var fixture=new QuestDatasetObservationFixture();fixture.SetQuest((uint)id,q.Name,Field<int>("Level"),ids,counts,Field<int[]>("ItemIds"),Field<int[]>("ItemCounts"));fixture.SetRaceClass(Field<int>("Race"),Field<int>("ClassId"));
                fixture.SetHistory(Field<uint[]>("Completed"));fixture.SetInventory(stock);
                QuestScheduleResult Schedule(bool accepted=false,bool complete=false,bool failedQuest=false,bool authority=true,bool log=true,bool full=false,bool rewarded=false,bool allow=true,bool safe=true,bool reachable=true,SpawnPoint? point=null,QuestStrategyPack? suppliedPack=null)
                {
                    var active=new List<QuestSchedulerAcceptedQuest>(Field<QuestSchedulerAcceptedQuest[]>("ActiveParents"));
                    if(accepted)active.Add(new QuestSchedulerAcceptedQuest{QuestId=(uint)id,IsCompleted=complete,IsFailed=failedQuest,NormalObjectiveIds=ids,NormalObjectiveRequiredCounts=counts,ObjectiveCounts=progress.ToArray()});
                    if(full)while(active.Count<25)active.Add(new QuestSchedulerAcceptedQuest{QuestId=(uint)(9980000+active.Count)});
                    var p=point??origin;
                    var snapshot=new QuestSchedulerSnapshot{UtcNow=new DateTime(2026,9,29,12,0,0,DateTimeKind.Utc),PlayerLevel=Field<int>("Level"),PlayerRaceId=Field<int>("Race"),PlayerClassId=Field<int>("ClassId"),
                        PlayerGuid=fixture.Player.Guid,MapId=p.Map,X=p.X,Y=p.Y,Z=p.Z,SkillValues=Field<IReadOnlyDictionary<int,int>>("Skills"),ReputationValues=Field<IReadOnlyDictionary<int,int>>("Reputations"),
                        HasAuthoritativeCompletions=authority,HasCompleteQuestLog=log,AcceptedQuests=active,CarriedItemCounts=stock,
                        CompletedQuestIds=rewarded?Field<uint[]>("Completed").Concat(new[]{(uint)id}).ToArray():Field<uint[]>("Completed")};
                    // A bounded native probe covers one exact point per cell,
                    // not every alternative. Negative scenarios provide explicit
                    // vetoes on all target points instead of pretending one probe
                    // established universal unreachability/safety.
                    Dictionary<string,List<SpawnPoint>> Veto(Dictionary<string,List<SpawnPoint>> points)=>points.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(v=>new SpawnPoint
                        {Map=v.Map,X=v.X,Y=v.Y,Z=v.Z,IsKnownSafe=safe?v.IsKnownSafe:false,IsKnownReachable=reachable?v.IsKnownReachable:false,SafetyScore=v.SafetyScore}).ToList());
                    var observedDb=safe&&reachable?db:new QuestDatabase{Quests=db.Quests,QuestGivers=db.QuestGivers,QuestEnders=db.QuestEnders,DependencyMetadata=db.DependencyMetadata,
                        CreatureSpawns=Veto(db.CreatureSpawns),GameObjectSpawns=Veto(db.GameObjectSpawns)};
                    try{return (QuestScheduleResult)typeof(QuestScheduler).GetMethod("MaterializeScheduleCore",H)!.Invoke(null,new object?[]{observedDb,snapshot,
                        (Func<QuestRecoveryKey,QuestRecoveryDecision>)(_=>new QuestRecoveryDecision{MayAttempt=allow,State=allow?QuestRecoveryState.Eligible:QuestRecoveryState.Quarantined}),50,250,80,null,null,null,null,
                        (Func<SpawnPoint,SpawnNavigationAssessment>)(_=>new SpawnNavigationAssessment{IsKnownSafe=safe,IsKnownReachable=reachable}),null,suppliedPack??pack})!;}
                    catch(TargetInvocationException e) when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
                }
                bool Work(QuestScheduleResult result)=>result.Plan.Any(p=>p.Quest.Id==id);
                string Xml(QuestScheduleResult result)
                {
                    string xml=new ProfileBuilder().BuildProfileXml(result.Plan,db,"Fixture","Fixture",Field<int>("Level"),null,pack);profiles.Add(Hash(System.Text.Encoding.UTF8.GetBytes(xml)));fixture.LoadProfile(xml);return xml;
                }
                Case("known source-bound recipe admits pickup",()=>Check(Schedule().Plan.Any(p=>p.Quest.Id==id&&p.Stage==QuestWorkStage.Pickup),"pickup absent"));
                Case("unknown history retains pickup hold",()=>Check(!Work(Schedule(authority:false)),"history bypass"));
                Case("incomplete log retains pickup hold",()=>Check(!Work(Schedule(log:false)),"raw log bypass"));
                Case("full log retains pickup hold",()=>Check(!Work(Schedule(full:true)),"capacity bypass"));
                Case("missing recipe cannot become generic cast execution",()=>Check(!Work(Schedule(suppliedPack:new QuestStrategyPack())),"generic cast admission"));
                Case("recovery veto survives source recipe",()=>Check(!Work(Schedule(allow:false)),"recovery bypass"));
                Case("pickup profile parses and acceptance requires actual raw log",()=>
                {
                    fixture.SetAccepted(false);Xml(Schedule());var node=ProfileManager.CurrentProfile.QuestOrder.OfType<IfNode>().SelectMany(n=>n.Body).OfType<PickUpNode>().First(n=>n.QuestId==id);
                    var owner=new ForcedQuestPickUp(node.QuestId,node.QuestName,node.GiverId,node.GiverName,node.GiverLocation,node.GiverType);Check(!owner.IsDone,"unaccepted pickup already done");fixture.SetAccepted(true);Check(owner.IsDone,"accepted raw log not acknowledged");
                });
                foreach(QuestStrategyRecipe recipe in pack.Recipes.Where(r=>r.QuestId==id).OrderBy(r=>r.ObjectiveIndex))
                {
                    int slot=Array.IndexOf(ids,recipe.CreditId);Check(slot>=0&&counts[slot]==recipe.CreditCount,"source typed identity mismatch");
                    var point=db.CreatureSpawns[recipe.TargetId.ToString()].First();
                    Case("failed accepted strategy cannot execute "+recipe.ObjectiveIndex,()=>Check(!Work(Schedule(accepted:true,failedQuest:true,point:point)),"failed lifecycle bypass"));
                    Case("known unsafe target cannot dispatch "+recipe.ObjectiveIndex,()=>Check(!Work(Schedule(accepted:true,safe:false,point:point)),"safety bypass"));
                    Case("unreachable target cannot dispatch "+recipe.ObjectiveIndex,()=>Check(!Work(Schedule(accepted:true,reachable:false,point:point)),"navigation bypass"));
                    for(int amount=0;amount<recipe.CreditCount;amount++)
                    {
                        int before=amount;
                        Case($"profile-behavior-typed-progress-reschedule:{recipe.ObjectiveIndex}:{before}",()=>
                        {
                            fixture.SetAccepted(true);fixture.SetProgress(progress);fixture.SetInventory(stock);
                            var result=Schedule(accepted:true,point:point);Check(result.Plan.Any(p=>p.Quest.Id==id&&p.ObjectiveIndex==recipe.ObjectiveIndex),"unfinished strategy absent");
                            var xml=XDocument.Parse(Xml(result));var node=xml.Descendants("CustomBehavior").Single(n=>(string?)n.Attribute("CreditId")==recipe.CreditId.ToString());
                            Check(!xml.Descendants("Objective").Any(n=>(string?)n.Attribute("QuestId")==id.ToString()),"strategy became generic kill");
                            var arguments=node.Attributes().Where(a=>a.Name.LocalName!="File").ToDictionary(a=>a.Name.LocalName,a=>a.Value);
                            object owner=Activator.CreateInstance(behavior,new object[]{arguments})!;GC.SuppressFinalize(owner);
                            try
                            {
                                behavior.GetMethod("OnStart",H)!.Invoke(owner,null);Check((int?)behavior.GetProperty("InitialObjectiveCount")!.GetValue(owner)==before,"initial actual typed progress disagreed");
                                Check(!(bool)behavior.GetMethod("HasAuthoritativeSuccess",H)!.Invoke(owner,null)!,"no progress became success");
                                progress[slot]++;fixture.SetProgress(progress);
                                Check((bool)behavior.GetMethod("HasAuthoritativeSuccess",H)!.Invoke(owner,null)!,"new raw progress not acknowledged");
                                Check(!fixture.Quest.IsCompleted,"progress manufactured server completion");
                            }
                            finally{behavior.GetMethod("Dispose",Type.EmptyTypes)!.Invoke(owner,null);}
                            bool scheduled=Schedule(accepted:true,point:point).Plan.Any(p=>p.Quest.Id==id&&p.ObjectiveIndex==recipe.ObjectiveIndex);
                            Check(scheduled==(progress[slot]<recipe.CreditCount),"reschedule ignored typed completion count");
                        });
                    }
                }
                Case("all counters without ready flag do not fabricate turn-in",()=>Check(!Schedule(accepted:true).Plan.Any(p=>p.Quest.Id==id&&p.Stage==QuestWorkStage.TurnIn),"counter sum invented readiness"));
                Case("authoritative turn-in then rewarded reschedule",()=>
                {
                    var ender=db.QuestEnders.First(e=>e.QuestId==id);var points=ender.EnderType==WholesomeAQ.QuestObjectType.Creature?db.CreatureSpawns:db.GameObjectSpawns;var point=points[ender.EnderId.ToString()].First();
                    fixture.SetAccepted(true,complete:true);Xml(Schedule(accepted:true,complete:true,point:point));var node=ProfileManager.CurrentProfile.QuestOrder.OfType<IfNode>().SelectMany(n=>n.Body).OfType<TurnInNode>().First(n=>n.QuestId==id);
                    var owner=new ForcedQuestTurnIn(node.QuestId,node.QuestName,node.TurnInId,node.TurnInName,node.TurnInLocation,node.TurnInType);Check(!owner.IsDone,"ready is not rewarded");
                    fixture.SetAccepted(false);fixture.SetHistory(Field<uint[]>("Completed").Concat(new[]{(uint)id}));Check(owner.IsDone,"actual rewarded history not acknowledged");
                    Check(!Work(Schedule(rewarded:true,point:point)),"rewarded strategy rescheduled");
                });
                records.Add(new{quest_id=id,pipeline_status=errorsForQuest==0?"PASS":"FAIL",passed_cases=passed,failed_cases=errorsForQuest,cases,
                    profile_sha256=profiles,dataset_sha256=Hash(File.ReadAllBytes(data)),repair_sha256=Hash(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(data)!,"quest_data.repairs.json"))),
                    strategy_sha256=Hash(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(data)!,"quest_strategies.json"))),execution_fingerprint=loader.ExecutionFingerprint,
                    source_refs=pack.Recipes.Where(r=>r.QuestId==id).Select(r=>r.SourceRef).ToArray(),live_completion_proven=false,
                    claim_limit="Actual loader/scheduler/profile/parser/pickup/UseItemOn start and typed progress acknowledgement/turn-in/reschedule; controlled raw observations, no native item dispatch or server spell effect."});
            }
        }
        finally{Styx.Helpers.Logging.FileLogging=logging;Directory.Delete(temporary,true);}
        using(var stream=new StreamWriter(new FileStream(output,FileMode.CreateNew,FileAccess.Write)))foreach(var row in records)stream.WriteLine(JsonSerializer.Serialize(row));
        Console.WriteLine($"Vetted source strategy checks: {total-failed}/{total}; failed={failed}; quests={records.Count}; no game attached.");
        if(failed!=0)throw new InvalidOperationException("Vetted source strategy pipeline failed");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
