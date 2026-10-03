// Actual LevelBot search and safety methods execute through the actual shared
// collision batch owner. Only world observations, mesh replies and the native
// executor are controlled; no resurrection or movement request is issued.
internal static class CorpseSearchProbe
{
    internal const string Leaves = """
public static class CorpseSearchCases {
 public static bool DirectPath;public static Func<WoWPoint,WoWPoint,WoWPoint[]> Path;
 static WoWPoint corpse=new(10,10,0);
 static void Setup(int hostileCount=1){
  DirectPath=true;Path=(from,to)=>new[]{from,new WoWPoint(to.X,to.Y,0)};
  ObjectManager.Me.CorpsePoint=corpse;ObjectManager.Me.Position=new(10,10,0);
  ObjectManager.Me.IsAlive=false;ObjectManager.Me.IsGhost=true;
  ObjectManager.Units=Enumerable.Range(0,hostileCount).Select(i=>new WoWUnit{Guid=(ulong)(100+i),BaseAddress=(uint)(10000+100*i),Position=corpse,IsHostile=true,MyAggroRange=20}).ToList();
  Probe.Batch=true;Probe.BatchHits=new byte[168];Probe.BatchPoints=Enumerable.Repeat(new WoWPoint(float.NaN,float.NaN,float.NaN),168).ToArray();
 }
 static bool Valid(WoWPoint point)=>float.IsFinite(point.X)&&float.IsFinite(point.Y)&&float.IsFinite(point.Z);
 public static void Run(Action<string,Action> test,Action<bool,string> check){
  foreach(int count in new[]{1,41,42,43}){int captured=count;
   test("corpse search evaluates supported escape points with "+captured+" hostiles",()=>{
    Setup(captured);WoWPoint result=WoWPoint.Empty;Exception error=null;try{result=Bots.Grind.LevelBot.Find();}catch(Exception observed){error=observed;}
    check(error==null&&Valid(result)&&result.Distance(corpse)>=25&&result.Distance(corpse)<39&&result.Z==0&&Probe.Calls==1,
      "safe search never reached a complete collision/mesh result: "+error?.Message);
   });
  }
  test("corpse direct safe position needs no collision batch",()=>{Setup(0);var result=Bots.Grind.LevelBot.Find();check(result==corpse&&Probe.Calls==0,"safe original point was lost");});
  test("corpse search checks alternate geometry when direct path unavailable",()=>{Setup(0);DirectPath=false;WoWPoint result=WoWPoint.Empty;Exception error=null;try{result=Bots.Grind.LevelBot.Find();}catch(Exception observed){error=observed;}check(error==null&&Valid(result)&&Probe.Calls==1,"alternate path was not evaluated: "+error?.Message);});
  foreach(string mode in new[]{"blocked","no-path","short-path","vertical-end","outside-recovery-range","unsafe-end"}){string captured=mode;
   test("corpse search rejects "+captured,()=>{
    Setup();if(captured=="blocked")Probe.During=()=>{Probe.BatchHits=Enumerable.Repeat((byte)1,168).ToArray();Probe.BatchPoints=Enumerable.Repeat(corpse.Add(0,0,2.132f),168).ToArray();};
    if(captured=="no-path")Path=(a,b)=>Array.Empty<WoWPoint>();
    if(captured=="short-path")Path=(a,b)=>new[]{a,corpse};
    if(captured=="vertical-end")Path=(a,b)=>new[]{a,b.Add(0,0,10)};
    if(captured=="outside-recovery-range")Path=(a,b)=>new[]{a,new WoWPoint(100,100,0)};
    if(captured=="unsafe-end")Path=(a,b)=>new[]{a,new WoWPoint(10,10,0)};
    WoWPoint result=WoWPoint.Empty;Exception error=null;try{result=Bots.Grind.LevelBot.Find();}catch(Exception observed){error=observed;}
    check(error==null&&!Valid(result)&&Probe.Calls==1,"failed geometry did not produce a known unavailable safe destination: "+error?.Message);
   });
  }
  foreach(string mode in new[]{"short-results","missing-executor","actor-replaced","run-replaced","invalid-hit"}){string captured=mode;
   test("corpse collision retains UNKNOWN/"+captured,()=>{
    Setup();if(captured=="short-results")Probe.ShortBatchHits=true;
    if(captured=="missing-executor")ObjectManager.Executor=null;
    if(captured=="actor-replaced")Probe.During=()=>ObjectManager.Me=new LocalPlayer();
    if(captured=="run-replaced")Probe.During=()=>Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new();
    if(captured=="invalid-hit")Probe.During=()=>Probe.BatchHits[0]=2;
    Exception error=null;try{_=Bots.Grind.LevelBot.Find();}catch(Exception observed){error=observed;}
    check(error is ObservationUnavailableException&&Probe.Calls==(captured=="missing-executor"?0:1),"unknown collision became a safe point or never reached its expected boundary");
   });
  }
  test("corpse collision cancellation propagates",()=>{Setup();var expected=new OperationCanceledException("corpse cancellation");Probe.Error=expected;Exception error=null;try{_=Bots.Grind.LevelBot.Find();}catch(Exception observed){error=observed;}check(ReferenceEquals(error,expected)&&Probe.Calls==1,"corpse collision swallowed cancellation");});
  test("zero-length collision input remains invalid",()=>{Setup();Exception error=null;try{GameWorld.MassTraceLine(new[]{new WorldLine(corpse,corpse)},new[]{GameWorld.CGWorldFrameHitFlags.HitTestLOS},out _,out _);}catch(Exception observed){error=observed;}check(error is ObservationUnavailableException&&Probe.Calls==0,"shared collision contract was relaxed");});
  Console.WriteLine("Corpse search integration: 19 cases; actual LevelBot and shared collision owner; controlled native/mesh observations.");
 }
}
""";
}
