using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Real MeshNavigator.MoveTo, controlled virtual player observations and counted
// mover callbacks. Arrival/direct-swim branches avoid native path generation.
// Managed request ownership is not a claim of native frame/session atomicity.
internal static class MeshMoveRequestOwnershipRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly WoWPoint Start = new(11, 22, 33), Target = new(44, 55, 66);
    private static readonly WoWPoint OldEnd = new(101, 102, 103), NewStart = new(81, 92, 103), NewEnd = new(84, 95, 106);
    private enum Boundary { OriginRead, CoreRead, AliveRead, ArrivalRead, ElevatorStop, SwimCommand }
    private enum Replacement { Path, Clear, Player, Mover, Provider, SameRequest }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Movement-request tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (Boundary value in Enum.GetValues<Boundary>())
        {
            var boundary = value;
            cases.Add(($"{boundary}: unchanged request retains ordinary result and one outcome", f => f.Stable(boundary)));
            foreach (Replacement kind in Enum.GetValues<Replacement>())
            {
                var replacement = kind;
                cases.Add(($"{boundary}: obsolete request preserves {replacement} replacement", f => f.Replace(boundary, replacement)));
            }
            foreach (string kind in new[] { "ordinary", "cancellation", "interruption" })
            {
                var k = kind;
                cases.Add(($"{boundary}: exact {k} observation or mover failure escapes without an outcome", f => f.Fault(boundary, Make(k))));
            }
        }
        cases.Add(("zero destination retains ordinary rejected outcome", f => f.Rejected(WoWPoint.Zero)));
        cases.Add(("nonfinite destination retains ordinary rejected outcome", f => f.Rejected(WoWPoint.Empty)));
        cases.Add(("missing player retains ordinary rejected outcome", f => f.MissingPlayer()));
        cases.Add(("one instance cannot invalidate another instance request", f => f.OtherInstance()));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS mesh move request: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL mesh move request assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR mesh move request fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Mesh move-request scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual MoveTo and controlled observation/mover boundaries; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Mesh move-request regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception Make(string kind) => kind == "cancellation" ? new OperationCanceledException("request cancellation")
        : kind == "interruption" ? new ThreadInterruptedException("request interruption") : new InvalidOperationException("request ordinary failure");
    private sealed class Player : LocalPlayer
    {
        internal Action? Callback;
        internal int Reads, Trigger;
        internal bool AliveTrigger;
        internal Player(uint address) : base(address) { }
        internal void Fire() { var callback = Callback; Callback = null; callback?.Invoke(); }
        public override WoWPoint Location { get { Reads++; if (Reads == Trigger) Fire(); return MeshMoveRequestOwnershipRegressionTests.Target; } }
        public override bool IsAlive { get { if (AliveTrigger) Fire(); return true; } }
    }
    private sealed class Mover : IPlayerMover
    {
        internal int Stops, Moves;
        internal Action? OnStop, OnMove;
        public void Move(WoWMovement.MovementDirection direction) => throw new InvalidOperationException("Native directional movement forbidden");
        public void MoveTowards(WoWPoint point) { Moves++; var callback = OnMove; OnMove = null; callback?.Invoke(); }
        public void MoveStop() { Stops++; var callback = OnStop; OnStop = null; callback?.Invoke(); }
    }
    private sealed class Stuck : StuckHandler
    {
        public override void Reset() { }
        public override bool IsStuck() => false;
        public override void Unstick() => throw new InvalidOperationException("Unstick forbidden");
    }
    private sealed class Snapshot
    {
        private readonly WoWPoint[] path;
        private readonly WoWPoint destination, origin, lastDestination;
        private readonly MoveResult? result;
        private readonly DateTime attempted;
        private readonly long sequence;
        private readonly ulong elevator;
        private readonly bool riding;
        internal Snapshot(Fixture f)
        {
            path = f.mesh.CurrentPath.ToArray(); destination = f.mesh.Destination;
            origin = f.mesh.LastMoveOrigin; lastDestination = f.mesh.LastMoveDestination; result = f.mesh.LastMoveResult;
            attempted = f.mesh.LastMoveAttemptUtc; sequence = f.mesh.LastMoveAttemptSequence;
            elevator = f.Selected; riding = f.mesh.IsRidingElevator;
        }
        internal bool Retained(Fixture f) => f.mesh.CurrentPath.SequenceEqual(path) && f.mesh.Destination == destination &&
            f.mesh.LastMoveOrigin == origin && f.mesh.LastMoveDestination == lastDestination && f.mesh.LastMoveResult == result &&
            f.mesh.LastMoveAttemptUtc == attempted && f.mesh.LastMoveAttemptSequence == sequence &&
            f.Selected == elevator && f.mesh.IsRidingElevator == riding;
    }
    private sealed class Fixture : IDisposable
    {
        private static readonly FieldInfo MoverField = typeof(Navigator).GetField("_playerMover", StaticHidden)!;
        private static readonly FieldInfo ProviderField = typeof(Navigator).GetField("_currentProvider", StaticHidden)!;
        private static readonly FieldInfo NativeField = typeof(Navigator).GetField("_navigator", StaticHidden)!;
        private readonly object source;
        private readonly object? oldMover, oldProvider, oldNative;
        private readonly LocalPlayer original;
        internal readonly MeshNavigator mesh = new();
        private readonly Mover mover = new();
        private readonly Player player;
        internal Fixture()
        {
            source = Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            original = ObjectManager.Me; player = new Player(original.BaseAddress);
            oldMover = MoverField.GetValue(null); oldProvider = ProviderField.GetValue(null); oldNative = NativeField.GetValue(null);
            try
            {
                MoverField.SetValue(null, mover); ProviderField.SetValue(null, mesh); NativeField.SetValue(null, null);
                mesh.StuckHandler = new Stuck(); ObjectManager.Me = player;
                mesh.OverrideCurrentPath(new[] { Start, OldEnd }); Set("_destination", OldEnd);
                if (!player.IsValid || ObjectManager.Executor != null || Navigator.IsNavigatorLoaded || player.IsSwimming)
                    throw new InvalidOperationException("Controlled movement-request fixture is not isolated");
                Record(Start, OldEnd);
            }
            catch { Dispose(); throw; }
        }
        private void Set(string name, object? value) => typeof(MeshNavigator).GetField(name, Hidden)!.SetValue(mesh, value);
        private object Elevator => typeof(MeshNavigator).GetField("_elevatorTransit", Hidden)!.GetValue(mesh)!;
        internal ulong Selected => (ulong)Elevator.GetType().GetProperty("SelectedTransportGuid", Hidden)!.GetValue(Elevator)!;
        private void Begin(ulong guid) { Elevator.GetType().GetMethod("Begin", Hidden)!.Invoke(Elevator, new object[] { guid, 123U, Start, OldEnd, Start, OldEnd }); Set("_ridingElevator", true); }
        private void Record(WoWPoint from, WoWPoint to) => typeof(MeshNavigator).GetMethod("RecordMoveOutcome", Hidden)!.Invoke(mesh, new object[] { from, to, MoveResult.Moved });
        private void Prepare(Boundary boundary, Action callback)
        {
            player.Reads = 0; player.Trigger = boundary == Boundary.OriginRead ? 1 : boundary == Boundary.CoreRead ? 2 : boundary == Boundary.ArrivalRead ? 3 : 0;
            player.AliveTrigger = boundary == Boundary.AliveRead;
            player.Callback = callback;
            if (boundary == Boundary.ElevatorStop) { Begin(555UL); player.Callback = null; mover.OnStop = callback; }
            if (boundary == Boundary.SwimCommand)
            {
                player.Callback = null;
                uint address = player.BaseAddress + 2608;
                Marshal.WriteInt32(new IntPtr(unchecked((int)address)), 2097152);
                var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)ObjectManager.Wow!.GetType().GetField("_cache", Hidden)!.GetValue(ObjectManager.Wow)!;
                cache.Value!.Remove(new IntPtr(unchecked((int)address)));
                if (!player.IsSwimming) throw new InvalidOperationException("Swimming flag was not observed");
                Set("_usingDirectSwimMovement", true); mover.OnMove = callback;
            }
        }
        internal void Stable(Boundary boundary)
        {
            bool invoked = false; long sequence = mesh.LastMoveAttemptSequence;
            Prepare(boundary, () => invoked = true);
            var result = mesh.MoveTo(Target);
            Check(invoked && result == (boundary == Boundary.SwimCommand ? MoveResult.Moved : MoveResult.ReachedDestination), "ordinary controlled movement result changed");
            Check(mesh.LastMoveAttemptSequence == sequence + 1 && mesh.LastMoveResult == result && mesh.LastMoveDestination == Target, "ordinary request did not publish one matching outcome");
            Check(mover.Moves == (boundary == Boundary.SwimCommand ? 1 : 0) && mover.Stops == (boundary == Boundary.ElevatorStop ? 1 : 0), "ordinary request added unexpected movement calls");
        }
        internal void Replace(Boundary boundary, Replacement replacement)
        {
            bool invoked = false; Snapshot? newer = null; Exception? callbackError = null; int stops = 0, moves = 0;
            Prepare(boundary, () =>
            {
                invoked = true;
                try
                {
                    if (replacement == Replacement.Path) { mesh.OverrideCurrentPath(new[] { NewStart, NewEnd }); Set("_destination", NewEnd); Record(NewStart, NewEnd); }
                    else if (replacement == Replacement.Clear) mesh.Clear();
                    else if (replacement == Replacement.Player) ObjectManager.Me = new Player(original.BaseAddress);
                    else if (replacement == Replacement.Mover) MoverField.SetValue(null, new Mover());
                    else if (replacement == Replacement.Provider) ProviderField.SetValue(null, new MeshNavigator());
                    else
                    {
                        // Same instance, player and requested point; only the request
                        // lifetime distinguishes this nested real public MoveTo.
                        var inner = mesh.MoveTo(Target);
                        if (inner != (boundary == Boundary.SwimCommand ? MoveResult.Moved : MoveResult.ReachedDestination))
                            throw new InvalidOperationException("Actual nested request did not complete its controlled branch");
                    }
                    newer = new Snapshot(this); stops = mover.Stops; moves = mover.Moves;
                }
                catch (Exception error) { callbackError = error; throw; }
            });
            var result = mesh.MoveTo(Target);
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();
            Check(invoked && newer != null, "replacement callback was not reached");
            Check(newer!.Retained(this), "old request changed replacement route or outcome history");
            Check(result == MoveResult.Failed, "obsolete request reported a successful move or arrival");
            Check(mover.Stops == stops && mover.Moves == moves, "old request continued movement after replacement");
        }
        internal void Fault(Boundary boundary, Exception signal)
        {
            bool invoked = false; long sequence = mesh.LastMoveAttemptSequence; Exception? caught = null;
            Prepare(boundary, () => { invoked = true; ExceptionDispatchInfo.Capture(signal).Throw(); });
            try { mesh.MoveTo(Target); } catch (Exception error) { caught = error; }
            Check(invoked && ReferenceEquals(caught, signal), "request changed or swallowed the exact boundary exception");
            Check(mesh.LastMoveAttemptSequence == sequence, "faulted request fabricated a completed outcome");
        }
        internal void Rejected(WoWPoint target)
        {
            long sequence = mesh.LastMoveAttemptSequence;
            Check(mesh.MoveTo(target) == MoveResult.Failed && mesh.LastMoveResult == MoveResult.Failed && mesh.LastMoveAttemptSequence == sequence + 1,
                "ordinary input rejection did not retain its failure outcome");
            Check(mover.Moves == 0 && mover.Stops == 0, "rejected input caused movement");
        }
        internal void MissingPlayer()
        {
            ObjectManager.Me = null; long sequence = mesh.LastMoveAttemptSequence;
            Check(mesh.MoveTo(Target) == MoveResult.Failed && mesh.LastMoveAttemptSequence == sequence + 1, "ordinary missing-player rejection changed");
            Check(mover.Moves == 0 && mover.Stops == 0, "missing player caused movement");
        }
        internal void OtherInstance()
        {
            var other = new MeshNavigator(); Prepare(Boundary.OriginRead, () => other.OverrideCurrentPath(new[] { NewStart, NewEnd }));
            Check(mesh.MoveTo(Target) == MoveResult.ReachedDestination && other.CurrentPath.SequenceEqual(new[] { NewStart, NewEnd }), "one instance invalidated another movement owner");
        }
        public void Dispose()
        {
            player.Callback = null; mover.OnStop = mover.OnMove = null;
            ObjectManager.Me = original; MoverField.SetValue(null, oldMover); ProviderField.SetValue(null, oldProvider); NativeField.SetValue(null, oldNative);
            ((IDisposable)source).Dispose();
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
