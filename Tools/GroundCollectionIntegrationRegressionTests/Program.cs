using CommonBehaviors.Actions;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.POI;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

int passed=0,total=0;
var failures=new List<string>();
WoWGameObject Egg(bool flying=false,float originHeight=0)
{
    World.Reset();World.ObjectMode=true;World.Sight=false;World.GroundZ=23.216196f;
    World.Actor.Position=new(-1450.16455f,3374.43042f,World.GroundZ+(flying?4.301733f:0));
    World.Actor.MountedValue=flying;World.Actor.Flags=flying?0x02000000u:0;
    var egg=new WoWGameObject { Guid=0xF11002C489010B39UL,BaseAddress=300,Entry=181385,
        Position=new(-1449.17004f,3374.82007f,World.GroundZ+originHeight) };
    BotPoi.Current=new BotPoi{Type=PoiType.Loot,Guid=egg.Guid,Entry=egg.Entry,Object=egg,Position=egg.Position};
    BotPoi.CurrentGeneration++;
    return egg;
}
void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
void Case(string name,System.Action body)
{
    total++;
    try{body();passed++;}
    catch(Exception error){failures.Add(name+": "+error.Message);Console.Error.WriteLine("FAIL actual collection: "+name+": "+error);}
    finally{World.Callback=null;Styx.BotEvents.Stop();}
}
void NoInteraction(string stage)=>Check(World.Interactions.Count==0,stage+" admitted native use");
Case("logged manual dismount uses native object usability instead of model-centre ray",()=>
{
    var egg=Egg(originHeight:.249304f);
    var action=new GroundLootApproach(()=>true);action.Start(null!);
    try
    {
        Check(action.Tick(null!)==RunStatus.Failure,"grounded in-range egg did not release the selected loot owner");
        Check(GroundLootApproach.CanInteractNow(egg,()=>true),"shared interaction gate still vetoed the object model");
        Check(GroundTransition.TryInteractWith(egg,()=>true,true),"owned native request was not submitted");
        Check(World.Interactions.SequenceEqual(new[]{egg.Guid})&&World.Dismounts==0,"interaction lost exact egg identity or dismounted again");
    }
    finally{action.Stop(null!);}
});
foreach(float height in new[]{-2.17852f,.249304f,3f})
Case("flight resolves actual supported object interaction volume / z="+height,()=>
{
    var egg=Egg(true,height);
    using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
    for(int tick=0;tick<48&&World.ExteriorFlights.Count+World.Descents==0;tick++)
        Check(owner.Tick(egg.Location,egg,()=>true)==GroundTransitionState.Pending,"flight search did not retain the object intent");
    Check(World.ExteriorFlights.Count+World.Descents>0,"valid ground within object range was rejected as the object's non-walkable model origin");
    NoInteraction("flight");Check(World.Dismounts==0,"flight request was treated as supported arrival");
    if(World.ExteriorFlights.Count>0){World.Actor.Position=World.ExteriorFlights[^1];owner.Tick(egg.Location,egg,()=>true);}
    var landing=GroundTransitionRuntime.LastObservation?.Landing;
    Check(landing.HasValue,"actual transition did not retain its measured landing");
    World.Actor.Position=landing!.Value;World.Actor.Flags=0;
    owner.Tick(egg.Location,egg,()=>true);owner.Tick(egg.Location,egg,()=>true);
    Check(World.Dismounts==1,"supported landing did not submit exactly one owned removal");
    NoInteraction("pending dismount");owner.Tick(egg.Location,egg,()=>true);
    Check(World.Dismounts==1,"unacknowledged dismount was repeated");
    World.Actor.MountedValue=false;
    Check(owner.Tick(egg.Location,egg,()=>true)==GroundTransitionState.Ready,"observed ground and mount loss failed to reach object readiness");
    Check(GroundTransition.TryInteractWith(egg,()=>true,true),"ready object was not submitted at its exact native recipient");
    Check(World.Interactions.SequenceEqual(new[]{egg.Guid}),"native recipient differs from selected egg");
});
Case("actual shared movement rate is bounded while collection retains the same object",()=>
{
    var egg=Egg();World.Actor.Position=egg.Position.Add(-10,0,0);
    double time=100;GroundTransitionRuntime.MonotonicClockOverride=()=>time;
    var action=new GroundLootApproach(()=>true);action.Start(null!);
    try
    {
        for(int i=0;i<8;i++){time+=.01;Check(action.Tick(null!)==RunStatus.Running,"pending route lost its selected object");}
        Check(World.Walks.Count==1&&World.Interactions.Count==0,"repeated caller ticks flooded actual mesh requests or invented interaction");
        time+=.25;action.Tick(null!);Check(World.Walks.Count==2,"bounded movement did not resume after its interval");
    }
    finally{action.Stop(null!);GroundTransitionRuntime.MonotonicClockOverride=null;}
});
foreach(string hazard in new[]{"unusable","disabled","zero-range","out-of-range","mounted","flying","falling","transport","vehicle","dead","missing-movement","blacklisted"})
Case("selected object admission denies "+hazard,()=>
{
    var egg=Egg();
    switch(hazard)
    {
        case "unusable":World.ObjectUsable=false;break;case "disabled":egg.IsDisabled=true;break;
        case "zero-range":egg.Range=0;break;case "out-of-range":egg.Position=egg.Position.Add(20,0,0);break;
        case "mounted":World.Actor.MountedValue=true;break;case "flying":World.Actor.Flags=0x02000000u;break;
        case "falling":World.Actor.Flags=0x3000u;break;case "transport":World.Actor.Transport=1;break;
        case "vehicle":World.Actor.InVehicle=true;break;case "dead":World.Actor.IsAlive=false;break;
        case "missing-movement":World.Actor.MovementKnown=false;break;case "blacklisted":Blacklist.Items.Add(egg.Guid);break;
    }
    Check(!GroundLootApproach.CanInteractNow(egg,()=>true),"negative/unknown admission became usable object authority");NoInteraction(hazard);
});
foreach(string hazard in new[]{"missing-support","liquid-surface","missing-mesh"})
Case("actual selected-object flight never lands on "+hazard,()=>
{
    var egg=Egg(true);
    World.MissingSupport=hazard=="missing-support";World.Liquid=hazard=="liquid-surface";World.MissingMesh=hazard=="missing-mesh";
    using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
    for(int i=0;i<12;i++)owner.Tick(egg.Location,egg,()=>true);
    Check(World.Descents==0&&World.Dismounts==0&&World.Interactions.Count==0,"invalid landing evidence generated a destructive transition effect");
});
foreach(string mutation in new[]{"actor","run","memory","poi","subject-address","subject-position","mount","caller"})
Case("prepared native interaction rechecks "+mutation,()=>
{
    var egg=Egg();bool admitted=true;
    World.Callback=stage=>
    {
        if(stage!="interaction-prepare")return;World.Callback=null;
        switch(mutation)
        {
            case "actor":ObjectManager.Me=new LocalPlayer{Guid=1,BaseAddress=100,Position=World.Actor.Position};break;
            case "run":Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();break;
            case "memory":ObjectManager.Wow=new GreenMagic.Memory();break;
            case "poi":BotPoi.Current=new();BotPoi.CurrentGeneration++;break;
            case "subject-address":egg.BaseAddress++;break;case "subject-position":egg.Position=egg.Position.Add(1,0,0);break;
            case "mount":World.Actor.MountedValue=true;break;case "caller":admitted=false;break;
        }
    };
    Check(!GroundTransition.TryInteractWith(egg,()=>admitted,true),"stale prepared native command was accepted");NoInteraction(mutation);
    Check(World.Callback==null,"test did not reach the actual native prepare boundary");
});
foreach(bool flying in new[]{false,true})
foreach(PoiType kind in new[]{PoiType.Loot,PoiType.Skin})
Case("actual corpse approach lands and observes dismount/"+flying+"/"+kind,()=>
{
    var location=Egg(flying).Position;World.ObjectMode=false;World.Sight=true;World.Actor.MountedValue=true;
    var corpse=new WoWUnit{Guid=88,BaseAddress=8800,Entry=18137,IsAlive=false,CanLoot=true,CanSkin=kind==PoiType.Skin,Position=location};
    BotPoi.Current=new BotPoi{Type=kind,Guid=corpse.Guid,Entry=corpse.Entry,Object=corpse,Position=location};BotPoi.CurrentGeneration++;
    var action=new GroundLootApproach(()=>true);action.Start(null!);
    try
    {
        for(int pulse=0;pulse<48&&World.ExteriorFlights.Count+World.Descents+World.Dismounts==0;pulse++)
            Check(action.Tick(null!)==RunStatus.Running,"mounted corpse work was released before ground admission");
        NoInteraction("corpse travel");
        if(flying)
        {
            Check(World.Dismounts==0,"airborne corpse approach requested mount removal");
            if(World.ExteriorFlights.Count>0){World.Actor.Position=World.ExteriorFlights[^1];action.Tick(null!);}
            var landing=GroundTransitionRuntime.LastObservation?.Landing;
            Check(landing.HasValue,"corpse approach did not retain an observed landing");
            World.Actor.Position=landing!.Value;World.Actor.Flags=0;
            action.Tick(null!);action.Tick(null!);
        }
        Check(World.Dismounts==1,"supported corpse approach did not request exactly one removal");
        for(int pulse=0;pulse<3;pulse++)Check(action.Tick(null!)==RunStatus.Running,"pending removal was treated as acknowledgement");
        Check(World.Dismounts==1,"pending corpse dismount was repeated");
        World.Actor.MountedValue=false;
        Check(action.Tick(null!)==RunStatus.Failure,"observed unmount did not release corpse interaction ownership");
        NoInteraction("wrapper readiness");
    }
    finally{action.Stop(null!);}
});
foreach(string change in new[]{"poi","revived","caller"})
Case("actual corpse approach retires its pending route/"+change,()=>
{
    var position=Egg(true).Position;World.ObjectMode=false;World.Sight=true;
    var corpse=new WoWUnit{Guid=88,BaseAddress=8800,Entry=18137,IsAlive=false,Position=position};
    BotPoi.Current=new BotPoi{Type=PoiType.Loot,Guid=corpse.Guid,Entry=corpse.Entry,Object=corpse,Position=position};BotPoi.CurrentGeneration++;
    bool admitted=true;var action=new GroundLootApproach(()=>admitted);action.Start(null!);
    try
    {
        for(int pulse=0;pulse<48&&World.ExteriorFlights.Count+World.Descents==0;pulse++)action.Tick(null!);
        Check(World.ExteriorFlights.Count+World.Descents>0,"corpse route never owned movement");
        int flights=World.ExteriorFlights.Count,descents=World.Descents;
        if(change=="poi"){BotPoi.Current=new();BotPoi.CurrentGeneration++;}
        else if(change=="revived")corpse.IsAlive=true;else admitted=false;
        action.Tick(null!);
        Check(World.ExteriorFlights.Count==flights&&World.Descents==descents&&World.Dismounts==0,"obsolete corpse ownership issued additional travel or removal");
        NoInteraction("revoked corpse");
    }
    finally{action.Stop(null!);}
});
Console.WriteLine($"Actual ground collection integration: {passed}/{total}; actual selected-source controller, shared transition/runtime/geometry, owned interaction; native observations and replies controlled, no live realm proof.");
if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
