using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the complete current objective and approach owners. The dynamic
// fixture supplies external world/time/quest observations and records requests;
// it does not replace the action state machine or invent progress on a request.
internal static class DirectGameObjectExecutionRegressionTests
{
    public static Func<int?>? ReadObservedProgress;
    public static Action<string>? ObserveExecutionEvent;

    [ModuleInitializer]
    internal static void Run()
    {
        _ = CompileProbe(runAdversarialCases: true);
    }

    internal static Assembly CompileProbe(bool runAdversarialCases)
    {
        string root=Root();
        string Class(string name,string type)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,name)))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText==type).ToFullString();
        string owner=Class("Bots/Quest/Objectives/UseGameObjectObjective.cs","UseGameObjectObjective");
        string approach=Class("CommonBehaviors/Actions/GroundLootApproach.cs","GroundLootApproach");
        string extraPath=Path.Combine(root,"CommonBehaviors/Actions/QuestGameObjectInteraction.cs");
        string extra=File.Exists(extraPath)?Class("CommonBehaviors/Actions/QuestGameObjectInteraction.cs","QuestGameObjectInteraction"):"";
        string code=DirectGameObjectExecutionFixture.Source+owner+"\n"+approach+"\n"+extra+Cases;
        code=System.Text.RegularExpressions.Regex.Replace(code,@"^[ \t]*#(?:end)?region\b[^\r\n]*(?:\r?\n|$)","",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        string directory=Path.Combine(Path.GetTempPath(),"cb-direct-go-execution-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Console.WriteLine("Direct GameObject complete-owner source SHA256: "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant());
        try
        {
            File.WriteAllText(Path.Combine(directory,"Probe.cs"),code,Encoding.UTF8);
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{directory})!;
            var result=(CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(row=>!row.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Direct GameObject fixture did not compile: "+string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var fixture=assembly.GetType("World",true)!;
            fixture.GetField("ExternalProgress")!.SetValue(null,ReadObservedProgress);
            fixture.GetField("ExternalEvent")!.SetValue(null,ObserveExecutionEvent);
            if(runAdversarialCases)
            {
                try{assembly.GetType("DirectGoCases",true)!.GetMethod("Run")!.Invoke(null,null);}
                catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            }
            return assembly;
        }
        finally{Directory.Delete(directory,true);}
    }

    private static string Root()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"CopilotBuddy.csproj")))return dir.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Cases="""
public static class DirectGoCases
{
    private sealed class Failure(string message):Exception(message){}
    private static int cases,passed,assertions,unexpected;
    public static void Run()
    {
        Case("use request waits for separate typed credit and never repeats while pending",()=>{
            using var f=new Scene();f.Tick();Check(World.Interactions.Count==1,"one initial interaction required");
            for(int i=0;i<5;i++){Clock.Advance(0.5);f.Tick();}
            Check(World.Interactions.Count==1,"unacknowledged action was repeated");
            Check(!f.Owner.IsCompleted,"void interaction became objective completion");
            World.Progress=1;f.Tick();Check(!f.Owner.IsCompleted,"partial credit completed required count");
        });
        foreach(string reason in new[]{"invalid-object","zero-guid","descriptor-guid","wrong-entry","disabled-object","blacklisted", "invalid-actor","dead-actor","zero-actor-guid",
                 "combat","pet-combat","taxi","transport","casting","channeling","falling","swimming","unknown-movement","unknown-progress","failed-quest","completed-quest",
                 "cannot-use","cannot-use-now","zero-range","not-in-range","no-line-of-sight","foreign-poi"})
        {
            Case("admission / "+reason,()=>{
                using var f=new Scene();
                switch(reason)
                {
                    case "invalid-object":f.Target.IsValid=false;break;
                    case "zero-guid":f.Target.Guid=0;break;
                    case "descriptor-guid":f.Target.DescriptorGuid=222;break;
                    case "wrong-entry":f.Target.Entry++;break;
                    case "disabled-object":f.Target.IsDisabled=true;break;
                    case "blacklisted":Blacklist.Add(f.Target.Guid,TimeSpan.FromMinutes(1));break;
                    case "invalid-actor":ObjectManager.Me.IsValid=false;break;
                    case "dead-actor":ObjectManager.Me.IsAlive=false;break;
                    case "zero-actor-guid":ObjectManager.Me.Guid=0;break;
                    case "combat":ObjectManager.Me.IsActuallyInCombat=true;break;
                    case "pet-combat":ObjectManager.Me.PetInCombat=true;break;
                    case "taxi":ObjectManager.Me.OnTaxi=true;break;
                    case "transport":ObjectManager.Me.IsOnTransport=true;World.Transport=3;break;
                    case "casting":ObjectManager.Me.IsCasting=true;break;
                    case "channeling":ObjectManager.Me.ChanneledCastingSpellId=1;break;
                    case "falling":World.MovementFlags=0x1000;break;
                    case "swimming":World.MovementFlags=0x200000;break;
                    case "unknown-movement":World.MovementKnown=false;break;
                    case "unknown-progress":World.ProgressKnown=false;break;
                    case "failed-quest":f.Owner.Quest.IsFailed=true;break;
                    case "completed-quest":World.Progress=3;break;
                    case "cannot-use":World.CanUse=false;break;
                    case "cannot-use-now":World.CanUseNow=false;break;
                    case "zero-range":f.Target.InteractRange=0;break;
                    case "not-in-range":f.Target.InteractRange=1;break;
                    case "no-line-of-sight":World.LineOfSight=false;break;
                    case "foreign-poi":BotPoi.Current=new BotPoi(f.Target,PoiType.Loot);break;
                }
                f.Tick();Check(World.Interactions.Count==0,"unsafe/unknown state dispatched interaction");
            });
        }
        foreach(string mutation in new[]{"actor","actor-guid","map","mover","provider","behavior","target-ref","target-guid","entry","descriptor","range","sight","combat","progress-unknown","poi"})
        {
            Case("native-readiness callback / "+mutation,()=>{
                using var f=new Scene();World.DuringReadiness=()=>{
                    switch(mutation)
                    {
                        case "actor":ObjectManager.Me=new LocalPlayer{Guid=88};break;
                        case "actor-guid":ObjectManager.Me.Guid++;break;
                        case "map":ObjectManager.Me.MapId++;break;
                        case "mover":WoWMovement.ActiveMover=new LocalPlayer{Guid=55};break;
                        case "provider":Navigator.NavigationProvider=new object();break;
                        case "behavior":QuestOrder.Instance.CurrentBehavior=new ForcedQuestObjective{Objective=f.Owner};break;
                        case "target-ref":ObjectManager.Objects.Clear();ObjectManager.Objects.Add(new WoWGameObject{Guid=f.Target.Guid,DescriptorGuid=f.Target.Guid,Entry=f.Target.Entry});break;
                        case "target-guid":f.Target.Guid++;break;
                        case "entry":f.Target.Entry++;break;
                        case "descriptor":f.Target.DescriptorGuid++;break;
                        case "range":f.Target.Location=new WoWPoint(30,10,10);break;
                        case "sight":World.LineOfSight=false;break;
                        case "combat":ObjectManager.Me.IsActuallyInCombat=true;break;
                        case "progress-unknown":World.ProgressKnown=false;break;
                        case "poi":BotPoi.Current=new BotPoi(f.Target,PoiType.Harvest);break;
                    }
                };
                f.Tick();Check(World.Interactions.Count==0,"readiness callback transferred stale action authority");
            });
        }
        Case("flight arrival requires supported descent then separate ground and dismount replies",()=>{
            using var f=new Scene();ObjectManager.Me.Location=new WoWPoint(12,10,30);ObjectManager.Me.IsFlying=true;ObjectManager.Me.Mounted=true;World.MovementFlags=0x02000000;
            f.Tick();Check(World.DescentRequests==1&&World.Interactions.Count==0,"aerial arrival did not enter owned descent");
            ObjectManager.Me.Location=new WoWPoint(12,10,10);ObjectManager.Me.IsFlying=false;World.MovementFlags=0;
            f.Tick();Check(World.DismountRequests==1&&World.Interactions.Count==0,"ground observation skipped dismount acknowledgement");
            ObjectManager.Me.Mounted=false;ObjectManager.Me.MovementInfo.IsDescending=false;
            f.Tick();Check(World.Interactions.Count==1,"confirmed ground/unmounted arrival did not interact");
        });
        foreach(string scenario in new[]{"missing-support","wrong-floor","blocked-descent","late-remount"})
            Case("ground safety / "+scenario,()=>{
                using var f=new Scene();ObjectManager.Me.Location=new WoWPoint(12,10,30);ObjectManager.Me.IsFlying=true;ObjectManager.Me.Mounted=true;World.MovementFlags=0x02000000;
                if(scenario=="missing-support")World.Support=false;
                if(scenario=="wrong-floor")World.SupportZ=25;
                if(scenario=="blocked-descent")World.LineOfSight=false;
                if(scenario=="late-remount") {ObjectManager.Me.IsFlying=false;World.MovementFlags=0;World.DuringReadiness=()=>ObjectManager.Me.Mounted=true;}
                f.Tick();Check(World.Interactions.Count==0,"unsafe landing dispatched interaction");
                if(scenario!="late-remount")Check(World.DescentRequests==0,"unsupported descent was issued");
            });
        Case("missing live object never turns static coordinates into an interaction",()=>{
            using var f=new Scene();ObjectManager.Objects.Clear();for(int i=0;i<20;i++){f.Tick();Clock.Advance(1);}
            Check(World.Interactions.Count==0&&!f.Owner.IsCompleted,"static travel became action/progress");
            ObjectManager.Objects.Add(f.Target);f.Tick();Check(World.Interactions.Count==1,"respawn was never reacquired");
        });
        Case("pending despawn retains acknowledgement window",()=>{
            using var f=new Scene();f.Tick();ObjectManager.Objects.Clear();World.Progress=1;Clock.Advance(1);f.Tick();
            Check(World.Interactions.Count==1&&!f.Owner.IsCompleted,"despawn invented a second action or full completion");
            var next=new WoWGameObject{Guid=12,DescriptorGuid=12,Entry=90001};ObjectManager.Objects.Add(next);f.Tick();
            Check(World.Interactions.Count==2&&World.Interactions[1]==12,"partial progress did not reacquire next object");
        });
        Case("no acknowledgement has finite per-GUID requests",()=>{
            using var f=new Scene();for(int i=0;i<100;i++){f.Tick();Clock.Advance(1);}
            Check(World.Interactions.Count>=1&&World.Interactions.Count<=2,"no-progress retry budget is unbounded");
            Check(!f.Owner.IsCompleted,"timeout became completion");
        });
        Case("temporary deferral permits one bounded recovery",()=>{
            using var f=new Scene();f.Tick();for(int i=0;i<30;i++){Clock.Advance(1);f.Tick();}
            Check(World.Interactions.Count==2,"failed attempt did not retry within the bounded recovery window");
            World.Progress=1;f.Tick();Check(!f.Owner.IsCompleted,"recovery fabricated remaining progress");
        });
        Case("three separate acknowledged objects complete exactly the required count",()=>{
            using var f=new Scene();for(int count=0;count<3;count++){
                f.Target.Guid=f.Target.DescriptorGuid=(ulong)(11+count);ObjectManager.Objects.Clear();ObjectManager.Objects.Add(f.Target);
                f.Tick();Check(World.Interactions.Count==count+1,"one dispatch per acquired object required");
                Check(World.Progress==count&&!f.Owner.IsCompleted,"request fabricated progress");
                World.Progress=count+1;f.Tick();Check(f.Owner.IsCompleted==(count==2),"authoritative counter boundary changed");
            }
            Check(World.Interactions.Distinct().Count()==3,"repeated GUID supplied separate progress");
        });
        Case("combat interruption cannot spend a stale pending request",()=>{
            using var f=new Scene();f.Tick();ObjectManager.Me.IsActuallyInCombat=true;Clock.Advance(30);f.Tick();
            Check(World.Interactions.Count==1,"combat issued another object request");
            ObjectManager.Me.IsActuallyInCombat=false;World.Progress=1;f.Tick();Check(!f.Owner.IsCompleted,"combat produced full credit");
        });
        foreach(var result in new[]{MoveResult.Failed,MoveResult.PathGenerationFailed})
            Case("missing live source / finite failed static travel "+result,()=>{
                using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);
                World.NavigationResult=result;
                for(int i=0;i<120;i++){f.Tick();Clock.Advance(1);}
                Check(World.GroundRequests>0&&World.GroundRequests<=3,"known failed static travel was resubmitted indefinitely");
                Check(World.Interactions.Count==0&&!f.Owner.IsCompleted,"failed navigation fabricated use or credit");
                Check(f.Owner.ExecutionObservation?.Phase=="static-travel-exhausted","failed route does not expose its exhausted budget");
            });
        foreach(var result in new[]{MoveResult.Moved,MoveResult.ReachedDestination})
            Case("missing live source / zero displacement "+result,()=>{
                using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);
                World.NavigationResult=result;
                for(int i=0;i<120;i++){f.Tick();Clock.Advance(1);}
                Check(World.GroundRequests>0&&World.GroundRequests<=32,"movement return without displacement reset the no-progress budget");
                Check(World.RouteClears==1,"exactly one bounded static-route recomputation was required");
                Check(World.Interactions.Count==0&&!f.Owner.IsCompleted,"route return was treated as source acquisition or progress");
            });
        Case("unknown progress cannot replenish the failed static travel budget",()=>{
            using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);World.NavigationResult=MoveResult.Failed;
            for(int i=0;i<5;i++){f.Tick();Clock.Advance(1);}
            World.ProgressKnown=false;for(int i=0;i<30;i++){f.Tick();Clock.Advance(1);}
            World.ProgressKnown=true;for(int i=0;i<60;i++){f.Tick();Clock.Advance(1);}
            Check(World.GroundRequests<=3,"unknown/known observations refilled failed navigation attempts");
        });
        Case("a new live candidate can recover an exhausted static route",()=>{
            using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);World.NavigationResult=MoveResult.Failed;
            for(int i=0;i<40;i++){f.Tick();Clock.Advance(1);}
            Check(World.GroundRequests<=3,"failed-route request budget was not bounded before respawn");
            f.Target.Location=ObjectManager.Me.Location.Add(2,0,0);ObjectManager.Objects.Add(f.Target);f.Tick();
            Check(World.Interactions.Count==1&&World.Progress==0&&!f.Owner.IsCompleted,"static-route hold prevented fresh live use or fabricated credit");
            World.Progress=1;f.Tick();Check(!f.Owner.IsCompleted,"recovered partial credit completed the whole objective");
        });
        Case("observed approach progress permits a long static route",()=>{
            using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);
            for(int i=0;i<100;i++){f.Tick();ObjectManager.Me.Location=ObjectManager.Me.Location.Add(-1,0,0);Clock.Advance(1);}
            Check(World.GroundRequests>=90&&World.RouteClears==0,"real observed displacement was cut off by a wall-clock route limit");
            Check(World.Progress==0&&!f.Owner.IsCompleted,"travel displacement was treated as quest progress");
        });
        Case("combat time does not consume static route no-progress time",()=>{
            using var f=new Scene();ObjectManager.Objects.Clear();ObjectManager.Me.Location=new WoWPoint(200,10,10);
            for(int i=0;i<5;i++){f.Tick();Clock.Advance(1);}
            int requested=World.GroundRequests;ObjectManager.Me.IsActuallyInCombat=true;
            for(int i=0;i<100;i++){f.Tick();Clock.Advance(1);}
            Check(World.GroundRequests==requested,"combat issued static route commands");
            ObjectManager.Me.IsActuallyInCombat=false;for(int i=0;i<5;i++){f.Tick();Clock.Advance(1);}
            Check(World.GroundRequests>requested&&World.RouteClears==0,"combat time was counted as failed approach time");
        });
        Console.WriteLine($"Direct GameObject execution scenarios: {passed}/{cases}; assertions={assertions}; unexpected={unexpected}; complete objective/approach owners, actual TreeSharp, controlled native/world/time/credit; no live completion.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Direct GameObject execution regressions failed");
    }
    private sealed class Scene:IDisposable
    {
        public readonly UseGameObjectObjective Owner;public readonly WoWGameObject Target;private readonly Composite tree;
        private bool running;
        public Scene()
        {
            World.Reset();Target=new WoWGameObject{Guid=11,DescriptorGuid=11,Entry=90001};ObjectManager.Objects.Add(Target);
            Owner=new UseGameObjectObjective(new PlayerQuest{Id=777,Name="Controlled direct objective"},new List<WoWQuestStep>(),
                new Styx.Logic.Questing.Quest.QuestObjective{ID=90001,Count=3,Index=0},new List<QuestObjective>());
            QuestOrder.Instance.CurrentBehavior=new ForcedQuestObjective{Objective=Owner};tree=Owner.CreateBranch();
        }
        public void Tick(){if(!running){tree.Start(null!);running=true;}var result=tree.Tick(null!);if(result!=RunStatus.Running){tree.Stop(null!);running=false;}}
        public void Dispose(){if(running)tree.Stop(null!);}
    }
    private static void Check(bool value,string message){if(!value)throw new Failure(message);}
    private static void Case(string name,System.Action action)
    {
        cases++;try{action();passed++;}catch(Failure e){assertions++;Console.WriteLine("FAIL DirectGO "+name+": "+e.Message);}
        catch(Exception e){unexpected++;Console.WriteLine("ERROR DirectGO "+name+": "+e);}
    }
}
public static class DirectGoCoupledDriver
{
    private static UseGameObjectObjective owner;
    private static WoWGameObject subject;
    private static Composite tree;
    private static bool running;
    public static void Begin(uint quest,int entry,int required,int slot,uint map,double x,double y,double z,ulong guid,
        Func<int?> progress,System.Action<string> observe,bool flying)
    {
        Stop();World.Reset();World.ExternalProgress=progress;World.ExternalEvent=observe;
        var point=new WoWPoint((float)x,(float)y,(float)z);
        ObjectManager.Me.MapId=map;ObjectManager.Me.Location=point.Add(-25,0,flying?20:0);
        ObjectManager.Me.Mounted=flying;ObjectManager.Me.IsFlying=flying;World.MovementFlags=flying?0x02000000u:0;
        World.SupportZ=(float)z;
        subject=new WoWGameObject{Guid=guid,DescriptorGuid=guid,Entry=(uint)entry,Location=point};
        ObjectManager.Objects.Add(subject);
        owner=new UseGameObjectObjective(new PlayerQuest{Id=quest,Name="Source-bound generated direct objective"},new List<WoWQuestStep>(),
            new Styx.Logic.Questing.Quest.QuestObjective{ID=entry,Count=required,Index=slot},new List<QuestObjective>());
        QuestOrder.Instance.CurrentBehavior=new ForcedQuestObjective{Objective=owner};tree=owner.CreateBranch();
    }
    public static string Tick()
    {
        if(!running){tree.Start(null);running=true;}
        var result=tree.Tick(null);if(result!=RunStatus.Running){tree.Stop(null);running=false;}
        return result.ToString();
    }
    public static void Advance(double seconds)=>Clock.Advance(seconds);
    public static int Interactions()=>World.Interactions.Count;
    public static int Flights()=>World.FlightRequests;
    public static int GroundMoves()=>World.GroundRequests;
    public static int Descents()=>World.DescentRequests;
    public static int Dismounts()=>World.DismountRequests;
    public static bool Done()=>owner.IsCompleted;
    public static string Phase()=>owner.ExecutionObservation?.Phase??"unknown";
    public static string Reason()=>owner.ExecutionObservation?.Reason??"unknown";
    public static string Snapshot()=>System.Text.Json.JsonSerializer.Serialize(owner.ExecutionObservation);
    public static void ArriveAbove(){ObjectManager.Me.Location=subject.Location.Add(0,0,20);}
    public static void GroundReply(){ObjectManager.Me.Location=subject.Location;ObjectManager.Me.IsFlying=false;World.MovementFlags=0;}
    public static void UnmountedReply(){ObjectManager.Me.Mounted=false;ObjectManager.Me.MovementInfo.IsDescending=false;}
    public static void RemoveObject()=>ObjectManager.Objects.Clear();
    public static void RestoreObject()=>ObjectManager.Objects.Add(subject);
    public static void Reacquire(ulong guid){ObjectManager.Objects.Clear();subject=new WoWGameObject{Guid=guid,DescriptorGuid=guid,Entry=subject.Entry,Location=subject.Location};ObjectManager.Objects.Add(subject);}
    public static void Stop(){if(running)tree?.Stop(null);running=false;owner?.Dispose();owner=null;tree=null;World.ExternalProgress=null;World.ExternalEvent=null;}
}
""";
}
