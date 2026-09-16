using System;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing
{
    public partial class MeshNavigator
    {
        // Managed identities only. These checks do not observe native frame,
        // map/descriptor freshness or an unobserved change-and-restore (ABA).
        private readonly struct MovementRequestObservation
        {
            private readonly object owner;
            private readonly NavigationProvider? provider;
            internal readonly LocalPlayer? Player;
            internal readonly IPlayerMover Mover;

            internal MovementRequestObservation(MeshNavigator mesh)
            {
                owner = mesh._routeOwner;
                Player = ObjectManager.Me;
                Mover = Navigator.PlayerMover;
                provider = Navigator.NavigationProvider;
            }

            internal bool IsCurrent(MeshNavigator mesh) =>
                ReferenceEquals(mesh._routeOwner, owner) &&
                ReferenceEquals(ObjectManager.Me, Player) &&
                ReferenceEquals(Navigator.PlayerMover, Mover) &&
                ReferenceEquals(Navigator.NavigationProvider, provider);
        }
    }
}
