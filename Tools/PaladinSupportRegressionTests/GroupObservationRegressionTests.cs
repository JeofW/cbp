using System.Runtime.CompilerServices;
using Styx;
using Styx.Logic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual shared group observer; only Lua/executor/object resolution leaves controlled.
internal static class GroupObservationRegressionTests
{
 [ModuleInitializer] internal static void Run()
 {
  var cases=new List<(string Name,System.Action Test)> {
   ("known solo includes self",()=>{Check(Read(out var m)&&m.Count==1&&ReferenceEquals(m[0],StyxWoW.Me),"solo missing");}),
   ("complete party includes every member",()=>{Fixture.Add();Check(Read(out var m)&&m.Count==2,"party lost member");}),
   ("complete raid includes self once",()=>{Fixture.Add(raid:true);Check(Read(out var m)&&m.Count==2,"raid lost member");}),
   ("unresolved GUID is UNKNOWN",()=>{Fixture.Add();Fixture.RosterObservation=new(){"group-v1","0","2","0x1","0xA","0xB"};Check(!Read(out var m)&&m.Count==0,"partial known");}),
   ("missing token is UNKNOWN",()=>{Fixture.Add();Fixture.RosterObservation=new(){"group-v1","0","1","0x1"};Check(!Read(out _),"missing token known");}),
   ("duplicate GUID is UNKNOWN",()=>{Fixture.Add();Fixture.RosterObservation=new(){"group-v1","0","2","0x1","0xA","0xA"};Check(!Read(out _),"duplicate known");}),
   ("nil GUID is UNKNOWN",()=>{Fixture.Add();Fixture.RosterObservation=new(){"group-v1","0","1","0x1",""};Check(!Read(out _),"nil known");}),
   ("changed player token is UNKNOWN",()=>{Fixture.RosterObservation=new(){"group-v1","0","0","0xFF"};Check(!Read(out _),"wrong owner known");}),
   ("empty Lua is not solo",()=>{Fixture.RosterObservation=new();Check(!Read(out _),"empty became solo");}),
   ("raid count over bound is UNKNOWN",()=>{Fixture.RosterObservation=new(){"group-v1","41","0","0x1"};Check(!Read(out _),"unbounded known");}),
   ("flags and Lua mode disagreement is UNKNOWN",()=>{StyxWoW.Me.IsInParty=true;Check(!Read(out _),"mismatch became solo");}),
   ("member replacement during later lookup is UNKNOWN",()=>{var first=Fixture.Add();var second=Fixture.Add();ObjectManager.Resolve=g=>{if(g==second.Guid)first.Guid=second.Guid;return g==10?first:second;};Check(!Read(out _),"replaced member borrowed another GUID");}),
   ("actor replacement during mapping is UNKNOWN",()=>{var p=Fixture.Add();ObjectManager.Resolve=g=>{StyxWoW.Me=new LocalPlayer{Guid=1};return p;};Check(!Read(out _),"replacement owner retained roster");}),
   ("executor replacement during mapping is UNKNOWN",()=>{var p=Fixture.Add();ObjectManager.Resolve=g=>{ObjectManager.Wow.Executor=new();return p;};Check(!Read(out _),"replacement executor retained roster");}),
   ("mapping across frames is UNKNOWN",()=>{var p=Fixture.Add();ObjectManager.Resolve=g=>{ObjectManager.Wow.Executor.FrameCount++;return p;};Check(!Read(out _),"stale frame retained roster");}),
   ("same-frame consumers reuse complete snapshot",()=>{Check(Read(out _)&&Read(out _),"known read failed");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==1,"same epoch repeated Lua");}),
   ("prepared action missing cache cannot query",()=>{Check(!GroupObservation.TryGetMembers(StyxWoW.Me,out _,out _,allowQuery:false),"uncached prepared roster became known");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==0,"prepared action issued Lua");}),
   ("prepared action reuses complete current frame",()=>{Check(Read(out _)&&GroupObservation.TryGetMembers(StyxWoW.Me,out _,out _,allowQuery:false),"current cached roster lost");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==1,"prepared action refreshed Lua");}),
   ("prepared action same-frame replacement is UNKNOWN without Lua",()=>{var first=Fixture.Add();Check(Read(out _),"initial observation failed");int before=Fixture.LuaQueries.Count(q=>q.Contains("group-v1"));var replacement=new WoWPlayer{Guid=first.Guid,BaseAddress=first.BaseAddress+100,Class=first.Class,Name=first.Name};ObjectManager.Resolve=g=>g==first.Guid?replacement:null;Check(!GroupObservation.TryGetMembers(StyxWoW.Me,out _,out _,allowQuery:false),"same-GUID replacement borrowed cached roster authority");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==before,"query=false refreshed Lua while rejecting replacement");}),
   ("prepared action same-wrapper base replacement is UNKNOWN without Lua",()=>{var first=Fixture.Add();Check(Read(out _),"initial observation failed");int before=Fixture.LuaQueries.Count(q=>q.Contains("group-v1"));first.BaseAddress++;Check(!GroupObservation.TryGetMembers(StyxWoW.Me,out _,out _,allowQuery:false),"base replacement borrowed cached roster authority");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==before,"query=false refreshed Lua while rejecting base replacement");}),
   ("prepared action stale frame denies without query",()=>{Check(Read(out _),"initial observation failed");ObjectManager.Wow.Executor.FrameCount++;Check(!GroupObservation.TryGetMembers(StyxWoW.Me,out _,out _,allowQuery:false),"stale prepared roster admitted");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==1,"stale prepared action refreshed Lua");}),
   ("new frame observes changed membership",()=>{Check(Read(out _),"initial failed");Fixture.Add();ObjectManager.Wow.Executor.FrameCount++;Check(Read(out var m)&&m.Count==2,"new frame reused old group");}),
   ("ordinary resolution failure remains UNKNOWN",()=>{Fixture.Add();ObjectManager.Resolve=g=>throw new InvalidOperationException("unavailable object");Check(!Read(out _),"resolution error became known");}),
   ("explicit cancellation is preserved",()=>{Fixture.Add();var signal=new OperationCanceledException("owned cancellation");PallyPowerControlFlowRegressionTests.Signal=signal;ObjectManager.Resolve=g=>throw signal;Exception? caught=null;try{Read(out _);}catch(Exception e){caught=e;}finally{PallyPowerControlFlowRegressionTests.Signal=null;}Check(ReferenceEquals(caught,signal),"group owner swallowed cancellation");}),
   ("wrapped interruption is preserved",()=>{Fixture.Add();var signal=new System.Threading.ThreadInterruptedException("owned interruption");PallyPowerControlFlowRegressionTests.Signal=signal;ObjectManager.Resolve=g=>throw new System.Reflection.TargetInvocationException(signal);Exception? caught=null;try{Read(out _);}catch(Exception e){caught=e;}finally{PallyPowerControlFlowRegressionTests.Signal=null;}Check(ReferenceEquals(caught,signal),"group owner swallowed wrapped interruption");}),
   ("zero frame does not cache",()=>{ObjectManager.Wow.Executor.FrameCount=0;Check(Read(out _)&&Read(out _),"zero read failed");Check(Fixture.LuaQueries.Count(q=>q.Contains("group-v1"))==2,"zero frame reused indefinitely");})
  };
  int passed=0;foreach(var c in cases){Fixture.Reset();try{c.Test();passed++;Console.WriteLine("PASS complete group: "+c.Name);}catch(Exception e){Console.Error.WriteLine("FAIL complete group: "+c.Name+": "+e);}}
  Fixture.Reset();Console.WriteLine($"Complete group observation: {passed}/{cases.Count}");if(passed!=cases.Count)throw new InvalidOperationException("group regressions");
 }
 private static bool Read(out IReadOnlyList<WoWPlayer> members)=>GroupObservation.TryGetMembers(StyxWoW.Me,out members,out _);
 private static void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
}
