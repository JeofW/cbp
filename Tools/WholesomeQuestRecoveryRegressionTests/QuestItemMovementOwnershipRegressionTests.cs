using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Complete UseItemOn root with real TreeSharp; reuse the retained world boundary.
// Navigation, status callbacks and actor/object observations are controlled.
internal static class QuestItemMovementOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        string boundary = (string)typeof(QuestItemTargetSelectionRegressionTests).GetField("Boundary", flags)!.GetRawConstantValue()!;
        void Replace(string before, string after)
        {
            int at = boundary.IndexOf(before, StringComparison.Ordinal);
            if (at < 0 || boundary.IndexOf(before, at + before.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Controlled boundary changed: " + before);
            boundary = boundary.Remove(at, before.Length).Insert(at, after);
        }
        Replace("public static string StatusText{get;set;}=\"\";", "private static string status=\"\";public static string StatusText{get=>status;set{status=value;QuestItemMovementCases.Status(value);}}");
        Replace("public static MoveResult MoveTo(WoWPoint p)=>MoveResult.Moved;", "public static MoveResult MoveTo(WoWPoint p){QuestItemMovementCases.Moved(\"mesh\",p);return QuestItemMovementCases.Result;}");
        Replace("public static void ClickToMove(WoWPoint p){}", "public static void ClickToMove(WoWPoint p){QuestItemMovementCases.Moved(\"ctm\",p);}");
        Replace("public bool TryUseContainerItem()=>true;", "public bool TryUseContainerItem(){QuestItemMovementCases.Uses++;return true;}");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-item-movement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.Copy(Path.Combine(root, "runtime-snapshot/Quest Behaviors/UseItemOn.cs"), Path.Combine(temporary, "UseItemOn.cs"));
            File.WriteAllText(Path.Combine(temporary, "Boundary.cs"), boundary + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Complete quest movement compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("QuestItemMovementCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temporary, true); }
    }
    private const string Cases = """

public static class QuestItemMovementCases {
 private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 private sealed class Failure(string text):Exception(text){}
 private static Script owner;private static LocalPlayer player;private static WoWUnit target;
 private static System.Action callback;private static bool fired;
 private static readonly List<(string kind,WoWPoint destination,ulong actor)> moves=new();
 public static MoveResult Result;public static int Uses;
 public static void Moved(string kind,WoWPoint point)=>moves.Add((kind,point,ObjectManager.Me?.Guid??0));
 public static void Status(string value){if(!fired&&value.StartsWith("Moving to",StringComparison.Ordinal)){fired=true;var change=callback;callback=null;change?.Invoke();}}
 private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
 private static void Set(string name,object value)=>typeof(Script).GetProperty(name,Hidden)!.SetValue(owner,value);
 private static RunStatus Pulse(){
  var errors=new List<string>();
  void Record(Styx.Helpers.LogLevel level,string message){if(message.Contains("Exception")||message.Contains("Object reference not set"))errors.Add(message);}
  Styx.Helpers.Logging.OnMessageLogged+=Record;
  try{var tree=(Composite)typeof(Script).GetMethod("CreateBehavior",Hidden)!.Invoke(owner,null)!;tree.Start(null!);try{
   int ticks=0;RunStatus status;while((status=tree.Tick(null!))==RunStatus.Running)if(++ticks>20)throw new Failure("unbounded movement decision");
   Check(errors.Count==0,"owner swallowed an observation exception: "+string.Join(";",errors));return status;
  }finally{tree.Stop(null!);}}finally{Styx.Helpers.Logging.OnMessageLogged-=Record;}
 }
 private static void Reset(int family){
  typeof(QuestItemSelectionCases).GetMethod("Reset",Hidden)!.Invoke(null,null);
  owner=(Script)typeof(QuestItemSelectionCases).GetField("owner",Hidden)!.GetValue(null)!;
  target=(WoWUnit)typeof(QuestItemSelectionCases).GetField("candidate",Hidden)!.GetValue(null)!;
  player=ObjectManager.Me!;target.Location=new WoWPoint(30,10,15);
  typeof(Script).GetField("_isDisposed",Hidden)!.SetValue(owner,false);GC.SuppressFinalize(owner);
  Set("NavigationState",family==1?Script.NavigationType.CTM:Script.NavigationType.Mesh);
  Set("WaitForNpcs",true);Set("MobAuraMissingName","Forbidden");Set("WaitTime",100);
  if(family==2){ObjectManager.Objects!.Clear();Set("Location",new WoWPoint(-30,20,15));}
  callback=null;fired=false;moves.Clear();Uses=0;Result=MoveResult.Moved;
 }
 public static void Run(){
  var cases=new List<(string,System.Action)>();
  foreach(int index in new[]{0,1,2}){
   int family=index;string prefix=new[]{"mesh recipient","CTM recipient","profile location"}[family];
   void Add(string name,System.Action body)=>cases.Add((prefix+": "+name,()=>{Reset(family);body();}));
   void At(string name,System.Action change)=>Add(name,()=>{callback=change;Pulse();Check(fired,"status callback not reached");Check(moves.Count==0&&Uses==0,"revoked decision moved, substituted a route, or used an item");});
   Add("ordinary approach preserves all coordinates",()=>{var expected=family==2?new WoWPoint(-30,20,15):target.Location;Check(Pulse()==RunStatus.Success&&moves.Count==1&&moves[0].destination==expected&&moves[0].actor==1,"ordinary approach changed");});
   At("actor reference changes before dispatch",()=>ObjectManager.Me=new LocalPlayer{Guid=1,Location=player.Location});
   At("actor GUID changes before dispatch",()=>player.Guid=9);
   At("actor disappears before dispatch",()=>ObjectManager.Me=null);
   At("actor dies before dispatch",()=>player.IsAlive=false);
   At("actor becomes invalid before dispatch",()=>player.IsValid=false);
   At("behavior is disposed before dispatch",()=>typeof(Script).GetField("_isDisposed",Hidden)!.SetValue(owner,true));
   At("behavior is completed before dispatch",()=>typeof(Script).GetField("_isBehaviorDone",Hidden)!.SetValue(owner,true));
   At("cast begins before dispatch",()=>player.IsCasting=true);
   At("channel begins before dispatch",()=>player.ChanneledCastingSpellId=101);
   foreach(string reason in new[]{"null","invalid","dead","zero-guid","casting","channel"}){
    string state=reason;Add("initial actor "+state+" cannot move",()=>{
     if(state=="null")ObjectManager.Me=null;else if(state=="invalid")player.IsValid=false;else if(state=="dead")player.IsAlive=false;else if(state=="zero-guid")player.Guid=0;else if(state=="casting")player.IsCasting=true;else player.ChanneledCastingSpellId=101;
     Pulse();Check(moves.Count==0&&Uses==0,"unavailable actor authorized movement");
    });
   }
   foreach(string reason in new[]{"reference","guid","missing"}){
    string state=reason;Add("a later pulse cannot transfer the existing behavior to "+state,()=>{
     Pulse();Check(moves.Count==1,"first owned approach missing");moves.Clear();
     if(state=="reference")ObjectManager.Me=new LocalPlayer{Guid=1,Location=player.Location};else if(state=="guid")player.Guid=9;else ObjectManager.Me=null;
     Pulse();Check(moves.Count==0&&Uses==0,"old lifetime followed a replacement actor");
    });
   }
   if(family!=1)foreach(MoveResult value in Enum.GetValues<MoveResult>().Concat(new[]{(MoveResult)987654})){
    var receipt=value;Add("navigation receipt "+receipt,()=>{Result=receipt;var status=Pulse();bool handled=receipt is MoveResult.Moved or MoveResult.PathGenerated or MoveResult.UnstuckAttempt or MoveResult.ReachedDestination;Check(moves.Count==1&&status==(handled?RunStatus.Success:RunStatus.Failure),"navigation failure hidden or fallback dispatched twice");});
   }
   if(family<2){
    At("selected GUID changes",()=>target.Guid=9);
    At("selected entry changes",()=>target.Entry=70002);
    At("selected target is removed",()=>ObjectManager.Objects!.Clear());
    At("selected target becomes invalid",()=>target.IsValid=false);
    At("selected target leaves collection radius",()=>target.Location=new WoWPoint(500,10,10));
    At("selected target acquires a forbidden aura",()=>target.Auras.Add("Forbidden"));
    At("target destination becomes NaN",()=>target.Location=new WoWPoint(float.NaN,10,10));
    Add("new nearer target does not replace the selected destination",()=>{var expected=target.Location;callback=()=>ObjectManager.Objects!.Add(new WoWUnit{Guid=3,Entry=70001,Location=new WoWPoint(20,10,10)});Pulse();Check(moves.Count==1&&moves[0].destination==expected,"new candidate stole an existing movement request");});
    Add("unrelated displayed combat target change does not redirect quest approach",()=>{var expected=target.Location;callback=()=>player.CurrentTarget=new WoWUnit{Guid=8};Pulse();Check(moves.Count==1&&moves[0].destination==expected,"explicit quest recipient was bound to unrelated display selection");});
    Add("no-navigation profile defers locally",()=>{Set("NavigationState",Script.NavigationType.None);Pulse();Check(moves.Count==0&&owner.IsDone,"explicit no-navigation mode was ignored");});
    Add("unknown target destination is not sent to navigation",()=>{target.Location=WoWPoint.Zero;Pulse();Check(moves.Count==0,"zero sentinel navigated");});
   }else{
    At("profile destination changes during status",()=>Set("Location",new WoWPoint(600,10,10)));
   }
  }
  int passed=0,assertions=0,unexpected=0;
  foreach(var test in cases){try{test.Item2();passed++;Console.WriteLine("PASS quest item movement: "+test.Item1);}catch(Failure error){assertions++;Console.Error.WriteLine("FAIL quest item movement: "+test.Item1+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR quest item movement: "+test.Item1+": "+error);}}
  Console.WriteLine($"Quest item movement ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked owner/real TreeSharp; controlled navigation; no physical route acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Quest item movement regressions.");
 }
}
""";
}
