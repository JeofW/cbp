#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using Bots.Grind;
using Bots.Quest;
using Harness;
using Levelbot.Actions.Combat;
using Singular;
using Singular.Dynamics;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

internal static class Program
{
    private const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static int passed,failed,unexpected;
    private static void Main()
    {
        Case("ground committed Kill requests transition",GroundKillTransition);
        Case("full PublishedQuestRoot preempts Running quest on mounted committed Kill",FullRootKillPreemption);
        foreach(bool committed in new[]{false,true})
            Case("already-grounded combat avoids rebuilding landing owner / "+committed,()=>AlreadyGroundedCurrent(committed));
        Case("already-grounded explicit pull avoids rebuilding landing owner",AlreadyGroundedExplicit);
        Case("ground UNKNOWN retains the full transition",UnknownGroundUsesTransition);
        foreach(string replacement in new[]{"actor","run","provider","memory","poi"})
            Case("initial ground admission rejects "+replacement+" replacement",()=>InitialGroundReplacement(replacement));
        foreach(bool successor in new[]{false,true})
            Case("initial ground admission reentry preserves lifetime / successor="+successor,()=>InitialGroundReentry(successor));
        Case("flying committed Kill preempts root and blocks pull",FlyingKillTransition);
        Case("healthy flying incidental aggro keeps escape",HealthyFlyingEscape);
        Case("healthy ground-mounted incidental aggro keeps escape",HealthyGroundEscape);
        Case("healthy ascending flying aggro keeps escape",()=>HealthyFlyingFlagEscape(0x02400000u));
        Case("healthy descending flying aggro keeps escape",()=>HealthyFlyingFlagEscape(0x02800000u));
        Case("critical flying aggro preempts",CriticalFlyingAggro);
        Case("flying no-progress aggro preempts",FlyingNoProgress);
        Case("rooted ground aggro preempts",RootedAggro);
        Case("stunned ground aggro preempts",StunnedAggro);
        Case("ascending flying Kill remains transition-pending",()=>FlyingFlagPending(0x02400000u));
        Case("descending flying Kill remains transition-pending",()=>FlyingFlagPending(0x02800000u));
        Case("airborne observation after forced unmount remains pending",AirborneAfterForcedUnmount);
        Case("forced unmount releases pull only after observation",ForcedUnmountPull);
        Case("retained original owner becomes Ready after forced unmount",RetainedOriginalOwnerReady);
        Case("same committed target coordinate revision retains transition",SameCommittedTargetMoveRetains);
        foreach(string replacement in new[]{"actor","run","provider","memory","poi"})
            Case("retained transition rejects "+replacement+" replacement before Ready",()=>RetainedReadyReplacement(replacement));
        Case("reentrant transition cancel preserves successor lifetime",ReentrantCancelPreservesSuccessor);
        Case("player-only protective combat transitions",()=>ProtectiveOnly(false));
        Case("pet-only protective combat transitions",()=>ProtectiveOnly(true));
        Case("player-only protective landing is local and targetless",()=>ProtectiveLocalLanding("player"));
        Case("pet-only protective landing is local and targetless",()=>ProtectiveLocalLanding("pet"));
        Case("combined protective landing is local and targetless",()=>ProtectiveLocalLanding("both"));
        Case("protective landing without display ignores old travel destination",ProtectiveLocalLandingWithoutDisplay);
        Case("player clears while exact pet persists",PlayerClearsPetPersists);
        Case("pet replacement with same GUID/base revokes protective owner",()=>PetOwnershipMutation("replacement"));
        Case("pet GUID mutation revokes protective owner",()=>PetOwnershipMutation("guid"));
        Case("pet base mutation revokes protective owner",()=>PetOwnershipMutation("base"));
        Case("pet death revokes protective owner",()=>PetOwnershipMutation("death"));
        Case("unrelated display target movement does not revoke protective owner",()=>DisplayTargetMutation("move"));
        Case("unrelated display target change does not revoke protective owner",()=>DisplayTargetMutation("change"));
        Case("unrelated display target death does not revoke protective owner",()=>DisplayTargetMutation("death"));
        Case("player combat cleared revokes player-only protective owner",()=>CombatCleared("player"));
        Case("pet combat cleared revokes pet-only protective owner",()=>CombatCleared("pet"));
        Case("clear combat flags revoke protective transition",ClearThreatRevokes);
        Case("changed target/POI cannot repeat pending dismount",ChangedIntentNoRepeatDismount);
        Case("unchanged intent cannot repeat pending dismount",SameIntentNoRepeatDismount);
        Case("dead target revokes committed transition",DeadTargetRevokes);
        Case("cleared committed POI revokes without invented evade API",ClearedPoiRevokes);
        Case("unknown movement denies attack and stays pending",UnknownMovementPending);
        Case("melee-range pull remains allowed on observed ground",()=>RangeCase(5,true,true));
        Case("ranged pull remains allowed inside configured range",()=>RangeCase(25,true,true));
        Case("out-of-range pull remains denied",()=>RangeCase(35,true,false));
        Case("blocked LOS pull remains denied",()=>RangeCase(5,false,false));
        Case("flight-form QuestBot opportunistic targeting remains suppressed",FlightFormTargetingSuppressed);
        Case("ActionPull waits for transition then dispatches",ActionPullTransition);
        Case("ActionPull retains same target across coordinate-only POI revision",ActionPullMovingPoiRevision);
        Case("ActionPull retains tagged-by-other cleanup before transition",ActionPullTaggedCleanup);
        Case("ActionPull rejects callback context replacement",ActionPullCallbackReplacement);
        Case("ActionSetTarget waits for transition and revalidates",ActionSetTargetTransition);
        Case("ActionSetTarget retains same target across coordinate-only POI revision",ActionSetTargetMovingPoiRevision);
        Case("ActionSetTarget rejects target callback replacement",ActionSetTargetReplacement);
        Case("Singular direct Pull is guarded each tick",()=>SingularMountedGuard(true));
        Case("Singular direct Combat is guarded each tick",()=>SingularMountedGuard(false));
        Case("Singular mounted Heal remains independently available",SingularHealPreserved);
        Case("mounted pre-pull support work remains available outside attack guard",MountedSupportPreserved);
        Case("pre-pull better-target publication owns the incremented POI generation",BetterTargetPublication);
        Case("pre-pull publication cannot consume a callback successor POI",BetterTargetPublicationCallback);
        Case("LevelBot resumed pull rejects remount",LevelBotResumeRemount);
        Case("LevelBot resumed combat rejects remount",GroundCombatResumeRemount);
        foreach(var mutation in EpochMutations())
            Case("resumed pull rejects "+mutation.Name,()=>EpochMutation(mutation.Apply));
        Case("cancellation propagates from resumed attack admission",CancellationPropagates);
        Case("process loss propagates from resumed attack admission",ProcessLossPropagates);
        Case("executor loss propagates from resumed attack admission",ExecutorLossPropagates);
        Case("session Stop propagates from resumed attack admission",SessionStopPropagates);
        Console.WriteLine($"Mounted combat scenarios: {passed}/{passed+failed+unexpected}; assertions={failed}; unexpected={unexpected}; exact extracted combat/root/routine branches + actual actions/helper; controlled GroundTransition/world leaves; no game/native calls.");
        if(failed+unexpected!=0)Environment.ExitCode=1;
    }

    private sealed class Failure(string text):Exception(text);
    private static void Case(string name,Func<bool> body){try{Check(body(),name);passed++;Console.WriteLine("PASS mounted combat: "+name);}catch(Failure e){failed++;Console.Error.WriteLine("FAIL mounted combat: "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR mounted combat: "+name+": "+e);}}
    private static void Check(bool good,string text){if(!good)throw new Failure(text);}

    private static (LocalPlayer Me,WoWUnit Target) World(bool mounted=false,bool flying=false,bool combat=false,bool kill=false,double distance=10)
    {
        Control.Reset();
        var me=new LocalPlayer{Guid=1,BaseAddress=100,MapId=530,IsAlive=true,Mounted=false,IsFlying=false,Location=new WoWPoint(0,0,10),HealthPercent=100,ObservedMovementFlags=0};
        var target=new WoWUnit{Guid=2,BaseAddress=200,Entry=16863,IsAlive=true,Location=new WoWPoint((float)distance,0,10),InLineOfSpellSight=true};
        StyxWoW.Me=me;WoWMovement.ActiveMover=me;Targeting.Instance.FirstUnit=target;Targeting.Instance.TargetList.Add(target);me.CurrentTarget=target;
        BotPoi.Current=new BotPoi(PoiType.None);MountedCombatTransition.RequiresProtectiveHandoff(WoWPoint.Empty);
        me.Mounted=mounted;me.IsFlying=flying;me.Combat=combat;me.ObservedMovementFlags=flying?0x02000000u:0;
        BotPoi.Current=kill?new BotPoi(target,PoiType.Kill):new BotPoi(new WoWPoint(120,0,10),PoiType.QuestTurnIn);
        return(me,target);
    }

    private static CombatRoutine Routine(CountingLeaf pull=null,CountingLeaf combat=null,CountingLeaf heal=null)
        =>new(){RestBehavior=new CountingLeaf(RunStatus.Failure),PreCombatBuffBehavior=new CountingLeaf(RunStatus.Failure),PullBehavior=pull??new CountingLeaf(RunStatus.Failure),HealBehavior=heal??new CountingLeaf(RunStatus.Failure),CombatBuffBehavior=new CountingLeaf(RunStatus.Failure),CombatBehavior=combat??new CountingLeaf(RunStatus.Failure),PullBuffBehavior=new CountingLeaf(RunStatus.Failure)};

    private static RunStatus Tick(Composite c,bool start=true){if(start)c.Start(null);var r=c.Tick(null);if(r!=RunStatus.Running)c.Stop(null);return r;}
    private static PublishedQuestRoot NewRoot()=>new(()=>()=>true,()=>new[]{"absent"});
    private static int Protective()=> (int)typeof(PublishedQuestRoot).GetMethod("ProtectivePriority",Hidden).Invoke(NewRoot(),null);

    private static bool GroundKillTransition(){var w=World(true,false,false,true);return Protective()==1&&Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running&&Control.DismountSubmissions==1;}
    private static bool FullRootKillPreemption(){var w=World(true,false,false,false);BotPoi.Current=new BotPoi(new WoWPoint(120,0,10),PoiType.QuestTurnIn);var root=NewRoot();root.Start(null);Check(root.Tick(null)==RunStatus.Running&&Control.RootQuest.Ticks==1,"root quest did not enter Running");BotPoi.Current=new BotPoi(w.Target,PoiType.Kill);Control.RootCombatEnabled=true;var status=root.Tick(null);root.Stop(null);return Control.RootQuest.Stops>=1&&Control.RootCombat.Ticks>=2&&status==RunStatus.Success;}
    private static bool FlyingKillTransition(){var w=World(true,true,false,true);var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);return Protective()==1&&Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running&&pull.Ticks==0&&Control.DismountSubmissions==0;}
    private static bool HealthyFlyingEscape(){var w=World(true,true,true,false);return Protective()==int.MaxValue&&MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location)==false;}
    private static bool HealthyGroundEscape(){var w=World(true,false,true,false);return Protective()==int.MaxValue&&!MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location);}
    private static bool HealthyFlyingFlagEscape(uint flags){var w=World(true,true,true,false);w.Me.ObservedMovementFlags=flags;return Protective()==int.MaxValue&&!MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location);}
    private static bool CriticalFlyingAggro(){var w=World(true,true,true,false);w.Me.HealthPercent=20;return Protective()==1&&MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location);}
    private static bool FlyingNoProgress(){var w=World(true,true,true,false);Check(!MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location),"initial escape should be healthy");var p=typeof(MountedCombatTransition).GetField("EscapeProgress",Hidden).GetValue(null);foreach(var n in new[]{"movedAt","approachedAt"})p.GetType().GetField(n,Hidden).SetValue(p,Environment.TickCount64-10000);return MountedCombatTransition.RequiresProtectiveHandoff(BotPoi.Current.Location)&&Protective()==1;}
    private static bool RootedAggro(){var w=World(true,false,true,false);w.Me.Rooted=true;return Protective()==1;}
    private static bool StunnedAggro(){var w=World(true,false,true,false);w.Me.Stunned=true;return Protective()==1;}
    private static bool FlyingFlagPending(uint flags){var w=World(true,true,false,true);w.Me.ObservedMovementFlags=flags;return Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running&&Control.DismountSubmissions==0;}
    private static bool AirborneAfterForcedUnmount(){var w=World(false,false,false,true);w.Me.ObservedMovementFlags=0x02000000u;var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);return Protective()==1&&Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running&&pull.Ticks==0;}
    private static bool AlreadyGroundedCurrent(bool committed)
    {
        var world=World(false,false,!committed,committed);
        using var transition=new MountedCombatTransition();
        for(int tick=0;tick<20;tick++)
            Check(transition.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Ready,"complete ground admission did not release combat");
        return Control.TransitionTicks==0&&Control.DismountSubmissions==0;
    }
    private static bool AlreadyGroundedExplicit()
    {
        var world=World(false,false,false,true);
        using var transition=new MountedCombatTransition();
        Check(transition.TickExplicit(world.Target,()=>true)==GroundTransitionState.Ready,"complete ground admission did not release explicit pull");
        return Control.TransitionTicks==0&&Control.DismountSubmissions==0;
    }
    private static bool UnknownGroundUsesTransition()
    {
        var world=World(false,false,false,true);world.Me.MovementKnown=false;
        using var transition=new MountedCombatTransition();
        return transition.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending&&Control.TransitionTicks==1&&Control.DismountSubmissions==0;
    }
    private static bool ForcedUnmountPull(){var w=World(true,false,false,true);var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running&&pull.Ticks==0,"transition did not own mounted pull");w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;var r=tree.Tick(null);tree.Stop(null);return pull.Ticks==1&&r!=RunStatus.Running;}
    private static bool InitialGroundReplacement(string replacement)
    {
        var world=World(false,false,false,true);
        using var transition=new MountedCombatTransition();
        Control.OnGroundAdmission=()=>{
            switch(replacement)
            {
                case "actor":
                    var actor=new LocalPlayer{Guid=9,BaseAddress=900,MapId=530,IsAlive=true,Location=world.Me.Location,ObservedMovementFlags=0,CurrentTarget=world.Target};
                    StyxWoW.Me=actor;WoWMovement.ActiveMover=actor;break;
                case "run":TreeRoot.RunIdentity=new object();break;
                case "provider":Navigator.NavigationProvider=new object();break;
                case "memory":var memory=new GreenMagic.Memory{ProcessId=11,ProcessHandle=new IntPtr(11)};ObjectManager.Wow=memory;ObjectManager.Executor.Memory=memory;break;
                case "poi":BotPoi.Current=new BotPoi(world.Target,PoiType.Kill);break;
            }
        };
        var state=transition.TickCurrent(BotPoi.Current.Location);
        return state==GroundTransitionState.Revoked&&Control.DismountSubmissions==0;
    }
    private static bool InitialGroundReentry(bool successor)
    {
        var world=World(false,false,false,true);
        using var transition=new MountedCombatTransition();
        Control.OnGroundAdmission=()=>{
            transition.Cancel();
            if(successor){world.Me.Mounted=true;Check(transition.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"replacement transition did not start");}
        };
        Check(transition.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked,"old callback returned Ready after lifetime replacement");
        if(!successor)return Control.TransitionTicks==0&&Control.DismountSubmissions==0;
        Check(Control.TransitionTicks==1&&Control.DismountSubmissions==1,"old owner erased or duplicated successor transition");
        world.Me.Mounted=false;
        return transition.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Ready&&Control.DismountSubmissions==1;
    }
    private static bool RetainedOriginalOwnerReady(){var w=World(true,false,false,true);using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"retained transition did not start");w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Ready;}
    private static bool SameCommittedTargetMoveRetains()
    {
        var w=World(true,true,false,true);using var t=new MountedCombatTransition();long route=BotPoi.CurrentGeneration,work=BotPoi.CurrentWorkGeneration;
        Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"moving-target transition did not start");
        w.Target.Location=new WoWPoint(25,4,10);BotPoi.Current.Location=w.Target.Location;
        Check(BotPoi.CurrentGeneration==route+1&&BotPoi.CurrentWorkGeneration==work,"coordinate-only POI revision changed semantic work identity");
        return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending;
    }
    private static bool RetainedReadyReplacement(string replacement)
    {
        var w=World(true,false,false,true);using var t=new MountedCombatTransition();
        Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"retained transition did not start");
        switch(replacement)
        {
            case "actor":
                var actor=new LocalPlayer{Guid=9,BaseAddress=900,MapId=530,IsAlive=true,Mounted=false,Location=w.Me.Location,ObservedMovementFlags=0,CurrentTarget=w.Target};
                StyxWoW.Me=actor;WoWMovement.ActiveMover=actor;break;
            case "run":w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;TreeRoot.RunIdentity=new object();break;
            case "provider":w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;Navigator.NavigationProvider=new object();break;
            case "memory":
                w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;var memory=new GreenMagic.Memory{ProcessId=11,ProcessHandle=new IntPtr(11)};ObjectManager.Wow=memory;ObjectManager.Executor.Memory=memory;break;
            case "poi":w.Me.Mounted=false;w.Me.ObservedMovementFlags=0;BotPoi.Current=new BotPoi(w.Target,PoiType.Kill);break;
            default:throw new ArgumentOutOfRangeException(nameof(replacement));
        }
        return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;
    }
    private static bool ReentrantCancelPreservesSuccessor()
    {
        var w=World(true,false,false,true);using var t=new MountedCombatTransition();
        Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"old transition did not start");
        var next=new WoWUnit{Guid=4,BaseAddress=400,Entry=99,IsAlive=true,Location=new WoWPoint(7,0,10)};
        GroundTransitionState nested=GroundTransitionState.Revoked;
        Control.OnTransitionCancel=()=>
        {
            Control.OnTransitionCancel=null;Targeting.Instance.FirstUnit=next;Targeting.Instance.TargetList.Clear();Targeting.Instance.TargetList.Add(next);
            w.Me.CurrentTarget=next;BotPoi.Current=new BotPoi(next,PoiType.Kill);nested=t.TickCurrent(BotPoi.Current.Location);
        };
        t.Cancel();
        int ticks=Control.TransitionTicks;
        return nested==GroundTransitionState.Pending&&t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending&&Control.TransitionTicks==ticks+1;
    }
    private static bool ProtectiveOnly(bool pet){var w=World(true,true,!pet,false);if(pet){w.Me.Combat=false;w.Me.Pet=new WoWUnit{Guid=3,BaseAddress=300,IsAlive=true,Combat=true};}w.Me.HealthPercent=20;return Protective()==1&&Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running;}
    private static (LocalPlayer Me,WoWUnit Display,WoWUnit Pet,MountedCombatTransition Owner) ProtectiveWorld(string source,bool display=true)
    {
        var w=World(true,false,false,false);w.Me.HealthPercent=20;
        w.Me.Combat=source is "player" or "both";
        var pet=new WoWUnit{Guid=3,BaseAddress=300,Entry=416,IsAlive=true,Combat=source is "pet" or "both",Location=new WoWPoint(3,4,10)};
        w.Me.Pet=pet;
        if(!display)w.Me.CurrentTarget=null;
        return(w.Me,w.Target,pet,new MountedCombatTransition());
    }
    private static WoWPoint OwnerDestination(MountedCombatTransition owner)=>(WoWPoint)typeof(MountedCombatTransition).GetField("_destination",Hidden).GetValue(owner);
    private static WoWObject OwnerSubject(MountedCombatTransition owner)=>(WoWObject)typeof(MountedCombatTransition).GetField("_subject",Hidden).GetValue(owner);
    private static bool ProtectiveLocalLanding(string source)
    {
        var p=ProtectiveWorld(source);using var owner=p.Owner;
        WoWPoint travel=BotPoi.Current.Location;Check(travel!=p.Me.Location&&p.Display.Location!=p.Me.Location,"protective fixture lacks distinct travel/display locations");
        Check(owner.TickCurrent(travel)==GroundTransitionState.Pending,"protective owner did not start");
        return OwnerSubject(owner)==null&&OwnerDestination(owner).Equals(p.Me.Location)
            && !OwnerDestination(owner).Equals(p.Display.Location)&&!OwnerDestination(owner).Equals(travel);
    }
    private static bool ProtectiveLocalLandingWithoutDisplay()
    {
        var p=ProtectiveWorld("player",display:false);using var owner=p.Owner;WoWPoint travel=BotPoi.Current.Location;
        Check(owner.TickCurrent(travel)==GroundTransitionState.Pending,"targetless protective owner did not start");
        return OwnerSubject(owner)==null&&OwnerDestination(owner).Equals(p.Me.Location)&&!OwnerDestination(owner).Equals(travel);
    }
    private static bool PlayerClearsPetPersists()
    {
        var p=ProtectiveWorld("both");using var owner=p.Owner;Check(owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"combined protective owner did not start");
        p.Me.Combat=false;return owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending;
    }
    private static bool PetOwnershipMutation(string mutation)
    {
        var p=ProtectiveWorld("pet");using var owner=p.Owner;Check(owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"pet protective owner did not start");
        switch(mutation)
        {
            case "replacement":p.Me.Pet=new WoWUnit{Guid=p.Pet.Guid,BaseAddress=p.Pet.BaseAddress,Entry=p.Pet.Entry,IsAlive=true,Combat=true,Location=p.Pet.Location};break;
            case "guid":p.Pet.Guid++;break;
            case "base":p.Pet.BaseAddress++;break;
            case "death":p.Pet.IsAlive=false;break;
            default:throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;
    }
    private static bool DisplayTargetMutation(string mutation)
    {
        var p=ProtectiveWorld("pet");using var owner=p.Owner;Check(owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"pet protective owner did not start");
        switch(mutation)
        {
            case "move":p.Display.Location=new WoWPoint(55,66,10);break;
            case "change":p.Me.CurrentTarget=new WoWUnit{Guid=8,BaseAddress=800,Entry=77,IsAlive=true,Location=new WoWPoint(70,0,10)};break;
            case "death":p.Display.IsAlive=false;break;
            default:throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending
            && OwnerSubject(owner)==null&&OwnerDestination(owner).Equals(p.Me.Location);
    }
    private static bool CombatCleared(string source)
    {
        var p=ProtectiveWorld(source);using var owner=p.Owner;Check(owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"protective owner did not start");
        if(source=="player")p.Me.Combat=false;else p.Pet.Combat=false;
        return owner.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;
    }
    private static bool ClearThreatRevokes(){var w=World(true,false,true,false);w.Me.Rooted=true;using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"protective transition missing");w.Me.Combat=false;w.Me.Rooted=false;return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;}
    private static bool ChangedIntentNoRepeatDismount(){var w=World(true,false,false,true);using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending&&Control.DismountSubmissions==1,"initial pending dismount missing");var next=new WoWUnit{Guid=4,BaseAddress=400,Entry=99,IsAlive=true,Location=new WoWPoint(9,0,10)};Targeting.Instance.FirstUnit=next;w.Me.CurrentTarget=next;BotPoi.Current=new BotPoi(next,PoiType.Kill);Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked,"old intent not revoked");Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"replacement did not acquire transition");return Control.DismountSubmissions==1;}
    private static bool SameIntentNoRepeatDismount(){var w=World(true,false,false,true);using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"initial transition missing");Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"pending transition did not persist");return Control.DismountSubmissions==1;}
    private static bool DeadTargetRevokes(){var w=World(true,false,false,true);using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"initial transition missing");w.Target.IsAlive=false;return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;}
    private static bool ClearedPoiRevokes(){var w=World(true,false,false,true);using var t=new MountedCombatTransition();Check(t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Pending,"initial transition missing");BotPoi.Clear("controlled intent loss");return t.TickCurrent(BotPoi.Current.Location)==GroundTransitionState.Revoked;}
    private static bool UnknownMovementPending(){var w=World(true,false,false,true);w.Me.MovementKnown=false;var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);return Tick(LevelBot.CreateCombatBehavior())==RunStatus.Running&&pull.Ticks==0&&Control.DismountSubmissions==0;}
    private static bool RangeCase(double distance,bool los,bool expect){var w=World(false,false,false,true,distance);w.Target.InLineOfSpellSight=los;var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);Tick(LevelBot.CreateCombatBehavior());return (pull.Ticks==1)==expect;}
    private static bool FlightFormTargetingSuppressed(){var w=World(false,false,false,false);w.Me.Shapeshift=ShapeshiftForm.FlightForm;w.Me.IsMoving=true;w.Me.CurrentTarget=null;BotPoi.Current=new BotPoi(PoiType.None);var tree=(Composite)typeof(QuestBot).GetMethod("CreateTargetingBehavior",Hidden).Invoke(null,null);Tick(tree);return w.Me.CurrentTarget==null&&BotPoi.Current.Type==PoiType.None;}
    private static bool ActionPullTransition(){var w=World(true,true,false,true);var action=new ActionPull();action.Start(null);Check(action.Tick(null)==RunStatus.Running&&Control.PullCalls==0,"airborne pull dispatched");w.Me.Mounted=false;w.Me.IsFlying=false;w.Me.ObservedMovementFlags=0;var r=action.Tick(null);action.Stop(null);return r!=RunStatus.Running&&Control.PullCalls==1;}
    private static bool ActionPullMovingPoiRevision(){var w=World(true,true,false,true);var action=new ActionPull();action.Start(null);try{Check(action.Tick(null)==RunStatus.Running&&Control.PullCalls==0,"airborne pull did not enter pending transition");long work=BotPoi.CurrentWorkGeneration;w.Target.Location=new WoWPoint(18,3,10);BotPoi.Current.Location=w.Target.Location;return BotPoi.CurrentWorkGeneration==work&&action.Tick(null)==RunStatus.Running&&Control.PullCalls==0;}finally{action.Stop(null);}}
    private static bool ActionPullTaggedCleanup(){var w=World(true,false,false,true);w.Target.TaggedByOther=true;w.Target.TaggedByMe=false;var r=Tick(new ActionPull());return r==RunStatus.Failure&&w.Me.CurrentTarget==null&&Control.TransitionTicks==0&&Control.PullCalls==0;}
    private static bool ActionPullCallbackReplacement(){var w=World(false,false,false,true);RoutineManager.Current=Routine();Control.OnPull=()=>ObjectManager.Wow=new GreenMagic.Memory{ProcessId=11,ProcessHandle=new IntPtr(11)};var action=new ActionPull();var r=Tick(action);return r==RunStatus.Failure&&Control.PullCalls==1;}
    private static bool ActionSetTargetTransition(){var w=World(true,true,false,false);w.Me.CurrentTarget=null;var action=new ActionSetTarget();action.Start(null);Check(action.Tick(null)==RunStatus.Running&&Control.NavigatorClears==0,"airborne target action completed early");w.Me.Mounted=false;w.Me.IsFlying=false;w.Me.ObservedMovementFlags=0;var r=action.Tick(null);action.Stop(null);return r==RunStatus.Success&&ReferenceEquals(w.Me.CurrentTarget,w.Target)&&Control.NavigatorClears==1;}
    private static bool ActionSetTargetMovingPoiRevision(){var w=World(true,true,false,false);BotPoi.Current=new BotPoi(w.Target,PoiType.Kill);w.Me.CurrentTarget=w.Target;var action=new ActionSetTarget();action.Start(null);try{Check(action.Tick(null)==RunStatus.Running&&Control.NavigatorClears==0,"airborne target action did not enter pending transition");long work=BotPoi.CurrentWorkGeneration;w.Target.Location=new WoWPoint(17,2,10);BotPoi.Current.Location=w.Target.Location;return BotPoi.CurrentWorkGeneration==work&&action.Tick(null)==RunStatus.Running&&Control.NavigatorClears==0;}finally{action.Stop(null);}}
    private static bool ActionSetTargetReplacement(){var w=World(true,false,false,false);w.Me.CurrentTarget=null;Control.OnTarget=_=>StyxWoW.Me=new LocalPlayer{Guid=9,BaseAddress=900,MapId=530,IsAlive=true};return Tick(new ActionSetTarget())==RunStatus.Failure;}

    private static SingularRoutine SingularWith(CountingLeaf pull=null,CountingLeaf combat=null,CountingLeaf heal=null){Control.SingularFactories[BehaviorType.Combat]=combat??new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.Pull]=pull??new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.Rest]=new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.CombatBuffs]=new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.Heal]=heal??new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.PullBuffs]=new CountingLeaf(RunStatus.Failure);Control.SingularFactories[BehaviorType.PreCombatBuffs]=new CountingLeaf(RunStatus.Failure);var s=new SingularRoutine();Check(s.CreateBehaviors(),"Singular construction failed");return s;}
    private static bool SingularMountedGuard(bool pull){var w=World(true,true,false,false);var leaf=new CountingLeaf(RunStatus.Success);var s=pull?SingularWith(pull:leaf):SingularWith(combat:leaf);Tick(pull?s.PullBehavior:s.CombatBehavior);return leaf.Ticks==0;}
    private static bool SingularHealPreserved(){var w=World(true,true,true,false);var heal=new CountingLeaf(RunStatus.Success);var s=SingularWith(heal:heal);Tick(s.HealBehavior);return heal.Ticks==1;}
    private static bool MountedSupportPreserved(){var w=World(true,false,false,false);var rest=new CountingLeaf(RunStatus.Success);var routine=Routine();routine.RestBehavior=rest;RoutineManager.Current=routine;Tick(LevelBot.CreateCombatBehavior());return rest.Ticks==1;}
    private static bool BetterTargetPublication()
    {
        var w=World(false,false,false,false);var old=new WoWUnit{Guid=5,BaseAddress=500,Entry=55,IsAlive=true,Location=new WoWPoint(6,0,10)};
        w.Me.CurrentTarget=old;BotPoi.Current=new BotPoi(old,PoiType.Kill);long before=BotPoi.CurrentGeneration;
        var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);Tick(LevelBot.CreateCombatBehavior());
        Check(BotPoi.CurrentGeneration==before+1,$"better-target publication generation changed by {BotPoi.CurrentGeneration-before}, expected 1");
        Check(ReferenceEquals(BotPoi.Current.AsObject,w.Target),"better-target publication did not retain the selected POI subject");
        Check(ReferenceEquals(w.Me.CurrentTarget,w.Target),"better-target publication did not retarget the selected unit");
        Check(pull.Ticks==0,"successful target-switch branch pulled in the same selector cycle");
        Tick(LevelBot.CreateCombatBehavior());
        Check(pull.Ticks==1,$"next valid cycle reached pull {pull.Ticks} times, expected once");
        return true;
    }
    private static bool BetterTargetPublicationCallback()
    {
        var w=World(false,false,false,false);var old=new WoWUnit{Guid=5,BaseAddress=500,Entry=55,IsAlive=true,Location=new WoWPoint(6,0,10)};
        w.Me.CurrentTarget=old;BotPoi.Current=new BotPoi(old,PoiType.Kill);var replacement=new BotPoi(new WoWPoint(40,0,10),PoiType.Repair);var pull=new CountingLeaf(RunStatus.Success);RoutineManager.Current=Routine(pull:pull);
        Control.OnPoiChanged=published=>{if(published.Type!=PoiType.Kill||!ReferenceEquals(published.AsObject,w.Target))return;Control.OnPoiChanged=null;BotPoi.Current=replacement;};
        Tick(LevelBot.CreateCombatBehavior());
        return ReferenceEquals(BotPoi.Current,replacement)&&ReferenceEquals(w.Me.CurrentTarget,old)&&pull.Ticks==0;
    }
    private static bool LevelBotResumeRemount(){var w=World(false,false,false,true);var pull=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:pull);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running&&pull.Ticks==1,"pull did not enter running");w.Me.Mounted=true;var r=tree.Tick(null);tree.Stop(null);return pull.Ticks==1;}
    private static bool GroundCombatResumeRemount(){var w=World(false,false,true,false);var combat=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(combat:combat);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running&&combat.Ticks==1,"combat did not enter running");w.Me.Mounted=true;var r=tree.Tick(null);tree.Stop(null);return combat.Ticks==1;}

    private sealed record Mutation(string Name,System.Action Apply);
    private static IEnumerable<Mutation> EpochMutations()
    {
        yield return new("actor reference",()=>StyxWoW.Me=new LocalPlayer{Guid=9,BaseAddress=900,MapId=530,IsAlive=true});
        yield return new("actor base",()=>StyxWoW.Me.BaseAddress++);yield return new("actor GUID",()=>StyxWoW.Me.Guid++);yield return new("actor map",()=>StyxWoW.Me.MapId++);
        yield return new("memory reference",()=>ObjectManager.Wow=new GreenMagic.Memory{ProcessId=10,ProcessHandle=new IntPtr(10)});yield return new("memory process",()=>ObjectManager.Wow.ProcessId++);
        yield return new("executor",()=>ObjectManager.Executor=new GreenMagic.ExecutorRand{Memory=ObjectManager.Wow,IsOpen=true,IsInitialized=true});yield return new("profile",()=>ProfileManager.CurrentProfileSnapshot=new object());
        yield return new("POI publication",()=>BotPoi.Current=new BotPoi(StyxWoW.Me.CurrentTarget,PoiType.Kill));yield return new("run",()=>TreeRoot.RunIdentity=new object());yield return new("active mover",()=>WoWMovement.ActiveMover=new WoWUnit{Guid=77,BaseAddress=770,IsAlive=true});
        yield return new("navigation provider",()=>Navigator.NavigationProvider=new object());yield return new("player mover",()=>Navigator.PlayerMover=new object());
        yield return new("target reference",()=>{var next=new WoWUnit{Guid=88,BaseAddress=880,IsAlive=true,Location=new WoWPoint(5,0,10)};StyxWoW.Me.CurrentTarget=next;Targeting.Instance.FirstUnit=next;});
        yield return new("target base",()=>StyxWoW.Me.CurrentTarget.BaseAddress++);yield return new("target GUID",()=>StyxWoW.Me.CurrentTarget.Guid++);yield return new("target death",()=>StyxWoW.Me.CurrentTarget.IsAlive=false);
    }
    private static bool EpochMutation(System.Action mutation){var w=World(false,false,false,true);var pull=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:pull);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running&&pull.Ticks==1,"running pull setup failed");mutation();try{tree.Tick(null);}finally{tree.Stop(null);}return pull.Ticks==1;}
    private static bool CancellationPropagates(){var w=World(false,false,false,true);var leaf=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:leaf);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running,"setup failed");leaf.BeforeTick=i=>{if(i==2)throw new OperationCanceledException("controlled");};bool threw=false;try{tree.Tick(null);}catch(OperationCanceledException){threw=true;}finally{tree.Stop(null);}return threw;}
    private static bool ProcessLossPropagates(){var w=World(false,false,false,true);var leaf=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:leaf);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running,"setup failed");ObjectManager.Wow.ProcessHandle=IntPtr.Zero;bool threw=false;try{tree.Tick(null);}catch(OperationCanceledException e)when(e.InnerException is Styx.InvalidProcessException){threw=true;}finally{ObjectManager.Wow.ProcessHandle=new IntPtr(10);tree.Stop(null);}return threw;}
    private static bool ExecutorLossPropagates(){var w=World(false,false,false,true);var leaf=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:leaf);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running,"setup failed");ObjectManager.Executor.IsOpen=false;bool threw=false;try{tree.Tick(null);}catch(OperationCanceledException e)when(e.InnerException is Styx.InvalidExecutorException){threw=true;}finally{ObjectManager.Executor.IsOpen=true;tree.Stop(null);}return threw;}
    private static bool SessionStopPropagates(){var w=World(false,false,false,true);var leaf=new CountingLeaf(_=>RunStatus.Running);RoutineManager.Current=Routine(pull:leaf);var tree=LevelBot.CreateCombatBehavior();tree.Start(null);Check(tree.Tick(null)==RunStatus.Running,"setup failed");TreeRoot.IsRunning=false;bool threw=false;try{tree.Tick(null);}catch(OperationCanceledException){threw=true;}finally{TreeRoot.IsRunning=true;tree.Stop(null);}return threw;}
}
