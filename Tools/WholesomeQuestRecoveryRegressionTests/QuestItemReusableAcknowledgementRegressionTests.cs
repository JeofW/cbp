using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

// Complete tracked owner, real TreeSharp, and actual inherited quest admission.
// Controlled world/item observations and local raw quest bytes are not a game.
internal static class QuestItemReusableAcknowledgementRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        string boundary = (string)typeof(QuestItemTargetSelectionRegressionTests).GetField("Boundary", flags)!.GetRawConstantValue()!;
        const string oldItem = "public bool TryUseContainerItem()=>true; public void UseContainerItem(){TryUseContainerItem();}";
        if (boundary.Split(new[] { oldItem }, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("Retained external item fixture changed");
        boundary = boundary.Replace(oldItem,
            "public bool TryUseContainerItem(){ReusableItemCases.Submit(this);return true;} public void UseContainerItem(){TryUseContainerItem();}");
        string temp = Path.Combine(Path.GetTempPath(), "cb-item-reusable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool oldLogging = Styx.Helpers.Logging.FileLogging;
        IDisposable? questWorld = null;
        void Observe(string state)
        {
            if (state == "reset")
            {
                questWorld?.Dispose();
                questWorld = (IDisposable)Activator.CreateInstance(
                    typeof(QuestPublicationRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            }
            if (questWorld == null) throw new InvalidOperationException("Missing actual quest observation fixture");
            Type kind = questWorld.GetType();
            uint descriptor = (uint)kind.GetField("descriptor", flags)!.GetValue(questWorld)!;
            var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)kind.GetField("cache", flags)!.GetValue(questWorld)!;
            uint id = state == "absent" ? 0U : 867U;
            uint completed = state == "complete" ? Convert.ToUInt32(Enum.Parse(
                typeof(Styx.StyxWoW).Assembly.GetTypes().Single(t => t.IsEnum && t.Name == "WoWDescriptorQuestFlags"), "Completed")) : 0U;
            Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + 632))), unchecked((int)id));
            Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + 636))), unchecked((int)completed));
            foreach (var key in cache.Value!.Keys.Where(k => unchecked((uint)k.ToInt32()) >= descriptor + 632
                && unchecked((uint)k.ToInt32()) < descriptor + 1132).ToArray()) cache.Value.Remove(key);
        }
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.Copy(Path.Combine(root, "runtime-snapshot", "Quest Behaviors", "UseItemOn.cs"), Path.Combine(temp, "UseItemOn.cs"));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), boundary + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Complete UseItemOn compilation failed: " + string.Join(";", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            try { assembly.GetType("ReusableItemCases", true)!.GetMethod("Run")!.Invoke(null, new object[] { (System.Action<string>)Observe }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { questWorld?.Dispose(); Styx.Helpers.Logging.FileLogging = oldLogging; Directory.Delete(temp, true); }
    }

    private const string Cases = """

public static class ReusableItemCases
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message){}
    private static Script owner=null!;
    private static LocalPlayer player=null!;
    private static WoWUnit target=null!;
    private static Composite? tree;
    private static bool running;
    private static System.Action<string> observe=null!;
    private static readonly List<ulong> used=new();
    public static void Submit(WoWItem item){used.Add(player.CurrentTarget?.Guid??0);}
    public static void Run(System.Action<string> observation)
    {
        observe=observation;
        var cases=new List<(string Name,System.Action Test)>();
        void Add(string name,System.Action test)=>cases.Add((name,()=>{Reset();test();}));
        Add("reusable tool and sole blacklisted recipient defer after expiry",()=>{
            SubmitOnce();Expire();Pulse();AssertDeferred();
        });
        Add("retained running acknowledgement tree releases on expiry",()=>{
            SubmitOnce();Pulse();Check(!owner.IsDone&&used.Count==1,"fresh wait changed the admitted attempt");
            Expire();for(int i=0;i<3&&!owner.IsDone;i++)Pulse();AssertDeferred();
        });
        Add("consumed tool cannot strand a retained running wait",()=>{
            SubmitOnce();Pulse();player.CarriedItems!.Clear();Expire();
            for(int i=0;i<3&&!owner.IsDone;i++)Pulse();AssertDeferred();
        });
        Add("eligible alternative can be submitted after a retained wait",()=>{
            SubmitOnce();Pulse();AddAlternative();Expire();
            for(int i=0;i<3&&used.Count<2;i++)Pulse();
            Check(used.SequenceEqual(new[]{2UL,3UL})&&Counter==2&&!owner.IsDone,
                "expired acknowledgement stranded an eligible second recipient");
        });
        Add("fresh acknowledgement does not duplicate a submission",()=>{
            SubmitOnce();AddAlternative();Pulse();Pulse();
            Check(used.Count==1&&Counter==1&&!owner.IsDone&&!Acknowledged(),"fresh wait retried or invented success");
        });
        Add("delayed actual quest completion remains authoritative",()=>{
            SubmitOnce();Pulse();observe("complete");
            Check(owner.IsDone&&Acknowledged()&&used.Count==1,"delayed quest completion was lost or duplicated");
        });
        Add("exhausted attempt cannot use an alternative after retained wait",()=>{
            Set("MaxAttempts",1);SubmitOnce();Pulse();AddAlternative();Expire();
            for(int i=0;i<3&&!owner.IsDone;i++)Pulse();AssertDeferred();
            Check(owner.AuthoritativeAttemptsExhausted,"available target bypassed the maximum-attempt deferral");
        });
        Add("initial intentional target wait is not a submitted timeout",()=>{
            ObjectManager.Objects!.Clear();Pulse();Pulse();
            Check(!owner.IsDone&&used.Count==0&&Counter==0&&!Acknowledged(),"initial target wait was reinterpreted as success or a submitted timeout");
        });
        Add("alternative before entering a wait retains bounded retry",()=>{
            SubmitOnce();AddAlternative();Expire();Pulse();
            Check(used.SequenceEqual(new[]{2UL,3UL})&&Counter==2&&!owner.IsDone,"ordinary alternative retry changed");
        });
        Add("legacy invocation completion keeps its existing contract",()=>{
            Set("SuccessEvidence",Script.SuccessEvidenceType.InvocationCount);Set("NumOfTimes",1);
            SubmitOnce();Pulse();Check(owner.IsDone&&used.Count==1&&!Acknowledged(),"legacy invocation was turned into authoritative acknowledgement");
        });
        int passed=0,assertions=0,unexpected=0;
        try
        {
            foreach(var test in cases)
            {
                try{test.Test();passed++;Console.WriteLine("PASS reusable item acknowledgement: "+test.Name);}
                catch(Failure error){assertions++;Console.Error.WriteLine("FAIL reusable item acknowledgement: "+test.Name+": "+error.Message);}
                catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR reusable item acknowledgement: "+test.Name+": "+error);}
                finally{StopTree();}
            }
        }
        finally{StopTree();}
        Console.WriteLine($"Reusable item acknowledgement scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked UseItemOn and retained TreeSharp continuation; actual inherited quest reader; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Reusable item acknowledgement regression");
    }
    private static void Reset()
    {
        StopTree();observe("reset");
        typeof(QuestItemSelectionCases).GetMethod("Reset",Hidden)!.Invoke(null,null);
        owner=(Script)typeof(QuestItemSelectionCases).GetField("owner",Hidden)!.GetValue(null)!;
        target=(WoWUnit)typeof(QuestItemSelectionCases).GetField("candidate",Hidden)!.GetValue(null)!;
        player=ObjectManager.Me!;player.IsMoving=false;
        player.CarriedItems!.Add(new WoWItem{Guid=17,Entry=12345});
        typeof(Script).GetField("_isDisposed",Hidden)!.SetValue(owner,false);GC.SuppressFinalize(owner);
        Set("QuestId",867);Set("SuccessEvidence",Script.SuccessEvidenceType.QuestComplete);
        Set("QuestRequirementInLog",Styx.Logic.Questing.CustomForcedBehavior.QuestInLogRequirement.InLog);
        Set("QuestRequirementComplete",Styx.Logic.Questing.CustomForcedBehavior.QuestCompleteRequirement.NotComplete);
        Set("NumOfTimes",3);Set("MaxAttempts",3);Set("WaitForNpcs",true);Set("WaitTime",100);
        Set("AcknowledgementTimeout",5000);Set("SubmissionRefusalTimeout",5000);
        typeof(Script).GetField("_lastSubmissionUtc",Hidden)!.SetValue(owner,-1L);
        typeof(Script).GetField("_submissionRefusalUtc",Hidden)!.SetValue(owner,-1L);
        tree=null;running=false;used.Clear();
        if(owner.IsDone)throw new InvalidOperationException("Actual inherited reader must observe accepted incomplete quest867");
    }
    private static void SubmitOnce()
    {
        Pulse();Check(used.SequenceEqual(new[]{2UL})&&Counter==1&&!owner.IsDone,
            "complete owner did not submit exactly once before the acknowledgement scenario");
    }
    private static void AddAlternative()=>ObjectManager.Objects!.Add(
        new WoWUnit{Guid=3,Entry=70001,Location=new WoWPoint(13,10,10)});
    private static void Expire()=>typeof(Script).GetField("_lastSubmissionUtc",Hidden)!.SetValue(owner,
        DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond-5001L);
    private static int Counter=>(int)typeof(Script).GetProperty("Counter",Hidden)!.GetValue(owner)!;
    private static bool Acknowledged()=>(bool)typeof(Script).GetMethod("HasAuthoritativeSuccess",Hidden)!.Invoke(owner,null)!;
    private static void AssertDeferred()=>Check(owner.IsDone&&used.Count==1&&Counter==1&&!Acknowledged(),
        "unacknowledged attempt did not defer, submitted twice, or claimed quest success");
    private static void Set(string name,object value)=>typeof(Script).GetProperty(name,Hidden)!.SetValue(owner,value);
    private static void Pulse()
    {
        var errors=new List<string>();
        void Record(Styx.Helpers.LogLevel level,string message)
        {if(message.Contains("Exception")||message.Contains("Object reference not set"))errors.Add(message);}
        Styx.Helpers.Logging.OnMessageLogged+=Record;
        try
        {
            tree??=(Composite)typeof(Script).GetMethod("CreateBehavior",Hidden)!.Invoke(owner,null)!;
            if(!running)tree.Start(null!);
            running=tree.Tick(null!)==RunStatus.Running;
            if(!running)tree.Stop(null!);
            Check(errors.Count==0,"swallowed owner exception: "+string.Join(";",errors));
        }
        finally{Styx.Helpers.Logging.OnMessageLogged-=Record;}
    }
    private static void StopTree(){if(running&&tree!=null)tree.Stop(null!);running=false;tree=null;}
    private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
}
""";
}
