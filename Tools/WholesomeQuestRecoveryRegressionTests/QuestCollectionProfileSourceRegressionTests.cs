using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Styx.Logic.Profiles.Quest;
using WholesomeAQ;
using DataObjective = WholesomeAQ.QuestObjective;
using DataType = WholesomeAQ.ObjectiveType;

// The real runtime reads FindCollectItem(ItemId), not the order node's MobId.
internal static class QuestCollectionProfileSourceRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Body)>();
        tests.Add(("creature source reaches the parsed item definition",()=>
        {
            var info=Parse(Build(Quest(Mob(0,501))))[0].FindCollectItem(901);
            Check(info?.OverridedCollectFrom?.ContainsMob(501)==true,"MobId was lost between generated order and runtime definition");
        }));
        tests.Add(("two selected creatures share one complete item definition",()=>
        {
            var parsed=Parse(Build(Quest(Mob(0,501),Mob(1,502))))[0];var info=parsed.FindCollectItem(901);
            Check(parsed.Objectives.OfType<CollectItemObjectiveInfo>().Count()==1 && info?.OverridedCollectFrom?.ContainsMob(501)==true &&
                  info.OverridedCollectFrom.ContainsMob(502) && info.OverridedHotspots!.Count==2,"first matching definition shadowed the second source or location");
        }));
        tests.Add(("mixed object and creature routes retain both namespaces",()=>
        {
            var info=Parse(Build(Quest(Object(0,501),Mob(1,502))))[0].FindCollectItem(901);
            Check(info?.OverridedCollectFrom?.ContainsGameObject(501)==true && info.OverridedCollectFrom.ContainsMob(502),"mixed-source route was dropped");
        }));
        tests.Add(("same numeric ID in creature and object namespaces remains distinct",()=>
        {
            var info=Parse(Build(Quest(Mob(0,501),Object(1,501))))[0].FindCollectItem(901);
            Check(info?.OverridedCollectFrom?.Count==2 && info.OverridedCollectFrom.ContainsMob(501) &&
                  info.OverridedCollectFrom.ContainsGameObject(501),"source namespace collision");
        }));
        tests.Add(("unselected modeled sources do not leak into the active plan",()=>
        {
            var info=Parse(Build(Quest(Mob(0,501),Mob(1,502)),new[]{1}))[0].FindCollectItem(901);
            Check(info?.OverridedCollectFrom?.ContainsMob(502)==true && !info.OverridedCollectFrom.ContainsMob(501),"unselected source became a profile override");
        }));
        tests.Add(("duplicate plan rows do not duplicate sources or hotspots",()=>
        {
            var parsed=Parse(Build(Quest(Mob(0,501)),new[]{0,0}))[0];var info=parsed.FindCollectItem(901);
            Check(parsed.Objectives.Count==1 && info?.OverridedCollectFrom?.Count==1 && info.OverridedHotspots!.Count==1,"duplicate plan inputs created duplicate definitions");
        }));
        tests.Add(("separate item and auxiliary requirements stay separate",()=>
        {
            var other=Mob(1,502);other.ItemId=902;var parsed=Parse(Build(Quest(Mob(0,501),other)))[0];
            Check(parsed.Objectives.Count==2 && parsed.FindCollectItem(901)?.OverridedCollectFrom?.ContainsMob(501)==true &&
                  parsed.FindCollectItem(902)?.OverridedCollectFrom?.ContainsMob(502)==true,"another item's source or requirement was merged");
        }));
        tests.Add(("source hints cannot create vendor or item-use actions",()=>
        {
            var xml=Build(Quest(Mob(0,501),Object(1,502)));var info=Parse(xml)[0].FindCollectItem(901);
            Check(info?.OverridedCollectFrom?.All(source=>source.Type!=CollectFromType.Vendor)==true &&
                  !xml.Descendants("CustomBehavior").Any() && !xml.Descendants("UseItem").Any(),"ordinary collection invented a new action");
        }));
        tests.Add(("conflicting quantities are never silently collapsed",()=>
        {
            var other=Mob(1,502);other.CollectCount=3;var parsed=Parse(Build(Quest(Mob(0,501),other)))[0];
            Check(parsed.Objectives.OfType<CollectItemObjectiveInfo>().Select(info=>info.Count).SequenceEqual(new uint[]{2,3}),"quantity conflict was silently changed");
        }));
        tests.Add(("same item on another quest cannot donate a source",()=>
        {
            var first=Quest(Mob(0,501));var second=Quest(Mob(0,502));second.Id++;
            var plans=Plans(first,null).Concat(Plans(second,null)).ToArray();
            var xml=XDocument.Parse(new ProfileBuilder().BuildProfileXml(plans,new(){Quests=new(){first,second}},"Fixture","Fixture",60));
            var parsed=Parse(xml);
            Check(parsed[0].FindCollectItem(901)?.OverridedCollectFrom?.ContainsMob(501)==true &&
                  !parsed[0].FindCollectItem(901)!.OverridedCollectFrom!.ContainsMob(502) &&
                  parsed[1].FindCollectItem(901)?.OverridedCollectFrom?.ContainsMob(502)==true,"collection sources crossed quest identities");
        }));
        tests.Add(("actual collection behavior consumes the generated source hint",()=>
        {
            var quest=Quest(Mob(0,501));using var live=new QuestDatasetObservationFixture();
            live.SetQuest((uint)quest.Id,quest.Name,60,new int[4],new int[4],new[]{901,0,0,0,0,0},new[]{2,0,0,0,0,0});
            live.LoadProfile(Build(quest).ToString());
            using var owner=new CollectItemObjective(live.Quest,new(),live.Quest.GetObjectives().Single(o=>o.ID==901),new());
            var info=(CollectItemObjectiveInfo?)typeof(CollectItemObjective).GetField("_collectItemInfo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner);
            Check(info?.OverridedCollectFrom?.ContainsMob(501)==true,"actual behavior did not receive the modeled collector");
            live.SetInventory(new());Check(!owner.IsCompleted,"source hint fabricated inventory");
            live.SetInventory(new(){[901]=1});Check(!owner.IsCompleted,"partial count completed");
            live.SetInventory(new(){[901]=2});Check(owner.IsCompleted,"actual item count not acknowledged");
        }));
        int passed=0,assertions=0,unexpected=0;
        foreach(var test in tests)
        {
            try{test.Body();passed++;Console.WriteLine("PASS collection profile source: "+test.Name);}
            catch(Failure error){assertions++;Console.Error.WriteLine("FAIL collection profile source: "+test.Name+": "+error.Message);}
            catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR collection profile source: "+test.Name+": "+error);}
        }
        Console.WriteLine($"Collection profile source scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual builder/parser/collection owner, no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Collection profile source regressions");
    }
    private static DataObjective Mob(int index,int entry)=>new(){Type=DataType.CollectItem,Index=index,MobId=entry,ItemId=901,CollectCount=2};
    private static DataObjective Object(int index,int entry)=>new(){Type=DataType.CollectFromGameObject,Index=index,GameObjectId=entry,ItemId=901,CollectCount=2};
    private static QuestEntry Quest(params DataObjective[] objectives)=>new(){Id=991001,Name="Source fixture",MinLevel=1,QuestLevel=1,Objectives=objectives.ToList()};
    private static QuestPlanEntry[] Plans(QuestEntry quest,int[]? selected)=>(selected??quest.Objectives.Select(o=>o.Index).ToArray()).Select(index=>new QuestPlanEntry
        {Quest=quest,Stage=QuestWorkStage.Objective,ObjectiveIndex=index,Hotspots=new[]{new SpawnPoint{Map=530,X=10+index,Y=20,Z=37}}}).ToArray();
    private static XDocument Build(QuestEntry quest,int[]? selected=null)=>XDocument.Parse(new ProfileBuilder().BuildProfileXml(Plans(quest,selected),new(){Quests=new(){quest}},"Fixture","Fixture",60));
    private static QuestInfo[] Parse(XDocument document)=>document.Root!.Elements("Quest").Select(QuestInfo.FromXML).ToArray();
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
