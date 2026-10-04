using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WholesomeAQ;

internal static class CatalogCoverageTests
{
    internal static void Run(string datasetPath,QuestDatabase database,QuestStrategyPack pack)
    {
        int passed=0,total=0,rows=0,blocked=0,strategies=0;
        void Check(bool good,string reason){if(!good)throw new InvalidOperationException(reason);}
        void Case(string name,Action test){total++;try{test();passed++;Console.WriteLine("PASS catalog coverage: "+name);}catch(Exception error){Console.Error.WriteLine("FAIL catalog coverage: "+name+": "+error);}}
        string directory=Path.GetDirectoryName(datasetPath)!;
        byte[] catalog=File.ReadAllBytes(Path.Combine(directory,"quest_execution_contracts.json"));
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        string dataSha=Hash(datasetPath),repairSha=Hash(Path.Combine(directory,"quest_data.repairs.json")),strategySha=Hash(Path.Combine(directory,"quest_strategies.json"));
        Case("all4335 effective quests and every objective have one bound mechanism",()=>
        {
            Check(database.Quests.Count==4335&&database.Quests.Select(q=>q.Id).Distinct().Count()==4335,"quest population differs");
            var scheduler=typeof(QuestScheduler).GetMethod("CanScheduleWholeQuestStrategy",BindingFlags.NonPublic|BindingFlags.Static)!;
            foreach(var quest in database.Quests)
            {
                Check(quest.SourceExecution?.IsBound==true&&quest.SourceExecution.Objectives.Count==quest.Objectives.Count,"unbound member "+quest.Id);
                for(int index=0;index<quest.Objectives.Count;index++)
                {
                    rows++;var objective=quest.Objectives[index];var contract=quest.SourceExecution!.Objectives[index];
                    Check(contract.RowIndex==index&&contract.Matches(objective),"row identity mismatch "+quest.Id+"/"+index);
                    bool primitive=QuestExecutionPolicy.CanExecutePrimitive(quest,objective);
                    var recipes=QuestExecutionPolicy.Strategies(quest,objective,pack);
                    bool strategy=recipes.Length==1&&QuestExecutionPolicy.CanExecuteStrategy(quest,objective,pack,recipes[0]);
                    Check(!(primitive&&strategy),"two executable owners for "+quest.Id+"/"+index);
                    if(contract.Driver=="Unsupported")
                    {
                        blocked++;Check(!primitive&&!strategy&&QuestExecutionPolicy.Rejection(quest,objective,pack)!=null,
                            "unsupported source row fell back to a primitive: "+quest.Id+"/"+index);
                    }
                    else if(contract.Driver=="Primitive")Check(primitive,"primitive admission contradicts bound row: "+quest.Id+"/"+index);
                    else
                    {
                        strategies++;Check(strategy,"implemented source strategy is not admitted: "+quest.Id+"/"+index);
                        Check((bool)scheduler.Invoke(null,new object?[]{quest,objective,pack,recipes[0]})!,"scheduler disagrees with materializer policy");
                    }
                }
            }
            Check(rows==5771&&blocked==931&&strategies==4,"exclusive effective-objective accounting changed without review");
        });
        Case("source-held quests cannot materialize a generic first objective",()=>
        {
            int checkedRows=0;
            foreach(var quest in database.Quests)
            {
                foreach(var objective in quest.Objectives.GroupBy(o=>o.Index).Select(g=>g.First()))
                {
                    if(QuestExecutionPolicy.Rejection(quest,objective,pack)==null)continue;
                    var entry=new QuestPlanEntry{Quest=quest,Stage=QuestWorkStage.Objective,ObjectiveIndex=objective.Index,
                        Hotspots=new[]{new SpawnPoint{Map=530,X=100,Y=10,Z=10}}};
                    bool rejected=false;
                    try{new ProfileBuilder().BuildProfileXml(new[]{entry},database,"SourceAudit","Fixture",63,null,pack);}
                    catch(InvalidDataException){rejected=true;}
                    Check(rejected,"materializer emitted source-held objective "+quest.Id+"/"+objective.Index);checkedRows++;
                }
            }
            Check(checkedRows>600,"source-held materializer population unexpectedly small");
        });
        var before=database.Quests.Select(q=>q.SourceExecution).ToArray();
        var mutations=new Dictionary<string,Action<JsonObject>>
        {
            ["wrong client"]=root=>root["ClientBuild"]=30403,
            ["wrong source revision"]=root=>root["SourceRevision"]=new string('0',40),
            ["wrong base hash"]=root=>root["QuestDataSha256"]=new string('0',64),
            ["wrong repairs hash"]=root=>root["QuestDataRepairsSha256"]=new string('0',64),
            ["wrong strategy hash"]=root=>root["StrategyPackSha256"]=new string('0',64),
            ["missing quest"]=root=>root["Quests"]!.AsArray().RemoveAt(0),
            ["duplicate quest"]=root=>root["Quests"]![1]=root["Quests"]![0]!.DeepClone(),
            ["unknown driver"]=root=>root["Quests"]![0]!["Objectives"]![0]!["Driver"]="AssumeKill",
            ["wrong objective count"]=root=>root["Quests"]![0]!["Objectives"]![0]!["KillCount"]=999,
            ["wrong objective index"]=root=>root["Quests"]![0]!["Objectives"]![0]!["RowIndex"]=1,
            ["unknown header field"]=root=>root["UseRetailFallback"]=true,
            ["contradictory quest status"]=root=>root["Quests"]![0]!["Status"]="AllProven"
        };
        foreach(var mutation in mutations)
            Case("atomic rejection of "+mutation.Key,()=>
            {
                var parsed=JsonNode.Parse(catalog)!.AsObject();mutation.Value(parsed);bool rejected=false;
                try{QuestExecutionCatalogLoader.Apply(JsonSerializer.SerializeToUtf8Bytes(parsed),database,dataSha,repairSha,strategySha);}
                catch(InvalidDataException){rejected=true;}
                Check(rejected,"malformed catalog was accepted");
                Check(database.Quests.Select((q,i)=>ReferenceEquals(q.SourceExecution,before[i])).All(x=>x),"rejected catalog partly published authority");
            });
        Case("missing catalog cannot restore ordinary kill fallback",()=>
        {
            var options=new JsonSerializerOptions{Converters={new JsonStringEnumConverter()}};
            var clone=JsonSerializer.Deserialize<QuestDatabase>(JsonSerializer.SerializeToUtf8Bytes(database,options),options)!;
            QuestExecutionCatalogLoader.Apply(null!,clone,dataSha,repairSha,strategySha);
            Check(clone.Quests.All(q=>q.SourceExecution!=null&&!q.SourceExecution.IsBound),"missing catalog left legacy null authority");
            foreach(var quest in clone.Quests)
                foreach(var objective in quest.Objectives)
                    Check(QuestExecutionPolicy.Rejection(quest,objective,pack)!=null,"missing catalog enabled "+quest.Id);
        });
        Console.WriteLine($"Exact source execution catalog: {passed}/{total}; 4335 quests, {rows} effective objective rows, {blocked} held rows, {strategies} strategy rows; no live completion claim.");
        if(passed!=total)Environment.ExitCode=1;
    }
}
