using System;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.Logic.POI;
using Styx.Logic.Profiles;

namespace Styx.Logic.Pathing
{
    public partial class MeshNavigator
    {
        private Func<bool>? _routeAdmission;
        private Func<bool>? _routeLease;
        internal object RequestIdentity => _routeOwner;
        // Observed managed and backing-owner identities only. These checks do not
        // prove native frame, map/descriptor freshness or unobserved ABA continuity.
        private readonly struct MovementRequestObservation
        {
            private readonly object owner;
            private readonly NavigationProvider? provider;
            private readonly object? memory;
            private readonly uint playerAddress;
            private readonly ulong playerGuid;
            private readonly uint map;
            private readonly Profile? profile;
            private readonly BotPoi poi;
            private readonly long poiGeneration;
            private readonly Func<bool>? admitted;
            private readonly Func<bool>? routeLease;
            internal readonly LocalPlayer? Player;
            internal readonly IPlayerMover Mover;

            internal MovementRequestObservation(MeshNavigator mesh)
            {
                owner = mesh._routeOwner;
                admitted = mesh._routeAdmission;
                routeLease = mesh._routeLease;
                Player = ObjectManager.Me;
                memory = ObjectManager.Wow;
                playerAddress = Player?.BaseAddress ?? 0U;
                playerGuid = Player?.Guid ?? 0UL;
                map = Player?.MapId ?? 0U;
                profile = ProfileManager.CurrentProfileSnapshot;
                poi = BotPoi.Current;
                poiGeneration = BotPoi.CurrentGeneration;
                Mover = Navigator.PlayerMover;
                provider = Navigator.NavigationProvider;
            }

            internal bool OwnsInput =>
                ReferenceEquals(ObjectManager.Me, Player) &&
                ReferenceEquals(ObjectManager.Wow, memory) &&
                (Player?.BaseAddress ?? 0U) == playerAddress &&
                (Player?.Guid ?? 0UL) == playerGuid &&
                (Player?.MapId ?? 0U) == map &&
                ReferenceEquals(Navigator.PlayerMover, Mover) &&
                ReferenceEquals(Navigator.NavigationProvider, provider);

            private bool PoiCurrent => ReferenceEquals(BotPoi.Current, poi) && !poi.IsWorldSubjectBlacklisted
                && (routeLease != null ? routeLease() : BotPoi.CurrentGeneration == poiGeneration)
                && ReferenceEquals(BotPoi.Current, poi);

            private bool ContextCurrent(MeshNavigator mesh) =>
                ReferenceEquals(mesh._routeOwner, owner) && OwnsInput &&
                ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile) &&
                PoiCurrent && ReferenceEquals(Navigator.NavigationProvider, provider);

            internal bool IsCurrent(MeshNavigator mesh) => ContextCurrent(mesh)
                && (admitted == null || admitted()) && ContextCurrent(mesh);
        }
    }
}
