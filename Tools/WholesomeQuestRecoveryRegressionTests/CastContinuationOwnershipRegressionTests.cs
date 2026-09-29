using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Retain every sight case and execute the exact contiguous Cast/Buff region.
// Add actor/recipient changes at selection, setup, logging and host admission.
// Host observations/effects remain controlled; this is not native acceptance.
internal static class CastContinuationOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        string source = File.ReadAllText(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"));
        int first = source.IndexOf("        #region Cast - by name", StringComparison.Ordinal);
        int end = source.IndexOf("        #region Heal - by name", StringComparison.Ordinal);
        if (first < 0 || end <= first) throw new InvalidOperationException("Actual Cast/Buff region changed.");
        string boundary = (string)typeof(SpellSightDispatchRegressionTests).GetField("Boundary", flags)!.GetRawConstantValue()!;
        static string ReplaceOnce(string text, string old, string replacement)
        {
            int at = text.IndexOf(old, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(old, at + old.Length, StringComparison.Ordinal) >= 0) throw new InvalidOperationException("Boundary anchor changed: " + old);
            return text.Remove(at, old.Length).Insert(at, replacement);
        }
        boundary = ReplaceOnce(boundary, "        int passed=0,assertions=0,unexpected=0;", AdditionalCases + "        int passed=0,assertions=0,unexpected=0;");
        boundary = ReplaceOnce(boundary, "DuringSetup, DuringLog;", "DuringSetup, DuringLog, DuringAdmission, DuringSubmit;");
        boundary = ReplaceOnce(boundary, "DuringSetup=null;DuringLog=null;", "DuringSetup=null;DuringLog=null;DuringAdmission=null;DuringSubmit=null;");
        boundary = ReplaceOnce(boundary, "=>SightCases.Ready&&target!=null&&Spells.TryGetValue(name,out var s)&&(!range||target.IsMe||(target.InLineOfSpellSight&&target.Distance>=s.MinRange&&target.Distance<=s.MaxRange));",
            "{var callback=SightCases.DuringAdmission;SightCases.DuringAdmission=null;callback?.Invoke();return SightCases.Ready&&target!=null&&Spells.TryGetValue(name,out var s)&&(!range||target.IsMe||(target.InLineOfSpellSight&&target.Distance>=s.MinRange&&target.Distance<=s.MaxRange));}");
        boundary = ReplaceOnce(boundary, "SightCases.Targets.Add(target.Guid);return true;", "SightCases.Targets.Add(target.Guid);var callback=SightCases.DuringSubmit;SightCases.DuringSubmit=null;callback?.Invoke();return true;");
        boundary = boundary.Replace("Spell sight dispatch scenarios:", "Cast continuation ownership scenarios:");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-cast-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            // Keep the embedded class spelling separate, as in the retained fixtures,
            // so the normalizer recognizes only this outer module initializer.
            string prefix = "using System;using System.Collections.Generic;using System.Linq;using CommonBehaviors.Actions;using Styx;using Styx.Logic.Combat;using Styx.WoWInternals.WoWObjects;using TreeSharp;using Action=TreeSharp.Action;namespace Singular.Helpers {public delegate WoWUnit UnitSelectionDelegate(object c);public delegate bool SimpleBooleanDelegate(object c);internal static " + "class Spell {private const float MeleeRange=5;\n";
            File.WriteAllText(Path.Combine(temporary, "Owners.cs"), prefix + source.Substring(first, end - first) + "}}");
            File.WriteAllText(Path.Combine(temporary, "Boundary.cs"), boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Exact cast owner compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("SightCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temporary, true); }
    }
    private const string AdditionalCases = """
        foreach(bool byId in new[]{false,true})
        {
            bool useId=byId;string label=useId?"ID":"name";
            void Add(string name,System.Action body)=>cases.Add(("owned "+label+": "+name,()=>{Reset();body();}));
            Composite For(UnitSelectionDelegate select,SimpleBooleanDelegate requires=null)=>useId?Spell.Cast(101,select,requires??(_=>true)):Spell.Cast("Test",_=>false,select,requires??(_=>true));
            foreach(string stage in new[]{"selector","setup","logging","admission"})
            foreach(string mode in new[]{"actor-reference","actor-guid","actor-zero","actor-invalid","actor-dead","recipient-guid","recipient-zero","recipient-invalid"})
            {
                string at=stage,change=mode;
                // Before the selector returns there is no owned recipient identity.
                // A newly selected valid GUID is an ordinary fresh selection.
                if(at=="selector"&&change=="recipient-guid")continue;
                Add(at+" revokes "+change,()=>
                {
                    var player=StyxWoW.Me;var selected=Target;
                    System.Action mutate=()=>{switch(change){
                        case "actor-reference":StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=selected};break;
                        case "actor-guid":player.Guid=9;break;
                        case "actor-zero":player.Guid=0;break;
                        case "actor-invalid":player.IsValid=false;break;
                        case "actor-dead":player.IsAlive=false;break;
                        case "recipient-guid":selected.Guid=9;break;
                        case "recipient-zero":selected.Guid=0;break;
                        case "recipient-invalid":selected.IsValid=false;break;
                    }};
                    UnitSelectionDelegate selector=_=>selected;
                    if(at=="selector")selector=_=>{mutate();return selected;};
                    else if(at=="setup"){Setup=true;DuringSetup=mutate;}
                    else if(at=="logging")DuringLog=mutate;
                    else DuringAdmission=mutate;
                    Tick(For(selector));Expect();
                });
            }
            foreach(string stage in new[]{"setup","logging"})
            {
                string at=stage;
                Add(at+" cannot transfer to a replacement selected recipient",()=>
                {
                    System.Action change=()=>StyxWoW.Me.CurrentTarget=new WoWUnit{Guid=3};
                    if(at=="setup"){Setup=true;DuringSetup=change;}else DuringLog=change;
                    Tick(For(_=>StyxWoW.Me.CurrentTarget));Expect();
                });
                Add(at+" retains an explicit recipient after unrelated displayed-target change",()=>
                {
                    var selected=Target;System.Action change=()=>StyxWoW.Me.CurrentTarget=new WoWUnit{Guid=3};
                    if(at=="setup"){Setup=true;DuringSetup=change;}else DuringLog=change;
                    Tick(For(_=>selected));Expect(2);
                });
            }
            Add("late admission cannot borrow another actor",()=>
            {
                var selected=Target;DuringLog=()=>DuringAdmission=()=>StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=selected};
                Tick(For(_=>selected));Expect();
            });
            Add("late requirements cannot change the actor",()=>
            {
                var selected=Target;bool afterLog=false;DuringLog=()=>afterLog=true;
                Tick(For(_=>selected,_=>{if(afterLog)StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=selected};return true;}));Expect();
            });
            Add("dead friendly recipient remains eligible for caller and host admission",()=>
            {
                Target.IsAlive=false;var selected=Target;Tick(For(_=>selected));Expect(2);
            });
            Add("fresh explicit recipient need not equal the displayed target",()=>
            {
                var selected=new WoWUnit{Guid=8};Tick(For(_=>selected));Expect(8);
            });
            Add("replacement actor after local dispatch cannot receive success bookkeeping",()=>
            {
                var selected=Target;int success=0;DuringSubmit=()=>StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=selected};
                Tick(new Sequence(For(_=>selected),new TreeSharp.Action(_=>{success++;return RunStatus.Success;})));
                Expect(2);Check(success==0,"local dispatch receipt borrowed a replacement actor");
            });
            Add("factory restarts after a revoked activation",()=>
            {
                var tree=For(_=>StyxWoW.Me.CurrentTarget);Setup=true;DuringSetup=()=>StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=new WoWUnit{Guid=3}};
                Tick(tree);Expect();Reset();Tick(tree);Expect(2);
            });
        }
        cases.Add(("owned name: movement-check callback cannot replace the actor",()=>
        {
            Reset();var selected=Target;
            Tick(Spell.Cast("Test",_=>{StyxWoW.Me=new WoWUnit{Guid=9,CurrentTarget=selected};return false;},_=>selected,_=>true));Expect();
        }));
""";
}
