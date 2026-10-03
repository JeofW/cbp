using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Join the exact original-memory entry and public latency reader, actual five-
// argument CanCast, actual cooldown comparator, and complete Flightor MoveTo.
// Only input metadata/readiness observations, geometry, and movement transport
// are controlled. This catches the cross-owner dependency hidden by prior suites.
internal static class NetworkFlightIntegrationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
#if !NETWORK_ENTRY_FOCUSED
        RunAll();
#endif
    }

    internal static void RunAll()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout is required");
        string Load(string path) => File.ReadAllText(Path.Combine(root.FullName, path));
        var manager = CSharpSyntaxTree.ParseText(Load("Styx/Logic/Combat/SpellManager.cs")).GetRoot();
        string actual = string.Join("\n", manager.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText == "CanCast" && m.ParameterList.Parameters.Count == 5
            || m.Identifier.ValueText == "IsCooldownReady").Select(m => m.ToString()));
        var assembly = FlightorWaitContinuityRegressionTests.Build(false,
            "public static class SpellManager {" + actual + ManagerBoundary + "}", new[] {
                Load("Styx/WoWInternals/Misc/WoWClient.cs"), Load("Styx/WoWInternals/Misc/NetStats.cs"),
                NetworkClientEntryRegressionTests.Boundary, Cases });
        try { assembly.GetType("NetworkFlightCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string ManagerBoundary = """
 public static bool HasSpell(string name)=>true;
 public static bool CanCast(string name)=>CanCast(name,World.Player);
 public static bool CanCast(string name,WoWUnit target)=>CanCast(new WoWSpell(),target,true,true,true);
 private static bool IsSpellAvailable(WoWSpell spell,uint lag,bool allowLag) {
  NetworkFlightCases.ReadinessReads++;NetworkFlightCases.Lag=lag;NetworkFlightCases.AllowLag=allowLag;
  if(NetworkFlightCases.ReadinessUnknown)throw new Styx.Helpers.ObservationUnavailableException("spell-cooldown","unknown current readiness");
  return IsCooldownReady(NetworkFlightCases.Remaining,lag,allowLag);
 }
 public static bool Cast(string name){World.Record("cast");return true;}
 public static bool Cast(string name,WoWUnit target){if(!ReferenceEquals(target,World.Player))throw new InvalidOperationException("wrong self target");return Cast(name);}
""";

    private const string Cases = """
using System;
public sealed class WoWSpell {
 public string Name="Crusader Aura";
 public bool TryGetCurrentSpellInfo(out uint cast,out bool funnel,out float minimum,out float maximum){cast=0;funnel=false;minimum=maximum=0;return true;}
}
public static class NetworkFlightCases {
 public static int ReadinessReads;public static uint Lag;public static bool AllowLag,ReadinessUnknown;public static double Remaining;
 static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
 public static void Run(){int total=0,passed=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS network to flight: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL network to flight: "+name+": "+e.Message);}}
  foreach(string mode in new[]{"ready","invalid-timing-ready","invalid-timing-cooldown","missing-client-ready","unknown-readiness","present-aura"}){
   string scenario=mode;Case(scenario,()=>{
    FlightWaitCases.Configure("takeoff");NetworkEntryCases.Reset();ReadinessReads=0;Lag=0;AllowLag=ReadinessUnknown=false;Remaining=0;
    var memory=Styx.WoWInternals.ObjectManager.Wow;
    if(scenario.StartsWith("invalid-timing"))BitConverter.GetBytes(5000U).CopyTo(memory.Blocks[0x12340000U+11860],68);
    if(scenario=="invalid-timing-cooldown")Remaining=15;
    if(scenario=="missing-client-ready")memory.Blocks.Remove(0xC79CF4);
    if(scenario=="unknown-readiness")ReadinessUnknown=true;
    if(scenario=="present-aura"){World.ReadId=id=>id==32223;memory.Blocks.Remove(0xC79CF4);}
    Exception escaped=null;try{FlightOwner.MoveTo(new WoWPoint(100,100,50),15);}catch(Exception e){escaped=e;}
    Check(escaped==null&&World.Commands.Exists(c=>c.StartsWith("towards:"))&&World.PathBuilds==1,
     "real network reader/admission chain prevented mounted movement: "+escaped?.Message);
    Check(World.Commands.Exists(c=>c.StartsWith("move-Forward, JumpAscend:")),"takeoff never followed observed mounting");
    bool expectedCast=scenario is "ready" or "invalid-timing-ready" or "missing-client-ready";
    Check(World.Commands.Exists(c=>c.StartsWith("cast:"))==expectedCast,"cooldown/unknown readiness was treated as aura permission");
    if(scenario.StartsWith("invalid-timing")||scenario=="missing-client-ready")
     Check(ReadinessReads==1&&Lag==0&&!AllowLag,"invalid timing did not select strict current readiness");
    if(scenario=="present-aura")Check(ReadinessReads==0&&memory.Reads.Count==0,"existing aura still depended on the client timing reader");
   });
  }
  Console.WriteLine($"Network-to-flight integration: {passed}/{total}; full public reader -> actual CanCast -> full MoveTo; controlled memory, cooldown scalar and transport; not live flight.");
  if(passed!=total)throw new InvalidOperationException("Network-to-flight integration failures");
 }
}
""";
}
