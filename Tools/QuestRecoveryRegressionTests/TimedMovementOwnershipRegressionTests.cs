using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals;
using Direction = Styx.WoWInternals.WoWMovement.MovementDirection;

internal static class TimedMovementOwnershipRegressionTests
{
    private static readonly DateTime Epoch = new(2026,9,12,0,0,0,DateTimeKind.Utc);
    [ModuleInitializer]
    internal static void Run()
    {
        if (ObjectManager.Me != null || ObjectManager.Wow != null || ObjectManager.Executor != null)
            throw new InvalidOperationException("Timed movement regressions require an unattached process.");
        var tests = new (string Name, Action Test)[]
        {
            ("overlapping mask renewal does not stop a renewed direction", () =>
            {
                var s = new WoWMovement.TimedMovementSchedule();
                s.Schedule(Direction.Forward | Direction.StrafeLeft, Epoch.AddSeconds(1));
                s.Schedule(Direction.Forward, Epoch.AddSeconds(3));
                Equal(Direction.StrafeLeft,s.TakeExpired(Epoch.AddSeconds(1)),"only the non-renewed bit expires");
                Equal(Direction.Forward,s.TakeExpired(Epoch.AddSeconds(3)),"renewed direction retains its own deadline");
            }),
            ("a composite renewal replaces both prior single-bit deadlines", () =>
            {
                var s = new WoWMovement.TimedMovementSchedule();
                s.Schedule(Direction.Forward,Epoch.AddSeconds(1));
                s.Schedule(Direction.StrafeLeft,Epoch.AddSeconds(2));
                s.Schedule(Direction.Forward | Direction.StrafeLeft,Epoch.AddSeconds(4));
                Equal(Direction.None,s.TakeExpired(Epoch.AddSeconds(3)),"no stale single-bit deadline may stop a composite renewal");
                Equal(Direction.Forward | Direction.StrafeLeft,s.TakeExpired(Epoch.AddSeconds(4)),"both renewed bits expire together");
            }),
            ("deadline boundary expires once", () =>
            {
                var s = new WoWMovement.TimedMovementSchedule(); s.Schedule(Direction.Forward,Epoch);
                Equal(Direction.None,s.TakeExpired(Epoch.AddTicks(-1)),"not early");
                Equal(Direction.Forward,s.TakeExpired(Epoch),"inclusive deadline");
                Equal(Direction.None,s.TakeExpired(Epoch),"never twice");
            }),
            ("directional stop cancels only that pending timer", () => WithGlobal(s =>
            {
                s.Schedule(Direction.Forward | Direction.StrafeLeft,Epoch);
                WoWMovement.MoveStop(Direction.Forward);
                Equal(Direction.StrafeLeft,s.TakeExpired(Epoch),"explicit stop must remove a stale timer without dropping the other bit");
            })),
            ("direct StopMovement cancels a pending timer", () => WithGlobal(s =>
            {
                s.Schedule(Direction.Forward,Epoch); WoWMovement.StopMovement(Direction.Forward);
                Equal(Direction.None,s.TakeExpired(Epoch),"public direct stop must use the same timer ownership boundary");
            })),
            ("global stop cancels timers even after the player disappears", () => WithGlobal(s =>
            {
                s.Schedule(Direction.Forward | Direction.StrafeLeft,Epoch); WoWMovement.MoveStop();
                Equal(Direction.None,s.TakeExpired(Epoch),"a no-mover early return must not retain prior-run deadlines");
            })),
            ("untimed start revokes the older timer", () => WithGlobal(s =>
            {
                s.Schedule(Direction.Forward,Epoch); WoWMovement.Move(Direction.Forward,true);
                Equal(Direction.None,s.TakeExpired(Epoch),"new indefinite ownership must not inherit an old duration");
            })),
            ("untimed convenience start revokes the older timer", () => WithGlobal(s =>
            {
                s.Schedule(Direction.Forward,Epoch); WoWMovement.Move(Direction.Forward);
                Equal(Direction.None,s.TakeExpired(Epoch),"all start entry points must revoke superseded durations");
            })),
            ("unrepresentable duration never starts movement", () => WithGlobal(s =>
            {
                int starts=0; Action<WoWMovement.MovementEventArgs> listener = e => { if (!e.Stop) starts++; };
                WoWMovement.OnMovementFlagsChanged += listener;
                try
                {
                    try { WoWMovement.Move(Direction.Forward,TimeSpan.MaxValue); }
                    catch(ArgumentOutOfRangeException) { }
                    Check(starts==0,"validate the deadline before dispatching a movement start");
                    Equal(Direction.None,s.TakeExpired(DateTime.MaxValue),"failed start leaves no schedule");
                }
                finally { WoWMovement.OnMovementFlagsChanged -= listener; }
            })),
            ("zero duration does not start a one-tick movement", () => WithGlobal(s =>
            {
                int starts=0; Action<WoWMovement.MovementEventArgs> listener = e => { if (!e.Stop) starts++; };
                WoWMovement.OnMovementFlagsChanged += listener;
                try { WoWMovement.Move(Direction.Forward,TimeSpan.Zero); Check(starts==0,"zero duration is a stop/no-op, not a start"); }
                finally { WoWMovement.OnMovementFlagsChanged -= listener; }
            })),
            ("seeded mask schedules agree with per-direction deadline model", Model)
        };
        int failed=0;
        foreach(var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS timed movement: "+test.Name); }
            catch(Exception e) { failed++; Console.Error.WriteLine("FAIL timed movement: "+test.Name+": "+e.Message); }
        }
        Console.WriteLine($"Timed movement scenarios: {tests.Length-failed}/{tests.Length}; 2000 seeded model operations; no game attached.");
        if(failed!=0) throw new InvalidOperationException($"{failed} timed movement scenarios failed.");
    }
    private static void Model()
    {
        var schedule=new WoWMovement.TimedMovementSchedule(); var model=new Dictionary<Direction,DateTime>();
        var bits=new[]{Direction.Forward,Direction.Backwards,Direction.StrafeLeft,Direction.StrafeRight};
        var random=new Random(12340); DateTime now=Epoch;
        for(int op=0;op<2000;op++)
        {
            now=now.AddMilliseconds(random.Next(0,4));
            if(random.Next(3)!=0)
            {
                Direction mask=Direction.None; foreach(var bit in bits) if(random.Next(2)==0) mask|=bit;
                DateTime deadline=now.AddMilliseconds(random.Next(0,30)); schedule.Schedule(mask,deadline);
                foreach(var bit in bits) if((mask&bit)!=0) model[bit]=deadline;
            }
            else
            {
                Direction expected=Direction.None;
                foreach(var pair in model.ToArray()) if(pair.Value<=now) { expected|=pair.Key; model.Remove(pair.Key); }
                Equal(expected,schedule.TakeExpired(now),"model mismatch at operation "+op);
            }
        }
        Direction tail=Direction.None; foreach(var bit in model.Keys) tail|=bit;
        Equal(tail,schedule.TakeExpired(DateTime.MaxValue),"model drain");
    }
    private static void WithGlobal(Action<WoWMovement.TimedMovementSchedule> test)
    {
        var schedule=(WoWMovement.TimedMovementSchedule)typeof(WoWMovement).GetField("_timedMovements",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        schedule.TakeExpired(DateTime.MaxValue);
        try { test(schedule); } finally { schedule.TakeExpired(DateTime.MaxValue); }
    }
    private static void Equal(Direction expected,Direction actual,string message) => Check(expected==actual,$"{message}: expected={expected}; actual={actual}");
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
