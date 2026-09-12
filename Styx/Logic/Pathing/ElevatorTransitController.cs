using System;
using Styx.WoWInternals;

namespace Styx.Logic.Pathing
{
	internal enum ElevatorTransitAction
	{
		MoveToWait,
		Wait,
		MoveToBoard,
		Ride,
		MoveToExit,
		Complete
	}

	internal readonly struct ElevatorTransitDecision
	{
		internal ElevatorTransitDecision(ElevatorTransitAction kind, WoWPoint target)
		{
			Kind = kind;
			Target = target;
		}

		internal ElevatorTransitAction Kind { get; }
		internal WoWPoint Target { get; }
	}

	/// <summary>
	/// Side-effect-free state machine for one vertical transport crossing. Runtime
	/// object lookup and movement remain in MeshNavigator so this policy can be
	/// regression-tested without a running game client.
	/// </summary>
	internal sealed class ElevatorTransitController
	{
		private static readonly TimeSpan DockConfirmationDuration = TimeSpan.FromMilliseconds(750);
		private const float DockDistanceTolerance = 1.25f;
		private const float DockMotionTolerance = 0.05f;
		private const float LandingDistanceTolerance = 3f;
		private const float LandingVerticalTolerance = 4.5f;

		private ElevatorTransitStage _stage;
		private bool _active;
		private DateTime _dockCandidateSinceUtc;
		private WoWPoint _dockCandidateOrigin;
		private bool _hasDockCandidate;

		internal ulong SelectedTransportGuid { get; private set; }
		internal uint SelectedTransportEntry { get; private set; }
		internal WoWPoint StartDock { get; private set; }
		internal WoWPoint EndDock { get; private set; }
		internal WoWPoint WaitPoint { get; private set; }
		internal WoWPoint ExitPoint { get; private set; }
		internal string StageName => _stage.ToString();
		internal bool NeedsBoardingCorridor => _stage == ElevatorTransitStage.Approach
		                                       || _stage == ElevatorTransitStage.Boarding;
		internal bool NeedsExitCorridor => _stage == ElevatorTransitStage.Riding
		                                   || _stage == ElevatorTransitStage.Exiting;

		internal void Begin(
			ulong selectedTransportGuid,
			uint selectedTransportEntry,
			WoWPoint startDock,
			WoWPoint endDock,
			WoWPoint waitPoint,
			WoWPoint exitPoint)
		{
			if (selectedTransportGuid == 0UL)
				throw new ArgumentOutOfRangeException(nameof(selectedTransportGuid));

			SelectedTransportGuid = selectedTransportGuid;
			SelectedTransportEntry = selectedTransportEntry;
			StartDock = startDock;
			EndDock = endDock;
			WaitPoint = waitPoint;
			ExitPoint = exitPoint;
			_stage = ElevatorTransitStage.Approach;
			_active = true;
			ResetDockCandidate();
		}

		internal void Reset()
		{
			_active = false;
			SelectedTransportGuid = 0UL;
			SelectedTransportEntry = 0U;
			_stage = ElevatorTransitStage.Approach;
			ResetDockCandidate();
		}

		internal ElevatorTransitDecision Observe(
			DateTime observedAtUtc,
			WoWPoint playerLocation,
			WoWPoint liveTransportLocation,
			bool liveLocationAvailable,
			ulong attachedTransportGuid,
			bool isFalling,
			bool hasGroundSupport,
			bool boardingPathSafe,
			bool exitPathSafe)
		{
			if (!_active)
				throw new InvalidOperationException("Elevator transit has not begun.");

			bool attachedToSelected = attachedTransportGuid == SelectedTransportGuid;
			bool attachedToDifferentTransport = attachedTransportGuid != 0UL && !attachedToSelected;

			if ((_stage == ElevatorTransitStage.Approach || _stage == ElevatorTransitStage.Boarding)
			    && attachedToSelected)
			{
				_stage = ElevatorTransitStage.Riding;
				ResetDockCandidate();
				return Decision(ElevatorTransitAction.Ride);
			}

			switch (_stage)
			{
				case ElevatorTransitStage.Approach:
				case ElevatorTransitStage.Boarding:
					if (isFalling || !hasGroundSupport || attachedToDifferentTransport || !boardingPathSafe)
					{
						// Boarding is a continuing authorization, not a one-time permission.
						// Lost safety evidence also invalidates the previous stable-dock dwell.
						ResetDockCandidate();
						return Decision(ElevatorTransitAction.Wait);
					}

					if (_stage == ElevatorTransitStage.Approach
					    && (playerLocation.Distance2D(WaitPoint) > LandingDistanceTolerance
					        || Math.Abs(playerLocation.Z - WaitPoint.Z) > LandingVerticalTolerance))
					{
						_stage = ElevatorTransitStage.Approach;
						ResetDockCandidate();
						return Decision(ElevatorTransitAction.MoveToWait, WaitPoint);
					}

					if (!liveLocationAvailable || liveTransportLocation.Distance(StartDock) > DockDistanceTolerance)
					{
						bool wasBoarding = _stage == ElevatorTransitStage.Boarding;
						_stage = ElevatorTransitStage.Approach;
						ResetDockCandidate();
						return wasBoarding && boardingPathSafe
							? Decision(ElevatorTransitAction.MoveToWait, WaitPoint)
							: Decision(ElevatorTransitAction.Wait);
					}

					if (!HasStableDockObservation(liveTransportLocation, observedAtUtc))
						return Decision(ElevatorTransitAction.Wait);
					_stage = ElevatorTransitStage.Boarding;
					return Decision(ElevatorTransitAction.MoveToBoard, liveTransportLocation);

				case ElevatorTransitStage.Riding:
					if (!attachedToSelected)
						return Decision(ElevatorTransitAction.Wait);
					if (!liveLocationAvailable || liveTransportLocation.Distance(EndDock) > DockDistanceTolerance)
					{
						ResetDockCandidate();
						return Decision(ElevatorTransitAction.Ride);
					}
					if (!HasStableDockObservation(liveTransportLocation, observedAtUtc))
						return Decision(ElevatorTransitAction.Ride);
					if (!exitPathSafe)
						return Decision(ElevatorTransitAction.Ride);

					_stage = ElevatorTransitStage.Exiting;
					return Decision(ElevatorTransitAction.MoveToExit, ExitPoint);

				case ElevatorTransitStage.Exiting:
					if (attachedToDifferentTransport || isFalling)
						return Decision(ElevatorTransitAction.Wait);

					if (attachedToSelected)
					{
						if (!liveLocationAvailable || liveTransportLocation.Distance(EndDock) > DockDistanceTolerance)
						{
							_stage = ElevatorTransitStage.Riding;
							ResetDockCandidate();
							return Decision(ElevatorTransitAction.Ride);
						}
						if (!exitPathSafe)
							return Decision(ElevatorTransitAction.Wait);
						return Decision(ElevatorTransitAction.MoveToExit, ExitPoint);
					}

					if (isFalling
					    || !hasGroundSupport
					    || !exitPathSafe
					    || Math.Abs(playerLocation.Z - ExitPoint.Z) > LandingVerticalTolerance)
						return Decision(ElevatorTransitAction.Wait);
					if (playerLocation.Distance2D(ExitPoint) <= LandingDistanceTolerance)
						return Decision(ElevatorTransitAction.Complete);
					return Decision(ElevatorTransitAction.MoveToExit, ExitPoint);

				default:
					return Decision(ElevatorTransitAction.Wait);
			}
		}

		private bool HasStableDockObservation(WoWPoint liveTransportLocation, DateTime observedAtUtc)
		{
			if (!_hasDockCandidate || _dockCandidateOrigin.Distance(liveTransportLocation) > DockMotionTolerance)
			{
				_hasDockCandidate = true;
				_dockCandidateOrigin = liveTransportLocation;
				_dockCandidateSinceUtc = observedAtUtc;
				return false;
			}

			return observedAtUtc - _dockCandidateSinceUtc >= DockConfirmationDuration;
		}

		private void ResetDockCandidate()
		{
			_hasDockCandidate = false;
			_dockCandidateOrigin = default;
			_dockCandidateSinceUtc = DateTime.MinValue;
		}

		private static ElevatorTransitDecision Decision(
			ElevatorTransitAction kind,
			WoWPoint target = default)
		{
			return new ElevatorTransitDecision(kind, target);
		}

		private enum ElevatorTransitStage
		{
			Approach,
			Boarding,
			Riding,
			Exiting
		}
	}
}
