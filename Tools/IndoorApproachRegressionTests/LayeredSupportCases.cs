using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.Logic.POI;

internal static class LayeredSupportCases
{
    [ModuleInitializer] internal static void Run()
    {
        int passed=0,total=0;
        void Case(string name,Action test)
        {
            total++;
            World.Reset();World.Actor.Position=new(100,10,0);World.Actor.Flags=0;
            World.Actor.MountedValue=true;World.FlightMountObserved=true;
            World.Target.Position=new(800,10,0);BotPoi.Current.Position=World.Target.Position;
            World.Liquid=true;
            try{test();passed++;Console.WriteLine("PASS layered support: "+name);}
            catch(Exception error){Console.Error.WriteLine("FAIL layered support: "+name+": "+error.Message);}
            finally{Styx.BotEvents.Stop();}
        }
        foreach(float depth in new[]{-.918f,-.2f,-1.2f})
        {
            float water=depth;
            Case("solid ground above water keeps ground progress/"+water,()=>
            {
                World.LiquidSurfaceZ=water;
                using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
                owner.Tick(World.Target.Position,World.Target,()=>true);
                Check(World.Walks.Count==1&&World.Dismounts==0,"buried liquid layer rejected a positively supported grounded journey");
            });
            Case("solid landing and departure footprints above water remain usable/"+water,()=>
            {
                World.LiquidSurfaceZ=water;World.PreferFlight=true;
                using var owner=new GroundTransition(GroundTransitionPurpose.Interaction);
                owner.Tick(World.Target.Position,World.Target,()=>true);
                Check(World.ExteriorFlights.Count==1&&World.Walks.Count==0,"buried liquid layer rejected the safe departure or landing footprint");
            });
            Case("supported combat landing above water releases one pending dismount/"+water,()=>
            {
                World.LiquidSurfaceZ=water;
                using var owner=new GroundTransition(GroundTransitionPurpose.Combat);
                owner.Tick(World.Target.Position,World.Target,()=>true);
                Check(World.Dismounts==1,"buried liquid layer stranded a supported combat mount");
            });
        }
        foreach(float water in new[]{0f,.1f,.6f})
            Case("exposed liquid is never dry landing authority/"+water,()=>
            {
                World.LiquidSurfaceZ=water;
                using var owner=new GroundTransition(GroundTransitionPurpose.Combat);
                var result=owner.Tick(World.Target.Position,World.Target,()=>true);
                Check(result!=GroundTransitionState.Ready&&World.Dismounts==0,"exposed water authorized ground dismount");
            });
        Console.WriteLine($"Layered support: {passed}/{total}; actual shared runtime and footprint search, controlled layered solid/liquid rays.");
        if(passed!=total)Environment.ExitCode=1;
    }
    private static void Check(bool good,string message){if(!good)throw new InvalidOperationException(message);}
}
