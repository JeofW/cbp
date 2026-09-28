using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Execute the complete tracked shared Movement owner and real TreeSharp.
// Reuse the retained observation/effect boundary and every existing sight case;
// add decision-continuity cases without replacing any production method.
internal static class CombatMovementContinuityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        string boundary = (string)typeof(CombatSightMovementRegressionTests)
            .GetField("Boundary", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        const string anchor = "        int passed=0,assertions=0,unexpected=0;";
        int at = boundary.IndexOf(anchor, StringComparison.Ordinal);
        if (at < 0 || boundary.IndexOf(anchor, at + anchor.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Retained movement fixture insertion boundary changed.");
        boundary = boundary.Insert(at, AdditionalCases).Replace("Combat sight movement scenarios:", "Combat movement continuity scenarios:");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-combat-movement-continuity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.Copy(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Movement.cs"), Path.Combine(temporary, "Movement.cs"));
            File.WriteAllText(Path.Combine(temporary, "Boundary.cs"), boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)type.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Complete Movement owner failed compilation: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("MovementCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temporary, true); }
    }

    private const string AdditionalCases = """
        foreach (int movementFamily in new[] { 0, 1, 2, 3 })
        {
            int family = movementFamily;
            string label = new[] { "melee target", "ranged current target", "explicit recipient", "explicit location" }[family];
            Composite Build(WoWUnit intended, bool stop = true) => family switch
            {
                0 => Movement.CreateMoveToMeleeBehavior(stop),
                1 => Movement.CreateMoveToTargetBehavior(stop, 5f),
                2 => Movement.CreateMoveToTargetBehavior(stop, 5f, _ => { Selections++; return intended; }),
                _ => Movement.CreateMoveToLocationBehavior(_ => { Selections++; return intended.Location; }, stop, _ => 5f)
            };
            void Add(string name, System.Action<WoWUnit> body) => cases.Add(("continuity " + label + ": " + name, () => { Reset(); body(Target); }));
            void Deny(string name, System.Action<WoWUnit> arrange) => Add(name, intended =>
            {
                arrange(intended);
                var status = Tick(Build(intended));
                Check(status == RunStatus.Failure && Moves.Count == 0 && Stops == 0,
                    "revoked observation must not move/stop or report a handled decision");
            });
            Add("ordinary valid approach remains available", intended =>
            {
                var destination = intended.Location;
                Check(Tick(Build(intended)) == RunStatus.Success && Moves.SequenceEqual(new[] { destination }) && Stops == 0,
                    "valid approach did not preserve the intended destination");
            });
            Add("one movement decision evaluates its selector once", intended =>
            {
                Tick(Build(intended));
                if (family >= 2) Check(Selections == 1, "selector ran " + Selections + " times");
                Check(Moves.Count == 1, "one decision issued duplicate movement");
            });
            Add("nearby target stops the current actor once", intended =>
            {
                Player.Location = intended.Location.Add(1, 0, 0); Player.IsMoving = true;
                Check(Tick(Build(intended)) == RunStatus.Success && Moves.Count == 0 && Stops == 1, "nearby target stop changed");
            });
            Add("already stopped in range is handled without an extra stop", intended =>
            {
                Player.Location = intended.Location.Add(1, 0, 0);
                Check(Tick(Build(intended)) == RunStatus.Success && Moves.Count == 0 && Stops == 0, "in-range idle handling changed");
            });
            Add("explicit continuous approach retains its no-stop choice", intended =>
            {
                Player.Location = intended.Location.Add(1, 0, 0); Player.IsMoving = true;
                Check(Tick(Build(intended, false)) == RunStatus.Success && Moves.Count == 1 && Stops == 0, "no-stop caller was overridden");
            });
            Add("range threshold remains strict", intended =>
            {
                Player.Location = intended.Location.Add(5, 0, 0); Player.IsMoving = true;
                Tick(Build(intended)); Check(Moves.Count == 1 && Stops == 0, "exact range boundary unexpectedly stopped");
            });
            Add("negative finite three-dimensional destination remains valid", intended =>
            {
                intended.Location = new WoWPoint(-20, -30, -5); var expected = intended.Location;
                Tick(Build(intended)); Check(Moves.SequenceEqual(new[] { expected }), "finite negative coordinates rejected or flattened");
            });
            foreach (MoveResult result in Enum.GetValues<MoveResult>().Concat(new[] { (MoveResult)987654 }))
            {
                var outcome = result;
                Add("navigation receipt " + outcome, intended =>
                {
                    Result = outcome; int fallback = 0;
                    Tick(new PrioritySelector(Build(intended), new TreeSharp.Action(_ => { fallback++; return RunStatus.Success; })));
                    bool handled = outcome is MoveResult.Moved or MoveResult.PathGenerated or MoveResult.UnstuckAttempt or MoveResult.ReachedDestination;
                    Check(Moves.Count == 1 && fallback == (handled ? 0 : 1), "navigation result was discarded or a failed path swallowed");
                });
            }
            Deny("missing actor is a refusal without swallowed exceptions", _ => StyxWoW.Me = null);
            Deny("invalid actor cannot authorize movement", _ => Player.IsValid = false);
            Deny("dead actor cannot authorize movement", _ => Player.IsAlive = false);
            Deny("zero actor identity cannot authorize movement", _ => Player.Guid = 0);
            Deny("active casting is preserved", _ => Player.IsCasting = true);
            Deny("active channel without cast flag is preserved", _ => Player.ChanneledCastingSpellId = 101);
            Deny("manual movement control is preserved", _ => SingularSettings.Instance.DisableAllMovement = true);
            Deny("unknown destination sentinel", intended => intended.Location = WoWPoint.Empty);
            Deny("zero destination sentinel", intended => intended.Location = WoWPoint.Zero);
            Deny("NaN destination", intended => intended.Location = new WoWPoint(float.NaN, 5, 6));
            Deny("infinite destination", intended => intended.Location = new WoWPoint(4, 5, float.PositiveInfinity));
            Deny("actor replaced during destination observation", intended => intended.OnLocation = () => StyxWoW.Me = new WoWUnit { Guid = 99, CurrentTarget = intended });
            Deny("actor GUID changed during destination observation", intended => intended.OnLocation = () => Player.Guid = 99);
            Deny("actor invalidated during destination observation", intended => intended.OnLocation = () => Player.IsValid = false);
            Deny("actor died during destination observation", intended => intended.OnLocation = () => Player.IsAlive = false);
            Deny("casting began during destination observation", intended => intended.OnLocation = () => Player.IsCasting = true);
            Deny("channel began during destination observation", intended => intended.OnLocation = () => Player.ChanneledCastingSpellId = 101);
            Deny("movement disabled during destination observation", intended => intended.OnLocation = () => SingularSettings.Instance.DisableAllMovement = true);
            Deny("actor changed during origin observation", intended => Player.OnLocation = () => StyxWoW.Me = new WoWUnit { Guid = 99, CurrentTarget = intended });
            Deny("nearby stop cannot act on a replacement actor", intended =>
            {
                var location = intended.Location.Add(1, 0, 0); Player.Location = location; Player.IsMoving = true;
                intended.OnLocation = () => StyxWoW.Me = new WoWUnit { Guid = 99, CurrentTarget = intended, Location = location, IsMoving = true };
            });
            if (family < 3)
            {
                Deny("invalid selected unit", intended => intended.IsValid = false);
                Deny("dead hostile selected unit", intended => intended.IsAlive = false);
                Deny("zero selected identity", intended => intended.Guid = 0);
                Deny("selected GUID changed during location observation", intended => intended.OnLocation = () => intended.Guid = 99);
                Deny("selected unit invalidated during location observation", intended => intended.OnLocation = () => intended.IsValid = false);
                Deny("selected hostile died during location observation", intended => intended.OnLocation = () => intended.IsAlive = false);
                Add("friendly corpse approach remains available for resurrection callers", intended =>
                {
                    intended.IsAlive = false; intended.IsFriendly = true; var expected = intended.Location;
                    Tick(Build(intended)); Check(Moves.SequenceEqual(new[] { expected }), "dead-friendly approach was blanket-disabled");
                });
            }
            if (family < 2)
            {
                Deny("missing current target", _ => Player.CurrentTarget = null);
                Deny("target selection changed during destination observation", intended => intended.OnLocation = () => Player.CurrentTarget = new WoWUnit { Guid = 3 });
                Deny("target selection cleared during destination observation", intended => intended.OnLocation = () => Player.CurrentTarget = null);
            }
            if (family == 2)
            {
                Add("explicit recipient survives unrelated displayed-target change", intended =>
                {
                    var expected = intended.Location;
                    intended.OnLocation = () => Player.CurrentTarget = new WoWUnit { Guid = 3 };
                    Tick(Build(intended)); Check(Moves.SequenceEqual(new[] { expected }), "explicit friendly/recipient selector was replaced by current-target policy");
                });
                Add("selector side effect cannot borrow a replacement actor", intended =>
                {
                    var tree = Movement.CreateMoveToTargetBehavior(true, 5, _ => { Selections++; StyxWoW.Me = new WoWUnit { Guid = 99 }; return intended; });
                    Check(Tick(tree) == RunStatus.Failure && Moves.Count == 0 && Stops == 0 && Selections == 1, "replacement actor inherited selector admission");
                });
                Add("null explicit selector refuses", _ => Check(Tick(Movement.CreateMoveToTargetBehavior(true, 5, null!)) == RunStatus.Failure && Moves.Count == 0, "null selector accepted"));
            }
        }
        cases.Add(("continuity melee: player pursuit retains two-yard stop range", () =>
        {
            Reset(); Target.IsPlayer = true; Player.Location = Target.Location.Add(3, 0, 0); Player.IsMoving = true;
            Tick(Movement.CreateMoveToMeleeBehavior(true)); Check(Moves.Count == 1 && Stops == 0, "player pursuit incorrectly used NPC range");
        }));
        cases.Add(("continuity location: range callback cannot replace movement owner", () =>
        {
            Reset(); var intended = Target;
            var tree = Movement.CreateMoveToLocationBehavior(_ => intended.Location, true, _ => { StyxWoW.Me = new WoWUnit { Guid = 99 }; return 5; });
            Check(Tick(tree) == RunStatus.Failure && Moves.Count == 0 && Stops == 0, "range callback replaced owner without revoking request");
        }));
        foreach (float badRange in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            float selectedRange = badRange;
            cases.Add(("continuity location: invalid stop range " + selectedRange, () =>
            {
                Reset(); var intended = Target;
                Check(Tick(Movement.CreateMoveToLocationBehavior(_ => intended.Location, true, _ => selectedRange)) == RunStatus.Failure && Moves.Count == 0 && Stops == 0,
                    "unusable range authorized movement or a stop");
            }));
        }
        cases.Add(("continuity location: unused range is not evaluated for continuous approach", () =>
        {
            Reset(); var intended = Target;
            var tree = Movement.CreateMoveToLocationBehavior(_ => intended.Location, false, _ => throw new Failure("unused range evaluated"));
            Check(Tick(tree) == RunStatus.Success && Moves.Count == 1 && Stops == 0, "continuous approach changed");
        }));
        cases.Add(("continuity location: null location selector refuses without errors", () =>
        {
            Reset(); Check(Tick(Movement.CreateMoveToLocationBehavior(null!, true, _ => 5)) == RunStatus.Failure && Moves.Count == 0, "null location selector accepted");
        }));
        cases.Add(("continuity location: null required range selector refuses without errors", () =>
        {
            Reset(); var intended = Target;
            Check(Tick(Movement.CreateMoveToLocationBehavior(_ => intended.Location, true, null!)) == RunStatus.Failure && Moves.Count == 0, "null required range accepted");
        }));
""";
}
