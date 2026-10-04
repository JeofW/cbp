using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual publication, accessors and protected-player policy. Include/weigh and
// post-publication callbacks are real delegates invoked by the production Pulse.
internal static class TargetPublicationProtectionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = FindRoot();
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Targeting.cs"))).GetRoot();
        var propertyNames = new HashSet<string> { "FirstUnit", "TargetList", "ObjectList", "SelectsCombatTargets" };
        string members = string.Join("\n", source.DescendantNodes().Where(n =>
            n is PropertyDeclarationSyntax p && propertyNames.Contains(p.Identifier.ValueText)
            || n is MethodDeclarationSyntax m && m.Identifier.ValueText is "Pulse" or "MarkObservationUnavailable" or "IsAllowedCombatCandidate")
            .Select(n => n.ToString()));
        var boundary = CSharpSyntaxTree.ParseText(CombatRecipientDispatchTests.Boundary).GetCompilationUnitRoot();
        var oldGroup = boundary.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "GroupCombatSafety");
        boundary = boundary.RemoveNode(oldGroup, SyntaxRemoveOptions.KeepNoTrivia)!;
        var group = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/Logic/Combat/GroupCombatSafety.cs"))).GetRoot();
        string policy = string.Join("\n", group.DescendantNodes().Where(n =>
            n is PropertyDeclarationSyntax p && p.Identifier.ValueText == "IsInInstance"
            || n is MethodDeclarationSyntax m && m.Identifier.ValueText == "IsProtectedPlayer").Select(n => n.ToString()));
        string support = "";
        if (source.DescendantNodes().OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.ValueText == "SelectsCombatTargets"))
        {
            foreach ((string name, string path) in new[] { ("Healing", "Styx/CommonBot/HealTargeting.cs"), ("Looting", "Styx/Logic/LootTargeting.cs") })
            {
                var property = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot()
                    .DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "SelectsCombatTargets");
                support += "public sealed class " + name + " : Targeting {" + property.ToString() + "}\n";
            }
        }
        else support = "public sealed class Healing:Targeting {} public sealed class Looting:Targeting {}";
        string text = boundary.ToFullString() + "public sealed class ObservationUnavailableException : Exception {public ObservationUnavailableException(string kind,string text):base(text){} public static ObservationUnavailableException Find(Exception e)=>e as ObservationUnavailableException;}"
            + "public static class ObservationFailureDiagnostics {public static void Report(ObservationUnavailableException e,string source){}}"
            + "public static class GroupCombatSafety {" + policy + "}"
            + "public class Targeting {" + Leaves + members + "}" + support + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Styx.StyxWoW).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compile = CSharpCompilation.Create("TargetPublication_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(text) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compile.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("TargetCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Leaves = """
 private List<WoWObject> _objectList=new();private string _observationFailure;
 private static Converter<WoWObject,WoWUnit> _objectToUnitConverter;
 private static Func<WoWObject,TargetPriority> _targetPrioritySelector;
 private static Func<TargetPriority,double> _getScoreFunc;private static Func<TargetPriority,WoWObject> _targetToObjectSelector;
 private static List<string> _blacklistedMobNames=new();
 private Delegate _removeTargetsFilterHandlers,_includeTargetsFilterHandlers,_weighTargetsFilterHandlers;
 private Action<List<string>> _targetListUpdateFinishedHandlers;
 public int MaxTargets=5;public bool DisplayTargetingExceptions=false;
 public sealed class TargetPriority {public WoWObject Object;public double Score;}
 public Targeting(){_includeTargetsFilterHandlers=new Action<List<WoWObject>,HashSet<WoWObject>>((input,output)=>{foreach(var unit in input)output.Add(unit);});}
 public void Set(params WoWObject[] objects){ObjectList=objects.ToList();}
 public void AddDuringInclude(WoWObject unit)=>_includeTargetsFilterHandlers=new Action<List<WoWObject>,HashSet<WoWObject>>((input,output)=>{foreach(var item in input)output.Add(item);output.Add(unit);});
 public void Weigh(Action<List<TargetPriority>> callback)=>_weighTargetsFilterHandlers=callback;
 public void After(Action callback)=>_targetListUpdateFinishedHandlers=_=>callback();
 private List<WoWObject> GetInitialObjectList()=>new(){World.Target};
 private void InvokeFilterDelegate(Delegate callback,object[] args)=>callback?.DynamicInvoke(args);
 private static WoWUnit ToWoWUnit(WoWObject value)=>value as WoWUnit;
 private static TargetPriority CreateTargetPriority(WoWObject value)=>new(){Object=value,Score=10};
 private static double GetScore(TargetPriority value)=>value.Score;
 private static WoWObject GetObject(TargetPriority value)=>value.Object;
""";
    private const string Cases = """
public static class TargetCases {
 private sealed class Failure(string text):Exception(text){}
 private static void Check(bool value,string why){if(!value)throw new Failure(why);}
 public static void Run(){int passed=0,total=0;var failures=new List<string>();
  void Case(string name,Action test){total++;World.Reset();try{test();passed++;Console.WriteLine("PASS target protection: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL target protection: "+name+": "+e.Message);}}
  foreach(bool raid in new[]{false,true})foreach(bool friendly in new[]{false,true})Case("retained instance player/"+raid+"/"+friendly,()=>{
   StyxWoW.Me.CurrentMap.IsDungeon=!raid;StyxWoW.Me.CurrentMap.IsRaid=raid;World.Member.Friendly=friendly;
   var targeting=new Targeting();targeting.Set(World.Member,World.Target);Check(ReferenceEquals(targeting.FirstUnit,World.Target)&&targeting.TargetList.SequenceEqual(new[]{World.Target}),"retained hostile player displaced the NPC");});
  Case("current outdoor party member is never retained",()=>{StyxWoW.Me.CurrentMap.IsDungeon=false;var t=new Targeting();t.Set(World.Member);Check(t.FirstUnit==null&&t.TargetList.Count==0,"party membership lost to changed hostility");});
  Case("ordinary outdoor player remains selectable",()=>{StyxWoW.Me.CurrentMap.IsDungeon=false;StyxWoW.Me.PartyMemberGuids.Clear();StyxWoW.Me.IsInParty=false;var t=new Targeting();t.Set(World.Member);Check(ReferenceEquals(t.FirstUnit,World.Member),"outdoor PvP was disabled");});
  Case("include callback cannot reinsert a controlled player",()=>{var t=new Targeting();t.AddDuringInclude(World.Member);t.Pulse();Check(t.TargetList.SequenceEqual(new[]{World.Target}),"include callback bypassed protected-player publication");});
  Case("weight callback cannot crowd the actual NPC out of the target limit",()=>{var t=new Targeting{MaxTargets=1};t.Weigh(list=>list.Add(new(){Object=World.Member,Score=10000}));t.Pulse();Check(ReferenceEquals(t.FirstUnit,World.Target),"protected player consumed the retained target limit");});
  foreach(string change in new[]{"type","descriptor","dead","invalid"})Case("post-publication candidate revocation/"+change,()=>{
   var t=new Targeting();t.After(()=>{switch(change){case "type":World.Target.Type=4;break;case "descriptor":World.Target.DescriptorGuid=40;break;case "dead":World.Target.IsAlive=false;break;case "invalid":World.Target.IsValid=false;break;}});t.Pulse();
   Check(t.FirstUnit==null&&t.TargetList.Count==0,"publication callback changed eligibility after the last filter");});
  foreach(bool healing in new[]{false,true})Case("support target lists retain players/"+healing,()=>{
   Targeting t=healing?new Healing():new Looting();t.Set(World.Member);Check(ReferenceEquals(t.FirstUnit,World.Member)&&t.TargetList.Count==1,"combat protection was incorrectly applied to a support target list");});
  Console.WriteLine($"Target publication protection: {passed}/{total}; failures={failures.Count}; actual publication/accessors/group policy, controlled delegate and world boundaries.");
  if(failures.Count>0)throw new InvalidOperationException("target publication protection regressions");
 }
}
""";
}
