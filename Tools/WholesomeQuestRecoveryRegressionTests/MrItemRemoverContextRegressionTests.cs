using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

// Execute the tracked deletion state declarations, lifecycle hooks and methods.
// Inventory, BotEvents delivery and Lua are controlled boundaries. Lua requests
// are recorded, not interpreted; no native item deletion or game is attached.
internal static class MrItemRemoverContextRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string folder = Path.Combine(root, "runtime-snapshot", "Plugins", "MrItemRemover2");
        string methods = File.ReadAllText(Path.Combine(folder, "Methods.cs"));
        string plugin = File.ReadAllText(Path.Combine(folder, "MrItemRemover2.cs"));
        int fieldsStart = methods.IndexOf("private static readonly TimeSpan DeleteTimeout", StringComparison.Ordinal);
        int fieldsEnd = methods.IndexOf("public void SellVenderItems", fieldsStart, StringComparison.Ordinal);
        if (fieldsStart < 0 || fieldsEnd <= fieldsStart) throw new InvalidOperationException("Tracked deletion state region missing");
        string state = methods.Substring(fieldsStart, fieldsEnd - fieldsStart);
        var pieces = new List<string> { state, Method(plugin, "OnEnable"), Method(plugin, "OnDisable") };
        foreach (string name in new[] { "BeginDelete", "TickPendingDelete", "PendingDeleteItemStillObserved",
            "ReadOwnedCursorState", "TryIssueDeleteRequest", "TryConfirmPendingDelete", "DeleteItemConfirmPopup",
            "BuildOwnedDeleteRequestLua", "BuildOwnedDeleteConfirmationLua", "ResetPendingDelete" })
            pieces.Add(Method(methods, name));
        foreach (string name in new[] { "CanDeleteNow", "OwnsPendingDeleteContext", "ReleasePendingDelete", "ResetDeleteLifetime" })
            pieces.Add(Method(methods, name, optional: true));
        string temporary = Path.Combine(Path.GetTempPath(), "cb-mir-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + string.Join("\n", pieces) + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked MIR lifecycle compilation failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            try { assembly.GetType("MirContextCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static string Method(string source, string name, bool optional = false)
    {
        Match match = Regex.Match(source, @"(?m)^\s*(?:private|public)\s+(?:(?:static|override)\s+)*[\w?<>]+\s+" + Regex.Escape(name) + @"\s*\(");
        if (!match.Success)
        {
            if (optional) return string.Empty;
            throw new InvalidOperationException("Tracked method missing: " + name);
        }
        int brace = source.IndexOf('{', match.Index), depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(match.Index, i - match.Index + 1);
        }
        throw new InvalidOperationException("Unterminated tracked method: " + name);
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Prefix = """
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Styx.Logic.BehaviorTree;
public class PluginBase { public virtual void OnEnable(){} public virtual void OnDisable(){} }
public sealed class TimerProbe { public void Reset(){} }
public sealed class LuaEventArgs : EventArgs { }
public class LocalPlayer
{
    public ulong Guid=7;
    public bool IsValid=true,IsAlive=true,IsGhost,Combat,IsCasting,IsChanneling,Mounted;
    public WoWItem[]? BagItems=Array.Empty<WoWItem>();
}
public sealed class WoWItem
{
    public ulong Guid=11; public uint Entry=22; public bool IsValid=true; public string Name="controlled item";
    public bool TryPickUp()
    {
        MirContextCases.Pickups++;
        bool accepted=MirContextCases.PickupResult;
        if(accepted) Lua.Cursor=1;
        var action=MirContextCases.OnPickup;MirContextCases.OnPickup=null;action?.Invoke();
        return accepted;
    }
}
public static class StyxWoW { public static LocalPlayer? Me; public static bool IsInGame=true; } namespace Styx.Logic.BehaviorTree { public static class TreeRoot { public static bool IsRunning=true,IsPaused; } }
public static class ObjectManager
{
    public static T? GetObjectByGuid<T>(ulong guid) where T:class => StyxWoW.Me?.BagItems?.FirstOrDefault(x=>x!=null&&x.Guid==guid) as T;
}
public static class BotEvents
{
    public static event Action<EventArgs>? OnBotStart,OnBotStop;
    public static void Start()=>OnBotStart?.Invoke(EventArgs.Empty);
    public static void Stop()=>OnBotStop?.Invoke(EventArgs.Empty);
    public static void Clear(){OnBotStart=null;OnBotStop=null;}
}
public sealed class MrItemRemover2Settings
{
    public static MrItemRemover2Settings Instance{get;}=new();
    public string EnableRemove="True";
    public void Load(){}
}
public static class Lua
{
    public static int Requests,Confirmations,Cursor=1;
    public static bool Submit=true;
    public static Action? DuringRequest,DuringPopup;
    public static void DoString(string code){}
    public static T GetReturnVal<T>(string code,uint index)
    {
        if(index!=0)throw new InvalidOperationException("Unexpected Lua index");
        if(typeof(T)==typeof(bool)&&code.Contains("DeleteCursorItem()"))
        {
            Requests++;bool answer=Submit;
            var f=DuringRequest;DuringRequest=null;f?.Invoke();return (T)(object)answer;
        }
        if(typeof(T)==typeof(int)&&code.Contains("button1:Click()"))
        {Confirmations++;return (T)(object)2;}
        if(typeof(T)==typeof(int)&&code.Contains("GetCursorInfo()"))return (T)(object)Cursor;
        if(typeof(T)==typeof(int)&&code.Contains("StaticPopup_FindVisible"))
        {var f=DuringPopup;DuringPopup=null;f?.Invoke();return (T)(object)1;}
        throw new InvalidOperationException("Unexpected Lua request: "+code);
    }
    public static class Events
    {
        private static readonly Dictionary<string,List<Action<object,LuaEventArgs>>> handlers=new();
        public static void AttachEvent(string name,Action<object,LuaEventArgs> callback)
        {if(!handlers.TryGetValue(name,out var list))handlers[name]=list=new();list.Add(callback);}
        public static void DetachEvent(string name,Action<object,LuaEventArgs> callback)
        {if(handlers.TryGetValue(name,out var list))list.Remove(callback);}
        public static void Fire(string name)
        {if(handlers.TryGetValue(name,out var list))foreach(var callback in list.ToArray())callback(null!,new LuaEventArgs());}
        public static void Clear()=>handlers.Clear();
    }
}
public class MrItemRemover2 : PluginBase
{
    private bool IsInitialized{get;set;}
    private bool EnableCheck{get;set;}
    public bool ManualCheckRequested{get;set;}
    private static LocalPlayer? Me=>StyxWoW.Me;
    private readonly TimerProbe _checkTimer=new();
    private void InitialMirLoad(){}
    private void PrintSettings(){}
    private void MirSave(){}
    private void SellVenderItems(object sender,LuaEventArgs args){}
    private void LootEnded(object sender,LuaEventArgs args){}
    private static void Slog(string format,params object[] args)=>MirContextCases.Log(format,args);
    private static void Dlog(string format,params object[] args)=>MirContextCases.Log(format,args);
""";

    private const string Suffix = """
}
public static class MirContextCases
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message){}
    private static MrItemRemover2 owner=null!;
    private static LocalPlayer player=null!;
    private static WoWItem item=null!;
    public static int Pickups;
    public static bool PickupResult=true;
    public static Action? OnPickup,OnLog;
    private static readonly List<string> logs=new();
    public static void Log(string format,object[] args)
    {logs.Add(string.Format(format,args));var f=OnLog;OnLog=null;f?.Invoke();}
    private static object? Call(object instance,string name,params object?[] args)
    {
        try{return instance.GetType().GetMethod(name,Hidden)!.Invoke(instance,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static object? Get(object instance,string name)=>instance.GetType().GetField(name,Hidden)!.GetValue(instance);
    private static void Set(object instance,string name,object value)=>instance.GetType().GetField(name,Hidden)!.SetValue(instance,value);
    private static bool Pending(object instance)=>(bool)instance.GetType().GetProperty("HasPendingDelete",Hidden)!.GetValue(instance)!;
    private static void Reset()
    {
        BotEvents.Clear();Lua.Events.Clear();Lua.Submit=true;Lua.Cursor=1;Lua.DuringRequest=Lua.DuringPopup=null;
        TreeRoot.IsRunning=true;TreeRoot.IsPaused=false;StyxWoW.IsInGame=true;MrItemRemover2Settings.Instance.EnableRemove="True";
        OnPickup=OnLog=null;PickupResult=true;
        player=new LocalPlayer();item=new WoWItem();player.BagItems=new[]{item};StyxWoW.Me=player;
        owner=new MrItemRemover2();Call(owner,"OnEnable");ClearRequests();
    }
    private static void ClearRequests(){Pickups=Lua.Requests=Lua.Confirmations=0;logs.Clear();}
    private static void Begin()=>Call(owner,"BeginDelete",item);
    private static void PendingSetup()
    {Begin();Check(Pickups==1&&Lua.Requests==1&&Pending(owner),"valid pending setup failed");ClearRequests();}
    private static void Mutation(string path)
    {if(path=="event")Call(owner,"DeleteItemConfirmPopup",null,new LuaEventArgs());else Call(owner,path);}
    private static void NoMutation()=>Check(Pickups==0&&Lua.Requests==0&&Lua.Confirmations==0,"stale context performed a pickup/delete/confirmation request");
    public static void Run()
    {
        var cases=new List<(string Name,Action Test)>();
        void Add(string name,Action test)=>cases.Add((name,()=>{Reset();test();}));
        var invalid=new (string Name,Action Change)[]{
            ("uninitialized plugin",()=>typeof(MrItemRemover2).GetProperty("IsInitialized",Hidden)!.SetValue(owner,false)),
            ("removal disabled",()=>MrItemRemover2Settings.Instance.EnableRemove="False"),
            ("stopped",()=>TreeRoot.IsRunning=false),("paused",()=>TreeRoot.IsPaused=true),
            ("lost world",()=>StyxWoW.IsInGame=false),("missing player",()=>StyxWoW.Me=null),
            ("invalid player",()=>player.IsValid=false),("zero player GUID",()=>player.Guid=0),
            ("dead",()=>player.IsAlive=false),("ghost",()=>player.IsGhost=true),
            ("combat",()=>player.Combat=true),("casting",()=>player.IsCasting=true)
        };
        foreach(var state in invalid)
        {
            Add("pickup rejects "+state.Name,()=>{state.Change();Begin();NoMutation();});
            foreach(string path in new[]{"TickPendingDelete","TryIssueDeleteRequest","TryConfirmPendingDelete","event"})
                Add(path+" rejects "+state.Name,()=>{PendingSetup();state.Change();Mutation(path);NoMutation();});
        }
        foreach(string path in new[]{"TickPendingDelete","TryIssueDeleteRequest","TryConfirmPendingDelete","event"})
        {
            Add(path+" rejects same-GUID replacement player",()=>{PendingSetup();StyxWoW.Me=new LocalPlayer{Guid=player.Guid,BagItems=player.BagItems};Mutation(path);NoMutation();});
            Add(path+" rejects changed player GUID",()=>{PendingSetup();player.Guid=99;Mutation(path);NoMutation();});
        }
        Add("ordinary admitted delete retains request and confirmation",()=>{PendingSetup();Call(owner,"TryConfirmPendingDelete");Check(Lua.Confirmations==1&&Lua.Requests==0,"valid confirmation was lost");});
        Add("refused pickup leaves no pending transaction",()=>{PickupResult=false;Begin();Check(Pickups==1&&Lua.Requests==0&&!Pending(owner),"refused pickup published deletion intent");});
        Add("player replacement during pickup blocks subsequent delete request",()=>{OnPickup=()=>StyxWoW.Me=new LocalPlayer{Guid=player.Guid};Begin();Check(Pickups==1&&Lua.Requests==0,"pickup result used a replacement actor");});
        Add("Stop during pickup blocks subsequent delete request",()=>{OnPickup=()=>TreeRoot.IsRunning=false;Begin();Check(Lua.Requests==0,"stopped pickup continued to delete");});
        Add("disable during popup observation blocks confirmation",()=>{PendingSetup();Lua.DuringPopup=()=>Call(owner,"OnDisable");Mutation("event");Check(Lua.Confirmations==0,"disabled callback confirmed deletion");});
        Add("bot Stop then Start without a pending tick revokes old request",()=>{PendingSetup();BotEvents.Stop();BotEvents.Start();Mutation("event");NoMutation();Check(!Pending(owner),"old request survived Stop/Start events");});
        Add("fresh request works after Stop/Start event revocation",()=>{PendingSetup();BotEvents.Stop();BotEvents.Start();Begin();Check(Pickups==1&&Lua.Requests==1,"fresh run could not begin independent work");});
        Add("enabling a second plugin instance does not erase first pending state",()=>{PendingSetup();var second=new MrItemRemover2();Call(second,"OnEnable");Check(Pending(owner),"second instance reset first transaction");});
        Add("second instance callback cannot confirm first instance request",()=>{PendingSetup();var second=new MrItemRemover2();Call(second,"OnEnable");Begin();ClearRequests();Call(second,"DeleteItemConfirmPopup",null,new LuaEventArgs());NoMutation();});
        Add("disabling an unrelated instance does not retire active owner's request",()=>{var second=new MrItemRemover2();Call(second,"OnEnable");PendingSetup();Call(second,"OnDisable");Check(Pending(owner),"unrelated disable reset active transaction");});
        Add("popup event subscription dispatches one confirmation for one owner",()=>{var second=new MrItemRemover2();Call(second,"OnEnable");PendingSetup();Lua.Events.Fire("DELETE_ITEM_CONFIRM");Check(Lua.Confirmations==1,"shared callback confirmed the request more than once");});
        Add("old successful request result cannot mark replacement request submitted",()=>{
            Lua.DuringRequest=()=>{Call(owner,"OnDisable");Call(owner,"OnEnable");Lua.Submit=false;Call(owner,"BeginDelete",new WoWItem{Guid=33,Entry=44});};
            Begin();Check((ulong)Get(owner,"_pendingDeleteGuid")! ==33&&!(bool)Get(owner,"_pendingDeleteRequested")!,"old result marked replacement request successful");
        });
        Add("timeout diagnostic cannot clear replacement transaction",()=>{
            PendingSetup();Set(owner,"_pendingDeleteSince",DateTime.UtcNow-TimeSpan.FromSeconds(11));
            OnLog=()=>{Call(owner,"OnDisable");Call(owner,"OnEnable");Call(owner,"BeginDelete",new WoWItem{Guid=33,Entry=44});};
            Call(owner,"TickPendingDelete");Check((ulong)Get(owner,"_pendingDeleteGuid")! ==33,"old timeout cleared replacement transaction");
        });
        Add("active timeout still releases local pending state",()=>{PendingSetup();Set(owner,"_pendingDeleteSince",DateTime.UtcNow-TimeSpan.FromSeconds(11));Call(owner,"TickPendingDelete");Check(!Pending(owner)&&logs.Any(x=>x.Contains("timed out")),"active timeout changed");});
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try{c.Test();passed++;Console.WriteLine("PASS MIR context: "+c.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL MIR context: "+c.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR MIR context: "+c.Name+": "+e);}
        }
        Console.WriteLine($"MIR deletion context scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; tracked state/lifecycle/mutation methods; controlled external observations; no Lua/game execution.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("MIR deletion context regression");
    }
    private static void Check(bool condition,string reason){if(!condition)throw new Failure(reason);}
}
""";
}
