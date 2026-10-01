// Runtime compiler fixture text is separate from the aggregate initializer.
// This leaves the existing initializer/topology validation unchanged.
internal static class HunterTrapSpecializationFixture
{
    internal const string Source = """
// The complete specialization factories run unchanged. Unrelated buff, pet,
// movement and interrupt helpers remain external boundaries in this trap scope.
public static class ExternalBoundary
{
    public static Composite None()=>new Action(_=>RunStatus.Failure);
    public static float Distance(this System.Numerics.Vector3 first,System.Numerics.Vector3 second)=>System.Numerics.Vector3.Distance(first,second);
}
public static partial class Common
{
    public static Composite CreateHunterCallPetBehavior(bool enabled)=>ExternalBoundary.None();
    public static Composite CreateHunterBackPedal()=>ExternalBoundary.None();
}
public static class Helpers
{
    public static class Common
    {
        public static Composite CreateAutoAttack(bool value)=>ExternalBoundary.None();
        public static Composite CreateInterruptSpellCast(UnitSelectionDelegate select)=>ExternalBoundary.None();
    }
}
public static class Safers { public static Composite EnsureTarget()=>ExternalBoundary.None(); }
public static partial class Movement
{
    public static Composite CreateMoveToLosBehavior()=>ExternalBoundary.None();
    public static Composite CreateFaceTargetBehavior()=>ExternalBoundary.None();
    public static Composite CreateEnsureMovementStoppedBehavior()=>ExternalBoundary.None();
    public static Composite CreateMoveToTargetBehavior(bool stop,float distance)=>ExternalBoundary.None();
}
public static partial class Spell
{
    public const float MeleeRange=5;
    public static Composite BuffSelf(string name,SimpleBooleanDelegate? requires=null)=>ExternalBoundary.None();
    public static Composite Buff(string name,bool mine=false)=>ExternalBoundary.None();
    public static Composite WaitForCast(bool stop)=>ExternalBoundary.None();
    public static Composite CastOnGround(string name,Func<object,System.Numerics.Vector3> select)=>ExternalBoundary.None();
}
public sealed class HunterSettings
{
    public bool UseDisengage;public int ViperManaPercent=5,ViperResumeManaPercent=80,MendPetPercent=60;
}
public sealed class SingularSettings { public static SingularSettings Instance=new();public HunterSettings Hunter=new(); }
public sealed class PetSpellObservation { public WoWSpell? Spell; }
public static class PetManager { public static List<PetSpellObservation> PetSpells=new(); }
namespace Singular.Managers
{
    public enum TalentSpec { BeastMasteryHunter,MarksmanshipHunter,SurvivalHunter }
    public static class TalentManager { public static bool HasGlyph(string name)=>false; }
}
namespace Singular.Dynamics
{
    public enum BehaviorType { Pull,Combat }
    public enum WoWContext { Normal,Battlegrounds,Instances }
}
""";
}
