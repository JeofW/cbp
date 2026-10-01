using System.Reflection;
using System.Runtime.CompilerServices;
using Bots.Quest.Objectives;
using Styx;
using Styx.Logic.AreaManagement;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;

// Exercise the real collection factory and GrindArea arrival owner. No fake
// arrival predicate; the controlled player's actual position is (10,10,10).
internal static class GroundHotspotArrivalRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        using var world=new QuestDatasetObservationFixture();
        world.SetQuest(10161,"In Case of Emergency...",58,new int[4],new int[4],new[]{28116,0,0,0,0,0},new[]{30,0,0,0,0,0});
        world.LoadProfile("<HBProfile><Name>Ground arrival</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><Quest Id=\"10161\" Name=\"Ground\"><Objective Type=\"CollectItem\" ItemId=\"28116\" CollectCount=\"30\"><CollectFrom><GameObject Id=\"183394\" /></CollectFrom><Hotspots><Hotspot X=\"10\" Y=\"10\" Z=\"-30\" /></Hotspots></Objective></Quest><QuestOrder /></HBProfile>");
        using var objective=new CollectItemObjective(world.Quest,new(),world.Quest.GetObjectives().Single(o=>o.ID==28116),new());
        objective.CreateBranch();
        var area=StyxWoW.AreaManager.CurrentGrindArea;
        if(area==null)throw new InvalidOperationException("Collection factory failed to install its real area");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var point=new Hotspot(10,10,-30);
        typeof(GrindArea).GetField("_currentHotspot",flags)!.SetValue(area,point);
        typeof(GrindArea).GetField("_lastProfile",flags)!.SetValue(area,ProfileManager.CurrentProfile);
        area.LastHotSpot=null!;
        var current=area.CurrentHotSpot;
        if(ReferenceEquals(area.LastHotSpot,current))
            throw new InvalidOperationException("Ground collection falsely arrived forty units above its objective using only horizontal distance");
        if(!area.HotspotChanged)
            throw new InvalidOperationException("Ground collection travel was suppressed before three-dimensional arrival");
        // Vertical displacement must be respected on either side of a floor.
        point=new Hotspot(10,10,50);
        typeof(GrindArea).GetField("_currentHotspot",flags)!.SetValue(area,point);
        area.LastHotSpot=null!;
        current=area.CurrentHotSpot;
        if(ReferenceEquals(area.LastHotSpot,current))
            throw new InvalidOperationException("Another floor falsely completed the ground objective approach");
        Console.WriteLine("Ground hotspot arrival: 3/3; actual collection factory and area owner; no physical travel or live completion claim.");
    }
}
