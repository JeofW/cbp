using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Loaders;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using TestAction = System.Action;

internal static class CastDispatchRegressionTests
{
    private const string MissingSpell = "__audit_missing_spell_12340__";
    private const int MissingId = int.MaxValue;
    private static WoWUnit? _target;
    private static int _selections;
    private static bool _unstableSelector;

    [ModuleInitializer]
    internal static void Run()
    {
        if (!Environment.GetCommandLineArgs().Contains("--routine-compatibility")) return;
        if (ObjectManager.Me != null || ObjectManager.Wow != null || ObjectManager.Executor != null)
            throw new InvalidOperationException("Cast-dispatch fixtures require an unattached process.");
        string source = (string)typeof(RoutineCompilationRegression)
            .GetMethod("ResolveSourceDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { AppContext.BaseDirectory })!;
        var compiler = new SourceCompiler(source);
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            compiler.AddReference(path);
        CompilerResults compiled = compiler.Compile();
        var errors = compiled.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
        if (errors.Length != 0 || compiler.CompiledAssembly == null)
            throw new InvalidOperationException("Dispatch source compilation failed: " + string.Join("; ", errors.Select(error => error.ToString())));
        var assembly = compiler.CompiledAssembly;
        var spell = assembly.GetType("Singular.Helpers.Spell", true)!;
        var selector = assembly.GetType("Singular.Helpers.UnitSelectionDelegate", true)!;
        var predicate = assembly.GetType("Singular.Helpers.SimpleBooleanDelegate", true)!;
        object select = typeof(CastDispatchRegressionTests).GetMethod(nameof(Select), BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate(selector);
        object accept = typeof(CastDispatchRegressionTests).GetMethod(nameof(Accept), BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate(predicate);
        var prevention = (Dictionary<string, DateTime>)spell.GetField("DoubleCastPreventionDict")!.GetValue(null)!;
        var markBuff = spell.GetMethod("UpdateDoubleCastDict", BindingFlags.Static | BindingFlags.NonPublic)!;
        Composite Dispatch(bool byId, object? identifier = null, bool missingSelector = false)
        {
            MethodInfo factory = byId
                ? spell.GetMethod("Cast", new[] { typeof(int), selector, predicate })!
                : spell.GetMethod("Cast", new[] { typeof(string), predicate, selector, predicate })!;
            object?[] arguments = byId
                ? new[] { identifier ?? MissingId, missingSelector ? null : select, accept }
                : new[] { identifier ?? MissingSpell, accept, missingSelector ? null : select, accept };
            return ActualDispatchAction(factory, arguments);
        }
        var tests = new List<(string Name, TestAction Run)>();
        foreach (bool byId in new[] { false, true })
        {
            bool id = byId;
            string label = id ? "ID" : "name";
            tests.Add(($"{label} dispatch rejects a disappeared target without exception logging", () =>
            {
                _target = null;
                Failure(Dispatch(id));
                Check(_selections == 1, "target loss must be checked once before logging or casting");
            }));
            tests.Add(($"{label} dispatch tolerates a missing selector", () =>
            {
                Failure(Dispatch(id, missingSelector: true));
                Check(_selections == 0, "an absent selector must not invoke selection");
            }));
            tests.Add(($"{label} dispatch rejects invalid spell metadata before selection", () =>
            {
                Failure(Dispatch(id, id ? (object)0 : " "));
                Check(_selections == 0, "invalid spell metadata must fail before callbacks");
            }));
            tests.Add(($"{label} dispatch propagates a real SpellManager rejection", () =>
            {
                Failure(Dispatch(id));
                Check(_selections == 1, "logging and dispatch must use the same selected object");
            }));
            tests.Add(($"{label} dispatch does not reselect between logging and casting", () =>
            {
                _unstableSelector = true;
                Failure(Dispatch(id));
                Check(_selections == 1, "a changing callback must not redirect the submitted target");
            }));
            tests.Add(($"{label} rejected dispatch yields to the next priority action", () =>
            {
                int movement = 0;
                var root = new PrioritySelector(Dispatch(id), new TreeSharp.Action(_ => { movement++; return RunStatus.Success; }));
                Check(Tick(root) == RunStatus.Success && movement == 1, "a rejected cast cannot monopolize the priority selector");
            }));
            tests.Add(($"{label} rejected dispatch does not execute success-only bookkeeping", () =>
            {
                var root = new Sequence(Dispatch(id), new TreeSharp.Action(_ =>
                {
                    markBuff.Invoke(null, new object[] { MissingSpell });
                    return RunStatus.Success;
                }));
                Failure(root);
                Check(!prevention.ContainsKey(MissingSpell), "rejected dispatch must not create double-cast prevention evidence");
            }));
        }
        tests.Add(("the real backend rejects an unknown name and ID", () =>
        {
            Check(!SpellManager.Cast(MissingSpell, _target!) && !SpellManager.Cast(MissingId, _target!),
                "fixture must exercise actual backend false results, not a mocked cast");
        }));
        tests.Add(("rejection does not erase pre-existing prevention evidence", () =>
        {
            DateTime before = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
            prevention[MissingSpell] = before;
            Failure(Dispatch(false));
            Check(prevention[MissingSpell] == before, "this boundary does not own earlier cast evidence");
        }));

        var oldPlayer = ObjectManager.Me;
        var oldSpells = new Dictionary<string, WoWSpell>(SpellManager.KnownSpells);
        var oldPrevention = new Dictionary<string, DateTime>(prevention);
        bool oldFileLogging = Logging.FileLogging;
        var failures = new List<string>();
        try
        {
            Logging.FileLogging = false;
            ObjectManager.Me = new LocalPlayer(0);
            SpellManager.KnownSpells.Clear();
            foreach (var test in tests)
            {
                _target = ObjectManager.Me;
                _selections = 0;
                _unstableSelector = false;
                prevention.Clear();
                var messages = new List<string>();
                Logging.LogMessageDelegate listener = batch => messages.AddRange(batch.Select(message => message.Message));
                Logging.OnLogMessage += listener;
                try
                {
                    test.Run();
                    Check(!messages.Any(message => message.Contains("Exception", StringComparison.OrdinalIgnoreCase)),
                        "TreeSharp swallowing an exception is not a dispatch-safety pass");
                    Console.WriteLine("PASS cast dispatch: " + test.Name);
                }
                catch (Exception error)
                {
                    failures.Add(test.Name + ": " + error.Message);
                    Console.Error.WriteLine("FAIL cast dispatch: " + failures[^1]);
                }
                finally { Logging.OnLogMessage -= listener; }
            }
        }
        finally
        {
            ObjectManager.Me = oldPlayer;
            Logging.FileLogging = oldFileLogging;
            SpellManager.KnownSpells.Clear();
            foreach (var item in oldSpells) SpellManager.KnownSpells.Add(item.Key, item.Value);
            prevention.Clear();
            foreach (var item in oldPrevention) prevention.Add(item.Key, item.Value);
            _target = null;
        }
        Console.WriteLine($"Cast dispatch scenarios: {tests.Count - failures.Count}/{tests.Count}; real compiled actions and backend rejection; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    // Factory construction reads client latency in unrelated dismount children.
    // Follow its real IL to the dispatch action, preserve its actual void/status
    // delegate contract, and bind the factory captures. No replacement cast logic.
    private static Composite ActualDispatchAction(MethodInfo factory, object?[] arguments)
    {
        var calls = typeof(RoutineBoundaryRegressionTests).GetMethod("CalledMethods", BindingFlags.Static | BindingFlags.NonPublic)!;
        IEnumerable<MethodBase> Called(MethodBase method) => (IEnumerable<MethodBase>)calls.Invoke(null, new object[] { method })!;
        MethodInfo action = Called(factory).OfType<MethodInfo>().Single(method => Called(method).Any(callee =>
            callee.DeclaringType == typeof(SpellManager) && callee.Name == "Cast" && callee.GetParameters().Length == 2));
        object? owner = action.IsStatic ? null : RuntimeHelpers.GetUninitializedObject(action.DeclaringType!);
        var parameters = factory.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
            action.DeclaringType!.GetField(parameters[i].Name!, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(owner, arguments[i]);
        return action.ReturnType == typeof(void)
            ? new TreeSharp.Action((ActionSucceedDelegate)action.CreateDelegate(typeof(ActionSucceedDelegate), owner))
            : new TreeSharp.Action((ActionDelegate)action.CreateDelegate(typeof(ActionDelegate), owner));
    }
    private static WoWUnit Select(object _) { _selections++; return _unstableSelector && _selections > 1 ? null! : _target!; }
    private static bool Accept(object _) => true;
    private static RunStatus Tick(Composite root)
    {
        root.Start(null!);
        try { return root.Tick(null!); }
        finally { root.Stop(null!); }
    }
    private static void Failure(Composite root) => Check(Tick(root) == RunStatus.Failure, "rejected dispatch must report Failure");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
