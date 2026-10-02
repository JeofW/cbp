internal static class DirectGameObjectExecutionFixture
{
    internal const string Source="""
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using TreeSharp;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using DateTime=Clock;
public readonly struct Clock
{
    private readonly double value;private Clock(double seconds){value=seconds;}
    public static double Seconds=1000;public static Clock UtcNow=>new(Seconds);public static Clock MinValue=>new(-1e9);
    public static Clock Now=>UtcNow;public static void Advance(double seconds){Seconds+=seconds;}
    public static TimeSpan operator -(Clock left,Clock right)=>TimeSpan.FromSeconds(left.value-right.value);
    public Clock AddSeconds(double seconds)=>new(value+seconds);
    public static bool operator <(Clock left,Clock right)=>left.value<right.value;
    public static bool operator >(Clock left,Clock right)=>left.value>right.value;
    public static bool operator <=(Clock left,Clock right)=>left.value<=right.value;
    public static bool operator >=(Clock left,Clock right)=>left.value>=right.value;
}
public readonly record struct WoWPoint(float X,float Y,float Z)
{
    public static WoWPoint Zero=>new(0,0,0);public static WoWPoint Empty=>Zero;
    public float DistanceSqr(WoWPoint other)=>(X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y)+(Z-other.Z)*(Z-other.Z);
    public float Distance(WoWPoint other)=>(float)Math.Sqrt(DistanceSqr(other));
    public float Distance2D(WoWPoint other)=>(float)Math.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y));
    public WoWPoint Add(float x,float y,float z)=>new(X+x,Y+y,Z+z);
}
public enum WoWObjectType{GameObject,Unit}
public enum ShapeshiftForm{Normal,FlightForm,EpicFlightForm}
public enum WoWGameObjectType{Goober,Door,Chest}
public class WoWObject
{
    public uint BaseAddress=1,Entry;public ulong Guid,DescriptorGuid;public bool IsValid=true,IsDisabled;
    public WoWPoint Location=new(12,10,10);public string Name="Controlled object";public WoWObjectType Type=WoWObjectType.GameObject;
    public float Distance=>ObjectManager.Me.Location.Distance(Location);public float DistanceSqr=>Distance*Distance;
    public float InteractRange=4.75f;public bool WithinInteractRange=>Distance<=InteractRange;
    public void Interact(bool wait=true){World.Interactions.Add(Guid);World.ExternalEvent?.Invoke("interaction:"+Guid);}
}
public class WoWGameObject:WoWObject
{
    public bool CanLoot=true,InUse;public WoWGameObjectType SubType=WoWGameObjectType.Goober;
    public bool CanUse()=>World.CanUse;
    public bool CanUseNow(){var callback=World.DuringReadiness;World.DuringReadiness=null;callback?.Invoke();return World.CanUseNow;}
}
public class WoWUnit:WoWObject
{
    public uint MapId=530;public bool IsAlive=true,IsMoving,IsFlying,Mounted,IsCasting,IsActuallyInCombat,PetInCombat,OnTaxi,IsOnTransport,IsGhost;
    public uint ChanneledCastingSpellId;public ShapeshiftForm Shapeshift;public readonly MovementObservation MovementInfo=new();
    public bool TryGetMovementState(out uint flags,out ulong transport){flags=World.MovementFlags;transport=World.Transport;return World.MovementKnown;}
    public bool HasAura(string name)=>false;
}
public sealed class LocalPlayer:WoWUnit { }
public sealed class MovementObservation{public bool IsDescending;}
public static class ObjectManager
{
    public static LocalPlayer Me;public static readonly List<WoWGameObject> Objects=new();
    public static List<T> GetObjectsOfType<T>()where T:class=>Objects.OfType<T>().ToList();
}
public static class StyxWoW
{
    public static LocalPlayer Me=>ObjectManager.Me;public static bool IsInWorld=true,IsInGame=true;
    public static void Sleep(int milliseconds){World.Sleeps++;}
}
public enum PoiType{None,Hotspot,Quest,Loot,Skin,Harvest,Kill}
public static class ProfileManager { public static object CurrentProfileSnapshot=new(); }
public sealed class BotPoi
{
    public static long CurrentGeneration;
    public static BotPoi Current=new();public PoiType Type;public WoWObject AsObject;
    public ulong Guid=>AsObject?.Guid??0;public uint Entry=>AsObject?.Entry??0;
    public BotPoi(){}public BotPoi(WoWObject subject,PoiType kind){AsObject=subject;Type=kind;}
    public static void Clear(string reason){World.PoiClears++;Current=new();}
}
public enum MoveResult{Moved,ReachedDestination,Failed,PathGenerationFailed}
public static class Navigator
{
    public static object NavigationProvider=new();public static float PathPrecision=3;
    public static MoveResult MoveTo(WoWPoint point){World.GroundRequests++;World.LastDestination=point;return World.NavigationResult;}
    public static void Clear(){World.RouteClears++;}
}
public static class Flightor{public static void MoveToGroundInteraction(WoWPoint point,Func<bool> admitted){if(admitted())MoveTo(point);}public static bool PreferFlightForGroundInteraction(WoWPoint point,float range)=>false;public static void MoveTo(WoWPoint point){World.FlightRequests++;World.LastDestination=point;}}
public static class WoWMovement
{
    public enum MovementDirection{Descend}
    public static WoWUnit ActiveMover;
    public static void Move(MovementDirection direction){World.DescentRequests++;ObjectManager.Me.MovementInfo.IsDescending=true;}
    public static void MoveStop(){World.Stops++;ObjectManager.Me.IsMoving=false;}
    public static void MoveStop(MovementDirection direction){World.DescentStops++;ObjectManager.Me.MovementInfo.IsDescending=false;}
}
public static class Mount{public static void Dismount(string reason){World.DismountRequests++;}}
public static class GameWorld
{
    public enum CGWorldFrameHitFlags{HitTestGroundAndStructures}
    public static bool TraceLine(WoWPoint from,WoWPoint to,CGWorldFrameHitFlags flags,out WoWPoint hit){hit=new(from.X,from.Y,World.SupportZ);return World.Support;}
    public static bool IsInLineOfSight(WoWPoint from,WoWPoint to)=>World.LineOfSight;
}
public static class Blacklist
{
    public static readonly Dictionary<ulong,Clock> Entries=new();
    public static bool Contains(ulong guid)=>Entries.TryGetValue(guid,out var until)&&Clock.UtcNow<until;
    public static void Add(ulong guid,TimeSpan interval){Entries[guid]=Clock.UtcNow.AddSeconds(interval.TotalSeconds);}
}
public static class Logging{public static void Write(string message,params object[] args){} }
public sealed class PlayerQuest{public uint Id;public string Name;public bool IsFailed;}
namespace Styx.Logic.Questing
{
    public class Quest
    {
        public struct QuestObjective {public int ID,Count,Index;}
    }
}
public static class QuestObjectiveCompletion
{
    public static bool IsTypedNormalObjectiveComplete(PlayerQuest quest,Styx.Logic.Questing.Quest.QuestObjective objective)
        =>!quest.IsFailed&&TryReadTypedNormalObjectiveProgress(quest,unchecked((int)0x80000000)|objective.ID,objective.Count,out int count)&&count>=objective.Count;
    public static bool TryReadTypedNormalObjectiveProgress(PlayerQuest quest,int id,int required,out int progress)
    {
        int? value=World.ExternalProgress!=null?World.ExternalProgress():(World.ProgressKnown?World.Progress:null);
        progress=value??0;return !quest.IsFailed&&value.HasValue;
    }
}
public abstract class QuestObjective:IDisposable
{
    protected QuestObjective(PlayerQuest quest,List<WoWQuestStep> steps,List<QuestObjective> prerequisites){Quest=quest;QuestSteps=steps;}
    public PlayerQuest Quest;public List<WoWQuestStep> QuestSteps;public bool DonePrerequisites=true;
    public QuestInfo OverridedQuestInfo=new();public QuestArea QuestArea=new();
    public abstract bool IsCompleted{get;}public abstract bool CanComplete{get;}public abstract WoWPoint GetObjectiveLocation();public abstract Composite CreateBranch();
    protected WoWQuestStep GetClosestQuestStep()=>new();public virtual void Dispose(){}
}
public sealed class QuestInfo{public UseObjectObjectiveInfo FindUseGameObject(uint entry)=>new();}
public sealed class UseObjectObjectiveInfo{public Hotspots OverridedHotspots=new(){new WoWPoint(12,10,10)};}
public sealed class Hotspots:List<WoWPoint>{public WoWPoint FindClosestTo(WoWPoint from)=>this.OrderBy(from.DistanceSqr).First();}
public sealed class Hotspot{public WoWPoint Position=new(12,10,10);}
public sealed class QuestArea{public bool HotspotsCreated=true;public List<Hotspot> Hotspots=new(){new Hotspot()};public void CreateHotspots(){}}
public readonly record struct Vector2i(int X,int Y);
public sealed class WoWQuestStep{public Vector2i StepPosition=new(12,10);public uint PoiID=1;}
public static class MeshHeightHelper{public static bool FindMeshHeight(ref Vector3 point)=>false;public static WoWPoint ToWoWPoint(Vector3 p)=>new(p.X,p.Y,p.Z);}
public sealed class ForcedQuestObjective{public QuestObjective Objective;}
public sealed class QuestOrder{public static QuestOrder Instance=new();public ForcedQuestObjective CurrentBehavior;}
public sealed class ActionSetActivity:TreeSharp.Action{public ActionSetActivity(Func<object,string> action):base(context=>{action(context);return RunStatus.Success;}){}}
public sealed class ActionIdle:TreeSharp.Action{public ActionIdle():base(_=>RunStatus.Success){}}
public sealed class DecoratorIsNotPoiType:Decorator{public DecoratorIsNotPoiType(IEnumerable<PoiType> types,Composite child):base(_=>!types.Contains(BotPoi.Current.Type),child){}}
public static class World
{
    public static int Progress;public static bool ProgressKnown=true,MovementKnown=true,Support=true,LineOfSight=true,CanUse=true,CanUseNow=true;
    public static uint MovementFlags;public static ulong Transport;public static float SupportZ=10;
    public static int GroundRequests,FlightRequests,DescentRequests,DismountRequests,DescentStops,Stops,RouteClears,PoiClears,Sleeps;
    public static WoWPoint LastDestination;public static MoveResult NavigationResult=MoveResult.Moved;
    public static readonly List<ulong> Interactions=new();public static System.Action DuringReadiness;
    public static Func<int?> ExternalProgress;public static System.Action<string> ExternalEvent;
    public static void Reset()
    {
        Progress=0;ProgressKnown=MovementKnown=Support=LineOfSight=CanUse=CanUseNow=true;MovementFlags=0;Transport=0;SupportZ=10;
        NavigationResult=MoveResult.Moved;
        GroundRequests=FlightRequests=DescentRequests=DismountRequests=DescentStops=Stops=RouteClears=PoiClears=Sleeps=0;
        Interactions.Clear();ObjectManager.Objects.Clear();Blacklist.Entries.Clear();DuringReadiness=null;Clock.Seconds=1000;
        ObjectManager.Me=new LocalPlayer{Guid=1,Location=new WoWPoint(10,10,10)};WoWMovement.ActiveMover=ObjectManager.Me;
        BotPoi.Current=new();Navigator.NavigationProvider=new();QuestOrder.Instance.CurrentBehavior=null;
    }
}
""";
}
