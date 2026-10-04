using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class ArelionRuntimeBoundaryTests
{
    internal static void Run(string root)
    {
        string[] sources={"ArelionLureRuntime.cs","ArelionLureController.cs","ArelionLureScripts.cs"};
        var syntax=sources.Select(name=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Logic/Questing",name)),path:name))
            .Append(CSharpSyntaxTree.ParseText(Boundary+Cases,path:"ControlledClientBoundary.cs"));
        string trusted=(string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references=trusted.Split(Path.PathSeparator).Where(p=>!Path.GetFileName(p).Equals("CopilotBuddy.dll",StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("ActualArelionBoundary_"+Guid.NewGuid().ToString("N"),syntax,references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var emitted=compilation.Emit(stream);
        if(!emitted.Success)throw new InvalidOperationException("Actual lure adapter fixture compilation: "+string.Join("; ",emitted.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(stream.ToArray());var world=assembly.GetType("AdapterWorld",true)!;
        using var lua=new RewardLua51Boundary.StockLua51(root);
        RewardLua51Boundary.StockLua51.Session? session=null;
        world.GetField("ResetLua")!.SetValue(null,new Action(()=>{session?.Dispose();session=lua.BeginSession(ArelionWorkflowTests.Client+
            "\nfunction CloseMerchant() merchant=false end\nfunction CloseQuest() quest=false end\nfunction CloseGossip() gossip=false end\n");}));
        world.GetField("Query")!.SetValue(null,new Func<string,string[]>(code=>
        {
            var result=session!.Execute(code,(uint)Encoding.UTF8.GetByteCount(code));
            if(result.Load!=0||result.Call!=0)throw new InvalidOperationException(result.Error);
            return result.Values.ToArray();
        }));
        try{assembly.GetType("ActualAdapterCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException error) when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();}
        finally{session?.Dispose();}
    }

    private const string Boundary="""
#nullable enable
using System;using System.Linq;using System.Collections.Generic;
using Styx.Logic.Pathing;using Styx.Logic.Questing;using Styx.WoWInternals.WoWObjects;
public static class AdapterWorld {
 public static Action ResetLua=null!;public static Func<string,string[]> Query=null!;
 public static Action<string>? BeforeLua,BeforeNative;public static bool Prepared,Current=true,Facing=true,ContainerCurrent=true,GroundReady=true;
 public static int NativeTargets,NativeClears,TransitCalls,InteractionMoves;public static uint ShownQuest;
 public static LocalPlayer Actor=null!;public static WoWUnit Viera=null!,Vendor=null!;
 public static void Reset(){ResetLua();BeforeLua=BeforeNative=null;Prepared=false;Current=Facing=ContainerCurrent=GroundReady=true;NativeTargets=NativeClears=TransitCalls=InteractionMoves=0;ShownQuest=0;
  Actor=new(){Guid=1,BaseAddress=100,Location=new(-720.839f,4162.23f,50.8059f)};
  Viera=new(){Guid=2,BaseAddress=200,Entry=17226,Location=Actor.Location,NpcFlags=0};
  Vendor=new(){Guid=3,BaseAddress=300,Entry=18907,Location=new(-174.478f,5529.21f,29.4909f)};
  Actor.CarriedItems.Add(new(){Guid=9,BaseAddress=900,Entry=23693,OwnerGuid=1});
  Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Entry=new(){Id=9483,Index=1};
  Styx.Logic.Inventory.Frames.Gossip.GossipFrame.CurrentId=9483;
  Navigator.NavigationProvider=new MeshNavigator();Flightor.RequestIdentity=new();
 }
 public static void Code(string code)=>Query(code);
 public static int Count(string name)=>int.Parse(Query("return "+name)[0]);
}
namespace Styx.Helpers { public class ObservationUnavailableException(string owner,string why):Exception(owner+":"+why){} }
namespace GreenMagic { public class ExecutorRand {
 public object AssemblyLock=new();public void Clear(){AdapterWorld.NativeClears++;AdapterWorld.BeforeNative?.Invoke("prepare");}
 public void AddLine(string line,params object[] args){}public void Execute(){AdapterWorld.NativeTargets++;}
} }
namespace Styx.Logic.Pathing {
 public readonly record struct WoWPoint(float X,float Y,float Z){public static WoWPoint Empty=>new(float.NaN,float.NaN,float.NaN);public float DistanceSqr(WoWPoint p)=>(X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z);}
 public enum GroundTransitionState{Pending,Ready,Unavailable,Revoked}public enum GroundTransitionPurpose{Interaction,Combat,Transit}
 public sealed class MeshNavigator{public object RequestIdentity=new();}
 public static class Navigator{public static object NavigationProvider=new MeshNavigator();}
 public static class Flightor{public static object RequestIdentity=new();}
 internal sealed class GroundTransitionContext {
  public LocalPlayer Actor;public ulong ActorGuid;public uint Map;public GreenMagic.ExecutorRand Executor=new();private readonly uint address;private readonly Func<bool> admitted;
  public GroundTransitionContext(WoWObject? s,WoWPoint p,bool b,Func<bool> a,bool journeyRoute=false){Actor=AdapterWorld.Actor;ActorGuid=Actor.Guid;address=Actor.BaseAddress;Map=Actor.MapId;admitted=a;}
  public bool Current=>AdapterWorld.Current&&ReferenceEquals(Actor,AdapterWorld.Actor)&&Actor.Guid==ActorGuid&&Actor.BaseAddress==address&&Actor.MapId==Map&&admitted();
 }
 public sealed class GroundTransition {
  public GroundTransition(GroundTransitionPurpose purpose){}
  public GroundTransitionState Tick(WoWPoint destination,WoWObject? subject,Func<bool> current){AdapterWorld.InteractionMoves++;return !current()?GroundTransitionState.Revoked:AdapterWorld.GroundReady?GroundTransitionState.Ready:GroundTransitionState.Pending;}
  public GroundTransitionState TickTransit(WoWPoint destination,double remaining,Func<bool> current){AdapterWorld.TransitCalls++;return current()?GroundTransitionState.Pending:GroundTransitionState.Revoked;}
  public static bool TryInteractWith(WoWObject subject,Func<bool> current)=>current();public void Cancel(){}
 }
}
namespace Styx.WoWInternals {
 public static class ObjectManager{public static List<T> GetObjectsOfType<T>() where T:WoWUnit=>new WoWUnit[]{AdapterWorld.Viera,AdapterWorld.Vendor}.OfType<T>().ToList();}
 public static class Lua {
  public static List<string> GetObservedReturnValues(string code,Func<bool> admitted){if(AdapterWorld.Prepared)throw new InvalidOperationException("query overwrote prepared native command");
   AdapterWorld.BeforeLua?.Invoke(code);AdapterWorld.Prepared=true;try{if(!admitted())throw new Styx.Helpers.ObservationUnavailableException("lua","late admission revoked");return AdapterWorld.Query(code).ToList();}finally{AdapterWorld.Prepared=false;}}
 }
}
namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject{public ulong Guid;public uint BaseAddress,Entry;public WoWPoint Location;public bool IsValid=true;public bool WithinInteractRange=>Location.DistanceSqr(AdapterWorld.Actor.Location)<=25;}
 public class WoWUnit:WoWObject{public bool IsAlive=true,IsMoving;public uint NpcFlags=3;public bool CanSelect=true;}
 public sealed class Inventory {public Container Backpack=new(){ItemGuids=new ulong[]{9}};}
 public sealed class Container{public ulong[] ItemGuids=Array.Empty<ulong>();}
 public sealed class LocalPlayer:WoWUnit{
  public uint MapId=530,Flags;public ulong Transport,CurrentTargetGuid=2;public bool Mounted,OnTaxi,IsSwimming,Combat,IsCasting,Rooted,Stunned;public int ChanneledCastingSpellId;
  public Inventory Inventory=new();public QuestLog QuestLog=new();public List<WoWItem> CarriedItems=new();
  public bool TryGetMovementState(out uint flags,out ulong transport){flags=Flags;transport=Transport;return true;}
  public bool IsSafelyFacing(WoWUnit target,int angle)=>AdapterWorld.Facing;public void SetFacing(WoWUnit target){}
  public Container? GetBagAtIndex(uint index)=>null;
 }
 public sealed class WoWItem:WoWObject{public ulong OwnerGuid;public bool IsCooldownReady=true;
  public static bool TryResolveContainerLocation(ulong guid,ulong[] backpack,ulong[][] bags,out int bag,out int slot){bag=0;slot=Array.IndexOf(backpack,guid)+1;return slot>0;}
  internal static bool IsContainerLocationCurrent(LocalPlayer actor,int bag,int slot,ulong guid)=>AdapterWorld.ContainerCurrent&&bag==0&&slot>0&&actor.Inventory.Backpack.ItemGuids[slot-1]==guid;
 }
}
namespace Styx.WoWInternals.World {
 public static class WorldQueryObservation{public readonly record struct State(bool Mounted,bool OnTaxi,bool Rooted,bool Stunned);public static State ReadGroundUnitState(LocalPlayer actor)=>new(actor.Mounted,actor.OnTaxi,actor.Rooted,actor.Stunned);}
}
namespace Styx.Logic.Questing {
 public sealed class QuestLogSnapshot{public bool IsIdentityComplete=true;public uint[] AcceptedQuestIds=new uint[]{9472};}
 public sealed class PlayerQuest{}
 public sealed class QuestLog{public QuestLogSnapshot CaptureSnapshot()=>new();public bool IsSnapshotCurrent(QuestLogSnapshot s)=>true;public PlayerQuest GetQuestById(uint id)=>new();}
 public static class QuestObjectiveCompletion{public static bool TryReadTypedNormalObjectiveProgress(PlayerQuest? quest,int id,int count,out int value){value=0;return true;}}
}
namespace Styx.Logic.Inventory.Frames.Gossip {
 public sealed class GossipQuestEntry{public int Id,Index;public bool IsCurrent=>Id==GossipFrame.CurrentId;}
 public sealed class GossipFrame{public static GossipFrame Instance=new();public static GossipQuestEntry Entry=new();public static int CurrentId;
  public List<GossipQuestEntry> AvailableQuests{get{if(AdapterWorld.Prepared)throw new InvalidOperationException("gossip enumeration during prepared command");return new(){new(){Id=1,Index=0},Entry};}}}
}
namespace Styx.Logic.Inventory.Frames.Quest {public sealed class QuestFrame{public static QuestFrame Instance=new();public uint CurrentShownQuestId=>AdapterWorld.ShownQuest;}}
namespace Styx.Logic.Combat {public static class RecoveryActions{public static bool BindContainerRequest(ulong guid,uint item,string script)=>AdapterWorld.Current;public static void ObserveContainerReply(string script,bool submitted){}}}
""";

    private const string Cases="""
public static class ActualAdapterCases {
 private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
 private static ArelionLureRuntime Ready(){var runtime=new ArelionLureRuntime(()=>true);runtime.Observe();Check(runtime.MoveViera(true)==GroundTransitionState.Ready,"ground control not ready");return runtime;}
 public static void Run(){int passed=0,total=0;var failures=new List<string>();
  void Case(string name,Action test){total++;AdapterWorld.Reset();try{test();passed++;Console.WriteLine("PASS actual lure adapter: "+name);}catch(Exception error){failures.Add(name+": "+error);Console.Error.WriteLine("FAIL actual lure adapter: "+failures.Last());}}
  Case("full adapter reads stock and lured state then uses the carried scroll",()=>{using var runtime=Ready();var observed=runtime.Observe();Check(observed.Accepted==true&&observed.Credit==0&&observed.Wine==0&&observed.Scroll==1&&observed.AtLureEndpoint,"actual adapter observations disagree");runtime.MoveViera(true);Check(runtime.UseScroll()==QuestWorkflowReceipt.Submitted&&AdapterWorld.Count("scrollUses")==1,"actual carried item path did not submit once");});
  Case("no positive ground-ready observation means no item use",()=>{using var runtime=new ArelionLureRuntime(()=>true);runtime.Observe();Check(runtime.UseScroll()==QuestWorkflowReceipt.Rejected&&AdapterWorld.Count("scrollUses")==0,"missing ground-ready receipt authorized use");});
  foreach(string mutation in new[]{"actor","base","target","target-base","movement","ground-support-owner","flight-owner","bag","combat","quest-context"}){string mode=mutation;
   Case("native entry rejects late "+mode,()=>{using var runtime=Ready();AdapterWorld.BeforeLua=code=>{if(!code.Contains("UseContainerItem(0,1)"))return;AdapterWorld.BeforeLua=null;
    switch(mode){case "actor":AdapterWorld.Actor=new();break;case "base":AdapterWorld.Actor.BaseAddress++;break;case "target":AdapterWorld.Actor.CurrentTargetGuid=77;break;
     case "target-base":AdapterWorld.Viera.BaseAddress++;break;case "movement":AdapterWorld.Actor.Location=new(0,0,0);break;
     case "ground-support-owner":((MeshNavigator)Navigator.NavigationProvider).RequestIdentity=new();break;case "flight-owner":Flightor.RequestIdentity=new();break;
     case "bag":AdapterWorld.Actor.Inventory.Backpack.ItemGuids[0]=10;break;case "combat":AdapterWorld.Actor.Combat=true;break;default:AdapterWorld.Current=false;break;}};
    try{runtime.UseScroll();}catch(Styx.Helpers.ObservationUnavailableException){}Check(AdapterWorld.Count("scrollUses")==0,"late admission mutation still issued native item use");});
  }
  Case("quest-frame replacement rejects the pending lure reward",()=>{using var runtime=Ready();AdapterWorld.ShownQuest=9483;AdapterWorld.Code("wine=1;quest=true;reward=true");AdapterWorld.BeforeLua=code=>{if(code.Contains("button:Click()"))AdapterWorld.ShownQuest=123;};runtime.AdvanceLureQuest();Check(AdapterWorld.Count("lureRewards")==0,"other shown quest received the lure reward click");});
  Case("gossip row replacement rejects the selected lure quest",()=>{using var runtime=Ready();AdapterWorld.Code("gossip=true");AdapterWorld.BeforeLua=code=>{if(code.Contains("SelectGossipAvailableQuest(2)"))Styx.Logic.Inventory.Frames.Gossip.GossipFrame.CurrentId=123;};runtime.SelectLureQuest();Check(AdapterWorld.Count("questSelections")==0,"another quest inherited selected gossip index");});
  Case("actual available quest owner selects9483 with its native row fence",()=>{using var runtime=Ready();AdapterWorld.Code("gossip=true");Check(runtime.SelectLureQuest()==QuestWorkflowReceipt.Submitted&&AdapterWorld.Count("selectedQuestIndex")==2,"actual native row selection failed");});
  Case("late actor replacement prevents native target selection",()=>{using var runtime=Ready();AdapterWorld.Actor.CurrentTargetGuid=0;AdapterWorld.BeforeNative=_=>AdapterWorld.Actor=new();runtime.UseScroll();Check(AdapterWorld.NativeTargets==0&&AdapterWorld.Count("scrollUses")==0,"target operation adopted replacement actor");});
  Case("target selection cannot itself acknowledge scroll use",()=>{using var runtime=Ready();AdapterWorld.Actor.CurrentTargetGuid=0;runtime.UseScroll();Check(AdapterWorld.NativeTargets==1&&AdapterWorld.Count("scrollUses")==0,"target request acknowledged item use");});
  Case("same Viera movement preserves recipient identity",()=>{using var runtime=Ready();var before=runtime.Observe();AdapterWorld.Viera.Location=new(-719,4162,50.8f);var after=runtime.Observe();Check(ReferenceEquals(before.VieraToken,after.VieraToken),"moving coordinate replaced semantic lure recipient");});
  Case("a replacement Viera wrapper cannot borrow the previous token",()=>{using var runtime=Ready();var before=runtime.Observe();AdapterWorld.Viera=new(){Guid=2,BaseAddress=200,Entry=17226,Location=AdapterWorld.Actor.Location,NpcFlags=0};var after=runtime.Observe();Check(!ReferenceEquals(before.VieraToken,after.VieraToken),"same numeric identity hid wrapper replacement");});
  Case("disposed runtime refuses all further actions",()=>{var runtime=Ready();runtime.Dispose();Check(!runtime.Current&&runtime.UseScroll()==QuestWorkflowReceipt.Rejected&&AdapterWorld.Count("scrollUses")==0,"disposed adapter retained action authority");});
  Case("lured NPC follow uses transit until the final item endpoint",()=>{
   using var runtime=new ArelionLureRuntime(()=>true);AdapterWorld.Viera.IsMoving=true;AdapterWorld.Viera.Location=new(-680,4160,51);
   runtime.Observe();for(int i=0;i<6;i++){AdapterWorld.Viera.Location=new(-680-i*2,4160,51);runtime.Observe();Check(runtime.MoveViera(true)==GroundTransitionState.Pending,"intermediate escort point was treated as an interaction stop");}
   Check(AdapterWorld.TransitCalls==6&&AdapterWorld.InteractionMoves==0,"following Viera repeatedly selected stopping interaction movement");
   AdapterWorld.Viera.IsMoving=false;AdapterWorld.Viera.Location=ArelionLureRuntime.LureEndpoint;runtime.Observe();
   Check(runtime.MoveViera(true)==GroundTransitionState.Ready&&AdapterWorld.InteractionMoves==1,"the actual item endpoint did not acquire its own ground-ready interaction");
  });
  Console.WriteLine($"Arelion actual runtime adapter: {passed}/{total}; complete production runtime/controller/scripts; controlled client descriptors, ground-ready leaves and executor; actual Lua5.1; no live traversal.");
  if(failures.Count!=0)throw new InvalidOperationException(string.Join("\n",failures));
 }
}
""";
}
