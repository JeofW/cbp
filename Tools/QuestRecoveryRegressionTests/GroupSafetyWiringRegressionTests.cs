using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Loaders;
using Styx.Logic.Combat;

// Compiled call-site checks, not a simulated dungeon run. Behavioral permission
// cases execute the actual linked owner in GroupEngagementRegressionTests.
internal static class GroupSafetyWiringRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (!Environment.GetCommandLineArgs().Contains("--routine-compatibility")) return;
        string source = (string)typeof(RoutineCompilationRegression)
            .GetMethod("ResolveSourceDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { AppContext.BaseDirectory })!;
        Assembly routine = Compile(source);
        string botPath = Path.GetFullPath(Path.Combine(source, "..", "..", "Bots", "CombatBot.cs"));
        Assembly bot = Compile(botPath);
        MethodInfo scanner = typeof(RoutineBoundaryRegressionTests)
            .GetMethod("CalledMethods", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodBase[] Calls(MethodBase m) => ((IEnumerable<MethodBase>)scanner.Invoke(null, new object[] { m })!).ToArray();
        MethodInfo Method(Assembly a, string type, string name) => a.GetType(type, true)!
            .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo[] NestedActions(MethodInfo factory, string dispatch) => Calls(factory).OfType<MethodInfo>()
            .Where(m => Calls(m).Any(c => c.Name == dispatch)).ToArray();
        var checks = new List<(string Name, System.Action Run)>();
        void Before(MethodInfo action, string guard, string dispatch)
        {
            var calls = Calls(action);
            int guarded = Array.FindIndex(calls, c => c.Name == guard);
            int submitted = Array.FindIndex(calls, c => c.Name == dispatch);
            if (guarded < 0 || submitted < 0 || guarded >= submitted)
                throw new InvalidOperationException($"{action.Name}: {guard} must precede {dispatch}");
        }
        Type spell = routine.GetType("Singular.Helpers.Spell", true)!;
        foreach (Type identifier in new[] { typeof(string), typeof(int) })
        {
            var factory = spell.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "Cast" && m.GetParameters()[0].ParameterType == identifier
                    && m.GetParameters().Length == (identifier == typeof(string) ? 4 : 3));
            checks.Add((identifier.Name + " cast dispatch rechecks target and AoE permission", () =>
            {
                var actions = NestedActions(factory, "Cast");
                if (actions.Length != 1) throw new InvalidOperationException("Expected one actual dispatch action");
                Before(actions[0], "IsCombatActionSafe", "Cast");
            }));
        }
        var auto = Method(routine, "Singular.Helpers.Common", "CreateAutoAttack");
        foreach (string dispatch in new[] { "ToggleAttack", "CastPetAction" })
        {
            string effect = dispatch;
            checks.Add((effect + " rechecks current enemy permission", () =>
                Before(NestedActions(auto, effect).Single(), "MayAttackCurrentTarget", effect)));
        }
        var wand = routine.GetType("Singular.Helpers.Common", true)!
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "CreateUseWand" && m.GetParameters().Length == 1);
        checks.Add(("wand dispatch rechecks current enemy permission", () =>
            Before(NestedActions(wand, "Cast").Single(), "MayAttackCurrentTarget", "Cast")));
        checks.Add(("routine eligibility delegates to the host owner", () =>
        {
            if (!Calls(Method(routine,"Singular.Helpers.Unit","IsEligibleDungeonCombatTarget"))
                .Any(c => c.DeclaringType?.FullName == "Styx.Logic.Combat.GroupCombatSafety" && c.Name == "MayAttack"))
                throw new InvalidOperationException("Routine uses an independent permission owner");
        }));
        checks.Add(("Combat Bot eligibility delegates to the same host owner", () =>
        {
            if (!Calls(Method(bot,"Styx.Bot.CustomBots.CombatBot","IsEligibleDungeonTarget"))
                .Any(c => c.DeclaringType?.FullName == "Styx.Logic.Combat.GroupCombatSafety" && c.Name == "MayAttack"))
                throw new InvalidOperationException("Botbase uses an independent permission owner");
        }));
        checks.Add(("Combat Bot pull gate checks eligible enemy", () =>
        {
            if (!Calls(Method(bot,"Styx.Bot.CustomBots.CombatBot","NeedPull"))
                .Any(c => c.Name == "IsEligibleDungeonTarget"))
                throw new InvalidOperationException("Pull gate bypasses engagement checks");
        }));
        checks.Add(("ground placement delegates the requested position to the native owner", () =>
        {
            if (!Calls(typeof(LegacySpellManager).GetMethod("ClickRemoteLocation")!)
                .Any(c => c.DeclaringType == typeof(SpellManager) && c.Name == "ClickRemoteLocation"))
                throw new InvalidOperationException("Legacy placement substitutes cursor/player commands for coordinates");
        }));
        var failed = new List<string>();
        foreach (var check in checks)
        {
            try { check.Run(); Console.WriteLine("PASS compiled engagement wiring: " + check.Name); }
            catch (Exception ex) { failed.Add(check.Name + ": " + ex.Message); Console.Error.WriteLine("FAIL compiled engagement wiring: " + failed[^1]); }
        }
        Console.WriteLine($"Compiled group boundary checks: {checks.Count-failed.Count}/{checks.Count}; not live action/packet coverage.");
        if (failed.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failed));
    }

    private static Assembly Compile(string source)
    {
        var compiler = new SourceCompiler(source);
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            compiler.AddReference(path);
        var result = compiler.Compile();
        var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
        if (errors.Length != 0 || compiler.CompiledAssembly == null)
            throw new InvalidOperationException("Boundary fixture source failed to compile: " + string.Join("; ", errors.Select(e => e.ToString())));
        return compiler.CompiledAssembly;
    }
}
