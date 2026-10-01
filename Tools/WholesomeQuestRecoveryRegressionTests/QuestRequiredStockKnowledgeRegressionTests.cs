using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using WholesomeAQ;

internal static class QuestRequiredStockKnowledgeRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        using var authority=new QuestDataRepairPackRegressionTests.Fixture();
        string root=QuestTypedStrategyBehaviorRegressionTests.Root();
        string path=Path.Combine(root,"runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.json");
        byte[] before=File.ReadAllBytes(path);var db=new DataLoader(path).Load();
        var expected=new Dictionary<int,Dictionary<int,int>>
        {
            [565]=new(){{2997,1},{2321,1},{3719,1}},[2746]=new(){{8683,2}},
            [10757]=new(){{23445,4},{22445,2}},[10763]=new(){{23445,4},{22445,2}},
            [13906]=new(){{47196,20},{8170,20}}
        };
        int passed=0,failures=0,errors=0;
        foreach(var item in expected)
        {
            try
            {
                var q=db.Quests.Single(q=>q.Id==item.Key);var stock=q.RequiredStockItems;
                Check(stock!=null && stock.Count==item.Value.Count && stock.All(r=>item.Value.TryGetValue(r.ItemId,out int count)&&count==r.Count),
                    "actual source-bound knowledge omitted required return materials");
                Check(q.Objectives.All(o=>stock!.All(r=>r.ItemId!=o.ItemId)),"stock replaced an ordinary item owner");
                var observed=item.Value.ToDictionary(p=>p.Key,p=>(long)p.Value);
                Check(QuestDeliveryPolicy.PickupRejection(q,observed)==null,"correct carried materials did not satisfy pickup stock policy");
                Check(QuestDeliveryPolicy.PickupRejection(q,null)!=null,"unknown stock admitted required-material pickup");
                if(q.Id==13906)
                {
                    Check(q.SupplementalSupply?.ItemId==46362 && q.SupplementalSupply.RequiredCount==1,
                        "separate source-supplied hatchling contract changed");
                    Check(QuestDeliveryPolicy.TurnInRejection(q,observed)!=null,"promised supplied item became an inventory receipt");
                    observed[46362]=1;
                }
                Check(QuestDeliveryPolicy.TurnInRejection(q,observed)==null,"complete required materials did not satisfy stock gate");
                passed++;Console.WriteLine("PASS required stock knowledge: "+item.Key);
            }
            catch(Failure error){failures++;Console.Error.WriteLine("FAIL required stock knowledge: "+item.Key+": "+error.Message);}
            catch(Exception error){errors++;Console.Error.WriteLine("ERROR required stock knowledge: "+item.Key+": "+error);}
        }
        Check(before.SequenceEqual(File.ReadAllBytes(path)) && db.Quests.Count==4335,"base dataset changed");
        Console.WriteLine($"Required stock knowledge scenarios: {passed}/{expected.Count}; assertions={failures}; unexpected={errors}; actual shipped loader and stock policy, no acquisition or live completion claim.");
        if(failures+errors!=0)throw new InvalidOperationException("Required stock knowledge regression");
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
