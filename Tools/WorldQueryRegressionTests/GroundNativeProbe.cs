// Source members are extracted in Program.cs without replacing their control
// flow. Only client observations and the executor/assembly storage are leaves.
internal static class GroundNativeProbe
{
    internal const string ObjectLeaves = "public uint Entry=70; public WoWPoint Position;public WoWPoint Location=>Position;public bool WithinInteractRange=>ObjectManager.Me.Location.DistanceSqr(Location)<=25;";
    internal const string PlayerLeaves = """
public uint MovementFlags;public ulong Transport;public bool MovementKnown=true;
public bool IsSwimming=>(MovementFlags&0x00200000u)!=0;
""";
    internal const string Leaves = """
namespace Styx.Logic.Profiles{public static class ProfileManager{public static object CurrentProfileSnapshot=new();}}
namespace Styx.Logic.POI{
 public enum PoiType{None,QuestPickUp,QuestTurnIn,Kill}
 public sealed class BotPoi{public static BotPoi Current=new();public static long CurrentGeneration;public static long CurrentWorkGeneration=>CurrentGeneration;public bool IsWorldSubjectBlacklisted;public PoiType Type;public ulong Guid;public uint Entry;public WoWObject AsObject;}
}
namespace Styx.WoWInternals{public static class WoWMovement{public static WoWUnit ActiveMover;}}
namespace Styx.Logic{public static class Blacklist{public static readonly HashSet<ulong> Items=new();public static bool Contains(ulong guid)=>Items.Contains(guid);}}
namespace Styx.WoWInternals.WoWObjects{
 public sealed class WoWGameObject:WoWObject{
  public bool IsDisabled,Usable=true;public float InteractRange=5;
  public bool CanUse(){GroundProbe.PrepareUsability();return Usable;}
  public bool CanUseNow(){GroundProbe.PrepareUsability();return Usable;}
 }
}
namespace Styx.Logic.Pathing{
 public class NavigationProvider{}
 public interface IPlayerMover{}
 public sealed class PlayerMover:IPlayerMover{}
 public static class Navigator{public static NavigationProvider NavigationProvider=new();public static IPlayerMover PlayerMover=new PlayerMover();public static float PathPrecision=1;public static bool CanNavigateFully(WoWPoint from,WoWPoint to)=>CorpseSearchCases.DirectPath;public static WoWPoint[] GeneratePath(WoWPoint from,WoWPoint to)=>CorpseSearchCases.Path(from,to);}
 public static class GroundApproachSearch{public static bool Finite(WoWPoint p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);}
 internal static class GroundTransitionRuntime{internal static void ObserveUnmounted(GroundTransitionContext stamp){}}
}
namespace Styx.WoWInternals.World{
 public static partial class GroundSight{public static bool Clear=true;}
}
public static class GroundProbe{
 public static WoWObject Subject;public static string[] VehicleValues;public static Exception MovementError;
 public static uint MountDisplay,RawForm,UnitFlagsValue,DescriptorPointer;public static string DescriptorFailure;public static Exception DescriptorError;
 public static Action<string> DescriptorRead;public static ulong? DescriptorGuid;
 public static int Interactions,LuaDuringPreparedInteraction,UsabilityDuringPreparedInteraction,UsabilityReads;public static string[] InteractionInstructions;
 public static Action DuringUsability;
 public static void Reset(LocalPlayer actor,WoWObject subject){
  Subject=subject;VehicleValues=new[]{"world-vehicle","0"};MovementError=null;Interactions=LuaDuringPreparedInteraction=0;InteractionInstructions=Array.Empty<string>();
  UsabilityDuringPreparedInteraction=UsabilityReads=0;DuringUsability=null;Styx.Logic.Blacklist.Items.Clear();
  MountDisplay=RawForm=UnitFlagsValue=0;DescriptorPointer=0x60000;DescriptorFailure=null;DescriptorError=null;DescriptorRead=null;DescriptorGuid=null;
  Probe.Instructions.Clear();WoWMovement.ActiveMover=actor;actor.Position=new(10,10,0);subject.Position=new(11,10,0);
  Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new();
  Styx.Logic.POI.BotPoi.Current=new(){Type=Styx.Logic.POI.PoiType.QuestPickUp,Guid=subject.Guid,Entry=subject.Entry,AsObject=subject};
  Styx.Logic.POI.BotPoi.CurrentGeneration++;Navigator.NavigationProvider=new();Navigator.PlayerMover=new PlayerMover();GroundSight.Clear=true;
 }
 public static void PrepareUsability(){
  UsabilityReads++;if(Probe.Instructions.Contains("call eax"))UsabilityDuringPreparedInteraction++;
  Probe.Instructions.Clear();Probe.Instructions.Add("controlled-native-usability-query");
  var callback=DuringUsability;DuringUsability=null;callback?.Invoke();
 }
 // Lua uses the same executor assembly storage in production. A final ground
 // guard which invokes it after preparing call-eax destroys that command.
 public static void PrepareLua(string code){
  if(Probe.Instructions.Contains("call eax"))LuaDuringPreparedInteraction++;
  Probe.Instructions.Clear();Probe.Instructions.Add("lua:"+code);
 }
 public static bool TryMovementBytes(uint address,out byte[] bytes){
  bytes=null;var actor=ObjectManager.Me;if(actor==null)return false;
  if(address==actor.BaseAddress+48)bytes=BitConverter.GetBytes(actor.Guid);
  else if(address==actor.BaseAddress+216)bytes=BitConverter.GetBytes(0x50000u);
  else if(address==0x50044)bytes=BitConverter.GetBytes(actor.MovementFlags);
  else if(address==0x50008)bytes=BitConverter.GetBytes(actor.Transport);
  if(bytes==null)return false;if(!actor.MovementKnown)bytes=Array.Empty<byte>();return true;
 }
 public static bool TryDescriptorBytes(uint address,out byte[] bytes){
  bytes=null;var actor=ObjectManager.Me;if(actor==null)return false;
  string field;
  if(address==actor.BaseAddress+8){field="pointer";bytes=BitConverter.GetBytes(DescriptorPointer);}
  else if(address==DescriptorPointer&&DescriptorPointer!=0){field="guid";bytes=BitConverter.GetBytes(DescriptorGuid??actor.Guid);}
  else if(address==DescriptorPointer+(uint)WoWUnitFields.MountDisplayId*4){field="mount";bytes=BitConverter.GetBytes(MountDisplay);}
  else if(address==DescriptorPointer+(uint)WoWUnitFields.Bytes2*4){field="form";bytes=BitConverter.GetBytes(RawForm<<24);}
  else if(address==DescriptorPointer+(uint)WoWUnitFields.Flags*4){field="flags";bytes=BitConverter.GetBytes(UnitFlagsValue);}
  else return false;
  if(DescriptorError!=null)throw DescriptorError;
  if(DescriptorFailure==field)bytes=bytes.Take(2).ToArray();
  DescriptorRead?.Invoke(field);
  return true;
 }
}
""";
    internal const string Cases = """
public static class GroundNativeCases{
 public static void Run(Action<string,Action> test,Action<bool,string> check){
  foreach(string change in new[]{"actor","guid","base","map","memory","executor","run","bot"}){
   string c=change;test("retained pure world owner rejects "+c,()=>{
    var method=typeof(WorldQueryObservation).GetMethod("CaptureLocalOwner",BindingFlags.Static|BindingFlags.NonPublic);
    var current=method==null?(Func<bool>)(()=>true):(Func<bool>)method.Invoke(null,new object[]{ObjectManager.Me});
    check(current()&&Probe.Calls==0,"pure local owner required a native command");
    switch(c){case "actor":ObjectManager.Me=new(){Guid=9};break;case "guid":ObjectManager.Me.Guid++;break;
     case "base":ObjectManager.Me.BaseAddress++;break;case "map":ObjectManager.Me.MapId++;break;
     case "memory":ObjectManager.Wow=new();break;case "executor":ObjectManager.Executor=new(){Memory=ObjectManager.Wow};break;
     case "run":Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();break;case "bot":Styx.Logic.BehaviorTree.TreeRoot.Current=new();break;}
    bool allowed=false;try{allowed=current();}catch(ObservationUnavailableException){}
    check(!allowed&&Probe.Calls==0,"retained world owner inherited "+c+" or dispatched native code");
   });
  }
  test("ground interaction keeps its native command after admission",()=>{
   bool dispatched=GroundTransition.TryInteractWith(GroundProbe.Subject);
   check(dispatched&&GroundProbe.Interactions==1,"healthy ground interaction returned "+dispatched+" but actual interaction entries="+GroundProbe.Interactions);
   check(GroundProbe.LuaDuringPreparedInteraction==0,"ground entry guard overwrote the prepared interaction with Lua");
   check(GroundProbe.InteractionInstructions.SequenceEqual(new[]{"mov ecx, 16384","mov eax, [ecx]","add eax, 176","mov eax, [eax]","call eax","retn"}),"ground admission changed the original interaction ABI");
  });
  WoWGameObject ObjectSubject(){var prior=GroundProbe.Subject;var value=new WoWGameObject{Guid=prior.Guid,BaseAddress=prior.BaseAddress,Entry=prior.Entry,Position=prior.Position};GroundProbe.Subject=value;return value;}
  test("usable ground object preserves native interaction despite a model-centre obstruction",()=>{
   var subject=ObjectSubject();GroundSight.Clear=false;
   check(GroundTransition.TryInteractWith(subject)&&GroundProbe.Interactions==1,"observed usable object was rejected by its model centre");
   check(GroundProbe.UsabilityReads==2&&GroundProbe.UsabilityDuringPreparedInteraction==0&&GroundProbe.LuaDuringPreparedInteraction==0,
    "object usability was missing or overwrote the prepared interaction");
   check(GroundProbe.InteractionInstructions.SequenceEqual(new[]{"mov ecx, 16384","mov eax, [ecx]","add eax, 176","mov eax, [eax]","call eax","retn"}),"object interaction ABI was replaced by its usability query");
  });
  test("native object unusability denies submission",()=>{
   var subject=ObjectSubject();subject.Usable=false;
   check(!GroundTransition.TryInteractWith(subject)&&GroundProbe.Interactions==0,"unusable object reached native interaction");
  });
  foreach(string mutation in new[]{"position","actor","poi"}){
   string change=mutation;test("object usability callback revokes changed "+change,()=>{
    var subject=ObjectSubject();GroundProbe.DuringUsability=()=>{
     if(change=="position")subject.Position=new(30,10,0);
     if(change=="actor")ObjectManager.Me=new(){Guid=9};
     if(change=="poi")Styx.Logic.POI.BotPoi.CurrentGeneration++;
    };
    check(!GroundTransition.TryInteractWith(subject)&&GroundProbe.Interactions==0,"usability callback donated stale object permission");
   });
  }
  foreach(string mutation in new[]{"disabled","blacklisted","range","mount"}){
   string change=mutation;test("object final native guard rejects "+change,()=>{
    var subject=ObjectSubject();Probe.Stage="afk";Probe.StageAction=()=>{
     if(change=="disabled")subject.IsDisabled=true;
     if(change=="blacklisted")Styx.Logic.Blacklist.Items.Add(subject.Guid);
     if(change=="range")ObjectManager.Me.Position=new(50,10,0);
     if(change=="mount")GroundProbe.MountDisplay=123;
    };
    check(!GroundTransition.TryInteractWith(subject)&&GroundProbe.Interactions==0,"final object guard retained stale "+change+" permission");
   });
  }
  foreach(string condition in new[]{"vehicle","unknown","wrong-envelope","missing-value","oversized-envelope"}){
   string c=condition;test("ground vehicle observation/"+c,()=>{
    GroundProbe.VehicleValues=c switch{"vehicle"=>new[]{"world-vehicle","1"},"wrong-envelope"=>new[]{"other","0"},"missing-value"=>new[]{"world-vehicle"},"oversized-envelope"=>new[]{"world-vehicle","0","extra"},_=>Array.Empty<string>()};
    check(!GroundTransition.CanActUnmounted(),"vehicle state "+c+" became ground action authority");
    check(!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,"unknown/occupied vehicle admitted native interaction");
   });
  }
  foreach(Exception signal in new Exception[]{new OperationCanceledException("vehicle cancelled"),new ThreadInterruptedException("vehicle stop"),new InvalidProcessException("vehicle process loss"),new InvalidExecutorException("vehicle executor loss")}){
   var captured=signal;test("ground vehicle preserves "+captured.GetType().Name,()=>{
    Probe.Error=captured;Exception observed=null;try{_=GroundTransition.CanActUnmounted();}catch(Exception e){observed=e;}
    check(ReferenceEquals(observed,captured),"vehicle observation swallowed the actual control signal");
    check(GroundProbe.Interactions==0,"failed vehicle observation dispatched an interaction");
   });
  }
  foreach(string change in new[]{"mount","airborne","transport","mover","movement-unknown","poi","profile"}){
   string c=change;test("ground interaction pure final guard rejects "+c,()=>{
    Probe.Stage="afk";Probe.StageAction=()=>{switch(c){
     case "mount":GroundProbe.MountDisplay=123;break;case "airborne":ObjectManager.Me.MovementFlags=0x02000000;break;
     case "transport":ObjectManager.Me.Transport=8;break;case "mover":WoWMovement.ActiveMover=new WoWUnit{Guid=9};break;
     case "movement-unknown":ObjectManager.Me.MovementKnown=false;break;
     case "poi":Styx.Logic.POI.BotPoi.CurrentGeneration++;break;case "profile":Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new();break;
    }};
    check(!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,"changed ground state reached native interaction");
   });
  }
  test("ground admission rejects a different initial input recipient",()=>{
   WoWMovement.ActiveMover=new WoWUnit{Guid=9,BaseAddress=9000};
   check(!GroundTransition.CanActUnmounted()&&!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,
    "ground action admitted the player while a different unit owns input");
  });
  foreach(Exception signal in new Exception[]{new OperationCanceledException("movement cancelled"),new ThreadInterruptedException("movement stop"),new InvalidProcessException("movement process loss"),new InvalidExecutorException("movement executor loss")})foreach(bool wrapped in new[]{false,true}){
   var captured=signal;bool wrap=wrapped;test("actual movement observation preserves "+captured.GetType().Name+"/"+wrap,()=>{
    GroundProbe.MovementError=wrap?new TargetInvocationException(captured):captured;
    Exception observed=null;try{_=GroundTransition.CanActUnmounted();}catch(Exception e){observed=e;}
    check(ReferenceEquals(observed,captured),"movement observation swallowed a control signal before the native ground guard");
    check(GroundProbe.Interactions==0,"movement observation failure authorized native entry");
   });
  }
  test("actual movement observation preserves complete raw flags and transport",()=>{
   ObjectManager.Me.MovementFlags=0x02000000;ObjectManager.Me.Transport=9;
   check(ObjectManager.Me.TryGetMovementState(out uint flags,out ulong transport)&&flags==0x02000000&&transport==9,
    "movement reader converted the raw observation into a policy result");
  });
  test("actual movement observation incomplete bytes remain unavailable",()=>{
   ObjectManager.Me.MovementKnown=false;
   check(!ObjectManager.Me.TryGetMovementState(out uint flags,out ulong transport)&&flags==0&&transport==0,
    "partial movement bytes fabricated a complete stationary actor");
  });
  test("actual movement observation ordinary read failure remains unavailable",()=>{
   GroundProbe.MovementError=new InvalidOperationException("controlled unreadable memory");
   check(!ObjectManager.Me.TryGetMovementState(out uint flags,out ulong transport)&&flags==0&&transport==0,
    "ordinary unavailable memory became a complete ground observation");
  });
  foreach(string failure in new[]{"pointer","guid","mount","form","flags","zero-pointer","unknown-form","read-error"}){
   string f=failure;test("actual mount/transport descriptors reject "+f,()=>{
    if(f=="zero-pointer")GroundProbe.DescriptorPointer=0;
    else if(f=="unknown-form")GroundProbe.RawForm=255;
    else if(f=="read-error")GroundProbe.DescriptorError=new InvalidOperationException("unreadable unit descriptors");
    else GroundProbe.DescriptorFailure=f;
    check(!GroundTransition.CanActUnmounted(),"failed descriptor "+f+" became an observed unmounted actor");
    check(!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,"failed mount/transport observation authorized native entry");
   });
  }
  foreach(string mounted in new[]{"display","flight-form","epic-flight-form","taxi"}){
   string m=mounted;test("actual mount/transport descriptors retain "+m,()=>{
    if(m=="display")GroundProbe.MountDisplay=123;
    if(m=="flight-form")GroundProbe.RawForm=(uint)ShapeshiftForm.FlightForm;
    if(m=="epic-flight-form")GroundProbe.RawForm=(uint)ShapeshiftForm.EpicFlightForm;
    if(m=="taxi")GroundProbe.UnitFlagsValue=(uint)UnitFlags.OnTaxi;
    check(!GroundTransition.CanActUnmounted()&&!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,
     "actual mounted/form/taxi descriptor was ignored");
   });
  }
  foreach(string change in new[]{"mount","form","flags","pointer","descriptor-guid","movement-airborne","movement-transport","memory","actor","run"}){
   string c=change;test("ground observation rejects changed descriptor/movement/"+c,()=>{
    bool fired=false;GroundProbe.DescriptorRead=field=>{if(field!="mount")return;GroundProbe.DescriptorRead=null;fired=true;switch(c){
     case "mount":GroundProbe.MountDisplay=123;break;case "form":GroundProbe.RawForm=(uint)ShapeshiftForm.FlightForm;break;
     case "flags":GroundProbe.UnitFlagsValue=(uint)UnitFlags.OnTaxi;break;case "pointer":GroundProbe.DescriptorPointer+=1024;break;
     case "descriptor-guid":GroundProbe.DescriptorGuid=99;break;case "movement-airborne":ObjectManager.Me.MovementFlags=0x02000000;break;
     case "movement-transport":ObjectManager.Me.Transport=8;break;case "memory":ObjectManager.Wow=new();break;
     case "actor":ObjectManager.Me=new(){Guid=9};break;case "run":Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();break;
    }};
    check(!GroundTransition.CanActUnmounted()&&fired&&GroundProbe.Interactions==0,"changed state "+c+" authorized ground work");
   });
  }
  foreach(Exception signal in new Exception[]{new OperationCanceledException("descriptor cancelled"),new ThreadInterruptedException("descriptor stop"),new InvalidProcessException("descriptor process loss"),new InvalidExecutorException("descriptor executor loss")}){
   var captured=signal;test("ground descriptors preserve "+captured.GetType().Name,()=>{
    GroundProbe.DescriptorError=captured;Exception observed=null;try{_=GroundTransition.CanActUnmounted();}catch(Exception e){observed=e;}
    check(ReferenceEquals(observed,captured)&&GroundProbe.Interactions==0,"descriptor read swallowed its actual control signal");
   });
  }
  foreach(string stage in new[]{"preflight","afk","instruction"}){
   string boundary=stage;test("actual native interaction rejects swimming/"+boundary,()=>{
    if(boundary=="preflight")ObjectManager.Me.MovementFlags=0x00200000u;
    else {Probe.Stage=boundary;Probe.StageAction=()=>ObjectManager.Me.MovementFlags=0x00200000u;}
    check(!GroundTransition.TryInteractWith(GroundProbe.Subject)&&GroundProbe.Interactions==0,
     "wet actor reached native ground interaction at "+boundary);
   });
  }
  test("underwater unmounted combat admission is preserved",()=>{
   ObjectManager.Me.MovementFlags=0x00200000u;
   check(GroundTransition.CanActUnmounted(),"dry interaction rule blocked the separate aquatic combat admission");
  });
  foreach(var kind in new[]{Styx.Logic.POI.PoiType.QuestPickUp,Styx.Logic.POI.PoiType.QuestTurnIn}){
   var type=kind;test("actual native dead questgiver interaction/"+type,()=>{
    var original=GroundProbe.Subject;
    var unit=new WoWUnit{Guid=original.Guid,BaseAddress=original.BaseAddress,Entry=original.Entry,IsAlive=false,IsQuestGiver=true};
    GroundProbe.Reset(ObjectManager.Me,unit);
    Styx.Logic.POI.BotPoi.Current.Type=type;
    check(GroundTransition.TryInteractWith(unit)&&GroundProbe.Interactions==1&&GroundProbe.LuaDuringPreparedInteraction==0,
     "source-selected dead questgiver failed actual native submission or executed a nested query");
   });
  }
  foreach(string change in new[]{"airborne","transport"}){
   string c=change;test("native ground guard rejects movement changing during its final descriptor read/"+c,()=>{
    bool fired=false;Probe.Stage="instruction";Probe.StageAction=()=>GroundProbe.DescriptorRead=field=>{
     if(field!="mount")return;GroundProbe.DescriptorRead=null;fired=true;
     if(c=="airborne")ObjectManager.Me.MovementFlags=0x02000000;else ObjectManager.Me.Transport=8;
    };
    bool dispatched=GroundTransition.TryInteractWith(GroundProbe.Subject);
    check(fired&&!dispatched&&GroundProbe.Interactions==0,
     "final guard submitted an interaction using movement read before the actor became "+c+"; entries="+GroundProbe.Interactions);
   });
  }
 }
}
""";
}
