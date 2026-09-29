using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using WholesomeAQ;

internal static class QuestCollectionCountRepairRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        int passed=0,assertions=0,unexpected=0,total=0;
        void Case(string name,Action<QuestDataRepairPackRegressionTests.Fixture> action)
        {
            total++;
            try { using var f=new QuestDataRepairPackRegressionTests.Fixture();Configure(f);action(f);passed++;Console.WriteLine("PASS collection count repair: "+name); }
            catch(Failure e) {assertions++;Console.Error.WriteLine("FAIL collection count repair: "+name+": "+e.Message);}
            catch(Exception e) {unexpected++;Console.Error.WriteLine("ERROR collection count repair: "+name+": "+e);}
        }
        Case("bound creature collection count is repaired",f=>
        {
            f.Write();QuestDatabase? db=null;try {db=f.Load();}catch(InvalidDataException) { }
            Check(db!=null && db.Quests[0].Objectives[0].CollectCount==8,"explicit source-bound count was not applied");
            Check(db!.Quests[0].Objectives[0].ItemId==991010 && db.Quests[0].Objectives[0].MobId==991020,"count repair rewrote target identity");
            Check(JsonNode.Parse(File.ReadAllText(f.DataPath))!["Quests"]![0]!["Objectives"]![0]!["CollectCount"]!.GetValue<int>()==3,"base file was rewritten");
        });
        Case("bound gameobject collection retains its namespace",f=>
        {
            f.SetBase(n=>{var o=n["Quests"]![0]!["Objectives"]![0]!;o["Type"]="CollectFromGameObject";o["MobId"]=0;o["GameObjectId"]=991020;});
            f.Pack["ObjectiveCountRepairs"]![0]!["ObjectiveType"]="CollectFromGameObject";f.Write();QuestDatabase? db=null;
            try{db=f.Load();}catch(InvalidDataException) { }
            Check(db!=null && db.Quests[0].Objectives[0].CollectCount==8 && db.Quests[0].Objectives[0].GameObjectId==991020,"typed GO count was not repaired");
        });
        foreach(string field in new[]{"QuestId","RowIndex","ObjectiveIndex","TargetId","ItemId","ExpectedCount"})
        {
            string target=field;Case("reject changed "+field,f=>{f.Pack["ObjectiveCountRepairs"]![0]![target]=999;Refused(f);});
        }
        Case("reject zero requirement",f=>{f.Pack["ObjectiveCountRepairs"]![0]!["RequiredCount"]=0;Refused(f);});
        Case("reject namespace mismatch",f=>{f.Pack["ObjectiveCountRepairs"]![0]!["ObjectiveType"]="CollectFromGameObject";Refused(f);});
        Case("reject duplicate physical source row",f=>{var rows=f.Pack["ObjectiveCountRepairs"]!.AsArray();rows.Add(rows[0]!.DeepClone());Refused(f);});
        Case("reject missing source receipt",f=>{f.Pack["ObjectiveCountRepairs"]![0]!["SourceRef"]="";Refused(f);});
        Case("reject scripted cast collection reinterpretation",f=>{f.SetBase(n=>n["Quests"]![0]!["SpecialFlags"]=32);Refused(f);});
        Case("reject ordinary kill rewritten as item collection",f=>{f.Pack["ObjectiveCountRepairs"]![0]!["ObjectiveType"]="KillMob";Refused(f);});
        Console.WriteLine($"Collection count repair scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual data loader; explicit old/new identity; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Collection count repair regression");
    }
    private static void Configure(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.SetBase(n=>n["Quests"]![0]!["Objectives"]=JsonNode.Parse("[{\"Type\":\"CollectItem\",\"MobId\":991020,\"GameObjectId\":0,\"ItemId\":991010,\"CollectCount\":3,\"Index\":17}]"));
        f.Pack["QuestMetadata"]![0]!["DeliveryItems"]=null;f.Pack["QuestMetadata"]![0]!["AcceptanceSupplies"]=null;
        f.Pack["ObjectiveCountRepairs"]=JsonNode.Parse("[{\"QuestId\":991001,\"RowIndex\":0,\"ObjectiveIndex\":17,\"ObjectiveType\":\"CollectItem\",\"TargetId\":991020,\"ItemId\":991010,\"ExpectedCount\":3,\"RequiredCount\":8,\"SourceRef\":\"controlled://quest-template/991001/required-item1\"}]");
    }
    private static void Refused(QuestDataRepairPackRegressionTests.Fixture f)
    {
        f.Write();bool refused=false;try {f.Load();}catch(InvalidDataException){refused=true;}Check(refused,"changed/unbound count patch was accepted");
    }
    private static void Check(bool value,string reason) {if(!value)throw new Failure(reason);}
}
