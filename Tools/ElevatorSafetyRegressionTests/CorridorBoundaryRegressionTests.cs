using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;

internal static class CorridorBoundaryRegressionTests
{
    private static readonly DateTime Epoch = new(2026,9,12,0,0,0,DateTimeKind.Utc);
    private static readonly MethodInfo? DistinctCorridors = typeof(ElevatorTransitController)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .SingleOrDefault(method => method.Name == "Observe" && method.GetParameters().Length == 10);

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("safe wait point never authorizes unsafe ascending boarding", () => UnsafeBoard(true)),
            ("safe wait point never authorizes unsafe descending boarding", () => UnsafeBoard(false)),
            ("actual boarding revocation stops an already boarding controller", () =>
            {
                var c = Ready();
                Check(Observe(c,800,board:false).Kind == ElevatorTransitAction.Wait,"the actual segment permission must be continuing");
            }),
            ("boarding permission recovery requires a fresh dock dwell", () =>
            {
                var c=Ready(); Observe(c,800,board:false);
                Check(Observe(c,1600).Kind == ElevatorTransitAction.Wait,"discard confirmation from before unsafe boarding");
                Check(Observe(c,2349).Kind == ElevatorTransitAction.Wait,"fresh dwell must last 750ms");
                Check(Observe(c,2350).Kind == ElevatorTransitAction.MoveToBoard,"safe recovery must not deadlock boarding");
            }),
            ("safe approach remains possible while the platform is absent", () =>
            {
                var c=Create(); var result=Observe(c,0,player:new WoWPoint(12,0,100),live:WoWPoint.Empty,available:false,board:false);
                Check(result.Kind == ElevatorTransitAction.MoveToWait && result.Target == c.WaitPoint,"approach and boarding are different permissions");
            }),
            ("an unsafe approach cannot borrow boarding permission", () =>
            {
                var c=Create(); Check(Observe(c,0,player:new WoWPoint(12,0,100),approach:false).Kind == ElevatorTransitAction.Wait,"check the actual approach leg");
            }),
            ("selected attachment retains ride ownership despite missing ground corridors", () =>
            {
                var c=Ready(); Check(Observe(c,800,attached:11,ground:false,approach:false,board:false).Kind == ElevatorTransitAction.Ride,"attachment is not a fresh ground boarding request");
            }),
            ("every configured landing and dock rejects non-finite coordinates", InvalidGeometry),
            ("non-finite player observations cannot authorize any movement", () =>
            {
                foreach(var bad in BadPoints())
                {
                    var c=Ready(); var result=Observe(c,800,player:bad);
                    Check(result.Kind == ElevatorTransitAction.Wait,"non-finite player position must fail closed");
                }
            }),
            ("non-finite available platform observations cannot authorize boarding", () =>
            {
                foreach(var bad in BadPoints())
                {
                    var c=Ready(); var result=Observe(c,800,live:bad);
                    Check(result.Kind != ElevatorTransitAction.MoveToBoard,"NaN distance comparisons must not keep boarding authorized");
                }
            }),
            ("invalid platform observation discards previous dock evidence", () =>
            {
                var c=Ready(); Observe(c,800,live:new WoWPoint(float.NaN,0,100));
                Check(Observe(c,1600).Kind == ElevatorTransitAction.Wait,"invalid observations break dwell continuity");
                Check(Observe(c,2350).Kind == ElevatorTransitAction.MoveToBoard,"valid fresh dwell recovers");
            }),
            ("seeded unsafe segments never produce MoveToBoard", () =>
            {
                var random=new Random(4171);
                for(int i=0;i<500;i++)
                {
                    var c=Ready(); var result=Observe(c,800+random.Next(10000),board:false,ground:random.Next(2)==0);
                    Check(result.Kind != ElevatorTransitAction.MoveToBoard,"unsafe segment authorized in seeded case "+i);
                }
            }),
            ("safe complete crossing remains possible in both directions", () =>
            {
                foreach(bool ascending in new[]{false,true})
                {
                    var c=Ready(ascending);
                    Check(Observe(c,800,player:c.StartDock,attached:11).Kind == ElevatorTransitAction.Ride,"selected attachment begins ride");
                    Check(Observe(c,5000,player:c.EndDock,live:c.EndDock,attached:11).Kind == ElevatorTransitAction.Ride,"one destination observation is insufficient");
                    var exit=Observe(c,5750,player:c.EndDock,live:c.EndDock,attached:11);
                    Check(exit.Kind == ElevatorTransitAction.MoveToExit && exit.Target == c.ExitPoint,"exit uses destination-side landing");
                    Check(Observe(c,6000,player:c.ExitPoint,live:c.EndDock).Kind == ElevatorTransitAction.Complete,"grounded destination-side completion");
                }
            }),
            ("reset revokes the selected transit", () =>
            {
                var c=Ready(); c.Reset();
                Check(c.SelectedTransportGuid==0,"reset must release transport identity");
                try { Observe(c,1000); } catch(InvalidOperationException) { return; }
                throw new InvalidOperationException("inactive transit must not continue");
            })
        };
        var failures=new List<string>();
        foreach(var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS lift boundary: "+test.Name); }
            catch(Exception error)
            {
                while(error is TargetInvocationException wrapped && wrapped.InnerException != null) error=wrapped.InnerException;
                failures.Add(test.Name+": "+error.Message); Console.Error.WriteLine("FAIL lift boundary: "+failures[^1]);
            }
        }
        Console.WriteLine($"Lift corridor/finite-input scenarios: {tests.Length-failures.Count}/{tests.Length}; 500 seeded permission checks; no game attached.");
        if(failures.Count!=0) throw new InvalidOperationException(string.Join(Environment.NewLine,failures));
    }

    private static ElevatorTransitController Create(bool ascending=false)
    {
        float start=ascending?0:100,end=ascending?100:0;
        var c=new ElevatorTransitController();
        c.Begin(11,4171,new WoWPoint(0,0,start),new WoWPoint(0,0,end),new WoWPoint(4,0,start),new WoWPoint(4,0,end));
        return c;
    }
    private static ElevatorTransitController Ready(bool ascending=false)
    {
        var c=Create(ascending);
        Check(Observe(c,0).Kind==ElevatorTransitAction.Wait,"fixture must first wait");
        Check(Observe(c,750).Kind==ElevatorTransitAction.MoveToBoard,"fixture must establish stable boarding");
        return c;
    }
    private static void UnsafeBoard(bool ascending)
    {
        var c=Create(ascending); Observe(c,0,board:false);
        Check(Observe(c,750,board:false).Kind==ElevatorTransitAction.Wait,"safe approach permission is not permission for the different platform segment");
    }
    private static ElevatorTransitDecision Observe(ElevatorTransitController c,int ms,
        WoWPoint? player=null,WoWPoint? live=null,bool available=true,ulong attached=0,
        bool ground=true,bool approach=true,bool board=true,bool exit=true)
    {
        var time=Epoch.AddMilliseconds(ms); var p=player??c.WaitPoint; var platform=live??c.StartDock;
        // Baseline replay uses the actual old two-permission controller exactly as
        // HandleElevator called it: its 'boarding' input was the approach-to-wait result.
        // After migration the additional permission is supplied to the real overload.
        if(DistinctCorridors==null)
            return c.Observe(time,p,platform,available,attached,false,ground,approach,exit);
        try { return (ElevatorTransitDecision)DistinctCorridors.Invoke(c,new object[]{time,p,platform,available,attached,false,ground,approach,board,exit})!; }
        catch(TargetInvocationException error) when(error.InnerException is InvalidOperationException invalid) { throw invalid; }
    }
    private static IEnumerable<WoWPoint> BadPoints()
    {
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            foreach(int axis in new[]{0,1,2})
                yield return new WoWPoint(axis==0?bad:0,axis==1?bad:0,axis==2?bad:100);
    }
    private static void InvalidGeometry()
    {
        foreach(var bad in BadPoints())
            for(int slot=0;slot<4;slot++)
            {
                var points=new[]{new WoWPoint(0,0,100),new WoWPoint(0,0,0),new WoWPoint(4,0,100),new WoWPoint(4,0,0)};
                points[slot]=bad; bool rejected=false;
                try { new ElevatorTransitController().Begin(11,4171,points[0],points[1],points[2],points[3]); }
                catch(ArgumentException) { rejected=true; }
                Check(rejected,"invalid configured geometry must be rejected before activation, slot="+slot);
            }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
