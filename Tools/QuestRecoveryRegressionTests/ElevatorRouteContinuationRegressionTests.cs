using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Tripper.Navigation;

internal static class ElevatorRouteContinuationRegressionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static WoWPoint P(float x, float y = 0, float z = 100) => new(x, y, z);
    private static readonly WoWPoint Origin = P(-4, 0, 0);
    private static readonly WoWPoint Lift = P(0, 0, 50);
    private static readonly WoWPoint Exit = P(8);
    private static readonly WoWPoint Corner = P(10, 20);
    private static readonly WoWPoint Goal = P(30);
    private static WoWPoint[] Original() => new[] { Origin, P(-50, 10, 50), Exit, Corner, Goal };

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, Action Test)[]
        {
            ("upper-floor corner is not replaced by a direct chord", () => PreserveTail(Original(), new[] { Origin, Exit, Corner, Goal })),
            ("descending continuation keeps its bend too", Descending),
            ("a partial tail does not acquire the requested destination", () => PreserveTail(new[] { Origin, Exit, Corner }, new[] { Origin, Exit, Corner })),
            ("a path ending at the landing stays at the landing", () => PreserveTail(new[] { Origin, Exit }, new[] { Origin, Exit })),
            ("landing order cannot reverse the original route", LandingOrder),
            ("non-finite transport elevation is not usable evidence", InvalidTransport),
            ("invalid intermediate mesh points cannot be silently discarded", InvalidIntermediate),
            ("existing three-point complete shortcut stays compatible", CompleteControl),
            ("missing destination-side landing still rejects the shortcut", MissingLanding),
            ("installation preserves aligned flags, areas and abilities", () => Install(true)),
            ("installation never upgrades an exhausted search to complete", PartialStatus),
            ("complete continuation retains its original completion status", () => Install(false)),
            ("missing or short metadata rejects without mutation", IncompleteMetadata),
            ("a fabricated shortcut tail cannot replace the native tail", FabricatedTail),
            ("invalid source indices cannot mutate the route", InvalidIndex),
            ("installed metadata does not alias the previous arrays", NoArrayAlias),
            ("dense curved continuations retain every mesh point", DenseTail)
        };
        var errors = new List<string>();
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS elevator continuation: " + test.Name); }
            catch (Exception error)
            {
                while (error is TargetInvocationException wrapped && wrapped.InnerException != null) error = wrapped.InnerException;
                errors.Add(test.Name + ": " + error.Message);
                Console.Error.WriteLine("FAIL elevator continuation: " + errors[^1]);
            }
        }
        Console.WriteLine($"Elevator route continuation: {cases.Length - errors.Count}/{cases.Length}. Actual route helper/owner, no game attached.");
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void PreserveTail(WoWPoint[] input, WoWPoint[] expected)
    {
        Check(MeshNavigator.TryCreateSafeElevatorShortcut(Origin, Goal, Lift, input, out var result), "valid landing fixture must be accepted");
        Check(result.SequenceEqual(expected), "all onward mesh points must survive; no straight-line destination may be invented");
    }
    private static void Descending()
    {
        WoWPoint Flip(WoWPoint value) => new(value.X, value.Y, 100 - value.Z);
        var input = Original().Select(Flip).ToArray();
        Check(MeshNavigator.TryCreateSafeElevatorShortcut(Flip(Origin), Flip(Goal), Flip(Lift), input, out var result), "descending landings must be accepted");
        Check(result.SequenceEqual(new[] { Flip(Origin), Flip(Exit), Flip(Corner), Flip(Goal) }), "descending route cannot cut through the corner");
    }
    private static void LandingOrder() => Check(!MeshNavigator.TryCreateSafeElevatorShortcut(Origin, Goal, Lift,
        new[] { Exit, Origin, Goal }, out _), "an exit before the waiting point is not an onward suffix");
    private static void InvalidTransport()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(!MeshNavigator.TryCreateSafeElevatorShortcut(Origin, Goal, P(0, 0, invalid), Original(), out _), "non-finite platform observation must fail closed");
    }
    private static void InvalidIntermediate()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var input = Original(); input[3] = P(invalid, 20);
            Check(!MeshNavigator.TryCreateSafeElevatorShortcut(Origin, Goal, Lift, input, out _), "invalid native route geometry must not be replaced by a guessed chord");
        }
    }
    private static void CompleteControl() => PreserveTail(new[] { Origin, Exit, Goal }, new[] { Origin, Exit, Goal });
    private static void MissingLanding() => Check(!MeshNavigator.TryCreateSafeElevatorShortcut(Origin, Goal, Lift,
        new[] { Origin, P(0, 8), Goal }, out _), "a perpendicular adjacent-shaft point is not a destination-side landing");

    private static object? Get(MeshNavigator nav, string name) => typeof(MeshNavigator).GetField(name, Private)!.GetValue(nav);
    private static void Set(MeshNavigator nav, string name, object? value) => typeof(MeshNavigator).GetField(name, Private)!.SetValue(nav, value);
    private static MeshNavigator Fixture(bool partial = true)
    {
        var nav = new MeshNavigator(); nav.OverrideCurrentPath(Original());
        Set(nav, "_destination", Goal); Set(nav, "_isPartialPath", partial);
        Set(nav, "_lastPathStatus", new Status(partial ? 0x40000060u : 0x40000000u));
        Set(nav, "_currentFlags", new[] { StraightPathFlags.Start, StraightPathFlags.None, StraightPathFlags.None, StraightPathFlags.OffMeshConnection, StraightPathFlags.End });
        Set(nav, "_currentPolyTypes", new[] { AreaType.Ground, AreaType.Ground, AreaType.Road, AreaType.Gate, AreaType.Ground });
        Set(nav, "_currentAbilityFlags", new[] { AbilityFlags.Run, AbilityFlags.Run, AbilityFlags.Run, AbilityFlags.Jump, AbilityFlags.Run });
        return nav;
    }
    private static bool Apply(MeshNavigator nav, WoWPoint[] points, int exitIndex) =>
        (bool)(typeof(MeshNavigator).GetMethod("TryInstallElevatorContinuation", Private)
            ?? throw new InvalidOperationException("No owner validates/preserves onward route metadata"))
            .Invoke(nav, new object[] { points, exitIndex })!;
    private static WoWPoint[] Shortcut() => new[] { Origin, Exit, Corner, Goal };
    private static void Install(bool partial)
    {
        var nav = Fixture(partial);
        Check(Apply(nav, Shortcut(), 2), "valid native continuation must install");
        Check(nav.CurrentPath.SequenceEqual(Shortcut()) && (int)Get(nav, "_currentPathIndex")! == 1, "boarding segment must lead into the preserved tail");
        Check(((StraightPathFlags[])Get(nav, "_currentFlags")!).SequenceEqual(new[] { StraightPathFlags.OffMeshConnection, StraightPathFlags.None, StraightPathFlags.OffMeshConnection, StraightPathFlags.End }), "onward special transitions must not become ordinary walking");
        Check(((AreaType[])Get(nav, "_currentPolyTypes")!).SequenceEqual(new[] { AreaType.Elevator, AreaType.Road, AreaType.Gate, AreaType.Ground }), "area ownership must remain aligned with original tail points");
        Check(((AbilityFlags[])Get(nav, "_currentAbilityFlags")!).Skip(1).SequenceEqual(new[] { AbilityFlags.Run, AbilityFlags.Jump, AbilityFlags.Run }), "onward traversal ability flags must survive");
        Check((bool)Get(nav, "_isPartialPath")! == partial, "a splice must not alter original completion evidence");
    }
    private static void PartialStatus()
    {
        var nav = Fixture(); Check(Apply(nav, Shortcut(), 2), "fixture must install");
        Check((bool)Get(nav, "_isPartialPath")! && nav.LastRouteNativeStatus == 0x40000060u,
            "partial plus node-exhaustion evidence must survive the synthetic transition");
    }
    private static void IncompleteMetadata()
    {
        foreach (string field in new[] { "_currentFlags", "_currentPolyTypes", "_currentAbilityFlags" })
        {
            var nav = Fixture(); Set(nav, field, null);
            Check(!Apply(nav, Shortcut(), 2) && nav.CurrentPath.SequenceEqual(Original()), "missing metadata must reject without modifying points");
            nav = Fixture(); var array = (Array)Get(nav, field)!;
            Set(nav, field, Array.CreateInstance(array.GetType().GetElementType()!, 3));
            Check(!Apply(nav, Shortcut(), 2) && nav.CurrentPath.SequenceEqual(Original()), "short metadata cannot be silently fabricated");
        }
    }
    private static void FabricatedTail()
    {
        var nav = Fixture(); var invented = Shortcut(); invented[2] = P(25, -20);
        Check(!Apply(nav, invented, 2) && nav.CurrentPath.SequenceEqual(Original()), "caller cannot invent an unsupported segment");
    }
    private static void InvalidIndex()
    {
        foreach (int index in new[] { -1, 0, 5, int.MaxValue })
        {
            var nav = Fixture(); Check(!Apply(nav, Shortcut(), index) && nav.CurrentPath.SequenceEqual(Original()), "invalid suffix offset must not alter route state");
        }
    }
    private static void NoArrayAlias()
    {
        var nav = Fixture(); var old = (AreaType[])Get(nav, "_currentPolyTypes")!;
        Check(Apply(nav, Shortcut(), 2), "fixture must install"); old[3] = AreaType.Lava;
        Check(((AreaType[])Get(nav, "_currentPolyTypes")!)[2] == AreaType.Gate, "installed metadata must own its snapshot");
    }
    private static void DenseTail()
    {
        var tail = Enumerable.Range(0, 2048).Select(i => P(20 + i / 10f, 20 + (i % 5))).Append(Goal).ToArray();
        var input = new[] { Origin, Exit }.Concat(tail).ToArray();
        PreserveTail(input, input);
    }
}
