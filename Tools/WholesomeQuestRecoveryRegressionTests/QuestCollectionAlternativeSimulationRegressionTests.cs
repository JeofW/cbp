using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WholesomeAQ;

// Runs the same dataset pipeline owner on a bounded controlled fixture. It must
// distinguish alternative acquisition sources from separate required objectives.
internal static class QuestCollectionAlternativeSimulationRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        using var authorityScope = new QuestDataRepairPackRegressionTests.Fixture();
        string temp=Path.Combine(Path.GetTempPath(),"cb-alternative-pipeline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        string[] names={"CB_QUEST_SIM_DATASET","CB_QUEST_SIM_OBSERVATIONS","CB_QUEST_SIM_OUTPUT","CB_QUEST_SIM_USE_DATA_LOADER","CB_QUEST_SIM_LIMIT"};
        var previous=names.ToDictionary(n=>n,Environment.GetEnvironmentVariable);
        var db=new QuestDatabase();var expected=new Dictionary<int,string>();
        for(int number=0;number<9;number++)
        {
            int id=997001+number,first=998000+number*10,second=first+1,item=999000+number*10;
            var q=new QuestEntry{Id=id,Name="Controlled alternative "+number,MinLevel=1,QuestLevel=1,
                Objectives=new(){new QuestObjective{Type=ObjectiveType.CollectItem,MobId=first,ItemId=item,CollectCount=3,Index=0},
                                 new QuestObjective{Type=ObjectiveType.CollectItem,MobId=second,ItemId=item,CollectCount=3,Index=1}}};
            if(number==3)q.Objectives[1]=new QuestObjective{Type=ObjectiveType.CollectFromGameObject,GameObjectId=second,ItemId=item,CollectCount=3,Index=1};
            if(number==6)q.Objectives[1].ItemId=item+1;
            if(number>=7)q.Objectives.RemoveAt(1);
            db.Quests.Add(q);db.QuestGivers.Add(new QuestGiverEntry{QuestId=id,GiverId=997100});db.QuestEnders.Add(new QuestEnderEntry{QuestId=id,EnderId=997100});
            if(number!=1 && number!=4 && number!=5)db.CreatureSpawns[first.ToString()]=new(){new SpawnPoint{Map=530,X=10,Y=10,Z=10}};
            if(number!=2 && number!=4 && number!=5 && number!=6)
            {
                var target=number==3?db.GameObjectSpawns:db.CreatureSpawns;target[second.ToString()]=new(){new SpawnPoint{Map=530,X=12,Y=10,Z=10}};
            }
            expected[id]=number>=4?"BLOCKED":"PASS";
        }
        db.CreatureSpawns["997100"]=new(){new SpawnPoint{Map=530,X=10,Y=10,Z=10}};
        var options=new JsonSerializerOptions{Converters={new JsonStringEnumConverter()}};
        string dataset=Path.Combine(temp,"quest_data.json"),observations=Path.Combine(temp,"observations.jsonl"),output=Path.Combine(temp,"results.jsonl");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(db,options);File.WriteAllBytes(dataset,bytes);
        ControlledExecutionCatalogFixture.Prepare(dataset);
        string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        File.WriteAllLines(observations,db.Quests.Select(q=>JsonSerializer.Serialize(new {
            quest_id=q.Id,dataset_sha256=hash,structural_classification="CONTROLLED-FIXTURE",reference_found=true,
            normal_ids=q.Id==997009?new[]{998081,0,0,0}:new int[4],normal_counts=q.Id==997009?new[]{1,0,0,0}:new int[4],
            item_ids=q.Id==997007||q.Id==997008?new[]{999000+(q.Id-997001)*10,999001+(q.Id-997001)*10,0,0,0,0}:new[]{999000+(q.Id-997001)*10,0,0,0,0,0},
            item_counts=q.Id==997007||q.Id==997008?new[]{3,3,0,0,0,0}:new[]{3,0,0,0,0,0}})));
        int passed=0,assertions=0;
        try
        {
            Environment.SetEnvironmentVariable(names[0],dataset);Environment.SetEnvironmentVariable(names[1],observations);Environment.SetEnvironmentVariable(names[2],output);
            Environment.SetEnvironmentVariable(names[3],"1");Environment.SetEnvironmentVariable(names[4],null);
            QuestDatasetSimulationRegressionTests.Run();
            foreach(string line in File.ReadLines(output))
            {
                using var document=JsonDocument.Parse(line);var r=document.RootElement;int id=r.GetProperty("quest_id").GetInt32();
                try
                {
                    Check(r.GetProperty("failed_cases").GetInt32()==0,"the production-owner pipeline raised an assertion");
                    Check(r.GetProperty("pipeline_status").GetString()==expected[id],"expected "+expected[id]+" but got "+r.GetProperty("pipeline_status").GetString()+"; "+r.GetProperty("pipeline_blocks"));
                    passed++;Console.WriteLine("PASS collection alternative pipeline: "+id);
                }
                catch(Failure e){assertions++;Console.Error.WriteLine("FAIL collection alternative pipeline: "+id+": "+e.Message);}
            }
        }
        finally {foreach(var pair in previous)Environment.SetEnvironmentVariable(pair.Key,pair.Value);Directory.Delete(temp,true);}
        Console.WriteLine($"Collection alternative pipeline scenarios: {passed}/9; assertions={assertions}; actual full dataset owner, scheduler/profile/behavior/inventory/turn-in; controlled sources.");
        if(assertions!=0 || passed!=9)throw new InvalidOperationException("Collection alternative simulation regression");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
