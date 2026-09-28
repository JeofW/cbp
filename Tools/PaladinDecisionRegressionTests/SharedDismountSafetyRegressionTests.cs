using Styx;
using Styx.WoWInternals;
using TreeSharp;
using Shared = Singular.Helpers.Common;

// Execute the complete production helper and TreeSharp. Shortening a Wait's
// public timeout exercises its real expiry branch without a thirty-second sleep.
internal static class SharedDismountSafetyRegressionTests
{
    private sealed class AssertionFailure : Exception
    { internal AssertionFailure(string message) : base(message) { } }

    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Body)>();
        foreach (var form in Enum.GetValues<ShapeshiftForm>())
        {
            var selectedForm = form;
            cases.Add(($"{form}: expired descent never removes flight", () =>
            {
                Setup(selectedForm, flying: true);
                var root = Shared.CreateDismount("regression timeout");
                foreach (var wait in Descendants(root).OfType<Wait>()) wait.Timeout = TimeSpan.Zero;
                Run(root, RunStatus.Failure);
                Check(!HasRemoval(), "airborne timeout issued mount/form removal");
                Check(DismountBoundary.Commands.SequenceEqual(new[] { "descend", "stop-descend" }), "descent was not stopped on timeout");
            }));
            cases.Add(($"{form}: observed landing permits one removal", () =>
            {
                Setup(selectedForm, flying: true);
                var root = Shared.CreateDismount("regression landing");
                root.Start(null!);
                try
                {
                    Check(root.Tick(null!) == RunStatus.Running, "descent did not wait for landing");
                    Check(!HasRemoval(), "removed flight before landing observation");
                    StyxWoW.Me.IsFlying = false;
                    Check(root.Tick(null!) == RunStatus.Success, "observed landing did not continue");
                    Check(DismountBoundary.Commands.SequenceEqual(new[] { "descend", "stop-descend", Removal(selectedForm) }), "landing command order changed");
                }
                finally { root.Stop(null!); }
            }));
            cases.Add(($"{form}: ground dismount remains available", () =>
            {
                Setup(selectedForm);
                Run(Shared.CreateDismount("regression ground"), RunStatus.Success);
                Check(DismountBoundary.Commands.SequenceEqual(new[] { Removal(selectedForm) }), "ground removal changed");
            }));
        }
        foreach (string state in new[] { "falling", "transport", "invalid", "dead", "unknown-guid" })
        {
            string change = state;
            cases.Add(("unsafe admission/" + change, () =>
            {
                Setup();
                Change(change);
                Run(Shared.CreateDismount("regression admission"), RunStatus.Failure);
                Check(DismountBoundary.Commands.Count == 0, "unsafe actor received movement/removal commands");
            }));
        }
        foreach (string state in new[] { "player-reference", "player-guid", "falling", "transport", "invalid", "dead" })
        {
            string change = state;
            cases.Add(("landing revalidates/" + change, () =>
            {
                Setup(flying: true);
                var root = Shared.CreateDismount("regression owner");
                root.Start(null!);
                try
                {
                    Check(root.Tick(null!) == RunStatus.Running, "expected pending descent");
                    StyxWoW.Me.IsFlying = false;
                    Change(change);
                    Check(root.Tick(null!) == RunStatus.Failure, "stale/unsafe landing continued");
                    Check(!HasRemoval(), "stale/unsafe landing issued removal");
                }
                finally { root.Stop(null!); }
            }));
        }
        cases.Add(("flight resumed during descent cleanup revokes removal", () =>
        {
            Setup(flying: true);
            var root = Shared.CreateDismount("regression final observation");
            root.Start(null!);
            try
            {
                Check(root.Tick(null!) == RunStatus.Running, "expected pending descent");
                StyxWoW.Me.IsFlying = false;
                DismountBoundary.AfterCommand = command => { if (command == "stop-descend") StyxWoW.Me.IsFlying = true; };
                Check(root.Tick(null!) == RunStatus.Failure, "late flight observation was ignored");
                Check(!HasRemoval(), "flight was removed after cleanup changed state");
            }
            finally { root.Stop(null!); }
        }));
        cases.Add(("timeout prevents the caller's following action", () =>
        {
            Setup(flying: true);
            bool continued = false;
            var root = new Sequence(Shared.CreateDismount("regression caller"),
                new TreeSharp.Action(_ => { continued = true; return RunStatus.Success; }));
            foreach (var wait in Descendants(root).OfType<Wait>()) wait.Timeout = TimeSpan.Zero;
            Run(root, RunStatus.Failure);
            Check(!continued && !HasRemoval(), "timeout became successful caller setup");
        }));
        cases.Add(("already dismounted is a successful no-op", () =>
        {
            Setup(); StyxWoW.Me.Mounted = false;
            Run(Shared.CreateDismount("regression no-op"), RunStatus.Success);
            Check(DismountBoundary.Commands.Count == 0, "no-op issued a command");
        }));
        cases.Add(("cancelling descent releases its command exactly once", () =>
        {
            Setup(flying: true);
            var root = Shared.CreateDismount("regression cancellation");
            root.Start(null!);
            Check(root.Tick(null!) == RunStatus.Running, "expected pending descent");
            root.Stop(null!); root.Stop(null!);
            Check(DismountBoundary.Commands.SequenceEqual(new[] { "descend", "stop-descend" }), "cancelled descent was left active or stopped twice");
            Check(!HasRemoval(), "cancellation removed flight");
        }));
        cases.Add(("cancellation never sends movement to a replacement actor", () =>
        {
            Setup(flying: true);
            var root = Shared.CreateDismount("regression foreign actor");
            root.Start(null!);
            Check(root.Tick(null!) == RunStatus.Running, "expected pending descent");
            Change("player-reference"); root.Stop(null!);
            Check(DismountBoundary.Commands.SequenceEqual(new[] { "descend" }), "cleanup commanded a replacement actor");
        }));
        cases.Add(("actor replacement during initial stop revokes remaining setup", () =>
        {
            Setup(flying: true); StyxWoW.Me.IsMoving = true;
            DismountBoundary.AfterCommand = command => { if (command == "stop") Change("player-reference"); };
            var root = Shared.CreateDismount("regression moving actor");
            foreach (var wait in Descendants(root).OfType<Wait>()) wait.Timeout = TimeSpan.Zero;
            Run(root, RunStatus.Failure);
            Check(DismountBoundary.Commands.SequenceEqual(new[] { "stop" }), "old setup acted on a replacement player");
        }));
        foreach (string state in new[] { "unavailable", "short-fall", "far-fall", "both-fall-flags" })
        {
            string observation = state;
            cases.Add(("ground permission requires complete state/" + observation, () =>
            {
                Setup();
                if (observation == "unavailable") StyxWoW.Me.MovementObservationAvailable = false;
                // Independent original-build inputs: root=0x800, fall=0x1000,
                // far fall=0x2000; do not derive the oracle from the host enum.
                else StyxWoW.Me.ObservedMovementFlags = observation == "short-fall" ? 0x1000U
                    : observation == "far-fall" ? 0x2000U : 0x3000U;
                Run(Shared.CreateDismount("regression complete observation"), RunStatus.Failure);
                Check(!HasRemoval(), "incomplete or airborne state removed flight");
            }));
        }
        cases.Add(("observation failure during cleanup revokes removal", () =>
        {
            Setup(flying: true);
            var root = Shared.CreateDismount("regression late read failure");
            root.Start(null!);
            try
            {
                Check(root.Tick(null!) == RunStatus.Running, "expected pending descent");
                StyxWoW.Me.IsFlying = false;
                DismountBoundary.AfterCommand = command => { if (command == "stop-descend") StyxWoW.Me.MovementObservationAvailable = false; };
                Check(root.Tick(null!) == RunStatus.Failure && !HasRemoval(), "failed state observation authorized removal");
            }
            finally { root.Stop(null!); }
        }));
        cases.Add(("ordinary ground motion is not an airborne prohibition", () =>
        {
            Setup(); StyxWoW.Me.ObservedMovementFlags = 1;
            Run(Shared.CreateDismount("regression motion"), RunStatus.Success);
            Check(HasRemoval(), "unrelated movement bit denied grounded removal");
        }));
        cases.Add(("root alone does not mean an airborne actor", () =>
        {
            Setup(); StyxWoW.Me.ObservedMovementFlags = 0x800;
            Run(Shared.CreateDismount("regression rooted ground"), RunStatus.Success);
            Check(HasRemoval(), "root flag was mistaken for falling");
        }));
        foreach (var selected in new[] { ShapeshiftForm.FlightForm, ShapeshiftForm.EpicFlightForm })
        {
            var form = selected;
            cases.Add(($"{form}: grounded form removal does not require a mount display", () =>
            {
                Setup(form); StyxWoW.Me.Mounted = false;
                Run(Shared.CreateDismount("regression flight form"), RunStatus.Success);
                Check(DismountBoundary.Commands.SequenceEqual(new[] { Removal(form) }), "flight form was silently treated as already dismounted");
            }));
            cases.Add(($"{form}: airborne form with no mount display still lands first", () =>
            {
                Setup(form, flying: true); StyxWoW.Me.Mounted = false;
                var root = Shared.CreateDismount("regression unmounted form landing");
                root.Start(null!);
                try
                {
                    Check(root.Tick(null!) == RunStatus.Running && !HasRemoval(), "flight form did not retain its landing wait");
                    StyxWoW.Me.IsFlying = false;
                    Check(root.Tick(null!) == RunStatus.Success, "landed flight form did not complete");
                    Check(DismountBoundary.Commands.SequenceEqual(new[] { "descend", "stop-descend", Removal(form) }), "landed flight form was not cancelled exactly once");
                }
                finally { root.Stop(null!); }
            }));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try
            {
                test.Body();
                Check(Fixture.Exceptions.Count == 0, "helper swallowed an exception");
                passed++; Console.WriteLine("PASS shared dismount: " + test.Name);
            }
            catch (AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL shared dismount: " + test.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR shared dismount: " + test.Name + ": " + e); }
            finally { DismountBoundary.Enabled = false; DismountBoundary.AfterCommand = null; }
        }
        Console.WriteLine($"Shared dismount scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; real shared helper/TreeSharp; controlled movement observations; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Shared dismount regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void Setup(ShapeshiftForm form = ShapeshiftForm.None, bool flying = false)
    {
        Fixture.Reset(); StyxWoW.Me.Guid = 101; StyxWoW.Me.Mounted = true;
        StyxWoW.Me.IsFlying = flying; StyxWoW.Me.Shapeshift = form;
        DismountBoundary.Enabled = true; DismountBoundary.Commands.Clear(); DismountBoundary.AfterCommand = null;
    }
    private static void Change(string state)
    {
        switch (state)
        {
            case "falling": StyxWoW.Me.IsFalling = true; break;
            case "transport": StyxWoW.Me.IsOnTransport = true; break;
            case "invalid": StyxWoW.Me.IsValid = false; break;
            case "dead": StyxWoW.Me.IsAlive = false; break;
            case "unknown-guid": StyxWoW.Me.Guid = 0; break;
            case "player-guid": StyxWoW.Me.Guid++; break;
            case "player-reference": StyxWoW.Me = new Player { Guid = 102, Mounted = true }; break;
        }
    }
    private static IEnumerable<Composite> Descendants(Composite root)
    {
        yield return root;
        if (root is GroupComposite group)
            foreach (var child in group.Children)
                foreach (var node in Descendants(child)) yield return node;
    }
    private static void Run(Composite root, RunStatus expected)
    {
        root.Start(null!);
        try { Check(root.Tick(null!) == expected, "unexpected completion status"); }
        finally { root.Stop(null!); }
    }
    private static string Removal(ShapeshiftForm form) => form == ShapeshiftForm.None ? "Dismount()" : "RunMacroText('/cancelform')";
    private static bool HasRemoval() => DismountBoundary.Commands.Any(c => c == "Dismount()" || c.Contains("cancelform"));
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
