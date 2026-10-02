#nullable disable
using System;
using System.Collections.Generic;
using TreeSharp;

namespace Harness
{
    internal static class Control
    {
        internal static int TransitionTicks, DismountSubmissions, TransitionCancels, NavigatorClears, PullCalls, PullBuffCalls, Stops;
        internal static bool SupportKnown = true;
        internal static System.Action<Styx.WoWInternals.WoWObjects.WoWUnit> OnTarget;
        internal static System.Action OnPull,OnPullBuff,OnTransitionCancel;
        internal static System.Action<Styx.Logic.POI.BotPoi> OnPoiChanged;
        internal static readonly Dictionary<Singular.Dynamics.BehaviorType, Composite> SingularFactories = new();
        internal static object PendingActor, PendingMemory;
        internal static CountingLeaf RootQuest,RootCombat;
        internal static bool RootCombatEnabled;
        internal static ulong PendingGuid;
        internal static uint PendingAddress, PendingMap;
        internal static int PendingProcess;

        internal static void Reset()
        {
            TransitionTicks=DismountSubmissions=TransitionCancels=NavigatorClears=PullCalls=PullBuffCalls=Stops=0;
            SupportKnown=true;OnTarget=null;OnPull=OnPullBuff=OnTransitionCancel=null;OnPoiChanged=null;SingularFactories.Clear();RootCombatEnabled=false;
            PendingActor=PendingMemory=null;PendingGuid=0;PendingAddress=PendingMap=0;PendingProcess=0;
            var memory=new GreenMagic.Memory{ProcessId=10,ProcessHandle=new IntPtr(10)};
            Styx.WoWInternals.ObjectManager.Wow=memory;
            Styx.WoWInternals.ObjectManager.Executor=new GreenMagic.ExecutorRand{Memory=memory,IsOpen=true,IsInitialized=true};
            Styx.Logic.Targeting.Instance=new Styx.Logic.Targeting();
            Styx.Logic.POI.BotPoi.Current=new Styx.Logic.POI.BotPoi(Styx.Logic.POI.PoiType.None);
            Styx.Logic.Pathing.Navigator.NavigationProvider=new object();
            Styx.Logic.Pathing.Navigator.PlayerMover=new object();
            Styx.Logic.Pathing.Navigator.IsNavigatorLoaded=true;
            Styx.WoWInternals.WoWMovement.ActiveMover=null;
            Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new object();
            Styx.Logic.BehaviorTree.TreeRoot.Current=new object();
            Styx.Logic.BehaviorTree.TreeRoot.IsRunning=true;
            Styx.Logic.Profiles.ProfileManager.CurrentProfile=new Styx.Logic.Profiles.Profile();
            Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new object();
            Styx.Logic.Battlegrounds.IsInsideBattleground=false;
            Styx.Helpers.LevelbotSettings.Instance.GroundMountFarmingMode=false;
            Styx.Helpers.CharacterSettings.Instance.PullDistance=30;
            Styx.Logic.Targeting.PullDistance=30;Styx.Logic.Targeting.CollectionRange=100;
            var area=new Styx.Logic.AreaManagement.GrindArea();area.CurrentHotSpot.Position=new Styx.Logic.Pathing.WoWPoint(0,0,0);
            Styx.StyxWoW.AreaManager.CurrentGrindArea=area;
            Singular.Settings.SingularSettings.Instance.DisableNonCombatBehaviors=false;
            Singular.Managers.TalentManager.CurrentSpec=Singular.Managers.TalentSpec.RetributionPaladin;
            RootQuest=new CountingLeaf(_=>RunStatus.Running);RootCombat=new CountingLeaf(_=>RootCombatEnabled?RunStatus.Success:RunStatus.Failure);
            Bots.Quest.QuestBot.RootFactory=()=>new PrioritySelector(
                new CountingLeaf(RunStatus.Failure),RootCombat,new CountingLeaf(RunStatus.Failure),new CountingLeaf(RunStatus.Failure),
                new Decorator(_=>true,new CountingLeaf(RunStatus.Failure)),new CountingLeaf(RunStatus.Failure),new CountingLeaf(RunStatus.Failure));
            var order=Bots.Quest.QuestState.Instance.Order;order.Nodes=new Bots.Quest.QuestOrder.OrderNodeCollection{new Bots.Quest.QuestOrder.OrderNode()};order.CurrentBehavior=new Bots.Quest.QuestOrder.ForcedBehavior();
        }
    }

    internal sealed class CountingLeaf : Composite
    {
        private readonly Func<int,RunStatus> next;
        internal CountingLeaf(RunStatus status):this(_=>status){}
        internal CountingLeaf(Func<int,RunStatus> next)=>this.next=next;
        internal int Ticks{get;private set;}
        internal int Starts{get;private set;}internal int Stops{get;private set;}
        internal System.Action<int> BeforeTick;
        public override void Start(object context){Starts++;base.Start(context);}
        public override void Stop(object context){bool active=LastStatus==RunStatus.Running;try{base.Stop(context);}finally{if(active)Stops++;}}
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            while(true){Ticks++;BeforeTick?.Invoke(Ticks);var status=next(Ticks);yield return status;if(status!=RunStatus.Running)yield break;}
        }
    }
}

namespace GreenMagic
{
    public sealed class Memory{public int ProcessId{get;set;}public IntPtr ProcessHandle{get;set;}=new IntPtr(1);}
    public sealed class ExecutorRand{public Memory Memory{get;set;}public bool IsOpen{get;set;}=true;public bool IsInitialized{get;set;}=true;}
}

namespace Styx.Logic.Pathing
{
    using Styx.WoWInternals.WoWObjects;
    public readonly struct WoWPoint:IEquatable<WoWPoint>
    {
        public readonly float X,Y,Z;public WoWPoint(float x,float y,float z){X=x;Y=y;Z=z;}public static WoWPoint Empty=>new(float.NaN,float.NaN,float.NaN);public static WoWPoint Zero=>new(0,0,0);
        public float Distance(WoWPoint o)=>MathF.Sqrt(DistanceSqr(o));public float DistanceSqr(WoWPoint o){float x=X-o.X,y=Y-o.Y,z=Z-o.Z;return x*x+y*y+z*z;}public float Distance2DSqr(WoWPoint o){float x=X-o.X,y=Y-o.Y;return x*x+y*y;}
        public bool Equals(WoWPoint o)=>X.Equals(o.X)&&Y.Equals(o.Y)&&Z.Equals(o.Z);public override bool Equals(object o)=>o is WoWPoint p&&Equals(p);public override int GetHashCode()=>HashCode.Combine(X,Y,Z);public static bool operator==(WoWPoint a,WoWPoint b)=>a.Equals(b);public static bool operator!=(WoWPoint a,WoWPoint b)=>!a.Equals(b);
    }
    public static class Navigator{public static object NavigationProvider{get;set;}=new();public static object PlayerMover{get;set;}=new();public static bool IsNavigatorLoaded{get;set;}=true;public static void Clear()=>Harness.Control.NavigatorClears++;}
    public enum GroundTransitionState{Pending,Ready,Unavailable,Revoked}
    public enum GroundTransitionPurpose{Interaction,Combat}

    internal sealed class GroundTransitionContext
    {
        private readonly LocalPlayer actor;private readonly GreenMagic.Memory memory;private readonly GreenMagic.ExecutorRand executor;private readonly object run,bot,profile,provider,input,mover;private readonly Styx.Logic.POI.BotPoi poi;private readonly long poiGeneration,poiWorkGeneration;private readonly WoWObject subject;private readonly Func<bool> admitted;private readonly bool running,combatRoute;
        private readonly ulong actorGuid,subjectGuid,moverGuid;private readonly uint actorAddress,subjectAddress,map,moverAddress;private readonly int process;private readonly IntPtr handle;
        internal GroundTransitionContext(WoWObject subject,WoWPoint destination,bool bindDestination,Func<bool> admitted,bool combatRoute=false)
        {
            this.admitted=admitted??(()=>true);actor=Styx.WoWInternals.ObjectManager.Me??throw new Styx.Helpers.ObservationUnavailableException("ground-transition-context","actor unavailable");memory=Styx.WoWInternals.ObjectManager.Wow??throw new Styx.Helpers.ObservationUnavailableException("ground-transition-context","memory unavailable");executor=Styx.WoWInternals.ObjectManager.Executor??throw new Styx.Helpers.ObservationUnavailableException("ground-transition-context","executor unavailable");
            run=Styx.Logic.BehaviorTree.TreeRoot.RunIdentity;bot=Styx.Logic.BehaviorTree.TreeRoot.Current;profile=Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot;provider=Navigator.NavigationProvider;input=Navigator.PlayerMover;mover=Styx.WoWInternals.WoWMovement.ActiveMover;
            poi=Styx.Logic.POI.BotPoi.Current;poiGeneration=Styx.Logic.POI.BotPoi.CurrentGeneration;poiWorkGeneration=Styx.Logic.POI.BotPoi.CurrentWorkGeneration;this.subject=subject;running=Styx.Logic.BehaviorTree.TreeRoot.IsRunning;this.combatRoute=combatRoute;
            actorGuid=actor.Guid;actorAddress=actor.BaseAddress;map=actor.MapId;subjectGuid=subject?.Guid??0;subjectAddress=subject?.BaseAddress??0;moverGuid=(mover as WoWObject)?.Guid??0;moverAddress=(mover as WoWObject)?.BaseAddress??0;process=memory.ProcessId;handle=memory.ProcessHandle;
            if(!Current)throw new Styx.Helpers.ObservationUnavailableException("ground-transition-context","context unavailable");
        }
        internal bool Current{get{if(memory.ProcessHandle==IntPtr.Zero)throw new Styx.InvalidProcessException("controlled process loss");if(!executor.IsOpen||!executor.IsInitialized)throw new Styx.InvalidExecutorException("controlled executor loss");return ReferenceEquals(Styx.WoWInternals.ObjectManager.Me,actor)&&ReferenceEquals(Styx.WoWInternals.ObjectManager.Wow,memory)&&ReferenceEquals(Styx.WoWInternals.ObjectManager.Executor,executor)&&ReferenceEquals(executor.Memory,memory)&&memory.ProcessId==process&&memory.ProcessHandle==handle&&actor.IsValid&&actor.IsAlive&&!actor.IsGhost&&actor.Guid==actorGuid&&actor.BaseAddress==actorAddress&&actor.MapId==map&&ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.RunIdentity,run)&&ReferenceEquals(Styx.Logic.BehaviorTree.TreeRoot.Current,bot)&&Styx.Logic.BehaviorTree.TreeRoot.IsRunning==running&&ReferenceEquals(Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot,profile)&&ReferenceEquals(Navigator.NavigationProvider,provider)&&ReferenceEquals(Navigator.PlayerMover,input)&&ReferenceEquals(Styx.WoWInternals.WoWMovement.ActiveMover,mover)&&(mover is WoWObject mo&&mo.Guid==moverGuid&&mo.BaseAddress==moverAddress)&&ReferenceEquals(Styx.Logic.POI.BotPoi.Current,poi)&&(combatRoute?Styx.Logic.POI.BotPoi.CurrentWorkGeneration==poiWorkGeneration:Styx.Logic.POI.BotPoi.CurrentGeneration==poiGeneration)&&(subject==null||subject.IsValid&&subject.IsAlive&&subject.Guid==subjectGuid&&subject.BaseAddress==subjectAddress)&&admitted();}}
    }

    public sealed class GroundTransition:IDisposable
    {
        private GroundTransitionContext context;private WoWObject subject;private Func<bool> admitted;private readonly GroundTransitionPurpose purpose;
        public GroundTransition(GroundTransitionPurpose purpose){this.purpose=purpose;}
        public GroundTransitionState Tick(WoWPoint destination,WoWObject subject,Func<bool> admitted)
        {
            if(admitted==null||!admitted())return GroundTransitionState.Revoked;
            if(context==null){this.subject=subject;this.admitted=admitted;try{context=new GroundTransitionContext(subject,destination,false,admitted,purpose==GroundTransitionPurpose.Combat);}catch(Styx.Helpers.ObservationUnavailableException){return GroundTransitionState.Pending;}}
            else if(!ReferenceEquals(this.subject,subject)||!context.Current)return GroundTransitionState.Revoked;
            Harness.Control.TransitionTicks++;
            if(CanActUnmounted(()=>context.Current))return GroundTransitionState.Ready;
            var actor=Styx.WoWInternals.ObjectManager.Me;
            if(actor!=null&&Harness.Control.SupportKnown&&actor.Mounted&&!actor.IsFlying&&actor.MovementKnown&&actor.ObservedTransportGuid==0&&(actor.ObservedMovementFlags&0x02003000u)==0)
            {
                var memory=Styx.WoWInternals.ObjectManager.Wow;
                bool same=ReferenceEquals(Harness.Control.PendingActor,actor)&&ReferenceEquals(Harness.Control.PendingMemory,memory)&&Harness.Control.PendingGuid==actor.Guid&&Harness.Control.PendingAddress==actor.BaseAddress&&Harness.Control.PendingMap==actor.MapId&&Harness.Control.PendingProcess==memory.ProcessId;
                if(!same){Harness.Control.DismountSubmissions++;Harness.Control.PendingActor=actor;Harness.Control.PendingMemory=memory;Harness.Control.PendingGuid=actor.Guid;Harness.Control.PendingAddress=actor.BaseAddress;Harness.Control.PendingMap=actor.MapId;Harness.Control.PendingProcess=memory.ProcessId;}
            }
            return GroundTransitionState.Pending;
        }
        public void Cancel(){bool owned=context!=null;context=null;subject=null;admitted=null;Harness.Control.TransitionCancels++;if(owned)Harness.Control.OnTransitionCancel?.Invoke();}
        public void Dispose()=>Cancel();
        public static bool CanActUnmounted(Func<bool> admitted=null)
        {
            admitted??=()=>true;if(!admitted())return false;var actor=Styx.WoWInternals.ObjectManager.Me;if(actor==null)return false;
            GroundTransitionContext stamp;try{stamp=new GroundTransitionContext(null,WoWPoint.Empty,false,admitted);}catch(Styx.Helpers.ObservationUnavailableException){return false;}
            if(!stamp.Current||!actor.TryGetMovementState(out uint flags,out ulong transport))return false;
            bool mounted=actor.Mounted||actor.Shapeshift is Styx.ShapeshiftForm.FlightForm or Styx.ShapeshiftForm.EpicFlightForm;
            bool allowed=!mounted&&(flags&0x02003000u)==0&&transport==0&&!actor.OnTaxi&&!actor.InVehicle;
            if(!mounted&&stamp.Current){Harness.Control.PendingActor=Harness.Control.PendingMemory=null;Harness.Control.PendingGuid=0;}
            return allowed&&stamp.Current;
        }
    }
}

namespace Styx
{
    public sealed class InvalidProcessException:Exception{public InvalidProcessException(string message):base(message){}}
    public sealed class InvalidExecutorException:Exception{public InvalidExecutorException(string message):base(message){}}
    public enum WoWClass{None,Paladin,Hunter,DeathKnight,Warlock,Mage}public enum ShapeshiftForm{Normal,FlightForm,EpicFlightForm}
    public static class StyxWoW{public static Styx.WoWInternals.WoWObjects.LocalPlayer Me{get;set;}public static AreaManagerStub AreaManager{get;}=new();}
    public sealed class AreaManagerStub{public Styx.Logic.AreaManagement.GrindArea CurrentGrindArea{get;set;}=new();}
}

namespace Styx.WoWInternals.WoWObjects
{
    using Styx.Logic.Pathing;
    public class WoWObject{public ulong Guid{get;set;}public uint Entry{get;set;}public uint BaseAddress{get;set;}=100;public bool IsValid{get;set;}=true;public virtual bool IsAlive{get;set;}=true;public WoWPoint Location{get;set;}public virtual WoWUnit ToUnit()=>this as WoWUnit;}
    public class WoWUnit:WoWObject{public bool Dead{get=>!IsAlive;set=>IsAlive=!value;}public bool Combat{get;set;}public bool IsPlayer{get;set;}public bool IsPet{get;set;}public WoWUnit OwnedByUnit{get;set;}public bool TaggedByOther{get;set;}public bool TaggedByMe{get;set;}public bool InLineOfSpellSight{get;set;}=true;public string Name{get;set;}="controlled";public int Level{get;set;}=60;public int Race{get;set;}public Styx.WoWClass Class{get;set;}=Styx.WoWClass.Paladin;public uint FactionId{get;set;}public double Distance=>Location.Distance(Styx.StyxWoW.Me?.Location??WoWPoint.Zero);public double DistanceSqr=>Location.DistanceSqr(Styx.StyxWoW.Me?.Location??WoWPoint.Zero);public void Target(){if(Styx.StyxWoW.Me!=null)Styx.StyxWoW.Me.CurrentTarget=this;Harness.Control.OnTarget?.Invoke(this);}}
    public class WoWPlayer:WoWUnit{}
    public class LocalPlayer:WoWUnit
    {
        public bool Mounted{get;set;}public bool IsFlying{get;set;}public bool IsGhost{get;set;}public uint MapId{get;set;}=1;public bool IsOnTransport{get;set;}public bool OnTaxi{get;set;}public bool InVehicle{get;set;}public bool Rooted{get;set;}public bool Stunned{get;set;}public double HealthPercent{get;set;}=100;public bool IsMoving{get;set;}public bool IsCasting{get;set;}public uint ChanneledCastingSpellId{get;set;}public WoWUnit Pet{get;set;}public bool GotAlivePet=>Pet!=null&&Pet.IsAlive;public WoWUnit CurrentTarget{get;set;}public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public bool GotTarget=>CurrentTarget!=null;public bool IsInParty{get;set;}public bool IsInRaid{get;set;}public bool IsInInstance{get;set;}public Styx.ShapeshiftForm Shapeshift{get;set;}public bool MovementKnown{get;set;}=true;public uint ObservedMovementFlags{get;set;}public ulong ObservedTransportGuid{get;set;}
        public bool TryGetMovementState(out uint flags,out ulong transport){flags=ObservedMovementFlags;transport=ObservedTransportGuid;return MovementKnown;}public void ClearTarget()=>CurrentTarget=null;public bool HasPendingSpell(string name)=>false;
    }
}

namespace Styx.WoWInternals
{
    public static class ObjectManager{public static Styx.WoWInternals.WoWObjects.LocalPlayer Me=>Styx.StyxWoW.Me;public static GreenMagic.Memory Wow{get;set;}=new();public static GreenMagic.ExecutorRand Executor{get;set;}=new();}
    public static class Lua{public static void DoString(string code){}public static IReadOnlyList<string> GetReturnValues(string code)=>new[]{"absent"};}
    public static class WoWMovement{public static Styx.WoWInternals.WoWObjects.WoWUnit ActiveMover{get;set;}}
}

namespace Styx.Logic.POI
{
    using Styx.Logic.Pathing;using Styx.WoWInternals.WoWObjects;
    public enum PoiType{None,Kill,Loot,Skin,Harvest,Sell,Repair,Train,Buy,Mail,Fly,Hotspot,QuestPickUp,QuestTurnIn}
    public class BotPoi
    {
        private static BotPoi current=new(PoiType.None);private static long generation,workGeneration;private PoiType type;private ulong guid;private uint entry;private WoWPoint location;
        private void Changed(bool work){if(!ReferenceEquals(current,this))return;if(work)workGeneration++;generation++;}
        public static BotPoi Current{get=>current;set{var next=value??new BotPoi(PoiType.None);if(ReferenceEquals(current,next))return;current=next;generation++;workGeneration++;Harness.Control.OnPoiChanged?.Invoke(next);}}public static long CurrentGeneration=>generation;public static long CurrentWorkGeneration=>workGeneration;
        public PoiType Type{get=>type;set{if(type==value)return;type=value;Changed(true);}}public ulong Guid{get=>guid;set{if(guid==value)return;guid=value;Changed(true);}}public uint Entry{get=>entry;set{if(entry==value)return;entry=value;Changed(true);}}public WoWPoint Location{get=>location;set{if(location.Equals(value))return;location=value;Changed(false);}}public WoWObject AsObject{get;set;}public bool IsWorldSubjectBlacklisted{get;set;}
        public BotPoi(PoiType type){this.type=type;}public BotPoi(WoWUnit unit,PoiType type){this.type=type;AsObject=unit;guid=unit?.Guid??0;entry=unit?.Entry??0;location=unit?.Location??WoWPoint.Zero;}public BotPoi(WoWPoint location,PoiType type){this.location=location;this.type=type;}public static void Clear(string reason)=>Current=new BotPoi(PoiType.None);
    }
}

namespace Styx.Logic
{
    using Styx.WoWInternals.WoWObjects;
    public sealed class Targeting{public static Targeting Instance{get;set;}=new();public static double PullDistance{get;set;}=30;public static double PullDistanceSqr=>PullDistance*PullDistance;public static double CollectionRange{get;set;}=100;public List<WoWUnit> TargetList{get;}=new();public WoWUnit FirstUnit{get;set;}public bool KillBetweenHotspots{get;set;}}
    public static class Battlegrounds{public static bool IsInsideBattleground{get;set;}}
}

namespace Styx.Logic.AreaManagement
{
    using Styx.Logic.Pathing;public sealed class HotSpot{public WoWPoint Position{get;set;}}public sealed class GrindArea{public HotSpot CurrentHotSpot{get;}=new();public HashSet<int> MobIDs{get;}=new();public HashSet<int> Factions{get;}=new();public int TargetMinLevel{get;set;}=1;public int TargetMaxLevel{get;set;}=int.MaxValue;}
}

namespace Styx.Logic.Profiles
{
    public sealed class Profile{public HashSet<uint> Factions{get;}=new();}public static class ProfileManager{public static Profile CurrentProfile{get;set;}=new();public static object CurrentProfileSnapshot{get;set;}=new();}
}
namespace Styx.Logic.Profiles.Quest { }

namespace Styx.Helpers
{
    public sealed class ObservationUnavailableException:Exception{public ObservationUnavailableException(string owner,string reason):base(owner+": "+reason){}}
    public sealed class LevelbotSettings{public static LevelbotSettings Instance{get;}=new();public bool GroundMountFarmingMode{get;set;}}
    public sealed class CharacterSettings{public static CharacterSettings Instance{get;}=new();public float PullDistance{get;set;}=30;}
    public static class Logging{public static void Write(string f,params object[] a){}public static void WriteDebug(string f,params object[] a){}public static void WriteDiagnostic(string f,params object[] a){}public static void WriteException(Exception e){}}
    public static class Blacklist{public static void Add(ulong guid,TimeSpan duration){}}
}

namespace Styx.Logic.BehaviorTree
{
    public static class TreeRoot{public static string StatusText{get;set;}public static object RunIdentity{get;set;}=new();public static object Current{get;set;}=new();public static bool IsRunning{get;set;}=true;public static void Stop(){Harness.Control.Stops++;IsRunning=false;}}
}

namespace Styx.Logic.Combat
{
    public static class RecoveryActions{public static void RethrowControlFlow(Exception e){if(e is OperationCanceledException||e is ThreadInterruptedException||e is Styx.InvalidProcessException||e is Styx.InvalidExecutorException)throw e;}}
    public static class RoutineManager{public static Styx.Combat.CombatRoutine.CombatRoutine Current{get;set;}=new();}
}

namespace Bots.Quest.QuestOrder
{
    public class OrderNode{}
    public class OrderNodeCollection:List<OrderNode>{}
    public class ForcedBehavior{public virtual bool SuppressServiceBehavior{get;set;}public virtual bool IsDone{get;set;}public virtual bool IsExecutionDeferred{get;set;}}
    public sealed class QuestOrder{public OrderNodeCollection Nodes{get;set;}=new();public OrderNode CurrentNode=>Nodes.Count==0?null:Nodes[0];public ForcedBehavior CurrentBehavior{get;set;}}
}
namespace Bots.Quest
{
    public sealed class QuestState{public static QuestState Instance{get;}=new();public Bots.Quest.QuestOrder.QuestOrder Order{get;}=new();}
}
namespace Bots.Quest.Actions
{
    public sealed class ForcedBehaviorExecutor:Composite
    {
        private readonly Func<bool> allowed;public ForcedBehaviorExecutor(Bots.Quest.QuestOrder.QuestOrder order,Func<bool> allowed)=>this.allowed=allowed;
        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if(!allowed()){yield return RunStatus.Failure;yield break;}var child=Harness.Control.RootQuest;child.Start(context);try{while(child.Tick(context)==RunStatus.Running)yield return RunStatus.Running;yield return child.LastStatus??RunStatus.Failure;}finally{child.Stop(context);}
        }
    }
}

namespace Styx.Combat.CombatRoutine
{
    public class CombatRoutine{public virtual Composite RestBehavior{get;set;}public virtual Composite PreCombatBuffBehavior{get;set;}public virtual Composite PullBehavior{get;set;}public virtual Composite HealBehavior{get;set;}public virtual Composite CombatBuffBehavior{get;set;}public virtual Composite CombatBehavior{get;set;}public virtual Composite PullBuffBehavior{get;set;}public bool NeedPullBuffs{get;set;}public void PullBuff(){Harness.Control.PullBuffCalls++;Harness.Control.OnPullBuff?.Invoke();}public void Pull(){Harness.Control.PullCalls++;Harness.Control.OnPull?.Invoke();}}
}

namespace Levelbot.Actions.Combat{public static class PullIsolationCoordinator{public static Composite CreatePreCombatBehavior()=>new Harness.CountingLeaf(RunStatus.Failure);public static Composite CreateRetreatBehavior()=>new Harness.CountingLeaf(RunStatus.Failure);}}

namespace Singular{public enum WoWContext{Normal,Instances,Battlegrounds}public static class Logger{public static void Write(string s){}public static void WriteDebug(string s){}}}
namespace Singular.Dynamics{public enum BehaviorType{Combat,Pull,Rest,CombatBuffs,Heal,PullBuffs,PreCombatBuffs}public static class CompositeBuilder{public static Composite GetComposite(Styx.WoWClass c,Singular.Managers.TalentSpec s,BehaviorType t,Singular.WoWContext w,out int count){if(Harness.Control.SingularFactories.TryGetValue(t,out var v)){count=1;return v;}count=0;return null;}}}
namespace Singular.Managers{public enum TalentSpec{None,RetributionPaladin}public static class TalentManager{public static TalentSpec CurrentSpec{get;set;}=TalentSpec.RetributionPaladin;}}
namespace Singular.Settings{public sealed class SingularSettings{public static SingularSettings Instance{get;}=new();public bool DisableNonCombatBehaviors{get;set;}}}
namespace Singular.Helpers{public static class Rest{public static Composite CreateDefaultRestBehaviour()=>new Harness.CountingLeaf(RunStatus.Failure);}public static class Item{public static Composite CreateUseTrinketsBehavior()=>new Harness.CountingLeaf(RunStatus.Failure);}}
namespace Singular.ClassSpecific{public static class Generic{public static Composite CreateRacialBehaviour()=>new Harness.CountingLeaf(RunStatus.Failure);}}
