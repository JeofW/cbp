using System;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing
{
    public partial class MeshNavigator
    {
        // Observed managed and backing-owner identities only. These checks do not
        // prove native frame, map/descriptor freshness or unobserved ABA continuity.
        private readonly struct MovementRequestObservation
        {
            private readonly object owner;
            private readonly NavigationProvider? provider;
            private readonly object? memory;
            private readonly uint playerAddress;
            internal readonly LocalPlayer? Player;
            internal readonly IPlayerMover Mover;

            internal MovementRequestObservation(MeshNavigator mesh)
            {
                owner = mesh._routeOwner;
                Player = ObjectManager.Me;
                memory = ObjectManager.Wow;
                playerAddress = Player?.BaseAddress ?? 0U;
                Mover = Navigator.PlayerMover;
                provider = Navigator.NavigationProvider;
            }

            internal bool IsCurrent(MeshNavigator mesh) =>
                ReferenceEquals(mesh._routeOwner, owner) &&
                ReferenceEquals(ObjectManager.Me, Player) &&
                ReferenceEquals(ObjectManager.Wow, memory) &&
                (Player?.BaseAddress ?? 0U) == playerAddress &&
                ReferenceEquals(Navigator.PlayerMover, Mover) &&
                ReferenceEquals(Navigator.NavigationProvider, provider);
        }
    }
}
