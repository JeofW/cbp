using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked taxi event handler, public entry and existing managed context
// observer. Native node/frame reads, persistence and external effects are controlled.
// This verifies managed admission/continuation, not causal TAXIMAP_OPENED provenance.
internal static class TaxiEventOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/FlightPaths.cs"))).GetRoot();
        string[] names = { "HandleTaxiMapOpened", "TakeFlightPath", "FindNodeByName" };
        var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => names.Contains(method.Identifier.ValueText)).ToArray();
        if (methods.Length != names.Length) throw new InvalidOperationException("Expected exact taxi handler and public entry.");
        var observation = source.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "FlightContextObservation");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-taxi-event-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + observation.ToFullString() +
                string.Join("\n", methods.Select(method => method.ToFullString())) + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual taxi handler compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("TaxiCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private const string Prefix = """
using System;using System.Collections.Generic;using System.Linq;using Styx.Logic.Pathing;
public enum PoiType {None,Fly,Repair,QuestPickUp,Kill}
public enum FlightPathReason {None,Learn,Update,Use}
public sealed class LuaEventArgs:EventArgs{}
public sealed class LocalPlayer {
 public uint BaseAddress=0x10000,MapId=1;public ulong Guid=1;public int Level=80;
 public bool IsValid=true,IsAlive=true;public WoWPoint Location=new WoWPoint(1,2,3);
}
public static class StyxWoW {
 public static LocalPlayer Me;
 public static void SleepForLagDuration(){World.Sleeps++;World.Fire("lag");}
}
public sealed class CharacterSettings {public static CharacterSettings Instance;public bool UseFlightPaths=true,LearnFlightPaths=true;}
public sealed class NavigationProvider{}
public static class Navigator {public static NavigationProvider NavigationProvider=new NavigationProvider();}
public sealed class BotPoi {
 public static BotPoi Current;public PoiType Type=PoiType.Fly;public uint Entry=10;public ulong Guid=100;
 public static void Clear(string reason){World.Clears++;Current=new BotPoi{Type=PoiType.None};World.Fire("clear");}
}
public sealed class XmlFlightNode {
 public string Name;public uint MasterEntry,Continent;public int UpdateLevel;public WoWPoint Location;
 public HashSet<string> Connections=new HashSet<string>();
 public XmlFlightNode(string name,uint map,WoWPoint point){Name=name;Continent=map;Location=point;}
 public XmlFlightNode(uint entry,int level,string name,uint map,WoWPoint point):this(name,map,point){MasterEntry=entry;UpdateLevel=level;}
 public void Connect(string name){Connections.Add(name);}
}
public sealed class NativeNode {public bool IsValid=true;public string Name;public int MapId=1;public WoWPoint Location=new WoWPoint(1,2,3);}
public static class TaxiNodeInfo {
 public static NativeNode GetCurrent(){World.Reads++;World.Fire("current");return World.Current;}
 public static uint GetNodeCount(){World.Fire("count");return (uint)World.Native.Count;}
 public static NativeNode GetByTableIndex(uint i){World.Fire("node");return World.Native[(int)i];}
}
public sealed class TaxiFrame {
 public static TaxiFrame Instance=new TaxiFrame();
 public bool IsVisible=true;
 public List<TaxiFrameNode> Nodes {get{World.Fire("nodes");return World.Frames;}}
 public void Hide(){World.Hides++;World.Fire("hide");}
 public sealed class TaxiFrameNode {
  public string Label;public bool Available=true;
  public string Name {get{if(Label=="Destination")World.Fire("destination-name");return Label;}}
  public bool Reachable {get{World.Fire("reachability");return Available;}}
  public void TakeNode(){World.Takes++;World.Taken=Label;World.Fire("take");}
 }
}
public static class Logging {
 public static void Write(string format,params object[] arguments){
  if(format.StartsWith("TaxiMap opened"))World.Fire("log-open");
  else if(format.StartsWith("Taking flight path"))World.Fire("log-take");
 }
 public static void WriteDebug(string format,params object[] arguments){}
 public static void WriteException(Exception error){World.Errors.Add(error);World.Fire("error-log");}
}
public static class World {
 public static NativeNode Current;public static List<NativeNode> Native;public static List<TaxiFrame.TaxiFrameNode> Frames;
 public static int Reads,Saves,Takes,Hides,Clears,Sleeps;public static string Taken,Stage;public static System.Action Callback;
 public static List<Exception> Errors=new List<Exception>();
 public static void Fire(string stage){if(stage!=Stage)return;Stage=null;var callback=Callback;Callback=null;callback?.Invoke();}
 public static void Reset(){
  Reads=Saves=Takes=Hides=Clears=Sleeps=0;Taken=Stage=null;Callback=null;Errors.Clear();
  StyxWoW.Me=new LocalPlayer();CharacterSettings.Instance=new CharacterSettings();Navigator.NavigationProvider=new NavigationProvider();BotPoi.Current=new BotPoi();
  Current=new NativeNode{Name="Origin"};Native=new List<NativeNode>{Current,new NativeNode{Name="Destination",Location=new WoWPoint(20,30,40)}};
  Frames=new List<TaxiFrame.TaxiFrameNode>{new TaxiFrame.TaxiFrameNode{Label="Origin"},new TaxiFrame.TaxiFrameNode{Label="Destination"}};
  TaxiFrame.Instance=new TaxiFrame();
  FlightProbe.Reason=FlightPathReason.Use;FlightProbe.NeedFlightPath=true;FlightProbe.XmlNodes=new List<XmlFlightNode>();
  FlightProbe.TakingPathFrom=new XmlFlightNode("Origin",1,new WoWPoint(1,2,3));
  FlightProbe.TakingPathTo=new XmlFlightNode("Destination",1,new WoWPoint(20,30,40));FlightProbe.ReplaceIntent();
 }
}
public static class FlightProbe {
 private static object _flightIntentOwner=new object();
 public static FlightPathReason Reason;public static bool NeedFlightPath;
 public static List<XmlFlightNode> XmlNodes;public static XmlFlightNode TakingPathFrom,TakingPathTo;
 public static bool CanTakeFlightPaths=>CharacterSettings.Instance.UseFlightPaths;
 public static void ReplaceIntent(){_flightIntentOwner=new object();}
 public static void InvokeEvent(){HandleTaxiMapOpened(null,null);}
 private static void SaveToXml(){World.Saves++;World.Fire("save");}
""";

    private const string Cases = """
public static class TaxiCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void NoEffects()=>Check(World.Reads==0&&World.Saves==0&&World.Takes==0&&World.Hides==0&&World.Clears==0,"unadmitted event read native state, saved nodes, took a taxi or changed another owner's UI/POI");
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action body){
   total++;World.Reset();
   try{body();passed++;Console.WriteLine("PASS taxi event ownership: "+name);}
   catch(Failure error){assertions++;Console.Error.WriteLine("FAIL taxi event ownership: "+name+": "+error.Message);}
   catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR taxi event ownership: "+name+": "+error);}
  }
  foreach(bool publicEntry in new[]{false,true}){
   bool entry=publicEntry;
   Case("healthy use/"+entry,()=>{if(entry)FlightProbe.TakeFlightPath();else FlightProbe.InvokeEvent();Check(World.Saves==1&&World.Takes==1&&World.Taken=="Destination"&&World.Hides==1&&World.Clears==0&&World.Sleeps==1,"normal use no longer submits once and retains flight intent");});
  }
  foreach(var reason in new[]{FlightPathReason.Learn,FlightPathReason.Update})foreach(bool learnOnly in new[]{false,true}){
   var intent=reason;bool only=learnOnly;
   Case("healthy learning/"+intent+"/"+only,()=>{FlightProbe.Reason=intent;CharacterSettings.Instance.UseFlightPaths=!only;FlightProbe.InvokeEvent();Check(World.Saves==1&&World.Takes==0&&World.Hides==1&&World.Clears==1,"learning should update once, never take a taxi and finish only its own POI");});
  }
  foreach(var type in new[]{PoiType.None,PoiType.Repair,PoiType.QuestPickUp,PoiType.Kill})foreach(var reason in new[]{FlightPathReason.Use,FlightPathReason.Update}){
   var kind=type;var intent=reason;
   Case("unrelated POI/"+kind+"/"+intent,()=>{BotPoi.Current.Type=kind;var retained=BotPoi.Current;FlightProbe.Reason=intent;FlightProbe.InvokeEvent();NoEffects();Check(ReferenceEquals(BotPoi.Current,retained),"unrelated POI was replaced");});
  }
  foreach(var reason in new[]{FlightPathReason.None,(FlightPathReason)987}){
   var intent=reason;Case("no admitted intent/"+intent,()=>{FlightProbe.Reason=intent;FlightProbe.InvokeEvent();NoEffects();});
  }
  Case("disabled use does not borrow learn permission",()=>{CharacterSettings.Instance.UseFlightPaths=false;FlightProbe.InvokeEvent();NoEffects();});
  foreach(var reason in new[]{FlightPathReason.Learn,FlightPathReason.Update}){
   var intent=reason;Case("all settings disabled/"+intent,()=>{FlightProbe.Reason=intent;CharacterSettings.Instance.UseFlightPaths=CharacterSettings.Instance.LearnFlightPaths=false;FlightProbe.InvokeEvent();NoEffects();});
  }
  foreach(string condition in new[]{"missing","invalid","dead","zero-guid"}){
   string state=condition;Case("unavailable actor/"+state,()=>{if(state=="missing")StyxWoW.Me=null;else if(state=="invalid")StyxWoW.Me.IsValid=false;else if(state=="dead")StyxWoW.Me.IsAlive=false;else StyxWoW.Me.Guid=0;FlightProbe.InvokeEvent();NoEffects();});
  }
  foreach(string observation in new[]{"missing","unreachable"}){
   string mode=observation;Case("destination currently "+mode,()=>{if(mode=="missing")World.Frames.RemoveAt(1);else World.Frames[1].Available=false;FlightProbe.InvokeEvent();Check(World.Takes==0&&World.Clears==0,"missing/unreachable current destination acquired a taxi submission");});
  }
  foreach(string stage in new[]{"log-open","current","nodes","reachability","save","log-take","take","lag","hide"})foreach(string mutation in new[]{"service","actor","network","disabled"}){
   string boundary=stage,change=mutation;
   Case(boundary+" cannot consume replaced "+change,()=>{
    if(boundary=="hide")FlightProbe.Reason=FlightPathReason.Update;
    bool fired=false;int saves=0,takes=0,hides=0,clears=0,sleeps=0;BotPoi replacement=null;
    World.Stage=boundary;World.Callback=()=>{
     fired=true;saves=World.Saves;takes=World.Takes;hides=World.Hides;clears=World.Clears;sleeps=World.Sleeps;
     if(change=="service")BotPoi.Current=new BotPoi{Type=PoiType.Repair};
     else if(change=="actor")StyxWoW.Me=new LocalPlayer();
     else if(change=="network")FlightProbe.XmlNodes=new List<XmlFlightNode>();
     else CharacterSettings.Instance.UseFlightPaths=CharacterSettings.Instance.LearnFlightPaths=false;
     replacement=BotPoi.Current;
    };
    FlightProbe.InvokeEvent();
    Check(fired,"controlled callback was not reached");
    Check(World.Saves==saves&&World.Takes==takes&&World.Hides==hides&&World.Clears==clears&&World.Sleeps==sleeps&&ReferenceEquals(BotPoi.Current,replacement),"obsolete handler continued saving/submitting/cleaning after the replacement");
   });
  }
  foreach(string mutation in new[]{"intent-token","poi-type","poi-entry","poi-guid","actor-guid","map","target-name","reason","settings-object"}){
   string change=mutation;Case("logging revokes changed "+change,()=>{
    World.Stage="log-open";World.Callback=()=>{
     if(change=="intent-token")FlightProbe.ReplaceIntent();else if(change=="poi-type")BotPoi.Current.Type=PoiType.Repair;
     else if(change=="poi-entry")BotPoi.Current.Entry=99;else if(change=="poi-guid")BotPoi.Current.Guid=99;
     else if(change=="actor-guid")StyxWoW.Me.Guid=99;else if(change=="map")StyxWoW.Me.MapId=99;
     else if(change=="target-name")FlightProbe.TakingPathTo.Name="Another";else if(change=="reason")FlightProbe.Reason=FlightPathReason.Update;
     else CharacterSettings.Instance=new CharacterSettings();
    };
    FlightProbe.InvokeEvent();Check(World.Reads==0&&World.Saves==0&&World.Takes==0&&World.Hides==0&&World.Clears==0,"changed managed context acquired native work or cleanup");
   });
  }
  Case("current-node read failure preserves the admitted error behavior",()=>{World.Current=null;FlightProbe.Reason=FlightPathReason.Update;FlightProbe.InvokeEvent();Check(World.Takes==0&&World.Saves==0&&World.Hides==1&&World.Clears==1,"unavailable-node legacy disposition changed");});
  Case("later independent event remains usable after revocation",()=>{World.Stage="log-open";World.Callback=()=>BotPoi.Current=new BotPoi{Type=PoiType.Repair};FlightProbe.InvokeEvent();World.Reset();FlightProbe.InvokeEvent();Check(World.Takes==1&&World.Hides==1,"revoked prior event poisoned a later admitted event");});
  Console.WriteLine($"Taxi event ownership scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual handler/public entry/context observer; controlled nodes/frame/persistence; no native menu ownership or live taxi acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Taxi event ownership regression failures.");
 }
}
""";
}
