using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Tripper.Navigation;
using NativeNavigator = Tripper.Navigation.Navigator;

if (Styx.WoWInternals.ObjectManager.Me != null || Styx.WoWInternals.ObjectManager.Wow != null)
    throw new InvalidOperationException("Native contract tests must not attach to a game.");
Console.WriteLine("Native DLL SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Navigation.dll")))).ToLowerInvariant());
using var navigator=new NativeNavigator();
if(!navigator.LoadMeshes()) throw new InvalidOperationException("The actual native library must load before these contract checks.");
var tests=new (string Name, Action Run)[]
{
    ("actual missing-map failure is initialization, not endpoint lookup", () =>
    {
        var result=Missing();
        Check(!result.Succeeded && result.Points.Length==0,"missing-map control must really fail");
        Check(result.FailStep==PathFindStep.InitPathFind,$"native code 2 is InitPathFind; observed {result.FailStep}");
        Check(result.Start==Vector3.Zero && result.End==Vector3.One,"failed query must retain requested endpoints");
    }),
    ("native raw diagnostic survives managed translation", () =>
    {
        var result=Missing();
        var property=typeof(PathFindResult).GetProperty("RawNativeFailStep");
        Check(property!=null && (int?)property.GetValue(result)==2,"preserve actual native code 2 for source/ABI diagnosis");
    }),
    ("all native step values map without renumbering the public enum", () =>
    {
        var decode=typeof(NativeNavigator).GetMethod("DecodeNativePathFindStep",BindingFlags.Static|BindingFlags.NonPublic);
        var pairs=new (int Raw,PathFindStep Expected)[]{(-1,PathFindStep.None),(0,PathFindStep.FindStartPoly),(1,PathFindStep.FindEndPoly),(2,PathFindStep.InitPathFind),(3,PathFindStep.UpdatePathFind),(4,PathFindStep.FinalizePathFind),(5,PathFindStep.FindStraightPath)};
        foreach(var pair in pairs)
        {
            // Baseline uses this direct cast at the native-result boundary.
            var actual=decode==null?(PathFindStep)pair.Raw:(PathFindStep)decode.Invoke(null,new object[]{pair.Raw})!;
            Check(actual==pair.Expected,$"raw={pair.Raw}; expected={pair.Expected}; actual={actual}");
        }
        Check((int)PathFindStep.None==0 && (int)PathFindStep.FindStartPoly==1 && (int)PathFindStep.FindEndPoly==2
            && (int)PathFindStep.InitPathFind==3 && (int)PathFindStep.UpdatePathFind==4 && (int)PathFindStep.FinalizePathFind==5
            && (int)PathFindStep.SnapPartialPathToEnd==6 && (int)PathFindStep.FindStraightPath==7,"public serialized values must remain unchanged");
    }),
    ("unknown native codes never impersonate an existing public step", () =>
    {
        var decode=typeof(NativeNavigator).GetMethod("DecodeNativePathFindStep",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(int raw in new[]{6,7,12345,int.MinValue})
        {
            var actual=decode==null?(PathFindStep)raw:(PathFindStep)decode.Invoke(null,new object[]{raw})!;
            Check(actual.ToString()=="Unknown","future native code must be explicitly unknown, not a valid unrelated enum member");
        }
    }),
    ("caller timing includes controlled lock contention", () =>
    {
        using var entered=new ManualResetEventSlim();
        Task<PathFindResult> worker;
        lock(navigator.MeshLock)
        {
            worker=Task.Run(()=> { entered.Set(); return Missing(); });
            Check(entered.Wait(TimeSpan.FromSeconds(5)),"query worker did not start");
            // Test-only contention. No sleep is added to production navigation.
            Thread.Sleep(250);
        }
        Check(worker.Wait(TimeSpan.FromSeconds(5)),"missing-map query failed to return after releasing the lock");
        var result=worker.Result;
        TimeSpan total=Duration(result,"CallerElapsed",result.Elapsed);
        TimeSpan wait=Duration(result,"LockWaitElapsed",TimeSpan.Zero);
        Console.WriteLine($"Controlled contention: legacy={result.Elapsed.TotalMilliseconds:F3}ms total={total.TotalMilliseconds:F3}ms wait={wait.TotalMilliseconds:F3}ms");
        Check(wait>=TimeSpan.FromMilliseconds(150) && total>=wait,"query timing must include actual wait, not start after acquiring the lock");
        ValidateTimings(result);
    }),
    ("failed and unloaded paths retain coherent nonnegative timing", () =>
    {
        ValidateTimings(Missing());
        using var unloaded=new NativeNavigator();
        var result=unloaded.FindPath(999999,Vector3.Zero,Vector3.One);
        ValidateTimings(result);
        Check(typeof(PathFindResult).GetProperty("RawNativeFailStep")?.GetValue(result)==null,"managed precondition failures have no native code");
    })
};
int failed=0;
foreach(var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS native contract: "+test.Name); }
    catch(Exception error) { failed++; Console.Error.WriteLine("FAIL native contract: "+test.Name+": "+error.Message); }
}
Console.WriteLine($"Native contract scenarios: {tests.Length-failed}/{tests.Length}; actual x86 library; deliberately missing map; no mesh download or game attached.");
return failed==0?0:1;

PathFindResult Missing()=>navigator.FindPath(999999,Vector3.Zero,Vector3.One);
static TimeSpan Duration(PathFindResult result,string name,TimeSpan fallback) =>
    typeof(PathFindResult).GetProperty(name)?.GetValue(result) is TimeSpan value?value:fallback;
static void ValidateTimings(PathFindResult result)
{
    string[] names={"CallerElapsed","LockWaitElapsed","NativeCallElapsed","ManagedAndCleanupElapsed"};
    var values=names.Select(name => typeof(PathFindResult).GetProperty(name)?.GetValue(result)).ToArray();
    Check(values.All(value=>value is TimeSpan span && span>=TimeSpan.Zero),"every returned outcome needs explicit nonnegative phase timings");
    var total=(TimeSpan)values[0]!; var phases=(TimeSpan)values[1]!+(TimeSpan)values[2]!+(TimeSpan)values[3]!;
    Check(Math.Abs((total-phases).TotalMilliseconds)<0.01,"phase timings must reconcile to caller-visible total");
}
static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
