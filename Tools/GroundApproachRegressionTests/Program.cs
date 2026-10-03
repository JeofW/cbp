#nullable disable
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Tripper.Navigation;

internal static class Program
{
    private static int passed, failed, unexpected;
    private static void Main()
    {
        foreach(var route in new[]{
            ("Hagash repair",new WoWPoint(-989.65204f,3218.9448f,45.25728f),new WoWPoint(-1329.01f,2397.58f,89.1584f)),
            ("Aledis turn-in",new WoWPoint(-1328.8708f,2398.1316f,89.101585f),new WoWPoint(-689.583f,4167.8f,58.5228f))})
            Case("distant "+route.Item1+" uses locally observed flight progress",()=>LocalFlightProgress(route.Item2,route.Item3));
        Case("local flight progress still rejects unknown mesh areas", LocalFlightUnknownArea);
        Case("local flight progress revalidates positive support", LocalFlightSupportRevoked);
        foreach(string fault in new[]{"none","no-end-marker","nonzero-reference","interior-zero","partial","missing-polygon","wrong-floor","unknown-area","changed-provider"})
            Case("native endpoint area observation/"+fault,()=>NativeEndpointArea(fault));
        Case("covered actor projects support below roof", CoveredActorProjectsBelowRoof);
        foreach (float altitude in new[] { 80f, 240f, 500f })
            foreach (float ground in new[] { -120f, 35f })
                Case($"targetless combat observes support {altitude} below actor at ground {ground}",
                    () => TargetlessSupport(altitude, ground, false));
        Case("covered targetless combat observes deep support below its roof", () => TargetlessSupport(240, -120, true));
        Case("targetless support search remains bounded below its actor", TargetlessDepthBound);
        Case("deep support does not bypass missing mesh", DeepSupportMissingMesh);
        Case("covered permission does not relax required exterior approach", CoveredDoesNotRelaxOnwardExterior);
        Case("ordinary interaction keeps exterior doorway column", DeepDoorwaySelectsExterior);
        Case("opposite entrance follows actual complete path", OppositeEntranceFromPath);
        Case("roof or other-floor landing is rejected", RoofWrongFloorRejected);
        Case("exterior candidate beside wall remains eligible", ExteriorBesideWall);
        Case("multiple candidates skip forbidden first landing", MultipleCandidates);
        Case("partial missing and wrong-Z paths are unusable", PathContracts);
        Case("GroundPath copies caller collections", ImmutablePathOutputs);
        Case("water footprint is rejected", WaterRejected);
        Case("cliff or incomplete footprint is rejected", CliffRejected);
        Case("head clearance collision is rejected", HeadClearanceRejected);
        Case("dynamic blocked doorway revokes retained plan", DynamicBlockedDoorRevalidation);
        Case("bounded search exhausts finite candidates", BoundedSearch);
        Case("reentrant current change revokes query result", ReentrantCurrentChange);
        Case("cancellation propagates from collision leaf", CancellationPropagates);
        Case("query adapter rejects provider replacement", AdapterProviderReplacement);
        Case("query adapter rejects incomplete collision batch", AdapterIncompleteCollision);
        Case("query adapter preserves partial path as incomplete", AdapterPartialPath);
        Case("query adapter propagates cancellation", AdapterCancellation);
        Console.WriteLine($"GroundApproach scenarios: {passed}/{passed+failed+unexpected}; assertions={failed}; unexpected={unexpected}; actual GroundApproachSearch/GroundApproachQueries with controlled collision+mesh leaves; no game/native calls.");
        if(failed+unexpected!=0) Environment.ExitCode=1;
    }

    private static void Case(string name,Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS ground approach: "+name); }
        catch(Failure e){failed++;Console.Error.WriteLine("FAIL ground approach: "+name+": "+e.Message);}
        catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR ground approach: "+name+": "+e);}
    }
    private sealed class Failure(string message):Exception(message);
    private static void Check(bool good,string message){if(!good)throw new Failure(message);}

    private sealed class Queries : IGroundApproachQueries
    {
        internal Func<WorldLine,GameWorld.CGWorldFrameHitFlags,GroundRay> Ray = (_,_)=>new(false,default);
        internal Func<WoWPoint,GroundSurface?> Snapper = p=>new GroundSurface(p,AreaType.Ground);
        internal Func<WoWPoint,WoWPoint,GroundPath> Pather = (a,b)=>Program.Path(true,a,b);
        internal Func<WoWPoint,float,bool> Blocked = (_,_)=>false;
        internal Action<string> After;
        internal int TraceCalls,SnapCalls,PathCalls,ForbiddenCalls;
        public GroundRay[] Trace(WorldLine[] lines,GameWorld.CGWorldFrameHitFlags flags)
        {TraceCalls++;var result=lines.Select(x=>Ray(x,flags)).ToArray();After?.Invoke("trace");return result;}
        public GroundSurface? Snap(WoWPoint point){SnapCalls++;var r=Snapper(point);After?.Invoke("snap");return r;}
        public GroundPath Path(WoWPoint from,WoWPoint to){PathCalls++;var r=Pather(from,to);After?.Invoke("path");return r;}
        public bool Forbidden(WoWPoint point,float radius){ForbiddenCalls++;var r=Blocked(point,radius);After?.Invoke("forbidden");return r;}
    }

    private static GroundPath Path(bool complete,WoWPoint from,WoWPoint to,AreaType area=AreaType.Ground)
        =>new(complete,complete?"ok":"partial",new[]{from,to},new[]{area});
    private static bool Liquid(GameWorld.CGWorldFrameHitFlags f)=>(f&(GameWorld.CGWorldFrameHitFlags.HitTestLiquid|GameWorld.CGWorldFrameHitFlags.HitTestLiquid2))!=0;
    private static bool Desc(WorldLine l)=>l.Start.Z>l.End.Z;
    private static float Dz(WorldLine l)=>Math.Abs(l.Start.Z-l.End.Z);
    private static GroundRay Hit(float x,float y,float z)=>new(true,new WoWPoint(x,y,z));

    private static Queries Flat(Func<WorldLine,float?> projection=null,Func<WorldLine,float?> ceiling=null,bool liquid=false)
    {
        var q=new Queries();
        q.Ray=(line,flags)=>
        {
            if(Liquid(flags))return liquid?Hit(line.Start.X,line.Start.Y,0):new(false,default);
            if(Desc(line))
            {
                if(Dz(line)>10){float? z=projection?.Invoke(line)??0;return z.HasValue?Hit(line.Start.X,line.Start.Y,z.Value):new(false,default);}
                return Hit(line.Start.X,line.Start.Y,0);
            }
            float? c=ceiling?.Invoke(line);return c.HasValue?Hit(line.Start.X,line.Start.Y,c.Value):new(false,default);
        };
        q.Snapper=p=>new GroundSurface(p,AreaType.Ground);
        return q;
    }

    private static Queries LocalRegion(WoWPoint origin)
    {
        var q=HorizontalSurface(origin.Z);var observed=q.Ray;
        q.Ray=(line,flags)=>line.Start.Distance2DSqr(origin)<=96*96?observed(line,flags):new(false,default);
        // A far, partial mesh path cannot prove final arrival. It also must not
        // veto a positively supported local leg that does not claim arrival.
        q.Pather=(a,b)=>Path(false,a,b);
        return q;
    }

    private static void LocalFlightProgress(WoWPoint origin,WoWPoint destination)
    {
        var q=LocalRegion(origin);
        var search=new GroundApproachSearch(origin,destination,1,2,true,false,q,()=>true);
        var plan=search.Step(4);
        Check(plan!=null,"all candidate work was spent on unobservable remote landing geometry: "+search.LastReason);
        Check(plan.Source=="local-flight-leg"&&plan.OnwardPath==null,"local progress fabricated a final ground approach");
        Check(plan.Landing.Distance2DSqr(origin)<=96*96&&plan.Landing.Distance2DSqr(destination)<origin.Distance2DSqr(destination),
            "flight leg was outside the observed region or made no progress");
        Check(plan.OpenColumn&&plan.AirWaypoint.Z>=plan.Landing.Z+20,"local leg lost takeoff/cruise clearance");
        Check(q.PathCalls==0,"local progress queried or trusted a distant final ground path");
    }

    private static void LocalFlightUnknownArea()
    {
        var origin=new WoWPoint(100,10,0);var q=LocalRegion(origin);
        q.Snapper=p=>new GroundSurface(p,(AreaType)0);
        var search=new GroundApproachSearch(origin,new WoWPoint(800,10,0),1,2,true,false,q,()=>true);
        Check(search.Step(8)==null,"unknown polygon area authorized a local flight leg");
    }

    private static void LocalFlightSupportRevoked()
    {
        var origin=new WoWPoint(100,10,0);var q=LocalRegion(origin);
        var search=new GroundApproachSearch(origin,new WoWPoint(800,10,0),1,2,true,false,q,()=>true);
        var plan=search.Step(4);
        Check(plan!=null,"initial local progress plan missing: "+search.LastReason);
        q.Ray=(_,_)=>new(false,default);
        Check(!search.Revalidate(plan),"missing support retained local flight authority");
    }

    private static void CoveredActorProjectsBelowRoof()
    {
        var origin=new WoWPoint(0,0,10);var dest=new WoWPoint(30,0,0);
        var q=Flat(
            projection:l=>l.Start.Z>50?20:0,
            ceiling:l=>Dz(l)>50?20:null);
        q.Snapper=p=>p.Z>1?null:new GroundSurface(new WoWPoint(p.X,p.Y,0),AreaType.Ground);
        var search=new GroundApproachSearch(origin,dest,1,2,false,true,q,()=>true);
        var plan=search.Step(1);
        Check(plan!=null,"covered actor column did not produce an in-place supported landing; reason="+search.LastReason);
        Check(plan.Landing.Distance2DSqr(origin)<=.01f&&Math.Abs(plan.Landing.Z)<.1f,"covered actor selected roof/foreign surface: "+plan.Landing);
        Check(!plan.OpenColumn,"covered actor plan falsely claimed exterior open column");
    }

    // Analytic collision leaves only return hits inside the submitted segment.
    // No ground coordinate is supplied as a combat destination or snapped from air.
    private static Queries HorizontalSurface(float ground, float? roof = null)
    {
        var q = new Queries();
        q.Ray = (line, flags) =>
        {
            if (Liquid(flags)) return new(false, default);
            var surfaces = roof.HasValue ? new[] { ground, roof.Value } : new[] { ground };
            var intersections = surfaces.Where(z => z >= Math.Min(line.Start.Z, line.End.Z)
                && z <= Math.Max(line.Start.Z, line.End.Z)).OrderBy(z => Math.Abs(z - line.Start.Z)).ToArray();
            return intersections.Length == 0 ? new(false, default) : Hit(line.Start.X, line.Start.Y, intersections[0]);
        };
        q.Snapper = p => Math.Abs(p.Z - ground) < .01f ? new GroundSurface(p, AreaType.Ground) : null;
        return q;
    }

    private static void TargetlessSupport(float altitude, float ground, bool covered)
    {
        var actor = new WoWPoint(30, -20, ground + altitude);
        var q = HorizontalSurface(ground, covered ? actor.Z + 10 : null);
        var search = new GroundApproachSearch(actor, actor, 1, 2, false, covered, q, () => true);
        var plan = search.Step(1);
        Check(plan != null, "airborne local destination lost observed support: " + search.LastReason);
        Check(plan.Landing.Distance2DSqr(actor) <= .01f && Math.Abs(plan.Landing.Z - ground) < .01f,
            "targetless combat borrowed a foreign point or inferred a floor");
        Check(plan.OnwardPath == null && q.PathCalls == 0, "targetless combat invented an enemy/onward path");
        Check(plan.OpenColumn == !covered, "covered/open column observation was lost");
    }

    private static void TargetlessDepthBound()
    {
        var actor = new WoWPoint(30, -20, 640);
        var q = HorizontalSurface(0);
        var ray = q.Ray;
        float lowest = actor.Z;
        q.Ray = (line, flags) => { lowest = Math.Min(lowest, line.End.Z); return ray(line, flags); };
        var search = new GroundApproachSearch(actor, actor, 1, 2, false, false, q, () => true);
        for (int i = 0; i < 100 && !search.Exhausted; i++) search.Step(8);
        Check(search.Exhausted && search.Plan == null && search.Attempts <= 384,
            "unobserved distant support became a plan or kept searching indefinitely");
        Check(lowest >= actor.Z - 512 && q.SnapCalls == 0,
            "targetless search exceeded the local probe budget or snapped an unobserved surface");
    }

    private static void DeepSupportMissingMesh()
    {
        var actor = new WoWPoint(30, -20, 240);
        var q = HorizontalSurface(0);
        q.Snapper = _ => null;
        var search = new GroundApproachSearch(actor, actor, 1, 2, false, false, q, () => true);
        Check(search.Step(1) == null && q.SnapCalls == 1
            && search.LastReason == "support-has-no-matching-safe-mesh-surface",
            "deep collision support bypassed the independent matching mesh requirement: " + search.LastReason);
    }

    private static void CoveredDoesNotRelaxOnwardExterior()
    {
        var origin=new WoWPoint(0,0,10);var dest=new WoWPoint(30,0,0);float originProjectionStart=float.NaN;
        var q=Flat(projection:l=>
        {
            if(Math.Abs(l.Start.X)<.1f)originProjectionStart=l.Start.Z;
            return l.Start.X>20||Math.Abs(l.Start.X)<.1f?20:0;
        });
        q.Snapper=p=>p.Z>1?null:new GroundSurface(new WoWPoint(p.X,p.Y,0),AreaType.Ground);
        q.Pather=(a,b)=>Path(true,a,b,AreaType.KnownBuilding);
        var search=new GroundApproachSearch(origin,dest,1,2,true,true,q,()=>true);
        for(int i=0;i<4&&search.Plan==null;i++)search.Step(4);
        Check(float.IsFinite(originProjectionStart)&&originProjectionStart>origin.Z+50,
            "required onward search used covered in-place actor projection");
        Check(search.Plan==null||search.Plan.Landing.Distance2DSqr(origin)>.5625f,
            "required onward search accepted covered actor column as interaction approach");
    }

    private static void DeepDoorwaySelectsExterior()
    {
        var origin=new WoWPoint(0,0,20);var dest=new WoWPoint(30,0,0);var q=Flat(
            projection:l=>l.Start.X>20?8:0,
            ceiling:l=>l.Start.X>20&&Dz(l)>50?8:null);
        q.Snapper=p=>p.Z>1?null:new GroundSurface(new WoWPoint(p.X,p.Y,0),AreaType.Ground);
        q.Pather=(a,b)=>a.X<=20?new GroundPath(true,"door",new[]{a,new WoWPoint(10,0,0),new WoWPoint(20,0,0),b},new[]{AreaType.Ground,AreaType.KnownBuilding}):Path(false,a,b);
        var s=new GroundApproachSearch(origin,dest,1,2,true,false,q,()=>true);GroundApproachPlan p=null;
        for(int i=0;i<8&&p==null;i++)p=s.Step(8);
        Check(p!=null&&p.Landing.X<=20.01f&&p.OpenColumn,"did not select exterior doorway approach; "+s.LastReason);
        Check(p.OnwardPath!=null&&p.OnwardPath.Complete,"doorway omitted onward ground route");
    }

    private static void OppositeEntranceFromPath()
    {
        var origin=new WoWPoint(30,0,20);var dest=new WoWPoint(0,0,0);var q=Flat(
            projection:l=>l.Start.X>-8?8:0,
            ceiling:l=>l.Start.X>-8&&Dz(l)>50?8:null);
        q.Snapper=p=>p.Z>1?null:new GroundSurface(new WoWPoint(p.X,p.Y,0),AreaType.Ground);
        q.Pather=(a,b)=>a.X>20?new GroundPath(true,"opposite",new[]{a,new WoWPoint(-12,0,0),b},new[]{AreaType.Ground,AreaType.KnownBuilding}):a.X<=-8?Path(true,a,b,AreaType.KnownBuilding):Path(false,a,b);
        var s=new GroundApproachSearch(origin,dest,1,2,true,false,q,()=>true);GroundApproachPlan p=null;
        for(int i=0;i<12&&p==null;i++)p=s.Step(8);
        Check(p!=null&&p.Landing.X<=-8,"search ignored actual opposite entrance path; "+s.LastReason);
    }

    private static void RoofWrongFloorRejected()
    {
        var origin=new WoWPoint(0,0,20);var dest=new WoWPoint(0,0,0);var q=Flat(projection:l=>Math.Abs(l.Start.X)<1?10:0);
        q.Snapper=p=>new GroundSurface(p,AreaType.Ground);
        q.Pather=(a,b)=>a.Z>1?new GroundPath(true,"wrong-z",new[]{a,new WoWPoint(b.X,b.Y,10)},new[]{AreaType.Ground}):Path(true,a,b,AreaType.KnownBuilding);
        var s=new GroundApproachSearch(origin,dest,1,2,true,false,q,()=>true);GroundApproachPlan p=null;
        for(int i=0;i<8&&p==null;i++)p=s.Step(8);
        Check(p!=null&&Math.Abs(p.Landing.Z)<.1f,"roof/other floor was accepted as landing");
    }

    private static void ExteriorBesideWall()
    {
        var o=new WoWPoint(5,1,20);var d=new WoWPoint(12,1,0);var q=Flat();q.Pather=(a,b)=>Path(true,a,b,AreaType.KnownBuilding);
        var s=new GroundApproachSearch(o,d,1,2,true,false,q,()=>true);var p=s.Step(4);
        Check(p!=null&&p.OpenColumn,"vertical-safe exterior beside wall was rejected");
    }

    private static void MultipleCandidates()
    {
        var o=new WoWPoint(20,0,20);var d=new WoWPoint(0,0,0);var q=Flat();
        q.Snapper=p=>p.X==20?null:new GroundSurface(new WoWPoint(p.X,p.Y,0),AreaType.Ground);
        q.Blocked=(p,_)=>Math.Abs(p.X)<.1f&&Math.Abs(p.Y)<.1f;
        var s=new GroundApproachSearch(o,d,1,2,false,false,q,()=>true);GroundApproachPlan p=null;
        for(int i=0;i<4&&p==null;i++)p=s.Step(4);
        Check(p!=null&&!q.Blocked(p.Landing,1)&&s.Attempts>=2,"search did not skip rejected candidate");
    }

    private static void PathContracts()
    {
        var a=new WoWPoint(0,0,0);var b=new WoWPoint(10,0,0);
        Check(!GroundApproachSearch.Usable(Path(false,a,b),a,b),"partial path accepted");
        Check(!GroundApproachSearch.Usable(new GroundPath(true,"missing",new[]{a},new[]{AreaType.Ground}),a,b),"missing endpoint accepted");
        Check(!GroundApproachSearch.Usable(new GroundPath(true,"wrong-z",new[]{a,new WoWPoint(10,0,4)},new[]{AreaType.Ground}),a,b),"wrong-Z endpoint accepted");
        Check(GroundApproachSearch.Usable(Path(true,a,b,AreaType.KnownBuilding),a,b),"valid building path rejected");
    }

    private static void ImmutablePathOutputs()
    {
        var a=new WoWPoint(0,0,0);var b=new WoWPoint(1,0,0);var points=new[]{a,b};var areas=new[]{AreaType.Ground};
        var p=new GroundPath(true,"copy",points,areas);points[0]=new WoWPoint(99,0,0);areas[0]=AreaType.Water;
        Check(p.Points[0].Equals(a)&&p.Areas[0]==AreaType.Ground,"GroundPath retained mutable caller arrays");
    }

    private static void WaterRejected()
    {
        var q=Flat(liquid:true);var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>true);
        Check(s.Step(1)==null&&s.LastReason=="landing-footprint-intersects-liquid","water landing was admitted");
    }

    private static void CliffRejected()
    {
        var q=Flat();q.Ray=(line,flags)=>
        {
            if(Liquid(flags))return new(false,default);if(Desc(line)&&Dz(line)>10)return Hit(line.Start.X,line.Start.Y,0);
            if(Desc(line)&&line.Start.X>.5f)return Hit(line.Start.X,line.Start.Y,-3);if(Desc(line))return Hit(line.Start.X,line.Start.Y,0);return new(false,default);
        };
        var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>true);
        Check(s.Step(1)==null&&s.LastReason=="landing-footprint-not-supported","cliff footprint admitted");
    }

    private static void HeadClearanceRejected()
    {
        var q=Flat(ceiling:l=>Dz(l)<50?2:null);var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>true);
        Check(s.Step(1)==null&&s.LastReason=="landing-body-clearance-blocked","blocked head/body clearance admitted");
    }

    private static void DynamicBlockedDoorRevalidation()
    {
        bool blocked=false;var q=Flat();q.Pather=(a,b)=>Path(!blocked,a,b,AreaType.KnownBuilding);
        var s=new GroundApproachSearch(new(0,0,20),new(10,0,0),1,2,true,false,q,()=>true);var p=s.Step(4);
        Check(p!=null,"baseline doorway plan missing");blocked=true;Check(!s.Revalidate(p),"blocked doorway retained stale plan");
    }

    private static void BoundedSearch()
    {
        var q=Flat(projection:_=>null);q.Snapper=_=>null;var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>true);
        for(int i=0;i<100&&!s.Exhausted;i++)s.Step(8);
        Check(s.Exhausted&&s.Attempts<=384&&s.Plan==null,"search was unbounded or fabricated plan");
    }

    private static void ReentrantCurrentChange()
    {
        bool current=true;var q=Flat();q.After=kind=>{if(kind=="snap")current=false;};var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>current);
        bool threw=false;try{s.Step(1);}catch(ObservationUnavailableException){threw=true;}Check(threw,"reentrant owner replacement published result");
    }

    private static void CancellationPropagates()
    {
        var q=Flat();q.Ray=(_,_)=>throw new OperationCanceledException("controlled cancellation");var s=new GroundApproachSearch(new(0,0,10),new(20,0,0),1,2,false,false,q,()=>true);
        bool threw=false;try{s.Step(1);}catch(OperationCanceledException){threw=true;}Check(threw,"cancellation converted to UNKNOWN/result");
    }

    private static GroundApproachQueries Adapter(out MeshNavigator mesh,Func<bool> current=null)
    {
        mesh=new MeshNavigator();Navigator.NavigationProvider=mesh;Navigator.IsNavigatorLoaded=true;ObjectManager.Me=new(){MapId=530};Navigator.TripperNavigator=new();
        GameWorld.Observe=(lines,_) => (new bool[lines.Length],new WoWPoint[lines.Length]);BlackspotManager.Observe=(_,_)=>false;
        return new GroundApproachQueries(mesh,530,current??(()=>true));
    }

    private static void AdapterProviderReplacement()
    {
        var q=Adapter(out var mesh);GameWorld.Observe=(lines,flags)=>{Navigator.NavigationProvider=new object();return(new bool[lines.Length],new WoWPoint[lines.Length]);};
        bool threw=false;try{q.Trace(new[]{new WorldLine(new(0,0,1),new(0,0,0))},GameWorld.CGWorldFrameHitFlags.HitTestGround);}catch(ObservationUnavailableException){threw=true;}Check(threw,"adapter accepted replaced provider callback");
    }

    private static void NativeEndpointArea(string fault)
    {
        // The native 530 route receipt has Start/End, Ground/0 and a zero
        // terminal polygon. A separate positive endpoint query must fill only
        // that marker; neither the zero value nor route status grants land area.
        var q=Adapter(out var mesh);
        var result=new PathFindResult{Succeeded=true,IsPartialPath=fault=="partial",
            Points=new[]{new Vector3(100,10,0),new Vector3(140,10,0)},
            PolyTypes=new[]{fault=="interior-zero"?(AreaType)0:AreaType.Ground,(AreaType)0},
            Flags=new[]{StraightPathFlags.Start,fault=="no-end-marker"?StraightPathFlags.None:StraightPathFlags.End},
            Polygons=new[]{new PolygonReference(1),new PolygonReference(fault=="nonzero-reference"?2UL:0UL)}};
        mesh.Find=(_,_)=>result;
        int observations=0;
        Navigator.TripperNavigator.Nearest=(_,p)=>
        {
            observations++;
            if(fault=="changed-provider")Navigator.NavigationProvider=new object();
            return(fault!="missing-polygon",3,p+new Vector3(0,0,fault=="wrong-floor"?8:0));
        };
        Navigator.TripperNavigator.Area=(_,_) => (0,fault=="unknown-area"?(byte)0:(byte)AreaType.Ground);
        GroundPath path=null;bool deferred=false;
        try{path=q.Path(new(100,10,0),new(140,10,0));}catch(ObservationUnavailableException){deferred=true;}
        if(fault=="none")
            Check(!deferred&&path.Complete&&path.Areas.Count==2&&path.Areas.All(a=>a==AreaType.Ground)&&observations==1,
                "complete native endpoint marker blocked a positively observed final ground route");
        else if(fault=="changed-provider")
            Check(deferred||path.Areas[^1]==0,"replaced provider published endpoint ground authority");
        else if(fault=="interior-zero")
            Check(path.Areas[0]==0,"endpoint observation filled an unknown interior segment");
        else
            Check(path.Areas[^1]==0,"invalid or unobserved endpoint became safe ground: "+fault);
        Check(result.PolyTypes[^1]==0,"query adapter rewrote the native provider's result buffer");
    }

    private static void AdapterIncompleteCollision()
    {
        var q=Adapter(out _);GameWorld.Observe=(lines,flags)=>(Array.Empty<bool>(),Array.Empty<WoWPoint>());
        bool threw=false;try{q.Trace(new[]{new WorldLine(new(0,0,1),new(0,0,0))},GameWorld.CGWorldFrameHitFlags.HitTestGround);}catch(ObservationUnavailableException){threw=true;}Check(threw,"incomplete collision batch accepted");
    }

    private static void AdapterPartialPath()
    {
        var q=Adapter(out var mesh);mesh.Find=(a,b)=>new PathFindResult{Succeeded=true,IsPartialPath=true,Points=new[]{new Vector3(a.X,a.Y,a.Z),new Vector3(b.X,b.Y,b.Z)},PolyTypes=new[]{AreaType.Ground}};
        var p=q.Path(new(0,0,0),new(10,0,0));Check(!p.Complete&&p.Points.Count==2,"partial native path became complete or lost observations");
    }

    private static void AdapterCancellation()
    {
        var q=Adapter(out var mesh);mesh.Find=(_,_)=>throw new OperationCanceledException("adapter cancel");bool threw=false;
        try{q.Path(new(0,0,0),new(10,0,0));}catch(OperationCanceledException){threw=true;}Check(threw,"adapter swallowed control-flow cancellation");
    }
}
