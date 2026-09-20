using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

// Execute the exact tracked Pulse, CheckForItems, selection/protection and delete
// lifecycle methods together. Inventory, timers, file lists and Lua are controlled
// boundaries; this is not a native inventory/deletion or real-time test.
internal static class MrItemRemoverScanContinuationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string folder = Path.Combine(root, "runtime-snapshot", "Plugins", "MrItemRemover2");
        string methods = File.ReadAllText(Path.Combine(folder, "Methods.cs"));
        string plugin = File.ReadAllText(Path.Combine(folder, "MrItemRemover2.cs"));
        int first = methods.IndexOf("private static readonly TimeSpan DeleteTimeout", StringComparison.Ordinal);
        int last = methods.IndexOf("public void SellVenderItems", first, StringComparison.Ordinal);
        if (first < 0 || last <= first) throw new InvalidOperationException("Tracked MIR state declarations missing");
        var pieces = new List<string> { methods.Substring(first, last - first) };
        foreach (string name in new[] { "OnEnable", "OnDisable", "Pulse", "LootEnded" })
            pieces.Add(Method(plugin, name));
        foreach (string name in new[] { "CheckForItems", "IsQuestItem", "GetTime", "BeginDelete", "TickPendingDelete",
            "CanDeleteNow", "OwnsPendingDeleteContext", "ReleasePendingDelete", "ResetDeleteLifetime",
            "PendingDeleteItemStillObserved", "ReadOwnedCursorState", "TryIssueDeleteRequest", "TryConfirmPendingDelete",
            "DeleteItemConfirmPopup", "BuildOwnedDeleteRequestLua", "BuildOwnedDeleteConfirmationLua", "ResetPendingDelete" })
            pieces.Add(Method(methods, name));
        foreach (string name in new[] { "CanScanItemsNow", "OwnsItemScan", "EndItemScan" })
            pieces.Add(Method(methods, name, optional: true));
        string temp = Path.Combine(Path.GetTempPath(), "cb-mir-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), Prefix + string.Join("\n", pieces) + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked MIR scan compile failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            try { assembly.GetType("MirScanCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temp, true); }
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
using System.Threading;
using Styx.Logic.BehaviorTree;
public class PluginBase { public virtual void OnEnable(){} public virtual void OnDisable(){} public virtual void Pulse(){} }
public sealed class TimerProbe
{
    public TimeSpan TimeLeft=TimeSpan.FromHours(1);
    public DateTime EndTime=DateTime.UtcNow.AddHours(1);
    public void Reset(){TimeLeft=TimeSpan.FromHours(1);}
}
public sealed class LuaEventArgs:EventArgs{}
public sealed class LocalPlayer
{
    public ulong Guid=7;
    public bool IsValid=true,IsAlive=true,IsGhost,Combat,IsCasting,IsChanneling,Mounted;
    public bool IsDead=>!IsAlive;
    public WoWItem[]? BagItems=Array.Empty<WoWItem>();
}
public enum WoWItemQuality { Poor,Common,Uncommon,Rare,Epic }
public sealed class ItemInfo { public uint BeginQuestId,SellPrice,RequiredLevel; }
public sealed class WoWItem
{
    public ulong Guid;public uint Entry;public bool IsValid=true,PickupResult=true,IsOpenable,Quest,QuestInfoReadable=true;
    public string Name="";public uint StackCount=1;public int BagSlot=1;public WoWItemQuality Quality=WoWItemQuality.Uncommon;
    public ItemInfo ItemInfo=new();
    public bool TryPickUp()
    {
        MirScanCases.Pickups.Add(Guid);
        bool accepted=PickupResult && Lua.Cursor==0;
        if(accepted){Lua.Cursor=1;Lua.HeldGuid=Guid;}
        var f=MirScanCases.OnPickup;MirScanCases.OnPickup=null;f?.Invoke();return accepted;
    }
    public bool TryGetContainerItemQuestInfo(out bool quest,out int id,out bool active)
    {quest=Quest;id=Quest?123:0;active=Quest;return QuestInfoReadable;}
}
public static class StyxWoW
{
    public static LocalPlayer? Me;public static bool IsInGame=true;
    public static void SleepForLagDuration(){var f=MirScanCases.OnSleep;MirScanCases.OnSleep=null;f?.Invoke();}
}
namespace Styx.Logic.BehaviorTree { public static class TreeRoot { public static bool IsRunning=true,IsPaused; } }
public static class ObjectManager
{public static T? GetObjectByGuid<T>(ulong guid) where T:class=>StyxWoW.Me?.BagItems?.FirstOrDefault(x=>x!=null&&x.Guid==guid) as T;}
public static class SpellManager { public static TimeSpan GlobalCooldownLeft=>TimeSpan.Zero; }
public static class TextExtensions { public static int ToInt32(this string s)=>int.Parse(s,CultureInfo.InvariantCulture); }
public static class BotEvents
{
    public static event Action<EventArgs>? OnBotStart,OnBotStop;
    public static void Start()=>OnBotStart?.Invoke(EventArgs.Empty);
    public static void Stop()=>OnBotStop?.Invoke(EventArgs.Empty);
    public static void Clear(){OnBotStart=null;OnBotStop=null;}
}
public sealed class MrItemRemover2Settings
{
    public static MrItemRemover2Settings Instance{get;set;}=new();
    public string EnableRemove="True",LootCheck="True",EnableOpen="False",RemoveFood="False",RemoveDrinks="False",DeleteQuestItems="False";
    public string DeleteAllGray="False",DeleteAllWhite="False",DeleteAllGreen="False",DeleteAllBlue="False";
    public int GoldGrays,SilverGrays,CopperGrays,Time=60;
    public void Load(){}
}
public static class Lua
{
    public static int Cursor,Confirmations,Requests;public static ulong HeldGuid;
    public static bool AutoAcknowledge=true;
    public static List<string> Uses=new();
    public static void DoString(string code){if(code.StartsWith("UseItemByName"))Uses.Add(code);}
    public static T GetReturnVal<T>(string code,uint index)
    {
        if(index!=0)throw new InvalidOperationException("Unexpected Lua index");
        if(typeof(T)==typeof(bool)&&code.Contains("DeleteCursorItem()"))
        {
            Requests++;
            if(AutoAcknowledge)
            {StyxWoW.Me!.BagItems=StyxWoW.Me.BagItems!.Where(x=>x.Guid!=HeldGuid).ToArray();Cursor=0;}
            return (T)(object)true;
        }
        if(typeof(T)==typeof(int)&&code.Contains("button1:Click()")){Confirmations++;return (T)(object)2;}
        if(typeof(T)==typeof(int)&&code.Contains("GetCursorInfo()"))return (T)(object)Cursor;
        if(typeof(T)==typeof(int)&&code.Contains("StaticPopup_FindVisible"))return (T)(object)1;
        throw new InvalidOperationException("Unexpected Lua boundary: "+code);
    }
    public static class Events
    {public static void AttachEvent(string n,Action<object,LuaEventArgs> cb){} public static void DetachEvent(string n,Action<object,LuaEventArgs> cb){}}
}
public class MrItemRemover2:PluginBase
{
    private bool IsInitialized{get;set;}
    private bool EnableCheck{get;set;}
    public bool ManualCheckRequested{get;set;}
    private static LocalPlayer? Me=>StyxWoW.Me;
    private readonly TimerProbe _checkTimer=new();
    private readonly string _removeListPath="remove",_bagListPath="bags";
    public List<string> ItemName=new(),BagList=new(),KeepList=new(),OpnList=new(),FoodList=new(),DrinkList=new(),Combine3List=new(),Combine5List=new(),Combine10List=new();
    private void InitialMirLoad(){} private void PrintSettings(){} private void MirSave(){}
    private void SellVenderItems(object sender,LuaEventArgs args){}
    private void LoadList(List<string> list,string path){}
    private static void Slog(string format,params object[] args)=>MirScanCases.Log(format,args);
    private static void Dlog(string format,params object[] args)=>MirScanCases.Log(format,args);
""";

    private const string Suffix = """
}
public static class MirScanCases
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message){}
    private static MrItemRemover2 owner=null!;private static LocalPlayer player=null!;
    private static WoWItem first=null!,second=null!;
    public static List<ulong> Pickups=new();public static Action? OnSleep,OnPickup,OnLog;
    private static List<string> logs=new();
    public static void Log(string format,object[] args)
    {logs.Add(string.Format(format,args));var f=OnLog;OnLog=null;f?.Invoke();}
    private static void Reset()
    {
        BotEvents.Clear();TreeRoot.IsRunning=true;TreeRoot.IsPaused=false;StyxWoW.IsInGame=true;
        MrItemRemover2Settings.Instance=new();Lua.Cursor=Lua.Confirmations=Lua.Requests=0;Lua.HeldGuid=0;Lua.AutoAcknowledge=true;Lua.Uses.Clear();
        OnSleep=OnPickup=OnLog=null;Pickups.Clear();logs.Clear();
        first=new WoWItem{Guid=11,Entry=21,Name="first"};second=new WoWItem{Guid=12,Entry=22,Name="second"};
        player=new LocalPlayer{BagItems=new[]{first,second}};StyxWoW.Me=player;
        owner=new MrItemRemover2();owner.OnEnable();owner.ItemName.AddRange(new[]{"first","second"});logs.Clear();
    }
    private static object? Call(string name,params object[] args)
    {
        try{return typeof(MrItemRemover2).GetMethod(name,Hidden)!.Invoke(owner,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static void Tick(int count=1){for(int i=0;i<count;i++)owner.Pulse();}
    private static void Trigger(){owner.ManualCheckRequested=true;}
    private static bool Pending=>(bool)typeof(MrItemRemover2).GetProperty("HasPendingDelete",Hidden)!.GetValue(owner)!;
    private static void Picked(params ulong[] expected)=>Check(Pickups.SequenceEqual(expected),"pickup order: "+string.Join(",",Pickups)+"; expected: "+string.Join(",",expected));
    private static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
    public static void Run()
    {
        var cases=new List<(string Name,Action Test)>();
        void Add(string name,Action test)=>cases.Add((name,()=>{Reset();test();}));
        Add("one manual trigger visits both requested removals",()=>{Trigger();Tick(12);Picked(11,12);Check(Lua.Requests==2&&!Pending,"finite scan did not retire");});
        Add("one loot trigger visits remaining candidates without another event",()=>{Call("LootEnded",new object(),new LuaEventArgs());Tick(12);Picked(11,12);});
        Add("refused first pickup is not retried or allowed to starve the next item",()=>{first.PickupResult=false;Trigger();Tick(12);Picked(11,12);Check(Lua.Requests==1,"refusal became a deletion request");});
        Add("all refused candidates are tried only once per pass",()=>{first.PickupResult=second.PickupResult=false;Trigger();Tick(12);Picked(11,12);Check(Lua.Requests==0,"refused candidate was deleted");});
        Add("one Pulse cannot start two deletion attempts after refusal",()=>{first.PickupResult=false;Trigger();Tick();Check(Pickups.Count==1,"same Pulse issued multiple pickup attempts");});
        Add("a returned first item does not starve the second",()=>{Lua.AutoAcknowledge=false;Trigger();Tick();Check(Pending,"pending setup missing");Lua.Cursor=0;Lua.AutoAcknowledge=true;Tick(12);Picked(11,12);});
        Add("pending request blocks selection of later candidates",()=>{Lua.AutoAcknowledge=false;Trigger();Tick(4);Picked(11);Check(Pending,"request unexpectedly retired");});
        Add("expired pending work resumes remaining pass without a new trigger",()=>{Lua.AutoAcknowledge=false;Trigger();Tick();typeof(MrItemRemover2).GetField("_pendingDeleteSince",Hidden)!.SetValue(owner,DateTime.UtcNow.AddSeconds(-11));Lua.Cursor=0;Lua.AutoAcknowledge=true;Tick(12);Picked(11,12);});
        Add("new arrivals do not extend an already captured finite pass",()=>{Trigger();Tick();var third=new WoWItem{Guid=13,Entry=23,Name="third"};owner.ItemName.Add("third");player.BagItems=player.BagItems!.Append(third).ToArray();Tick(12);Picked(11,12);});
        Add("a fresh trigger can inspect a later arrival",()=>{Trigger();Tick(12);var third=new WoWItem{Guid=13,Entry=23,Name="third"};owner.ItemName.Add("third");player.BagItems=new[]{third};Trigger();Tick(8);Check(Pickups.Contains(13),"later trigger did not start a fresh pass");});
        Add("same-name replacement is not borrowed into the captured pass",()=>{Trigger();Tick();player.BagItems=new[]{new WoWItem{Guid=13,Entry=second.Entry,Name=second.Name}};Tick(12);Picked(11);});
        Add("same-GUID replacement object is not the captured candidate",()=>{Trigger();Tick();player.BagItems=new[]{new WoWItem{Guid=second.Guid,Entry=second.Entry,Name=second.Name}};Tick(12);Picked(11);});
        Add("an empty captured pass does not claim a removal",()=>{player.BagItems=Array.Empty<WoWItem>();Trigger();Tick(4);Picked();Check(Lua.Requests==0,"empty pass deleted an item");});
        Add("null inventory is a bounded deferral not an exception or empty success",()=>{player.BagItems=null;Trigger();try{Tick(4);}catch(Exception e){throw new Failure("unknown inventory escaped: "+e.GetType().Name);}Picked();});
        Add("invalid initial inventory cannot publish a destructive pass",()=>{second.IsValid=false;Trigger();Tick(4);Picked();});
        Add("missing player does not enter manual inventory work",()=>{StyxWoW.Me=null;Trigger();try{Tick();}catch(Exception e){throw new Failure("missing player escaped: "+e.GetType().Name);}Picked();});
        Add("Stop Start invalidates remaining scan without a pending tick",()=>{Trigger();Tick();BotEvents.Stop();BotEvents.Start();Tick(12);Picked(11);});
        Add("player replacement invalidates remaining scan",()=>{Trigger();Tick();StyxWoW.Me=new LocalPlayer{Guid=player.Guid,BagItems=player.BagItems};Tick(12);Picked(11);});
        Add("disable invalidates remaining scan",()=>{Trigger();Tick();owner.OnDisable();Tick(4);Picked(11);});
        Add("combat prevents opening as well as deletion",()=>{player.Combat=true;owner.ItemName.Clear();owner.OpnList.Add(first.Name);first.IsOpenable=true;MrItemRemover2Settings.Instance.EnableOpen="True";Trigger();Tick();Check(Pickups.Count==0&&Lua.Uses.Count==0,"combat scan dispatched item use");});
        Add("pause prevents scanning",()=>{TreeRoot.IsPaused=true;Trigger();Tick(2);Picked();});
        Add("disable during initial lag wait prevents later candidate actions",()=>{OnSleep=()=>owner.OnDisable();Trigger();Tick(4);Picked();});
        Add("player replacement during lag wait does not adopt the new actor",()=>{OnSleep=()=>StyxWoW.Me=new LocalPlayer{Guid=player.Guid,BagItems=player.BagItems};Trigger();Tick(4);Picked();});
        Add("a scan logging callback cannot recursively dispatch a second candidate",()=>{Trigger();OnLog=()=>owner.Pulse();Tick();Check(Pickups.Count<=1,"reentrant scan started more than one candidate");});
        Add("keep-list item remains protected while later work runs",()=>{owner.KeepList.Add(first.Name);Trigger();Tick(12);Picked(12);});
        Add("generic quality removal preserves quest item",()=>{owner.ItemName.Clear();first.Quest=true;MrItemRemover2Settings.Instance.DeleteAllGreen="True";Trigger();Tick(12);Picked(12);});
        Add("unreadable quest protection preserves item in generic selection",()=>{owner.ItemName.Clear();first.QuestInfoReadable=false;MrItemRemover2Settings.Instance.DeleteAllGreen="True";Trigger();Tick(12);Picked(12);});
        Add("legacy explicit manual opening remains available with removal disabled",()=>{owner.ItemName.Clear();owner.OpnList.Add(first.Name);first.IsOpenable=true;MrItemRemover2Settings.Instance.EnableRemove="False";MrItemRemover2Settings.Instance.EnableOpen="True";Trigger();Tick(4);Check(Lua.Uses.Count>0&&Pickups.Count==0,"manual open policy changed with deletion disabled");});
        int pass=0,assertions=0,unexpected=0;
        foreach(var test in cases)
        {
            try{test.Test();pass++;Console.WriteLine("PASS MIR finite scan: "+test.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL MIR finite scan: "+test.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR MIR finite scan: "+test.Name+": "+e);}
        }
        Console.WriteLine($"MIR finite scan scenarios: {pass}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; exact Pulse/CheckForItems/selection/lifecycle methods; controlled inventory, timer, files and Lua; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("MIR finite scan regression");
    }
}
""";
}
