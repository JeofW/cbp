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
using Styx.Logic.Questing;

// Complete tracked owners; the quest descriptor reader and behavior base are real.
// World/UI observations are controlled. This does not execute client Lua or prove
// that an identical NPC/menu was not replaced between native client frames.
internal static class GossipEventLifetimeRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string boundary = (string)typeof(QuestItemTargetSelectionRegressionTests)
            .GetField("Boundary", Hidden)!.GetRawConstantValue()!;
        void Replace(string before, string after)
        {
            if (boundary.Split(new[] { before }, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("Controlled boundary changed: " + before);
            boundary = boundary.Replace(before, after);
        }
        Replace("public static void SleepForLagDuration(){}", "public static bool IsInGame{get;set;}=true;public static void SleepForLagDuration(){}");
        Replace("public bool IsAlive{get;set;}=true;", "public bool CanSelect=>true;public void Interact(){GossipLifetimeCases.Interactions++;}public bool IsAlive{get;set;}=true;");
        Replace("public Styx.Logic.Questing.PlayerQuest? GetQuestById(uint id)=>null;", "public Styx.Logic.Questing.PlayerQuest? GetQuestById(uint id)=>GossipLifetimeCases.FindQuest(id);");

        string temp = Path.Combine(Path.GetTempPath(), "cb-gossip-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool priorLogging = Styx.Helpers.Logging.FileLogging;
        IDisposable? fixture = null;
        PlayerQuest? observedQuest = null;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            foreach (string name in new[] { "UseItemOn", "GossipEvent" })
                File.Copy(Path.Combine(root, "runtime-snapshot", "Quest Behaviors", name + ".cs"), Path.Combine(temp, name + ".cs"));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), boundary + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked gossip compile failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;

            void Configure()
            {
                fixture?.Dispose();
                fixture = null;
                observedQuest = null;
                fixture = (IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests)
                    .GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
                uint descriptor = (uint)fixture.GetType().GetField("descriptor", Hidden)!.GetValue(fixture)!;
                var cache = (ThreadLocal<Dictionary<IntPtr, byte[]>>)fixture.GetType().GetField("cache", Hidden)!.GetValue(fixture)!;
                observedQuest = Styx.StyxWoW.Me.QuestLog.GetQuestById(867U)
                    ?? throw new InvalidOperationException("Actual fixture quest867 missing");
                object metadata = typeof(Quest).GetProperty("InternalInfo")!.GetValue(observedQuest)!;
                metadata.GetType().GetField("ObjectiveId")!.SetValue(metadata, new[] { 70001, 0, 0, 0 });
                metadata.GetType().GetField("ObjectiveRequiredCount")!.SetValue(metadata, new[] { 4, 0, 0, 0 });
                typeof(Quest).GetProperty("InternalInfo")!.SetValue(observedQuest, metadata);
                void Write(uint offset, uint value) => Marshal.WriteInt32(new IntPtr(unchecked((int)(descriptor + offset))), unchecked((int)value));
                Write(632, 867U); Write(636, 0U); Write(640, 0U); Write(644, 0U);
                foreach (IntPtr key in cache.Value!.Keys.Where(k => unchecked((uint)k.ToInt32()) >= descriptor + 632U
                    && unchecked((uint)k.ToInt32()) < descriptor + 1132U).ToArray()) cache.Value.Remove(key);
                if (!observedQuest.GetData(out QuestDescriptorData data) || data.Id != 867U || data.ObjectivesDone[0] != 0)
                    throw new InvalidOperationException("Fixture did not reach actual incomplete quest reader");
            }
            Func<uint, PlayerQuest?> find = id => id == 867U ? observedQuest : null;
            try
            {
                assembly.GetType("GossipLifetimeCases", true)!.GetMethod("Run")!.Invoke(null,
                    new object[] { find, (Action)Configure });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally
        {
            fixture?.Dispose();
            Styx.Helpers.Logging.FileLogging = priorLogging;
            Directory.Delete(temp, true);
        }
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Cases = """
public static class GossipLifetimeCases
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message){}
    public static System.Func<uint,Styx.Logic.Questing.PlayerQuest?> FindQuest=null!;
    private static System.Action configure=null!;
    public static System.Action? OnMenu;
    public static int Selections,Closes,Interactions;
    public static ulong CurrentNpc;
    private static object owner=null!;
    private static Type kind=typeof(Styx.Bot.Quest_Behaviors.GossipEvent.GossipEvent);
    private static LocalPlayer player=null!;
    private static Styx.Logic.Inventory.Frames.Gossip.GossipFrame Frame=>Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance;
    public static void Run(System.Func<uint,Styx.Logic.Questing.PlayerQuest?> find,System.Action config)
    {
        FindQuest=find;configure=config;
        var tests=new List<(string Name,System.Action Test)>();
        void Add(string name,System.Action test)=>tests.Add((name,()=>{Reset();test();}));
        Add("unchanged observed NPC menu remains selectable",()=>{Open();Tick();Check(Selections==1,"valid menu was not selected");});
        Add("NPC replacement while reading options blocks selection",()=>{Open();OnMenu=()=>CurrentNpc=3;Tick();Check(OnMenu==null,"menu boundary not reached");Check(Selections==0,"selected a foreign NPC menu");});
        Add("disposal while reading options blocks selection",()=>{Open();OnMenu=()=>Invoke("Dispose");Tick();Check(Selections==0,"disposed owner selected an option");});
        Add("player object replacement while reading options blocks selection",()=>{Open();OnMenu=()=>ObjectManager.Me=new LocalPlayer{Guid=player.Guid,Location=player.Location};Tick();Check(Selections==0,"replaced actor selected an option");});
        Add("player GUID replacement while reading options blocks selection",()=>{Open();OnMenu=()=>player.Guid=99;Tick();Check(Selections==0,"changed actor identity selected an option");});
        Add("death while reading options blocks selection",()=>{Open();OnMenu=()=>player.IsAlive=false;Tick();Check(Selections==0,"dead actor selected an option");});
        Add("world loss while reading options blocks selection",()=>{Open();OnMenu=()=>Styx.StyxWoW.IsInGame=false;Tick();Check(Selections==0,"world loss did not revoke selection");});
        Add("an old disposed tree cannot select or interact",()=>{Open();Invoke("Dispose");Tick();Check(Selections==0&&Interactions==0,"old tree continued after disposal");});
        Add("deferral does not close a foreign NPC menu",()=>{Open();CurrentNpc=3;Invoke("DeferAuthoritativeAttempt","controlled");Check(Closes==0,"deferral closed a foreign menu");});
        Add("deferral can close its own still-current NPC menu",()=>{Open();Invoke("DeferAuthoritativeAttempt","controlled");Check(Closes==1,"owned cleanup was lost");});
        Add("retry reset does not close a foreign NPC menu",()=>{Open();CurrentNpc=3;Invoke("ResetForRetry");Check(Closes==0,"retry reset closed foreign menu");});
        Add("retry reset closes its own menu before discarding identity",()=>{Open();Invoke("ResetForRetry");Check(Closes==1,"reset lost cleanup identity before close");});
        Add("a visible foreign menu is not stolen to start an interaction",()=>{Frame.IsVisible=true;CurrentNpc=3;Tick();Check(Closes==0&&Interactions==0,"new interaction stole a pre-existing menu");});
        Add("actor replacement before pending tick blocks mutation",()=>{Open();ObjectManager.Me=new LocalPlayer{Guid=player.Guid,Location=player.Location};Tick();Check(Selections==0&&Closes==0&&Interactions==0,"pending tick used a replaced actor");});
        Add("cleanup with no owned interaction preserves visible UI",()=>{Frame.IsVisible=true;CurrentNpc=2;Invoke("ResetForRetry");Check(Closes==0,"unowned reset closed visible UI");});
        Add("disposed owner cannot perform later deferral cleanup",()=>{Open();Invoke("Dispose");Invoke("DeferAuthoritativeAttempt","controlled");Check(Closes==0,"disposed callback closed UI");});
        Add("cleanup with no visible menu remains a no-op",()=>{Open();Frame.IsVisible=false;Invoke("ResetForRetry");Check(Closes==0,"cleanup mutated hidden UI");});
        int pass=0,assertions=0,unexpected=0;
        foreach(var test in tests)
        {
            try{test.Test();pass++;Console.WriteLine("PASS gossip lifetime: "+test.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL gossip lifetime: "+test.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR gossip lifetime: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Gossip lifetime scenarios: {pass}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked owner and real quest reader; controlled actor/UI; no Lua or game execution.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Gossip lifetime regression");
    }
    private static void Reset()
    {
        configure();Styx.StyxWoW.IsInGame=true;
        typeof(QuestItemSelectionCases).GetMethod("Reset",Hidden)!.Invoke(null,null);
        player=ObjectManager.Me!;player.IsMoving=false;player.CurrentTarget=ObjectManager.Objects!.OfType<WoWUnit>().Single();
        owner=Activator.CreateInstance(kind,new object[]{new Dictionary<string,string>{
            ["QuestId"]="867",["ObjectiveIndex"]="0",["SuccessEvidence"]="ObjectiveProgress",
            ["MobId"]="70001",["CollectionDistance"]="100",["Range"]="4",["MaxAttempts"]="3",
            ["AcknowledgementTimeout"]="5000",["GossipOptionIndex"]="0",["X"]="10",["Y"]="10",["Z"]="10"}})!;
        GC.SuppressFinalize(owner);
        if(((Styx.Logic.Questing.CustomForcedBehavior)owner).IsAttributeProblem)throw new InvalidOperationException("Actual constructor rejected controlled arguments");
        kind.GetProperty("Location",Hidden)!.SetValue(owner,player.Location);
        Selections=Closes=Interactions=0;OnMenu=null;CurrentNpc=2;Frame.IsVisible=false;
        Invoke("OnStart");
        Check(!(bool)kind.GetProperty("IsDone")!.GetValue(owner)!,"fresh incomplete owner unexpectedly done");
    }
    private static void Open()
    {
        Frame.IsVisible=true;
        kind.GetField("_gossipOpenStartedUtc",Hidden)!.SetValue(owner,DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond);
        kind.GetField("_interactionGuid",Hidden)!.SetValue(owner,2UL);
    }
    private static object? Invoke(string name,params object[] args)
    {
        try{return kind.GetMethod(name,Hidden)!.Invoke(owner,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static void Tick()=>Invoke("TickBehavior");
    private static void Check(bool condition,string reason){if(!condition)throw new Failure(reason);}
} namespace Styx.Logic.Inventory.Frames.Gossip
{
    public sealed class GossipFrame
    {
        public static GossipFrame Instance{get;}=new();public bool IsVisible{get;set;}
        public List<int> GossipOptionEntries{get{var f=GossipLifetimeCases.OnMenu;GossipLifetimeCases.OnMenu=null;f?.Invoke();return new(){1};}}
        public void SelectGossipOption(int index){GossipLifetimeCases.Selections++;}
        public void Close(){if(IsVisible)GossipLifetimeCases.Closes++;IsVisible=false;}
    }
} namespace Styx.WoWInternals
{
    public static class Lua
    {
        public static T GetReturnVal<T>(string script,uint index)
        {
            if(typeof(T)!=typeof(bool)||index!=0)throw new InvalidOperationException("Unexpected return type/index");
            if(script=="return UnitGUID('npc') == '0x0000000000000002'")return (T)(object)(GossipLifetimeCases.CurrentNpc==2);
            // An ownership-guarded close request remains a controlled boundary,
            // not evidence that this fixture executes a Lua interpreter.
            if(script.Contains("CloseGossip()",StringComparison.Ordinal)
                &&script.Contains("UnitGUID('npc')",StringComparison.Ordinal)
                &&script.Contains("0x0000000000000002",StringComparison.Ordinal))
            {
                var frame=Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance;
                bool own=frame.IsVisible&&GossipLifetimeCases.CurrentNpc==2;
                if(own)frame.Close();return (T)(object)own;
            }
            throw new InvalidOperationException("Unexpected Lua observation: "+script);
        }
    }
}
""";
}
