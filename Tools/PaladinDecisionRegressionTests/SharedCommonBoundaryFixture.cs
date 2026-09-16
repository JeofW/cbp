// Compile the complete production shared helper. Only external world, spell
// dispatch, pet, wand and dismount boundaries are controlled; native calls fail.
global using UnitSelectionDelegate = System.Func<object, Styx.UnitState?>;
global using SimpleBooleanDelegate = System.Func<object, bool>;
using TreeSharp;

namespace Styx
{
    public partial class UnitState
    {
        public UnitState? CurrentTarget { get; set; }
        public bool IsCasting { get; set; }
        public bool CanInterruptCurrentSpellCast { get; set; }
        public bool IsFriendly { get; set; }
        public bool IsDemon => UndeadOrDemon;
        public bool IsUndead => UndeadOrDemon;
        public bool IsHumanoid { get; set; } = true;
        public bool IsDragon { get; set; }
        public bool IsGiant { get; set; }
        public bool MeIsSafelyBehind { get; set; }
    }
    public sealed partial class Player
    {
        public int AutoRepeatingSpellId => 0;
        public bool GotAlivePet => false;
        public UnitState Pet => throw new InvalidOperationException("Unexpected pet read");
        public bool IsFlying => false;
        public WoWInternals.ShapeshiftForm Shapeshift => WoWInternals.ShapeshiftForm.None;
        public bool IsWanding() => false;
        // Dispatch does not fabricate a subsequently observed client flag.
        public void ToggleAttack() { Fixture.Trace.Add("autoattack-toggle"); }
    }
    public static partial class StyxWoW { public static class WoWClient { public static int Latency => 0; } }
}
namespace Styx.WoWInternals.WoWObjects
{
    // Global aliases below are unnecessary for the original helper, but an actual
    // owner snapshot may use the canonical unit/player names in this boundary.
    public static class NamespaceMarker { }
}
namespace Styx.WoWInternals
{
    public enum ShapeshiftForm { None, FlightForm, EpicFlightForm }
    public static class Lua { public static void DoString(string _) => throw new InvalidOperationException("Lua forbidden"); }
    public static class WoWMovement
    {
        public enum MovementDirection { Descend }
        public static void Move(MovementDirection _) => throw new InvalidOperationException("Movement forbidden");
        public static void MoveStop() => throw new InvalidOperationException("Movement forbidden");
        public static void MoveStop(MovementDirection _) => throw new InvalidOperationException("Movement forbidden");
    }
}
namespace Styx.Helpers
{
    public static partial class Logging { public static void WriteDebug(string _) { } }
    public sealed class WaitTimer
    {
        private readonly TimeSpan duration;
        private DateTime deadline;
        public WaitTimer(TimeSpan value) { duration = value; }
        public bool IsFinished => DateTime.UtcNow >= deadline;
        public void Reset() { deadline = DateTime.UtcNow + duration; }
    }
}
namespace Styx.Logic.Combat
{
    public static partial class SpellManager { public static bool Cast(string _) => throw new InvalidOperationException("Wand dispatch forbidden"); }
    public static class GroupCombatSafety { public static bool MayAttackCurrentTarget() => Styx.StyxWoW.Me.CurrentTarget != null; }
}
namespace CommonBehaviors.Actions
{
    public sealed class ActionAlwaysSucceed : TreeSharp.Action { public ActionAlwaysSucceed() : base(_ => RunStatus.Success) { } }
}
namespace Singular.Managers
{
    public static partial class TalentManager { public static int GetCount(int _, int __) => 0; }
    public static class PetManager { public static void CastPetAction(string _) => throw new InvalidOperationException("Pet dispatch forbidden"); }
}
namespace Singular.Helpers
{
    public static class Item { public static bool HasWand => false; }
    public static partial class Spell
    {
        public static Composite Cast(string name, UnitSelectionDelegate select) => Fixture.Attempt(name, select, null);
    }
}
