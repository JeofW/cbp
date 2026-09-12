#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Manages vendor NPCs from profiles.
    /// </summary>
    public class VendorManager
    {
        private readonly List<Vendor> _filteredVendors;

        /// <summary>
        /// Creates an empty vendor manager.
        /// </summary>
        public VendorManager()
        {
            Blacklist = new HashSet<Vendor>();
            AllVendors = new List<Vendor>();
            ForcedVendors = new List<Vendor>();
        }

        /// <summary>
        /// Creates a vendor manager from XML.
        /// </summary>
        public VendorManager(XElement element) : this()
        {
            foreach (var child in element.Elements().ToList())
            {
                try
                {
                    if (child.Name != "Vendor")
                        throw new ProfileUnknownElementException(child, "Vendor");

                    AllVendors.Add(new Vendor(child));
                }
                catch (ProfileException ex)
                {
                    Logging.WriteException(ex);
                }
            }
            _filteredVendors = AllVendors;
        }

        /// <summary>
        /// Gets or sets forced vendors that override profile vendors.
        /// </summary>
        public List<Vendor> ForcedVendors { get; set; }

        /// <summary>
        /// Gets all vendors in the profile.
        /// </summary>
        public List<Vendor> AllVendors { get; private set; }

        /// <summary>
        /// Gets vendors grouped by type, excluding blacklisted.
        /// </summary>
        public Lookup<Vendor.VendorType, Vendor> Vendors
        {
            get
            {
                if (_filteredVendors == null)
                    return null;
                // Filter blacklisted vendors at query time instead of mutating _filteredVendors,
                // so vendors that are later un-blacklisted remain available.
                return (Lookup<Vendor.VendorType, Vendor>)_filteredVendors
                    .Where(v => !IsBlacklisted(v) && IsUsable(v))
                    .ToLookup(v => v.Type);
            }
        }

        /// <summary>
        /// Gets the blacklisted vendors.
        /// </summary>
        public HashSet<Vendor> Blacklist { get; private set; }

        public bool IsBlacklisted(Vendor vendor) => VendorSafetyPolicy.IsRejected(vendor.Entry) ||
            Blacklist.Any(failed => failed.Entry == vendor.Entry);

        public static void RejectVendor(int entry, string reason)
        {
            if (VendorSafetyPolicy.Reject(entry))
                Logging.Write("[VendorSafety] Skipping NPC {0} for this session: {1}. Trying the next suitable NPC.", entry, reason);
        }

        /// <summary>Validate newly visible NPCs before moving closer or interacting.</summary>
        public static bool RejectInvalidCurrentVendor()
        {
            var poi = Styx.Logic.POI.BotPoi.Current;
            if (!VendorSafetyPolicy.IsService(poi.Type)) return false;
            if (VendorSafetyPolicy.IsRejected((int)poi.Entry)) return true;
            var unit = poi.AsUnit;
            if (unit == null || !unit.IsValid) return false;
            if (!VendorSafetyPolicy.IsInvalidServiceNpc(poi.Type, unit.Dead, unit.IsHostile,
                    unit.IsVendor, unit.IsRepairMerchant, unit.IsTrainer || unit.IsAnyTrainer)) return false;
            RejectVendor((int)poi.Entry, poi.Name + " is dead, hostile, or lacks the requested service");
            return true;
        }

        /// <summary>
        /// Gets the closest vendor of any type.
        /// </summary>
        public Vendor GetClosestVendor()
        {
            return GetClosestVendor(Vendor.VendorType.Unknown);
        }

        /// <summary>
        /// A vendor with no UsableWhen is always usable, otherwise its condition decides.
        /// HB 6.2.3 VendorManager.smethod_0 folds this into the same predicate as the blacklist.
        /// </summary>
        private static bool IsUsable(Vendor vendor)
        {
            // Unknown quest completion must defer eligibility, including negated checks.
            return vendor.UsableWhen == null ||
                QuestConditionEvaluation.Evaluate(vendor.UsableWhen.CallableExpression) == QuestConditionEvaluationState.True;
        }

        /// <summary>
        /// Gets the closest vendor of a specific type.
        /// For Sell type, also accepts Repair and Ammo vendors (they can all buy items).
        /// </summary>
        public Vendor GetClosestVendor(Vendor.VendorType type)
        {
            try
            {
                WoWClass playerClass = StyxWoW.Me?.Class ?? WoWClass.None;
                var source = GetEligibleVendors(type, playerClass).ToList();
                if (source.Count == 0)
                {
                    // Retry exhausted service candidates, but preserve explicit profile guards
                    // and forced-vendor precedence when automatic discovery is enabled.
                    if (Styx.Helpers.CharacterSettings.Instance.FindVendorsAutomatically &&
                        CanUseAutomaticFallback(type, playerClass))
                    {
                        try
                        {
                            NpcResult nearestNpc = NpcQueries.GetNearestNpc(
                                StyxWoW.Me.FactionTemplate.Faction,
                                StyxWoW.Me.MapId,
                                StyxWoW.Me.Location,
                                type.AsNpcFlag(),
                                npc => !IsBlacklisted(new Vendor(
                                    npc.Entry,
                                    npc.Name,
                                    type,
                                    npc.Location)));
                            if (nearestNpc != null)
                            {
                                return new Vendor(nearestNpc.Entry, nearestNpc.Name, type, nearestNpc.Location);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Write(ex.ToString());
                        }
                    }
                    return null;
                }

                WoWPoint location = ObjectManager.Me.Location;
                WoWFaction playerFaction = StyxWoW.Me?.FactionTemplate?.Faction;
                return source
                    .OrderByDescending(v => GetProfileVendorFactionPreference(v, playerFaction))
                    .ThenBy(v => location.Distance(v.Location))
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
                return null;
            }
        }

        private static int GetProfileVendorFactionPreference(Vendor vendor, WoWFaction playerFaction)
        {
            if (vendor == null || playerFaction == null || vendor.Entry <= 0)
                return 0;

            NpcResult npc = NpcQueries.GetNpcById((uint)vendor.Entry);
            if (npc == null || npc.Faction == 0)
                return 0;

            return NpcQueries.GetFactionPreference(
                playerFaction.RelationTo(new WoWFaction(npc.Faction)));
        }

        public IEnumerable<Vendor> GetEligibleVendors(Vendor.VendorType type, WoWClass playerClass)
        {
            var source = ForcedVendors != null && ForcedVendors.Count > 0 ? ForcedVendors : AllVendors;
            return (source ?? Enumerable.Empty<Vendor>()).Where(v =>
                MatchesVendorType(v, type) &&
                (type != Vendor.VendorType.Train || v.TrainClass == playerClass) &&
                !IsBlacklisted(v) && IsUsable(v));
        }

        private bool CanUseAutomaticFallback(Vendor.VendorType type, WoWClass playerClass)
        {
            if (ForcedVendors != null && ForcedVendors.Count > 0) return false;
            return AllVendors == null || AllVendors.Count == 0 || AllVendors.Any(v =>
                MatchesVendorType(v, type) &&
                (type != Vendor.VendorType.Train || v.TrainClass == playerClass) &&
                IsBlacklisted(v) && IsUsable(v));
        }

        /// <summary>
        /// Checks if a vendor matches the requested type.
        /// For Sell type, also matches Repair and Ammo vendors.
        /// </summary>
        private static bool MatchesVendorType(Vendor vendor, Vendor.VendorType type)
        {
            if (type == Vendor.VendorType.Unknown)
                return true;
            
            // For Sell type, also accept Repair and Ammo vendors
            if (type == Vendor.VendorType.Sell)
            {
                return vendor.Type == Vendor.VendorType.Sell || 
                       vendor.Type == Vendor.VendorType.Repair ||
                       vendor.Type == Vendor.VendorType.Ammo;
            }
            
            return vendor.Type == type;
        }
    }
}
