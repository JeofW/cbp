using Styx;
using Styx.Helpers;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Drawing;
using System.Linq;

namespace VendorRecovery
{
    /// <summary>
    /// Prevents an invalid Sell/Repair vendor from trapping the bot in an
    /// endless interaction loop. Failed profile vendors are blacklisted for
    /// the current profile session so the next configured vendor can be used.
    /// </summary>
    public class VendorRecoveryPlugin : HBPlugin
    {
        private static readonly TimeSpan FailureTimeout = TimeSpan.FromSeconds(35);

        private DateTime? _failureWindowStarted;
        private uint _trackedEntry;
        private PoiType _trackedType;
        private DateTime _lastPulse = DateTime.MinValue;

        public override string Name { get { return "Vendor Recovery"; } }
        public override string Author { get { return "Codex"; } }
        public override Version Version { get { return new Version(1, 0, 0); } }
        public override bool WantButton { get { return false; } }

        public override void OnEnable()
        {
            Reset();
            Logging.Write(
                "[VendorRecovery] Enabled. Failed Sell/Repair vendors will be skipped after {0} seconds at the NPC.",
                (int)FailureTimeout.TotalSeconds);
        }

        public override void OnDisable()
        {
            Reset();
        }

        public override void Pulse()
        {
            // The plugin manager pulses much faster than this guard needs.
            if (DateTime.UtcNow - _lastPulse < TimeSpan.FromMilliseconds(250))
                return;
            _lastPulse = DateTime.UtcNow;

            try
            {
                PoiType poiType = BotPoi.Current.Type;
                if (poiType != PoiType.Sell && poiType != PoiType.Repair)
                {
                    Reset();
                    return;
                }

                uint entry = BotPoi.Current.Entry;
                if (entry == 0 || entry != _trackedEntry || poiType != _trackedType)
                {
                    _trackedEntry = entry;
                    _trackedType = poiType;
                    _failureWindowStarted = null;
                }

                // A visible merchant frame proves this vendor works.
                if (MerchantFrame.Instance.IsVisible)
                {
                    _failureWindowStarted = null;
                    return;
                }

                if (StyxWoW.Me == null
                    || StyxWoW.Me.Dead
                    || StyxWoW.Me.Combat
                    || BotPoi.Current.AsObject == null
                    || !BotPoi.Current.AsObject.WithinInteractRange)
                {
                    _failureWindowStarted = null;
                    return;
                }

                if (!_failureWindowStarted.HasValue)
                {
                    _failureWindowStarted = DateTime.UtcNow;
                    Logging.Write(
                        "[VendorRecovery] Reached {0} ({1}); waiting for the merchant window.",
                        BotPoi.Current.Name, entry);
                    return;
                }

                if (DateTime.UtcNow - _failureWindowStarted.Value < FailureTimeout)
                    return;

                BlacklistCurrentVendor();
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[VendorRecovery] Pulse error: {0}", ex);
                Reset();
            }
        }

        private void BlacklistCurrentVendor()
        {
            Profile profile = ProfileManager.CurrentProfile;
            VendorManager manager = profile != null ? profile.VendorManager : null;
            Vendor vendor = BotPoi.Current.AsVendor;

            if (vendor == null && manager != null && manager.AllVendors != null)
            {
                vendor = manager.AllVendors.FirstOrDefault(
                    candidate => candidate.Entry == (int)_trackedEntry);
            }

            if (manager != null && vendor != null)
                manager.Blacklist.Add(vendor);

            VendorManager.RejectVendor((int)_trackedEntry, "merchant window did not open");

            TryAddVisibleReplacement(manager);

            if (GossipFrame.Instance.IsVisible)
                GossipFrame.Instance.Close();

            if (StyxWoW.Me.GotTarget)
                StyxWoW.Me.ClearTarget();

            Logging.Write(
                Color.Orange,
                "[VendorRecovery] Merchant window did not open for {0} ({1}) after {2} seconds. Blacklisting it for this profile session and trying the next vendor.",
                BotPoi.Current.Name,
                _trackedEntry,
                (int)FailureTimeout.TotalSeconds);

            BotPoi.Clear("Vendor Recovery timed out");
            Reset();
        }

        private void TryAddVisibleReplacement(VendorManager manager)
        {
            if (manager == null || manager.AllVendors == null)
                return;

            bool hasConfiguredFallback = manager.AllVendors.Any(candidate =>
                !manager.IsBlacklisted(candidate)
                && candidate.Entry != (int)_trackedEntry
                && (_trackedType == PoiType.Repair
                    ? candidate.Type == Vendor.VendorType.Repair
                    : candidate.Type == Vendor.VendorType.Sell
                        || candidate.Type == Vendor.VendorType.Repair
                        || candidate.Type == Vendor.VendorType.Ammo));

            if (hasConfiguredFallback)
                return;

            WoWUnit replacement = ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(unit =>
                    unit.Entry != _trackedEntry
                    && !VendorSafetyPolicy.IsRejected((int)unit.Entry)
                    && !unit.Dead
                    && unit.IsFriendly
                    && (_trackedType == PoiType.Repair
                        ? unit.IsRepairMerchant
                        : unit.IsVendor))
                .OrderBy(unit => unit.DistanceSqr)
                .FirstOrDefault();

            if (replacement == null)
                return;

            Vendor.VendorType replacementType = _trackedType == PoiType.Repair
                ? Vendor.VendorType.Repair
                : Vendor.VendorType.Sell;

            Vendor discovered = new Vendor(replacement, replacementType);
            manager.AllVendors.Add(discovered);

            Logging.Write(
                Color.Yellow,
                "[VendorRecovery] Discovered nearby replacement vendor {0} ({1}) and added it for this profile session.",
                replacement.Name,
                replacement.Entry);
        }

        private void Reset()
        {
            _failureWindowStarted = null;
            _trackedEntry = 0;
            _trackedType = PoiType.None;
        }
    }
}
