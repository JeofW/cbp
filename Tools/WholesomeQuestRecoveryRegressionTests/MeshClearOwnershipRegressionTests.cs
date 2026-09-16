using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

// Real MeshNavigator.Clear, real managed route/elevator state, counted mover and
// stuck-handler boundaries. No mesh load, native movement, game or path search.
internal static class MeshClearOwnershipRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly WoWPoint Start = new(10, 20, 30), End = new(40, 50, 60);
    private static readonly WoWPoint[] Replacement = { new(71, 82, 93), new(74, 85, 96) };
    private enum Mode { Empty, Path, Connector, Elevator, Both, Swimming }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Mesh cleanup tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (Mode mode in Enum.GetValues<Mode>())
        {
            var m = mode;
            cases.Add(($"stable {m} drains managed state and stops at most once", f => f.Stable(m)));
        }
        foreach (Mode mode in new[] { Mode.Connector, Mode.Elevator, Mode.Both })
        {
            var m = mode;
            foreach (string kind in new[] { "ordinary", "cancellation", "interruption" })
            {
                var k = kind;
                cases.Add(($"{m} stuck-handler {k} retains cleanup and stop semantics", f => f.Faults(m, Make(k), null)));
                cases.Add(($"{m} mover {k} cannot strand managed state", f => f.Faults(m, null, Make(k))));
            }
            foreach (bool inMover in new[] { false, true })
            {
                var boundary = inMover;
                foreach (bool throwing in new[] { false, true })
                {
                    var throws = throwing;
                    cases.Add(($"{m} {(boundary ? "mover" : "stuck")} replacement survives cleanup, throwing={throws}", f => f.Replace(m, boundary, throws)));
                }
            }
        }
        foreach (var pair in new[] { ("cancellation", "interruption"), ("interruption", "cancellation"), ("ordinary", "cancellation"), ("cancellation", "ordinary") })
        {
            var p = pair;
            cases.Add(($"first observed stop survives stuck={p.Item1}, mover={p.Item2}", f => f.Faults(Mode.Both, Make(p.Item1), Make(p.Item2))));
        }
        cases.Add(("nested clear from stuck callback does not revive old route state", f => f.Nested(false)));
        cases.Add(("nested clear from mover callback does not revive old route state", f => f.Nested(true)));
        cases.Add(("null legacy stuck handler retains cleanup compatibility", f => f.NullHandler()));
        cases.Add(("mover replacement is not stopped by the old cleanup", f => f.ReplaceMover()));
        cases.Add(("clearing one mesh cannot drain another mesh's path", f => f.OtherMesh()));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS mesh clear ownership: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL mesh clear ownership assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR mesh clear ownership fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Mesh clear-ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual MeshNavigator.Clear; controlled mover/stuck callbacks; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Mesh clear-ownership regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static Exception Make(string kind) => kind == "cancellation" ? new OperationCanceledException("mesh cancel")
        : kind == "interruption" ? new ThreadInterruptedException("mesh interruption") : new InvalidOperationException("mesh ordinary error");
    private static bool Stop(Exception error) => error is OperationCanceledException or ThreadInterruptedException;
    private sealed class Mover : IPlayerMover
    {
        internal int Stops, Moves, ObsoleteStops;
        internal bool ReplacementPublished;
        internal Action? Callback;
        public void Move(WoWMovement.MovementDirection direction) { Moves++; throw new InvalidOperationException("No native movement is permitted"); }
        public void MoveTowards(WoWPoint point) { Moves++; throw new InvalidOperationException("No native movement is permitted"); }
        public void MoveStop()
        {
            Stops++; if (ReplacementPublished) ObsoleteStops++;
            var callback = Callback; Callback = null; callback?.Invoke();
        }
    }
    private sealed class Stuck : StuckHandler
    {
        internal int Resets, ObsoleteResets;
        internal bool ReplacementPublished;
        internal Action? Callback;
        public override bool IsStuck() => false;
        public override void Unstick() => throw new InvalidOperationException("No unstick dispatch is permitted");
        public override void Reset()
        {
            Resets++; if (ReplacementPublished) ObsoleteResets++;
            var callback = Callback; Callback = null; callback?.Invoke();
        }
    }
    private sealed class Fixture : IDisposable
    {
        private static readonly FieldInfo MoverField = typeof(Navigator).GetField("_playerMover", StaticHidden)!;
        private readonly object? previousMover = MoverField.GetValue(null);
        private readonly MeshNavigator mesh = new();
        private readonly Mover mover = new();
        private readonly Stuck stuck = new();
        private readonly List<Exception> observed = new();
        internal Fixture() { MoverField.SetValue(null, mover); mesh.StuckHandler = stuck; }
        private void Set(string field, object? value) => typeof(MeshNavigator).GetField(field, Hidden)!.SetValue(mesh, value);
        private T Get<T>(string field) => (T)typeof(MeshNavigator).GetField(field, Hidden)!.GetValue(mesh)!;
        private object Elevator => Get<object>("_elevatorTransit");
        private ulong Selected => (ulong)Elevator.GetType().GetProperty("SelectedTransportGuid", Hidden)!.GetValue(Elevator)!;
        private void Seed(Mode mode)
        {
            if (mode != Mode.Empty)
            {
                mesh.OverrideCurrentPath(new[] { Start, End }); Set("_destination", End);
                mesh.CurrentAvoidPath = new[] { Start, End }; mesh.CurrentAvoidPathIndex = 1;
                Set("_isPartialPath", true); Set("_cachedPushAheadIndex", 1);
                Set("<LastMoveResult>k__BackingField", (MoveResult?)MoveResult.Moved);
                Set("<LastMoveOrigin>k__BackingField", Start); Set("<LastMoveDestination>k__BackingField", End);
                Set("<LastMoveAttemptUtc>k__BackingField", DateTime.UtcNow);
            }
            if (mode == Mode.Connector || mode == Mode.Both) Set("_localConnectorTarget", Start);
            if (mode == Mode.Elevator || mode == Mode.Both)
            {
                Elevator.GetType().GetMethod("Begin", Hidden)!.Invoke(Elevator, new object[] { 555UL, 123U, Start, End, Start, End });
                Set("_ridingElevator", true);
            }
            if (mode == Mode.Swimming) Set("_usingDirectSwimMovement", true);
        }
        private bool Drained => !mesh.HasActivePath && mesh.CurrentPath.Count == 0 && mesh.CurrentPathIndex == 0 &&
            mesh.Destination == WoWPoint.Zero && !mesh.IsRidingElevator && Selected == 0 &&
            mesh.CurrentAvoidPath == null && mesh.CurrentAvoidPathIndex == 0 &&
            Get<WoWPoint>("_localConnectorTarget") == WoWPoint.Zero && !Get<bool>("_usingDirectSwimMovement") &&
            mesh.LastMoveResult == null && mesh.LastMoveAttemptUtc == DateTime.MinValue;
        private static bool NeedsStop(Mode mode) => mode == Mode.Connector || mode == Mode.Elevator || mode == Mode.Both;
        private Exception? Clear()
        {
            try { Check(mesh.Clear(), "Clear lost its successful boolean contract"); return null; }
            catch (AssertionFailure) { throw; }
            catch (Exception error) { return error; }
        }
        private void Throw(Exception? error) { if (error != null) { observed.Add(error); ExceptionDispatchInfo.Capture(error).Throw(); } }
        internal void Stable(Mode mode)
        {
            Seed(mode); var sequence = mesh.LastMoveAttemptSequence; bool beforeStuck = false, beforeMove = !NeedsStop(mode);
            stuck.Callback = () => beforeStuck = Drained; mover.Callback = () => beforeMove = Drained;
            Check(Clear() == null && Drained, "ordinary cleanup retained route state");
            Check(beforeStuck && beforeMove, "managed route state was not detached before external cleanup");
            Check(mover.Stops == (NeedsStop(mode) ? 1 : 0), "cleanup duplicated or fabricated movement stop");
            Check(stuck.Resets == 1 && mover.Moves == 0 && mesh.LastMoveAttemptSequence == sequence, "cleanup changed unrelated execution history");
            Check(Clear() == null && Drained && mover.Stops == (NeedsStop(mode) ? 1 : 0), "repeated cleanup revived movement");
        }
        internal void Faults(Mode mode, Exception? stuckError, Exception? moveError)
        {
            Seed(mode); stuck.Callback = () => Throw(stuckError); mover.Callback = () => Throw(moveError);
            var caught = Clear();
            // Ordinary stuck-handler Reset errors historically do not escape;
            // cancellation/interruption must not be swallowed. First observed
            // stop wins regardless of cleanup ordering, then mover failure.
            var expected = observed.FirstOrDefault(Stop) ?? moveError;
            Check(ReferenceEquals(caught, expected), "cleanup lost the observed first stop/error identity");
            Check(Drained, "throwing cleanup stranded an active route or elevator");
            Check(mover.Stops == 1 && stuck.Resets == 1 && mover.Moves == 0, "cleanup did not drain each admitted boundary once");
        }
        internal void Replace(Mode mode, bool inMover, bool throwing)
        {
            Seed(mode); bool invoked = false, detached = false; var signal = throwing ? Make("cancellation") : null;
            Action callback = () =>
            {
                invoked = true; detached = Drained;
                mesh.OverrideCurrentPath(Replacement); mesh.CurrentAvoidPath = Replacement;
                mover.ReplacementPublished = stuck.ReplacementPublished = true;
                Throw(signal);
            };
            if (inMover) mover.Callback = callback; else stuck.Callback = callback;
            var caught = Clear();
            Check(invoked && detached, "replacement callback ran before old managed state was detached");
            Check(ReferenceEquals(caught, signal), "replacement changed exact cancellation identity");
            Check(mesh.CurrentPath.SequenceEqual(Replacement) && ReferenceEquals(mesh.CurrentAvoidPath, Replacement), "obsolete cleanup overwrote a replacement path");
            Check(mover.ObsoleteStops == 0 && stuck.ObsoleteResets == 0 && mover.Moves == 0, "old cleanup resumed destructive callbacks after replacement");
        }
        internal void Nested(bool inMover)
        {
            Seed(Mode.Both); int nested = 0; bool detached = false; Exception? innerError = null;
            Action callback = () => { nested++; detached = Drained; innerError = Clear(); };
            if (inMover) mover.Callback = callback; else stuck.Callback = callback;
            var caught = Clear();
            Check(caught == null && innerError == null && nested == 1 && detached && Drained, "nested cleanup consumed undetached state or failed");
            Check(mover.Stops <= 1 && mover.Moves == 0, "nested cleanup repeated the old movement stop");
        }
        internal void NullHandler()
        { Seed(Mode.Both); mesh.StuckHandler = null!; Check(Clear() == null && Drained && mover.Stops == 1, "null handler broke legacy cleanup"); }
        internal void ReplaceMover()
        {
            Seed(Mode.Both); var replacement = new Mover();
            stuck.Callback = () => MoverField.SetValue(null, replacement);
            Check(Clear() == null && Drained && replacement.Stops == 0 && replacement.Moves == 0, "old cleanup issued stop through a replacement mover");
        }
        internal void OtherMesh()
        {
            Seed(Mode.Path); var other = new MeshNavigator(); other.OverrideCurrentPath(Replacement);
            Check(Clear() == null && Drained && other.CurrentPath.SequenceEqual(Replacement), "one mesh cleared another instance");
        }
        public void Dispose() { mover.Callback = null; stuck.Callback = null; MoverField.SetValue(null, previousMover); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
