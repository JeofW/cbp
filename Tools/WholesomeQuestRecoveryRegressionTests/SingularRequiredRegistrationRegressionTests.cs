using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using TreeSharp;

// Compiled production attributes for all required class/spec/context registrations,
// plus actual builder construction of the Restoration Shaman trees. Declaration
// coverage and construction are separate from ticking a rotation or live acceptance.
internal static class SingularRequiredRegistrationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Singular registration tests require Windows x86.");
        using var fixture = new Fixture();
        var cases = new List<(string Name, System.Action Test)>();
        var specs = Enum.GetValues(fixture.SpecType).Cast<object>()
            .Where(s => s.ToString() != "Any" && s.ToString() != "Lowbie").ToArray();
        Check(specs.Length == 30, "declared specialization inventory changed");
        foreach (var value in specs)
        {
            var spec = value; var cls = (WoWClass)(Convert.ToInt32(spec) >> 8);
            foreach (string valueContext in new[] { "Normal", "Instances", "Battlegrounds" })
            {
                var context = valueContext;
                foreach (string valueRole in new[] { "Combat", "Pull" })
                {
                    var role = valueRole;
                    cases.Add(($"{cls}/{spec}/{context}/{role}: required registration is declared", () => fixture.Declared(cls, spec, context, role)));
                }
            }
        }
        foreach (string value in new[] { "Normal", "Instances", "Battlegrounds" })
        {
            var context = value;
            cases.Add(($"RestorationShaman/{context}/Pull: actual builder installs the required tree", () => fixture.Construct(context, "Pull")));
            cases.Add(($"RestorationShaman/{context}/Combat: existing actual construction remains available", () => fixture.Construct(context, "Combat")));
            cases.Add(($"ElementalShaman/{context}: Restoration factory remains excluded", () => fixture.Foreign(context)));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS Singular required registration: " + item.Name); }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL Singular required registration assertion: " + item.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR Singular required registration fixture/owner: " + item.Name + ": " + e); }
        }
        Console.WriteLine($"Singular required-registration scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; compiled attributes plus real Restoration builder construction; no rotation tick or game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Singular required-registration regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object existing, movement;
        private readonly object mover;
        private readonly Type behaviorType, contextType;
        private readonly FieldInfo methods;
        private readonly MethodInfo build, restoration;
        private readonly List<MethodInfo> production;
        internal readonly Type SpecType;

        internal Fixture()
        {
            var fixtureType = typeof(SingularBehaviorCountRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!;
            var movementType = typeof(MeshMoveRequestOwnershipRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!;
            movement = Activator.CreateInstance(movementType, true)!;
            mover = movementType.GetField("mover", Hidden)!.GetValue(movement)!;
            try { existing = Activator.CreateInstance(fixtureType, true)!; }
            catch { ((IDisposable)movement).Dispose(); throw; }
            try
            {
                var assembly = ((Type)fixtureType.GetField("factories", Hidden)!.GetValue(existing)!).Assembly;
                SpecType = assembly.GetType("Singular.Managers.TalentSpec", true)!;
                behaviorType = assembly.GetType("Singular.BehaviorType", true)!;
                contextType = assembly.GetType("Singular.WoWContext", true)!;
                var builder = assembly.GetType("Singular.Dynamics.CompositeBuilder", true)!;
                methods = builder.GetField("_methods", Hidden)!; build = builder.GetMethod("GetComposite", Hidden)!;
                restoration = assembly.GetType("Singular.ClassSpecific.Shaman.Restoration", true)!
                    .GetMethod("CreateRestoShamanCombatBehavior", Hidden)!;
                production = assembly.GetTypes().Where(t => t.FullName != "Singular.Dynamics.W47BuilderControlledFactories")
                    .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    .Where(m => !m.IsGenericMethod && m.GetParameters().Length == 0 && typeof(Composite).IsAssignableFrom(m.ReturnType)).ToList();
                var settings = fixtureType.GetField("settings", Hidden)!.GetValue(existing)!;
                settings.GetType().GetProperty("UseInstanceRotation", Hidden)!.SetValue(settings, false);
            }
            catch { Dispose(); throw; }
        }
        private static IEnumerable<object> Attributes(MethodInfo method, string kind) =>
            method.GetCustomAttributes(false).Where(a => a.GetType().FullName == "Singular.Dynamics." + kind + "Attribute");
        private static int Value(object attribute, string property) => Convert.ToInt32(attribute.GetType().GetProperty(property, Hidden)!.GetValue(attribute));
        private bool Matches(MethodInfo method, WoWClass cls, object spec, string context, string role)
        {
            int wantedSpec = Convert.ToInt32(spec), any = Convert.ToInt32(Enum.Parse(SpecType, "Any"));
            int wantedContext = Convert.ToInt32(Enum.Parse(contextType, context));
            int wantedRole = Convert.ToInt32(Enum.Parse(behaviorType, role));
            return Attributes(method, "Class").Any(a => Value(a, "SpecificClass") == (int)cls || Value(a, "SpecificClass") == (int)WoWClass.None)
                && Attributes(method, "Spec").Any(a => Value(a, "SpecificSpec") == wantedSpec || Value(a, "SpecificSpec") == any)
                && Attributes(method, "Context").Any(a => (Value(a, "SpecificContext") & wantedContext) != 0)
                && Attributes(method, "Behavior").Any(a => (Value(a, "Type") & wantedRole) != 0);
        }
        internal void Declared(WoWClass cls, object spec, string context, string role) =>
            Check(production.Any(m => Matches(m, cls, spec, context, role)), "required class/spec/context registration is missing");

        private Composite? Build(object spec, string context, string role, List<MethodInfo> selected, out int count)
        {
            if (ObjectManager.Executor != null || Navigator.IsNavigatorLoaded || ObjectManager.Wow == null)
                throw new InvalidOperationException("Construction fixture lost its self-process isolation");
            int moves = (int)mover.GetType().GetField("Moves", Hidden)!.GetValue(mover)!;
            int stops = (int)mover.GetType().GetField("Stops", Hidden)!.GetValue(mover)!;
            methods.SetValue(null, selected);
            var args = new object[] { WoWClass.Shaman, spec, Enum.Parse(behaviorType, role), Enum.Parse(contextType, context), 0 };
            var factoryFailures = new List<string>();
            System.Action<LogLevel, string> logged = (_, text) => { if (text.Contains("ERROR Creating composite:")) factoryFailures.Add(text); };
            Logging.OnMessageLogged += logged;
            try
            {
                Composite? result;
                try { result = (Composite?)build.Invoke(null, args); }
                catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
                count = (int)args[4];
                if (factoryFailures.Count != 0) throw new InvalidOperationException("Factory construction failed instead of proving registration: " + string.Join("; ", factoryFailures));
                Check(moves == (int)mover.GetType().GetField("Moves", Hidden)!.GetValue(mover)! &&
                    stops == (int)mover.GetType().GetField("Stops", Hidden)!.GetValue(mover)!, "construction dispatched movement");
                return result;
            }
            finally { Logging.OnMessageLogged -= logged; }
        }
        internal void Construct(string context, string role)
        {
            var tree = Build(Enum.Parse(SpecType, "RestorationShaman"), context, role, production, out int count);
            Check(tree != null && count > 0, "actual builder has no installed required behavior");
            // Never Start/Tick class rotations in this source-only construction test.
            Check(tree is GroupComposite group && group.Children.Count > 0, "constructed behavior has no executable selector children");
        }
        internal void Foreign(string context)
        {
            var tree = Build(Enum.Parse(SpecType, "ElementalShaman"), context, "Pull", new List<MethodInfo> { restoration }, out int count);
            Check(tree == null && count == 0, "Restoration registration leaked into another specialization");
        }
        public void Dispose()
        {
            try { ((IDisposable)existing).Dispose(); }
            finally { ((IDisposable)movement).Dispose(); }
        }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
