#nullable disable
using System.Reflection;
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
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class RequiredQuestCombatCases
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Case(string name,System.Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS required quest combat: "+name); }
            catch(Exception error) { Console.Error.WriteLine("FAIL required quest combat: "+name+": "+error.Message); }
        }
        foreach(bool flying in new[]{false,true}) foreach(bool aggro in new[]{false,true})
            Case("required mob preempts a running hotspot and lands before damage/"+flying+"/"+aggro,()=>Journey(flying,aggro));
        foreach(string nesting in new[]{"If","While","If/While","While/If"})
            Case("selected nested objective preempts airborne hotspot/"+nesting,()=>Journey(true,false,nesting));
        foreach(string change in new[]{"node","leaf","ancestor","uninitialized"})
            Case("nested target acknowledgement cannot borrow replaced "+change,()=>
            {
                var w=Arrange(true,false,"If");using var cleanup=new Cleanup(w.Root);
                var rootOrder=QuestState.Instance.Order;
                var wrapper=(ForcedIf)rootOrder.CurrentBehavior;
                Control.OnTarget=_=>
                {
                    if(change=="node")rootOrder.Nodes=new OrderNodeCollection{new OrderNode()};
                    if(change=="ancestor")rootOrder.CurrentBehavior=new ForcedIf{IfNode=wrapper.IfNode,ActiveOrder=wrapper.ActiveOrder};
                    if(change=="leaf")wrapper.ActiveOrder.CurrentBehavior=new ForcedQuestObjective{Objective=new Bots.Quest.Objectives.GrindObjective{RequiredEntry=19349}};
                    if(change=="uninitialized")wrapper.ActiveOrder=null;
                };
                Pulse(w.Root);Check(BotPoi.Current.Type!=PoiType.Kill&&Control.DismountSubmissions==0&&w.Damage.Ticks==0,
                    "replaced conditional ancestry acquired the predecessor's combat target");
            });
        foreach(string veto in new[]{"unrelated","complete","prerequisite","farming","service","player","range","dead","replacement"})
            Case("required handoff respects "+veto,()=>Veto(veto));
        Case("temporary target-list gap preserves an acknowledged required Kill",()=>
        {
            var w=Arrange(false,false);using var cleanup=new Cleanup(w.Root);Pulse(w.Root);
            Check(BotPoi.Current.Type==PoiType.Kill,"required obligation was not established");
            w.Actor.Mounted=false;Targeting.Instance.FirstUnit=null;Targeting.Instance.TargetList.Clear();
            Pulse(w.Root);Check(BotPoi.Current.Type==PoiType.Kill&&Control.RootQuest.Ticks==1,"temporary selection gap discarded a live required obligation");
        });
        Case("a temporarily unavailable pull does not resume hotspot execution",()=>
        {
            var w=Arrange(false,false);using var cleanup=new Cleanup(w.Root);Pulse(w.Root);
            Check(BotPoi.Current.Type==PoiType.Kill,"required obligation was not established");
            w.Actor.Mounted=false;w.Target.InLineOfSpellSight=false;
            for(int i=0;i<3;i++)Pulse(w.Root);
            Check(BotPoi.Current.Type==PoiType.Kill&&Control.RootQuest.Ticks==1&&w.Damage.Ticks==0,"waiting for a safe pull resumed the unrelated quest/hotspot branch");
        });
        Console.WriteLine($"Required quest combat: {passed}/{total}; actual published root, target handoff, combat transition and branches; controlled objective credit and native leaves.");
        if(passed!=total)Environment.ExitCode=1;
    }
    private static void Check(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    private static Composite TargetingBranch()=>(Composite)typeof(QuestBot).GetMethod("CreateTargetingBehavior",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
    private static (LocalPlayer Actor,WoWUnit Target,ForcedQuestObjective Objective,PublishedQuestRoot Root,CountingLeaf Damage) Arrange(bool flying,bool combat,string nesting="")
    {
        Control.Reset();
        var actor=new LocalPlayer{Guid=1,BaseAddress=100,MapId=530,IsAlive=true,Location=new(100,10,flying?70:0),Mounted=true,IsFlying=flying,ObservedMovementFlags=flying?0x02000000u:0,IsMoving=true,Combat=combat};
        var target=new WoWUnit{Guid=2,BaseAddress=200,Entry=19349,IsAlive=true,Location=new(105,10,0),InLineOfSpellSight=true};
        StyxWoW.Me=actor;WoWMovement.ActiveMover=actor;
        BotPoi.Current=new BotPoi(new WoWPoint(110,10,0),PoiType.Hotspot);
        var objective=new ForcedQuestObjective{Objective=new Bots.Quest.Objectives.GrindObjective{RequiredEntry=19349}};
        QuestState.Instance.Order.CurrentBehavior=objective;
        foreach(string wrapper in nesting.Split('/',StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var order=QuestState.Instance.Order;
            var selected=new Bots.Quest.QuestOrder.QuestOrder{Nodes=order.Nodes,CurrentBehavior=order.CurrentBehavior};
            if(wrapper=="If")
            {
                var node=new Styx.Logic.Profiles.Quest.IfNode();
                order.Nodes=new OrderNodeCollection{node};order.CurrentBehavior=new ForcedIf{IfNode=node,ActiveOrder=selected};
            }
            else
            {
                var node=new Styx.Logic.Profiles.Quest.WhileNode();
                order.Nodes=new OrderNodeCollection{node};order.CurrentBehavior=new ForcedWhile{WhileNode=node,ActiveOrder=selected};
            }
        }
        var damage=new CountingLeaf(RunStatus.Success);
        RoutineManager.Current=new CombatRoutine{RestBehavior=new CountingLeaf(RunStatus.Failure),PreCombatBuffBehavior=new CountingLeaf(RunStatus.Failure),
            PullBehavior=damage,HealBehavior=new CountingLeaf(RunStatus.Failure),CombatBuffBehavior=new CountingLeaf(RunStatus.Failure),CombatBehavior=damage,PullBuffBehavior=new CountingLeaf(RunStatus.Failure)};
        QuestBot.RootFactory=()=>new PrioritySelector(new CountingLeaf(RunStatus.Failure),LevelBot.CreateCombatBehavior(),new CountingLeaf(RunStatus.Failure),TargetingBranch(),
            new Decorator(_=>true,new CountingLeaf(RunStatus.Failure)),new CountingLeaf(RunStatus.Failure),new CountingLeaf(RunStatus.Failure));
        Func<bool> permission=()=>true;
        var root=new PublishedQuestRoot(()=>permission,()=>new[]{"absent"});
        root.Start(null);Check(root.Tick(null)==RunStatus.Running&&Control.RootQuest.Ticks==1,"hotspot fixture did not start the retained quest child");
        Targeting.Instance.FirstUnit=target;Targeting.Instance.TargetList.Add(target);
        return(actor,target,objective,root,damage);
    }
    private static void Pulse(PublishedQuestRoot root)
    {if(root.LastStatus!=RunStatus.Running)root.Start(null);root.Tick(null);}
    private static void Journey(bool flying,bool aggro,string nesting="")
    {
        var w=Arrange(flying,aggro,nesting);using var cleanup=new Cleanup(w.Root);
        Pulse(w.Root);
        Check(BotPoi.Current.Type==PoiType.Kill&&ReferenceEquals(BotPoi.Current.AsObject,w.Target),"required mob never acquired the Kill POI from running hotspot travel");
        var committed=BotPoi.Current;
        for(int i=0;i<3;i++)Pulse(w.Root);
        Check(ReferenceEquals(BotPoi.Current,committed)&&Control.RootQuest.Ticks==1&&w.Damage.Ticks==0,"quest hotspot resumed or attacked before grounded mount acknowledgement");
        if(flying)Check(Control.DismountSubmissions==0,"required target caused an airborne dismount");
        w.Actor.IsFlying=false;w.Actor.ObservedMovementFlags=0;w.Actor.Location=new(100,10,0);
        Pulse(w.Root);Pulse(w.Root);
        Check(Control.DismountSubmissions==1&&w.Damage.Ticks==0,"safe landing did not retain exactly one pending dismount");
        w.Actor.Mounted=false;
        for(int i=0;i<5&&w.Damage.Ticks==0;i++)Pulse(w.Root);
        Check(w.Damage.Ticks>0&&Control.DismountSubmissions==1&&Control.RootQuest.Ticks==1,
            "observed unmount failed to release required combat; damage="+w.Damage.Ticks+" poi="+BotPoi.Current.Type
            +" displayed="+w.Actor.CurrentTargetGuid+" quest="+Control.RootQuest.Ticks+" state="+w.Root.LastStatus
            +" canPull="+typeof(LevelBot).GetMethod("CanPull",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)
            +" dismount="+Control.DismountSubmissions);
        ((Bots.Quest.Objectives.GrindObjective)w.Objective.Objective).IsCompleted=true;
        w.Actor.Combat=false;w.Actor.CurrentTarget=null;Targeting.Instance.FirstUnit=null;Targeting.Instance.TargetList.Clear();BotPoi.Current=new BotPoi(PoiType.None);
        Pulse(w.Root);Pulse(w.Root);
        Check(Control.RootQuest.Ticks>1,"cleared combat obligation failed to return scheduling to quest travel");
    }
    private static void Veto(string veto)
    {
        var w=Arrange(true,false);using var cleanup=new Cleanup(w.Root);
        var objective=(Bots.Quest.Objectives.GrindObjective)w.Objective.Objective;
        switch(veto)
        {
            case "unrelated":objective.RequiredEntry=99;break;
            case "complete":objective.IsCompleted=true;break;
            case "prerequisite":objective.DonePrerequisites=false;break;
            case "farming":Styx.Helpers.LevelbotSettings.Instance.GroundMountFarmingMode=true;break;
            case "service":BotPoi.Current=new BotPoi(new WoWPoint(300,10,0),PoiType.Repair);break;
            case "player":w.Target.IsPlayer=true;break;
            case "range":w.Target.Location=new(150,10,0);break;
            case "dead":w.Target.IsAlive=false;break;
            case "replacement":Control.OnTarget=_=>QuestState.Instance.Order.CurrentBehavior=new ForcedQuestObjective{Objective=new Bots.Quest.Objectives.GrindObjective{RequiredEntry=19349}};break;
        }
        Pulse(w.Root);Check(BotPoi.Current.Type!=PoiType.Kill&&Control.DismountSubmissions==0&&w.Damage.Ticks==0,"vetoed obligation acquired combat");
    }
    private sealed class Cleanup(PublishedQuestRoot root):IDisposable {public void Dispose()=>root.Stop(null);}
}

namespace Bots.Quest.Objectives
{
    public abstract class QuestObjective {public bool IsCompleted,DonePrerequisites=true;public virtual bool IsRequiredCombatTarget(WoWUnit target)=>false;}
    public sealed partial class GrindObjective:QuestObjective
    {
        public uint RequiredEntry;private bool IsMobObjective(WoWUnit unit)=>unit.Entry==RequiredEntry;
    }
}
namespace Bots.Quest.QuestOrder { public sealed class ForcedQuestObjective:ForcedBehavior {public Bots.Quest.Objectives.QuestObjective Objective;} }
