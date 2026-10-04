using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class GroundObjectTravelCostRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root!.FullName, "Styx/Logic/Pathing/Flightor.cs"))).GetRoot();
        string methods = string.Join("\n", tree.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText is "PreferFlightForGroundInteraction" or "IsFlightTravelCheaper"));
        // Reuse only controlled observation leaves, not another fixture's cost
        // policy. Compile the complete current production estimator below.
        var boundarySyntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
            "Tools/WholesomeQuestRecoveryRegressionTests/MountTimeCostRegressionTests.cs"))).GetRoot();
        var boundaryValue = boundarySyntax.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(v => v.Identifier.ValueText == "Boundary").Initializer!.Value as LiteralExpressionSyntax
            ?? throw new InvalidOperationException("Explicit cost-test external boundary required");
        string text = boundaryValue.Token.ValueText + "namespace Styx.Logic.Pathing {public static class Flightor { public static bool CanFly=>global::World.Flight;" +
            "public static class MountHelper {public static WoWSpell FlyingMount=>global::World.Spell;public static bool Mounted=>global::World.Actor.Mounted;}" + methods + "}}\n" + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("GroundTravel_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(text),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/Logic/Pathing/TravelTimeEstimator.cs"))) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        Assembly.Load(stream.ToArray()).GetType("Cases")!.GetMethod("Run")!.Invoke(null, null);
    }
    private const string Boundary = """
using System;
public readonly record struct WoWPoint(float X,float Y,float Z){public float Distance(WoWPoint p)=>MathF.Sqrt((X-p.X)*(X-p.X)+(Y-p.Y)*(Y-p.Y)+(Z-p.Z)*(Z-p.Z));}
public sealed class Spell{public int CastTime=3000;}
public sealed class Movement {public float RunSpeed=7;}
public sealed class Actor {public WoWPoint Location;public Movement MovementInfo=new();}
public static class StyxWoW {public static Actor Me=new();}
public static class Navigator {public static bool IsInNoFlyZone,IsRidingElevator;public static WoWPoint[] Route=Array.Empty<WoWPoint>();public static int Queries;public static WoWPoint[] GeneratePath(WoWPoint from,WoWPoint to){Queries++;return Route;}}
""";
    private const string Cases = """
public static class Cases{
 public static void Run(){int passed=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);passed++;}
 World.Reset();World.Flight=true;World.Actor.Location=new WoWPoint(0,0,0);World.Spell.SpellEffects=new[]{new SpellEffect{AuraType=(WoWApplyAuraType)207,BasePoints=279,DieSides=1}};
 var far=new WoWPoint(200,0,0);var near=new WoWPoint(10,0,0);var mid=new WoWPoint(40,0,0);
 Check(Flightor.PreferFlightForGroundInteraction(far,5),"long eligible flight was withheld");
 World.Flight=false;int queries=World.Queries;Check(!Flightor.PreferFlightForGroundInteraction(far,5)&&World.Queries==queries,"no flight capability bypassed");World.Flight=true;
 Navigator.IsInNoFlyZone=true;Check(!Flightor.PreferFlightForGroundInteraction(far,5),"no-fly boundary bypassed");Navigator.IsInNoFlyZone=false;
 Navigator.IsRidingElevator=true;Check(!Flightor.PreferFlightForGroundInteraction(far,5),"elevator boundary bypassed");Navigator.IsRidingElevator=false;
 Check(!Flightor.PreferFlightForGroundInteraction(near,5),"nearby target remounted");
 Check(!Flightor.PreferFlightForGroundInteraction(mid,5),"mount/climb/landing overhead omitted");
 World.Route=new[]{World.Actor.Location,new WoWPoint(0,200,0),new WoWPoint(40,200,0),mid};BotPoi.CurrentWorkGeneration++;Check(Flightor.PreferFlightForGroundInteraction(mid,5),"long winding ground route was measured as straight-line separation");
 World.Route=new[]{new WoWPoint(0,200,0)};BotPoi.CurrentWorkGeneration++;Check(!Flightor.PreferFlightForGroundInteraction(mid,5),"partial ground route supplied a full route cost");
 StyxWoW.Me.MovementInfo.RunSpeed=0;Check(!Flightor.PreferFlightForGroundInteraction(far,5),"unknown speed supplied permission");StyxWoW.Me.MovementInfo.RunSpeed=7;
 World.Spell=null;Check(!Flightor.PreferFlightForGroundInteraction(far,5),"unavailable mount supplied permission");
 Check(!Flightor.IsFlightTravelCheaper(100,99,7,26.6,3),"shorter-than-direct path accepted");
  Check(!Flightor.IsFlightTravelCheaper(double.NaN,300,7,26.6,3),"nonfinite route accepted");
 World.Reset();World.Flight=true;World.Actor.Location=new WoWPoint(10,10,10);World.Spell.SpellEffects=new[]{new SpellEffect{AuraType=(WoWApplyAuraType)207,BasePoints=159,DieSides=1}};
 bool moved=false;World.During=()=>{moved=true;World.Actor.Location=new WoWPoint(11,10,10);};
 Check(Flightor.PreferFlightForGroundInteraction(new WoWPoint(800,10,10),3)&&moved,
   "ordinary motion invalidated every flight-cost review despite a favorable current-distance lower bound");
 World.Reset();World.Flight=true;World.Spell.SpellEffects=new[]{new SpellEffect{AuraType=(WoWApplyAuraType)207,BasePoints=159,DieSides=1}};
 World.During=()=>World.Actor=new();
 Check(!Flightor.PreferFlightForGroundInteraction(new WoWPoint(800,10,10),3),"moving-cost tolerance admitted a replaced actor");
 Console.WriteLine($"Ground object travel cost: {passed}/14; actual decision methods, controlled capability/geometry; no flight-route proof.");
 }
}
""";
}
