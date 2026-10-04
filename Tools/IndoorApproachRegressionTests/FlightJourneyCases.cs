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

        Case("mount acknowledgement retains selected departure when ground speed changes its cost",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.ExteriorFlights.Count==1,"initial flight selection missing");
            World.Actor.MountedValue=true;World.FlightMountObserved=true;
            // This models the separate estimator's new observation after mount:
            // the live run-speed comparison is now unfavorable, capability remains.
            World.Actor.IsMoving=true;World.FlightCostRequiresStop=true;
            for(int pulse=0;pulse<5;pulse++)Tick(owner);
            Check(World.Walks.Count==0&&World.Dismounts==0&&World.ExteriorFlights.Count>1,
                "acknowledged flight mount switched to ground CTM before observed takeoff");
            World.Actor.Flags=0x02000000u;World.Actor.Position=new(100,10,5);
            Tick(owner);Check(World.Walks.Count==0,"observed lift-off lost the same selected journey");
        });
        Case("retained departure still respects revoked flight capability",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);int flights=World.ExteriorFlights.Count;
            World.Actor.MountedValue=true;World.FlightMountObserved=true;World.PreferFlight=false;
            Tick(owner);
            Check(World.ExteriorFlights.Count==flights&&World.Walks.Count==1,
                "departure retention overruled a no-longer-eligible flight capability");
        });
        Case("local waypoint arrival never requests descent",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Check(Tick(owner)==GroundTransitionState.Pending&&World.ExteriorFlights.Count==1,"initial local flight leg missing");
            ObserveFlightArrival();
            Check(Tick(owner)==GroundTransitionState.Pending&&World.Descents==0&&World.Dismounts==0,
                "intermediate flight waypoint was treated as final landing authority");
            Tick(owner);
            Check(World.ExteriorFlights.Count>=2&&World.ExteriorFlights[^1].X>World.ExteriorFlights[0].X&&World.Descents==0,"arrival did not renew the next local flight leg");
        });
        Case("safe flight renews before reaching the current local waypoint without stopping",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);var previous=World.ExteriorFlights[^1];int stops=World.Stops;
            World.Actor.Position=previous.Add(-20,0,0);World.Actor.MountedValue=true;World.Actor.Flags=0x02000000u;World.Actor.IsMoving=true;
            Tick(owner);
            Check(World.ExteriorFlights[^1].X>previous.X+10,"local flight did not prepare forward movement before its CTM endpoint");
            Check(World.Stops==stops&&World.Descents==0&&World.Dismounts==0,"local renewal stopped or landed a continuing journey");
        });
        Case("observed intermediate arrival dispatches its safe successor without an idle pulse",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);int stops=World.Stops;var previous=World.ExteriorFlights[^1];ObserveFlightArrival();
            Tick(owner);
            Check(World.Stops==stops&&World.ExteriorFlights.Count==2&&World.ExteriorFlights[^1].X>previous.X,
                "an intermediate arrival introduced a stop or required an extra planning pulse");
        });
        Case("time-stepped flight crosses multiple local regions without intermediate idle",()=>
        {
            World.Actor.Flags=0x02000000u;World.Actor.MountedValue=true;World.Actor.Position=new(100,10,40);
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            bool input=false;WoWPoint destination=default;int idle=0;
            World.Callback=stage=>
            {
                if(stage=="stop")input=false;
                if(stage=="exterior-flight"){destination=World.ExteriorFlights[^1];input=true;}
            };
            Tick(owner);int stops=World.Stops;float start=World.Actor.Position.X;
            for(int frame=0;frame<120;frame++)
            {
                const float step=18.2f*.2f;
                float distance=World.Actor.Position.Distance(destination);
                if(input&&distance>0)
                {
                    float fraction=Math.Min(1,step/distance);
                    var position=World.Actor.Position;
                    World.Actor.Position=new(position.X+(destination.X-position.X)*fraction,
                        position.Y+(destination.Y-position.Y)*fraction,position.Z+(destination.Z-position.Z)*fraction);
                    if(fraction==1)input=false;
                }
                if(!input)idle++;
                World.Actor.IsMoving=input;
                Tick(owner,.2);
            }
            Check(World.Actor.Position.X>start+400,"simulated flight failed to make sustained progress");
            Check(idle==0&&World.Stops==stops,"continuous flight introduced "+idle+" idle frames and "+(World.Stops-stops)+" stop commands");
            Check(World.Dismounts==0&&World.Descents==0,"intermediate progress acquired final landing authority");
        });
        Case("optional flight-cost review never stops useful mounted ground travel",()=>
        {
            World.Actor.IsMoving=true;World.Actor.MountedValue=true;World.FlightCostRequiresStop=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            for(int pulse=0;pulse<8;pulse++)
            {
                World.Actor.Position=new(100+pulse*20,10,0);Tick(owner,4);
            }
            Check(World.Stops==0&&World.Walks.Count==8,"a mount-choice observation repeatedly paused established ground travel");
            Check(World.ExteriorFlights.Count==0&&World.Dismounts==0,"unavailable flight cost authorized a mount switch");
        });
        Case("unavailable lookahead does not stop the still-valid current flight leg",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);var previous=World.ExteriorFlights[^1];int stops=World.Stops;
            World.Actor.Position=previous.Add(-20,0,0);World.Actor.MountedValue=true;World.Actor.Flags=0x02000000u;
            World.FutureGeometryUnavailableAfterX=previous.X+1;
            Tick(owner);
            Check(World.Stops==stops,"an unavailable future region stopped the currently validated route");
            Check(World.ExteriorFlights[^1].Equals(previous)&&World.Descents==0,"missing future geometry authorized a new endpoint or descent");
        });
        Case("semantic replacement during lookahead never dispatches a successor",()=>
        {
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);var previous=World.ExteriorFlights[^1];int commands=World.ExteriorFlights.Count;
            World.Actor.Position=previous.Add(-20,0,0);World.Actor.MountedValue=true;World.Actor.Flags=0x02000000u;
            World.Callback=stage=>{if(stage=="tiles")World.Target.Guid++;};
            Check(Tick(owner)==GroundTransitionState.Revoked,"replacement while preparing future geometry retained the old journey");
            Check(World.ExteriorFlights.Count==commands&&World.Descents==0&&World.Dismounts==0,"lookahead dispatched input after semantic replacement");
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
        Case("selected flight preparation waits for an observed stop",()=>
        {
            World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);
            Check(World.Stops>0&&World.Walks.Count==0&&World.ExteriorFlights.Count==0,"moving cost rejection silently selected ground travel");
            World.Actor.IsMoving=false;Tick(owner);
            Check(World.ExteriorFlights.Count==1,"observed stop did not release the flight decision");
        });
        Case("moving ground fallback stops only after flight is selected",()=>
        {
            World.PreferFlight=false;World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Walks.Count==1,"unavailable flight interrupted initial ground travel");
            World.PreferFlight=true;World.Actor.Position=new(150,10,0);now+=4;Tick(owner);
            Check(World.Stops>0&&World.Walks.Count==1,"flight reconsideration never stopped its changing cost inputs");
            World.Actor.IsMoving=false;Tick(owner);
            Check(World.ExteriorFlights.Count==1,"stopped review remained pinned to its old ground decision");
        });
        Case("an unacknowledged selected-flight preparation stop yields ground progress",()=>
        {
            World.Actor.IsMoving=true;
            using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
            Tick(owner);Check(World.Stops>0&&World.Walks.Count==0,"initial review stop was not attempted");
            for(int pulse=0;pulse<5;pulse++)Tick(owner,1);
            Check(World.Walks.Count>0&&World.ExteriorFlights.Count==0&&World.Dismounts==0,"failed review stop parked travel or fabricated flight");
        });
        Case("incidental aggro revokes a selected-flight preparation stop",()=>
        {
            World.Actor.IsMoving=true;World.Actor.MountedValue=true;
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
