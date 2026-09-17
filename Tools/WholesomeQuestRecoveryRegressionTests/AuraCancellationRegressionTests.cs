using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Compile the unchanged tracked WoWAura owner. World observations and Lua dispatch
// alone are controlled. The produced scripts are retained for interpreter checks;
// this is not a game process or proof of server acknowledgement.
internal static class AuraCancellationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string? root=null;
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj"))){root=d.FullName;break;}
        if(root==null)throw new InvalidOperationException("Tracked source checkout required");
        string temp=Path.Combine(Path.GetTempPath(),"cb-aura-owner-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.Copy(Path.Combine(root,"Styx","Logic","Combat","WoWAura.cs"),Path.Combine(temp,"WoWAura.cs"));
            File.WriteAllText(Path.Combine(temp,"Boundary.cs"),Boundary);
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{temp})!;
            foreach(string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference",flags)!.Invoke(compiler,new object[]{reference});
            var result=(CompilerResults)compilerType.GetMethod("Compile",flags)!.Invoke(compiler,Array.Empty<object>())!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Actual aura owner compile failed: "+string.Join(";",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)compilerType.GetProperty("CompiledAssembly",flags)!.GetValue(compiler)!;
            try { assembly.GetType("AuraCases",true)!.GetMethod("Run")!.Invoke(null,null); }
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally { Directory.Delete(temp,true); }
    }
    private const string Boundary="""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using Styx.Logic.Combat;
using Styx.WoWInternals;
public static class AuraCases
{
    private sealed class Failure(string message):Exception(message){}
    internal static Action? Observe,Dispatch,Log;
    internal static List<string> Scripts=new();
    public static void Run()
    {
        var cases=new List<(string,Action)>();
        void Add(string n,Action f)=>cases.Add((n,f));
        Add("ordinary self-owned buff produces one identity-bound request",()=>{var a=Reset();Check(a.TryCancel(),"request rejected");Script();});
        Add("raw harmful prefix cannot become a helpful-buff index",()=>{var a=Reset();ObjectManager.Me!.Auras.Insert(0,New(777,128|1));Check(a.TryCancel(),"request rejected");Script();});
        Add("raw unrelated buff cannot redirect the requested identity",()=>{var a=Reset();ObjectManager.Me!.Auras.Insert(0,New(778,16|1));Check(a.TryCancel(),"request rejected");Script();});
        Add("missing current aura has no name fallback",()=>{var a=Reset();ObjectManager.Me!.Auras.Clear();Denied(a);});
        Add("same spell cast by someone else cannot authorize cancellation",()=>{var a=Reset();ObjectManager.Me!.Auras[0]=New(25780,17,9);Denied(a);});
        Add("current noncancellable flags veto old permission",()=>{var a=Reset();ObjectManager.Me!.Auras[0]=New(25780,1);Denied(a);});
        Add("current harmful flags veto old permission",()=>{var a=Reset();ObjectManager.Me!.Auras[0]=New(25780,145);Denied(a);});
        Add("inactive current aura is not permission",()=>{var a=Reset();ObjectManager.Me!.Auras[0]=New(25780,16);Denied(a);});
        Add("passive aura is never cancelled",()=>{Reset();var a=New(25780,81);ObjectManager.Me!.Auras=new(){a};Denied(a);});
        Add("caller noncancellable flags remain denied",()=>{Reset();Denied(New(25780,1));});
        Add("caller harmful flags remain denied",()=>{Reset();Denied(New(25780,145));});
        Add("caller inactive flags remain denied",()=>{Reset();Denied(New(25780,16));});
        Add("non-self caster remains denied",()=>{Reset();Denied(New(25780,17,9));});
        Add("zero spell identity remains denied",()=>{Reset();var a=New(0,17);ObjectManager.Me!.Auras=new(){a};Denied(a);});
        Add("negative spell identity remains denied",()=>{Reset();var a=New(-1,17);ObjectManager.Me!.Auras=new(){a};Denied(a);});
        foreach(string state in new[]{"missing-player","dead","invalid","zero-address","missing-memory","missing-executor"})
        {
            string s=state;Add("initial "+s+" is denied",()=>{var a=Reset();Change(s);Denied(a);});
        }
        foreach(string state in new[]{"missing-player","new-player","dead","invalid","address","guid","missing-memory","new-memory","missing-executor","new-executor"})
        {
            string s=state;Add("observed "+s+" replacement revokes the request",()=>{var a=Reset();Observe=()=>Change(s);Denied(a);});
        }
        Add("unchanged callback retains cancellation",()=>{var a=Reset();Observe=()=>{};Check(a.TryCancel(),"unchanged callback revoked");Script();});
        Add("dispatch cancellation propagates exactly",()=>StopSignal(new OperationCanceledException("cancel")));
        Add("dispatch interruption propagates exactly",()=>StopSignal(new ThreadInterruptedException("interrupt")));
        Add("ordinary dispatch error remains false",()=>{var a=Reset();Dispatch=()=>throw new InvalidOperationException("offline");Check(!a.TryCancel(),"ordinary failure escaped compatibility");});
        int pass=0,failed=0,unexpected=0;
        foreach(var item in cases)
        {
            try{item.Item2();pass++;Console.WriteLine("PASS aura cancellation: "+item.Item1);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL aura cancellation assertion: "+item.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR aura cancellation fixture: "+item.Item1+": "+e);}
        }
        Console.WriteLine($"Aura cancellation scenarios: {pass}/{cases.Count}; assertions={failed}; unexpected={unexpected}; linked actual WoWAura; controlled observation/dispatch; no game attached.");
        if(failed+unexpected>0)throw new InvalidOperationException("Aura cancellation failures");
    }
    private static WoWAura Reset()
    {
        Scripts.Clear();Observe=null;Dispatch=null;Log=null;
        ObjectManager.Me=new Player();ObjectManager.Wow=new Memory();ObjectManager.Executor=new object();
        var a=New(25780,49);ObjectManager.Me.Auras.Add(a);return a;
    }
    private static WoWAura New(int id,int flags,ulong caster=1)=>new(id,caster,(WoWAura.AuraFlags)flags,1,80,0,0);
    private static void Change(string s)
    {
        switch(s){case "missing-player":ObjectManager.Me=null;break;case "new-player":ObjectManager.Me=new Player();break;
        case "dead":ObjectManager.Me!.IsAlive=false;break;case "invalid":ObjectManager.Me!.IsValid=false;break;
        case "zero-address":ObjectManager.Me!.BaseAddress=0;break;case "address":ObjectManager.Me!.BaseAddress+=16;break;
        case "guid":ObjectManager.Me!.Guid=2;break;case "missing-memory":ObjectManager.Wow=null;break;
        case "new-memory":ObjectManager.Wow=new Memory();break;case "missing-executor":ObjectManager.Executor=null;break;
        case "new-executor":ObjectManager.Executor=new object();break;}
    }
    private static void Denied(WoWAura aura){Check(!aura.TryCancel(),"invalid request reported success");Check(Scripts.Count==0,"invalid request crossed dispatch");}
    private static void Script()
    {
        Check(Scripts.Count==1,"expected exactly one request");string s=Scripts[0];
        Check(!Regex.IsMatch(s,@"CancelUnitBuff\s*\(\s*[""']player[""']\s*,\s*\d"),"raw aura ordinal used as Lua helpful index");
        Check(s.Contains("25780")&&s.Contains("UnitGUID")&&s.Contains("0000000000000001")&&s.Contains("GetSpellInfo"),"request is not bound to spell and actor identity");
        Console.WriteLine("AURA_SCRIPT_BASE64:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(s)));
    }
    private static void StopSignal(Exception expected){var a=Reset();Dispatch=()=>throw expected;Exception? got=null;try{a.TryCancel();}catch(Exception e){got=e;}Check(ReferenceEquals(got,expected),"stop signal swallowed or replaced");}
    private static void Check(bool yes,string why){if(!yes)throw new Failure(why);}
}
namespace Styx.WoWInternals
{
    public class Player
    {
        public ulong Guid=1;public uint BaseAddress=100;public bool IsAlive=true,IsValid=true;
        public List<WoWAura> Auras=new();
        public List<WoWAura> GetAllAuras(){var result=Auras.ToList();var callback=AuraCases.Observe;AuraCases.Observe=null;callback?.Invoke();return result;}
    }
    public class Memory{public T Read<T>(uint address)=>default!;}
    public static class ObjectManager{public static Player? Me;public static Memory? Wow;public static object? Executor;}
    public static class Lua{public static void DoString(string script){AuraCases.Dispatch?.Invoke();AuraCases.Scripts.Add(script);}}
}
namespace Styx.Helpers
{
    public static class Logging{public static void WriteDebug(string text,params object[] values)=>AuraCases.Log?.Invoke();public static void WriteException(Exception error){} }
}
namespace Styx
{
    public static class StyxWoW{public static class WoWClient{public static ulong PerformanceCounter()=>1;}}
}
namespace Styx.Logic.Combat
{
    public class WoWSpell{public string Name=>"Righteous Fury";public string Rank=>"";public Effect? SpellEffect1=>null;public static WoWSpell FromId(int id)=>new();}
    public class Effect{public WoWApplyAuraType AuraType=>WoWApplyAuraType.None;}
}
""";
}
