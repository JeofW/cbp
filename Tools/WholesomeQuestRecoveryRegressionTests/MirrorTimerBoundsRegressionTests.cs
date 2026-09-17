using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Read the actual timer property against a controlled clock cache in the
// existing test-process world. No live client offset or oxygen escape proof.
internal static class MirrorTimerBoundsRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new (string Name,int Initial,int Maximum,int Rate,uint Elapsed,bool Paused,uint Expected)[] {
            ("ordinary countdown",3000,10000,-1,1000,false,2000),
            ("exactly exhausted",3000,10000,-1,3000,false,0),
            ("exhausted cannot wrap",3000,10000,-1,4000,false,0),
            ("refill saturates maximum",3000,4000,10,500,false,4000),
            ("paused drain holds observation",3000,10000,-1,1000,true,3000),
            ("paused refill holds observation",3000,10000,10,1000,true,3000),
            ("zero rate retains value",3000,10000,0,1000,false,3000),
            ("negative initial is empty",-50,10000,0,1000,false,0),
            ("initial over maximum is bounded",12000,10000,0,1000,false,10000),
            ("zero maximum is unavailable",3000,0,-1,1000,false,0),
            ("negative maximum is unavailable",3000,-1,-1,1000,false,0),
            ("long elapsed drain cannot wrap",3000,10000,-1,uint.MaxValue-1,false,0),
            ("large refill cannot overflow",3000,10000,int.MaxValue,uint.MaxValue-1,false,10000),
            ("counter wrap preserves short elapsed",3000,10000,-1,20,false,2980)
        };
        int passed=0,assertions=0,unexpected=0;
        using var world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
        var cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)ObjectManager.Wow!.GetType().GetField("_cache",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(ObjectManager.Wow)!;
        foreach(var c in cases)
        {
            try
            {
                const uint now=5;
                cache.Value![new IntPtr(0x0086AE20)]=BitConverter.GetBytes(now);
                object boxed=new MirrorTimerInfo { InitialValue=c.Initial,MaxValue=c.Maximum,ChangePerMillisecond=c.Rate,StartTime=unchecked(now-c.Elapsed) };
                typeof(MirrorTimerInfo).GetField("_paused",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(boxed,c.Paused?1u:0u);
                uint actual=((MirrorTimerInfo)boxed).CurrentTime;
                if(actual!=c.Expected) { assertions++; Console.Error.WriteLine($"FAIL mirror timer assertion: {c.Name}: expected={c.Expected},actual={actual}"); }
                else { passed++; Console.WriteLine("PASS mirror timer: "+c.Name); }
            }
            catch(Exception e) { unexpected++;Console.Error.WriteLine("ERROR mirror timer fixture: "+c.Name+": "+e); }
        }
        Console.WriteLine($"Mirror timer bounds scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual timer and controlled clock; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Mirror timer regressions: assertions={assertions}; unexpected={unexpected}");
    }
}
