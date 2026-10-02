using System;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	/// <summary>
		/// Moves toward the currently owned POI. Cached values suppress duplicate
		/// diagnostics; they never supply a destination for a later world context.
	/// </summary>
	public class ActionMoveToPoi : NavigationAction
	{
		private WoWPoint _lastLocation = WoWPoint.Empty;
		private ulong _lastGuid;
			private bool _hasLoggedMove;
				private readonly Func<bool>? _callerCurrent;
				private GroundTransition? _groundApproach;

			public ActionMoveToPoi() { }

			public ActionMoveToPoi(Func<bool> callerCurrent)
			{
				_callerCurrent = callerCurrent ?? throw new ArgumentNullException(nameof(callerCurrent));
			}

			protected override RunStatus Run(object context)
			{
				if (_callerCurrent != null && !_callerCurrent()) return RunStatus.Failure;
				LocalPlayer? actor = ObjectManager.Me;
				WoWUnit? mover = WoWMovement.ActiveMover;
				BotPoi poi = BotPoi.Current;
				ulong actorGuid = actor?.Guid ?? 0, moverGuid = mover?.Guid ?? 0;
				uint map = actor?.MapId ?? 0;
				var provider = Navigator.NavigationProvider;
				PoiType type = poi?.Type ?? PoiType.None;
				ulong poiGuid = poi?.Guid ?? 0;
				uint entry = poi?.Entry ?? 0;
				bool ContextCurrent() => actor != null && actorGuid != 0 && mover != null && moverGuid != 0
					&& ReferenceEquals(ObjectManager.Me, actor) && actor.Guid == actorGuid && actor.MapId == map
					&& actor.IsValid && actor.IsAlive && !actor.OnTaxi && !actor.IsOnTransport
					&& !actor.IsCasting && actor.ChanneledCastingSpellId == 0
					&& ReferenceEquals(WoWMovement.ActiveMover, mover) && mover.IsValid && mover.Guid == moverGuid
					&& poi != null && ReferenceEquals(BotPoi.Current, poi) && poi.Type == type
					&& poi.Guid == poiGuid && poi.Entry == entry && ReferenceEquals(Navigator.NavigationProvider, provider);
				if (!ContextCurrent()) { _hasLoggedMove = false; return RunStatus.Failure; }

				WoWObject? subject = poi.AsObject;
				WoWUnit? unit = subject?.ToUnit();
				ulong subjectGuid = subject?.Guid ?? 0;
				bool? alive = unit?.IsAlive;
				WoWPoint destination = subject != null ? subject.Location : poi.Location;
					bool Current() => ContextCurrent() && (_callerCurrent?.Invoke() ?? true) && ContextCurrent()
						&& ReferenceEquals(poi.AsObject, subject)
					&& (subject == null || subjectGuid != 0 && subject.IsValid && subject.Guid == subjectGuid
						&& (poiGuid == 0 || poiGuid == subjectGuid) && unit?.IsAlive == alive)
					&& (subject != null ? subject.Location : poi.Location).Equals(destination) && ContextCurrent();
				if (!Finite(destination) || !Current()) { _hasLoggedMove = false; return RunStatus.Failure; }

				if (!_hasLoggedMove || _lastGuid != subjectGuid || !_lastLocation.Equals(destination))
				{
					Logging.Write("Moving to {0}", poi);
					if (!Current()) { _hasLoggedMove = false; return RunStatus.Failure; }
					_lastGuid = subjectGuid;
					_lastLocation = destination;
					_hasLoggedMove = true;
				}
					if (!Current()) return RunStatus.Failure;
					if (RequiresGroundApproach(type))
					{
						if (_groundApproach == null)
						{
							_groundApproach = new GroundTransition(GroundTransitionPurpose.Interaction);
							CleanupHandlers.Push(new ApproachCleanup(this, context, _groundApproach));
						}
						GroundTransitionState result = _groundApproach.Tick(destination, subject, Current);
						if (!Current() || result == GroundTransitionState.Revoked) return RunStatus.Failure;
						// A retained action owns the exterior/landing/ground legs. Neither
						// a flight request nor an unavailable route completes this action.
						return result == GroundTransitionState.Ready ? RunStatus.Success : RunStatus.Running;
					}
					Flightor.MoveTo(destination);
				// The void Flightor API only establishes dispatch. A handled tick is
				// not physical arrival or a successful native/mesh route receipt.
				return Current() ? RunStatus.Success : RunStatus.Failure;
				}

				private static bool RequiresGroundApproach(PoiType type) => type is PoiType.QuestPickUp
					or PoiType.QuestTurnIn or PoiType.Buy or PoiType.Sell or PoiType.Repair
					or PoiType.Train or PoiType.Mail or PoiType.Fly or PoiType.InnKeeper;

				private sealed class ApproachCleanup : CleanupHandler
				{
					private readonly ActionMoveToPoi _action;
					private readonly GroundTransition _approach;
					internal ApproachCleanup(ActionMoveToPoi action, object context, GroundTransition approach)
						: base(action, context) { _action = action; _approach = approach; }
					protected override void DoCleanup(object context)
					{
						// Detach before stopping input: a callback may start another run.
						if (ReferenceEquals(_action._groundApproach, _approach)) _action._groundApproach = null;
						_approach.Cancel();
					}
				}

			private static bool Finite(WoWPoint point) => point != WoWPoint.Zero && point != WoWPoint.Empty
				&& !float.IsNaN(point.X) && !float.IsInfinity(point.X)
				&& !float.IsNaN(point.Y) && !float.IsInfinity(point.Y)
				&& !float.IsNaN(point.Z) && !float.IsInfinity(point.Z);
	}
}
