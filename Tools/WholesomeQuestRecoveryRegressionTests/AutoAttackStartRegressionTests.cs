using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual Common auto-attack composition and actual LocalPlayer toggle Lua,
// executed with real TreeSharp and stock Lua5.1. Managed observations and the
// original-client API effects are separate controlled boundaries: a stale
// inactive observation must not turn an already-started attack off.
internal static class AutoAttackStartRegressionTests
{
    private sealed class Failure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string common = Method(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Common.cs")), "CreateAutoAttack");
        string toggle = Method(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/LocalPlayer.cs")), "ToggleAttack");
        string directory = Path.Combine(Path.GetTempPath(), "cb-autoattack-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + toggle + "}\n" +
                "public static class Common {\n" + common + "}\n" + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var compiled = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = compiled.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var probe = assembly.GetType("Probe", true)!;
            var luaApi = assembly.GetType("Lua", true)!;
            var safety = assembly.GetType("GroupCombatSafety", true)!;
            using var lua = new RewardLua51Boundary.StockLua51(root);
            void Case(string name, Action test)
            {
                total++;
                try { test(); passed++; Console.WriteLine("PASS autoattack startup: " + name); }
                catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL autoattack startup: " + name + ": " + e.Message); }
                catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR autoattack startup: " + name + ": " + e); }
                finally { luaApi.GetField("Observe")!.SetValue(null, null); safety.GetField("DuringCheck")!.SetValue(null, null); }
            }
            void Scenario(bool nativeActive, bool observedActive, bool autoShot, int pulses, bool safe = true,
                bool duringSafety = false, bool pet = false, bool includePet = false, bool petMatches = false,
                string finalState = "npc")
            {
                using var session = lua.BeginSession("nativeActive=" + (nativeActive ? "true" : "false") + "\n" + Setup);
                List<string> Execute(string script)
                {
                    var result = session.Execute(script, (uint)Encoding.UTF8.GetByteCount(script));
                    if (result.Load != 0 || result.Call != 0) throw new InvalidOperationException(result.Error);
                    return result.Values;
                }
                int requests = 0;
                Execute("finalState='" + finalState + "'");
                luaApi.GetField("Observe")!.SetValue(null, new Action<string>(script => { requests++; Execute(script); }));
                safety.GetField("Allowed")!.SetValue(null, safe);
                if (duringSafety) safety.GetField("DuringCheck")!.SetValue(null, new Action(() => Execute("nativeActive=true")));
                int[] counts = (int[])probe.GetMethod("Run")!.Invoke(null,
                    new object[] { observedActive, autoShot, pulses, pet, includePet, petMatches })!;
                int expectedRequests = !observedActive && !autoShot && safe ? pulses : 0;
                bool alreadyStarted = nativeActive || duringSafety && expectedRequests > 0;
                var native = Execute("return starts,stops,nativeActive and 1 or 0");
                Check(requests == expectedRequests, "managed startup/pet policy changed its request count");
                bool nativePermitted = finalState == "npc" || finalState == "outdoor-player";
                Check(native.SequenceEqual(new[] { !alreadyStarted && expectedRequests > 0 && nativePermitted ? "1" : "0", "0", alreadyStarted || expectedRequests > 0 && nativePermitted ? "1" : "0" }),
                    "native starts/stops/active=" + string.Join("/", native) + "; stale startup must not toggle an active attack off");
                Check(counts[0] == pulses, "startup swallowed the remaining spell rotation");
                Check(counts[1] == (safe && pet && includePet && !petMatches ? pulses : 0), "pet attack admission changed");
                Check(counts[2] == 0, "owner raised a swallowed exception");
            }
            foreach (bool active in new[] { false, true })
            foreach (bool observed in new[] { false, true })
            foreach (bool autoShot in new[] { false, true })
            foreach (int pulses in new[] { 1, 3 })
            {
                bool a = active, o = observed, shot = autoShot; int n = pulses;
                Case($"native={a} observed={o} autoshot={shot} pulses={n}", () => Scenario(a, o, shot, n));
            }
            foreach (bool active in new[] { false, true })
            foreach (bool includePet in new[] { false, true })
            {
                bool a = active, p = includePet;
                Case($"group refusal active={a} includePet={p}", () => Scenario(a, false, false, 2, safe: false, pet: true, includePet: p));
            }
            Case("attack starts during safety observation", () => Scenario(false, false, false, 1, duringSafety: true));
            Case("repeated late startup remains idempotent", () => Scenario(false, false, false, 3, duringSafety: true));
            Case("different pet target is still assigned", () => Scenario(false, false, false, 1, pet: true, includePet: true));
            Case("matching pet target is left alone", () => Scenario(false, false, false, 1, pet: true, includePet: true, petMatches: true));
            Case("pet inclusion remains optional", () => Scenario(false, false, false, 1, pet: true));
            foreach (string finalState in new[] { "instance-player", "raid-instance-player", "party-player", "raid-player", "target-replaced", "actor-replaced", "outdoor-player" })
                Case("native startup recipient " + finalState, () => Scenario(false, false, false, 1, finalState: finalState));
            Case("legacy explicit toggle retains its own contract", () =>
            {
                using var session = lua.BeginSession("nativeActive=true\n" + Setup);
                luaApi.GetField("Observe")!.SetValue(null, new Action<string>(script =>
                {
                    var r = session.Execute(script, (uint)Encoding.UTF8.GetByteCount(script));
                    Check(r.Load == 0 && r.Call == 0, "legacy toggle script failed");
                }));
                var playerType = assembly.GetType("Player", true)!;
                playerType.GetMethod("ToggleAttack")!.Invoke(Activator.CreateInstance(playerType), null);
                const string readState = "return stops,nativeActive and 1 or 0";
                var result = session.Execute(readState, (uint)Encoding.UTF8.GetByteCount(readState));
                Check(result.Load == 0 && result.Call == 0 && result.Values.SequenceEqual(new[] { "1", "0" }),
                    "legacy ToggleAttack behavior was replaced globally");
            });
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Autoattack startup scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual Common/LocalPlayer Lua and TreeSharp; stock Lua5.1 with controlled native attack state; no game/server/native execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Autoattack startup regression");
    }
    // Copy the method span verbatim, excluding unrelated surrounding region
    // directives in leading trivia. No statement inside the owner is rewritten.
    private static string Method(string source, string name) => CSharpSyntaxTree.ParseText(source).GetRoot()
        .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToString();
    private static void Check(bool ok, string why) { if (!ok) throw new Failure(why); }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
using System; using TreeSharp; using Action=TreeSharp.Action;
public static class Lua { public static System.Action<string> Observe; public static void DoString(string script)=>Observe(script); }
public static class Logging { public static int Exceptions; public static void WriteException(Exception e){Exceptions++;} }
public static class StyxWoW { public static Player Me; }
public class Unit { public Unit CurrentTarget; }
public sealed class Player:Unit {
 public bool IsAutoAttacking;public int AutoRepeatingSpellId;public bool GotAlivePet;public Unit Pet=new Unit();
""";
    private const string Suffix = """
public static class GroupCombatSafety {
 public static bool Allowed=true;public static System.Action DuringCheck;
 public static bool MayAttackCurrentTarget(){var a=DuringCheck;DuringCheck=null;a?.Invoke();return Allowed;}
}
public static class CombatAttackSafety {
 public static bool TryStartAttack(Unit target){
  var type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Logic.Combat.CombatAttackSafety",true);
  var script=(string)type.GetMethod("BuildAttackLua",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)
   .Invoke(null,new object[]{1UL,2UL,0UL,"target","StartAttack()",false});Lua.DoString(script);return true;
 }
}
public static class PetManager { public static int Attacks;public static void CastPetAction(string name){if(name!="Attack")throw new InvalidOperationException("Unexpected pet command");Attacks++;} }
public static class Probe {
 public static int[] Run(bool observed,bool autoShot,int pulses,bool pet,bool includePet,bool petMatches){
  StyxWoW.Me=new Player{IsAutoAttacking=observed,AutoRepeatingSpellId=autoShot?75:0,GotAlivePet=pet,CurrentTarget=new Unit()};
  if(petMatches)StyxWoW.Me.Pet.CurrentTarget=StyxWoW.Me.CurrentTarget;
  int fallback=0;PetManager.Attacks=0;Logging.Exceptions=0;
  var tree=new PrioritySelector(Common.CreateAutoAttack(includePet),new Action(_=>{fallback++;return RunStatus.Success;}));
  for(int i=0;i<pulses;i++){tree.Start(null);try{if(tree.Tick(null)==RunStatus.Running)throw new InvalidOperationException("Unexpected startup wait");}finally{tree.Stop(null);}}
  return new[]{fallback,PetManager.Attacks,Logging.Exceptions};
 }
}
""";
    private const string Setup = """
clicks=0;starts=0;stops=0
-- Controlled API contract from the retained build12340 function bodies.
-- AttackTarget reaches the toggle path; StartAttack first checks live state.
function AttackTarget()
 if nativeActive then stops=stops+1;nativeActive=false
 else starts=starts+1;nativeActive=true end
end
function StartAttack()
 if not nativeActive then AttackTarget() end
end
finalState='npc'
function UnitGUID(unit)
 if unit=='player' then return finalState=='actor-replaced' and '0x0000000000000099' or '0x0000000000000001' end
 if unit=='target' then return finalState=='target-replaced' and '0x0000000000000099' or '0x0000000000000002' end
 if unit=='party1' or unit=='raid2' then return '0x0000000000000002' end
 if unit=='raid1' then return '0x0000000000000001' end
end
function UnitIsPlayer(unit) return finalState~='npc' and finalState~='target-replaced' and finalState~='actor-replaced' end
function UnitCanAttack(from,to) return true end
function IsInInstance() if finalState=='instance-player' then return true,'party' elseif finalState=='raid-instance-player' then return true,'raid' else return false,'none' end end
function GetNumPartyMembers() return finalState=='party-player' and 1 or 0 end
function GetNumRaidMembers() return finalState=='raid-player' and 2 or 0 end
""";
}
