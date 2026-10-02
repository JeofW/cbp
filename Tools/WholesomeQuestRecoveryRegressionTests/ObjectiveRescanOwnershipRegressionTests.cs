using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the exact rescan caller, not a duplicate policy. Only observed player,
// frame, quest ownership, progress reads and the final hotspot effect are external.
internal static class ObjectiveRescanOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Tracked checkout required");
        var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs"))).GetRoot();
        var method=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="TryBoundedObjectiveRescan").ToString();
        var rest=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="TryObserveRestAuras").ToString();
        string folder=Path.Combine(Path.GetTempPath(),"cb-rescan-owner-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder,"Probe.cs"),Prefix+method+"\n"+rest+Suffix);
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{folder})!;
            foreach(string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference",flags)!.Invoke(compiler,new object[]{path});
            var result=(CompilerResults)compilerType.GetMethod("Compile",flags)!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Rescan owner compilation: "+string.Join(';',errors.Select(e=>e.ToString())));
            var assembly=(Assembly)compilerType.GetProperty("CompiledAssembly",flags)!.GetValue(compiler)!;
            try{assembly.GetType("Cases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        finally
        {
            string full=Path.GetFullPath(folder);
            string temporaryRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(temporaryRoot,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("cb-rescan-owner-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected rescan fixture cleanup path");
            Directory.Delete(full,true);
        }
    }
    private const string Prefix="""
using System;using System.Linq;using System.Collections.Generic;
public class Probe {
 public bool _stopped,_restingPaused;public Attempt _attemptOwnership=new();
 public bool Run(ForcedQuestObjective behavior,QuestRecoveryKey key)=>TryBoundedObjectiveRescan(behavior,key,1,new[]{0});
 private static IReadOnlyList<int> ReadObjectiveCounts(Quest quest){var action=Cases.AfterRead;Cases.AfterRead=null;action?.Invoke();return Cases.Counts;}
 private static void Log(string text){}
""";
    private const string Suffix="""
}
public class Aura{public string Name;}
/* Controlled actor observation boundary. */ namespace Styx.WoWInternals.WoWObjects {
public class LocalPlayer{public ulong Guid=1;public uint MapId=530,FreeNormalBagSlots=20;public bool IsValid=true,IsAlive=true,IsGhost,IsActuallyInCombat,PetInCombat,IsFlying,IsMoving,IsCasting,OnTaxi,IsOnTransport,Food,Drink,MovementKnown=true,AurasKnown=true;public int ChanneledCastingSpellId;public uint Flags;public bool TryGetAllAuras(out List<global::Aura> auras,string consumer){auras=null;if(!AurasKnown)return false;auras=new();if(Food)auras.Add(new global::Aura{Name="Food"});if(Drink)auras.Add(new global::Aura{Name="Drink"});return true;}public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=IsOnTransport?1UL:0UL;return MovementKnown;}}
}
public class Actor:Styx.WoWInternals.WoWObjects.LocalPlayer{}
/* Controlled rest observation leaf; no unknown aura coverage is converted to absence. */ namespace Styx.Logic.Common {
public static class Rest {public static bool TryObserveActivity(Styx.WoWInternals.WoWObjects.LocalPlayer actor,out bool food,out bool drink){food=drink=false;if(actor==null||!actor.AurasKnown)return false;food=actor.Food;drink=actor.Drink;return true;}}
}
public static class StyxWoW{public static Actor Me=new();public static bool IsInWorld=true;public static AreaManager AreaManager=new();}
public class AreaManager{public Area CurrentGrindArea=new();}
public class Area{public int Calls;public bool TryAdvanceCurrentHotspot(out int previous,out int next){Calls++;previous=0;next=1;return true;}}
public enum PoiType{None,Loot,Hotspot,Repair}
public class BotPoi{public static BotPoi Current=new();public PoiType Type;}
public static class TreeRoot{public static bool IsPaused;}
public class MeshNavigator{public bool IsRidingElevator;}
public static class Navigator{public static object NavigationProvider=new MeshNavigator();}
public class Frame{public bool IsVisible;}
public static class MerchantFrame{public static Frame Instance=new();}
public static class TrainerFrame{public static Frame Instance=new();}
/* Controlled frame boundary, not the test entry namespace. */ namespace Styx.Logic.Inventory.Frames.LootFrame{public static class LootFrame{public static global::Frame Instance=new();}}
/* Controlled frame boundary, not the test entry namespace. */ namespace Styx.Logic.Inventory.Frames.Gossip{public static class GossipFrame{public static global::Frame Instance=new();}}
public class Quest{public uint Id=10161;}
public class Objective{public Quest Quest=new();}
public class ForcedQuestObjective{public Objective Objective=new();}
public class Order{public ForcedQuestObjective CurrentBehavior;}
public static class QuestOrder{public static Order Instance=new();}
public class QuestRecoveryKey{public uint QuestId=10161;}
public class Attempt{public QuestRecoveryKey Key;public long Generation=1;public bool TryGet(object owner,out QuestRecoveryKey key,out long generation){key=Key;generation=Generation;return true;}}
public class QuestRecoveryManager{public static QuestRecoveryManager Instance=new();public bool Owned=true;public bool OwnsAttempt(QuestRecoveryKey key,long generation)=>Owned;}
public static class Cases{
 public static System.Action AfterRead;public static int[] Counts=new[]{0};
 static Probe probe;static ForcedQuestObjective behavior;static QuestRecoveryKey key;static Area area;
 static void Reset(){AfterRead=null;Counts=new[]{0};StyxWoW.Me=new Actor();StyxWoW.IsInWorld=true;StyxWoW.AreaManager=new AreaManager();area=StyxWoW.AreaManager.CurrentGrindArea;TreeRoot.IsPaused=false;BotPoi.Current=new BotPoi();Navigator.NavigationProvider=new MeshNavigator();MerchantFrame.Instance=new Frame();TrainerFrame.Instance=new Frame();Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance=new Frame();Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance=new Frame();QuestRecoveryManager.Instance=new QuestRecoveryManager();behavior=new ForcedQuestObjective();key=new QuestRecoveryKey();probe=new Probe();probe._attemptOwnership.Key=key;QuestOrder.Instance=new Order{CurrentBehavior=behavior};}
 static void Change(string name){var actor=StyxWoW.Me;switch(name){
 case "combat":actor.IsActuallyInCombat=true;break;case "pet-combat":actor.PetInCombat=true;break;
 case "flying":actor.IsFlying=true;break;case "moving":actor.IsMoving=true;break;case "casting":actor.IsCasting=true;break;
 case "channel":actor.ChanneledCastingSpellId=2;break;case "taxi":actor.OnTaxi=true;break;case "transport":actor.IsOnTransport=true;break;
 case "rest":probe._restingPaused=true;break;case "food":actor.Food=true;break;case "drink":actor.Drink=true;break;
 case "auras-unknown":actor.AurasKnown=false;break;
 case "full":actor.FreeNormalBagSlots=0;break;case "poi-type":BotPoi.Current.Type=PoiType.Loot;break;
 case "loot-frame":Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance.IsVisible=true;break;
 case "gossip":Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance.IsVisible=true;break;
 case "merchant":MerchantFrame.Instance.IsVisible=true;break;case "trainer":TrainerFrame.Instance.IsVisible=true;break;
 case "movement-unknown":actor.MovementKnown=false;break;case "falling":actor.Flags=0x2000;break;
 case "invalid":actor.IsValid=false;break;case "dead":actor.IsAlive=false;break;case "ghost":actor.IsGhost=true;break;
 case "pause":TreeRoot.IsPaused=true;break;case "stopped":probe._stopped=true;break;case "outside-world":StyxWoW.IsInWorld=false;break;
 case "elevator":((MeshNavigator)Navigator.NavigationProvider).IsRidingElevator=true;break;
 case "actor":StyxWoW.Me=new Actor();break;case "guid":actor.Guid++;break;case "map":actor.MapId++;break;
 case "owner":QuestOrder.Instance.CurrentBehavior=new ForcedQuestObjective();break;case "generation":probe._attemptOwnership.Generation++;break;
 case "key":probe._attemptOwnership.Key=new QuestRecoveryKey();break;case "counts":Counts=new[]{1};break;
 case "attempt":QuestRecoveryManager.Instance.Owned=false;break;case "area":StyxWoW.AreaManager.CurrentGrindArea=new Area();break;
 case "provider":Navigator.NavigationProvider=new object();break;case "poi":BotPoi.Current=new BotPoi();break;
 default:throw new Exception(name);}}
 public static void Run(){int passed=0,failed=0;Reset();if(!probe.Run(behavior,key)||area.Calls!=1)throw new Exception("healthy actual rescan missing");passed++;
 string[] cases={"combat","pet-combat","flying","moving","casting","channel","taxi","transport","rest","food","drink","auras-unknown","full","poi-type","loot-frame","gossip","merchant","trainer","movement-unknown","falling","invalid","dead","ghost","pause","stopped","outside-world","elevator","actor","guid","map","owner","generation","key","counts","attempt","area","provider","poi"};
 foreach(string name in cases){Reset();AfterRead=()=>Change(name);bool result=probe.Run(behavior,key);if(result||area.Calls!=0){failed++;Console.WriteLine("FAIL rescan late "+name+": result="+result+", advances="+area.Calls);}else passed++;}
 Console.WriteLine("Objective rescan ownership: "+passed+"/"+(passed+failed)+"; failures="+failed+"; exact runtime caller with controlled observation boundaries.");if(failed!=0)throw new Exception("Rescan ownership failures");}
}
""";
}
