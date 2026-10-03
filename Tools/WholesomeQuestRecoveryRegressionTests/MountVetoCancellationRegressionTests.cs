using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.Logic;
using Styx.Logic.Pathing;

internal static class MountVetoCancellationRegressionTests
{
    [ModuleInitializer] internal static void Run()
    {
        var eventInfo=typeof(Mount).GetEvent("OnMountUp")!;
        var method=typeof(Mount).GetMethod("AllowMountAttempt",BindingFlags.NonPublic|BindingFlags.Static)!;
        int count=0;
        foreach(Exception signal in new Exception[]{new OperationCanceledException("cancel"),new ThreadInterruptedException("stop")})
        {
            _signal=signal;
            var handler=Delegate.CreateDelegate(eventInfo.EventHandlerType!,typeof(MountVetoCancellationRegressionTests).GetMethod(nameof(Throw),BindingFlags.NonPublic|BindingFlags.Static)!);
            eventInfo.AddEventHandler(null,handler);
            Exception? caught=null;
            try{method.Invoke(null,new object[]{false,"Selected mount",new WoWPoint(100,100,100)});}
            catch(TargetInvocationException error){caught=error.InnerException;}
            finally{eventInfo.RemoveEventHandler(null,handler);_signal=null;}
            if(!ReferenceEquals(caught,signal))throw new InvalidOperationException("mount veto swallowed "+signal.GetType().Name);
            count++;
        }
        Console.WriteLine($"Mount veto control flow: {count}/2; actual event dispatch; no client action.");
    }
    private static Exception? _signal;
    private static void Throw(object? sender,EventArgs args)=>throw _signal!;
}
