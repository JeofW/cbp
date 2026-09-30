using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Actual allocated player memory and stock Lua5.1 executing the production
// request. Only externally supplied API observations are controlled.
internal static class QuestSpellKnowledgeSnapshotRegressionTests
{
    private const BindingFlags H=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private sealed class Failure(string message):Exception(message){}
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string Name,Action<Fixture> Body)>();
        void Case(string name,Action<Fixture> body)=>tests.Add((name,body));
        Case("positive self API result confirms only the requested spell",f=>{
            var sample=f.Capture(new[]{54197});Check(Ids(sample)!.SequenceEqual(new[]{54197U}),"positive ID not retained");});
        foreach(string mode in new[]{"false","nil","number","string","missing-api","wrong-owner","changed-owner","pet-only","error"})
        {
            string observation=mode;Case("unproven spell result="+mode,f=>{
                f.Mode=observation;Check(Ids(f.Capture(new[]{54197}))?.Count is null or 0,"unproven API result became positive");});
        }
        Case("false result never creates negative knowledge",f=>{
            f.Mode="false";var sample=f.Capture(new[]{54197});var method=sample.GetType().GetMethod("Confirms")!;
            Check(Invoke(method,sample,54197U)==null,"client absence became known false");});
        Case("separate requested spell identities stay independent",f=>{
            var sample=f.Capture(new[]{54197,123});Check(Ids(sample)!.SequenceEqual(new[]{54197U}),"other spell inherited membership");});
        Case("duplicate input requests are bounded and deduplicated",f=>{
            var sample=f.Capture(new[]{54197,54197});Check(Ids(sample)!.SequenceEqual(new[]{54197U}),"duplicate request changed receipt");});
        foreach(string fault in new[]{"no-values","extra-value","unmarked","wrong-guid","duplicate","unrequested","zero","overflow","negative","too-long"})
        {
            string captured=fault;Case("malformed receipt="+fault,f=>{
                string prefix="KS1|0x"+f.Player.Guid.ToString("X16")+"|";
                f.Transport=_=>captured switch {
                    "no-values"=>new(),"extra-value"=>new(){prefix+"54197","extra"},"unmarked"=>new(){"54197"},
                    "wrong-guid"=>new(){"KS1|0x0000000000000999|54197"},"duplicate"=>new(){prefix+"54197,54197"},
                    "unrequested"=>new(){prefix+"123"},"zero"=>new(){prefix+"0"},"overflow"=>new(){prefix+"4294967296"},
                    "negative"=>new(){prefix+"-1"},_=>new(){prefix+new string('1',4096)}};
                Check(Ids(f.Capture(new[]{54197}))==null,"malformed receipt supplied usable knowledge");});
        }
        foreach(int[] requested in new[]{Array.Empty<int>(),new[]{0},new[]{-1},Enumerable.Range(1,65).ToArray()})
        {
            int[] input=requested;Case("invalid or excessive request="+string.Join(',',input),f=>{
                f.Transport=_=>throw new Failure("invalid request reached Lua");
                Check(Ids(f.Capture(input))==null,"invalid request became knowledge");});
        }
        Case("changed managed player during Lua revokes the sample",f=>{
            f.AfterExecute=()=>ObjectManager.Me=new LocalPlayer(f.Player.BaseAddress);Check(Ids(f.Capture(new[]{54197}))==null,"old player query was relabelled");});
        Case("changed memory wrapper during Lua revokes the sample",f=>{
            f.AfterExecute=()=>typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,
                typeof(object).GetMethod("MemberwiseClone",H)!.Invoke(f.Memory,null));
            Check(Ids(f.Capture(new[]{54197}))==null,"foreign memory query was relabelled");});
        Case("changed raw player GUID during Lua revokes the sample",f=>{
            f.AfterExecute=()=>Marshal.WriteInt64(new IntPtr(unchecked((int)f.Player.BaseAddress+48)),999);
            Check(Ids(f.Capture(new[]{54197}))==null,"changed raw owner retained knowledge");});
        Case("short or unreadable descriptor never grants knowledge",f=>{
            Marshal.WriteInt32(new IntPtr(unchecked((int)f.Player.BaseAddress+8)),1);
            Check(Ids(f.Capture(new[]{54197}))==null,"unreadable descriptor was accepted");});
        Case("ordinary transport errors remain unknown",f=>{f.Transport=_=>throw new IOException("controlled");Check(Ids(f.Capture(new[]{54197}))==null,"error authorized knowledge");});
        foreach(Exception error in new Exception[]{new OperationCanceledException("stop"),new ThreadInterruptedException("stop")})
        {
            Exception original=error;Case("cancellation propagates="+error.GetType().Name,f=>{
                f.Transport=_=>throw original;try{f.Capture(new[]{54197});throw new Failure("cancellation swallowed");}
                catch(Exception actual)when(ReferenceEquals(actual,original)){} });
        }
        Case("cache policy is restored",f=>{
            var enabled=(ThreadLocal<bool>)typeof(Memory).GetField("_cacheEnabled",H)!.GetValue(f.Memory)!;
            bool before=enabled.Value;f.Capture(new[]{54197});Check(enabled.Value==before,"cache policy leaked");});
        Case("fresh positive read recovers after unavailable observation",f=>{
            f.Mode="missing-api";Check(Ids(f.Capture(new[]{54197}))?.Count is null or 0,"missing API granted knowledge");
            f.Mode="positive";Check(Ids(f.Capture(new[]{54197}))!.Contains(54197U),"positive retry did not recover");});
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {
            try{using var f=new Fixture();test.Body(f);passed++;Console.WriteLine("PASS spell snapshot: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL spell snapshot: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR spell snapshot: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Spell knowledge scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual player memory and generated Lua5.1; positive evidence only, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Spell knowledge snapshot regression");
    }

    internal sealed class Fixture:IDisposable
    {
        internal readonly QuestDatasetObservationFixture Native=new();
        private RewardLua51Boundary.StockLua51? _lua;
        internal LocalPlayer Player=>Native.Player;
        internal Memory Memory=>ObjectManager.Wow;
        internal string Mode="positive";
        internal Func<string,List<string>>? Transport;
        internal Action? AfterExecute;
        internal object Capture(int[] requested)
        {
            Type? owner=typeof(LocalPlayer).Assembly.GetType("Styx.Logic.Questing.QuestSpellKnowledgeSnapshot");
            MethodInfo? method=owner?.GetMethod("CaptureCore",H);Check(method!=null,"positive owned spell observer missing");
            Func<string,List<string>> execute=script=>{
                List<string> result;
                if(Transport!=null)result=Transport(script);
                else
                {
                    _lua??=new RewardLua51Boundary.StockLua51(Root());
                    string setup="clicks=0; local guid='0x"+Player.Guid.ToString("X16")+"'; "+
                        "function UnitGUID(unit) return scenario=='wrong-owner' and '0x0000000000000999' or guid end "+
                        "function IsSpellKnown(id,pet) if pet~=false then error('pet fallback forbidden') end "+
                        "if scenario=='changed-owner' then guid='0x0000000000000999' end "+
                        "if scenario=='error' then error('controlled') end "+
                        "if scenario=='false' or scenario=='pet-only' then return false end "+
                        "if scenario=='nil' then return nil end if scenario=='number' then return 1 end "+
                        "if scenario=='string' then return 'true' end return id==54197 end "+
                        "if scenario=='missing-api' then IsSpellKnown=nil end";
                    var observed=_lua.Execute(script,(uint)Encoding.UTF8.GetByteCount(script),Mode,new[]{"",""},setup);
                    Check(observed.Load==0,"production request did not compile in stock Lua5.1");
                    Check(Mode=="error" ? observed.Call!=0 : observed.Call==0,"unexpected generated Lua result: "+observed.Error);
                    Check(observed.Clicks==0,"knowledge query performed a gameplay action");
                    result=observed.Values;
                }
                AfterExecute?.Invoke();return result;
            };
            return Invoke(method!,null,Player,Memory,requested,execute)!;
        }
        public void Dispose(){_lua?.Dispose();Native.Dispose();}
    }
    internal static IReadOnlyCollection<uint>? Ids(object snapshot)=>(IReadOnlyCollection<uint>?)snapshot.GetType().GetProperty("ConfirmedSpellIds")!.GetValue(snapshot);
    internal static object? Invoke(MethodInfo method,object? instance,params object?[] args)
    {try{return method.Invoke(instance,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static string Root(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p!=null && !File.Exists(Path.Combine(p.FullName,"CopilotBuddy.csproj")))p=p.Parent;return p?.FullName??throw new InvalidOperationException("Current source root missing");}
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
