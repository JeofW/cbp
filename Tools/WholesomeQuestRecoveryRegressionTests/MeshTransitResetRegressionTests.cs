using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

// Actual private transit cleanup owners and the real public destination-change
// entry. Controlled movement callbacks, not simulated cleanup implementations.
// No native movement, game, transport capture, terrain or mesh loading.
internal static class MeshTransitResetRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private enum Boundary { Connector, Elevator }
    private static readonly WoWPoint Start = new(11, 22, 33), End = new(44, 55, 66);
    private static readonly WoWPoint NewStart = new(81, 92, 103), NewEnd = new(84, 95, 106);
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Transit cleanup tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (Boundary value in Enum.GetValues<Boundary>())
        {
            var b = value;
            cases.Add(($"{b} stable cleanup detaches before its movement stop", f => f.Stable(b)));
            cases.Add(($"{b} cleanup retains unrelated managed state", f => f.Unrelated(b)));
            foreach (string kind in new[] { "ordinary", "cancellation", "interruption" })
            {
                var k = kind;
                cases.Add(($"{b} mover {k} preserves identity after detachment", f => f.Fault(b, Make(k))));
            }
            foreach (string kind in new[] { "none", "ordinary", "cancellation", "interruption" })
            {
                var k = kind;
                cases.Add(($"{b} replacement survives old stop, error={k}", f => f.Replace(b, Make(k), false)));
                cases.Add(($"{b} public Clear reentrancy retains replacement, error={k}", f => f.Replace(b, Make(k), true)));
            }
        }
        cases.Add(("inactive elevator cancellation does not stop an unrelated ground route", f => f.InactiveElevator()));
        cases.Add(("same requested destination retains the selected elevator", f => f.Destination(false, false)));
        cases.Add(("within-precision destination retains the selected elevator", f => f.Destination(false, true)));
        cases.Add(("changed requested destination drains elevator before stop", f => f.Destination(true, false)));
        cases.Add(("changed-destination stop callback retains a replacement elevator", f => f.DestinationReplacement()));
        cases.Add(("Clear empty public path replacement revokes later old handler reset", f => f.ClearGuard(0)));
        cases.Add(("Clear direct path-list mutation revokes later old handler reset", f => f.ClearGuard(1)));
        cases.Add(("Clear handler replacement revokes later old handler reset", f => f.ClearGuard(2)));
        cases.Add(("Clear mover replacement revokes later old handler reset", f => f.ClearGuard(3)));
        cases.Add(("Clear provider replacement revokes later old handler reset", f => f.ClearGuard(4)));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS mesh transit reset: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL mesh transit reset assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR mesh transit reset fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Mesh transit-reset scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual transit cleanup owners; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Mesh transit-reset regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception? Make(string kind) => kind == "none" ? null : kind == "cancellation"
        ? new OperationCanceledException("transit cancellation") : kind == "interruption"
        ? new ThreadInterruptedException("transit interruption") : new InvalidOperationException("transit ordinary error");
    private sealed class Mover : IPlayerMover
    {
        internal int Stops, Moves;
        internal Action? Callback;
        public void Move(WoWMovement.MovementDirection direction) { Moves++; throw new InvalidOperationException("Native move forbidden"); }
        public void MoveTowards(WoWPoint point) { Moves++; throw new InvalidOperationException("Native move forbidden"); }
        public void MoveStop() { Stops++; var callback = Callback; Callback = null; callback?.Invoke(); }
    }
    private sealed class Stuck : StuckHandler
    {
        internal int Resets;
        public override void Reset() => Resets++;
        public override bool IsStuck() => false;
        public override void Unstick() => throw new InvalidOperationException("Unstick forbidden");
    }
    private sealed class Fixture : IDisposable
    {
        private static readonly FieldInfo MoverField = typeof(Navigator).GetField("_playerMover", StaticHidden)!;
        private static readonly FieldInfo ProviderField = typeof(Navigator).GetField("_currentProvider", StaticHidden)!;
        private readonly object? previousMover = MoverField.GetValue(null), previousProvider = ProviderField.GetValue(null);
        private readonly MeshNavigator mesh = new();
        private readonly Mover mover = new();
        private readonly Stuck stuck = new();
        internal Fixture() { MoverField.SetValue(null, mover); mesh.StuckHandler = stuck; }
        private void Set(string name, object? value) => typeof(MeshNavigator).GetField(name, Hidden)!.SetValue(mesh, value);
        private T Get<T>(string name) => (T)typeof(MeshNavigator).GetField(name, Hidden)!.GetValue(mesh)!;
        private object Elevator => Get<object>("_elevatorTransit");
        private ulong Selected => (ulong)Elevator.GetType().GetProperty("SelectedTransportGuid", Hidden)!.GetValue(Elevator)!;
        private void Begin(ulong guid, WoWPoint a, WoWPoint z)
        {
            Elevator.GetType().GetMethod("Begin", Hidden)!.Invoke(Elevator, new object[] { guid, 123U, a, z, a, z });
            Set("_ridingElevator", true);
        }
        private void Seed(Boundary b)
        {
            mesh.OverrideCurrentPath(new[] { Start, End }); Set("_destination", End);
            if (b == Boundary.Connector) { Set("_localConnectorTarget", Start); Set("_localConnectorDestination", End); }
            else Begin(555UL, Start, End);
        }
        private bool Detached(Boundary b) => b == Boundary.Connector
            ? Get<WoWPoint>("_localConnectorTarget") == WoWPoint.Zero && Get<WoWPoint>("_localConnectorDestination") == WoWPoint.Zero
              && mesh.CurrentPath.Count == 0 && mesh.CurrentPathIndex == 0
            : Selected == 0UL && !mesh.IsRidingElevator;
        private object? Call(string name, params object[] arguments)
        {
            try { return typeof(MeshNavigator).GetMethod(name, Hidden)!.Invoke(mesh, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        private Exception? Cleanup(Boundary b)
        {
            try { Call(b == Boundary.Connector ? "ResetLocalConnector" : "CancelElevatorTransitMovement"); return null; }
            catch (Exception error) { return error; }
        }
        internal void Stable(Boundary b)
        {
            Seed(b); bool detached = false; mover.Callback = () => detached = Detached(b);
            Check(Cleanup(b) == null && Detached(b), "stable cleanup retained its transit state");
            Check(detached, "transit state remained live at the movement callback");
            Check(mover.Stops == 1 && mover.Moves == 0 && stuck.Resets == 0, "cleanup fabricated or duplicated unrelated effects");
        }
        internal void Fault(Boundary b, Exception? error)
        {
            Seed(b); bool detached = false;
            mover.Callback = () => { detached = Detached(b); ExceptionDispatchInfo.Capture(error!).Throw(); };
            Check(ReferenceEquals(Cleanup(b), error), "cleanup changed the exact mover exception");
            Check(detached && Detached(b), "mover exception stranded transit state");
            Check(mover.Stops == 1 && mover.Moves == 0, "faulted cleanup duplicated a movement effect");
        }
        internal void Replace(Boundary b, Exception? error, bool clearFirst)
        {
            Seed(b); bool invoked = false, detached = false; Exception? nestedError = null;
            mover.Callback = () =>
            {
                invoked = true; detached = Detached(b);
                if (clearFirst) { try { mesh.Clear(); } catch (Exception ex) { nestedError = ex; } }
                mesh.OverrideCurrentPath(new[] { NewStart, NewEnd }); Set("_destination", NewEnd);
                if (b == Boundary.Connector) { Set("_localConnectorTarget", NewStart); Set("_localConnectorDestination", NewEnd); }
                else Begin(999UL, NewStart, NewEnd);
                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            };
            var caught = Cleanup(b);
            Check(invoked && detached, "replacement observed an undetached old transit");
            Check(ReferenceEquals(caught, error) && nestedError == null, "replacement lost exception or nested cleanup identity");
            Check(mesh.CurrentPath.SequenceEqual(new[] { NewStart, NewEnd }) && mesh.Destination == NewEnd,
                "old transit cleanup overwrote a replacement route");
            Check(b == Boundary.Connector ? Get<WoWPoint>("_localConnectorTarget") == NewStart && Get<WoWPoint>("_localConnectorDestination") == NewEnd
                : Selected == 999UL && mesh.IsRidingElevator, "old cleanup consumed replacement transit state");
            Check(mover.Stops == 1 && mover.Moves == 0, "nested public Clear repeated the old movement stop");
        }
        internal void Unrelated(Boundary b)
        {
            Seed(b); Set("_currentAvoidPath", new[] { NewStart, NewEnd });
            if (b == Boundary.Connector) Begin(777UL, NewStart, NewEnd);
            else { Set("_localConnectorTarget", NewStart); Set("_localConnectorDestination", NewEnd); }
            var sequence = mesh.LastMoveAttemptSequence;
            Check(Cleanup(b) == null && Detached(b), "ordinary transit cleanup failed");
            Check(mesh.Destination == End && Get<WoWPoint[]>("_currentAvoidPath").SequenceEqual(new[] { NewStart, NewEnd })
                && mesh.LastMoveAttemptSequence == sequence, "scoped transit cleanup erased unrelated state");
            Check(b == Boundary.Connector ? Selected == 777UL && mesh.IsRidingElevator
                : Get<WoWPoint>("_localConnectorTarget") == NewStart && mesh.CurrentPath.SequenceEqual(new[] { Start, End }),
                "one transit cleanup consumed another subsystem");
        }
        internal void InactiveElevator()
        {
            mesh.OverrideCurrentPath(new[] { Start, End });
            Check(Cleanup(Boundary.Elevator) == null && mover.Stops == 0 && mesh.CurrentPath.SequenceEqual(new[] { Start, End }),
                "inactive elevator cleanup stopped an unrelated path");
        }
        internal void Destination(bool changed, bool near)
        {
            Seed(Boundary.Elevator); bool detached = false; mover.Callback = () => detached = Detached(Boundary.Elevator);
            var target = changed ? NewEnd : near ? End.Add(0.5f, 0, 0) : End;
            bool result = (bool)Call("CancelElevatorTransitIfDestinationChanged", target)!;
            Check(result == changed, "destination-change admission changed");
            Check(changed ? detached && Detached(Boundary.Elevator) && mover.Stops == 1
                : Selected == 555UL && mesh.IsRidingElevator && mover.Stops == 0, "destination-change cleanup violated owned transit state");
            Check(mesh.CurrentPath.SequenceEqual(new[] { Start, End }), "elevator cancellation erased the ground route");
        }
        internal void DestinationReplacement()
        {
            Seed(Boundary.Elevator); mover.Callback = () => { mesh.OverrideCurrentPath(new[] { NewStart, NewEnd }); Begin(999UL, NewStart, NewEnd); };
            Check((bool)Call("CancelElevatorTransitIfDestinationChanged", NewEnd)!, "changed destination lost admission");
            Check(Selected == 999UL && mesh.IsRidingElevator && mesh.CurrentPath.SequenceEqual(new[] { NewStart, NewEnd }),
                "destination-change cleanup erased a replacement elevator");
        }
        internal void ClearGuard(int kind)
        {
            Seed(Boundary.Connector); var replacementHandler = new Stuck(); var replacementMover = new Mover();
            var replacementProvider = new MeshNavigator();
            mover.Callback = () =>
            {
                if (kind == 0) mesh.OverrideCurrentPath(Array.Empty<WoWPoint>());
                else if (kind == 1) mesh.CurrentPath.Add(NewStart);
                else if (kind == 2) mesh.StuckHandler = replacementHandler;
                else if (kind == 3) Navigator.PlayerMover = replacementMover;
                else ProviderField.SetValue(null, replacementProvider); // No native provider activation.
            };
            Check(mesh.Clear() && mover.Stops == 1 && mover.Moves == 0, "guard control lost ordinary cleanup");
            Check(stuck.Resets == 0 && replacementHandler.Resets == 0 && replacementMover.Stops == 0,
                "obsolete Clear reset a later owner after the mover callback");
            if (kind == 1) Check(mesh.CurrentPath.SequenceEqual(new[] { NewStart }), "Clear erased direct replacement path mutation");
        }
        public void Dispose() { mover.Callback = null; MoverField.SetValue(null, previousMover); ProviderField.SetValue(null, previousProvider); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
