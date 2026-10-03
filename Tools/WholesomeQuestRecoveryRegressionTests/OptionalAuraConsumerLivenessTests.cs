using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Exact death-wait predicate and complete PVP consumer; the aura/memory leaf
// distinguishes missing metadata from genuinely unreadable raw coverage.
internal static class OptionalAuraConsumerLivenessTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "CopilotBuddy.csproj"))) path = path.Parent;
        string root = path?.FullName ?? throw new InvalidOperationException("Checkout required");
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Bots/Grind/LevelBot.cs"))).GetRoot();
        var death = source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "CreateDeathBehavior");
        var predicate = death.DescendantNodes().OfType<LambdaExpressionSyntax>().Single(l =>
            l.Body is ExpressionSyntax && l.ToString().Contains("RessAtSpiritHealers") && l.ToString().Contains("HasAura"));
        string temp = Path.Combine(Path.GetTempPath(), "cb-optional-aura-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.Copy(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/PVP.cs"), Path.Combine(temp, "PVP.cs"));
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), Boundary + "\npublic static class DeathProbe { static bool _waitingForHealerRecovery; static DateTime _healerResurrectedUtc; public static bool Wait()=>((Func<object,bool>)(" + predicate + "))(null); }\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("OptionalAuraCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temp, true); }
    }
    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using Styx;using Styx.Helpers;using Styx.Logic.Combat;using Styx.WoWInternals.WoWObjects;
/* Controlled external boundary. */ namespace Styx {public static class StyxWoW {public static WoWUnit Me=new();}}
/* Controlled external boundary. */ namespace Styx.Helpers {public class CharacterSettings {public static CharacterSettings Instance=new();public bool RessAtSpiritHealers=true;}}
/* Controlled external boundary. */ namespace Styx.WoWInternals.WoWObjects {
 public class Aura {public bool IsHarmful,Missing;public int Id;public Spell Value=new();public Spell Spell=>Missing?throw new Styx.Helpers.ObservationUnavailableException("auras","missing metadata"):Value;}
 public class Spell {public WoWSpellMechanic Mechanic;}
 public class WoWUnit {public bool IsAlive=true,IsGhost,Combat,RawUnknown;public List<Aura> Auras=new();
  public IEnumerable<Aura> GetRawAuras()=>RawUnknown?throw new Styx.Helpers.ObservationUnavailableException("auras","raw unavailable"):Auras;
  public IEnumerable<Aura> GetAllAuras(){var rows=GetRawAuras().ToArray();foreach(var row in rows)_=row.Spell;return rows;}
  public bool HasAura(int id)=>GetRawAuras().Any(a=>a.Id==id);
  public bool HasAura(string name)=>GetAllAuras().Any(a=>a.Id==15007);
  public bool HasAuraWithMechanic(params WoWSpellMechanic[] kinds)=>GetAllAuras().Any(a=>kinds.Contains(a.Spell.Mechanic));
 }
}
""";
    private const string Cases = """
public static class OptionalAuraCases {
 static void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
 public static void Run(){int total=0,passed=0;
  void Case(string name,Action body){total++;StyxWoW.Me=new();CharacterSettings.Instance=new();try{body();passed++;Console.WriteLine("PASS optional aura consumer: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL optional aura consumer: "+name+": "+e.Message);}}
  void Unknown(Action body){bool denied=false;try{body();}catch(ObservationUnavailableException){denied=true;}Check(denied,"unreadable relevant coverage was treated as absent");}
  Case("unrelated helpful metadata cannot suspend death wait predicate",()=>{StyxWoW.Me.Auras.Add(new(){Id=61988,Missing=true});Check(!DeathProbe.Wait(),"unrelated marker triggered sickness wait");});
  Case("exact resurrection sickness still waits",()=>{StyxWoW.Me.Auras.Add(new(){Id=15007,Missing=true});Check(DeathProbe.Wait(),"actual sickness lost its wait");});
  Case("unreadable raw sickness coverage remains unavailable",()=>{StyxWoW.Me.RawUnknown=true;Unknown(()=>DeathProbe.Wait());});
  foreach(bool silence in new[]{false,true}){
   bool Read()=>silence?Singular.Helpers.PVP.IsSilenced(StyxWoW.Me):Singular.Helpers.PVP.IsCrowdControlled(StyxWoW.Me);
   Case("helpful metadata is outside harmful mechanic mask/"+silence,()=>{StyxWoW.Me.Auras.Add(new(){Missing=true});Check(!Read(),"helpful aura was classified as harmful control");});
   Case("known harmful mechanics survive unrelated helpful metadata/"+silence,()=>{StyxWoW.Me.Auras.Add(new(){Missing=true});StyxWoW.Me.Auras.Add(new(){IsHarmful=true,Value=new(){Mechanic=silence?WoWSpellMechanic.Silenced:WoWSpellMechanic.Stunned}});Check(Read(),"known harmful control was lost");});
   Case("unknown harmful mechanics still defer/"+silence,()=>{StyxWoW.Me.Auras.Add(new(){IsHarmful=true,Missing=true});Unknown(()=>Read());});
  }
  Console.WriteLine($"Optional aura consumers: {passed}/{total}; exact production death predicate and full PVP readers; controlled raw metadata, no game.");if(passed!=total)throw new InvalidOperationException("optional aura consumer failures");
 }
}
""";
}
