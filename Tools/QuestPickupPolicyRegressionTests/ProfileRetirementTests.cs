using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Bots.Quest;
using Bots.Quest.QuestOrder;
using CommonBehaviors.Actions;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using TreeSharp;

internal static class ProfileRetirementTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly MethodInfo Initialize = typeof(QuestState).GetMethod("InitializeFromProfile", Hidden)!;

    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        var state=QuestState.Instance;var order=state.Order;
        var priorNodes=order.Nodes;var priorBehavior=order.CurrentBehavior;var priorPoi=BotPoi.Current;
        void Case(string name,System.Action test)
        {
            total++;order.Nodes=new();order.CurrentBehavior=null;BotPoi.Current=new BotPoi(PoiType.None);
            try { test();passed++;Console.WriteLine("PASS actual profile retirement: "+name); }
            catch(Exception error){Console.Error.WriteLine("FAIL actual profile retirement: "+name+": "+error);}
        }
        try
        {
            foreach(bool nullProfile in new[]{false,true})
                Case("profile replacement retires existing branch and owner, null="+nullProfile,()=>
                {
                    var owner=new Tracked();order.CurrentBehavior=owner;owner.Branch.Start(null!);owner.Branch.Tick(null!);
                    Replace(nullProfile?null:new Profile());
                    Check(owner.Disposals==1&&owner.Leaf.Stops==1&&order.CurrentBehavior==null,
                        "profile publication discarded an undisposed/running predecessor");
                    Replace(new Profile());
                    Check(owner.Disposals==1&&owner.Leaf.Stops==1,"repeated refresh retired old work twice");
                });
            foreach(int depth in new[]{1,3})
                Case("nested accepted pickup is retired before replacement objective, depth="+depth,()=>
                {
                    var pickup=Pickup();BotPoi.Current=PublishFromFactory(pickup);
                    ForcedBehavior parent=pickup;
                    for(int level=0;level<depth;level++)
                    {
                        var wrapper=new ForcedIf(new IfNode(()=>true,Array.Empty<OrderNode>()));
                        var nested=new QuestOrder{CurrentBehavior=parent};
                        typeof(ForcedIf).GetField("conditionalOrder",Hidden)!.SetValue(wrapper,nested);
                        parent=wrapper;
                    }
                    order.CurrentBehavior=parent;
                    Replace(new Profile());
                    Check(order.CurrentBehavior==null&&BotPoi.Current.Type==PoiType.None,
                        "discarded conditional work left the accepted pickup's giver selected");
                });
            Case("same-quest successor POI is not owned by the predecessor",()=>
            {
                var pickup=Pickup();BotPoi.Current=PublishFromFactory(pickup);
                var successor=PublishFromFactory(Pickup());BotPoi.Current=successor;
                pickup.Dispose();
                Check(ReferenceEquals(BotPoi.Current,successor),"metadata equality let an old pickup clear a new stage's POI");
            });
            Case("cleanup callback cannot overwrite a reentrant profile publication",()=>
            {
                var successor=new Tracked();var replacement=new Profile();
                var owner=new Tracked();order.CurrentBehavior=owner;
                owner.OnDispose=()=>{Replace(replacement);order.CurrentBehavior=successor;};
                Replace(new Profile());
                Check(owner.Disposals==1&&ReferenceEquals(order.CurrentBehavior,successor),
                    "predecessor profile initialization overwrote callback-published work");
            });
            Case("cleanup cannot clear a reentrant same-quest successor",()=>
            {
                var pickup=Pickup();BotPoi.Current=PublishFromFactory(pickup);
                var leaf=new TrackingLeaf();typeof(ForcedBehavior).GetField("_branch",Hidden)!.SetValue(pickup,leaf);
                BotPoi? successor=null;
                leaf.OnStop=()=>{successor=PublishFromFactory(Pickup());BotPoi.Current=successor;};
                leaf.Start(null!);leaf.Tick(null!);order.CurrentBehavior=pickup;
                Replace(new Profile());
                Check(successor!=null&&ReferenceEquals(BotPoi.Current,successor),"retiring branch cleared successor work from its stop callback");
            });
            Case("profile retirement preserves a foreign combat destination",()=>
            {
                var pickup=Pickup();BotPoi.Current=PublishFromFactory(pickup);order.CurrentBehavior=pickup;
                var combat=new BotPoi(new WoWPoint(100,100,10),PoiType.Kill);BotPoi.Current=combat;
                Replace(new Profile());
                Check(ReferenceEquals(BotPoi.Current,combat),"profile refresh cleared unrelated combat work");
            });
        }
        finally { order.Nodes=priorNodes;order.CurrentBehavior=priorBehavior;BotPoi.Current=priorPoi; }
        Console.WriteLine($"Actual profile retirement: {passed}/{total}; complete QuestState, conditional owners and pickup factory/disposal; controlled existing branch leaves; no client attachment.");
        if(passed!=total)Environment.ExitCode=1;
    }

    internal static BotPoi PublishFromFactory(ForcedQuestPickUp owner)
    {
        static IEnumerable<Composite> Walk(Composite branch)
        {
            yield return branch;
            if(branch is GroupComposite group)
                foreach(var child in group.Children)
                    foreach(var node in Walk(child))yield return node;
        }
        var action=Walk(owner.Branch).OfType<ActionSetPoi>().First();
        var factory=(Delegate)typeof(ActionSetPoi).GetField("_poiRetrievalFunc",Hidden)!.GetValue(action)!;
        return (BotPoi)factory.DynamicInvoke(new object?[]{null})!;
    }
    private static ForcedQuestPickUp Pickup()=>new(9472,"Arelion's Mistress",16793,"Magistrix Carinda",new WoWPoint(-596,4177,64),QuestObjectType.Npc);
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static void Replace(Profile? profile)
    {
        try{Initialize.Invoke(QuestState.Instance,new object?[]{profile});}
        catch(TargetInvocationException error) when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();}
    }
    private sealed class Tracked:ForcedBehavior
    {
        internal int Disposals;internal System.Action? OnDispose;
        internal TrackingLeaf Leaf=new();
        public override bool IsDone=>false;
        protected override Composite CreateBehavior()=>Leaf;
        public override void Dispose(){Disposals++;OnDispose?.Invoke();}
    }
    private sealed class TrackingLeaf:Composite
    {
        internal int Stops;internal System.Action? OnStop;
        protected override IEnumerable<RunStatus> Execute(object context){while(true)yield return RunStatus.Running;}
        public override void Stop(object context){Stops++;var callback=OnStop;OnStop=null;callback?.Invoke();base.Stop(context);}
    }
}
