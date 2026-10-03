using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.Logic.POI;

/// <summary>Actual shared owner/search/machine with separately injected client acknowledgements.</summary>
internal static class FlightJourneyCases
{
    private static double now;
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Case(string name,Action body)
        {
            total++;
            try
            {
                World.Reset();now=0;GroundTransitionRuntime.MonotonicClockOverride=()=>now;
                World.Actor.Position=new(100,10,0);World.Actor.Flags=0;World.Actor.MountedValue=false;
                World.Target.Position=new(800,10,0);BotPoi.Current.Position=World.Target.Position;
                World.PreferFlight=true;
                body();passed++;Console.WriteLine("PASS actual flight journey: "+name);
            }
            catch(Exception error) { Console.Error.WriteLine("FAIL actual flight journey: "+name+": "+error); }
            finally { Styx.BotEvents.Stop();GroundTransitionRuntime.MonotonicClockOverride=null; }
        }

        Case("local waypoint arrival never requests descent",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Check(Tick(owner)==GroundTransitionState.Pending&&World.ExteriorFlights.Count==1,"initial local flight leg missing");
            ObserveFlightArrival();
            Check(Tick(owner)==GroundTransitionState.Pending&&World.Descents==0&&World.Dismounts==0,
                "intermediate flight waypoint was treated as final landing authority");
            Tick(owner);
            Check(World.ExteriorFlights.Count==2&&World.Descents==0,"arrival did not renew the next local flight leg");
        });
        Case("an outdoor-reported roof requires ground departure",()=>
        {
            World.Actor.Position=new(205,15,0);World.Actor.Outdoors=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Walks.Count>0&&World.ExteriorFlights.Count==0&&World.Dismounts==0,
                "outdoors flag overruled the actual overhead collision");
        });
        Case("an indoor departure reconsiders flight after reaching open ground",()=>
        {
            World.Actor.Outdoors=false;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Walks.Count==1,"initial indoor ground departure missing");
            World.Actor.Position=new(150,10,0);World.Actor.Outdoors=true;now+=4;
            Tick(owner);
            Check(World.ExteriorFlights.Count>0&&World.Dismounts==0,"indoor fallback permanently committed the journey to ground");
        });
        Case("a previously unavailable flight choice is reviewed after useful ground progress",()=>
        {
            World.PreferFlight=false;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Walks.Count==1,"initial ground travel missing");
            World.PreferFlight=true;World.Actor.Position=new(150,10,0);now+=4;
            Tick(owner);
            Check(World.ExteriorFlights.Count>0,"ground choice hid later eligible flight");
        });
        Case("unknown local mesh falls back then recovers in a newly observed region",()=>
        {
            World.MissingMesh=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            for(int i=0;i<12&&World.Walks.Count==0;i++)Tick(owner);
            Check(World.ExteriorFlights.Count==0&&World.Walks.Count>0,"missing mesh authorized flight or indefinitely parked travel");
            World.MissingMesh=false;World.Actor.Position=new(150,10,0);now+=4;
            Tick(owner);
            Check(World.ExteriorFlights.Count>0,"new positive local geometry could not recover flight priority");
        });
        Case("coordinate transit also uses eligible distant flight",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Transit);
            now+=.25;
            owner.TickTransit(World.Target.Position,700,()=>true);
            Check(World.ExteriorFlights.Count>0&&World.Walks.Count==0,"coordinate transit bypassed eligible flight selection");
        });
        Case("ground mount keeps moving when flying remains unavailable",()=>
        {
            World.PreferFlight=false;World.Actor.MountedValue=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            for(int i=0;i<6;i++)
            {
                World.Actor.Position=new(100+i*30,10,0);now+=4;Tick(owner);
            }
            Check(World.Walks.Count==6&&World.ExteriorFlights.Count==0&&World.Dismounts==0,
                "flight review disrupted useful mounted ground movement");
        });
        Case("long flight renews local progress and retains final landing acknowledgements",LongFlight);
        Case("moving departure waits for a stopped flight decision",()=>
        {
            World.FlightCostRequiresStop=true;World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Stops>0&&World.Walks.Count==0&&World.ExteriorFlights.Count==0,"moving cost rejection silently selected ground travel");
            World.Actor.IsMoving=false;Tick(owner);
            Check(World.ExteriorFlights.Count==1,"observed stop did not release the flight decision");
        });
        Case("moving ground fallback can acquire a stopped flight decision",()=>
        {
            World.PreferFlight=false;World.FlightCostRequiresStop=true;World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Walks.Count==1,"unavailable flight interrupted initial ground travel");
            World.PreferFlight=true;World.Actor.Position=new(150,10,0);now+=4;Tick(owner);
            Check(World.Stops>0&&World.Walks.Count==1,"flight reconsideration never stopped its changing cost inputs");
            World.Actor.IsMoving=false;Tick(owner);
            Check(World.ExteriorFlights.Count==1,"stopped review remained pinned to its old ground decision");
        });
        Case("an unacknowledged review stop yields ground progress",()=>
        {
            World.FlightCostRequiresStop=true;World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Stops>0&&World.Walks.Count==0,"initial review stop was not attempted");
            for(int pulse=0;pulse<5;pulse++)Tick(owner,1);
            Check(World.Walks.Count>0&&World.ExteriorFlights.Count==0&&World.Dismounts==0,"failed review stop parked travel or fabricated flight");
        });
        Case("incidental aggro revokes an optional review stop",()=>
        {
            World.FlightCostRequiresStop=true;World.Actor.IsMoving=true;World.Actor.MountedValue=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Stops>0&&World.Walks.Count==0,"initial review stop was not attempted");
            World.Actor.Combat=true;Tick(owner);
            Check(World.Walks.Count==1&&World.Dismounts==0&&World.ExteriorFlights.Count==0,"review stop overruled mounted escape after combat changed");
        });
        Case("ground-to-flight upgrade waits for one observed mount removal",()=>
        {
            World.Actor.MountedValue=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Dismounts==1&&World.ExteriorFlights.Count==0,"flying mount was requested while the ground mount remained observed");
            for(int pulse=0;pulse<5;pulse++)Tick(owner);
            Check(World.Dismounts==1&&World.ExteriorFlights.Count==0,"pending ground mount removal repeated or acknowledged itself");
            World.Actor.MountedValue=false;
            Tick(owner);
            Check(World.ExteriorFlights.Count==1&&World.Dismounts==1,"observed ground mount removal did not release the flight request");
        });
        Case("an already observed flying mount takes off without removing it",()=>
        {
            World.Actor.MountedValue=true;World.FlightMountObserved=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Dismounts==0&&World.ExteriorFlights.Count==1,"flight preparation removed the mount already selected for flying");
        });
        Case("incidental combat before flight upgrade keeps ground travel",()=>
        {
            World.Actor.MountedValue=true;World.Actor.Combat=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Dismounts==0&&World.ExteriorFlights.Count==0&&World.Walks.Count==1,"flight upgrade overruled mounted escape");
        });
        Case("late combat revokes mount-switch removal at the effect boundary",()=>
        {
            World.Actor.MountedValue=true;
            World.Callback=stage=>{if(stage=="dismount-prepare")World.Actor.Combat=true;};
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Actor.Combat&&World.Dismounts==0&&World.ExteriorFlights.Count==0,"late combat admitted mount-switch effects");
            World.Callback=null;Tick(owner);
            Check(World.Walks.Count>0&&World.Dismounts==0,"revoked upgrade did not resume mounted escape");
        });
        Case("an unacknowledged upgrade yields bounded mounted ground progress",()=>
        {
            World.Actor.MountedValue=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Dismounts==1,"initial upgrade removal missing");
            for(int pulse=0;pulse<15;pulse++)Tick(owner,1);
            Check(World.Dismounts==1&&World.ExteriorFlights.Count==0&&World.Walks.Count>0,
                "unobserved upgrade parked travel or repeatedly removed the mount");
        });
        Console.WriteLine($"Actual flight journeys: {passed}/{total}; linked shared runtime, search, machine and context; controlled native/client leaves; no live traversal proof.");
        if(passed!=total)Environment.ExitCode=1;
    }

    private static void Check(bool condition,string reason)
    { if(!condition)throw new InvalidOperationException(reason); }

    private static GroundTransitionState Tick(GroundTransition owner,double seconds=.25)
    { now+=seconds;return owner.Tick(World.Target.Position,World.Target,()=>true); }

    private static void ObserveFlightArrival()
    {
        Check(World.ExteriorFlights.Count>0,"no submitted flight waypoint to observe");
        World.Actor.Position=World.ExteriorFlights[^1];World.Actor.MountedValue=true;World.Actor.Flags=0x02000000u;
    }

    private static void LongFlight()
    {
        World.Target.Position=new(3000,10,0);BotPoi.Current.Position=World.Target.Position;
        using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
        for(int pulse=0;pulse<150&&World.Descents==0;pulse++)
        {
            int requests=World.ExteriorFlights.Count;
            Check(Tick(owner,4)==GroundTransitionState.Pending,"productive long flight expired: "+owner.Phase);
            Check(World.Dismounts==0&&World.Walks.Count==0,"incomplete flight triggered ground travel/removal");
            if(World.ExteriorFlights.Count>requests)ObserveFlightArrival();
        }
        Check(now>120&&World.ExteriorFlights.Count>20,"fixture did not exercise a journey longer than the landing deadline");
        Check(World.Descents==1&&World.Actor.Position.Distance2DSqr(World.Target.Position)<=25,
            "descent was requested at an intermediate region or final landing never began");
        Check(Tick(owner)==GroundTransitionState.Pending&&World.Dismounts==0,"descent submission acknowledged landing");
        // Later client updates separately report supported ground, then mount loss.
        World.Actor.Position=World.Target.Position.Add(-2,0,0);World.Actor.Flags=0;
        Check(Tick(owner)==GroundTransitionState.Pending&&World.Dismounts==0,"landing did not stop retained descent first");
        Check(Tick(owner)==GroundTransitionState.Pending&&World.Dismounts==1,"final supported landing did not request one dismount");
        Check(Tick(owner)==GroundTransitionState.Pending&&World.Dismounts==1,"pending dismount was duplicated");
        World.Actor.MountedValue=false;
        Check(Tick(owner)==GroundTransitionState.Ready,"observed final unmount did not release interaction");
        Check(World.Interactions.Count==0,"travel owner fabricated an interaction/completion receipt");
    }
}
