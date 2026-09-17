using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Compile the complete tracked Escort script against real TreeSharp and the
// host's CustomForcedBehavior parser. Only world/target/movement observations
// and external effects are controlled. No profile auto-generation or game run.
internal static class EscortBehaviorRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string? root=null;
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj"))){root=d.FullName;break;}
        if(root==null)throw new InvalidOperationException("Tracked checkout required");
        string temp=Path.Combine(Path.GetTempPath(),"cb-escort-owner-"+Guid.NewGuid().ToString("N"));
        bool oldLogging=Styx.Helpers.Logging.FileLogging;
        Directory.CreateDirectory(temp);
        try
        {
            Styx.Helpers.Logging.FileLogging=false;
            File.Copy(Path.Combine(root,"runtime-snapshot","Quest Behaviors","Escort.cs"),Path.Combine(temp,"Escort.cs"));
            File.WriteAllText(Path.Combine(temp,"Boundary.cs"),Boundary);
            Type type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(type,new object[]{temp})!;
            foreach(string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference",flags)!.Invoke(compiler,new object[]{path});
            var results=(CompilerResults)type.GetMethod("Compile",flags)!.Invoke(compiler,null)!;
            var errors=results.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Actual Escort compile failed: "+string.Join(";",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly",flags)!.GetValue(compiler)!;
            try { assembly.GetType("EscortCases",true)!.GetMethod("Run")!.Invoke(null,null); }
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally { Styx.Helpers.Logging.FileLogging=oldLogging; Directory.Delete(temp,true); }
    }
    private const string Boundary="""
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Script=Styx.Bot.Quest_Behaviors.Escort.Escort;
using Kind=Styx.Bot.Quest_Behaviors.Escort.ObjectType;
using Mode=Styx.Bot.Quest_Behaviors.Escort.DefendUnitType;
using Until=Styx.Bot.Quest_Behaviors.Escort.EscortUntilType;
public static class EscortCases
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private sealed class Failure(string message):Exception(message){}
    private static Script owner=null!;
    private static WoWObject subject=null!;
    internal static List<ulong> Targets=new();
    public static void Run()
    {
        var cases=new List<(string,System.Action)>();
        foreach(bool objectMode in new[]{false,true})
        {
            bool obj=objectMode;string label=obj?"object":"NPC";
            void Add(string name,System.Action body)=>cases.Add((label+": "+name,()=>{Reset(obj);body();}));
            Add("protected subject alone is not an enemy",()=>ExpectEnemies());
            Add("nearby hostile remains an eligible perimeter threat",()=>{var u=Enemy(2);ExpectEnemies(u.Guid);});
            Add("friendly bystander cannot mask a hostile",()=>{Enemy(1,false);var u=Enemy(2);ExpectEnemies(u.Guid);});
            Add("dead hostile is not a defense target",()=>{Enemy(2).IsAlive=false;ExpectEnemies();});
            Add("invalid hostile is not a defense target",()=>{Enemy(2).IsValid=false;ExpectEnemies();});
            Add("unrelated far hostile is not pulled",()=>{Enemy(30);ExpectEnemies();});
            Add("ten-yard perimeter remains strict",()=>{Enemy(10);ExpectEnemies();});
            Add("inside perimeter remains available",()=>{var u=Enemy(9.99f);ExpectEnemies(u.Guid);});
            Add("vertical separation is retained",()=>{var u=Enemy(0);u.Location=subject.Location.Add(0,0,20);ExpectEnemies();});
            Add("missing subject cannot authorize a threat",()=>{ObjectManager.Objects.Clear();Enemy(2);ExpectEnemies();});
            Add("invalid subject cannot authorize a threat",()=>{subject.IsValid=false;Enemy(2);ExpectEnemies();});
            Add("missing player cannot authorize a threat",()=>{Enemy(2);ObjectManager.Me=null;ExpectEnemies();});
            foreach(int scenario in Enumerable.Range(0,8))
            {
                int n=scenario;
                Add("destination condition "+n,()=>{
                    Set("DefendType",obj?Mode.ItemStartTimer:Mode.Unit);
                    if(n==0)subject.Location=subject.Location.Add(30,0,0);
                    if(n==2)ObjectManager.Objects.Clear();
                    if(n==3)subject.Location=subject.Location.Add(0,0,20);
                    if(n==4)Set("EscortDestination",WoWPoint.Empty);
                    if(n==5)Set("EscortUntil",Until.QuestComplete);
                    if(n==6)subject.Location=subject.Location.Add(5,0,0);
                    if(n==7)subject.Location=subject.Location.Add(5.01f,0,0);
                    bool expected=n==1||n==6;
                    var root=(GroupComposite)Invoke(typeof(Script).GetMethod("CreateBehavior",Hidden)!,owner,null)!;
                    var mode=(Decorator)root.Children[obj?1:0];
                    var arrival=(Decorator)((GroupComposite)mode.DecoratedChild).Children[0];
                    bool allowed=(bool)Invoke(typeof(Decorator).GetMethod("CanRun",Hidden)!,arrival,new object[]{null!})!;
                    Check(allowed==expected,"arrival must require both escort and player at the finite destination; case="+n);
                });
            }
        }
        cases.Add(("NPC: attacker beyond perimeter remains eligible",()=>{Reset(false);var u=Enemy(30);u.CurrentTarget=(WoWUnit)subject;ExpectEnemies(u.Guid);}));
        cases.Add(("NPC: actual attacker outranks unrelated close hostile",()=>{Reset(false);var near=Enemy(2);var attacker=Enemy(30);attacker.CurrentTarget=(WoWUnit)subject;ExpectEnemies(attacker.Guid,near.Guid);}));
        cases.Add(("NPC: a friendly unit targeting the escort is not attacked",()=>{Reset(false);Enemy(30,false).CurrentTarget=(WoWUnit)subject;ExpectEnemies();}));
        cases.Add(("NPC: subject is never selected even with hostile observation",()=>{Reset(false);((WoWUnit)subject).IsHostile=true;ExpectEnemies();}));
        cases.Add(("one query captures its subject once",()=>{Reset(false);Enemy(2);Enemy(3);Enemy(4);ObjectManager.Reads=0;_ = ReadEnemies();Check(ObjectManager.Reads==2,"repeated subject enumeration inside threat filtering");}));
        foreach(bool item in new[]{false,true})
        {
            bool timed=item;
            cases.Add(((item?"timer":"unit")+": defense targets enemy not enemy's victim",()=>{
                Reset(false);Set("DefendType",timed?Mode.ItemStartTimer:Mode.Unit);
                ObjectManager.Me!.Combat=true;var threat=Enemy(2);threat.CurrentTarget=(WoWUnit)subject;
                var root=(GroupComposite)Invoke(typeof(Script).GetMethod("CreateBehavior",Hidden)!,owner,null)!;
                var branch=(Decorator)root.Children[timed?1:0];
                var defense=((GroupComposite)branch.DecoratedChild).Children.Last();
                defense.Start(null!);try{defense.Tick(null!);}finally{defense.Stop(null!);}
                Check(Targets.SequenceEqual(new[]{threat.Guid}),"defense did not select the hostile attacker exactly once");
            }));
        }
        int pass=0,failed=0,unexpected=0;
        foreach(var item in cases)
        {
            try{item.Item2();pass++;Console.WriteLine("PASS escort observation: "+item.Item1);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL escort observation assertion: "+item.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR escort observation fixture: "+item.Item1+": "+e);}
        }
        Console.WriteLine($"Escort observation scenarios: {pass}/{cases.Count}; assertions={failed}; unexpected={unexpected}; complete tracked Escort and real TreeSharp; controlled world/effects; no automatic quest mapping or game attached.");
        if(failed+unexpected!=0)throw new InvalidOperationException("Escort observation failures");
    }
    private static void Reset(bool gameObject)
    {
        ObjectManager.Me=new LocalPlayer{Guid=1,Location=new WoWPoint(10,10,10)};
        ObjectManager.Objects.Clear();Targets.Clear();ObjectManager.Reads=0;
        subject=gameObject?new WoWGameObject():new WoWUnit();subject.Guid=2;subject.Entry=70001;subject.Location=ObjectManager.Me.Location;
        ObjectManager.Objects.Add(subject);
        owner=(Script)RuntimeHelpers.GetUninitializedObject(typeof(Script));GC.SuppressFinalize(owner);
        Set("ObjectId",new[]{70001});Set("MobType",gameObject?Kind.GameObject:Kind.Npc);
        Set("EscortDestination",ObjectManager.Me.Location);Set("Location",ObjectManager.Me.Location);
        Set("EscortUntil",Until.DestinationReached);Set("DefendType",Mode.Unit);Set("MaxRange",20d);
        owner.TimeOut=new Stopwatch();
    }
    private static WoWUnit Enemy(float distance,bool hostile=true)
    {
        var u=new WoWUnit{Guid=(ulong)(100+ObjectManager.Objects.Count),Entry=80001,IsHostile=hostile,Location=subject.Location.Add(distance,0,0)};
        ObjectManager.Objects.Add(u);return u;
    }
    private static void Set(string name,object value)=>typeof(Script).GetProperty(name,Hidden)!.SetValue(owner,value);
    private static List<WoWUnit> ReadEnemies()
    {
        try{return (List<WoWUnit>)Invoke(typeof(Script).GetProperty("EnemyList",Hidden)!.GetMethod!,owner,null)!;}
        catch(NullReferenceException){throw new Failure("missing subject caused a production threat-query null dereference");}
    }
    private static void ExpectEnemies(params ulong[] expected)=>Check(ReadEnemies().Select(x=>x.Guid).SequenceEqual(expected),"threat set/order includes a protected, invalid or unrelated unit");
    private static object? Invoke(MethodInfo method,object target,object[]? args)
    {
        try{return method.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static void Check(bool value,string why){if(!value)throw new Failure(why);}
}
/* Controlled world boundary. */ namespace Styx.WoWInternals.WoWObjects
{
    public class WoWObject
    {
        public ulong Guid;public uint Entry;public string Name="controlled";public WoWPoint Location;
        public bool IsValid=true;public bool WithinInteractRange=>Distance<=5;
        public float Distance=>ObjectManager.Me==null?float.MaxValue:Location.Distance(ObjectManager.Me.Location);
        public float DistanceSqr=>Distance*Distance;public float X=>Location.X;public float Y=>Location.Y;public float Z=>Location.Z;
        public void Interact(){}
    }
    public class WoWUnit:WoWObject
    {
        public bool IsAlive=true,IsHostile,Combat;public bool Dead=>!IsAlive;public bool IsFriendly=>!IsHostile;
        public WoWUnit? CurrentTarget;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;
        public void Target(){EscortCases.Targets.Add(Guid);ObjectManager.Me!.CurrentTarget=this;}
    }
    public class WoWGameObject:WoWObject{}
    public class WoWItem:WoWObject{public void UseContainerItem(){}}
    public class LocalPlayer:WoWUnit
    {
        public List<WoWItem> CarriedItems=new();public ControlledLog QuestLog=new();public void ClearTarget(){CurrentTarget=null;}
    }
    public class ControlledLog{public Styx.Logic.Questing.PlayerQuest? GetQuestById(uint id)=>null;}
}
/* Controlled world boundary. */ namespace Styx.WoWInternals
{
    public static class ObjectManager
    {
        public static LocalPlayer? Me;public static List<WoWObject> Objects=new();public static int Reads;
        public static List<T> GetObjectsOfType<T>()where T:WoWObject{Reads++;return Objects.OfType<T>().ToList();}
    }
    public static class Lua{public static void DoString(string s){}}
}
/* Controlled external effects only; no replacement tree. */ namespace Styx
{
    public static class StyxWoW{public static LocalPlayer Me=>ObjectManager.Me!;public static void SleepForLagDuration(){}}
    public static class BotEvents{public static event System.Action<EventArgs>? OnBotStop;}
}
/* Controlled external effects only. */ namespace Styx.Logic.Pathing
{
    public static class Navigator{public static MoveResult MoveTo(WoWPoint p)=>MoveResult.Moved;}
}
/* Controlled external effects only. */ namespace Styx.Logic.BehaviorTree
{
    public static class TreeRoot{public static string GoalText="",StatusText="";}
}
/* Controlled routine dispatch only. */ namespace Styx.Logic.Combat
{
    public static class RoutineManager{public static Routine Current=new();}
    public class Routine{public Composite PullBehavior=>new TreeSharp.Action(_=>RunStatus.Failure);public void Pull(){}}
}
/* Controlled settings observation. */ namespace Styx.Helpers
{
    public class CharacterSettings{public static CharacterSettings Instance=new();public bool HarvestHerbs,HarvestMinerals,LootChests,LootMobs,NinjaSkin,SkinMobs;}
}
/* Unused quest-status observation; these cases exercise arrival, not server credit. */ namespace Styx.Logic.Questing
{
    public class PlayerQuest{public bool IsFailed;}
}
""";
}
