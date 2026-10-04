#nullable disable
using System.Numerics;

namespace Styx.Helpers
{
    public sealed class ObservationUnavailableException : Exception
    {
        public ObservationUnavailableException(string owner, string reason) : base(owner + ": " + reason) { }
    }
    public static class BlackspotManager
    {
        public static Func<Styx.Logic.Pathing.WoWPoint, float, bool> Observe = (_, _) => false;
        public static bool IsBlackspotted(Styx.Logic.Pathing.WoWPoint point, float radius) => Observe(point, radius);
    }
}

namespace Tripper.Navigation
{
    public enum AreaType : byte
    {
        Ground=1, Water=2, Lava=3, Road=4, Fall=5, Elevator=6, Gate=7, Portal=8,
        DefendersPortal=9, HordePortal=10, AlliancePortal=11, Blocked=12, InteractUnit=13,
        InteractObject=14, Horde=15, Alliance=16, Blackspot=17, KnownBuilding=18
    }
    public readonly struct Status
    {
        private readonly int value;
        public Status(int value) => this.value = value;
        public bool Succeeded => value == 0;
    }
}

namespace Styx.Logic.Pathing
{
    public static class Flightor
    {
        public static WoWPoint GetFlightRouteWaypoint(WoWPoint from, WoWPoint to) => to;
        public static bool CanFollowFlightSegment(WoWPoint from, WoWPoint to) => true;
    }
    public struct WoWPoint : IEquatable<WoWPoint>
    {
        public float X, Y, Z;
        public WoWPoint(float x,float y,float z){X=x;Y=y;Z=z;}
        public static readonly WoWPoint Empty = new(float.NaN,float.NaN,float.NaN);
        public WoWPoint Add(float x,float y,float z)=>new(X+x,Y+y,Z+z);
        public float Distance(WoWPoint o){float x=X-o.X,y=Y-o.Y,z=Z-o.Z;return MathF.Sqrt(x*x+y*y+z*z);}
        public float DistanceSqr(WoWPoint o){float x=X-o.X,y=Y-o.Y,z=Z-o.Z;return x*x+y*y+z*z;}
        public float Distance2DSqr(WoWPoint o){float x=X-o.X,y=Y-o.Y;return x*x+y*y;}
        public bool Equals(WoWPoint o)=>X.Equals(o.X)&&Y.Equals(o.Y)&&Z.Equals(o.Z);
        public override bool Equals(object o)=>o is WoWPoint p&&Equals(p);
        public override int GetHashCode()=>HashCode.Combine(X,Y,Z);
        public override string ToString()=>$"({X:F2},{Y:F2},{Z:F2})";
    }

    public sealed class PathFindResult
    {
        public bool Succeeded, IsPartialPath, Aborted;
        public string Status="ok", FailStep="none";
        public Vector3[] Points=Array.Empty<Vector3>();
        public Tripper.Navigation.AreaType[] PolyTypes=Array.Empty<Tripper.Navigation.AreaType>();
        public Tripper.Navigation.PolygonReference[] Polygons=Array.Empty<Tripper.Navigation.PolygonReference>();
        public Tripper.Navigation.StraightPathFlags[] Flags=Array.Empty<Tripper.Navigation.StraightPathFlags>();
    }

    public sealed class MeshNavigator
    {
        public Func<WoWPoint,WoWPoint,PathFindResult> Find = (_,_)=>new();
        public PathFindResult FindPath(WoWPoint from, WoWPoint to)=>Find(from,to);
    }

    public sealed class NativeNavigatorStub
    {
        public Action<uint,Vector3,float> Ensure = (_,_,_)=>{};
        public Func<uint,Vector3,(bool Ok,ulong Poly,Vector3 Snap)> Nearest = (_,p)=>(true,1,p);
        public Func<uint,ulong,(int Status,byte Area)> Area = (_,_) => (0,(byte)Tripper.Navigation.AreaType.Ground);
        public void EnsureTilesAroundPosition(uint map,Vector3 position,float radius)=>Ensure(map,position,radius);
        public bool FindNearestPolyRef(uint map,Vector3 position,out ulong polygon,out Vector3 snapped){var v=Nearest(map,position);polygon=v.Poly;snapped=v.Snap;return v.Ok;}
        public int GetPolyArea(uint map,ulong polygon,out byte area){var v=Area(map,polygon);area=v.Area;return v.Status;}
    }

    public static class Navigator
    {
        public static object NavigationProvider;
        public static bool IsNavigatorLoaded=true;
        public static NativeNavigatorStub TripperNavigator=new();
        public static float LoadTilesAroundRadius=64;
    }
}

namespace Styx.WoWInternals.WoWObjects
{
    public sealed class LocalPlayer { public uint MapId; }
}

namespace Styx.WoWInternals
{
    public static class ObjectManager { public static Styx.WoWInternals.WoWObjects.LocalPlayer Me; }
}

namespace Styx.Logic.Combat
{
    public static class RecoveryActions
    {
        public static void RethrowControlFlow(Exception error)
        {
            if(error is OperationCanceledException || error is ThreadInterruptedException) throw error;
        }
    }
}

namespace Styx.WoWInternals.World
{
    using Styx.Logic.Pathing;
    public struct WorldLine { public WoWPoint Start,End; public WorldLine(WoWPoint start,WoWPoint end){Start=start;End=end;} }
    public static class GameWorld
    {
        [Flags] public enum CGWorldFrameHitFlags:uint
        {
            HitTestNothing=0,HitTestBoundingModels=1,HitTestWMO=16,HitTestGround=256,
            HitTestLiquid=65536,HitTestLiquid2=131072,HitTestMovableObjects=1048576,
            HitTestGroundAndStructures=1048849
        }
        public static Func<WorldLine[],CGWorldFrameHitFlags[],(bool[] Hits,WoWPoint[] Points)> Observe =
            (lines,_) => (new bool[lines.Length],new WoWPoint[lines.Length]);
        public static void MassTraceLine(WorldLine[] lines,CGWorldFrameHitFlags[] flags,out bool[] hits,out WoWPoint[] points)
        {var v=Observe(lines,flags);hits=v.Hits;points=v.Points;}
    }
}
