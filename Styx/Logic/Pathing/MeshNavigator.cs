using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Styx.Common;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing.Interop;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;
using TripperNav = Tripper.Navigation;
using Matrix = Tripper.Tools.Math.Matrix;

namespace Styx.Logic.Pathing
{
	/// <summary>
	/// Concrete NavigationProvider that wraps Tripper.Navigator (Navigation.dll).
	/// Direct port of HB 6.2.3 MeshNavigator (Styx.Pathing.MeshNavigator).
	///
	/// Responsibilities (matching HB WoD minus HB 6.2.3 door handling):
	/// - Navmesh path generation (FindPath + EnsureTiles + Blackspot sync)
	/// - Path following with push-ahead (method_25/26)
	/// - Start-index skip (method_14)
	/// - Off-mesh connection dispatch (elevator, portal, interact, jump)
	/// - Drift detection (method_15)
	/// - Alive/ghost query filter (method_28)
	/// - PathPrecision-based waypoint advance (method_24/27)
	///
	/// Not here (stays in Navigator facade):
	/// - Flightor routing, mount/dismount, avoidance wiring, bot lifecycle
	/// </summary>
	public partial class MeshNavigator : NavigationProvider
	{
		#region Fields — path state (HB 6.2.3 MeshNavigator fields)

		// HB 6.2.3 bool_0 guard: prevents double-registration of event handlers.
		// OnSetAsCurrent throws InvalidOperationException if already registered.
		// OnRemoveAsCurrent throws if not registered. Prevents BotEvents.OnPulse double-hook.
		private bool _isCurrent;

		private WoWPoint _destination;
		private readonly List<WoWPoint> _currentPath = new List<WoWPoint>();

		private int _currentPathIndex;
		private TripperNav.StraightPathFlags[]? _currentFlags;
		private TripperNav.AreaType[]? _currentPolyTypes;
		private TripperNav.AbilityFlags[]? _currentAbilityFlags;
		private bool _isPartialPath;
		private bool _ridingElevator;
		private bool _usingDirectSwimMovement;
		private WoWPoint _localConnectorTarget;
		private WoWPoint _localConnectorDestination;
		private DateTime _localConnectorStartedUtc;
		private readonly WaitTimer _localConnectorSearchTimer = new WaitTimer(TimeSpan.FromSeconds(2));

		// Push-ahead cache (HB 6.2.3 method_25/26)
		private int _cachedPushAheadIndex = -1;
		private WoWPoint _cachedClickPoint = WoWPoint.Zero;
		private DateTime _lastMovePulseUtc = DateTime.MinValue;
		private float _smoothedMoveIntervalSeconds = 0.15f;

		private WaitTimer _pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500));
		private WaitTimer _interactTimer = new WaitTimer(TimeSpan.FromMilliseconds(2000));
		private static readonly TimeSpan UnstickDriftGrace = TimeSpan.FromSeconds(2);
		private DateTime _suppressDriftUntilUtc = DateTime.MinValue;
		private const float LiveCollisionProbeDistance = 6f;
		private readonly LiveCollisionTracker _liveCollisionTracker = new LiveCollisionTracker();
		private readonly CommandedMovementProgressTracker _commandedProgress = new CommandedMovementProgressTracker();
		private readonly LocalDetourAttemptHistory _localDetourHistory = new LocalDetourAttemptHistory();
		private readonly WaitTimer _liveCollisionProbeTimer = new WaitTimer(TimeSpan.FromMilliseconds(250));
		private readonly WaitTimer _liveCollisionRepathTimer = new WaitTimer(TimeSpan.FromSeconds(2));

		private readonly WaitTimer _doorScanTimer = new WaitTimer(TimeSpan.FromSeconds(1.0));
		private readonly WaitTimer _doorInteractTimer = new WaitTimer(TimeSpan.FromSeconds(1.0));

		// HB 6.2.3 uint_0: transports the elevator search must skip. HB lists the two doors
		// of the middle Undervator plus a Draenor object; the other four Undercity doors are
		// the same lowerLdoor/upperLdoor models (displayId 462, the Undervator itself is 455).
		private static readonly uint[] ElevatorDoorEntries =
			{ 20650, 20651, 20653, 20654, 20656, 20657 };

		private readonly ElevatorTransitController _elevatorTransit = new ElevatorTransitController();
		private DateTime _nextElevatorDiagnosticUtc = DateTime.MinValue;

		// HB 6.2.3 areaType_0: faction area type for RaycastBlocked
		private TripperNav.AreaType _factionAreaType = TripperNav.AreaType.Ground;

		// Avoidance path (set by Navigator facade from NavAvoidWaypointProvider)
		private WoWPoint[]? _currentAvoidPath;

		private int _currentAvoidPathIndex;

		private StuckHandler _stuckHandler;
		private TripperNav.Navigator? _activeTripperNavigator;

		#endregion

		#region Constructor

		public MeshNavigator()
		{
			PathPrecision = 2f;
			// The concrete handler is activated by this class's lifecycle methods when
			// Navigator.NavigationProvider selects this MeshNavigator.
			_stuckHandler = new DefaultStuckHandler();
		}

		#endregion

		#region NavigationProvider implementation

		/// <summary>HB 6.2.3 MeshNavigator.PathPrecision — default 2.0 yards.</summary>
		public override float PathPrecision { get; set; }

		/// <summary>
		/// HB 6.2.3 MeshNavigator.OnSetAsCurrent — hooks BotEvents.OnPulse for per-pulse
		/// Guard mirrors HB 6.2.3 bool_0: throws if already current.
		/// </summary>
		public override void OnSetAsCurrent()
		{
			if (_isCurrent)
				throw new InvalidOperationException("This MeshNavigator instance is already in use");

			var tripperNavigator = Navigator.TripperNavigator;
			bool handlerActivationStarted = false;
			bool pulseAttached = false;
			bool logAttached = false;
			try
			{
				handlerActivationStarted = true;
				_stuckHandler?.OnSetAsCurrent();
				BotEvents.OnPulse += OnPulse;
				pulseAttached = true;
				tripperNavigator.LogMessage += OnNavigatorLog;
				logAttached = true;
				_activeTripperNavigator = tripperNavigator;
				_isCurrent = true;
			}
			catch
			{
				if (logAttached)
				{
					try { tripperNavigator.LogMessage -= OnNavigatorLog; } catch { }
				}
				if (pulseAttached)
				{
					try { BotEvents.OnPulse -= OnPulse; } catch { }
				}
				if (handlerActivationStarted)
				{
					try { _stuckHandler?.OnRemoveAsCurrent(); } catch { }
				}
				_activeTripperNavigator = null;
				throw;
			}
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.OnRemoveAsCurrent — unhooks BotEvents.OnPulse.
		/// Guard mirrors HB 6.2.3 bool_0: throws if not currently registered.
		/// </summary>
		public override void OnRemoveAsCurrent()
		{
			if (!_isCurrent)
				throw new InvalidOperationException("This MeshNavigator instance is not in use");

			try { BotEvents.OnPulse -= OnPulse; }
			catch (Exception ex) { LogLifecycleCleanupFailure("pulse handler", ex); }
			try
			{
				if (_activeTripperNavigator != null)
					_activeTripperNavigator.LogMessage -= OnNavigatorLog;
			}
			catch (Exception ex) { LogLifecycleCleanupFailure("navigator log handler", ex); }
			try { _stuckHandler?.OnRemoveAsCurrent(); }
			catch (Exception ex) { LogLifecycleCleanupFailure("stuck handler", ex); }
			_activeTripperNavigator = null;
			_isCurrent = false;
		}

		/// <summary>
		/// HB 6.2.3 method_1: updates faction area type on pulse.
		/// Path generation loads the required start and destination tiles. Streaming
		/// them synchronously on every pulse stalls the behavior and movement loop.
		/// </summary>
		private void OnPulse(object sender, EventArgs e)
		{
			var me = ObjectManager.Me;
			if (me != null)
				_factionAreaType = me.IsHorde ? TripperNav.AreaType.Horde : TripperNav.AreaType.Alliance;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.UpdateMaps():
		/// </summary>
		public void UpdateMaps()
		{
			var me = ObjectManager.Me;
			if (me == null || !Navigator.IsNavigatorLoaded)
				return;

			uint mapId = Navigator.TripperNavigator.CurrentMapId;
			var pos = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, pos, 1);
			}
			catch { }
		}

		/// <summary>HB 6.2.3 MeshNavigator.StuckHandler — Class469 instance.
		/// Lifecycle (OnSetAsCurrent/OnRemoveAsCurrent) is handled by this concrete provider.
		/// MeshNavigator uses _stuckHandler directly for Reset() calls.
		/// </summary>
		public override StuckHandler StuckHandler
		{
			get => _stuckHandler;
			set
			{
				if (ReferenceEquals(value, _stuckHandler))
					return;
				// HB pattern: only call lifecycle if this provider is current.
				if (_isCurrent)
				{
					try
					{
						value?.OnSetAsCurrent();
					}
					catch
					{
						try { value?.OnRemoveAsCurrent(); } catch { }
						throw;
					}
					try { _stuckHandler?.OnRemoveAsCurrent(); }
					catch (Exception ex) { LogLifecycleCleanupFailure("replaced stuck handler", ex); }
				}
				_stuckHandler = value;
			}
		}

		private static void LogLifecycleCleanupFailure(string component, Exception exception)
		{
			Logging.WriteDiagnostic(
				"[Nav] Failed to detach {0}; continuing lifecycle cleanup: {1}",
				component,
				exception.Message);
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.MoveTo — main navigation method.
		/// Generates path if needed, follows path with push-ahead, handles offmesh/doors/stuck.
		/// </summary>
		public override MoveResult MoveTo(WoWPoint destination)
		{
			return MoveTo(destination, PathPrecision, "Navigation");
		}

		public MoveResult MoveTo(WoWPoint destination, string destinationName)
		{
			return MoveTo(destination, PathPrecision, destinationName);
		}

		public MoveResult MoveTo(WoWPoint destination, float precision)
		{
			return MoveTo(destination, precision, "Navigation");
		}

		internal static bool ShouldThrottlePathRegeneration(
			bool destinationChanged,
			bool hasCurrentPath,
			bool throttleFinished)
		{
			return !destinationChanged && !hasCurrentPath && !throttleFinished;
		}

		internal static bool ShouldCheckRouteDrift(DateTime utcNow, DateTime suppressUntilUtc)
		{
			return utcNow >= suppressUntilUtc;
		}

		internal static bool IsSlowPathGeneration(TimeSpan elapsed)
		{
			return elapsed >= TimeSpan.FromSeconds(1);
		}

		internal static bool HasMoveDestinationChanged(
			WoWPoint previousRequestedDestination,
			WoWPoint requestedDestination,
			WoWPoint pathEndpoint,
			bool isPartialPath,
			float pathPrecision)
		{
			if (previousRequestedDestination == WoWPoint.Zero)
				return true;

			float thresholdSqr = pathPrecision * pathPrecision;
			WoWPoint comparisonPoint = isPartialPath
				? previousRequestedDestination
				: pathEndpoint != WoWPoint.Zero ? pathEndpoint : previousRequestedDestination;
			return comparisonPoint.DistanceSqr(requestedDestination) > thresholdSqr;
		}

		public MoveResult MoveTo(WoWPoint destination, float precision, string destinationName)
		{
			WoWPoint origin = ObjectManager.Me?.Location ?? WoWPoint.Zero;
			MoveResult result = MoveToCore(destination, precision, destinationName);
			RecordMoveOutcome(origin, destination, result);
			return result;
		}

		internal void RecordMoveOutcome(WoWPoint origin, WoWPoint destination, MoveResult result)
		{
			DateTime now = DateTime.UtcNow;
			bool changed = LastMoveDestination != destination || LastMoveResult != result;
			LastMoveOrigin = origin;
			LastMoveDestination = destination;
			LastMoveResult = result;
			LastMoveAttemptUtc = now;
			LastMoveAttemptSequence++;
			if ((result == MoveResult.PathGenerationFailed || result == MoveResult.Failed)
			    && (changed || now - _lastMoveFailureLogUtc >= TimeSpan.FromSeconds(5)))
			{
				_lastMoveFailureLogUtc = now;
				Logging.WriteDiagnostic(
					"[Nav] MoveTo failed: map={0} from={1} to={2} result={3} pathCount={4} pathIndex={5} partial={6}",
					ObjectManager.Me?.MapId ?? 0, origin, destination, result,
					_currentPath.Count, _currentPathIndex, _isPartialPath);
			}
		}

		internal void DiscardExhaustedPath()
		{
			if (_currentPath.Count == 0 || _currentPathIndex < _currentPath.Count)
				return;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_cachedPushAheadIndex = -1;
		}

		private static bool CanUseLocalConnector(LocalPlayer me)
		{
			return me.IsAlive && !me.IsSwimming && !me.IsFalling && !me.IsFlying
			    && !me.IsOnTransport && ObjectManager.Executor != null;
		}

		private bool TryStartLocalConnector(LocalPlayer me, WoWPoint destination)
		{
			if (_ridingElevator || !CanUseLocalConnector(me) || !_localConnectorSearchTimer.IsFinished
			    || _currentAvoidPath != null || !_localDetourHistory.CanAttempt(me.MapId, me.Location, DateTime.UtcNow)
			    || HasRequiredAvoidanceRoute(destination))
				return false;
			_localConnectorSearchTimer.Reset();
			WoWPoint origin = me.Location;
			uint mapId = me.MapId;
			var budget = System.Diagnostics.Stopwatch.StartNew();
			foreach (WoWPoint requested in LocalMeshConnector.Candidates(origin, destination))
			{
				if (budget.ElapsedMilliseconds >= 500)
					break;
				var requestedVector = new Vector3(requested.X, requested.Y, requested.Z);
				if (!TripperNavigator.FindNearestPolyRef(mapId, requestedVector, out var polygon, out var snapped))
					continue;
				WoWPoint landing = new WoWPoint(snapped.X, snapped.Y, snapped.Z);
				if (!LocalMeshConnector.IsCandidateWithinLimits(origin, requested, landing)
				    || BlackspotManager.IsBlackspotted(landing, Math.Max(0.5f, me.BoundingRadius)))
					continue;
				var areaStatus = new TripperNav.Status(TripperNavigator.GetPolyArea(mapId, polygon, out byte area));
				if (!areaStatus.Succeeded || (area != (byte)TripperNav.AreaType.Ground
				    && area != (byte)TripperNav.AreaType.Road && area != (byte)TripperNav.AreaType.Horde
				    && area != (byte)TripperNav.AreaType.Alliance && area != (byte)TripperNav.AreaType.KnownBuilding))
					continue;
				TripperNav.PathFindResult onward = FindPath(landing, destination);
				if (!LocalMeshConnector.HasUsableOnwardPath(onward, landing, destination)
				    || !ValidateLocalConnector(me, origin, landing))
					continue;

				_localConnectorTarget = landing;
				_localDetourHistory.Record(mapId, origin, DateTime.UtcNow);
				_localConnectorDestination = destination;
				_localConnectorStartedUtc = DateTime.UtcNow;
				Logging.WriteDiagnostic("[Nav] Following validated local detour: map={0} from={1} landing={2} destination={3}",
					mapId, origin, landing, destination);
				Navigator.PlayerMover.MoveTowards(landing);
				return true;
			}
			return false;
		}

		private MoveResult? ContinueLocalConnector(LocalPlayer me, WoWPoint destination)
		{
			// Dynamic hazards retain priority while crossing a previously clear corridor.
			if (HasRequiredAvoidanceRoute(destination))
			{
				ResetLocalConnector();
				return null;
			}
			if (_localConnectorDestination.DistanceSqr(destination) > PathPrecision * PathPrecision)
			{
				ResetLocalConnector();
				return null;
			}
			if (!CanUseLocalConnector(me) || DateTime.UtcNow - _localConnectorStartedUtc > TimeSpan.FromSeconds(5))
			{
				ResetLocalConnector();
				return MoveResult.Failed;
			}
			if (me.Location.Distance2DSqr(_localConnectorTarget) <= 0.65f * 0.65f
			    && Math.Abs(me.Location.Z - _localConnectorTarget.Z) <= 0.75f)
			{
				ResetLocalConnector();
				return null;
			}
			if (!ValidateLocalConnector(me, me.Location, _localConnectorTarget))
			{
				ResetLocalConnector();
				return MoveResult.Failed;
			}
			Navigator.PlayerMover.MoveTowards(_localConnectorTarget);
			return MoveResult.Moved;
		}

		private static bool HasRequiredAvoidanceRoute(WoWPoint destination)
		{
			return Navigator.NavAvoidWaypointProvider?.Invoke(destination) is { Length: > 0 };
		}

		private void ResetLocalConnector()
		{
			_commandedProgress.Reset();
			Navigator.PlayerMover.MoveStop();
			_localConnectorTarget = WoWPoint.Zero;
			_localConnectorDestination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_cachedPushAheadIndex = -1;
			_pathRegenThrottle.Stop();
		}

		private static bool ValidateLocalConnector(LocalPlayer me, WoWPoint origin, WoWPoint landing)
		{
			return LocalMeshConnector.ValidateGroundCorridor(origin, landing, TraceLocalConnector,
				p => BlackspotManager.IsBlackspotted(p), Math.Max(0.5f, me.BoundingRadius), Math.Max(1.2f, me.BoundingHeight));
		}

		private static bool HasGroundSupport(LocalPlayer me)
		{
			if (me.IsFalling || me.MovementInfo.IsFalling || ObjectManager.Executor == null)
				return false;

			try
			{
				WoWPoint origin = me.Location;
				bool hit = GameWorld.TraceLine(
					origin.Add(0f, 0f, 0.5f),
					origin.Add(0f, 0f, -1.5f),
					GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures,
					out WoWPoint ground);
				return hit
				       && ground != WoWPoint.Zero
				       && ground.Distance2D(origin) <= Math.Max(0.5f, me.BoundingRadius)
				       && Math.Abs(ground.Z - origin.Z) <= 0.75f;
			}
			catch
			{
				return false;
			}
		}

		private static bool IsElevatorGroundCorridorSafe(
			LocalPlayer me,
			WoWPoint target,
			bool hasGroundSupport)
		{
			if (me.Location.Distance2DSqr(target) <= 0.75f * 0.75f
			    && Math.Abs(me.Location.Z - target.Z) <= 0.75f)
			{
				return hasGroundSupport;
			}

			return LocalMeshConnector.ValidateGroundCorridor(
				me.Location,
				target,
				TraceLocalConnector,
				p => BlackspotManager.IsBlackspotted(p),
				Math.Max(0.5f, me.BoundingRadius),
				Math.Max(1.2f, me.BoundingHeight),
				maxDistance: 15f);
		}

		private static bool TraceLocalConnector(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags flags,
			out bool[] hits, out WoWPoint[] points)
		{
			hits = Array.Empty<bool>();
			points = Array.Empty<WoWPoint>();
			if (ObjectManager.Executor == null)
				return false;
			try
			{
				GameWorld.MassTraceLine(lines, flags, out hits, out points);
				return hits.Length == lines.Length && points.Length == lines.Length;
			}
			catch { return false; }
		}

		private MoveResult MoveToCore(WoWPoint destination, float precision, string destinationName)
		{
			if (destination == WoWPoint.Zero)
				return MoveResult.Failed;

			if (!IsFiniteRoutePoint(destination))
			{
				ResetTerminalRouteEvidence();
				LastRouteFailure = RouteFailureReason.InvalidCoordinates;
				return MoveResult.Failed;
			}

			LocalPlayer? me = ObjectManager.Me;
			if (me == null)
				return MoveResult.Failed;
			if (!IsFiniteRoutePoint(me.Location))
			{
				ResetTerminalRouteEvidence();
				LastRouteFailure = RouteFailureReason.InvalidCoordinates;
				return MoveResult.Failed;
			}
			CancelElevatorTransitIfDestinationChanged(destination);
			ObserveMovementCadence(DateTime.UtcNow);

			ApplyAliveQueryFilter(me.IsAlive);

			if (!me.IsSwimming)
				UpdateDirectSwimState(isSwimming: false, useDirectSwimming: false);

			if (me.IsSwimming)
			{
				// Once direct swimming is selected, keep it until land. Re-running a full
				// ground-path query on every swimming pulse is expensive and can change modes
				// at the shoreline while the old path still points behind the player.
				bool useDirectSwimming = _usingDirectSwimMovement
					|| !HasShortGroundPath(me.Location, destination, 2000f);
				UpdateDirectSwimState(isSwimming: true, useDirectSwimming);
				if (useDirectSwimming)
				{
					Navigator.PlayerMover.MoveTowards(destination);
					return MoveResult.Moved;
				}
			}

			float distance = me.Location.Distance(destination);
			if (distance < precision)
			{
				_commandedProgress.Reset();
				ResetTerminalRouteEvidence();
				_suppressDriftUntilUtc = DateTime.MinValue;
				_liveCollisionTracker.Reset();
				_destination = WoWPoint.Zero;
				_currentPath.Clear();
				return MoveResult.ReachedDestination;
			}

			if (TryOpenClosedDoor(me))
				return MoveResult.Moved;

			if (_localConnectorTarget != WoWPoint.Zero)
			{
				MoveResult? connectorResult = ContinueLocalConnector(me, destination);
				if (connectorResult.HasValue)
					return connectorResult.Value;
			}

			if (ShouldDeferTerminalRetry(me.Location, destination, me.MapId, DateTime.UtcNow))
				return MoveResult.Failed;

			// A snapped final waypoint may be consumed before the requested point is
			// reached. Let the normal regeneration path retry instead of keeping a
			// nonempty, exhausted path that falls through to failure forever.
			DiscardExhaustedPath();

			// Complete routes retain the HB endpoint comparison. A partial route compares
			// the caller's requested destination so its reachable portion can be consumed.
			WoWPoint pathEndpoint = _currentPath.Count > 0 ? _currentPath[_currentPath.Count - 1] : _destination;
			bool destinationChanged = HasMoveDestinationChanged(
				_destination,
				destination,
				pathEndpoint,
				_isPartialPath,
				PathPrecision);
			bool needsPathRegen = !destinationChanged && _currentPath.Count == 0;

			if (destinationChanged || needsPathRegen)
			{
				if (ShouldThrottlePathRegeneration(
						destinationChanged,
						_currentPath.Count > 0,
						_pathRegenThrottle.IsFinished))
					return MoveResult.Moved;

				_pathRegenThrottle.Reset();
				_destination = destination;
				_currentPath.Clear();

				if (destinationChanged)
				{
					_commandedProgress.Reset();
					ResetTerminalRouteEvidence();
					_suppressDriftUntilUtc = DateTime.MinValue;
					_liveCollisionTracker.Reset();
					try { StuckHandler.Reset(); } catch { }
					ResetElevatorTransit();
				}

				// HB 6.2.3 MeshNavigator.FindPath → MeshMovePath assignment.
				ResetTerminalRouteEvidence();
				var pathResult = FindPath(me.Location, destination);
				_lastPathStatus = pathResult.Status;
				if (!pathResult.Succeeded || pathResult.Points == null || pathResult.Points.Length == 0)
				{
					Logging.Write(System.Drawing.Color.Red,
						"Could not generate path from {0} to {1} on map {2} (status: {3})",
						me.Location, destination, me.MapId, pathResult.Status);
					RecordTerminalRouteFailure(me, partial: false);
					return MoveResult.PathGenerationFailed;
				}

				foreach (var point in pathResult.Points)
					_currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));
				_currentFlags = pathResult.Flags;
				_currentPolyTypes = pathResult.PolyTypes;
				_currentAbilityFlags = pathResult.AbilityFlags;
				_isPartialPath = pathResult.IsPartialPath;

				_currentPathIndex = 0;
				_cachedPushAheadIndex = -1;

				// Detour always outputs path[0] = the snapped start position, which is within
				// PathPrecision of the player. If we leave _currentPathIndex = 0, the waypoint
				// advance loop will immediately "reach" it on the first tick, call Reset(), and
				// wipe _tried* flags from any in-progress Unstick sequence.
				// Fix: skip the trivial start point when it's within PathPrecision of the player.
				if (_currentPath.Count > 1
				    && me.Location.Distance2DSqr(_currentPath[0]) <= PathPrecision * PathPrecision)
				{
					_currentPathIndex = 1;
				}

				// HB 6.2.3 method_14: skip waypoints the player has already passed
				SkipPassedWaypoints(me);
				TryInstallNearbyElevatorShortcut(me, destination);
			}

			// A successful partial path can consist of the start polygon alone. Its
			// endpoint is already within waypoint precision, so ordinary following
			// cannot move. Try a very short, live-validated walking connector to a
			// nearby polygon that has a complete onward route.
			if (_isPartialPath && _currentPath.Count > 0
			    && me.Location.Distance2DSqr(_currentPath[_currentPath.Count - 1]) <= 4f
			    && Math.Abs(me.Location.Z - _currentPath[_currentPath.Count - 1].Z) < 2f)
			{
				if (TryStartLocalConnector(me, destination))
					return MoveResult.Moved;
			}

			if (_currentPath.Count > 0 && _currentPathIndex < _currentPath.Count)
			{
				// Drift detection (HB 6.2.3 method_9 → method_15). HB runs the on-path check
				// on every MoveTo, BEFORE the off-mesh dispatch and the stuck check, with no
				// suppression. Uses navmesh RaycastBlocked as primary check: if the player can
				// see the next waypoint through the navmesh, we are still on path. Only when
				// the raycast is blocked AND the hit is not near the waypoint AND 2D perp
				// distance exceeds PathPrecision do we regenerate. Off-mesh connection
				// segments and falling are on-path by definition (HB method_15 early returns).
				if (ShouldCheckRouteDrift(DateTime.UtcNow, _suppressDriftUntilUtc)
				    && _currentPathIndex > 0 && _currentPathIndex < _currentPath.Count
				    && !(WoWMovement.ActiveMover ?? StyxWoW.Me).IsFalling)
				{
					bool isOffMeshSeg = _currentFlags != null && (_currentPathIndex - 1) < _currentFlags.Length
					    && (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0;

					if (!isOffMeshSeg)
					{
						bool offPath;
						if (Navigator.IsNavigatorLoaded)
						{
							uint mapId = (uint)me.MapId;
							var playerVec = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
							WoWPoint nextWp = _currentPath[_currentPathIndex];
							var nextVec = new Vector3(nextWp.X, nextWp.Y, nextWp.Z);
							bool blocked = TripperNavigator.RaycastBlocked(mapId, playerVec, nextVec, out float hitT, _factionAreaType);
							if (!blocked)
							{
								offPath = false;
							}
							else if (Math.Abs(hitT) < 1e-5f)
							{
								offPath = false;
							}
							else
							{
								WoWPoint hitPoint = new WoWPoint(
									me.Location.X + (nextWp.X - me.Location.X) * hitT,
									me.Location.Y + (nextWp.Y - me.Location.Y) * hitT,
									me.Location.Z + (nextWp.Z - me.Location.Z) * hitT);
								bool hitCloseToWaypoint = IsAtPoint(hitPoint, nextWp);
								float perpDist = DistanceToLineSegment2D(me.Location, _currentPath[_currentPathIndex - 1], nextWp);
								offPath = !hitCloseToWaypoint && perpDist * perpDist >= PathPrecision * PathPrecision;
							}
						}
						else
						{
							float driftDist = DistanceToLineSegment2D(me.Location,
								_currentPath[_currentPathIndex - 1], _currentPath[_currentPathIndex]);
							offPath = driftDist * driftDist > PathPrecision * PathPrecision;
						}

						if (offPath)
						{
							Logging.WriteDiagnostic("Generating new path because we are not on the old path anymore!");
							_currentPath.Clear();
							_currentPathIndex = 0;
							_cachedPushAheadIndex = -1;
							return MoveToCore(destination, precision, destinationName);
						}
					}
				}

				// Off-mesh segment re-dispatch (HB 6.2.3 method_18)
				if (_currentPathIndex > 0 && _currentFlags != null
				    && (_currentPathIndex - 1) < _currentFlags.Length
				    && (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
				{
					var offMeshAreaType = (_currentPolyTypes != null && (_currentPathIndex - 1) < _currentPolyTypes.Length)
						? _currentPolyTypes[_currentPathIndex - 1]
						: TripperNav.AreaType.Ground;

					WoWPoint offMeshEndPt = _currentPath[_currentPathIndex];
					WoWPoint offMeshStartPt = _currentPath[_currentPathIndex - 1];
					if (IsAtPoint(me.Location, offMeshEndPt) && offMeshAreaType != TripperNav.AreaType.Elevator)
					{
					_currentPathIndex++;
					ResetElevatorTransit();
					if (_currentPathIndex >= _currentPath.Count)
						return CompletePathOrRecover(me);
					return MoveResult.Moved;
				}

				return DispatchOffMesh(me, offMeshEndPt, offMeshStartPt, offMeshAreaType);
			}

				// Advance before checking stuck. A short CTM target may naturally stop at or just
				// beyond an intermediate waypoint between slow bot pulses; treating that arrival
				// as a stuck sample is what caused the repeated stop/jump/backtrack loop.
				while (_currentPathIndex < _currentPath.Count)
				{
					WoWPoint candidate = _currentPath[_currentPathIndex];
					bool isFinalPoint = _currentPathIndex == _currentPath.Count - 1;
					float waypointPrecision = isFinalPoint ? precision : PathPrecision;
					bool reachedWaypoint = me.Location.Distance2DSqr(candidate)
						<= waypointPrecision * waypointPrecision
						&& Math.Abs(me.Location.Z - candidate.Z) < 4.5f;

					if (!reachedWaypoint && !isFinalPoint && _currentPathIndex > 0)
					{
						reachedWaypoint = HasReachedOrPassedWaypoint(
							me.Location,
							_currentPath[_currentPathIndex - 1],
							candidate,
							waypointPrecision);
					}

					if (!reachedWaypoint)
						break;

					_currentPathIndex++;
					_commandedProgress.Reset();
					if (_currentPathIndex >= _currentPath.Count)
						return CompletePathOrRecover(me);

					if (_currentFlags != null && (_currentPathIndex - 1) < _currentFlags.Length
					    && (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
						return MoveResult.Moved;

					try { StuckHandler.Reset(); } catch { }
				}

				// Generic stuck recovery now runs only after completed/passed waypoints are
				// consumed. Live collision probing remains the fast obstruction detector.
				if (!_ridingElevator)
				{
					if (StuckHandler.IsStuck())
					{
						StuckHandler.Unstick();
						_suppressDriftUntilUtc = DateTime.UtcNow + UnstickDriftGrace;
						return MoveResult.UnstuckAttempt;
					}
				}

				// Avoidance path (HB 6.2.3 AvoidanceNavigationProvider pattern)
				if (Navigator.NavAvoidWaypointProvider != null)
				{
					bool hadAvoidPath = _currentAvoidPath != null;
					var avoidPoints = Navigator.NavAvoidWaypointProvider(_destination);
					if (avoidPoints != null && avoidPoints.Length > 0)
					{
						bool avoidEndpointChanged = _currentAvoidPath == null || _currentAvoidPath.Length == 0
							|| _currentAvoidPath[_currentAvoidPath.Length - 1].DistanceSqr(avoidPoints[avoidPoints.Length - 1]) > PathPrecision * PathPrecision;
						if (avoidEndpointChanged)
						{
							_currentAvoidPath = avoidPoints;
							_currentAvoidPathIndex = 0;
						}

						while (_currentAvoidPathIndex < _currentAvoidPath.Length
						       && me.Location.Distance2DSqr(_currentAvoidPath[_currentAvoidPathIndex]) <= PathPrecision * PathPrecision)
						{
							_currentAvoidPathIndex++;
						}

						if (_currentAvoidPathIndex < _currentAvoidPath.Length)
						{
							var avoidWp = _currentAvoidPath[_currentAvoidPathIndex];
							return MoveAlongGroundPath(me, avoidWp);
						}

						_currentAvoidPath = null;
						_currentAvoidPathIndex = 0;
					}
					else
					{
						_currentAvoidPath = null;
						_currentAvoidPathIndex = 0;

						if (hadAvoidPath)
						{
							var refreshedPath = Navigator.ComputeRawPath(me.Location, _destination);
							if (refreshedPath == null || refreshedPath.Length == 0)
								return MoveResult.PathGenerationFailed;

							OverrideCurrentPath(refreshedPath);
							try { StuckHandler.Reset(); } catch { }
						}
					}
				}

				WoWPoint nextPoint = _currentPath[_currentPathIndex];

				// Water/lava Z+2f lift (HB 6.2.3 step 15)
				if (_currentPolyTypes != null && _currentPathIndex < _currentPolyTypes.Length)
				{
					var polyType = _currentPolyTypes[_currentPathIndex];
					if (polyType == TripperNav.AreaType.Water || polyType == TripperNav.AreaType.Lava)
						nextPoint = new WoWPoint(nextPoint.X, nextPoint.Y, nextPoint.Z + 2f);
				}

				// Push-ahead (HB 6.2.3 method_25/26)
				WoWPoint clickPoint = ComputeClickPoint(me, nextPoint);
				bool isFinalMovePoint = _currentPathIndex == _currentPath.Count - 1;

				if (TryRepathAroundLiveCollision(me, clickPoint, isFinalMovePoint))
					return MoveToCore(destination, precision, destinationName);

				return MoveAlongGroundPath(me, clickPoint);
			}

			return MoveResult.PathGenerationFailed;
		}

		private MoveResult MoveAlongGroundPath(LocalPlayer me, WoWPoint clickPoint)
		{
			bool canRecover = CanUseLocalConnector(me) && !_ridingElevator
				&& !me.Stunned && !me.Fleeing && !me.Dazed && !me.Rooted && !me.IsCasting;
			if (!canRecover)
				_commandedProgress.Reset();
			else if (_commandedProgress.ObserveCommand(me.Location, _destination, DateTime.UtcNow))
			{
				Logging.WriteDiagnostic("[Nav] Ground movement made no progress for 3s despite repeated commands: from={0} waypoint={1} destination={2} moving={3} speed={4:F1} ctm={5}. Trying validated detour, then stuck recovery.",
					me.Location, clickPoint, _destination, me.IsMoving, me.MovementInfo.CurrentSpeed, WoWMovement.ClickToMoveInfo.Type);
				Navigator.PlayerMover.MoveStop();
				if (TryStartLocalConnector(me, _destination))
					return MoveResult.Moved;
				Logging.WriteDiagnostic("[Nav] No supported, clear local detour found; advancing stuck recovery.");
				StuckHandler.Unstick();
				_suppressDriftUntilUtc = DateTime.UtcNow + UnstickDriftGrace;
				return MoveResult.UnstuckAttempt;
			}
			Navigator.PlayerMover.MoveTowards(clickPoint);
			return MoveResult.Moved;
		}


		/// <summary>
		/// Clears all navigation state. HB 6.2.3 MeshNavigator.Clear().
		/// </summary>
		public override bool Clear()
		{
			_commandedProgress.Reset();
			ResetTerminalRouteEvidence();
			try { StuckHandler.Reset(); } catch { }
			if (_localConnectorTarget != WoWPoint.Zero)
				Navigator.PlayerMover.MoveStop();
			LastMoveResult = null;
			LastMoveOrigin = WoWPoint.Zero;
			LastMoveDestination = WoWPoint.Zero;
			LastMoveAttemptUtc = DateTime.MinValue;
			_lastMoveFailureLogUtc = DateTime.MinValue;
			_localConnectorTarget = WoWPoint.Zero;
			_localConnectorDestination = WoWPoint.Zero;
			_localConnectorSearchTimer.Stop();

			_destination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			CancelElevatorTransitMovement();
			_usingDirectSwimMovement = false;
			_currentAvoidPath = null;
			_currentAvoidPathIndex = 0;
			_cachedPushAheadIndex = -1;
			_lastMovePulseUtc = DateTime.MinValue;
			_smoothedMoveIntervalSeconds = 0.15f;
			_suppressDriftUntilUtc = DateTime.MinValue;
			_liveCollisionTracker.Reset();
			_liveCollisionProbeTimer.Stop();
			_liveCollisionRepathTimer.Stop();
			_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500));
			return true;
		}

		private bool TryRepathAroundLiveCollision(
			LocalPlayer me,
			WoWPoint clickPoint,
			bool isFinalMovePoint)
		{
			if (!_liveCollisionProbeTimer.IsFinished)
				return false;
			_liveCollisionProbeTimer.Reset();

			if (!ShouldProbeLiveCollision(
					_ridingElevator,
					me.IsSwimming,
					me.IsFalling,
					isFinalMovePoint,
					me.Location.Distance2DSqr(_destination)))
			{
				_liveCollisionTracker.Reset();
				return false;
			}

			WoWPoint location = me.Location;
			float clickDistance = location.Distance2D(clickPoint);
			float probeDistance = Math.Min(LiveCollisionProbeDistance, clickDistance);
			if (probeDistance < 2f)
			{
				_liveCollisionTracker.Reset();
				return false;
			}

			float scale = probeDistance / clickDistance;
			var probeEnd = new WoWPoint(
				location.X + (clickPoint.X - location.X) * scale,
				location.Y + (clickPoint.Y - location.Y) * scale,
				location.Z + (clickPoint.Z - location.Z) * scale);
			WoWPoint probeStartRaised = location.Add(0f, 0f, 1.2f);
			WoWPoint probeEndRaised = probeEnd.Add(0f, 0f, 1.2f);
			bool traceHit = GameWorld.TraceLine(
				probeStartRaised,
				probeEndRaised,
				GameWorld.CGWorldFrameHitFlags.HitTestWMO
					| GameWorld.CGWorldFrameHitFlags.HitTestBoundingModels,
				out WoWPoint raisedHitPoint);
			float hitDistance = traceHit
				? probeStartRaised.Distance2D(raisedHitPoint)
				: 0f;
			if (traceHit && ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
					.Any(go => IsOpenableDoor(go, me)
						&& go.Location.Distance2DSqr(raisedHitPoint) <= 16f))
			{
				_liveCollisionTracker.Reset();
				return false;
			}

			if (!_liveCollisionTracker.Observe(
					traceHit,
					raisedHitPoint,
					hitDistance,
					probeDistance)
				|| !_liveCollisionRepathTimer.IsFinished)
				return false;

			WoWPoint obstruction = new WoWPoint(
				raisedHitPoint.X,
				raisedHitPoint.Y,
				location.Z);
			ApplyConfirmedLiveCollision(obstruction, hitDistance);
			return true;
		}

		internal static bool ShouldProbeLiveCollision(
			bool ridingElevator,
			bool swimming,
			bool falling,
			bool isFinalMovePoint,
			float destinationDistanceSqr)
		{
			return !ridingElevator
				&& !swimming
				&& !falling
				&& (!isFinalMovePoint || destinationDistanceSqr > 100f);
		}

		internal void ApplyConfirmedLiveCollision(WoWPoint obstruction, float hitDistance)
		{
			Logging.WriteDiagnostic(
				"[Nav] Confirmed live collision {0:F1}y ahead at {1}; blackspotting and regenerating path.",
				hitDistance,
				obstruction);
			if (!BlackspotManager.IsBlackspotted(obstruction))
				BlackspotManager.AddBlackspot(obstruction, 3f, 4f, "LiveCollision");
			_liveCollisionRepathTimer.Reset();
			_currentPath.Clear();
			_currentPathIndex = 0;
			_cachedPushAheadIndex = -1;
			try { StuckHandler.Reset(); } catch { }
		}

		/// <summary>
		/// Generates a navmesh path from player position to destination.
		/// HB 6.2.3 MeshNavigator.GeneratePath.
		/// </summary>
		public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)
		{
			if (!Navigator.IsNavigatorLoaded)
				return Array.Empty<WoWPoint>();

			uint mapId = Navigator.TripperNavigator.CurrentMapId;
			var start = new Vector3(from.X, from.Y, from.Z);
			var end = new Vector3(to.X, to.Y, to.Z);

			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, start, Navigator.LoadTilesAroundRadius);
				TripperNavigator.EnsureTilesAroundPosition(mapId, end, Navigator.LoadTilesAroundRadius);
			}
			catch { }

			BlackspotManager.EnsureBlackspotsMarked();
			ApplyAliveQueryFilter(ObjectManager.Me?.IsAlive ?? true);

			var sw = System.Diagnostics.Stopwatch.StartNew();
			var result = TripperNavigator.FindPath(mapId, start, end, true);
			sw.Stop();
			if (IsSlowPathGeneration(sw.Elapsed))
				Logging.WriteDiagnostic($"[Nav] GeneratePath {sw.ElapsedMilliseconds}ms map={mapId} dist={(end - start).Length():F0}y");

			if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
			{
				var path = new WoWPoint[result.Points.Length];
				for (int i = 0; i < result.Points.Length; i++)
					path[i] = new WoWPoint(result.Points[i].X, result.Points[i].Y, result.Points[i].Z);
				return path;
			}

			return Array.Empty<WoWPoint>();
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.CanNavigateWithin — uses the full PathFindResult endpoint.
		/// </summary>
		public override bool CanNavigateWithin(WoWPoint from, WoWPoint to, float distanceTolerancy)
		{
			TripperNav.PathFindResult result = FindPath(from, to);
			return result.Succeeded
			       && result.Points != null
			       && result.Points.Length != 0
			       && Vector3.DistanceSquared(result.Points[result.Points.Length - 1], new Vector3(to.X, to.Y, to.Z)) < distanceTolerancy * distanceTolerancy;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.CanNavigateFully — partial paths are not fully navigable.
		/// </summary>
		public override bool CanNavigateFully(WoWPoint from, WoWPoint to)
		{
			TripperNav.PathFindResult result = FindPath(from, to);
			return result.Succeeded && !result.IsPartialPath;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.PathDistance — returns null for failed or partial paths.
		/// </summary>
		public override float? PathDistance(WoWPoint from, WoWPoint to, float maxDistance = float.MaxValue)
		{
			TripperNav.PathFindResult result = FindPath(from, to);
			if (!result.Succeeded || result.IsPartialPath || result.Points == null || result.Points.Length == 0)
				return null;

			Vector3 start = new Vector3(from.X, from.Y, from.Z);
			Vector3 end = new Vector3(to.X, to.Y, to.Z);
			Vector3[] points = result.Points;
			float distance = Vector3.Distance(start, points[0]);
			distance += Vector3.Distance(points[points.Length - 1], end);

			for (int i = 0; i < points.Length - 1; i++)
			{
				if (distance > maxDistance)
					return maxDistance;
				distance += Vector3.Distance(points[i], points[i + 1]);
			}

			return distance;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.AtLocation — checks if two points are within PathPrecision.
		/// </summary>
		public override bool AtLocation(WoWPoint point1, WoWPoint point2)
		{
			return point1.Distance2DSqr(point2) <= PathPrecision * PathPrecision
			       && Math.Abs(point1.Z - point2.Z) < 4.5f;
		}

		#endregion

		#region Public properties/accessors

		public WoWPoint Destination => _destination;

		// Only movement calls produce this evidence. Path probes and an exhausted
		// CurrentPath are not evidence that a movement request failed.
		public MoveResult? LastMoveResult { get; private set; }
		public WoWPoint LastMoveOrigin { get; private set; }
		public WoWPoint LastMoveDestination { get; private set; }
		public DateTime LastMoveAttemptUtc { get; private set; } = DateTime.MinValue;
		public long LastMoveAttemptSequence { get; private set; }
		private DateTime _lastMoveFailureLogUtc = DateTime.MinValue;

		public List<WoWPoint> CurrentPath => _currentPath;

		public int CurrentPathIndex => _currentPathIndex;

		public bool HasActivePath => _currentPath.Count > 0 && _currentPathIndex < _currentPath.Count;

		public bool IsRidingElevator => _ridingElevator;

		/// <summary>
		/// Returns the remaining unvisited navmesh waypoints.
		/// Used by Bots.DungeonBuddy.Avoidance.Helpers.GetAvoidPath().
		/// </summary>
		public WoWPoint[] GetRemainingNavPath()
		{
			if (_currentPath.Count == 0 || _currentPathIndex >= _currentPath.Count)
				return Array.Empty<WoWPoint>();
			return _currentPath.Skip(_currentPathIndex).ToArray();
		}

		/// <summary>
		/// Replaces the active navmesh path. Called by Helpers.GetAvoidPath().
		/// HB 6.2.3: CurrentMovePath.Path = FindPath(from, to); Index = 0.
		/// </summary>
		public void OverrideCurrentPath(WoWPoint[] points)
		{
			ResetTerminalRouteEvidence();
			_currentPath.Clear();
			if (points != null)
				foreach (var p in points)
					_currentPath.Add(p);
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			_cachedPushAheadIndex = -1;
		}

		public void SetFactionAreaType(TripperNav.AreaType areaType)
		{
			_factionAreaType = areaType;
		}

		#endregion

		#region Internal — path generation

		private TripperNav.Navigator TripperNavigator => Navigator.TripperNavigator;

		// HB 6.2.3 MeshNavigator.smethod_0: route navigator internal messages to the log.
		private static void OnNavigatorLog(string message) =>
			Logging.WriteDiagnostic(System.Windows.Media.Colors.LightBlue, message);

		/// <summary>
		/// HB 6.2.3 MeshNavigator.FindPath — single entry point for navmesh path queries.
		/// All public methods (GeneratePath, CanNavigateWithin, CanNavigateFully, PathDistance)
		/// and MoveTo's regen / swim-check route through here.
		/// </summary>
		public TripperNav.PathFindResult FindPath(WoWPoint from, WoWPoint to)
		{
			if (!Navigator.IsNavigatorLoaded)
				return new TripperNav.PathFindResult();

			uint mapId = (uint)(ObjectManager.Me?.MapId ?? 0);
			var start = new Vector3(from.X, from.Y, from.Z);
			var end = new Vector3(to.X, to.Y, to.Z);
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			TripperNav.PathFindResult result;
			Navigator.BeginTileLogBatch();
			try
			{
				try
				{
					TripperNavigator.EnsureTilesAroundPosition(mapId, start, Navigator.LoadTilesAroundRadius);
					TripperNavigator.EnsureTilesAroundPosition(mapId, end, Navigator.LoadTilesAroundRadius);
				}
				catch { }

				BlackspotManager.EnsureBlackspotsMarked();
				ApplyAliveQueryFilter(ObjectManager.Me?.IsAlive ?? true);

				result = TripperNavigator.FindPath(mapId, start, end, true);
			}
			finally
			{
				int loadedTiles = Navigator.EndTileLogBatch();
				if (loadedTiles > 0)
					Logging.WriteDiagnostic("[Nav] Loaded {0} mesh tiles for one path query", loadedTiles);
			}
			stopwatch.Stop();
			if (IsSlowPathGeneration(stopwatch.Elapsed))
			{
				Logging.WriteDiagnostic(
					"[Nav] Slow path generation: {0}ms map={1} from={2} to={3} status={4}",
					stopwatch.ElapsedMilliseconds,
					mapId,
					from,
					to,
					result.Status);
			}
			return result;
		}


		#endregion

		#region Internal — push-ahead (HB 6.2.3 method_25/26)

		/// <summary>
		/// HB 6.2.3 MeshNavigator.method_26: push-ahead click target computation.
		/// Extends the click target far enough to bridge the observed movement-command interval,
		/// following the path polyline rather than projecting straight through corners.
		/// Called only for intermediate waypoints (not first, not last) — HB method_25 guard.
		/// </summary>
		private WoWPoint ComputeClickPoint(LocalPlayer me, WoWPoint waypoint)
		{
			if (Navigator.PlayerMover is not ClickToMoveMover)
				return waypoint;

			// HB 6.2.3 method_25: Index > 0 && Index < Path.Points.Length - 1
			if (_currentPathIndex == 0 || _currentPathIndex >= _currentPath.Count - 1)
				return waypoint;

			float lookaheadDistance = CalculateMovementLookahead(
				me.MovementInfo.CurrentSpeed,
				me.MovementInfo.RunSpeed,
				_smoothedMoveIntervalSeconds,
				PathPrecision);
			WoWPoint lookahead = ComputePathLookahead(
				_currentPath,
				_currentPathIndex,
				lookaheadDistance);
			if (lookahead == waypoint)
				return waypoint;

			uint mapId = (uint)me.MapId;
			var waypointVec = new Vector3(waypoint.X, waypoint.Y, waypoint.Z);
			var tightExtents = new Vector3(0.5f, 0.5f, 3f);
			WoWPoint selected = SelectFarthestMovementLookahead(
				_currentPath,
				_currentPathIndex,
				lookaheadDistance,
				me.Location,
				(from, candidate) =>
				{
					var fromVec = new Vector3(from.X, from.Y, from.Z);
					var candidateVec = new Vector3(candidate.X, candidate.Y, candidate.Z);
					try
					{
						TripperNavigator.EnsureTilesAroundPosition(mapId, waypointVec, 0);
						TripperNavigator.EnsureTilesAroundPosition(mapId, candidateVec, 0);
					}
					catch { }
					var status = TripperNavigator.RaycastWithExtents(
						mapId, fromVec, candidateVec, tightExtents,
						out float hitT, out _, out _, out _);
					return status.Succeeded && hitT >= 1.0f;
				});
			return KeepGroundClickTargetAhead(me.Location, waypoint, selected, PathPrecision);
		}

		internal static WoWPoint KeepGroundClickTargetAhead(
			WoWPoint playerLocation,
			WoWPoint currentWaypoint,
			WoWPoint selectedLookahead,
			float pathPrecision)
		{
			float arrivalRadiusSqr = pathPrecision * pathPrecision;
			if (playerLocation.Distance2DSqr(selectedLookahead) <= arrivalRadiusSqr
				&& playerLocation.Distance2DSqr(currentWaypoint) > arrivalRadiusSqr)
			{
				return currentWaypoint;
			}

			return selectedLookahead;
		}

		private void ObserveMovementCadence(DateTime nowUtc)
		{
			if (_lastMovePulseUtc != DateTime.MinValue)
			{
				float elapsedSeconds = (float)(nowUtc - _lastMovePulseUtc).TotalSeconds;
				if (elapsedSeconds >= 0.02f && elapsedSeconds <= 2f)
					_smoothedMoveIntervalSeconds = (_smoothedMoveIntervalSeconds + elapsedSeconds) * 0.5f;
				else if (elapsedSeconds > 2f)
					_smoothedMoveIntervalSeconds = 0.15f;
			}
			_lastMovePulseUtc = nowUtc;
		}

		internal static float CalculateMovementLookahead(
			float currentSpeed,
			float runSpeed,
			float commandIntervalSeconds,
			float pathPrecision)
		{
			float effectiveSpeed = Math.Max(currentSpeed, runSpeed);
			float predictionWindow = MathEx.Clamp(commandIntervalSeconds + 0.4f, 0.45f, 1.35f);
			return MathEx.Clamp(effectiveSpeed * predictionWindow, pathPrecision, 18f);
		}

		internal static WoWPoint SelectFarthestClearLookahead(
			IReadOnlyList<WoWPoint> path,
			int currentIndex,
			float desiredDistance,
			Func<WoWPoint, bool> isClear)
		{
			if (path == null || path.Count == 0 || currentIndex < 0 || currentIndex >= path.Count || isClear == null)
				return WoWPoint.Zero;

			WoWPoint waypoint = path[currentIndex];
			WoWPoint desired = ComputePathLookahead(path, currentIndex, desiredDistance);
			if (isClear(desired))
				return desired;

			float low = 0f;
			float high = Math.Max(0f, desiredDistance);
			WoWPoint best = waypoint;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				float candidateDistance = (low + high) * 0.5f;
				WoWPoint candidate = ComputePathLookahead(path, currentIndex, candidateDistance);
				if (isClear(candidate))
				{
					best = candidate;
					low = candidateDistance;
				}
				else
				{
					high = candidateDistance;
				}
			}
			return best;
		}

		internal static WoWPoint SelectFarthestMovementLookahead(
			IReadOnlyList<WoWPoint> path,
			int currentIndex,
			float desiredDistance,
			WoWPoint playerLocation,
			Func<WoWPoint, WoWPoint, bool> isSegmentClear)
		{
			if (path == null || currentIndex < 0 || currentIndex >= path.Count || isSegmentClear == null)
				return WoWPoint.Zero;

			return SelectFarthestClearLookahead(
				path,
				currentIndex,
				desiredDistance,
				candidate => isSegmentClear(playerLocation, candidate));
		}

		internal static WoWPoint ComputePathLookahead(
			IReadOnlyList<WoWPoint> path,
			int currentIndex,
			float lookaheadDistance)
		{
			if (path == null || path.Count == 0 || currentIndex < 0 || currentIndex >= path.Count)
				return WoWPoint.Zero;

			WoWPoint cursor = path[currentIndex];
			float remaining = Math.Max(0f, lookaheadDistance);
			for (int i = currentIndex + 1; i < path.Count && remaining > 0f; i++)
			{
				WoWPoint next = path[i];
				// Ground CTM completes based on planar travel, so uphill mesh noise must
				// not spend the whole look-ahead budget on a near-zero X/Y target.
				// Downhill Z is different: discounting it can project CTM across a drop
				// or cliff edge. Preserve full 3D cost whenever the path descends.
				float segmentLength = next.Z < cursor.Z
					? cursor.Distance(next)
					: cursor.Distance2D(next);
				if (segmentLength > remaining && segmentLength > 0.001f)
				{
					float scale = remaining / segmentLength;
					return new WoWPoint(
						cursor.X + (next.X - cursor.X) * scale,
						cursor.Y + (next.Y - cursor.Y) * scale,
						cursor.Z + (next.Z - cursor.Z) * scale);
				}

				remaining -= segmentLength;
				cursor = next;
			}

			return cursor;
		}

		#endregion

		#region Internal — start-index skip (HB 6.2.3 method_14)

		private bool HasShortGroundPath(WoWPoint from, WoWPoint to, float maxLength)
		{
			TripperNav.PathFindResult result = FindPath(from, to);
			if (result.Succeeded && !result.IsPartialPath && result.Points != null)
				return ComputePathLength(result.Points) <= maxLength;
			return false;
		}

		internal bool UpdateDirectSwimState(bool isSwimming, bool useDirectSwimming)
		{
			bool leftDirectSwimming = _usingDirectSwimMovement && !isSwimming;
			_usingDirectSwimMovement = isSwimming && useDirectSwimming;
			if (!leftDirectSwimming)
				return false;

			// The direct swim branch targets the final destination and does not consume
			// mesh waypoints. Never resume the pre-water path after the player reaches land.
			_destination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			_cachedPushAheadIndex = -1;
			_liveCollisionTracker.Reset();
			try { StuckHandler.Reset(); } catch { }
			return true;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.method_14: skips waypoints the player has already passed.
		/// Uses navmesh RaycastBlocked from player to each successive waypoint; stops at the
		/// first blocked segment. Special case: off-mesh connection — project player onto the
		/// off-mesh segment and advance if already at the projected point (smethod_3 / method_27).
		/// </summary>
		private void SkipPassedWaypoints(LocalPlayer me)
		{
			if (_currentPath.Count < 2 || me == null || !Navigator.IsNavigatorLoaded)
				return;

			uint mapId = (uint)me.MapId;
			var playerVec = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);

			int idx = 1;
			while (idx < _currentPath.Count)
			{
				// HB method_14: stop at off-mesh connection boundary.
				if (_currentFlags != null && (idx - 1) < _currentFlags.Length
				    && (_currentFlags[idx - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
					break;

				var wpVec = new Vector3(_currentPath[idx].X, _currentPath[idx].Y, _currentPath[idx].Z);
				bool blocked = TripperNavigator.RaycastBlocked(mapId, playerVec, wpVec, out _, _factionAreaType);
				if (blocked)
					break;
				idx++;
			}

			int skipTo = idx - 1;

			// HB method_14 off-mesh special case (smethod_3 / method_27):
			// if we stopped at an off-mesh connection, project the player onto the segment and
			// advance the index if we have already reached the projected point.
			if (skipTo >= 0 && skipTo < _currentPath.Count - 1
			    && _currentFlags != null && skipTo < _currentFlags.Length
			    && (_currentFlags[skipTo] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
			{
				var area = (_currentPolyTypes != null && skipTo < _currentPolyTypes.Length)
					? _currentPolyTypes[skipTo] : TripperNav.AreaType.Ground;

				// Only advance if the off-mesh is a traversal type (not Elevator/Portal/Interact).
				if (area != TripperNav.AreaType.Elevator && area != TripperNav.AreaType.Portal
				    && area != TripperNav.AreaType.DefendersPortal && area != TripperNav.AreaType.HordePortal
				    && area != TripperNav.AreaType.AlliancePortal && area != TripperNav.AreaType.InteractUnit
				    && area != TripperNav.AreaType.InteractObject)
				{
					WoWPoint proj = ProjectOnSegment(me.Location, _currentPath[skipTo], _currentPath[skipTo + 1]);
					if (IsAtPoint(me.Location, proj))
						skipTo++;
				}
			}

			if (skipTo >= 2)
				Logging.WriteDiagnostic("[MeshNavigator] Skipped {0} path nodes", skipTo);
			if (skipTo > 0)
				_currentPathIndex = skipTo;
		}

		#endregion

		#region Internal — alive query filter (HB 6.2.3 method_28)

		private void ApplyAliveQueryFilter(bool isAlive)
		{
			try
			{
				if (!Navigator.IsNavigatorLoaded)
					return;

				ushort onlyWhileAlive = (ushort)TripperNav.AbilityFlags.OnlyWhileAlive;
				ushort include = TripperNavigator.GetIncludeFlags();
				ushort exclude = TripperNavigator.GetExcludeFlags();

				if (isAlive)
				{
					exclude = (ushort)(exclude & ~onlyWhileAlive);
					include = (ushort)(include | onlyWhileAlive);
				}
				else
				{
					include = (ushort)(include & ~onlyWhileAlive);
					exclude = (ushort)(exclude | onlyWhileAlive);
				}

				TripperNavigator.SetIncludeFlags(include);
				TripperNavigator.SetExcludeFlags(exclude);
			}
			catch { }
		}

		#endregion

		#region Internal — off-mesh dispatch (HB 6.2.3 method_18)

		private MoveResult DispatchOffMesh(LocalPlayer me, WoWPoint endPoint, WoWPoint startPoint, TripperNav.AreaType areaType)
		{
			switch (areaType)
			{
				case TripperNav.AreaType.Elevator:
					return HandleElevator(me, endPoint, startPoint);
				case TripperNav.AreaType.Portal:
				case TripperNav.AreaType.DefendersPortal:
				case TripperNav.AreaType.HordePortal:
				case TripperNav.AreaType.AlliancePortal:
					return HandlePortal(me);
				case TripperNav.AreaType.InteractUnit:
					return HandleInteractUnit(me);
				case TripperNav.AreaType.InteractObject:
					return HandleInteractObject(me);
				default:
					return HandleStandardOffMesh(me, endPoint);
			}
		}

		private MoveResult HandleStandardOffMesh(LocalPlayer me, WoWPoint targetPoint)
		{
			if (_currentAbilityFlags != null && (_currentPathIndex - 1) >= 0
			    && (_currentPathIndex - 1) < _currentAbilityFlags.Length)
			{
				var abilityFlags = _currentAbilityFlags[_currentPathIndex - 1];
				if (abilityFlags != 0 && (abilityFlags & (TripperNav.AbilityFlags.Run | TripperNav.AbilityFlags.Jump)) == 0)
				{
					Logging.WriteDiagnostic("Invalid offmesh connection encountered at {0}", me.Location);
					return MoveResult.Failed;
				}
			}

			// HB 6.2.3 method_19 hands off to method_24, whose first statement is the stuck
			// check — the gap crossing is driven by Unstick's jump, not by the connection.
			if (StuckHandler.IsStuck())
			{
				StuckHandler.Unstick();
				_suppressDriftUntilUtc = DateTime.UtcNow + UnstickDriftGrace;
				return MoveResult.UnstuckAttempt;
			}

			WoWPoint moveTarget = targetPoint;
			if (!me.IsSwimming && me.Location.Z - targetPoint.Z > 2.0f)
				moveTarget = new WoWPoint(targetPoint.X, targetPoint.Y, me.Location.Z);

			WoWMovement.ClickToMove(moveTarget);
			return MoveResult.Moved;
		}

		#endregion

		#region Internal — elevator (HB 6.2.3 method_20)

		private bool TryInstallNearbyElevatorShortcut(LocalPlayer me, WoWPoint destination)
		{
			bool routeAlreadyUsesElevator = false;
			if (_currentFlags != null && _currentPolyTypes != null)
			{
				int segmentCount = Math.Min(_currentFlags.Length, _currentPolyTypes.Length);
				for (int index = 0; index < segmentCount; index++)
				{
					if ((_currentFlags[index] & TripperNav.StraightPathFlags.OffMeshConnection) != 0
					    && _currentPolyTypes[index] == TripperNav.AreaType.Elevator)
					{
						routeAlreadyUsesElevator = true;
						break;
					}
				}
			}

			var candidates = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.Where(IsElevatorTransport)
				.Select(go => new { Transport = go, LiveLocation = GetLiveTransportLocation(go) })
				.Where(item => item.LiveLocation != WoWPoint.Empty
				               && ShouldPreferNearbyElevator(
					               me.Location, destination, item.LiveLocation, routeAlreadyUsesElevator))
				.OrderBy(item => item.LiveLocation.Distance2DSqr(me.Location))
				.ToArray();
			WoWPoint[] originalMeshPath = _currentPath.ToArray();
			WoWGameObject? transport = null;
			WoWPoint liveLocation = WoWPoint.Empty;
			WoWPoint[] shortcut = Array.Empty<WoWPoint>();
			bool hasGroundSupport = HasGroundSupport(me);
			foreach (var candidate in candidates)
			{
				if (!TryCreateSafeElevatorShortcut(
						me.Location,
						destination,
						candidate.LiveLocation,
						originalMeshPath,
						out shortcut))
				{
					continue;
				}
				if (!IsElevatorGroundCorridorSafe(me, shortcut[0], hasGroundSupport))
					continue;

				transport = candidate.Transport;
				liveLocation = candidate.LiveLocation;
				break;
			}
			if (transport == null)
			{
				if (candidates.Length > 0)
					Logging.WriteDiagnostic(
						"[Nav] Skipping {0} nearby elevator candidate(s): no source/destination mesh landings were proven.",
						candidates.Length);
				return false;
			}
			_currentPath.Clear();
			_currentPath.AddRange(shortcut);
			_currentPathIndex = 1;
			_currentFlags = new[]
			{
				TripperNav.StraightPathFlags.OffMeshConnection,
				TripperNav.StraightPathFlags.None,
				TripperNav.StraightPathFlags.End
			};
			_currentPolyTypes = new[]
			{
				TripperNav.AreaType.Elevator,
				TripperNav.AreaType.Ground,
				TripperNav.AreaType.Ground
			};
			_currentAbilityFlags = new TripperNav.AbilityFlags[shortcut.Length];
			_isPartialPath = false;
			_cachedPushAheadIndex = -1;
			BeginElevatorTransit(transport, liveLocation, shortcut[0], shortcut[1]);
			Logging.WriteDiagnostic(
				"[Nav] Preferring nearby elevator over ground detour: entry={0} guid={1:X} live={2} from={3} exit={4} destination={5}",
				transport.Entry, transport.Guid, liveLocation, shortcut[0], shortcut[1], destination);
			return true;
		}

		internal static bool ShouldPreferNearbyElevator(
			WoWPoint player,
			WoWPoint destination,
			WoWPoint transport,
			bool routeAlreadyUsesElevator)
		{
			if (routeAlreadyUsesElevator)
				return false;
			float verticalGap = Math.Abs(destination.Z - player.Z);
			if (verticalGap < 12f || player.Distance2D(destination) > 120f)
				return false;
			if (player.Distance2D(transport) > 35f || destination.Distance2D(transport) > 70f)
				return false;

			float minimumZ = Math.Min(player.Z, destination.Z) - 6f;
			float maximumZ = Math.Max(player.Z, destination.Z) + 6f;
			return transport.Z >= minimumZ && transport.Z <= maximumZ;
		}

		internal static bool TryCreateSafeElevatorShortcut(
			WoWPoint player,
			WoWPoint destination,
			WoWPoint transport,
			IReadOnlyList<WoWPoint> originalMeshPath,
			out WoWPoint[] shortcut)
		{
			WoWPoint waitingPoint = SelectDirectionalMeshLanding(
				transport, player, player.Z, originalMeshPath, 3f, 15f);
			WoWPoint exitPoint = SelectDirectionalMeshLanding(
				transport, destination, destination.Z, originalMeshPath, 5f, 15f);
			if (waitingPoint == WoWPoint.Empty || exitPoint == WoWPoint.Empty)
			{
				shortcut = Array.Empty<WoWPoint>();
				return false;
			}

			shortcut = new[] { waitingPoint, exitPoint, destination };
			return true;
		}

		private static WoWPoint SelectDirectionalMeshLanding(
			WoWPoint transport,
			WoWPoint toward,
			float elevation,
			IReadOnlyList<WoWPoint> originalMeshPath,
			float minimumDistance,
			float maximumDistance)
		{
			if (originalMeshPath.Count == 0)
				return WoWPoint.Empty;

			float towardX = toward.X - transport.X;
			float towardY = toward.Y - transport.Y;
			float towardLength = (float)Math.Sqrt(towardX * towardX + towardY * towardY);
			if (towardLength < 0.01f)
				return WoWPoint.Empty;

			var candidate = originalMeshPath
				.Where(point => Math.Abs(point.Z - elevation) < 4.5f)
				.Select(point => new
				{
					Point = point,
					Distance = point.Distance2D(transport),
					ForwardDot = (point.X - transport.X) * towardX
					             + (point.Y - transport.Y) * towardY
				})
				.Where(item => item.Distance >= minimumDistance
				               && item.Distance <= maximumDistance
				               && item.ForwardDot / (item.Distance * towardLength) >= 0.75f)
				.OrderBy(item => item.Distance)
				.FirstOrDefault();
			return candidate == null ? WoWPoint.Empty : candidate.Point;
		}

		internal static WoWPoint SelectLiveTransportLocation(
			WoWPoint reportedLocation,
			Matrix worldMatrix,
			bool usesAnimatedTransportMatrix)
		{
			if (!usesAnimatedTransportMatrix)
				return reportedLocation;

			float[] values =
			{
				worldMatrix.M11, worldMatrix.M12, worldMatrix.M13, worldMatrix.M14,
				worldMatrix.M21, worldMatrix.M22, worldMatrix.M23, worldMatrix.M24,
				worldMatrix.M31, worldMatrix.M32, worldMatrix.M33, worldMatrix.M34,
				worldMatrix.M41, worldMatrix.M42, worldMatrix.M43, worldMatrix.M44
			};
			if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
				return WoWPoint.Empty;
			if (Math.Abs(worldMatrix.M14) > 0.01f
			    || Math.Abs(worldMatrix.M24) > 0.01f
			    || Math.Abs(worldMatrix.M34) > 0.01f
			    || Math.Abs(worldMatrix.M44 - 1f) > 0.01f)
			{
				return WoWPoint.Empty;
			}

			float determinant = ((Matrix4x4)worldMatrix).GetDeterminant();
			if (!float.IsFinite(determinant)
			    || Math.Abs(determinant) <= 0.000001f
			    || Math.Abs(determinant) >= 1000000f)
			{
				return WoWPoint.Empty;
			}

			var liveLocation = new WoWPoint(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
			return float.IsFinite(liveLocation.X)
			       && float.IsFinite(liveLocation.Y)
			       && float.IsFinite(liveLocation.Z)
			       && Math.Abs(liveLocation.X) < 100000f
			       && Math.Abs(liveLocation.Y) < 100000f
			       && Math.Abs(liveLocation.Z) < 100000f
			       && liveLocation.DistanceSqr(WoWPoint.Zero) > 0.01f
			       && liveLocation != WoWPoint.Empty
				? liveLocation
				: WoWPoint.Empty;
		}

		private static bool IsElevatorTransport(WoWGameObject go)
		{
			return go.SubType == WoWGameObjectType.Transport
			       && !ElevatorDoorEntries.Contains(go.Entry);
		}

		private static WoWPoint GetLiveTransportLocation(WoWGameObject transport)
		{
			try
			{
				return SelectLiveTransportLocation(
					transport.Location,
					transport.GetWorldMatrix(),
					transport.SubType == WoWGameObjectType.Transport
					|| transport.SubType == WoWGameObjectType.MapObjectTransport);
			}
			catch
			{
				return WoWPoint.Empty;
			}
		}

		private void BeginElevatorTransit(
			WoWGameObject transport,
			WoWPoint liveLocation,
			WoWPoint startPoint,
			WoWPoint endPoint)
		{
			var startDock = new WoWPoint(liveLocation.X, liveLocation.Y, startPoint.Z);
			var endDock = new WoWPoint(liveLocation.X, liveLocation.Y, endPoint.Z);
			_elevatorTransit.Begin(
				transport.Guid,
				transport.Entry,
				startDock,
				endDock,
				startPoint,
				endPoint);
			_ridingElevator = true;
			_nextElevatorDiagnosticUtc = DateTime.MinValue;
			try { StuckHandler.Reset(); } catch { }
		}

		private WoWGameObject? GetOrLockElevator(
			LocalPlayer me,
			WoWPoint startPoint,
			WoWPoint endPoint)
		{
			if (_elevatorTransit.SelectedTransportGuid != 0UL)
			{
				return ObjectManager.GetObjectByGuid<WoWGameObject>(
					_elevatorTransit.SelectedTransportGuid);
			}

			WoWGameObject? attached = me.Transport;
			WoWGameObject? transport = attached != null && IsElevatorTransport(attached)
				? attached
				: ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
					.Where(IsElevatorTransport)
					.Select(go => new { Transport = go, LiveLocation = GetLiveTransportLocation(go) })
					.Where(item => item.LiveLocation != WoWPoint.Empty
					               && item.LiveLocation.Distance2D(startPoint) <= 40f)
					.OrderBy(item => item.LiveLocation.Distance2DSqr(startPoint))
					.Select(item => item.Transport)
					.FirstOrDefault();
			if (transport == null)
				return null;

			WoWPoint liveLocation = GetLiveTransportLocation(transport);
			if (liveLocation == WoWPoint.Empty)
				return null;

			BeginElevatorTransit(transport, liveLocation, startPoint, endPoint);
			return transport;
		}

		private MoveResult HandleElevator(LocalPlayer me, WoWPoint endPoint, WoWPoint startPoint)
		{
			WoWGameObject? transport = GetOrLockElevator(me, startPoint, endPoint);
			WoWPoint liveLocation = transport == null
				? WoWPoint.Empty
				: GetLiveTransportLocation(transport);
			bool liveLocationAvailable = liveLocation != WoWPoint.Empty;

			if (_elevatorTransit.SelectedTransportGuid == 0UL)
			{
				Navigator.PlayerMover.MoveStop();
				TreeRoot.StatusText = "Waiting for a valid elevator";
				return MoveResult.Moved;
			}

			_ridingElevator = true;
			bool isFalling = me.IsFalling || me.MovementInfo.IsFalling;
			bool hasGroundSupport = HasGroundSupport(me);
			bool attachedToSelectedTransport = me.WoWMovementInfo.TransportGuid
			                                   == _elevatorTransit.SelectedTransportGuid;
			bool needsGroundBoarding = _elevatorTransit.NeedsBoardingCorridor && !attachedToSelectedTransport;
			bool approachPathSafe = !needsGroundBoarding
			                        || IsElevatorGroundCorridorSafe(me, _elevatorTransit.WaitPoint, hasGroundSupport);
			// Check the same live target that MoveToBoard will command. A safe
			// waiting point alone says nothing about the remaining boarding gap.
			bool boardingPathSafe = !needsGroundBoarding
			                        || (liveLocationAvailable
			                            && liveLocation.Distance(_elevatorTransit.StartDock) <= 1.25f
			                            && IsElevatorGroundCorridorSafe(me, liveLocation, hasGroundSupport));
			bool exitPathSafe = !_elevatorTransit.NeedsExitCorridor
			                      || (attachedToSelectedTransport
				                      ? liveLocationAvailable
				                        && liveLocation.Distance(_elevatorTransit.EndDock) <= 1.25f
				                        && IsElevatorGroundCorridorSafe(
					                        me, _elevatorTransit.ExitPoint, hasGroundSupport)
				                      : IsElevatorGroundCorridorSafe(
					                      me, _elevatorTransit.ExitPoint, hasGroundSupport));
			ElevatorTransitDecision decision = _elevatorTransit.Observe(
				DateTime.UtcNow,
				me.Location,
				liveLocation,
				liveLocationAvailable,
				me.WoWMovementInfo.TransportGuid,
				isFalling,
				hasGroundSupport,
				approachPathSafe,
				boardingPathSafe,
				exitPathSafe);

			LogElevatorDecision(
				decision,
				me,
				transport,
				liveLocation,
				hasGroundSupport,
				approachPathSafe,
				boardingPathSafe,
				exitPathSafe);
			switch (decision.Kind)
			{
				case ElevatorTransitAction.MoveToWait:
					TreeRoot.StatusText = "Moving to the elevator waiting point";
					Navigator.PlayerMover.MoveTowards(decision.Target);
					return MoveResult.Moved;

				case ElevatorTransitAction.MoveToBoard:
					TreeRoot.StatusText = "Boarding the docked elevator";
					Navigator.PlayerMover.MoveTowards(decision.Target);
					return MoveResult.Moved;

				case ElevatorTransitAction.Ride:
					Navigator.PlayerMover.MoveStop();
					TreeRoot.StatusText = "Riding the selected elevator";
					return MoveResult.Moved;

				case ElevatorTransitAction.MoveToExit:
					TreeRoot.StatusText = "Moving to the elevator landing";
					Navigator.PlayerMover.MoveTowards(decision.Target);
					return MoveResult.Moved;

				case ElevatorTransitAction.Complete:
					Navigator.PlayerMover.MoveStop();
					_currentPathIndex++;
					ResetElevatorTransit();
					try { StuckHandler.Reset(); } catch { }
					if (_currentPathIndex >= _currentPath.Count)
						return CompletePathOrRecover(me);
					return MoveResult.Moved;

				default:
					Navigator.PlayerMover.MoveStop();
					TreeRoot.StatusText = "Waiting for the selected elevator";
					return MoveResult.Moved;
			}
		}

		private void LogElevatorDecision(
			ElevatorTransitDecision decision,
			LocalPlayer me,
			WoWGameObject? transport,
			WoWPoint liveLocation,
			bool hasGroundSupport,
			bool approachPathSafe,
			bool boardingPathSafe,
			bool exitPathSafe)
		{
			DateTime now = DateTime.UtcNow;
			if (now < _nextElevatorDiagnosticUtc)
				return;

			_nextElevatorDiagnosticUtc = now.AddSeconds(2);
			Logging.WriteDiagnostic(
				"[Nav][Elevator] stage={0} action={1} entry={2} guid={3:X} object={4} live={5} player={6} attached={7:X} falling={8} grounded={9} approachPath={10} boardPath={11} exitPath={12} mounted={13} startDock={14} endDock={15} landing={16}",
				_elevatorTransit.StageName,
				decision.Kind,
				_elevatorTransit.SelectedTransportEntry,
				_elevatorTransit.SelectedTransportGuid,
				transport == null ? "missing" : "present",
				liveLocation,
				me.Location,
				me.WoWMovementInfo.TransportGuid,
				me.IsFalling || me.MovementInfo.IsFalling,
				hasGroundSupport,
				approachPathSafe,
				boardingPathSafe,
				exitPathSafe,
				me.Mounted,
				_elevatorTransit.StartDock,
				_elevatorTransit.EndDock,
				_elevatorTransit.ExitPoint);
		}

		private void ResetElevatorTransit()
		{
			_elevatorTransit.Reset();
			_ridingElevator = false;
			_nextElevatorDiagnosticUtc = DateTime.MinValue;
		}

		private void CancelElevatorTransitMovement()
		{
			if (_elevatorTransit.SelectedTransportGuid != 0UL || _ridingElevator)
				Navigator.PlayerMover.MoveStop();
			ResetElevatorTransit();
		}

		internal bool CancelElevatorTransitIfDestinationChanged(WoWPoint destination)
		{
			if (_elevatorTransit.SelectedTransportGuid == 0UL
			    || _destination == WoWPoint.Zero
			    || _destination.DistanceSqr(destination) <= PathPrecision * PathPrecision)
			{
				return false;
			}

			CancelElevatorTransitMovement();
			return true;
		}

		#endregion

		#region Internal — closed doors (HB 6.2.3 method_7/8/29)

		private bool TryOpenClosedDoor(LocalPlayer me)
		{
			if (!_doorScanTimer.IsFinished)
				return false;

			WoWGameObject? door = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.FirstOrDefault(go => IsOpenableDoor(go, me));

			if (door == null)
			{
				_doorScanTimer.Reset();
				return false;
			}

			if (me.IsMoving)
			{
				WoWMovement.MoveStop();
			}
			else if (!me.IsCasting && _doorInteractTimer.IsFinished)
			{
				Logging.WriteDiagnostic("Opening Closed Door {0} (Id: {1})", door.Name, door.Entry);
				door.Interact();
				_doorInteractTimer.Reset();
			}
			return true;
		}

		private bool IsOpenableDoor(WoWGameObject go, LocalPlayer me)
		{
			try
			{
				if (go.SubType != WoWGameObjectType.Door)
					return false;

				if (go.State != WoWGameObjectState.Ready)
					return false;

				if (go.SubObj is not WoWDoor)
					return false;

				if (!go.WithinInteractRange || go.InUse)
					return false;

				if (!go.CanUse() || !go.CanUseNow())
					return false;

				uint requiredItem = 0;
				LockEntry? lockRecord = go.LockRecord;
				if (lockRecord.HasValue)
				{
					LockEntry lockEntry = lockRecord.Value;
					for (int i = 0; i < lockEntry.Type.Length; i++)
					{
						if (lockEntry.Type[i] == 1 && lockEntry.LockProperties[i] != 0)
						{
							requiredItem = lockEntry.LockProperties[i];
							break;
						}
					}
				}

				if (requiredItem != 0 && me.GetCarriedItemCount(requiredItem) <= 0)
					return false;

				if (go.Locked && requiredItem == 0)
					return false;

				return true;
			}
			catch
			{
				return false;
			}
		}

		#endregion

		#region Internal — portal/interact (HB 6.2.3 method_21/22/23)

		private MoveResult HandlePortal(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			float bestDistSqr = float.MaxValue;
			WoWGameObject? bestPortal = null;

			foreach (var go in ObjectManager.GetObjectsOfType<WoWGameObject>(false, false))
			{
				if (go.SubType == WoWGameObjectType.Goober
				    || go.SubType == WoWGameObjectType.SpellCaster)
				{
					float distSqr = go.Location.DistanceSqr(me.Location);
					if (distSqr < bestDistSqr)
					{
						bestDistSqr = distSqr;
						bestPortal = go;
					}
				}
			}

			if (bestPortal != null && bestPortal.WithinInteractRange)
			{
				Logging.WriteDiagnostic("Interacting with:{0}", bestPortal.Name);
				bestPortal.Interact();
				return MoveResult.Moved;
			}

			Logging.WriteDiagnostic("Could not find portal to take.");
			return MoveResult.Failed;
		}

		private MoveResult HandleInteractUnit(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			if (!_interactTimer.IsFinished)
				return MoveResult.Moved;

			// Sort by distance to offmesh entry point (HB method_22: meshMovePath_0.Path.Points[Index-1])
			WoWPoint offMeshEntry = _currentPathIndex > 0 ? _currentPath[_currentPathIndex - 1] : me.Location;

			var unit = ObjectManager.CachedUnits
				.Where(u => !u.IsDead && !u.IsHostile && !u.PlayerControlled && !u.IsPlayer)
				.OrderBy(u => u.Location.DistanceSqr(offMeshEntry))
				.FirstOrDefault();

			if (unit == null)
			{
				Logging.WriteDiagnostic("Could not find unit to interact with.");
				return MoveResult.Failed;
			}

			if (!unit.WithinInteractRange)
			{
				WoWMovement.ClickToMove(unit.Location);
				return MoveResult.Moved;
			}

			if (me.Mounted)
				Mount.Dismount("InteractUnit in path");
			unit.Interact();
			_interactTimer.Reset();
			return MoveResult.Moved;
		}

		private MoveResult HandleInteractObject(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			if (!_interactTimer.IsFinished)
				return MoveResult.Moved;

			// Sort by distance to offmesh entry point (HB method_21: meshMovePath_0.Path.Points[Index-1])
			WoWPoint offMeshEntry = _currentPathIndex > 0 ? _currentPath[_currentPathIndex - 1] : me.Location;

			var gameObject = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.OrderBy(go => go.Location.DistanceSqr(offMeshEntry))
				.FirstOrDefault(go => go.CanUseNow() && !go.InUse);

			if (gameObject == null)
			{
				_currentPathIndex++;
				return MoveResult.Moved;
			}

			if (!gameObject.WithinInteractRange)
			{
				WoWMovement.ClickToMove(gameObject.Location);
				return MoveResult.Moved;
			}

			if (me.Mounted)
				Mount.Dismount("InteractObject in path");
			gameObject.Interact();
			_interactTimer.Reset();
			return MoveResult.Moved;
		}

		#endregion

		#region Internal — geometry helpers

		private bool IsAtPoint(WoWPoint playerPos, WoWPoint target)
		{
			return playerPos.Distance2DSqr(target) <= PathPrecision * PathPrecision
			       && Math.Abs(playerPos.Z - target.Z) < 4.5f;
		}

		internal static bool HasReachedOrPassedWaypoint(
			WoWPoint player,
			WoWPoint previous,
			WoWPoint waypoint,
			float precision)
		{
			if (Math.Abs(player.Z - waypoint.Z) >= 4.5f)
				return false;

			if (player.Distance2DSqr(waypoint) <= precision * precision)
				return true;

			float dx = waypoint.X - previous.X;
			float dy = waypoint.Y - previous.Y;
			float lengthSquared = dx * dx + dy * dy;
			if (lengthSquared < 0.0001f)
				return false;

			// The player is past B when (P-B) points in the same direction as A→B.
			float beyondDot = (player.X - waypoint.X) * dx + (player.Y - waypoint.Y) * dy;
			if (beyondDot < 0f)
				return false;

			float perpendicularDistance = Math.Abs(
				(player.X - previous.X) * dy - (player.Y - previous.Y) * dx)
				/ (float)Math.Sqrt(lengthSquared);
			float corridorWidth = Math.Max(3f, precision * 2f);
			return perpendicularDistance <= corridorWidth;
		}

		/// <summary>
		/// 2D distance from a point to a line segment (Z ignored).
		/// HB 6.2.3 method_15/smethod_1.
		/// </summary>
		private static float DistanceToLineSegment2D(WoWPoint point, WoWPoint segA, WoWPoint segB)
		{
			float dx = segB.X - segA.X;
			float dy = segB.Y - segA.Y;
			float lenSqr = dx * dx + dy * dy;

			if (lenSqr < 0.0001f)
			{
				float px = point.X - segA.X;
				float py = point.Y - segA.Y;
				return (float)Math.Sqrt(px * px + py * py);
			}

			float t = ((point.X - segA.X) * dx + (point.Y - segA.Y) * dy) / lenSqr;
			t = Math.Max(0f, Math.Min(1f, t));

			float closestX = segA.X + t * dx;
			float closestY = segA.Y + t * dy;

			float ex = point.X - closestX;
			float ey = point.Y - closestY;
			return (float)Math.Sqrt(ex * ex + ey * ey);
		}

		/// HB 4.3.4 Class81.smethod_7: total arc length of a navmesh point array.
		private static float ComputePathLength(Vector3[] points)
		{
			float total = 0f;
			for (int i = 1; i < points.Length; i++)
				total += Vector3.Distance(points[i - 1], points[i]);
			return total;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.smethod_3: project point P onto segment [A, B], clamped to [0, 1].
		/// Used in SkipPassedWaypoints off-mesh special case.
		/// </summary>
		private static WoWPoint ProjectOnSegment(WoWPoint p, WoWPoint a, WoWPoint b)
		{
			float abX = b.X - a.X, abY = b.Y - a.Y, abZ = b.Z - a.Z;
			float abLenSqr = abX * abX + abY * abY + abZ * abZ;
			if (abLenSqr < 0.0001f) return a;
			float t = ((p.X - a.X) * abX + (p.Y - a.Y) * abY + (p.Z - a.Z) * abZ) / abLenSqr;
			if (t < 0f) return a;
			if (t > 1f) return b;
			return new WoWPoint(a.X + abX * t, a.Y + abY * t, a.Z + abZ * t);
		}

		#endregion
	}
}
