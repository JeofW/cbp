using System.Runtime.CompilerServices;
using Bots.Grind;
using Bots.Quest;
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
using Action=System.Action;

internal static class LootPriorityCases
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Case(string name,Action test)
        {
            total++;Control.Reset();
            var actor=new LocalPlayer{Guid=1,BaseAddress=100,MapId=530,Location=new(0,0,10)};
            StyxWoW.Me=actor;WoWMovement.ActiveMover=actor;BotPoi.Current=new(PoiType.None);
            RoutineManager.Current=new CombatRoutine{
                RestBehavior=new CountingLeaf(RunStatus.Failure),PreCombatBuffBehavior=new CountingLeaf(RunStatus.Success),
                PullBehavior=new CountingLeaf(RunStatus.Failure),HealBehavior=new CountingLeaf(RunStatus.Failure),
                CombatBuffBehavior=new CountingLeaf(RunStatus.Failure),CombatBehavior=new CountingLeaf(RunStatus.Failure)};
            try{test();passed++;Console.WriteLine("PASS loot priority: "+name);}
            catch(Exception error){Console.Error.WriteLine("FAIL loot priority: "+name+": "+error.Message);}
        }
        foreach(PoiType type in new[]{PoiType.Loot,PoiType.Skin,PoiType.Harvest})
            Case("owned "+type+" is not delayed by a fresh optional buff",()=>
            {
                BotPoi.Current=new(Corpse(),type);var buffs=(CountingLeaf)RoutineManager.Current.PreCombatBuffBehavior;
                Once(LevelBot.CreateCombatBehavior());Check(buffs.Ticks==0,"optional pre-combat buff overtook selected collection work");
            });
        Case("a ready corpse overtakes optional buffs before POI selection",()=>
        {
            LootTargeting.Instance.FirstObject=Corpse();var buffs=(CountingLeaf)RoutineManager.Current.PreCombatBuffBehavior;
            Once(LevelBot.CreateCombatBehavior());Check(buffs.Ticks==0,"fresh corpse waited for optional buffing before becoming loot work");
        });
        Case("a newly available corpse retires a yielded optional buff",()=>
        {
            var buffs=new CountingLeaf(RunStatus.Running);RoutineManager.Current.PreCombatBuffBehavior=buffs;
            var tree=LevelBot.CreateCombatBehavior();tree.Start(null);try{
                Check(tree.Tick(null)==RunStatus.Running&&buffs.Ticks==1,"buff fixture did not yield");
                LootTargeting.Instance.FirstObject=Corpse();tree.Tick(null);
                Check(buffs.Ticks==1&&buffs.Stops==1,"resumed optional buff ignored newly available corpse work");
            }finally{tree.Stop(null);}
        });
        Case("ready corpse preempts retained quest movement on its next pulse",()=>
        {
            RoutineManager.Current.PreCombatBuffBehavior=new CountingLeaf(RunStatus.Failure);
            var movement=new CountingLeaf(RunStatus.Running);var loot=new CountingLeaf(RunStatus.Success);int addonReads=0;
            QuestBot.RootFactory=()=>new PrioritySelector(new CountingLeaf(RunStatus.Failure),LevelBot.CreateCombatBehavior(),
                loot,new CountingLeaf(RunStatus.Failure),new Decorator(_=>true,new CountingLeaf(RunStatus.Failure)),
                movement,new CountingLeaf(RunStatus.Failure));
            var root=new PublishedQuestRoot(()=>()=>true,()=>{addonReads++;return new[]{"absent"};});
            // Replace only the controlled quest execution leaf after the actual
            // root constructor has installed its forced-order owner.
            root.Children[5]=movement;movement.Parent=root;root.Start(null);
            try{
                // Initial loot branch must fail until the observed corpse exists.
                root.Children[2]=new Decorator(_=>LootTargeting.Instance.FirstObject!=null,loot);
                root.Children[2].Parent=root;
                Check(root.Tick(null)==RunStatus.Running&&movement.Ticks==1,"quest movement did not retain its active tick");
                LootTargeting.Instance.FirstObject=Corpse();int before=addonReads;root.Tick(null);
                Check(movement.Ticks==1&&movement.Stops==1&&loot.Ticks==1,"new corpse remained queued behind the movement endpoint");
                Check(addonReads==before,"an optional addon query delayed the protective loot handoff");
            }finally{root.Stop(null);}
        });
        Case("recovery still precedes ready loot",()=>
        {
            LootTargeting.Instance.FirstObject=Corpse();var rest=new CountingLeaf(RunStatus.Running);RoutineManager.Current.RestBehavior=rest;
            Check(Once(LevelBot.CreateCombatBehavior())==RunStatus.Running&&rest.Ticks==1,"loot displaced required recovery");
        });
        Case("ordinary travel without loot still receives optional buffs",()=>
        {
            var buffs=(CountingLeaf)RoutineManager.Current.PreCombatBuffBehavior;
            Check(Once(LevelBot.CreateCombatBehavior())==RunStatus.Success&&buffs.Ticks==1,"no-loot travel lost its optional buffs");
        });
        Console.WriteLine($"Loot priority: {passed}/{total}; actual combat/pre-pull and published quest root; controlled routine/world leaves.");
        if(passed!=total)Environment.ExitCode=1;
    }
    private static WoWUnit Corpse()=>new(){Guid=33,BaseAddress=3300,IsAlive=false,Location=new(3,0,10)};
    private static RunStatus Once(Composite tree){tree.Start(null);try{return tree.Tick(null);}finally{tree.Stop(null);}}
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

namespace Bots.Grind
{
    public partial class LevelBot
    {
        // Controlled source registry for priority tests. The complete selection
        // and timed receipt implementation runs in GroundCollectionFocusedTests.
        private static WoWObject SelectLootCandidate(LootTargeting targeting)=>targeting.FirstObject;
    }
}
