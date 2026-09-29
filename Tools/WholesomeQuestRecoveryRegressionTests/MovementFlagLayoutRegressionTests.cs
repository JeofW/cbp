using System;
using System.Runtime.CompilerServices;
using Styx.WoWInternals.WoWObjects;

// Independent original-build constants: TC335 8fda442f UnitDefines.h,
// corroborated by AC WotLK and build12340 IsFalling/far-fall writers.
// Consumer fixtures also exercise actual landing and raw falling behavior.
internal static class MovementFlagLayoutRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var expected = new (string Name, uint Value)[]
        {
            ("None",0), ("Forward",1), ("Backward",2), ("StrafeLeft",4), ("StrafeRight",8),
            ("TurnLeft",0x10), ("TurnRight",0x20), ("PitchUp",0x40), ("PitchDown",0x80),
            ("Walk",0x100), ("Walking",0x100), ("OnTransport",0x200), ("Levitating",0x400),
            ("Root",0x800), ("Falling",0x1000), ("FallingFar",0x2000), ("PendingStop",0x4000),
            ("PendingStrafeStop",0x8000), ("PendingSTrFlagStop",0x8000), ("PendingForward",0x10000),
            ("PendingBackward",0x20000), ("PendingStrafeLeft",0x40000), ("PendingSTrFlagLeft",0x40000),
            ("PendingStrafeRight",0x80000), ("PendingSTrFlagRight",0x80000), ("PendingRoot",0x100000),
            ("Swimming",0x200000), ("Ascending",0x400000), ("Descending",0x800000),
            ("CanFly",0x1000000), ("Flying",0x2000000), ("SplineElevation",0x4000000),
            ("SplineEnabled",0x8000000), ("WaterWalking",0x10000000), ("Waterwalking",0x10000000),
            ("SafeFall",0x20000000), ("FallingSlow",0x20000000), ("Hover",0x40000000), ("FallMask",0x3000)
        };
        int passed=0, assertions=0;
        foreach (var item in expected)
        {
            if (Enum.TryParse(item.Name, out WoWMovementInfo.MovementFlag flag) && (uint)flag==item.Value)
            { passed++; Console.WriteLine("PASS movement flag layout: "+item.Name); }
            else
            { assertions++; Console.Error.WriteLine($"FAIL movement flag layout: {item.Name}; expected 0x{item.Value:x}, actual 0x{(uint)flag:x}"); }
        }
        Console.WriteLine($"Movement flag layout scenarios: {passed}/{expected.Length}; assertions={assertions}; unexpected=0; real compiled enum, independently sourced build12340 constants; no game attached.");
        if (assertions!=0) throw new InvalidOperationException($"Movement flag layout regressions: assertions={assertions}; unexpected=0");
    }
}
