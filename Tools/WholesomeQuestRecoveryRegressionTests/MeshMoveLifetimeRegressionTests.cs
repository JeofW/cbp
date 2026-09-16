using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Calls the real public MoveTo through the unchanged, already executed W47
// descriptor-backed fixture. A retained wrapper is not a retained backing owner.
// No native/game effects; observed changes are not an unobserved ABA guarantee.
internal static class MeshMoveLifetimeRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WoWPoint Destination = new(44, 55, 66);
    private enum Change { AddressCleared, AddressRebound, MemoryRemoved }
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Movement lifetime tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>();
        foreach (string value in new[] { "OriginRead", "CoreRead", "AliveRead", "ArrivalRead", "ElevatorStop", "SwimCommand" })
        {
            var boundary = value;
            cases.Add(($"{boundary}: same-address refresh retains the request", f => f.Stable(boundary, false)));
            foreach (Change valueChange in Enum.GetValues<Change>())
            {
                var change = valueChange;
                cases.Add(($"{boundary}: observed {change} revokes the same-wrapper request", f => f.Invalidate(boundary, change)));
            }
        }
        foreach (string value in new[] { "ordinary", "cancellation", "interruption" })
        {
            var kind = value;
            cases.Add(($"invalidating callback retains exact {kind} exception", f => f.Fault(kind)));
        }
        cases.Add(("unrelated wrapper relocation cannot revoke this request", f => f.Stable("OriginRead", true)));
        cases.Add(("same memory-owner reassignment retains the request", f => f.SameMemory()));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS mesh move lifetime: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL mesh move lifetime assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR mesh move lifetime fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Mesh move-lifetime scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual MoveTo and backing-owner transitions; no game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException($"Mesh move-lifetime regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly Type Existing = typeof(MeshMoveRequestOwnershipRegressionTests);
        private static readonly Type ExistingFixture = Existing.GetNestedType("Fixture", BindingFlags.NonPublic)!;
        private static readonly Type ExistingBoundary = Existing.GetNestedType("Boundary", BindingFlags.NonPublic)!;
        private static readonly Type ExistingSnapshot = Existing.GetNestedType("Snapshot", BindingFlags.NonPublic)!;
        private static readonly PropertyInfo MemoryOwner = typeof(ObjectManager).GetProperty("Wow", StaticHidden)!;
        private static readonly MethodInfo UpdateAddress = typeof(WoWObject).GetMethod("UpdateBaseAddress", Hidden)!;
        private readonly object source = Activator.CreateInstance(ExistingFixture, true)!;
        private readonly object? memory;
        private readonly MeshNavigator mesh;
        private readonly LocalPlayer player;
        private readonly object mover;

        internal Fixture()
        {
            memory = MemoryOwner.GetValue(null);
            mesh = (MeshNavigator)Field(source, "mesh")!;
            player = (LocalPlayer)Field(source, "player")!;
            mover = Field(source, "mover")!;
            if (memory == null || player.BaseAddress == 0 || !ReferenceEquals(ObjectManager.Me, player))
                throw new InvalidOperationException("Existing movement fixture has no current backing owner");
        }
        private static object? Field(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner);
        private static object? Call(object owner, string name, params object[] arguments)
        {
            try { return owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        private void Prepare(string boundary, Action callback) => Call(source, "Prepare", Enum.Parse(ExistingBoundary, boundary), callback);
        private object Snapshot() => Activator.CreateInstance(ExistingSnapshot, Hidden, null, new[] { source }, null)!;
        private bool Retained(object snapshot) => (bool)Call(snapshot, "Retained", source)!;
        private int Moves => (int)Field(mover, "Moves")!;
        private int Stops => (int)Field(mover, "Stops")!;
        private void Rebind(LocalPlayer target, uint address) => UpdateAddress.Invoke(target, new object[] { address });
        private void Mutate(Change change)
        {
            if (change == Change.MemoryRemoved) MemoryOwner.SetValue(null, null);
            else Rebind(player, change == Change.AddressCleared ? 0U : player.BaseAddress + 4U);
            Check(ReferenceEquals(ObjectManager.Me, player), "test replaced the wrapper instead of its backing owner");
        }
        internal void Invalidate(string boundary, Change change)
        {
            bool invoked = false; object? after = null; int moves = -1, stops = -1;
            Prepare(boundary, () => { invoked = true; Mutate(change); after = Snapshot(); moves = Moves; stops = Stops; });
            var result = mesh.MoveTo(Destination);
            Check(invoked && after != null, "backing-owner callback was not reached");
            Check(Retained(after!), "obsolete request wrote route or outcome after backing-owner change");
            Check(result == MoveResult.Failed, "obsolete same-wrapper request reported successful movement");
            Check(Moves == moves && Stops == stops, "obsolete request dispatched movement after backing-owner change");
        }
        internal void Stable(string boundary, bool unrelated)
        {
            bool invoked = false; long sequence = mesh.LastMoveAttemptSequence;
            Prepare(boundary, () =>
            {
                invoked = true;
                if (unrelated) Rebind(new LocalPlayer(player.BaseAddress), 0U);
                else Rebind(player, player.BaseAddress);
            });
            var result = mesh.MoveTo(Destination);
            Check(invoked && result == (boundary == "SwimCommand" ? MoveResult.Moved : MoveResult.ReachedDestination), "stable backing owner lost ordinary movement result");
            Check(mesh.LastMoveAttemptSequence == sequence + 1 && mesh.LastMoveResult == result, "stable request lost its single outcome");
            Check(Moves == (boundary == "SwimCommand" ? 1 : 0) && Stops == (boundary == "ElevatorStop" ? 1 : 0), "stable request added movement effects");
        }
        internal void SameMemory()
        {
            bool invoked = false; long sequence = mesh.LastMoveAttemptSequence;
            Prepare("OriginRead", () => { invoked = true; MemoryOwner.SetValue(null, memory); });
            Check(mesh.MoveTo(Destination) == MoveResult.ReachedDestination && invoked && mesh.LastMoveAttemptSequence == sequence + 1,
                "same memory-owner reassignment invalidated stable work");
        }
        internal void Fault(string kind)
        {
            Exception expected = kind == "cancellation" ? new OperationCanceledException("backing-owner cancellation")
                : kind == "interruption" ? new ThreadInterruptedException("backing-owner interruption")
                : new InvalidOperationException("backing-owner ordinary error");
            Exception? caught = null; bool invoked = false; long sequence = mesh.LastMoveAttemptSequence;
            Prepare("OriginRead", () => { invoked = true; Mutate(Change.AddressCleared); ExceptionDispatchInfo.Capture(expected).Throw(); });
            try { mesh.MoveTo(Destination); } catch (Exception error) { caught = error; }
            Check(invoked && ReferenceEquals(caught, expected), "invalidating callback changed the exact exception");
            Check(mesh.LastMoveAttemptSequence == sequence && Moves == 0 && Stops == 0, "faulted request published an outcome or movement");
        }
        public void Dispose()
        {
            // Restore the real source fixture's memory before it drains its own
            // self-process allocation. No replacement memory/native owner is run.
            MemoryOwner.SetValue(null, memory);
            ((IDisposable)source).Dispose();
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
