using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Full tracked Movement, actual Spell.MeleeRange and real TreeSharp. Existing
// movement cases remain intact; controlled property callbacks expose changes
// between decision observations and the eventual target/facing/movement effect.
internal static class CombatControlContinuityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        string boundary = (string)typeof(CombatSightMovementRegressionTests).GetField("Boundary", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        string movementCases = (string)typeof(CombatMovementContinuityRegressionTests).GetField("AdditionalCases", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        static string ReplaceOnce(string text, string old, string replacement)
        {
            int at = text.IndexOf(old, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(old, at + old.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Controlled boundary anchor changed: " + old);
            return text.Remove(at, old.Length).Insert(at, replacement);
        }
        boundary = ReplaceOnce(boundary, "        int passed=0,assertions=0,unexpected=0;", movementCases + AdditionalCases + "        int passed=0,assertions=0,unexpected=0;");
        boundary = ReplaceOnce(boundary, "IsValid=true,IsAlive=true,IsMoving,IsCasting", "IsValid=true,IsAlive=true,IsCasting");
        boundary = ReplaceOnce(boundary, "public System.Action? OnSight,OnLocation;", "public System.Action? OnSight,OnLocation,OnMoving,OnFacing,OnName; public int FaceCalls; public float CombatReach; public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0; private bool moving; public bool IsMoving{get{var a=OnMoving;OnMoving=null;a?.Invoke();return moving;}set{moving=value;}}");
        boundary = ReplaceOnce(boundary, "public bool IsSafelyFacing(WoWUnit target,float angle)=>Facing;", "public bool IsSafelyFacing(WoWUnit target,float angle){var a=OnFacing;OnFacing=null;a?.Invoke();return Facing;}");
        boundary = ReplaceOnce(boundary, "public void Face(){MovementCases.Faces++;}", "public void Face(){MovementCases.Faces++;FaceCalls++;}");
        boundary = ReplaceOnce(boundary, "public string SafeName()=>\"controlled\";", "public string SafeName(){var a=OnName;OnName=null;a?.Invoke();return \"controlled\";}");
        boundary = ReplaceOnce(boundary, "public static class Group{public static bool MeIsTank=>false;}", "public static class Group{public static bool MeIsTank;}");
        string spellSource = File.ReadAllText(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"));
        var melee = CSharpSyntaxTree.ParseText(spellSource).GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == "MeleeRange");
        var selectedRange = CSharpSyntaxTree.ParseText(spellSource).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method => method.Identifier.ValueText == "MeleeRangeFor");
        boundary = ReplaceOnce(boundary, "public static class Spell{public static float MeleeRange=>5;}", "public static class Spell{" + melee.ToString() + selectedRange?.ToString() + "}");
        boundary = boundary.Replace("Combat sight movement scenarios:", "Combat control continuity scenarios:");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-combat-control-continuity-" + Guid.NewGuid().ToString("N"));
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
            if (errors.Length != 0) throw new InvalidOperationException("Complete movement control owner failed compilation: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("MovementCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temporary, true); }
    }

    private const string AdditionalCases = """
        foreach (int controlFamily in Enumerable.Range(0, 9))
        {
            int family = controlFamily;
            string label = new[] { "current facing", "explicit facing", "stop", "range stop", "current LOS", "explicit LOS", "target setup", "facing setup", "behind movement" }[family];
            WoWUnit Prepare()
            {
                Reset(); Singular.SingularRoutine.CurrentWoWContext = Singular.WoWContext.Normal; Group.MeIsTank = false;
                Player.Facing = false;
                if (family == 2 || family == 3) { Player.IsMoving = true; Target.Sight = true; Target.Distance = 2; }
                return family == 6 ? new WoWUnit { Guid = 3, Location = new WoWPoint(25, 30, 5) } : Target;
            }
            Composite Build(WoWUnit intended) => family switch
            {
                0 => Movement.CreateFaceTargetBehavior(),
                1 => Movement.CreateFaceTargetBehavior(_ => { Selections++; return intended; }),
                2 => Movement.CreateEnsureMovementStoppedBehavior(),
                3 => Movement.CreateEnsureMovementStoppedWithinRange(5),
                4 => Movement.CreateMoveToLosBehavior(),
                5 => Movement.CreateMoveToLosBehavior(_ => { Selections++; return intended; }),
                6 or 7 => Movement.CreateEnsureTargetAndFaceBehavior(_ => { Selections++; return intended; }),
                _ => Movement.CreateMoveBehindTargetBehavior(_ => true)
            };
            void Observe(WoWUnit intended, System.Action change)
            {
                if (family == 0 || family == 1 || family == 7) Player.OnFacing = change;
                else if (family == 2) Player.OnMoving = change;
                else if (family == 3 || family == 4 || family == 5) intended.OnSight = change;
                else if (family == 6) intended.OnName = change;
                else intended.OnLocation = change;
            }
            void Add(string name, System.Action<WoWUnit> test) => cases.Add(("control " + label + ": " + name, () => test(Prepare())));
            void Deny(string name, System.Action<WoWUnit> arrange) => Add(name, intended =>
            {
                arrange(intended); var status = Tick(Build(intended));
                Check(status == RunStatus.Failure && Moves.Count + Stops + Faces + TargetRequests == 0, "revoked participants still authorized control or readiness");
            });
            Add("ordinary action remains available", intended =>
            {
                Tick(Build(intended));
                Check(Moves.Count + Stops + Faces + TargetRequests == 1, "ordinary action was lost or duplicated");
                if (family == 0 || family == 1 || family == 7) Check(intended.FaceCalls == 1, "faced a different selected unit");
            });
            if (family == 1 || family == 5 || family == 6 || family == 7)
                Add("one selector observation per decision", intended => { Tick(Build(intended)); Check(Selections == 1, "selection repeated " + Selections + " times"); });
            Deny("missing actor", _ => StyxWoW.Me = null);
            Deny("invalid actor", _ => Player.IsValid = false);
            Deny("dead actor", _ => Player.IsAlive = false);
            Deny("zero actor GUID", _ => Player.Guid = 0);
            Deny("actor changes during observation", intended => Observe(intended, () => StyxWoW.Me = new WoWUnit { Guid = 99, CurrentTarget = intended, IsMoving = family == 2 || family == 3 }));
            Deny("actor GUID changes during observation", intended => Observe(intended, () => Player.Guid = 99));
            Deny("actor becomes invalid during observation", intended => Observe(intended, () => Player.IsValid = false));
            Deny("actor dies during observation", intended => Observe(intended, () => Player.IsAlive = false));
            if (family != 2)
            {
                Deny("zero selected GUID", intended => intended.Guid = 0);
                Deny("invalid selected unit", intended => intended.IsValid = false);
                Deny("dead hostile selected unit", intended => intended.IsAlive = false);
                Deny("selected GUID changes during observation", intended => Observe(intended, () => intended.Guid = 99));
                Deny("selected unit becomes invalid during observation", intended => Observe(intended, () => intended.IsValid = false));
                Deny("selected hostile dies during observation", intended => Observe(intended, () => intended.IsAlive = false));
            }
            if (family != 2 && family != 3)
            {
                Deny("channel prevents new control", _ => Player.ChanneledCastingSpellId = 101);
                Deny("channel begins during observation", intended => Observe(intended, () => Player.ChanneledCastingSpellId = 101));
            }
            if (family >= 4)
            {
                Deny("casting prevents new approach or target setup", _ => Player.IsCasting = true);
                Deny("casting begins during observation", intended => Observe(intended, () => Player.IsCasting = true));
            }
            if (family != 6)
            {
                // A movement setting suppresses movement/facing. The existing
                // off-target selection contract remains available without a turn.
                Add("manual movement control suppresses physical effects", intended =>
                {
                    SingularSettings.Instance.DisableAllMovement = true; Tick(Build(intended));
                    Check(Moves.Count + Stops + Faces == 0, "manual movement control ignored");
                });
                Deny("movement disabled during observation", intended => Observe(intended, () => SingularSettings.Instance.DisableAllMovement = true));
            }
            if (family == 0 || family == 3 || family == 4 || family == 6 || family == 7 || family == 8)
            {
                Deny("displayed target changes during observation", intended => Observe(intended, () => Player.CurrentTarget = new WoWUnit { Guid = 8 }));
                Deny("displayed target clears during observation", intended => Observe(intended, () => Player.CurrentTarget = null));
            }
            if (family == 0 || family == 3 || family == 4 || family == 8)
                Deny("missing current target", _ => Player.CurrentTarget = null);
            if (family == 1 || family == 5)
                Add("explicit selection survives unrelated displayed-target change", intended =>
                {
                    Observe(intended, () => Player.CurrentTarget = new WoWUnit { Guid = 8 }); Tick(Build(intended));
                    Check(Moves.Count + Faces == 1, "explicit selector was silently converted to current-target policy");
                });
            if (family == 0 || family == 1)
                Add("requested cast-time facing remains available", intended =>
                {
                    Player.IsCasting = true; Tick(Build(intended)); Check(Faces == 1, "ordinary casting incorrectly vetoed requested facing");
                });
            if (family == 2 || family == 3)
                Add("stopping movement remains available during a channel", intended =>
                {
                    Player.ChanneledCastingSpellId = 101; Tick(Build(intended)); Check(Stops == 1, "stop action incorrectly acquired a move/cast veto");
                });
            if (family == 4 || family == 5 || family == 8)
                foreach (MoveResult result in new[] { MoveResult.Failed, MoveResult.PathGenerationFailed, (MoveResult)987654 })
                {
                    var receipt = result;
                    Add("failed movement result " + receipt, intended =>
                    {
                        Result = receipt; Check(Tick(Build(intended)) == RunStatus.Failure && Moves.Count == 1, "failed movement was reported as handled");
                    });
                }
        }
        foreach (bool melee in new[] { false, true })
        {
            bool actualMelee = melee;
            cases.Add(("control location: revoked actor is checked before dependent range callback " + actualMelee, () =>
            {
                Reset(); var intended = Target; intended.OnLocation = () => StyxWoW.Me = null;
                Composite tree = actualMelee ? Movement.CreateMoveToMeleeBehavior(true)
                    : Movement.CreateMoveToLocationBehavior(_ => intended.Location, true, _ => Spell.MeleeRange);
                Check(Tick(tree) == RunStatus.Failure && Moves.Count == 0 && Stops == 0, "invalid actor reached actual melee-range observation");
            }));
        }
        cases.Add(("control target setup: displayed target GUID changes while logging", () =>
        {
            Reset(); var displayed = Target; var intended = new WoWUnit { Guid = 3 };
            intended.OnName = () => displayed.Guid = 99;
            Check(Tick(Movement.CreateEnsureTargetAndFaceBehavior(_ => intended)) == RunStatus.Failure && TargetRequests == 0, "target setup overrode a changed displayed identity");
        }));
        cases.Add(("control behind: caller requirements cannot replace the actor", () =>
        {
            Reset(); var intended = Target;
            var tree = Movement.CreateMoveBehindTargetBehavior(_ => { StyxWoW.Me = new WoWUnit { Guid = 99, CurrentTarget = intended }; return true; });
            Check(Tick(tree) == RunStatus.Failure && Moves.Count == 0, "requirements transferred behind movement to another actor");
        }));
        cases.Add(("control behind: caller requirements cannot select another target", () =>
        {
            Reset(); var tree = Movement.CreateMoveBehindTargetBehavior(_ => { Player.CurrentTarget = new WoWUnit { Guid = 99 }; return true; });
            Check(Tick(tree) == RunStatus.Failure && Moves.Count == 0, "requirements transferred behind movement to another target");
        }));
        cases.Add(("control behind: battleground exclusion remains", () =>
        {
            Reset(); Singular.SingularRoutine.CurrentWoWContext = Singular.WoWContext.Battlegrounds;
            try { Check(Tick(Movement.CreateMoveBehindTargetBehavior()) == RunStatus.Failure && Moves.Count == 0, "battleground behind movement enabled"); }
            finally { Singular.SingularRoutine.CurrentWoWContext = Singular.WoWContext.Normal; }
        }));
        cases.Add(("control behind: tank exclusion remains", () =>
        {
            Reset(); Group.MeIsTank = true;
            try { Check(Tick(Movement.CreateMoveBehindTargetBehavior()) == RunStatus.Failure && Moves.Count == 0, "tank behind movement enabled"); }
            finally { Group.MeIsTank = false; }
        }));
        cases.Add(("control behind: null requirements refuse without errors", () =>
        {
            Reset(); Check(Tick(Movement.CreateMoveBehindTargetBehavior(null!)) == RunStatus.Failure && Moves.Count == 0, "null requirements accepted");
        }));
""";
}
