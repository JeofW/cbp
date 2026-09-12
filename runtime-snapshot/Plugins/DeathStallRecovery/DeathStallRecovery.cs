using Styx;
using Styx.CommonBot;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Drawing;
using System.Threading;

namespace DeathStallRecovery
{
    /// <summary>
    /// Stops the bot when corpse recovery has made no movement progress for
    /// an extended period. This prevents a blocked native path-generation
    /// call from leaving Questing apparently running forever.
    /// </summary>
    public sealed class DeathStallRecoveryPlugin : HBPlugin
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
        private const float ProgressDistance = 3f;

        private readonly object _stateLock = new object();
        private Timer _watchdogTimer;
        private DateTime _deathObservedAt;
        private DateTime _lastProgressAt;
        private WoWPoint _lastLocation;
        private string _lastLocationText;
        private bool _trackingDeath;
        private int _stopIssued;

        public override string Name { get { return "Death Stall Recovery"; } }
        public override string Author { get { return "Codex"; } }
        public override Version Version { get { return new Version(1, 0, 0); } }
        public override bool WantButton { get { return false; } }

        public override void OnEnable()
        {
            Reset();
            Timer replacement = new Timer(CheckForStall, null, CheckInterval, CheckInterval);
            Timer previous = Interlocked.Exchange(ref _watchdogTimer, replacement);
            if (previous != null)
                previous.Dispose();

            Logging.Write(
                "[DeathStallRecovery] Enabled. The bot will stop if corpse recovery makes no movement progress for {0} seconds.",
                (int)StallTimeout.TotalSeconds);
        }

        public override void OnDisable()
        {
            Timer timer = Interlocked.Exchange(ref _watchdogTimer, null);
            if (timer != null)
                timer.Dispose();

            Reset();
        }

        public override void Pulse()
        {
            try
            {
                LocalPlayer me = StyxWoW.Me;
                if (!TreeRoot.IsRunning || me == null || (!me.Dead && !me.IsGhost))
                {
                    Reset();
                    return;
                }

                DateTime now = DateTime.UtcNow;
                WoWPoint location = me.Location;

                lock (_stateLock)
                {
                    if (!_trackingDeath)
                    {
                        _trackingDeath = true;
                        _deathObservedAt = now;
                        _lastProgressAt = now;
                        _lastLocation = location;
                        _lastLocationText = location.ToString();
                        Interlocked.Exchange(ref _stopIssued, 0);

                        Logging.Write(
                            Color.Yellow,
                            "[DeathStallRecovery] Death detected at {0}; monitoring corpse-recovery movement.",
                            _lastLocationText);
                        return;
                    }

                    if (_lastLocation.Distance(location) >= ProgressDistance)
                    {
                        _lastLocation = location;
                        _lastLocationText = location.ToString();
                        _lastProgressAt = now;
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[DeathStallRecovery] Pulse error: {0}", ex);
            }
        }

        private void CheckForStall(object state)
        {
            try
            {
                if (!TreeRoot.IsRunning)
                    return;

                DateTime deathObservedAt;
                DateTime lastProgressAt;
                string lastLocationText;

                lock (_stateLock)
                {
                    if (!_trackingDeath)
                        return;

                    deathObservedAt = _deathObservedAt;
                    lastProgressAt = _lastProgressAt;
                    lastLocationText = _lastLocationText;
                }

                DateTime now = DateTime.UtcNow;
                if (now - lastProgressAt < StallTimeout)
                    return;

                // Pulse may have observed a resurrection or fresh movement
                // after the snapshot above. Never stop based on stale state.
                lock (_stateLock)
                {
                    if (!_trackingDeath || _lastProgressAt != lastProgressAt)
                        return;
                }

                if (Interlocked.Exchange(ref _stopIssued, 1) != 0)
                    return;

                Logging.Write(
                    Color.Red,
                    "[DeathStallRecovery] Corpse recovery stalled: no movement for {0} seconds (dead for {1} seconds, last location {2}). Stopping the bot. Resurrect manually before restarting.",
                    (int)(now - lastProgressAt).TotalSeconds,
                    (int)(now - deathObservedAt).TotalSeconds,
                    lastLocationText);

                TreeRoot.Stop("Corpse recovery stalled. Resurrect manually before restarting.");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _stopIssued, 0);
                Logging.WriteDebug("[DeathStallRecovery] Watchdog error: {0}", ex);
            }
        }

        private void Reset()
        {
            lock (_stateLock)
            {
                _trackingDeath = false;
                _deathObservedAt = DateTime.MinValue;
                _lastProgressAt = DateTime.MinValue;
                _lastLocation = WoWPoint.Zero;
                _lastLocationText = "(unknown)";
            }

            Interlocked.Exchange(ref _stopIssued, 0);
        }
    }
}
