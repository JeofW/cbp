using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Decorators.Combat
{
    public class DecoratorNeedToFindTarget : Decorator
    {
        public DecoratorNeedToFindTarget(Composite child) : base(child)
        {
        }

        protected override bool CanRun(object context)
            => CanAcquireTarget(Targeting.Instance.FirstUnit);

        // Shared by the ordinary acquisition branch and its continuing-movement
        // preemption. Both must use the same profile/range/mount intent.
        internal static bool CanAcquireTarget(WoWUnit firstUnit)
        {
            if (firstUnit == null)
                return false;

            // Ground mount farming mode check
            if (LevelbotSettings.Instance.GroundMountFarmingMode && StyxWoW.Me.Mounted)
                return false;

            // Distance check
            if (firstUnit.DistanceSqr > Targeting.PullDistanceSqr)
                return false;

            // Mounted checks for grind area
            if (StyxWoW.Me.Mounted)
            {
                GrindArea currentGrindArea = StyxWoW.AreaManager.CurrentGrindArea;
                if (currentGrindArea != null && !Battlegrounds.IsInsideBattleground)
                    return IsRequestedMountedTarget(firstUnit);
            }

            return true;
        }

        // Hotspot dismounting must share the profile's actual pull intent.
        internal static bool IsRequestedMountedTarget(WoWUnit firstUnit)
        {
            GrindArea currentGrindArea = StyxWoW.AreaManager?.CurrentGrindArea;
            if (firstUnit == null || currentGrindArea == null || firstUnit.DistanceSqr > Targeting.PullDistanceSqr)
                return false;
            // Check if target is within collection range of hotspot.
            if (firstUnit.Location.Distance(currentGrindArea.CurrentHotSpot.Position) > Targeting.CollectionRange
                && !Targeting.Instance.KillBetweenHotspots)
                return false;

            // The profile target filter already admits explicit MobIDs.
            // Keep that same identity when mounted, even when the profile
            // leaves TargetMaxLevel at its default unlimited value.
            if (firstUnit.Entry <= int.MaxValue &&
                currentGrindArea.MobIDs.Contains((int)firstUnit.Entry) &&
                IsWithinLevelRange(firstUnit, currentGrindArea))
                return true;

            // Check faction filters.
            Profile currentProfile = ProfileManager.CurrentProfile;
            if (currentProfile != null && currentProfile.Factions.Contains(firstUnit.FactionId))
                return true;

            if (currentGrindArea.Factions.Contains((int)firstUnit.FactionId))
                return true;

            return currentGrindArea.TargetMaxLevel != int.MaxValue && IsWithinLevelRange(firstUnit, currentGrindArea);
        }

        private static bool IsWithinLevelRange(WoWUnit unit, GrindArea grindArea)
        {
            if (grindArea == null)
                return false;

            int level = unit.Level;
            return level >= grindArea.TargetMinLevel && level <= grindArea.TargetMaxLevel;
        }
    }
}
