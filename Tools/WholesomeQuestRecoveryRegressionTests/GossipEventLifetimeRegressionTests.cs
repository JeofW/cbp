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
using System.Xml.Linq;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

// Complete tracked owners; the quest descriptor reader and behavior base are real.
// World/UI observations are controlled. This does not execute client Lua or prove
// that an identical NPC/menu was not replaced between native client frames.
// The W88 cases retain the 17 W81 cases and add same-NPC menu-content races.
// Generated Lua is recorded for separate interpreter verification, not executed here.
// W90 additionally feeds actual loaded scheduler/CodeNode arguments into complete
// owners through the retained controlled actor/item/menu boundary. No owner
// attributes or production source are rewritten to create a successful request.
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
        Replace("public bool IsAlive{get;set;}=true;", "public bool CanSelect=>true;public void Interact(){GossipLifetimeCases.Interactions++;GossipLifetimeCases.AfterInteract?.Invoke(this);}public bool IsAlive{get;set;}=true;");
        Replace("public Styx.Logic.Questing.PlayerQuest? GetQuestById(uint id)=>null;", "public Styx.Logic.Questing.PlayerQuest? GetQuestById(uint id)=>GossipLifetimeCases.FindQuest(id);");
        Replace("public bool TryUseContainerItem()=>true;",
            "public bool TryUseContainerItem()=>GossipLifetimeCases.SubmitItem(this);");
        Replace("public void Target(){ObjectManager.Me!.CurrentTarget=this;}",
            "public void Target(){ObjectManager.Me!.CurrentTarget=this;GossipLifetimeCases.AfterTarget?.Invoke();}");

        string temp = Path.Combine(Path.GetTempPath(), "cb-gossip-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool priorLogging = Styx.Helpers.Logging.FileLogging;
        var createdBehaviorFiles = new List<string>();
        IDisposable? fixture = null;
        PlayerQuest? observedQuest = null;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            string behaviorDirectory = Path.Combine(Styx.Helpers.Logging.ApplicationPath, "Quest Behaviors");
            Directory.CreateDirectory(behaviorDirectory);
            foreach (string name in new[] { "UseItemOn", "GossipEvent" })
            {
                string source = Path.Combine(root, "runtime-snapshot", "Quest Behaviors", name + ".cs");
                File.Copy(source, Path.Combine(temp, name + ".cs"));
                string runtimeFile = Path.Combine(behaviorDirectory, name + ".cs");
                if (File.Exists(runtimeFile))
                {
                    if (!File.ReadAllBytes(runtimeFile).SequenceEqual(File.ReadAllBytes(source)))
                        throw new InvalidOperationException("Different runtime behavior must not be overwritten: " + runtimeFile);
                }
                else
                {
                    File.Copy(source, runtimeFile);
                    createdBehaviorFiles.Add(runtimeFile);
                }
            }
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), boundary + Cases +
                "public static class Logging {public static void WriteDebug(string format,params object[] args){}}\n");
            RewardLua51Boundary.WriteManagedBridge(temp, File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs")));
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
                GossipLua51Boundary.Run(assembly, root);
                fixture?.Dispose();
                fixture = null;
                observedQuest = null;
                RunGeneratedDispatch(assembly);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally
        {
            fixture?.Dispose();
            Styx.Helpers.Logging.FileLogging = priorLogging;
            foreach (string path in createdBehaviorFiles) File.Delete(path);
            Directory.Delete(temp, true);
        }
    }


    private static void RunGeneratedDispatch(Assembly assembly)
    {
        Type fixtureType = typeof(QuestStrategySchedulerRegressionTests)
            .GetNestedType("Case", BindingFlags.NonPublic)!;
        ConstructorInfo constructor = fixtureType.GetConstructors(Hidden).Single();
        MethodInfo run = assembly.GetType("GossipLifetimeCases", true)!.GetMethod("RunGenerated")!;
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (string behavior in new[] { "UseItemOn", "GossipEvent" })
        foreach (int index in new[] { 0, 3, 17 })
        foreach (string scenario in new[] { "acknowledgement", "final-ready", "wrong-recipient" })
        {
            string name = behavior + " dataset=" + index + " " + scenario;
            try
            {
                object?[] inputs = constructor.GetParameters().Select(p => p.DefaultValue).ToArray();
                inputs[0] = behavior;
                inputs[1] = index;
                using var fixture = (IDisposable)constructor.Invoke(inputs);
                fixtureType.GetMethod("Scan", Hidden)!.Invoke(fixture, null);
                var xml = (XDocument)fixtureType.GetMethod("Xml", Hidden)!.Invoke(fixture, null)!;
                var node = (CodeNode)CodeNode.FromXml(xml.Descendants("CustomBehavior").Single());
                if (node.Path != behavior || node.Arguments["ObjectiveIndex"] != index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ||
                    node.Arguments["SuccessEvidence"] != "QuestComplete")
                    throw new InvalidOperationException("Generated identity/whole-quest contract was not retained");
                var player = (Styx.WoWInternals.WoWObjects.LocalPlayer)fixtureType
                    .GetProperty("Player", Hidden)!.GetValue(fixture)!;
                uint descriptor = (uint)fixtureType.GetProperty("Descriptor", Hidden)!.GetValue(fixture)!;
                MethodInfo write = fixtureType.GetMethod("Write", Hidden)!;
                void Observe(string state)
                {
                    if (state == "complete")
                        write.Invoke(fixture, new object[] { descriptor + 636U,
                            (uint)WoWDescriptorQuestFlags.Completed });
                    else if (state == "packed")
                    {
                        write.Invoke(fixture, new object[] { descriptor + 640U, 0x00090009U });
                        write.Invoke(fixture, new object[] { descriptor + 644U, 0x00090009U });
                    }
                    else throw new InvalidOperationException("Unknown controlled observation: " + state);
                }
                Func<uint, PlayerQuest?> find = id => player.QuestLog.GetQuestById(id);
                run.Invoke(null, new object[] { behavior, node.Arguments, scenario, find,
                    (Action<string>)Observe, player.Guid });
                passed++;
                Console.WriteLine("PASS generated recipient dispatch: " + name);
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException target && target.InnerException != null)
                    error = target.InnerException;
                if (error.GetType().Name == "Failure")
                {
                    assertions++;
                    Console.Error.WriteLine("FAIL generated recipient dispatch: " + name + ": " + error.Message);
                }
                else
                {
                    unexpected++;
                    Console.Error.WriteLine("ERROR generated recipient dispatch: " + name + ": " + error);
                }
            }
        }
        Console.WriteLine($"Generated recipient dispatch scenarios: {passed}/18; assertions={assertions}; unexpected={unexpected}; actual loader/raw QuestLog/scan/scheduler/XML/CodeNode, complete owner constructors/start/retained TreeSharp ticks; controlled actor/item/menu requests; no native Lua, executor or game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException("Generated recipient dispatch regression");
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
    public static System.Action? OnMenu,BeforeClientRequest;
    public static System.Action? AfterTarget;
    public static System.Action<WoWUnit>? AfterInteract;
    public static int ExpectedOptionNumber=1;
    private static int itemRequests;
    private static ulong submittedTarget;
    private static uint submittedItem;
    public static bool SubmitItem(WoWItem item)
    {
        itemRequests++;
        submittedTarget=ObjectManager.Me?.CurrentTarget?.Guid??0;
        submittedItem=item.Entry;
        return true; // Recording external request boundary, not server acknowledgement.
    }
    public static string MenuText=null!;
    public static string[] Options=null!;
    public static object?[] Available=null!,Active=null!;
    public static bool SnapshotKnown,ThrowOnSnapshot,StringOnlyBridge;
    public static string? CaptureFault;
    public static ulong ClientPlayer;
    public static readonly Dictionary<string,string> GeneratedLua=new();
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
        var mutations=new (string Name,System.Action Change)[]{
            ("option text",()=>Options[0]="A different action"),
            ("option type",()=>Options[1]="vendor"),
            ("option order",()=>Options=new[]{Options[2],Options[3],Options[0],Options[1]}),
            ("option count",()=>Options=Options.Concat(new[]{"Extra","gossip"}).ToArray()),
            ("greeting",()=>MenuText="A different interaction"),
            ("available quest",()=>Available[0]="A different available quest"),
            ("active quest completion",()=>Active[3]=true)
        };
        foreach(var mutation in mutations)
        {
            var m=mutation;
            Add("same NPC changed "+m.Name+" during option observation is not selected or closed",()=>{
                Open();OnMenu=m.Change;Tick();Check(OnMenu==null,"option observation was not reached");
                Check(Selections==0&&Closes==0,"changed observed menu was mutated");CheckNoSubmission();});
            Add("same NPC changed "+m.Name+" at final selection is not selected or closed",()=>{
                Open();BeforeClientRequest=m.Change;Tick();Check(BeforeClientRequest==null,"final request boundary was not reached");
                Check(Selections==0&&Closes==0,"final request mutated a replacement menu");CheckNoSubmission();});
            Add("same NPC changed "+m.Name+" before retry cleanup is preserved",()=>{
                Open();m.Change();Invoke("ResetForRetry");Check(Closes==0,"retry cleanup closed a replacement menu");});
            Add("same NPC changed "+m.Name+" at deferral cleanup is preserved",()=>{
                Open();BeforeClientRequest=m.Change;Invoke("DeferAuthoritativeAttempt","controlled");
                Check(BeforeClientRequest==null,"cleanup request boundary was not reached");Check(Closes==0,"deferral cleanup closed a replacement menu");});
        }
        Add("cleanup without a captured menu cannot adopt the currently visible menu",()=>{
            Open(false);Invoke("ResetForRetry");Check(Closes==0,"cleanup borrowed an unobserved menu");});
        Add("unknown initial menu observation is not selection authority",()=>{
            Open(false);SnapshotKnown=false;Tick();Check(Selections==0&&Closes==0,"unknown menu caused a mutation");CheckNoSubmission();});
        Add("failed initial menu observation is not selection authority",()=>{
            Open(false);ThrowOnSnapshot=true;Tick();Check(Selections==0&&Closes==0,"failed menu read caused a mutation");CheckNoSubmission();});
        Add("a changed client player at the final selection request is rejected",()=>{
            Open();BeforeClientRequest=()=>ClientPlayer=99;Tick();Check(Selections==0&&Closes==0,"final request used a different client player");CheckNoSubmission();});
        Add("a changed client player at cleanup is rejected",()=>{
            Open();BeforeClientRequest=()=>ClientPlayer=99;Invoke("ResetForRetry");Check(Closes==0,"cleanup used a different client player");});
        Add("unchanged quoted unicode multiline menu remains selectable",()=>{
            MenuText="Quoted ' text \\ and \n新しい会話";Options[0]="Choose 'this' \\ option";Open(false);Tick();
            Check(Selections==1,"non-ASCII or quoted menu could not be selected");});
        Add("unchanged quoted unicode multiline menu remains owned for cleanup",()=>{
            MenuText="Quoted ' text \\ and \n新しい会話";Open();Invoke("ResetForRetry");Check(Closes==1,"quoted menu cleanup was lost");});
        Add("current NPC predicate survives the host string-only Lua return bridge",()=>{
            Open(false);StringOnlyBridge=true;Tick();Check(Selections==1,"boolean NPC observation was lost by the string-only return bridge");});
        Add("foreign NPC predicate stays rejected through the string-only bridge",()=>{
            Open(false);StringOnlyBridge=true;CurrentNpc=3;Tick();Check(Selections==0&&Closes==0,"foreign NPC passed transport check");});
        Add("a menu larger than one native string read remains selectable",()=>{
            MenuText=new string('q',1200);Open(false);Tick();Check(Selections==1,"long menu was truncated or rejected instead of completely observed");});
        foreach(string fault in new[]{"missing-chunk","truncated-chunk","wrong-length"})
        {
            string f=fault;
            Add("incomplete menu transport "+f+" is not mutation authority",()=>{
                Open(false);CaptureFault=f;Tick();Check(Selections==0&&Closes==0,"incomplete menu transport caused a mutation");CheckNoSubmission();});
        }
        int pass=0,assertions=0,unexpected=0;
        foreach(var test in tests)
        {
            try{test.Test();pass++;Console.WriteLine("PASS gossip lifetime: "+test.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL gossip lifetime: "+test.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR gossip lifetime: "+test.Name+": "+e);}
        }
        foreach(var script in GeneratedLua.OrderBy(x=>x.Key))
            Console.WriteLine("Gossip Lua recorded "+script.Key+": "+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script.Value)));
        Console.WriteLine($"Gossip lifetime scenarios: {pass}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; complete tracked owner and real quest reader; controlled actor/UI; no Lua or game execution.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Gossip lifetime regression");
    }

    public static void RunGenerated(string behavior,Dictionary<string,string> args,string scenario,
        System.Func<uint,Styx.Logic.Questing.PlayerQuest?> find,System.Action<string> observe,ulong actorGuid)
    {
        // External observations only: never borrow Reset's hand-written behavior
        // arguments, overwrite owner properties, or inject pending/submitted state.
        FindQuest=find;
        kind=behavior=="UseItemOn"?typeof(Script):typeof(Styx.Bot.Quest_Behaviors.GossipEvent.GossipEvent);
        ObjectManager.Me=player=new LocalPlayer{Guid=actorGuid,Location=new WoWPoint(10,10,10),IsMoving=false};
        var target=new WoWUnit{Guid=2,Entry=70001,Location=new WoWPoint(12,10,10)};
        ObjectManager.Objects=new List<WoWObject>{target};
        player.CarriedItems!.Add(new WoWItem{Guid=17,Entry=12345});
        Styx.StyxWoW.IsInGame=true;
        OnMenu=BeforeClientRequest=AfterTarget=null;
        AfterInteract=unit=>{Frame.IsVisible=true;CurrentNpc=unit.Guid;};
        Selections=Closes=Interactions=itemRequests=0;submittedTarget=0;submittedItem=0;
        CurrentNpc=2;ClientPlayer=actorGuid;Frame.IsVisible=false;
        SnapshotKnown=true;ThrowOnSnapshot=false;StringOnlyBridge=true;CaptureFault=null;
        MenuText="Generated source-bound interaction";
        Options=new[]{"Not the requested option","gossip","Requested option","gossip"};
        Available=Array.Empty<object?>();Active=Array.Empty<object?>();
        ExpectedOptionNumber=behavior=="GossipEvent"?int.Parse(args["GossipOptionIndex"],
            System.Globalization.CultureInfo.InvariantCulture)+1:1;
        var retainedArguments=new Dictionary<string,string>(args);
        var current=(Styx.Logic.Questing.CustomForcedBehavior)Activator.CreateInstance(kind,new object[]{args})!;
        GC.SuppressFinalize(current);
        Composite? branch=null;
        bool running=false;
        int Requests()=>behavior=="UseItemOn"?itemRequests:Selections;
        int Counter()=>(int)kind.GetProperty("Counter",Hidden)!.GetValue(current)!;
        bool Acknowledged()=>(bool)kind.GetMethod("HasAuthoritativeSuccess",Hidden)!.Invoke(current,null)!;
        void Stop()
        {
            if(running&&branch!=null)branch.Stop(null!);
            running=false;branch=null;
        }
        void DisposeOwner()
        {
            Stop();
            if(current is Script use)use.Dispose(true);else current.Dispose();
        }
        void Pulse()
        {
            var failures=new List<string>();
            void Record(Styx.Helpers.LogLevel level,string message)
            {if(message.Contains("Exception")||message.Contains("Object reference not set"))failures.Add(message);}
            Styx.Helpers.Logging.OnMessageLogged+=Record;
            try
            {
                branch??=(Composite)kind.GetMethod("CreateBehavior",Hidden)!.Invoke(current,null)!;
                if(!running)branch.Start(null!);
                running=branch.Tick(null!)==RunStatus.Running;
                if(!running)branch.Stop(null!);
                Check(failures.Count==0,"swallowed owner exception: "+string.Join(";",failures));
            }
            finally{Styx.Helpers.Logging.OnMessageLogged-=Record;}
        }
        try
        {
            Check(!current.IsAttributeProblem,"actual owner rejected unchanged CodeNode arguments");
            current.OnStart();
            Check(!current.IsDone&&!Acknowledged()&&Counter()==0,
                "incomplete generated owner was not initially eligible");
            Check(kind.GetProperty("InitialObjectiveCount",Hidden)!.GetValue(current)==null,
                "whole-quest owner read an unmapped dataset index as a raw counter");
            if(scenario=="wrong-recipient")
            {
                target.Entry=79999;
                ObjectManager.Objects.Add(new WoWGameObject{Guid=3,Entry=70001,Location=target.Location});
                Pulse();Pulse();
                Check(Requests()==0&&Interactions==0&&Counter()==0&&!current.IsDone&&!Acknowledged(),
                    "wrong creature or same-entry GameObject acquired request authority");
                return;
            }
            if(scenario=="final-ready")
            {
                bool reached=false;
                void Complete(){reached=true;observe("complete");AfterTarget=null;OnMenu=null;}
                if(behavior=="UseItemOn")AfterTarget=Complete;else OnMenu=Complete;
                for(int i=0;i<4&&!reached;i++)Pulse();
                Check(reached,"generated recipe never reached target/menu setup");
                Check(Requests()==0&&current.IsDone&&Acknowledged(),
                    "ready quest was ignored at the generated recipe's last setup boundary");
                return;
            }
            for(int i=0;i<4&&Requests()==0;i++)Pulse();
            Check(Requests()==1&&Counter()==1&&!current.IsDone&&!Acknowledged(),
                "matching generated recipe did not submit once without claiming acknowledgement");
            if(behavior=="UseItemOn")
                Check(submittedTarget==2&&submittedItem==12345,"generated item request changed recipient or item");
            else Check(Interactions==1&&ExpectedOptionNumber==2,"generated gossip request lost exact option/interaction");
            observe("packed");Pulse();
            Check(Requests()==1&&Counter()==1&&!current.IsDone&&!Acknowledged()&&
                kind.GetProperty("InitialObjectiveCount",Hidden)!.GetValue(current)==null,
                "unrelated raw counters acknowledged, repeated or renumbered a whole-quest recipe");
            observe("complete");
            Check(current.IsDone&&Acknowledged(),"actual raw quest ready-state did not acknowledge the submitted recipe");
            Pulse();Check(Requests()==1,"completed owner submitted again");
            DisposeOwner();
            current=(Styx.Logic.Questing.CustomForcedBehavior)Activator.CreateInstance(kind,new object[]{args})!;
            GC.SuppressFinalize(current);current.OnStart();
            Check(current.IsDone&&Acknowledged()&&Counter()==0,"fresh generated owner repeated an already-ready quest");
            Pulse();Check(Requests()==1,"fresh owner requested an already-ready quest action");
        }
        finally
        {
            try{DisposeOwner();}
            finally
            {
                AfterTarget=null;AfterInteract=null;OnMenu=BeforeClientRequest=null;
                Check(args.Count==retainedArguments.Count&&retainedArguments.All(p=>args.TryGetValue(p.Key,out string? value)&&value==p.Value),
                    "generated behavior arguments were modified during dispatch");
            }
        }
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
        Selections=Closes=Interactions=0;OnMenu=BeforeClientRequest=null;CurrentNpc=2;Frame.IsVisible=false;
        AfterTarget=null;AfterInteract=null;ExpectedOptionNumber=1;itemRequests=0;
        ClientPlayer=player.Guid;SnapshotKnown=true;ThrowOnSnapshot=false;StringOnlyBridge=false;CaptureFault=null;
        MenuText="Source-bound interaction";Options=new[]{"Proceed","gossip","Leave","gossip"};
        Available=new object?[]{"Available quest",10,false,false,false};Active=new object?[]{"Active quest",10,false,false};
        Invoke("OnStart");
        Check(!(bool)kind.GetProperty("IsDone")!.GetValue(owner)!,"fresh incomplete owner unexpectedly done");
    }
    private static void Open(bool observe=true)
    {
        Frame.IsVisible=true;
        kind.GetField("_gossipOpenStartedUtc",Hidden)!.SetValue(owner,DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond);
        kind.GetField("_interactionGuid",Hidden)!.SetValue(owner,2UL);
        // Baseline W81 has no capture method. Keeping this optional allows the
        // unchanged fixture to show its assertion-level red before the repair.
        if(observe)kind.GetMethod("TryCaptureGossipMenu",Hidden)?.Invoke(owner,null);
    }
    private static void CheckNoSubmission()=>Check((long)kind.GetField("_lastSubmissionUtc",Hidden)!.GetValue(owner)!<0,"rejected selection was marked submitted");
    public static void AtClientRequest(){var f=BeforeClientRequest;BeforeClientRequest=null;f?.Invoke();}
    public static string Signature()
    {
        var parts=new List<string>();
        void Add(params object?[] values)
        {
            parts.Add(values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach(object? value in values)
            {
                string type=value==null?"nil":value is string?"string":value is bool?"boolean":"number";
                string text=value==null?"nil":value is bool b?(b?"true":"false"):Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!;
                byte[] bytes=System.Text.Encoding.UTF8.GetBytes(text);
                parts.Add(type+":"+bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant());
            }
        }
        Add(MenuText);Add(Options.Cast<object?>().ToArray());Add(Available);Add(Active);
        return string.Join("|",parts);
    }
    public static bool ClientContext(string script)
    {
        string npc="0x"+CurrentNpc.ToString("X16",System.Globalization.CultureInfo.InvariantCulture);
        string actor="0x"+ClientPlayer.ToString("X16",System.Globalization.CultureInfo.InvariantCulture);
        return Frame.IsVisible&&script.Contains("UnitGUID('npc') ~= '"+npc+"'",StringComparison.Ordinal)
            &&script.Contains("UnitGUID('player') ~= '"+actor+"'",StringComparison.Ordinal);
    }
    public static void Record(string name,string script){if(!GeneratedLua.ContainsKey(name))GeneratedLua.Add(name,script);}
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
        public List<int> GossipOptionEntries{get{var f=GossipLifetimeCases.OnMenu;GossipLifetimeCases.OnMenu=null;f?.Invoke();return Enumerable.Range(0,GossipLifetimeCases.Options.Length/2).ToList();}}
        public void SelectGossipOption(int index){GossipLifetimeCases.AtClientRequest();GossipLifetimeCases.Selections++;}
        public void Close(){if(IsVisible)GossipLifetimeCases.Closes++;IsVisible=false;}
    }
} namespace Styx.WoWInternals
{
    public static class Lua
    {
        public static List<string> GetReturnValues(string script)
        {
            if(RewardRecordedBridge.Observe!=null)return RewardRecordedBridge.GetReturnValues(script);
            foreach(string api in new[]{"GetGossipText()","GetGossipOptions()","GetGossipAvailableQuests()","GetGossipActiveQuests()"})
                if(!script.Contains(api,StringComparison.Ordinal))throw new InvalidOperationException("Incomplete original-client menu observation: "+api);
            GossipLifetimeCases.Record("capture",script);
            if(GossipLifetimeCases.ThrowOnSnapshot)throw new InvalidOperationException("controlled unreadable menu");
            if(!GossipLifetimeCases.SnapshotKnown||!GossipLifetimeCases.ClientContext(script))return new List<string>();
            string value=GossipLifetimeCases.Signature();
            // GreenMagic.Memory.ReadString reads 512 bytes even when given another
            // length. Model complete short string returns, not an unlimited bridge.
            // The production Lua splitter is separately exercised by an interpreter.
            var values=new List<string>{value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)};
            for(int i=0;i<value.Length;i+=480)values.Add(value.Substring(i,Math.Min(480,value.Length-i)));
            if(GossipLifetimeCases.CaptureFault=="missing-chunk")values.RemoveAt(values.Count-1);
            if(GossipLifetimeCases.CaptureFault=="truncated-chunk")values[1]=values[1].Substring(0,values[1].Length-1);
            if(GossipLifetimeCases.CaptureFault=="wrong-length")values[0]=(value.Length+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return values;
        }
        public static T GetReturnVal<T>(string script,uint index)
        {
            if(RewardRecordedBridge.Observe!=null)return RewardRecordedBridge.GetReturnVal<T>(script,index);
            if(index!=0)throw new InvalidOperationException("Unexpected return index");
            if(typeof(T)==typeof(string))
            {
                foreach(string api in new[]{"GetGossipText()","GetGossipOptions()","GetGossipAvailableQuests()","GetGossipActiveQuests()"})
                    if(!script.Contains(api,StringComparison.Ordinal))throw new InvalidOperationException("Incomplete original-client menu observation: "+api);
                GossipLifetimeCases.Record("capture",script);
                if(GossipLifetimeCases.ThrowOnSnapshot)throw new InvalidOperationException("controlled unreadable menu");
                return (T)(object)(GossipLifetimeCases.SnapshotKnown&&GossipLifetimeCases.ClientContext(script)?GossipLifetimeCases.Signature():null)!;
            }
            if(typeof(T)!=typeof(bool)&&typeof(T)!=typeof(int))throw new InvalidOperationException("Unexpected return type");
            if(script.Contains("local observed",StringComparison.Ordinal))
            {
                bool select=script.Contains("SelectGossipOption(",StringComparison.Ordinal);
                GossipLifetimeCases.Record(select?"select":"close",script);
                GossipLifetimeCases.AtClientRequest();
                var expected=System.Text.RegularExpressions.Regex.Match(script,"observed ~= '([^']*)'");
                if(!expected.Success)throw new InvalidOperationException("Final mutation lacks captured-menu comparison");
                bool own=GossipLifetimeCases.SnapshotKnown&&!GossipLifetimeCases.ThrowOnSnapshot
                    &&GossipLifetimeCases.ClientContext(script)&&expected.Groups[1].Value==GossipLifetimeCases.Signature();
                if(own)
                {
                    if(select){if(!script.Contains("SelectGossipOption("+GossipLifetimeCases.ExpectedOptionNumber+")",StringComparison.Ordinal))throw new InvalidOperationException("Original one-based selection contract changed");GossipLifetimeCases.Selections++;}
                    else Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance.Close();
                }
                return Receipt<T>(own);
            }
            const string oldNpc="return UnitGUID('npc') == '0x0000000000000002'";
            const string numericNpc="return (UnitGUID('npc') == '0x0000000000000002') and 1 or 0";
            if(script==oldNpc||script==numericNpc)
            {
                GossipLifetimeCases.Record("npc",script);
                // lua_tolstring transports numbers/strings, not raw booleans.
                bool observed=GossipLifetimeCases.CurrentNpc==2&&(!GossipLifetimeCases.StringOnlyBridge||script==numericNpc);
                return Receipt<T>(observed);
            }
            // An ownership-guarded close request remains a controlled boundary,
            // not evidence that this fixture executes a Lua interpreter.
            if(script.Contains("CloseGossip()",StringComparison.Ordinal)
                &&script.Contains("UnitGUID('npc')",StringComparison.Ordinal)
                &&script.Contains("0x0000000000000002",StringComparison.Ordinal))
            {
                GossipLifetimeCases.AtClientRequest();
                var frame=Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance;
                bool own=frame.IsVisible&&GossipLifetimeCases.CurrentNpc==2;
                if(own)frame.Close();return Receipt<T>(own);
            }
            throw new InvalidOperationException("Unexpected Lua observation: "+script);
        }
        private static T Receipt<T>(bool value)=>(T)(typeof(T)==typeof(int)?(object)(value?1:0):value);
    }
}
""";
}
