using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class UpstreamWorldRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        int passed=0, failed=0;
        void Case(string name, Action test){try{test();passed++;Console.WriteLine("PASS upstream world: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL upstream world: "+name+": "+e.Message);}}
        Case("mesh height tile preparation",()=>{
            var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(UpstreamSeptemberRegressionTests.Root(),"Styx/Logic/Pathing/Navigator.cs"))).GetRoot();
            var method=tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="FindMeshHeight"&&m.ParameterList.Parameters.Count==3);
            UpstreamSeptemberRegressionTests.Probe("mesh height",HeightPrefix+method+HeightCases);
        });
        Case("quest area height generation",()=>UpstreamSeptemberRegressionTests.Probe("quest area",QuestArea,
            "Styx/Logic/AreaManagement/QuestArea.cs"));
        Case("selected boss navigation",()=>{
            var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(UpstreamSeptemberRegressionTests.Root(),"Bots/DungeonBuddy/DungeonBuddy.cs"))).GetRoot();
            string methods=string.Join("\n",tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>new[]{"IsTargetableBossUnit","FindCurrentBossUnit","GetCurrentProfileBoss"}.Contains(m.Identifier.ValueText)).Select(m=>m.ToString()));
            UpstreamSeptemberRegressionTests.Probe("dungeon selection",DungeonPrefix+methods+DungeonCases);
        });
        Console.WriteLine($"Upstream world families: {passed}/{passed+failed}; failures={failed}; actual owners and controlled geometry; no physical-world safety claim.");
        if(failed!=0)throw new InvalidOperationException("Upstream world regressions");
    }
    private const string HeightPrefix="""
using System;using System.Numerics;using Styx;using Styx.WoWInternals;
public static class Navigator {
 public static bool IsNavigatorLoaded=true;public static Native TripperNavigator=new();private static int GetCurrentMapId()=>(int)(StyxWoW.Me?.MapId??0);
""";
    private const string HeightCases="""
}
public class Player{public uint MapId;public ulong Guid=1;public bool IsValid=true;}
public class Native {
 public uint CurrentMapId;public bool Loaded,LoadAccepted=true;public int Loads,Nearest;public Action OnLoad;public float Height=42;
 public bool IsTileLoaded(uint map,int x,int y)=>Loaded;
 public bool LoadTile(Tripper.Navigation.TileIdentifier tile){Loads++;OnLoad?.Invoke();Loaded=LoadAccepted;return LoadAccepted;}
 public bool FindNearestPoint(uint map,Vector3 point,out Vector3 nearest){Nearest++;nearest=new Vector3(point.X,point.Y,Height);return Loaded;}
}
/* Controlled actor. */ namespace Styx{public static class StyxWoW{public static Player Me=new();}}
public static class Cases{public static void Run(){
 void Reset(){StyxWoW.Me=new();Navigator.TripperNavigator=new();}
 Reset();if(!Navigator.FindMeshHeight(10,20,out float z)||z!=42||Navigator.TripperNavigator.Loads!=1)throw new Exception("unloaded tile was never prepared before height query");
 Reset();Navigator.TripperNavigator.LoadAccepted=false;if(Navigator.FindMeshHeight(10,20,out _)||Navigator.TripperNavigator.Nearest!=0)throw new Exception("failed tile load authorized nearest point");
 Reset();Navigator.TripperNavigator.OnLoad=()=>StyxWoW.Me.MapId=1;if(Navigator.FindMeshHeight(10,20,out _))throw new Exception("map replacement inherited height");
 Reset();StyxWoW.Me=null;if(Navigator.FindMeshHeight(10,20,out _)||Navigator.TripperNavigator.Loads!=0)throw new Exception("missing player borrowed map zero");
 Reset();if(Navigator.FindMeshHeight(float.NaN,20,out _))throw new Exception("invalid query admitted");
 Reset();Navigator.TripperNavigator.Height=float.NaN;if(Navigator.FindMeshHeight(10,20,out _))throw new Exception("invalid native result admitted");
 Console.WriteLine("Upstream mesh height cases: 6 passed; actual helper, controlled tile/native reads.");}}
""";
    private const string QuestArea="""
using System;using System.Collections.Generic;using System.Numerics;using Styx;using Styx.Helpers;using Styx.Logic.AreaManagement;using Styx.Logic.Pathing;using Styx.Logic.Questing;
public static class Cases{public static void Run(){
 QuestArea Create()=>new(new PlayerQuest(),new List<WoWQuestStep>{new(){AreaPoints=new(){new Vector2(10,20)}},new(){AreaPoints=new(){new Vector2(30,40)}}});
 var area=Create();area.CreateHotspots();if(area.Hotspots.Count!=2||area.Hotspots[0].Z!=42||!area.HotspotsCreated)throw new Exception("height owner missing or polygon list generated more than once");
 area.CreateHotspots();if(area.Hotspots.Count!=2)throw new Exception("repeated creation duplicated hotspots");
 Navigator.Available=false;area=Create();area.CreateHotspots();if(area.Hotspots.Count!=0||area.HotspotsCreated)throw new Exception("temporarily unavailable mesh permanently completed empty hotspot creation");
 Navigator.Available=true;area.CreateHotspots();if(area.Hotspots.Count!=2)throw new Exception("height recovery never retried");
 Console.WriteLine("Upstream quest area cases: 4 passed; complete class with controlled height provider.");}}
/* Controlled base collection. */ namespace Styx.Logic.AreaManagement {public class GrindArea{public virtual AreaType Type=>AreaType.Grind;public List<WoWPoint> Hotspots=new();public CircularQueue<Hotspot> CircledHotspots=new();}}
/* Controlled quest metadata. */ namespace Styx.Logic.Questing {public class PlayerQuest{public string Name="controlled";}public class WoWQuestStep{public List<Vector2> AreaPoints=new();}}
/* Controlled height boundary distinguishes obsolete high-altitude nearest-point lookup. */ namespace Styx.Logic.Pathing{public static class Navigator{public static bool Available=true;public static bool FindMeshHeight(ref Tripper.XNAMath.Vector3 point)=>false;public static bool FindHeight(ref Tripper.XNAMath.Vector3 point){point.Z=42;return Available;}}}
/* Controlled diagnostic sink. */ namespace Styx.Helpers{public static class Logging{public static void Write(string value){}}}
""";
    private const string DungeonPrefix="""
using System;using System.Linq;using System.Collections.Generic;using Styx;using Styx.Logic.Pathing;using Profiles=Bots.DungeonBuddy.Profiles;
public static class DungeonProbe {
 public static bool Admit(WoWUnit unit)=>IsTargetableBossUnit(unit,ProfileManager.CurrentProfile.BossEncounters);
 public static WoWUnit Find()=>FindCurrentBossUnit();
""";
    private const string DungeonCases="""
}
public class WoWUnit{public uint Entry;public bool IsValid=true,IsAlive=true;public WoWPoint Location=new(10,20,30);public double DistanceSqr=100;}
public class Player{public WoWPoint Location=new(1,2,3);}
public static class StyxWoW{public static Player Me=new();}
public static class ObjectManager{public static List<WoWUnit> Units=new();public static IEnumerable<T> GetObjectsOfType<T>()=>Units.OfType<T>();}
public class Profile{public List<Profiles.Handlers.Boss> BossEncounters=new();}
public static class ProfileManager{public static Profile CurrentProfile=new();}
public static class BossManager{public static Profiles.Handlers.Boss CurrentBoss;}
public class DungeonBuddySettings{public static DungeonBuddySettings Instance=new();public bool KillOptionalBosses;}
/* Controlled path observation. */ namespace Styx.Logic.Pathing{public static class Navigator{public static float? Length=30;public static float? PathDistance(WoWPoint a,WoWPoint b,float maximum=float.MaxValue)=>Length;}}
public static class Cases{public static void Run(){
 var optional=new Profiles.Handlers.Boss{Entry=1,Optional=true,KillOrder=1};var current=new Profiles.Handlers.Boss{Entry=2,KillOrder=2};var later=new Profiles.Handlers.Boss{Entry=3,KillOrder=3};
 ProfileManager.CurrentProfile.BossEncounters=new(){optional,current,later};BossManager.CurrentBoss=current;
 if(DungeonProbe.Admit(new WoWUnit{Entry=1}))throw new Exception("skipped optional boss overrode the manager's current encounter");
 if(!DungeonProbe.Admit(new WoWUnit{Entry=2})||DungeonProbe.Admit(new WoWUnit{Entry=3}))throw new Exception("unselected future boss acquired navigation");
 ObjectManager.Units=new(){new WoWUnit{Entry=2}};if(DungeonProbe.Find()==null)throw new Exception("reachable selected boss rejected");
 foreach(float? length in new float?[]{null,-1,float.NaN,float.PositiveInfinity,101}){Navigator.Length=length;if(DungeonProbe.Find()!=null)throw new Exception("unavailable/invalid/long path treated as nearby selected boss");}
 Console.WriteLine("Upstream boss navigation cases: 8 passed; actual selectors and controlled route results.");}}
""";
}
