using System;
using System.Collections.Generic;
using System.Globalization;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using TreeSharp;

namespace Styx.Bot.Quest_Behaviors.ArelionsMistress
{
    /// <summary>Original335 quest9472: acquire wine, reward9483, follow Viera, use the scroll and observe credit.</summary>
    public sealed class ArelionsMistress : CustomForcedBehavior
    {
        private readonly QuestRecoveryKey _key = QuestRecoveryKey.ForObjective(9472, 0);
        private ArelionLureRuntime _runtime;
        private ArelionLureController _controller;
        private long _attempt;
        private bool _disposed, _done;
        private double _unknownSince = double.NaN, _nextLog;
        private string _lastPhase = "";

        public ArelionsMistress(Dictionary<string, string> args) : base(args)
        {
            if (args == null || !args.TryGetValue("QuestId", out string value)
                || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id != 9472)
                throw new ArgumentException("ArelionsMistress requires the exact original quest9472 contract.", nameof(args));
        }
        public override string SubversionId => "$Id: ArelionsMistress.cs TC335 quest9472/9483 $";
        public override string SubversionRevision => "$Revision: source-bound-1 $";
        public override bool IsDone => _disposed || _done;
        public override void OnStart() { }
        protected override Composite CreateBehavior() => new TreeSharp.Action(_ => TickOwned());

        private bool Admitted() => !_disposed && !_done && TreeRoot.IsRunning && _attempt > 0
            && QuestRecoveryManager.Instance.OwnsAttempt(_key, _attempt);

        private RunStatus TickOwned()
        {
            if (_disposed || _done) return RunStatus.Success;
            try
            {
                if (_attempt == 0)
                {
                    var claim = QuestRecoveryManager.Instance.TryBeginAttempt(_key, QuestRecoveryRuntime.Capture());
                    if (!claim.MayAttempt || claim.AttemptGeneration <= 0) { _done = true; return RunStatus.Success; }
                    _attempt = claim.AttemptGeneration;
                }
                if (!Admitted()) return Finish(QuestWorkflowStatus.Revoked, "The objective attempt owner changed.");
                if (_runtime == null)
                {
                    _runtime = new ArelionLureRuntime(Admitted);
                    _controller = new ArelionLureController(_runtime);
                }
                var status = _controller.Tick();
                _unknownSince = double.NaN;
                double now = Environment.TickCount64 / 1000.0;
                if (_lastPhase != _controller.Phase || now >= _nextLog)
                {
                    _lastPhase = _controller.Phase; _nextLog = now + 10;
                    Logging.Write("[ArelionsMistress] phase={0}; reason={1}; request-is-not-quest-credit", _controller.Phase, _controller.Reason);
                }
                return status == QuestWorkflowStatus.Running ? RunStatus.Running : Finish(status, _controller.Reason);
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (error is not ObservationUnavailableException) throw;
                double now = Environment.TickCount64 / 1000.0;
                if (double.IsNaN(_unknownSince)) _unknownSince = now;
                if (now - _unknownSince >= 30) return Finish(QuestWorkflowStatus.Deferred, "Required observation unavailable: " + error.Message);
                return RunStatus.Running;
            }
        }

        private RunStatus Finish(QuestWorkflowStatus status, string reason)
        {
            var runtime = _runtime;
            _runtime = null; _controller = null;
            if (_attempt > 0)
            {
                if (status == QuestWorkflowStatus.Deferred)
                    QuestRecoveryManager.Instance.TryDeferBehaviorAttempt(_key, _attempt, QuestRecoveryRuntime.Capture(), reason);
                else QuestRecoveryManager.Instance.AbandonAttempt(_key, _attempt);
            }
            _done = true;
            runtime?.Dispose();
            Logging.Write("[ArelionsMistress] {0}: {1}", status, reason);
            return RunStatus.Success;
        }
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var runtime = _runtime; _runtime = null; _controller = null;
            try { runtime?.Dispose(); }
            finally
            {
                if (_attempt > 0) QuestRecoveryManager.Instance.AbandonAttempt(_key, _attempt);
                base.Dispose();
            }
        }
    }
}
