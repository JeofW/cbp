using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

internal static class MovementLatencyRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestChangedDestinationBypassesRetryThrottle();
        TestUnchangedPartialPathIsReusedUntilItsEndpoint();
        TestCachedWeightSetDoesNotQueryLiveTalentState();
        TestConfiguredStuckHandlerReceivesLifecycle();
        TestThrowingStuckHandlerRemovalKeepsLifecycleConsistent();
        TestLiveCollisionProbeExclusions();
        TestLiveCollisionNeedsTwoConsistentHits();
        TestConfirmedLiveCollisionClearsPathAndAddsBlackspot();
        TestUnstickGraceSuppressesImmediateDrift();
        TestOvershotWaypointAdvancesWithoutBacktracking();
        TestLookaheadCoversTheObservedMovementPulseGap();
        TestVerticalMeshStepsDoNotConsumeGroundLookahead();
        TestDownhillMeshStepsConsumeFullSafeLookahead();
        TestCornerLookaheadRetainsAReachablePointBeyondTheTurn();
        TestLookaheadValidationStartsFromThePlayer();
        TestLookaheadCannotCollapseOntoThePlayer();
        TestNearbyVerticalTransportBuildsAnElevatorShortcut();
        TestSyntheticElevatorReusesDestinationSideMeshLanding();
        TestAnimatedElevatorUsesValidatedLiveMatrixTranslation();
        TestClearingNavigationStopsActiveElevatorMovement();
        TestChangingDestinationStopsActiveElevatorMovement();
        TestLeavingDirectSwimMovementInvalidatesTheStaleGroundPath();
        TestTimedMovementScheduleExpiresWithoutBlockingTheBotThread();
        TestGenericStuckRecoveryRequiresPersistentGroundMovementFailure();
        TestSlowSharedPulseBreakdownIsRateLimited();
        TestSlowPluginPulseDiagnosticsAreRateLimitedPerPlugin();
        TestRuntimeCompilerDoesNotReferencePreviousTemporaryPlugins();
        TestSlowPathDiagnosticThreshold();
        TestNavigatorTileLogsAreBatched();
    }

    private static void TestConfiguredStuckHandlerReceivesLifecycle()
    {
        var navigator = new MeshNavigator();
        var handler = new TrackingStuckHandler();
        navigator.StuckHandler = handler;

        navigator.OnSetAsCurrent();
        try
        {
            Assert(handler.SetAsCurrentCount == 1,
                "the active MeshNavigator must activate its configured stuck handler");
        }
        finally
        {
            navigator.OnRemoveAsCurrent();
        }

        Assert(handler.RemoveAsCurrentCount == 1,
            "removing MeshNavigator must detach its configured stuck handler");
    }

    private static void TestLiveCollisionNeedsTwoConsistentHits()
    {
        var tracker = new LiveCollisionTracker();
        var firstHit = new WoWPoint(2f, 0f, 0f);
        var matchingHit = new WoWPoint(2.5f, 0f, 0f);

        Assert(!tracker.Observe(traceHit: true, firstHit, hitDistance: 2f, probeDistance: 6f),
            "one live collision sample must not reroute ordinary transient contact");
        Assert(tracker.Observe(traceHit: true, matchingHit, hitDistance: 2.5f, probeDistance: 6f),
            "two nearby live collision samples must request a route around the obstruction");

        Assert(!tracker.Observe(traceHit: false, WoWPoint.Zero, hitDistance: 0f, probeDistance: 6f),
            "a clear probe must reset collision confirmation");
        Assert(!tracker.Observe(traceHit: true, firstHit, hitDistance: 2f, probeDistance: 6f),
            "a collision after a clear probe must start a new confirmation sequence");
    }

    private static void TestThrowingStuckHandlerRemovalKeepsLifecycleConsistent()
    {
        var navigator = new MeshNavigator();
        var throwingHandler = new ThrowingRemoveStuckHandler();
        var replacementHandler = new TrackingStuckHandler();
        navigator.StuckHandler = throwingHandler;
        navigator.OnSetAsCurrent();

        navigator.StuckHandler = replacementHandler;
        Assert(throwingHandler.RemoveAsCurrentCount == 1,
            "active handler replacement must attempt to detach the old handler");
        Assert(replacementHandler.SetAsCurrentCount == 1,
            "active handler replacement must keep the new handler active if old cleanup fails");
        navigator.OnRemoveAsCurrent();

        var removalNavigator = new MeshNavigator();
        var removalHandler = new ThrowingRemoveStuckHandler();
        removalNavigator.StuckHandler = removalHandler;
        removalNavigator.OnSetAsCurrent();
        removalNavigator.OnRemoveAsCurrent();
        removalNavigator.StuckHandler = new TrackingStuckHandler();
        removalNavigator.OnSetAsCurrent();
        removalNavigator.OnRemoveAsCurrent();
    }

    private static void TestLiveCollisionProbeExclusions()
    {
        Assert(!MeshNavigator.ShouldProbeLiveCollision(
                ridingElevator: true, swimming: false, falling: false,
                isFinalMovePoint: false, destinationDistanceSqr: 400f),
            "elevator movement must not be mistaken for a blocked ground route");
        Assert(!MeshNavigator.ShouldProbeLiveCollision(
                ridingElevator: false, swimming: true, falling: false,
                isFinalMovePoint: false, destinationDistanceSqr: 400f),
            "swimming must not use the ground collision reroute");
        Assert(!MeshNavigator.ShouldProbeLiveCollision(
                ridingElevator: false, swimming: false, falling: true,
                isFinalMovePoint: false, destinationDistanceSqr: 400f),
            "falling must not use the ground collision reroute");
        Assert(!MeshNavigator.ShouldProbeLiveCollision(
                ridingElevator: false, swimming: false, falling: false,
                isFinalMovePoint: true, destinationDistanceSqr: 100f),
            "the final NPC approach must not blackspot the target's model");
        Assert(MeshNavigator.ShouldProbeLiveCollision(
                ridingElevator: false, swimming: false, falling: false,
                isFinalMovePoint: false, destinationDistanceSqr: 400f),
            "ordinary ground path following must probe live collision geometry");
    }

    private static void TestConfirmedLiveCollisionClearsPathAndAddsBlackspot()
    {
        var navigator = new MeshNavigator();
        var obstruction = new WoWPoint(12345.25f, -23456.5f, 78f);
        var insideExistingCoverage = obstruction.Add(2.9f, 0f, 0f);
        var outsideExistingCoverage = obstruction.Add(3.1f, 0f, 0f);
        int initialSpotCount = BlackspotManager.Blackspots.Count;
        navigator.OverrideCurrentPath(new[]
        {
            new WoWPoint(12340f, -23456.5f, 78f),
            new WoWPoint(12360f, -23456.5f, 78f)
        });

        try
        {
            navigator.ApplyConfirmedLiveCollision(obstruction, hitDistance: 2.5f);

            Assert(!navigator.HasActivePath,
                "confirmed live collision recovery must clear the stale path before regeneration");
            Assert(BlackspotManager.Blackspots.Any(spot => spot.Location == obstruction),
                "confirmed live collision recovery must make the pathfinder avoid the actual hit point");

            navigator.ApplyConfirmedLiveCollision(insideExistingCoverage, hitDistance: 2.5f);
            Assert(BlackspotManager.Blackspots.Count == initialSpotCount + 1,
                "a collision inside the real three-yard coverage must reuse the existing blackspot");

            navigator.ApplyConfirmedLiveCollision(outsideExistingCoverage, hitDistance: 2.5f);
            Assert(BlackspotManager.Blackspots.Count == initialSpotCount + 2,
                "a collision outside the real coverage must extend avoidance with another blackspot");
        }
        finally
        {
            foreach (var addedSpot in BlackspotManager.Blackspots
                         .Where(spot => spot.Location == obstruction
                             || spot.Location == insideExistingCoverage
                             || spot.Location == outsideExistingCoverage)
                         .ToArray())
            {
                BlackspotManager.RemoveBlackspot(addedSpot);
            }
        }
    }

    private static void TestNavigatorTileLogsAreBatched()
    {
        Navigator.BeginTileLogBatch();
        int loadedTiles;
        try
        {
            Assert(!Navigator.ShouldLogTileLoad(),
                "tile events inside a path query must be suppressed");
            Assert(!Navigator.ShouldLogTileLoad(),
                "every tile message in the same path query must join the batch");
        }
        finally
        {
            loadedTiles = Navigator.EndTileLogBatch();
        }

        Assert(loadedTiles == 2,
            "the completed path query must report one aggregate tile count");
        Assert(Navigator.ShouldLogTileLoad(),
            "tile messages outside path generation must remain visible");
    }

    private static void TestSlowPathDiagnosticThreshold()
    {
        Assert(!MeshNavigator.IsSlowPathGeneration(TimeSpan.FromMilliseconds(999)),
            "normal path queries must not generate diagnostic spam");
        Assert(MeshNavigator.IsSlowPathGeneration(TimeSpan.FromSeconds(1)),
            "one-second path queries must be visible in runtime logs");
    }

    private static void TestUnstickGraceSuppressesImmediateDrift()
    {
        var unstickTime = new DateTime(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);
        var graceEnd = unstickTime.AddSeconds(2);

        Assert(!MeshNavigator.ShouldCheckRouteDrift(unstickTime.AddMilliseconds(1999), graceEnd),
            "deliberate unstick displacement must not immediately regenerate the route");
        Assert(MeshNavigator.ShouldCheckRouteDrift(graceEnd, graceEnd),
            "persistent drift must be checked when the two-second recovery window ends");
    }

    private static void TestOvershotWaypointAdvancesWithoutBacktracking()
    {
        var previous = new WoWPoint(0f, 0f, 0f);
        var waypoint = new WoWPoint(5f, 0f, 0f);

        Assert(MeshNavigator.HasReachedOrPassedWaypoint(
                new WoWPoint(8f, 0.5f, 0f), previous, waypoint, precision: 1.5f),
            "a player that crossed a ground waypoint inside the route corridor must advance instead of walking back");
        Assert(!MeshNavigator.HasReachedOrPassedWaypoint(
                new WoWPoint(5f, 8f, 0f), previous, waypoint, precision: 1.5f),
            "being laterally beyond a waypoint must not skip an unrelated route segment");
    }

    private static void TestLookaheadCoversTheObservedMovementPulseGap()
    {
        float lookahead = MeshNavigator.CalculateMovementLookahead(
            currentSpeed: 0f,
            runSpeed: 7f,
            commandIntervalSeconds: 0.658f,
            pathPrecision: 1.5f);
        Assert(lookahead >= 5.5f,
            "walking lookahead must cover the measured command gap plus a small handoff margin");

        float slowPulseLookahead = MeshNavigator.CalculateMovementLookahead(
            currentSpeed: 0f,
            runSpeed: 7f,
            commandIntervalSeconds: 0.9f,
            pathPrecision: 1.5f);
        Assert(slowPulseLookahead >= 8.5f,
            "a one-second bot cadence must leave enough movement queued to avoid stopping before the next command");

        WoWPoint point = MeshNavigator.ComputePathLookahead(
            new[]
            {
                new WoWPoint(0f, 0f, 0f),
                new WoWPoint(5f, 0f, 0f),
                new WoWPoint(5f, 10f, 0f)
            },
            currentIndex: 1,
            lookaheadDistance: 4f);
        Assert(point.Distance(new WoWPoint(5f, 4f, 0f)) < 0.01f,
            "push-ahead must follow the next path segment instead of extending through a corner");
    }

    private static void TestVerticalMeshStepsDoNotConsumeGroundLookahead()
    {
        WoWPoint point = MeshNavigator.ComputePathLookahead(
            new[]
            {
                new WoWPoint(0f, 0f, 0f),
                new WoWPoint(0.5f, 0f, 3f),
                new WoWPoint(10f, 0f, 3f)
            },
            currentIndex: 0,
            lookaheadDistance: 6f);

        Assert(point.Distance2D(new WoWPoint(6f, 0f, 3f)) < 0.01f,
            "vertical mesh steps must not consume ground click-to-move lookahead and leave a near-zero planar target");
    }

    private static void TestDownhillMeshStepsConsumeFullSafeLookahead()
    {
        WoWPoint point = MeshNavigator.ComputePathLookahead(
            new[]
            {
                new WoWPoint(0f, 0f, 10f),
                new WoWPoint(0.5f, 0f, 7f),
                new WoWPoint(10f, 0f, 7f)
            },
            currentIndex: 0,
            lookaheadDistance: 6f);

        Assert(point.Distance2D(new WoWPoint(3.458f, 0f, 7f)) < 0.02f,
            "downhill mesh separation must consume full 3D lookahead so CTM cannot project across a cliff edge");
    }

    private static void TestCornerLookaheadRetainsAReachablePointBeyondTheTurn()
    {
        var path = new[]
        {
            new WoWPoint(0f, 0f, 0f),
            new WoWPoint(5f, 0f, 0f),
            new WoWPoint(5f, 10f, 0f)
        };

        WoWPoint selected = MeshNavigator.SelectFarthestClearLookahead(
            path,
            currentIndex: 1,
            desiredDistance: 9f,
            point => point.Y <= 3.5f);

        Assert(selected.X == 5f && selected.Y > 0f && selected.Y <= 3.5f,
            "a blocked full lookahead around a corner must retain a clear point beyond the vertex instead of stopping at it");
    }

    private static void TestLookaheadValidationStartsFromThePlayer()
    {
        var path = new[]
        {
            new WoWPoint(0f, 0f, 0f),
            new WoWPoint(5f, 0f, 0f),
            new WoWPoint(5f, 10f, 0f)
        };
        var player = new WoWPoint(0f, 0f, 0f);

        WoWPoint selected = MeshNavigator.SelectFarthestMovementLookahead(
            path,
            currentIndex: 1,
            desiredDistance: 7f,
            player,
            (from, to) => from == player && to.Y <= 0.01f);

        Assert(selected == path[1],
            "lookahead must stop at a switchback when the real player-to-click chord cuts outside the safe corridor");
    }

    private static void TestLookaheadCannotCollapseOntoThePlayer()
    {
        var player = new WoWPoint(0f, 0f, 0f);
        var currentWaypoint = new WoWPoint(8f, 0f, 0f);
        var collapsedLookahead = new WoWPoint(0.2f, 0.1f, 0f);

        WoWPoint selected = MeshNavigator.KeepGroundClickTargetAhead(
            player, currentWaypoint, collapsedLookahead, pathPrecision: 1.5f);

        Assert(selected == currentWaypoint,
            "a future switchback point inside CTM arrival range must not replace an unreached current waypoint");
    }

    private static void TestNearbyVerticalTransportBuildsAnElevatorShortcut()
    {
        var player = new WoWPoint(-5479.55f, -2394.70f, 56.72f);
        var destination = new WoWPoint(-5454.95f, -2442.79f, 90.02f);
        var transport = new WoWPoint(-5478f, -2396f, 89f);

        Assert(MeshNavigator.ShouldPreferNearbyElevator(
                player, destination, transport, routeAlreadyUsesElevator: false),
            "the Freewind vertical route must prefer its nearby lift over the distant ground ramp");
        Assert(!MeshNavigator.ShouldPreferNearbyElevator(
                player, new WoWPoint(-5350f, -2394f, 58f), transport, routeAlreadyUsesElevator: false),
            "an ordinary mostly-horizontal route must not be redirected onto a transport");
        Assert(!MeshNavigator.ShouldPreferNearbyElevator(
                player, destination, transport, routeAlreadyUsesElevator: true),
            "a mesh-provided elevator segment must remain authoritative");

        var sourceLanding = new WoWPoint(-5482f, -2392f, player.Z);
        var destinationLanding = new WoWPoint(-5473f, -2403f, destination.Z);
        Assert(MeshNavigator.TryCreateSafeElevatorShortcut(
                player,
                destination,
                transport,
                new[] { player, sourceLanding, destinationLanding, destination },
                out WoWPoint[] shortcut),
            "a nearby lift requires mesh-backed source and destination landings");
        Assert(shortcut.Length == 3 && shortcut[0] == sourceLanding && shortcut[1].Z == destination.Z
               && shortcut[1].Distance2D(transport) > 3f && shortcut[2] == destination,
            "the synthetic lift route must wait at ground level, exit beyond the platform upstairs, then resume the destination");
    }

    private static void TestAnimatedElevatorUsesValidatedLiveMatrixTranslation()
    {
        var reported = new WoWPoint(100f, 200f, 300f);
        var liveMatrix = (Tripper.Tools.Math.Matrix)System.Numerics.Matrix4x4.CreateTranslation(
            10f, 20f, 30f);

        Assert(MeshNavigator.SelectLiveTransportLocation(
                reported, liveMatrix, usesAnimatedTransportMatrix: true)
            == new WoWPoint(10f, 20f, 30f),
            "an animated lift must use its current world-matrix translation, not its static spawn point");
        Assert(MeshNavigator.SelectLiveTransportLocation(
                reported, default, usesAnimatedTransportMatrix: true) == WoWPoint.Empty,
            "a malformed animated matrix must stop lift movement instead of falling back to a stale spawn point");
        Assert(MeshNavigator.SelectLiveTransportLocation(
                reported, Tripper.Tools.Math.Matrix.Identity, usesAnimatedTransportMatrix: true)
            == WoWPoint.Empty,
            "an uninitialized identity matrix must not impersonate an animated transport at the world origin");
        Assert(MeshNavigator.SelectLiveTransportLocation(
                reported, default, usesAnimatedTransportMatrix: false) == reported,
            "ordinary static objects must retain their reported location");
    }

    private static void TestSyntheticElevatorReusesDestinationSideMeshLanding()
    {
        var player = new WoWPoint(-4f, 0f, 100f);
        var destination = new WoWPoint(30f, 0f, 0f);
        var transport = new WoWPoint(0f, 0f, 100f);
        var bridgeLanding = new WoWPoint(8f, 1f, 0f);
        var originalMeshPath = new[]
        {
            player,
            new WoWPoint(-8f, 0f, 90f),
            new WoWPoint(-5f, 0f, 0f),
            bridgeLanding,
            new WoWPoint(20f, 0f, 0f),
            destination
        };

        Assert(MeshNavigator.TryCreateSafeElevatorShortcut(
                player, destination, transport, originalMeshPath, out WoWPoint[] shortcut),
            "fixture must provide mesh-backed landings on both sides of the lift");

        Assert(shortcut[1] == bridgeLanding,
            "a synthetic lift must exit onto a destination-side navmesh landing instead of guessing beside a cliff");

        Assert(!MeshNavigator.TryCreateSafeElevatorShortcut(
                player,
                destination,
                transport,
                new[] { player, new WoWPoint(-6f, 0f, 0f), destination },
                out _),
            "automatic lift injection must be skipped when no nearby destination-side mesh landing is proven");

        Assert(!MeshNavigator.TryCreateSafeElevatorShortcut(
                player,
                destination,
                transport,
                new[] { player, new WoWPoint(0f, 8f, 0f), destination },
                out _),
            "a perpendicular point near an adjacent shaft must not count as a destination-side landing");
    }

    private static void TestClearingNavigationStopsActiveElevatorMovement()
    {
        var navigator = new MeshNavigator();
        var mover = new ElevatorRecordingMover();
        IPlayerMover previousMover = Navigator.PlayerMover;
        var field = typeof(MeshNavigator).GetField(
            "_elevatorTransit",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var controller = (ElevatorTransitController)field.GetValue(navigator)!;
        controller.Begin(
            11UL,
            11899U,
            new WoWPoint(0f, 0f, 100f),
            new WoWPoint(0f, 0f, 0f),
            new WoWPoint(4f, 0f, 100f),
            new WoWPoint(6f, 0f, 0f));

        try
        {
            Navigator.PlayerMover = mover;
            navigator.Clear();
            Assert(mover.StopCalls == 1 && controller.SelectedTransportGuid == 0UL,
                "clearing navigation must stop old elevator CTM before releasing the selected GUID");
        }
        finally
        {
            Navigator.PlayerMover = previousMover;
        }
    }

    private static void TestChangingDestinationStopsActiveElevatorMovement()
    {
        var navigator = new MeshNavigator();
        var mover = new ElevatorRecordingMover();
        IPlayerMover previousMover = Navigator.PlayerMover;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var controller = (ElevatorTransitController)typeof(MeshNavigator)
            .GetField("_elevatorTransit", flags)!.GetValue(navigator)!;
        typeof(MeshNavigator).GetField("_destination", flags)!
            .SetValue(navigator, new WoWPoint(30f, 0f, 0f));
        controller.Begin(
            11UL,
            11899U,
            new WoWPoint(0f, 0f, 100f),
            new WoWPoint(0f, 0f, 0f),
            new WoWPoint(4f, 0f, 100f),
            new WoWPoint(6f, 0f, 0f));

        try
        {
            Navigator.PlayerMover = mover;
            Assert(navigator.CancelElevatorTransitIfDestinationChanged(new WoWPoint(50f, 0f, 0f))
                   && mover.StopCalls == 1
                   && controller.SelectedTransportGuid == 0UL,
                "a changed destination must stop old elevator CTM immediately, before route throttling");
        }
        finally
        {
            Navigator.PlayerMover = previousMover;
        }
    }

    private static void TestLeavingDirectSwimMovementInvalidatesTheStaleGroundPath()
    {
        var navigator = new MeshNavigator();
        navigator.OverrideCurrentPath(new[]
        {
            new WoWPoint(0f, 0f, 0f),
            new WoWPoint(10f, 0f, 0f)
        });

        Assert(!navigator.UpdateDirectSwimState(isSwimming: true, useDirectSwimming: true),
            "entering direct swim movement must retain the ground path until the shoreline transition");
        Assert(navigator.HasActivePath,
            "direct swimming must not churn the mesh path on every swimming pulse");
        Assert(navigator.UpdateDirectSwimState(isSwimming: false, useDirectSwimming: false),
            "leaving direct swim movement must request a fresh ground path");
        Assert(!navigator.HasActivePath,
            "the stale pre-water path must not be resumed after reaching land");
    }

    private static void TestTimedMovementScheduleExpiresWithoutBlockingTheBotThread()
    {
        var schedule = new WoWMovement.TimedMovementSchedule();
        var start = new DateTime(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);
        schedule.Schedule(WoWMovement.MovementDirection.Forward, start.AddMilliseconds(100));
        schedule.Schedule(WoWMovement.MovementDirection.StrafeLeft, start.AddMilliseconds(600));

        Assert(schedule.TakeExpired(start.AddMilliseconds(99)) == WoWMovement.MovementDirection.None,
            "timed recovery movement must continue until its deadline without sleeping the bot thread");
        Assert(schedule.TakeExpired(start.AddMilliseconds(100)) == WoWMovement.MovementDirection.Forward,
            "the first elapsed recovery direction must stop exactly when its deadline is observed");
        Assert(schedule.TakeExpired(start.AddMilliseconds(600)) == WoWMovement.MovementDirection.StrafeLeft,
            "later recovery directions must remain independently scheduled");
    }

    private static void TestGenericStuckRecoveryRequiresPersistentGroundMovementFailure()
    {
        Assert(!DefaultStuckHandler.ShouldEvaluateMovementSample(
                isMoving: false, swimming: false, falling: false),
            "a naturally stopped CTM endpoint must not enter generic stuck recovery even if speed data is stale");
        Assert(!DefaultStuckHandler.ShouldEvaluateMovementSample(
                isMoving: true, swimming: true, falling: false),
            "swimming progress must not be evaluated as ground obstruction");
        Assert(DefaultStuckHandler.ShouldEvaluateMovementSample(
                isMoving: true, swimming: false, falling: false),
            "active ground movement must remain eligible for generic stuck recovery");

        var tracker = new MovementProgressTracker(requiredFailures: 2);
        Assert(!tracker.Observe(insufficientProgress: true),
            "one slow ground sample must not trigger a jump or strafe");
        Assert(tracker.Observe(insufficientProgress: true),
            "two consecutive slow ground samples must permit generic recovery");
        Assert(!tracker.Observe(insufficientProgress: true),
            "a completed recovery decision must start a fresh persistence window");
        Assert(!tracker.Observe(insufficientProgress: false),
            "normal progress must reset the persistent-failure sequence");
        Assert(!tracker.Observe(insufficientProgress: true),
            "a failure after normal progress must start a new sequence");
    }

    private static void TestSlowSharedPulseBreakdownIsRateLimited()
    {
        var previousLog = new DateTime(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

        Assert(!Styx.WoWPulsator.ShouldLogSlowPulseBreakdown(
                totalMilliseconds: 199, previousLog.AddSeconds(20), previousLog),
            "ordinary shared pulses must not add timing-log overhead");
        Assert(!Styx.WoWPulsator.ShouldLogSlowPulseBreakdown(
                totalMilliseconds: 500, previousLog.AddSeconds(9), previousLog),
            "slow shared-pulse diagnostics must be rate limited");
        Assert(Styx.WoWPulsator.ShouldLogSlowPulseBreakdown(
                totalMilliseconds: 500, previousLog.AddSeconds(10), previousLog),
            "a sustained slow shared pulse must expose its stage breakdown after the rate limit");
    }

    private static void TestSlowPluginPulseDiagnosticsAreRateLimitedPerPlugin()
    {
        var previousLog = new DateTime(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

        Assert(!Styx.Plugins.PluginManager.ShouldLogSlowPluginPulse(
                elapsedMilliseconds: 19, previousLog.AddSeconds(20), previousLog),
            "fast plugin callbacks must not generate timing noise");
        Assert(!Styx.Plugins.PluginManager.ShouldLogSlowPluginPulse(
                elapsedMilliseconds: 100, previousLog.AddSeconds(9), previousLog),
            "each slow plugin diagnostic must be independently rate limited");
        Assert(Styx.Plugins.PluginManager.ShouldLogSlowPluginPulse(
                elapsedMilliseconds: 100, previousLog.AddSeconds(10), previousLog),
            "a persistently slow plugin must identify itself after the rate limit");
    }

    private static void TestRuntimeCompilerDoesNotReferencePreviousTemporaryPlugins()
    {
        string temporaryPlugin = Path.Combine(Path.GetTempPath(), "DrinkPotions_123_deadbeef.dll");
        string stableRuntime = Path.Combine(AppContext.BaseDirectory, "CopilotBuddy.dll");

        Assert(!Styx.Loaders.SourceCompiler.ShouldReferenceLoadedAssemblyLocation(temporaryPlugin),
            "refresh compilation must not import an earlier temporary plugin assembly");
        Assert(Styx.Loaders.SourceCompiler.ShouldReferenceLoadedAssemblyLocation(stableRuntime),
            "stable application assemblies must remain available to runtime source compilation");
    }

    private static void TestChangedDestinationBypassesRetryThrottle()
    {
        Assert(!MeshNavigator.ShouldThrottlePathRegeneration(
                   destinationChanged: true,
                   hasCurrentPath: false,
                   throttleFinished: false),
            "a changed destination must bypass the previous destination's regeneration throttle");
        Assert(MeshNavigator.ShouldThrottlePathRegeneration(
                   destinationChanged: false,
                   hasCurrentPath: false,
                   throttleFinished: false),
            "an immediate retry for the same failed destination must remain throttled");
        Assert(!MeshNavigator.ShouldThrottlePathRegeneration(
                   destinationChanged: false,
                   hasCurrentPath: false,
                   throttleFinished: true),
            "the same destination must regenerate after its throttle expires");
    }

    private static void TestUnchangedPartialPathIsReusedUntilItsEndpoint()
    {
        var requested = new WoWPoint(100f, 100f, 30f);
        var snappedPartialEndpoint = new WoWPoint(40f, 40f, 10f);

        Assert(!MeshNavigator.HasMoveDestinationChanged(
                requested,
                requested,
                snappedPartialEndpoint,
                isPartialPath: true,
                pathPrecision: 2f),
            "an active partial route to the same request must be followed instead of regenerated from every new player position");
        Assert(MeshNavigator.HasMoveDestinationChanged(
                requested,
                new WoWPoint(110f, 100f, 30f),
                snappedPartialEndpoint,
                isPartialPath: true,
                pathPrecision: 2f),
            "a genuinely changed request must still replace an active partial route");
    }

    private static void TestCachedWeightSetDoesNotQueryLiveTalentState()
    {
        var cachedField = typeof(Styx.Logic.Inventory.WeightSetEx).GetField(
            "_cachedWeightSet",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var loadedField = typeof(Styx.Logic.Inventory.WeightSetEx).GetField(
            "_loadedWeightSets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        object? originalCached = cachedField.GetValue(null);
        object? originalLoaded = loadedField.GetValue(null);
        var originalPlayer = ObjectManager.Me;
        var cached = (Styx.Logic.Inventory.WeightSetEx)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Styx.Logic.Inventory.WeightSetEx));

        try
        {
            loadedField.SetValue(null, new[] { cached });
            cachedField.SetValue(null, cached);
            ObjectManager.Me = null;

            Assert(ReferenceEquals(Styx.Logic.Inventory.WeightSetEx.CurrentWeightSet, cached),
                "a cached weight set must return without synchronous talent Lua calls on the movement pulse");
        }
        finally
        {
            ObjectManager.Me = originalPlayer;
            cachedField.SetValue(null, originalCached);
            loadedField.SetValue(null, originalLoaded);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TrackingStuckHandler : StuckHandler
    {
        internal int SetAsCurrentCount { get; private set; }
        internal int RemoveAsCurrentCount { get; private set; }

        public override bool IsStuck() => false;
        public override void Unstick() { }
        public override void OnSetAsCurrent() => SetAsCurrentCount++;
        public override void OnRemoveAsCurrent() => RemoveAsCurrentCount++;
    }

    private sealed class ThrowingRemoveStuckHandler : StuckHandler
    {
        internal int RemoveAsCurrentCount { get; private set; }

        public override bool IsStuck() => false;
        public override void Unstick() { }
        public override void OnSetAsCurrent() { }
        public override void OnRemoveAsCurrent()
        {
            RemoveAsCurrentCount++;
            throw new InvalidOperationException("test removal failure");
        }
    }

    private sealed class ElevatorRecordingMover : IPlayerMover
    {
        internal int StopCalls { get; private set; }
        public void Move(WoWMovement.MovementDirection direction) { }
        public void MoveTowards(WoWPoint location) { }
        public void MoveStop() => StopCalls++;
    }
}
