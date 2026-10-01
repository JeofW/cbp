internal static class CollisionBlackspotLifetimeFixture
{
    internal const string Prefix = """
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Tripper.Navigation;
namespace CollisionBlackspotProbe {
""";
    internal const string Boundary = """
public static class Environment { public static long TickCount64=>NativeState.Now; }
public sealed class Actor { public uint MapId; }
public static class StyxWoW { public static Actor Me=new Actor(); }
public static class Logging
{
 public static string ApplicationPath;
 public static readonly List<string> Messages=new();
 public static void Write(string value)=>Messages.Add(value);
 public static void WriteDebug(string value)=>Messages.Add(value);
}
public sealed class ProfileValue { public List<Blackspot> Blackspots=new(); }
public static class ProfileManager { public static ProfileValue CurrentProfile; }
public static class BotEvents
{
 public static event Action<EventArgs> OnBotStopped;
 public static void Stop()=>OnBotStopped?.Invoke(EventArgs.Empty);
 public static class Profile
 {
  public sealed class NewProfileLoadedEventArgs { public ProfileValue OldProfile,NewProfile; }
  public static event Action<NewProfileLoadedEventArgs> OnNewProfileLoaded;
  public static void Change(ProfileValue oldProfile,ProfileValue newProfile)=>OnNewProfileLoaded?.Invoke(new NewProfileLoadedEventArgs{OldProfile=oldProfile,NewProfile=newProfile});
 }
}
public sealed class TileLoadedEventArgs:EventArgs { public uint MapId;public int TileX,TileY; }
public sealed class NativeNavigator
{
 public event EventHandler<TileLoadedEventArgs> TileLoaded;
 public void Load(uint map,WoWPoint point){var tile=TileIdentifier.GetByPosition(point.X,point.Y);TileLoaded?.Invoke(this,new TileLoadedEventArgs{MapId=map,TileX=tile.X,TileY=tile.Y});}
}
public static class Navigator
{
 public static NativeNavigator TripperNavigator=new NativeNavigator();
 public static event EventHandler<NavigationProviderChangedEventArgs<NavigationProvider>> OnNavigationProviderChanged;
}
public static class NativeState
{
 public const uint Success=0x40000000,Failure=0x80000000;
 public static long Now=1000;
 public static bool AreaKnown=true,FlagsKnown=true,RejectAreaRestore,RejectFlagsRestore;
 public static readonly Dictionary<(uint,ulong),byte> Areas=new();
 public static readonly Dictionary<(uint,ulong),ushort> Flags=new();
 public static readonly List<(string Kind,uint Map,ulong Poly,int Value)> Calls=new();
 public static int QueryCount=1;
 public static string CallbackStage;public static Action Callback;
 public static void Pulse(string stage){if(CallbackStage!=stage)return;var action=Callback;CallbackStage=null;Callback=null;action?.Invoke();}
 public static void Reset(){Now=1000;AreaKnown=FlagsKnown=true;RejectAreaRestore=RejectFlagsRestore=false;Areas.Clear();Flags.Clear();Calls.Clear();QueryCount=1;CallbackStage=null;Callback=null;}
 public static byte Area(uint map,ulong poly)=>Areas.TryGetValue((map,poly),out byte value)?value:(byte)3;
 public static ushort Flag(uint map,ulong poly)=>Flags.TryGetValue((map,poly),out ushort value)?value:(ushort)0x22;
}
public static class NativeMethods
{
 public struct XYZ { public float X,Y,Z;public XYZ(float x,float y,float z){X=x;Y=y;Z=z;} }
 public static void SetAreaCost(uint map,int area,float cost){NativeState.Calls.Add(("cost",map,0,area));}
 public static void EnsureTiles(uint map,XYZ center,int radius){NativeState.Calls.Add(("ensure",map,0,radius));}
 public static int QueryPolygons(uint map,XYZ center,XYZ extent,IntPtr target,int maximum)
 {NativeState.Calls.Add(("query",map,0,NativeState.QueryCount));if(NativeState.QueryCount==1)Marshal.WriteInt64(target,101);return NativeState.QueryCount;}
 public static uint GetPolyArea(uint map,ulong poly,out byte area)
 {area=NativeState.Area(map,poly);NativeState.Calls.Add(("read-area",map,poly,area));return NativeState.AreaKnown?NativeState.Success:NativeState.Failure;}
 public static uint GetPolyFlags(uint map,ulong poly,out ushort flags)
 {flags=NativeState.Flag(map,poly);NativeState.Calls.Add(("read-flags",map,poly,flags));NativeState.Pulse("read-flags");return NativeState.FlagsKnown?NativeState.Success:NativeState.Failure;}
 public static uint SetPolyArea(uint map,ulong poly,byte area)
 {NativeState.Calls.Add(("write-area",map,poly,area));if(area!=17&&NativeState.RejectAreaRestore)return NativeState.Failure;NativeState.Areas[(map,poly)]=area;NativeState.Pulse(area==17?"mark-area":"restore-area");return NativeState.Success;}
 public static uint SetPolyFlags(uint map,ulong poly,ushort flags)
 {NativeState.Calls.Add(("write-flags",map,poly,flags));if(NativeState.RejectFlagsRestore)return NativeState.Failure;NativeState.Flags[(map,poly)]=flags;return NativeState.Success;}
}
public static class Cases
{
 private static readonly Blackspot A=new(new WoWPoint(10,10,4),3,4),B=new(new WoWPoint(12,10,4),3,4);
 private static int passed,failed,unexpected;
 private static readonly BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 public static void Run(string folder)
 {
  Logging.ApplicationPath=folder;
  Case("known area and flags restore exactly",()=>{BlackspotManager.AddBlackspots(new[]{A});Check(NativeState.Area(0,101)==17,"mark missing");BlackspotManager.RemoveBlackspot(A);Check(NativeState.Area(0,101)==3&&NativeState.Flag(0,101)==0x22,"original native state changed");});
  Case("unknown original area does not authorize marking",()=>{NativeState.AreaKnown=false;BlackspotManager.AddBlackspots(new[]{A});Check(!NativeState.Calls.Any(c=>c.Kind=="write-area"),"unknown area was overwritten");});
  Case("unknown original flags do not authorize marking",()=>{NativeState.FlagsKnown=false;BlackspotManager.AddBlackspots(new[]{A});Check(!NativeState.Calls.Any(c=>c.Kind=="write-area"),"unknown flags were accepted for a future restore");});
  Case("unknown flags never become a guessed walkable mask",()=>{NativeState.FlagsKnown=false;BlackspotManager.AddBlackspots(new[]{A});BlackspotManager.RemoveBlackspot(A);Check(!NativeState.Calls.Any(c=>c.Kind=="write-flags"),"unknown original flags were replaced with a fabricated mask");});
  Case("failed area restore remains retryable",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.RejectAreaRestore=true;BlackspotManager.RemoveBlackspot(A);Check(NativeState.Area(0,101)==17,"negative restore did not fail");NativeState.RejectAreaRestore=false;BlackspotManager.EnsureBlackspotsMarked();Check(NativeState.Area(0,101)==3&&NativeState.Flag(0,101)==0x22,"failed native restore evidence was discarded");});
  Case("failed flags restore remains retryable",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.Flags[(0,101)]=0x12;NativeState.RejectFlagsRestore=true;BlackspotManager.RemoveBlackspot(A);Check(NativeState.Flag(0,101)==0x12,"negative flag restore did not fail");NativeState.RejectFlagsRestore=false;BlackspotManager.EnsureBlackspotsMarked();Check(NativeState.Flag(0,101)==0x22,"failed flag restoration was forgotten");});
  Case("overlapping polygon owners postpone restoration",()=>{BlackspotManager.AddBlackspots(new[]{A,B});NativeState.Calls.Clear();BlackspotManager.RemoveBlackspot(A);Check(NativeState.Area(0,101)==17&&!NativeState.Calls.Any(c=>c.Kind=="write-area"),"one owner restored another's polygon");BlackspotManager.RemoveBlackspot(B);Check(NativeState.Area(0,101)==3,"last owner did not restore original area");});
  Case("new polygon owner prevents a pending restore",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.RejectAreaRestore=true;BlackspotManager.RemoveBlackspot(A);NativeState.RejectAreaRestore=false;BlackspotManager.AddBlackspots(new[]{B});BlackspotManager.EnsureBlackspotsMarked();Check(NativeState.Area(0,101)==17,"pending restore erased the replacement owner");BlackspotManager.RemoveBlackspot(B);Check(NativeState.Area(0,101)==3,"replacement owner inherited a blackspot as its original area");});
  Case("map zero rejects a foreign tile callback",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.Calls.Clear();Navigator.TripperNavigator.Load(530,A.Location);Check(!NativeState.Calls.Any(c=>c.Map==530),"map zero was mistaken for unknown-map permission");});
  Case("matching map-zero tile remains eligible",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.Calls.Clear();Navigator.TripperNavigator.Load(0,A.Location);Check(NativeState.Calls.Any(c=>c.Kind=="query"&&c.Map==0),"valid map-zero callback was discarded");});
  Case("temporary collision expires at its boundary",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=30999;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.Blackspots.Contains(A),"lease expired early");NativeState.Now=31000;BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==3,"temporary collision never expired/restored");});
  Case("expiry cannot remove a permanent region",()=>{BlackspotManager.AddBlackspots(new[]{A});Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=100000;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==17,"permanent ownership acquired a temporary deadline");});
  Case("explicit permanent registration promotes a temporary region",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));BlackspotManager.AddBlackspots(new[]{A});NativeState.Now=100000;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==17,"expired lease deleted later permanent registration");});
  Case("temporary map ownership cannot leak to another map",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));StyxWoW.Me.MapId=530;NativeState.Calls.Clear();BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A)&&!NativeState.Calls.Any(c=>c.Kind=="write-area"&&c.Map==530),"prior-map collision was reapplied in a different world");});
  Case("expired temporary region cannot be resurrected by tile loading",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=31000;NativeState.Calls.Clear();Navigator.TripperNavigator.Load(0,A.Location);Check(!BlackspotManager.Blackspots.Contains(A)&&!NativeState.Calls.Any(c=>c.Kind=="write-area"&&c.Value==17),"tile event revived an expired collision");});
  Case("clock rollback cannot extend a temporary lease",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=999;BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A),"rollback extended collision lifetime");});
  Case("duplicate temporary reports do not slide the deadline",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=20000;Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=31000;BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A),"duplicate collision reports kept a region alive indefinitely");});
  Case("expired lease can be replaced by a fresh collision",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=31000;BlackspotManager.EnsureBlackspotsMarked();Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.Now=40000;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.Blackspots.Contains(A),"a fresh collision inherited an expired lease");NativeState.Now=61000;BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A),"new collision lost its own deadline");});
  Case("bot stop clears transient leases",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));BotEvents.Stop();Check(!BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==3,"bot stop retained temporary ownership");NativeState.Now=2000;BlackspotManager.AddBlackspots(new[]{A});NativeState.Now=40000;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.Blackspots.Contains(A),"stale lease removed a later permanent region");});
  Case("expired polygon restoration can recover after a native failure",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));NativeState.RejectAreaRestore=true;NativeState.Now=31000;BlackspotManager.EnsureBlackspotsMarked();Check(!BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==17,"expiry control did not keep failed native state explicit");NativeState.RejectAreaRestore=false;BlackspotManager.EnsureBlackspotsMarked();Check(NativeState.Area(0,101)==3,"expired native restoration cannot retry");});
  Case("explicit global region replaces temporary ownership",()=>{Temporary(A,0,TimeSpan.FromSeconds(30));BlackspotManager.AddGlobalBlackspot(A.Location,A.Radius,0,"Explicit permanent rule");NativeState.Now=31000;BlackspotManager.EnsureBlackspotsMarked();Check(BlackspotManager.GlobalBlackspots.Any(value=>value.MapId==0&&value.Blackspot.Location==A.Location)&&BlackspotManager.IsBlackspotted(A.Location),"temporary collision suppressed explicit permanent registration");});
  Case("caller snapshots do not mutate after later registration",()=>{var saved=BlackspotManager.Blackspots;BlackspotManager.AddBlackspots(new[]{A});Check(saved.Count==0,"read-only snapshot changed after lock release");});
  Case("foreign-map collision observation cannot register",()=>{Temporary(A,530,TimeSpan.FromSeconds(30));Check(!BlackspotManager.Blackspots.Any()&&!NativeState.Calls.Any(call=>call.Kind=="write-area"),"foreign map observation created a current-world region");});
  Case("removed owner after original-flags read cannot mark",()=>{NativeState.CallbackStage="read-flags";NativeState.Callback=()=>BlackspotManager.RemoveBlackspot(A);BlackspotManager.AddBlackspots(new[]{A});Check(!BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==3,"native read callback revoked the owner but marking continued");});
  Case("removed owner during native mark restores its unowned polygon",()=>{NativeState.CallbackStage="mark-area";NativeState.Callback=()=>BlackspotManager.RemoveBlackspot(A);BlackspotManager.AddBlackspots(new[]{A});Check(!BlackspotManager.Blackspots.Contains(A)&&NativeState.Area(0,101)==3,"native mark callback left an unowned avoidance polygon");});
  Case("replacement native owner survives a revoked mark",()=>{NativeState.CallbackStage="mark-area";NativeState.Callback=()=>{BlackspotManager.RemoveBlackspot(A);BlackspotManager.AddBlackspots(new[]{B});};BlackspotManager.AddBlackspots(new[]{A});Check(!BlackspotManager.Blackspots.Contains(A)&&BlackspotManager.Blackspots.Contains(B)&&NativeState.Area(0,101)==17,"old mark erased replacement native ownership");BlackspotManager.RemoveBlackspot(B);Check(NativeState.Area(0,101)==3&&NativeState.Flag(0,101)==0x22,"replacement could not recover original state");});
  Case("new owner during native restore prevents stale flags write",()=>{BlackspotManager.AddBlackspots(new[]{A});NativeState.CallbackStage="restore-area";NativeState.Callback=()=>BlackspotManager.AddBlackspots(new[]{B});NativeState.Calls.Clear();BlackspotManager.RemoveBlackspot(A);Check(NativeState.Area(0,101)==17&&!NativeState.Calls.Any(call=>call.Kind=="write-flags"),"stale restore wrote after replacement ownership");BlackspotManager.RemoveBlackspot(B);Check(NativeState.Area(0,101)==3,"replacement restore failed");});
  foreach(var duration in new[]{TimeSpan.Zero,TimeSpan.FromMilliseconds(-1),TimeSpan.FromMinutes(6)})
   Case("invalid temporary duration "+duration,()=>{bool rejected=false;try{Temporary(A,0,duration);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected&&!BlackspotManager.Blackspots.Contains(A),"invalid lifetime authorized a persistent region");});
  foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
   Case("non-finite collision coordinate "+bad,()=>{bool rejected=false;try{Temporary(new Blackspot(new WoWPoint(10,10,bad),3,4),0,TimeSpan.FromSeconds(30));}catch(ArgumentException){rejected=true;}Check(rejected&&!BlackspotManager.Blackspots.Any(),"unknown geometry authorized a native region");});
  Console.WriteLine($"Collision blackspot lifetime cases: {passed}/{passed+failed+unexpected}; assertions={failed}; unexpected={unexpected}; complete manager, controlled native/time/map boundaries; no live terrain.");
  if(failed!=0||unexpected!=0)throw new InvalidOperationException("Collision blackspot lifetime regressions failed");
 }
 private static void Temporary(Blackspot spot,uint map,TimeSpan duration)
 {
  var method=typeof(BlackspotManager).GetMethod("AddTemporaryCollisionBlackspot",Hidden);
  // The baseline's collision caller uses this permanent API. Retain that real
  // behavior when the bounded method is absent, so expiry assertions fail.
  if(method==null){BlackspotManager.AddBlackspot(spot.Location,spot.Radius,spot.Height,"LiveCollision");return;}
  try{method.Invoke(null,new object[]{spot,map,duration});}
  catch(TargetInvocationException error)when(error.InnerException!=null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();}
 }
 private static void Case(string name,Action test)
 {
  Reset();
  try{test();passed++;}
  catch(ExpectedFailure error){failed++;Console.WriteLine("FAIL blackspot lifetime "+name+": "+error.Message);}
  catch(Exception error){unexpected++;Console.WriteLine("ERROR blackspot lifetime "+name+": "+error);}
  finally{Reset();}
 }
 private static void Reset()
 {
  NativeState.RejectAreaRestore=NativeState.RejectFlagsRestore=false;NativeState.AreaKnown=NativeState.FlagsKnown=true;
  BlackspotManager.ClearBlackspots();
  foreach(var field in typeof(BlackspotManager).GetFields(Hidden))
  {
   if(field.Name=="_globalBlackspots"||field.Name=="_blackspotPolygons"||field.Name=="_originalPolyAreas"||field.Name=="_originalPolyFlags"||field.Name=="_temporaryBlackspots")
   {var value=field.GetValue(null);value?.GetType().GetMethod("Clear",Type.EmptyTypes)?.Invoke(value,null);}
  }
  NativeState.Reset();StyxWoW.Me=new Actor{MapId=0};ProfileManager.CurrentProfile=null;Logging.Messages.Clear();
 }
 private static void Check(bool value,string message){if(!value)throw new ExpectedFailure(message);}
 private sealed class ExpectedFailure:Exception{public ExpectedFailure(string message):base(message){}}
}
""";
}
