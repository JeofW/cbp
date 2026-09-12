using System.CodeDom.Compiler;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Loaders;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using TestAction = System.Action;

internal static class RoutineBoundaryRegressionTests
{
    private static int _predicates, _selections;
    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((ushort)code.Value));

    [ModuleInitializer]
    internal static void Run()
    {
        if (!Environment.GetCommandLineArgs().Contains("--routine-compatibility")) return;
        if (ObjectManager.Me != null || ObjectManager.Wow != null || ObjectManager.Executor != null)
            throw new InvalidOperationException("Routine boundary fixtures require an unattached process.");
        string source = (string)typeof(RoutineCompilationRegression)
            .GetMethod("ResolveSourceDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { AppContext.BaseDirectory })!;
        var compiler = new SourceCompiler(source);
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)) compiler.AddReference(path);
        CompilerResults compiled = compiler.Compile();
        var errors = compiled.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
        if (errors.Length != 0 || compiler.CompiledAssembly == null)
            throw new InvalidOperationException("Routine fixture compilation failed: " + string.Join("; ", errors.Select(error => error.ToString())));
        Assembly assembly = compiler.CompiledAssembly;
        Type spell = assembly.GetType("Singular.Helpers.Spell", throwOnError: true)!;
        Type select = assembly.GetType("Singular.Helpers.UnitSelectionDelegate", throwOnError: true)!;
        Type predicate = assembly.GetType("Singular.Helpers.SimpleBooleanDelegate", throwOnError: true)!;
        Delegate Bind(Type type, string name) => typeof(RoutineBoundaryRegressionTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate(type);
        object? none = Bind(select, nameof(NoTarget));
        object? unit = Bind(select, nameof(Unit));
        object? yes = Bind(predicate, nameof(Requirement));
        object? no = Bind(predicate, nameof(Reject));
        object? movement = Bind(predicate, nameof(Movement));
        Composite CastName(string? name, object? selector, object? requirements, object? check = null) =>
            (Composite)spell.GetMethod("Cast", new[] { typeof(string), predicate, select, predicate })!
                .Invoke(null, new[] { (object?)name, check ?? movement, selector, requirements })!;
        Composite CastId(int id, object? selector, object? requirements) =>
            (Composite)spell.GetMethod("Cast", new[] { typeof(int), select, predicate })!.Invoke(null, new[] { (object)id, selector, requirements })!;
        Composite BuffName(object? selector) =>
            (Composite)spell.GetMethod("Buff", new[] { typeof(string), typeof(bool), select, predicate, typeof(string[]) })!
                .Invoke(null, new[] { (object)"Power Word: Fortitude", false, selector, yes, new[] { "Power Word: Fortitude" } })!;
        Composite BuffId(object? selector) =>
            (Composite)spell.GetMethod("Buff", new[] { typeof(int), select, predicate })!.Invoke(null, new[] { (object)1243, selector, yes })!;
        var tests = new List<(string Name, TestAction Run)>
        {
            ("name cast rejects absent target before dependent requirements", () => { FailQuietly(CastName("Smite",none,yes)); Check(_predicates==0 && _selections==1,"requirements must not run without the selected target"); }),
            ("ID cast rejects absent target before dependent requirements", () => { FailQuietly(CastId(585,none,yes)); Check(_predicates==0 && _selections==1,"ID path must obey the same ownership contract"); }),
            ("name cast rejects missing selector without user callbacks", () => { FailQuietly(CastName("Smite",null,yes)); Check(_predicates==0,"missing selector must fail before requirements"); }),
            ("ID cast rejects missing selector without user callbacks", () => { FailQuietly(CastId(585,null,yes)); Check(_predicates==0,"missing selector must fail before requirements"); }),
            ("invalid spell names do not evaluate target-dependent callbacks", () => { foreach(string? name in new[]{null,""," "}) FailQuietly(CastName(name,unit,yes)); Check(_predicates==0 && _selections==0,"invalid metadata must be rejected before callbacks"); }),
            ("invalid spell IDs do not evaluate target-dependent callbacks", () => { foreach(int id in new[]{0,-1}) FailQuietly(CastId(id,unit,yes)); Check(_predicates==0 && _selections==0,"invalid IDs must be rejected before callbacks"); }),
            ("missing requirements do not trigger target selection", () => { FailQuietly(CastName("Smite",unit,null)); FailQuietly(CastId(585,unit,null)); Check(_selections==0,"invalid predicate configuration must fail before user selection"); }),
            ("false requirements reject without native spellbook access", () => { FailQuietly(CastName("Smite",unit,no)); FailQuietly(CastId(585,unit,no)); Check(_predicates==2,"each valid selected context evaluates requirements once"); }),
            ("name buff tolerates a missing selector without exception logging", () => FailQuietly(BuffName(null))),
            ("ID buff tolerates a missing selector without exception logging", () => FailQuietly(BuffId(null)))
        };
        Type shadow = assembly.GetType("Singular.ClassSpecific.Priest.Shadow", throwOnError: true)!;
        foreach(string factory in new[]{"CreateShadowPriestNormalCombat","CreateShadowPriestPvPPullAndCombat","CreateShadowPriestInstancePullAndCombat"})
        {
            string captured=factory;
            tests.Add(("yielding and cancellation in "+captured, () => VerifyDelay(DelayReferencedBy(shadow,captured))));
        }
        var failures=new List<string>();
        foreach(var test in tests)
        {
            _predicates=0; _selections=0;
            try { test.Run(); Console.WriteLine("PASS routine boundary: "+test.Name); }
            catch(Exception error)
            {
                while(error is TargetInvocationException wrapped && wrapped.InnerException!=null) error=wrapped.InnerException;
                failures.Add(test.Name+": "+error.Message); Console.Error.WriteLine("FAIL routine boundary: "+failures[^1]);
            }
        }
        Console.WriteLine($"Routine boundary scenarios: {tests.Count-failures.Count}/{tests.Count}; actual compiled helper/delay code; no game attached.");
        if(failures.Count!=0) throw new InvalidOperationException(string.Join(Environment.NewLine,failures));
    }

    private static WoWUnit NoTarget(object _) { _selections++; return null!; }
    private static WoWUnit Unit(object _) { _selections++; return new WoWUnit(0); }
    private static bool Requirement(object _) { _predicates++; return true; }
    private static bool Reject(object _) { _predicates++; return false; }
    private static bool Movement(object _) => true;
    private static void FailQuietly(Composite composite)
    {
        var messages=new List<string>(); bool old=Logging.FileLogging; Logging.FileLogging=false;
        Logging.LogMessageDelegate listener=batch => messages.AddRange(batch.Select(message=>message.Message));
        Logging.OnLogMessage+=listener;
        try
        {
            composite.Start(null!);
            Check(composite.Tick(null!)==RunStatus.Failure,"invalid input must yield Failure, not Success/Running");
            Check(!messages.Any(message=>message.Contains("Exception",StringComparison.Ordinal)),"swallowing an exception in Composite.Tick is not a null-safety pass");
        }
        finally { composite.Stop(null!); Logging.OnLogMessage-=listener; Logging.FileLogging=old; }
    }

    // Inspect stable factory IL to find the exact delay component it references,
    // rather than relying on compiler-generated lambda names or constructing the
    // unrelated client-dependent rotation. Execute that actual component below.
    private static Composite DelayReferencedBy(Type shadow,string factory)
    {
        var references=CalledMethods(shadow.GetMethod(factory)!).ToArray();
        var helper=references.SingleOrDefault(method=>method.DeclaringType==shadow && method.Name=="CreateInnerFocusDelay");
        if(helper!=null) return (Composite)helper.Invoke(null,null)!;
        MethodInfo delay=references.OfType<MethodInfo>().Single(method=>CalledMethods(method).Any(callee=>callee.DeclaringType==typeof(Thread) && callee.Name=="Sleep"));
        object? owner=delay.IsStatic?null:RuntimeHelpers.GetUninitializedObject(delay.DeclaringType!);
        var action=(ActionSucceedDelegate)delay.CreateDelegate(typeof(ActionSucceedDelegate),owner);
        return new TreeSharp.Action(action);
    }
    private static void VerifyDelay(Composite delay)
    {
        delay.Start(null!);
        Check(delay.Tick(null!)==RunStatus.Running,"first delay tick must yield; baseline blocking Sleep returns Success");
        Check(delay is WaitContinue,"delay must use the existing cancellable tree wait primitive");
        var end=typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic)!;
        end.SetValue(delay,DateTime.Now.AddHours(1));
        for(int i=0;i<500;i++) Check(delay.Tick(null!)==RunStatus.Running,"pending ticks cannot advance the follow-on action");
        delay.Stop(null!);
        Check(delay.Tick(null!)==RunStatus.Failure,"stopped delay must not resume itself");
        int follow=0;
        var sequence=new Sequence(delay,new TreeSharp.Action(_=>{follow++;return RunStatus.Success;}));
        sequence.Start(null!);
        Check(sequence.Tick(null!)==RunStatus.Running && follow==0,"restart must establish a new delay");
        end.SetValue(delay,DateTime.Now.AddSeconds(-1));
        Check(sequence.Tick(null!)==RunStatus.Success && follow==1,"elapsed delay permits exactly one follow-on action");
        Check(sequence.Tick(null!)==RunStatus.Success && follow==1,"completed tick must not repeat the action");
        sequence.Stop(null!);
    }
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[] bytes=method.GetMethodBody()?.GetILAsByteArray()??Array.Empty<byte>();
        for(int i=0;i<bytes.Length;)
        {
            ushort key=bytes[i++]; if(key==0xfe) key=(ushort)(0xfe00|bytes[i++]);
            OpCode code=Opcodes[key];
            if(code.OperandType==OperandType.InlineMethod)
            {
                int token=BitConverter.ToInt32(bytes,i); i+=4;
                yield return method.Module.ResolveMethod(token)!; continue;
            }
            i+=code.OperandType switch
            {
                OperandType.InlineNone=>0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
                OperandType.InlineVar=>2,
                OperandType.InlineI8 or OperandType.InlineR=>8,
                OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(bytes,i),
                _=>4
            };
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
