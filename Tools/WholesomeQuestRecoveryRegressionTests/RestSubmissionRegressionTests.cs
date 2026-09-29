using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked immediate-use/admission owners. Only actor observations,
// inventory lookup, clock, logging and the native item-use leaf are controlled.
internal static class RestSubmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/Logic/Common/Rest.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x => x.Identifier.ValueText == "Rest");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText is "UseImmediate" or "CanUseConsumables").ToArray();
        if (methods.Length != 2) throw new InvalidOperationException("Both complete rest owners are required.");
        string source = Prefix + "\npublic static class CoreRest {\n" +
            "private static Timer _drinkTimer=new(),_feedTimer=new();public static bool NoFood,NoDrink;\n" +
            "public static void Reset(){_drinkTimer=new();_feedTimer=new();NoFood=NoDrink=false;}\n" +
            "public static Timer Timer(bool drink)=>drink?_drinkTimer:_feedTimer;\n" +
            "public static bool Invoke(bool drink)=>typeof(CoreRest).GetMethod(\"UseImmediate\",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{drink}) is true;\n" +
            string.Join("\n", methods.Select(x => x.ToString())) + "}\n" + Cases;
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("References missing.");
        var compilation = CSharpCompilation.Create("PostMergeRest_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(output.ToArray()).GetType("RestCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private const string Prefix = """
#nullable enable
using System;using System.Collections.Generic;using System.Reflection;
public sealed class Timer{public bool IsFinished=true;public int Resets;public void Reset(){IsFinished=false;Resets++;}}
public sealed class LocalPlayer{public uint BaseAddress=100;public bool IsValid=true,IsAlive=true,IsGhost,Combat,Mounted,IsOnTransport,IsMoving,IsCasting,IsChanneling;}
public static class ObjectManager{public static LocalPlayer? Me;public static object? Wow;}
public static class LiquidEnvironment{public static bool IsPlayerInLiquid(LocalPlayer p)=>World.Wet;}
public sealed class WoWItem{public string Name="current consumable";public bool Use(){World.Uses++;World.AfterUse?.Invoke();return World.AcceptUse;}}
public static class Consumable{
 public static WoWItem? GetBestDrink(bool specialty){World.Lookups++;World.AfterLookup?.Invoke();return World.HasDrink?new WoWItem():null;}
 public static WoWItem? GetBestFood(bool specialty){World.Lookups++;World.AfterLookup?.Invoke();return World.HasFood?new WoWItem():null;}}
public static class Logging{public static void Write(string text,params object[] args){World.AfterLog?.Invoke();}public static void WriteDebug(string text,params object[] args){}}
public static class World{
 public static bool Wet,HasFood,HasDrink,AcceptUse;public static int Uses,Lookups;public static Action? AfterLookup,AfterLog,AfterUse;
 public static void Reset(){ObjectManager.Me=new();ObjectManager.Wow=new();Wet=false;HasFood=HasDrink=AcceptUse=true;Uses=Lookups=0;AfterLookup=AfterLog=AfterUse=null;CoreRest.Reset();}}
""";
    private const string Cases = """
public static class RestCases{
 static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 public static void Run(){int passed=0;var errors=new List<string>();
 void Case(string name,Action test){World.Reset();try{test();passed++;Console.WriteLine("PASS rest submission: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL rest submission: "+errors[^1]);}}
 foreach(bool drink in new[]{false,true}){
  bool d=drink;string kind=d?"drink/":"food/";
  Case(kind+"accepted native request has a receipt",()=>Check(CoreRest.Invoke(d)&&World.Uses==1,"successful submission was indistinguishable from rejection"));
  Case(kind+"declined request has no receipt",()=>{World.AcceptUse=false;Check(!CoreRest.Invoke(d)&&World.Uses==1,"failed use claimed consumption");});
  Case(kind+"current inventory clears a stale absence flag",()=>{CoreRest.NoFood=CoreRest.NoDrink=true;CoreRest.Invoke(d);Check(!(d?CoreRest.NoDrink:CoreRest.NoFood),"stale absence survived current consumable discovery");});
  Case(kind+"empty inventory retains bounded legacy absence",()=>{World.HasFood=World.HasDrink=false;Check(!CoreRest.Invoke(d)&&World.Uses==0&&(d?CoreRest.NoDrink:CoreRest.NoFood)&&CoreRest.Timer(d).Resets==1,"missing item admission changed");});
  Case(kind+"hydration after an empty observation can recover",()=>{World.HasFood=World.HasDrink=false;CoreRest.Invoke(d);CoreRest.Timer(d).IsFinished=true;World.HasFood=World.HasDrink=true;Check(CoreRest.Invoke(d)&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"empty observation poisoned the next attempt");});
  foreach(string state in new[]{"moving","mounted","combat","dead","ghost","transport","casting","channeling","wet","missing-player"}){
   string s=state;Case(kind+"denied/"+s,()=>{var p=ObjectManager.Me!;switch(s){case "moving":p.IsMoving=true;break;case "mounted":p.Mounted=true;break;case "combat":p.Combat=true;break;case "dead":p.IsAlive=false;break;case "ghost":p.IsGhost=true;break;case "transport":p.IsOnTransport=true;break;case "casting":p.IsCasting=true;break;case "channeling":p.IsChanneling=true;break;case "wet":World.Wet=true;break;case "missing-player":ObjectManager.Me=null;break;}Check(!CoreRest.Invoke(d)&&World.Uses==0&&CoreRest.Timer(d).Resets==0&&!(d?CoreRest.NoDrink:CoreRest.NoFood),"ineligible actor spent retry budget, used an item, or claimed absence");});}
  Case(kind+"throttle prevents duplicate request",()=>{CoreRest.Timer(d).IsFinished=false;Check(!CoreRest.Invoke(d)&&World.Uses==0&&World.Lookups==0,"throttle was bypassed");});
  Case(kind+"replacement during lookup has no use",()=>{World.AfterLookup=()=>ObjectManager.Me=new();Check(!CoreRest.Invoke(d)&&World.Uses==0,"lookup handed use to a replacement actor");});
  Case(kind+"replacement during log has no use",()=>{World.AfterLog=()=>ObjectManager.Me=new();Check(!CoreRest.Invoke(d)&&World.Uses==0,"logging handed use to a replacement actor");});
  Case(kind+"memory replacement during use revokes receipt",()=>{World.AfterUse=()=>ObjectManager.Wow=new();Check(!CoreRest.Invoke(d)&&World.Uses==1,"receipt survived a world replacement");});
 }
 Console.WriteLine($"Rest submission scenarios: {passed}/{passed+errors.Count}; complete tracked owners; controlled item dispatch; no server consumption acknowledgement.");
 if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));
 }
}
""";
}
