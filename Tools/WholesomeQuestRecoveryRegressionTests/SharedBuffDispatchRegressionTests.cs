using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

// Execute exact contiguous Cast/Buff/BuffSelf source regions with real TreeSharp.
// Only world observations, range/safety admission and terminal spell dispatch are
// controlled. Full Singular compilation is separately retained by the existing suite.
internal static class SharedBuffDispatchRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string? root=null;
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj"))){root=d.FullName;break;}
        if(root==null)throw new InvalidOperationException("Tracked checkout required");
        string text=File.ReadAllText(Path.Combine(root,"runtime-snapshot","Routines","Singular wotlk","Helpers","Spell.cs"));
        const string start="        #region Cast - by name", end="        #region Heal - by name";
        int first=text.IndexOf(start,StringComparison.Ordinal), last=text.IndexOf(end,StringComparison.Ordinal);
        if(first<0||last<=first||text.IndexOf(start,first+start.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("Ambiguous actual Cast/Buff region boundaries");
        string region=text.Substring(first,last-first);
        Console.WriteLine("Shared buff exact-region SHA256: "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(region))).ToLowerInvariant());
        string prefix="using System; using System.Collections.Generic; using System.Linq; using CommonBehaviors.Actions; using Styx; using Styx.Logic.Combat; using Styx.WoWInternals.WoWObjects; using TreeSharp; using Action=TreeSharp.Action; namespace Singular.Helpers { public delegate WoWUnit UnitSelectionDelegate(object c); public delegate bool SimpleBooleanDelegate(object c); internal static class Spell { private const float MeleeRange=5; ";
        string temp=Path.Combine(Path.GetTempPath(),"cb-shared-buff-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging=Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging=false;
            File.WriteAllText(Path.Combine(temp,"Owners.cs"),prefix+region+"}}",Encoding.UTF8);
            File.WriteAllText(Path.Combine(temp,"Boundary.cs"),Boundary,Encoding.UTF8);
            Type type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(type,new object[]{temp})!;
            foreach(string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference",flags)!.Invoke(compiler,new object[]{path});
            var result=(CompilerResults)type.GetMethod("Compile",flags)!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Exact shared buff owners did not compile: "+string.Join(";",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly",flags)!.GetValue(compiler)!;
            try{assembly.GetType("SharedBuffCases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Styx.Helpers.Logging.FileLogging=logging;Directory.Delete(temp,true);}
    }
    private const string Boundary="""
using System;
using System.Collections.Generic;
using System.Linq;
using Singular.Helpers;
using Styx;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
public static class SharedBuffCases
{
    private sealed class Failure(string text):Exception(text){}
    internal static readonly List<ulong> CastTargets=new();
    internal static System.Action? DuringSetup;
    internal static bool Setup, Accepted=true;
    public static void Run()
    {
        var cases=new List<(string,System.Action)>();
        void Add(string name,System.Action body)=>cases.Add((name,()=>{Reset();body();}));
        Add("ID self buff requires no hostile target",()=>{StyxWoW.Me.CurrentTarget=null;Tick(Spell.BuffSelf(101));Expect(1);});
        Add("ID self buff cannot target the selected enemy",()=>{Tick(Spell.BuffSelf(101));Expect(1);});
        Add("ID self buff ignores an enemy's existing aura",()=>{Aura(StyxWoW.Me.CurrentTarget!,"Test",101,9);Tick(Spell.BuffSelf(101));Expect(1);});
        Add("ID self buff does not duplicate the player's aura",()=>{Aura(StyxWoW.Me,"Test",101,1);Tick(Spell.BuffSelf(101));Expect();});
        Add("ID self requirement overload retains player selection",()=>{Tick(Spell.BuffSelf(101,_=>true));Expect(1);});
        Add("ID self requirement rejection remains effective",()=>{Tick(Spell.BuffSelf(101,_=>false));Expect();});
        Add("ID target buff still targets the selected enemy",()=>{Tick(Spell.Buff(101));Expect(2);});
        Add("ID target aura is a veto",()=>{Aura(StyxWoW.Me.CurrentTarget!,"Test",101,9);Tick(Spell.Buff(101));Expect();});
        Add("ID unrelated aura is not a veto",()=>{Aura(StyxWoW.Me.CurrentTarget!,"Other",202,9);Tick(Spell.Buff(101));Expect(2);});
        Add("ID aura arriving during setup revokes submission",()=>{Setup=true;DuringSetup=()=>Aura(StyxWoW.Me.CurrentTarget!,"Test",101,9);Tick(Spell.Buff(101));Expect();});
        Add("ID replacement target with coverage is not recast",()=>{Setup=true;DuringSetup=()=>{var t=new WoWUnit{Guid=3};Aura(t,"Test",101,9);StyxWoW.Me.CurrentTarget=t;};Tick(Spell.Buff(101));Expect();});
        Add("ID missing target during setup remains rejected",()=>{Setup=true;DuringSetup=()=>StyxWoW.Me.CurrentTarget=null;Tick(Spell.Buff(101));Expect();});
        foreach(int id in new[]{0,-1}){int v=id;Add("invalid buff ID "+v+" never dispatches",()=>{Tick(Spell.BuffSelf(v));Expect();});}
        foreach(string name in new[]{"Blessing of Might","Arcane Intellect","Power Word: Fortitude","Mark of the Wild","Battle Shout","Horn of Winter","Water Shield","Aspect of the Hawk","Fel Armor","Slice and Dice"})
        {
            string n=name;
            Add(n+": observed coverage is a shared-helper veto",()=>{Learn(n);Aura(StyxWoW.Me,n,101,1);Tick(Spell.BuffSelf(n));Expect();});
            Add(n+": coverage appearing after setup blocks duplicate",()=>{Learn(n);Setup=true;DuringSetup=()=>Aura(StyxWoW.Me,n,101,1);Tick(Spell.BuffSelf(n));Expect();});
            Add(n+": unchanged unbuffed player retains dispatch",()=>{Learn(n);Setup=true;DuringSetup=()=>{};Tick(Spell.BuffSelf(n));Expect(1);});
        }
        Add("caller-declared alternate coverage is rechecked after setup",()=>{Learn("Blessing of Might");Setup=true;DuringSetup=()=>Aura(StyxWoW.Me,"Battle Shout",202,9);Tick(Spell.Buff("Blessing of Might",false,_=>StyxWoW.Me,_=>true,"Blessing of Might","Battle Shout"));Expect();});
        Add("another caster's aura does not block myBuff semantics",()=>{Learn("Test");Setup=true;DuringSetup=()=>Aura(StyxWoW.Me.CurrentTarget!,"Test",101,9);Tick(Spell.Buff("Test",true));Expect(2);});
        Add("own aura appearing after setup does block myBuff",()=>{Learn("Test");Setup=true;DuringSetup=()=>Aura(StyxWoW.Me.CurrentTarget!,"Test",101,1);Tick(Spell.Buff("Test",true));Expect();});
        Add("unrelated effect remains complementary",()=>{Learn("Battle Shout");Aura(StyxWoW.Me,"Horn of Winter",202,9);Tick(Spell.BuffSelf("Battle Shout"));Expect(1);});
        Add("intentional aspect transition is not globally frozen",()=>{Learn("Aspect of the Dragonhawk");Aura(StyxWoW.Me,"Aspect of the Viper",202,1);Tick(Spell.BuffSelf("Aspect of the Dragonhawk"));Expect(1);});
        Add("missing named target after setup remains rejected",()=>{Learn("Test");Setup=true;DuringSetup=()=>StyxWoW.Me.CurrentTarget=null;Tick(Spell.Buff("Test"));Expect();});
        Add("false requirement still prevents named dispatch",()=>{Learn("Test");Tick(Spell.BuffSelf("Test",_=>false));Expect();});
        Add("rejected dispatch does not acquire retry dictionary entry",()=>{Learn("Test");Accepted=false;Tick(Spell.BuffSelf("Test"));Expect();Check(!Spell.DoubleCastPreventionDict.ContainsKey("Test"),"rejected submission acquired retry state");});
        Add("successful named dispatch retains existing retry guard",()=>{Learn("Test");Tick(Spell.BuffSelf("Test"));Tick(Spell.BuffSelf("Test"));Expect(1);});
        Add("missing caller-declared coverage array is rejected",()=>{Learn("Test");Tick(Spell.Buff("Test",false,_=>StyxWoW.Me,_=>true,null!));Expect();});
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try{c.Item2();passed++;Console.WriteLine("PASS shared buff: "+c.Item1);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL shared buff assertion: "+c.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR shared buff fixture/owner: "+c.Item1+": "+e);}
        }
        Console.WriteLine($"Shared buff dispatch scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; exact Cast/Buff region and real TreeSharp; controlled observations/dispatch; not all class rotations or game effects.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Shared buff dispatch failures");
    }
    private static void Reset(){StyxWoW.Me=new WoWUnit{Guid=1,CurrentTarget=new WoWUnit{Guid=2}};CastTargets.Clear();Spell.DoubleCastPreventionDict.Clear();SpellManager.Spells.Clear();Setup=false;DuringSetup=null;Accepted=true;Learn("Test");}
    private static void Learn(string name)=>SpellManager.Spells[name]=new WoWSpell();
    private static void Aura(WoWUnit unit,string name,int id,ulong caster)=>unit.Auras[name]=new Aura{Name=name,SpellId=id,CreatorGuid=caster};
    private static void Tick(Composite tree){tree.Start(null!);try{int count=0;while(tree.Tick(null!)==RunStatus.Running)if(++count>12)throw new Failure("unbounded decision");}finally{tree.Stop(null!);}}
    private static void Expect(params ulong[] ids)=>Check(CastTargets.SequenceEqual(ids),"unexpected submission targets: "+string.Join(',',CastTargets));
    private static void Check(bool yes,string text){if(!yes)throw new Failure(text);}
}
/* Controlled observations, not a substitute for buff owners. */ namespace Styx.WoWInternals.WoWObjects
{
    public class Aura{public int SpellId;public string Name="";public ulong CreatorGuid;}
    public class WoWUnit
    {
        public ulong Guid;public WoWUnit? CurrentTarget;public bool Mounted,IsCasting;
        public bool IsMe=>ReferenceEquals(this,StyxWoW.Me);public float Distance=>20;public bool InLineOfSpellSight=>true;
        public Dictionary<string,Aura> Auras=new();
        public bool HasAura(string name)=>Auras.ContainsKey(name);
        public bool HasMyAura(string name)=>Auras.TryGetValue(name,out var aura)&&aura.CreatorGuid==StyxWoW.Me.Guid;
        public string SafeName()=>"controlled";
    }
}
/* Controlled spell admission/dispatch only. */ namespace Styx.Logic.Combat
{
    public class WoWSpell{public uint SpellRangeId=>3;public float MinRange=>0,MaxRange=>100;public int CastTime=>0;public bool IsFunnel=>false;public bool IsChanneled=>false;}
    public static class SpellManager
    {
        public static Dictionary<string,WoWSpell> Spells=new();
        public static bool CanCast(string name,WoWUnit target,bool range,bool movement)=>target!=null&&Spells.ContainsKey(name);
        public static bool CanCast(int id,WoWUnit target,bool range)=>id>0&&target!=null;
        public static bool Cast(string name,WoWUnit target)=>Submit(target);
        public static bool Cast(int id,WoWUnit target)=>Submit(target);
        private static bool Submit(WoWUnit target){if(!SharedBuffCases.Accepted)return false;SharedBuffCases.CastTargets.Add(target.Guid);return true;}
    }
}
/* Controlled owner observation. */ namespace Styx{public static class StyxWoW{public static WoWUnit Me=null!;}}
/* Controlled setup yields; production Cast/Buff and host TreeSharp remain actual. */ namespace Singular.Helpers
{
    public static class Unit{public static bool IsCombatActionSafe(string name,WoWUnit target)=>target!=null;public static bool IsCombatActionSafe(int id,WoWUnit target)=>target!=null;}
    public static class Logger{public static void Write(string text){}}
    public class SetupAction:Composite
    {
        protected override IEnumerable<RunStatus> Execute(object context){yield return RunStatus.Running;var action=SharedBuffCases.DuringSetup;SharedBuffCases.DuringSetup=null;action?.Invoke();yield return RunStatus.Success;}
    }
    public static class Common{public static Composite CreateDismount(string why)=>new SetupAction();}
    public static class Movement
    {
        public static bool NeedsOffTargetCastSetup(WoWUnit target)=>SharedBuffCases.Setup;
        public static Composite CreateEnsureTargetAndFaceBehavior(UnitSelectionDelegate select)=>new SetupAction();
    }
}
""";
}
