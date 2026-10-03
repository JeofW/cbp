#nullable disable
using System.Runtime.CompilerServices;
using Bots.Grind;
using Bots.Quest;
using Bots.Quest.QuestOrder;
using Harness;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class MandatoryTravelCases
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Case(string name,System.Action body)
        {
            total++;
            try { body();passed++;Console.WriteLine("PASS mandatory mounted travel: "+name); }
            catch(Exception error) { Console.Error.WriteLine("FAIL mandatory mounted travel: "+name+": "+error); }
        }
        foreach(bool pickup in new[]{false,true})
            foreach(bool flying in new[]{false,true})
                foreach(int attackers in new[]{1,2})
                    Case($"{(pickup?"pickup":"turn-in")}, flying={flying}, attackers={attackers}",()=>Journey(pickup,flying,attackers));
        Case("replaced stage cannot retain mandatory travel authority",()=>
        {
            var original=Arrange(false,false);StyxWoW.Me.Combat=true;
            QuestState.Instance.Order.CurrentBehavior=new ForcedQuestTurnIn{QuestId=10286};
            Check(!QuestLootHandoff.CanRunMandatory(original),"replaced stage retained authority");
        });
        Case("incidental combat does not drain an unrelated loot POI",()=>
        {
            var owner=Arrange(false,false);StyxWoW.Me.Combat=true;
            var loot=new BotPoi(new WoWPoint(10,0,0),PoiType.Loot);BotPoi.Current=loot;
            Check(!QuestLootHandoff.CanRunMandatory(owner)&&ReferenceEquals(BotPoi.Current,loot),"combat drained/reassigned incidental loot");
        });
        Case("unmounted combat keeps priority over mandatory work",()=>
        {
            var owner=Arrange(false,false);StyxWoW.Me.Combat=true;StyxWoW.Me.Mounted=false;
            Check(!QuestLootHandoff.CanRunMandatory(owner),"unmounted combat was vetoed by mandatory travel");
        });
        foreach(bool flying in new[]{false,true})
            Case("moving NPC search preserves mounted travel, flying="+flying,()=>
            {
                var owner=(ForcedQuestTurnIn)Arrange(false,flying);owner.CaptureSearch();
                Check(owner.ObserveSearchCurrent(),"initial patrol owner was not admitted");
                StyxWoW.Me.Combat=true;
                Check(owner.ObserveSearchCurrent(),"incidental aggro revoked the actual patrol owner");
                StyxWoW.Me.Mounted=false;StyxWoW.Me.IsFlying=false;StyxWoW.Me.ObservedMovementFlags=0;
                Check(!owner.ObserveSearchCurrent(),"unmounted patrol hid combat takeover");
            });
        Case("moving NPC search revokes replaced work even while mounted",()=>
        {
            var owner=(ForcedQuestTurnIn)Arrange(false,false);owner.CaptureSearch();StyxWoW.Me.Combat=true;
            BotPoi.Current=new BotPoi(new WoWPoint(900,10,0),PoiType.QuestTurnIn);
            Check(!owner.ObserveSearchCurrent(),"replacement POI retained old patrol admission");
        });
        Console.WriteLine($"Mandatory mounted travel: {passed}/{total}; actual QuestLootHandoff, PublishedQuestRoot and combat branches; controlled quest execution/movement and client leaves.");
        if(passed!=total)Environment.ExitCode=1;
    }

    private static void Check(bool condition,string reason)
    { if(!condition)throw new InvalidOperationException(reason); }

    private static ForcedBehavior Arrange(bool pickup,bool flying)
    {
        Control.Reset();
        var actor=new LocalPlayer{Guid=1,BaseAddress=100,MapId=530,IsAlive=true,Location=new WoWPoint(100,10,flying?40:0),
            Mounted=true,IsFlying=flying,ObservedMovementFlags=flying?0x02000000u:0};
        StyxWoW.Me=actor;WoWMovement.ActiveMover=actor;
        var target=new WoWUnit{Guid=2,BaseAddress=200,Entry=16863,IsAlive=true,Location=new WoWPoint(105,10,0),InLineOfSpellSight=true};
        actor.CurrentTarget=target;Targeting.Instance.FirstUnit=target;Targeting.Instance.TargetList.Add(target);
        ForcedBehavior owner=pickup?new ForcedQuestPickUp{QuestId=10286}:new ForcedQuestTurnIn{QuestId=10286};
        var order=QuestState.Instance.Order;
        order.Nodes=new OrderNodeCollection{pickup?new PickUpNode{QuestId=10286}:new TurnInNode{QuestId=10286}};
        order.CurrentBehavior=owner;
        BotPoi.Current=new BotPoi(new WoWPoint(800,10,0),pickup?PoiType.QuestPickUp:PoiType.QuestTurnIn);
        return owner;
    }

    private static void Journey(bool pickup,bool flying,int attackers)
    {
        var owner=Arrange(pickup,flying);var actor=StyxWoW.Me;int moves=0,attacks=0;
        var combat=new CountingLeaf(_=>{attacks++;return RunStatus.Success;});
        RoutineManager.Current=new CombatRoutine{RestBehavior=new CountingLeaf(RunStatus.Failure),PreCombatBuffBehavior=new CountingLeaf(RunStatus.Failure),
            PullBehavior=new CountingLeaf(RunStatus.Failure),HealBehavior=new CountingLeaf(RunStatus.Failure),CombatBuffBehavior=new CountingLeaf(RunStatus.Failure),
            CombatBehavior=combat,PullBuffBehavior=new CountingLeaf(RunStatus.Failure)};
        // The shared mandatory gate is the exact predicate used by both actual
        // forced factories. This controlled leaf records navigation scheduling;
        // geometric movement/effect admission is tested by the linked runtime suite.
        Control.RootQuest=new CountingLeaf(_=>
        {
            if(!QuestLootHandoff.CanRunMandatory(owner))return RunStatus.Failure;
            moves++;return RunStatus.Running;
        });
        QuestBot.RootFactory=()=>new PrioritySelector(new CountingLeaf(RunStatus.Failure),LevelBot.CreateCombatBehavior(),
            new QuestLootHandoff(new CountingLeaf(RunStatus.Failure)),new CountingLeaf(RunStatus.Failure),
            new Decorator(_=>true,new CountingLeaf(RunStatus.Failure)),new CountingLeaf(RunStatus.Failure),new CountingLeaf(RunStatus.Failure));
        Func<bool> publication=()=>true;
        var root=new PublishedQuestRoot(()=>publication,()=>new[]{"absent"});root.Start(null);
        try
        {
            Check(root.Tick(null)==RunStatus.Running&&moves==1,"initial quest journey did not start");
            actor.Combat=true;
            if(attackers==2)Targeting.Instance.TargetList.Add(new WoWUnit{Guid=3,BaseAddress=300,Entry=16863,IsAlive=true,Location=new WoWPoint(106,10,0)});
            for(int tick=0;tick<6;tick++)
            {
                root.Tick(null);
                Check(moves==tick+2,"mounted travel lost scheduling at incidental combat; no combat owner replaced it");
                Check(attacks==0&&Control.DismountSubmissions==0,"incidental aggro authorized voluntary dismount or an attack");
            }
            actor.Mounted=false;
            if(flying)
            {
                root.Tick(null);
                Check(attacks==0&&moves==7,"forced aerial unmount admitted ordinary travel or combat before landing");
                actor.IsFlying=false;actor.ObservedMovementFlags=0;actor.Location=new WoWPoint(100,10,0);
            }
            for(int tick=0;tick<4&&attacks==0;tick++)root.Tick(null);
            Check(attacks>0&&moves==7,"observed ground unmount did not release combat and preempt quest travel");
            Check(Control.DismountSubmissions==0,"client-observed mount loss produced a redundant dismount");
        }
        finally { root.Stop(null); }
    }
}

// Only data/cache boundaries required by the complete linked QuestLootHandoff.
// No missing cache observation is converted to completion or a loot receipt.
namespace Styx.WoWInternals.WoWObjects
{
    public partial class LocalPlayer { public QuestLog QuestLog{get;}=new(); }
    public sealed class QuestLog
    {
        public QuestSnapshot CaptureSnapshot()=>new();
        public bool IsSnapshotCurrent(QuestSnapshot snapshot)=>true;
    }
    public sealed class QuestSnapshot
    {
        public bool IsComplete;
        public List<QuestEntry> Quests{get;}=new();
        public HashSet<uint> ReadyQuestIds{get;}=new();public HashSet<uint> FailedQuestIds{get;}=new();
    }
    public sealed class QuestEntry { public uint Id;public List<int> CollectItemIds{get;}=new(); }
    public sealed class UnitInfo { public uint[] QuestItems=Array.Empty<uint>(); }
    public static class UnitCacheBoundary { public static bool GetCachedInfo(this WoWUnit unit,out UnitInfo info){info=new();return false;} }
    public class WoWGameObject:WoWObject { public bool GetCachedInfo(out UnitInfo info){info=new();return false;} }
}
namespace Styx.Logic.Inventory.Frames.LootFrame
{
    public sealed class LootFrame { public static LootFrame Instance{get;}=new();public ulong LootingObjectGuid;public void Close(){} }
}
namespace Styx.Logic.Combat
{
    public static partial class RecoveryActions { public static void ReportDeferral(Exception error,string owner)=>RethrowControlFlow(error); }
}
namespace Styx.Logic.Profiles.Quest
{
    public class PickUpNode:OrderNode { public uint QuestId; }
    public class TurnInNode:OrderNode { public uint QuestId; }
    public class IfNode:OrderNode { public List<OrderNode> Body{get;}=new(); }
    public class WhileNode:OrderNode { public List<OrderNode> Body{get;}=new(); }
}
namespace Bots.Quest.QuestOrder
{
    public class ForcedQuestPickUp:ForcedBehavior { public uint QuestId; }
    public partial class ForcedQuestTurnIn:ForcedBehavior
    {
        public uint QuestId;
        private bool disposed;
        private object searchRun,searchProfile,searchBehavior;
        private BotPoi searchPoi;
        private LocalPlayer searchActor;
        private ulong searchActorGuid;
        private uint searchActorAddress,searchMap;
        private static LocalPlayer Me=>StyxWoW.Me;
        private bool ShouldSetPoi(object context)=>false;
        internal void CaptureSearch()
        {
            searchRun=Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;searchActor=Me;searchPoi=BotPoi.Current;
            searchProfile=Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot;searchBehavior=QuestOrder.Instance.CurrentBehavior;
            searchActorGuid=Me.Guid;searchActorAddress=Me.BaseAddress;searchMap=Me.MapId;
        }
        internal bool ObserveSearchCurrent()=>SearchCurrent();
    }
    public class ForcedIf:ForcedBehavior { public IfNode IfNode;public QuestOrder ActiveOrder; }
    public class ForcedWhile:ForcedBehavior { public WhileNode WhileNode;public QuestOrder ActiveOrder; }
}
