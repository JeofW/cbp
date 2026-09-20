using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Real Pulse, selection/quest guard and deletion methods, with controlled inventory,
// configuration IO and client observations. Recorded requests are not real deletion.
internal static class MrItemRemoverScanRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string folder = Path.Combine(root, "runtime-snapshot", "Plugins", "MrItemRemover2");
        string methods = File.ReadAllText(Path.Combine(folder, "Methods.cs"));
        string plugin = File.ReadAllText(Path.Combine(folder, "MrItemRemover2.cs"));
        string boundary = (string)typeof(MrItemRemoverContextRegressionTests).GetField("Prefix", Hidden)!.GetRawConstantValue()!;
        boundary = boundary.Replace("MirContextCases", "MirScanCases");
        void Replace(string before, string after)
        {
            if (boundary.Split(new[] { before }, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("Controlled scan boundary changed: " + before);
            boundary = boundary.Replace(before, after);
        }
        Replace("using System.Globalization;", "using System.Globalization; using System.Threading;");
        Replace("public virtual void OnDisable(){}", "public virtual void OnDisable(){} public virtual void Pulse(){}");
        Replace("public sealed class TimerProbe { public void Reset(){} }",
            "public sealed class TimerProbe { public TimeSpan TimeLeft=>TimeSpan.FromDays(1); public DateTime EndTime=>DateTime.UtcNow+TimeLeft; public void Reset(){} }");
        Replace("public WoWItem[]? BagItems=Array.Empty<WoWItem>();", "public bool IsDead=>!IsAlive; public WoWItem[]? BagItems=Array.Empty<WoWItem>();");
        Replace("MirScanCases.Pickups++;", "MirScanCases.Pickups++; MirScanCases.Attempted.Add(Guid);");
        Replace("public bool TryPickUp()", """
public int BagSlot=0;
public uint StackCount=1;
public bool IsOpenable,QuestItem;
public bool QuestInfoKnown=true;
public WoWItemQuality Quality=WoWItemQuality.Poor;
public ItemMetadata ItemInfo=new();
public bool TryGetContainerItemQuestInfo(out bool isQuestItem,out int questId,out bool isActive)
{isQuestItem=QuestItem;questId=0;isActive=QuestItem;return QuestInfoKnown;}
public bool TryPickUp()
""");
        Replace("public static bool IsInGame=true;", "public static bool IsInGame=true; public static void SleepForLagDuration(){var f=MirScanCases.OnSleep;MirScanCases.OnSleep=null;f?.Invoke();}");
        Replace("public string EnableRemove=\"True\";", """
public string EnableRemove="True",EnableOpen="False",RemoveFood="False",RemoveDrinks="False",LootCheck="True",
DeleteQuestItems="False",DeleteAllGray="True",DeleteAllWhite="False",DeleteAllGreen="False",DeleteAllBlue="False";
public int GoldGrays,SilverGrays,CopperGrays,Time=5;
""");
        Replace("public static void DoString(string code){}", "public static void DoString(string code){if(code.StartsWith(\"UseItemByName\",StringComparison.Ordinal))MirScanCases.Uses++;}");
        Replace("private void LootEnded(object sender,LuaEventArgs args){}", "");

        string Method(string source, string name, bool optional = false) =>
            (string)typeof(MrItemRemoverContextRegressionTests).GetMethod("Method", Hidden)!
                .Invoke(null, new object[] { source, name, optional })!;
        int start = methods.IndexOf("private static readonly TimeSpan DeleteTimeout", StringComparison.Ordinal);
        int end = methods.IndexOf("public void SellVenderItems", start, StringComparison.Ordinal);
        if (start < 0 || end <= start) throw new InvalidOperationException("Tracked scan/deletion state not found");
        var pieces = new List<string> { methods.Substring(start, end - start) };
        foreach (string name in new[] { "OnEnable", "OnDisable", "Pulse", "LootEnded" }) pieces.Add(Method(plugin, name));
        foreach (string name in new[] { "BeginDelete", "TickPendingDelete", "PendingDeleteItemStillObserved", "ReadOwnedCursorState",
            "TryIssueDeleteRequest", "TryConfirmPendingDelete", "DeleteItemConfirmPopup", "BuildOwnedDeleteRequestLua",
            "BuildOwnedDeleteConfirmationLua", "ResetPendingDelete", "CanDeleteNow", "OwnsPendingDeleteContext",
            "ReleasePendingDelete", "ResetDeleteLifetime", "CheckForItems", "GetTime", "IsQuestItem" })
            pieces.Add(Method(methods, name));
        foreach (string name in new[] { "OwnsItemScan", "EndItemScan", "TryDeleteScannedItem" }) pieces.Add(Method(methods, name, true));
        string temporary = Path.Combine(Path.GetTempPath(), "cb-mir-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), boundary + ExtraFields + string.Join("\n", pieces) + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked MIR scan compilation failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            try { assembly.GetType("MirScanCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }

    private const string ExtraFields = """
private readonly string _removeListPath="remove",_bagListPath="bags";
public List<string> BagList=new(),Combine10List=new(),Combine3List=new(),Combine5List=new(),DrinkList=new(),FoodList=new(),
InventoryList=new(),ItemName=new(),ItemNameSell=new(),KeepList=new(),OpnList=new();
private void LoadList(List<string> list,string path){MirScanCases.ListReads++;}
""";
    private const string Cases = """
}
public enum WoWItemQuality { Poor,Common,Uncommon,Rare }
public sealed class ItemMetadata { public uint BeginQuestId,SellPrice; }
public static class SpellManager { public static TimeSpan GlobalCooldownLeft=>TimeSpan.Zero; }
public static class NumberParsing { public static int ToInt32(this string text)=>int.Parse(text,CultureInfo.InvariantCulture); }
public static class MirScanCases
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message){}
    private static MrItemRemover2 owner=null!;
    private static LocalPlayer player=null!;
    public static int Pickups,Uses,ListReads;
    public static bool PickupResult=true;
    public static Action? OnPickup,OnLog,OnSleep;
    public static Action<string>? ObserveLog;
    public static readonly List<ulong> Attempted=new();
    public static void Log(string format,object[] args)
    {string text=string.Format(format,args);var f=OnLog;OnLog=null;f?.Invoke();ObserveLog?.Invoke(text);}
    private static object? Call(object instance,string name,params object?[] args)
    {
        try{return instance.GetType().GetMethod(name,Hidden)!.Invoke(instance,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static object? Get(string name)=>typeof(MrItemRemover2).GetField(name,Hidden)!.GetValue(owner);
    private static bool Pending=>(bool)typeof(MrItemRemover2).GetProperty("HasPendingDelete",Hidden)!.GetValue(owner)!;
    private static void Reset()
    {
        BotEvents.Clear();Lua.Events.Clear();Lua.Submit=true;Lua.Cursor=0;Lua.DuringRequest=Lua.DuringPopup=null;
        TreeRoot.IsRunning=true;TreeRoot.IsPaused=false;StyxWoW.IsInGame=true;
        var s=MrItemRemover2Settings.Instance;
        s.EnableRemove="True";s.EnableOpen=s.RemoveFood=s.RemoveDrinks=s.DeleteQuestItems=s.DeleteAllWhite=s.DeleteAllGreen=s.DeleteAllBlue="False";
        s.DeleteAllGray="True";s.LootCheck="True";
        OnPickup=OnLog=OnSleep=null;ObserveLog=null;PickupResult=true;
        player=new LocalPlayer{BagItems=new[]{Item(11),Item(12)}};StyxWoW.Me=player;
        owner=new MrItemRemover2();Call(owner,"OnEnable");Pickups=Uses=ListReads=Lua.Requests=Lua.Confirmations=0;Attempted.Clear();
    }
    private static WoWItem Item(ulong guid)=>new(){Guid=guid,Entry=(uint)(guid+100),Name="item"+guid};
    private static void Tick()
    {
        try{Call(owner,"Pulse");}
        catch(NullReferenceException){throw new Failure("controlled missing inventory/player escaped the scan as a null dereference");}
    }
    private static void Manual()=>owner.ManualCheckRequested=true;
    private static void ObservePendingAbsence()
    {
        if(!Pending||!(bool)Get("_pendingDeleteRequested")!)return;
        ulong guid=(ulong)Get("_pendingDeleteGuid")!;
        player.BagItems=player.BagItems!.Where(x=>x.Guid!=guid).ToArray();Lua.Cursor=0;
    }
    private static void Drain(int ticks=24,bool remove=true)
    {for(int i=0;i<ticks;i++){if(remove)ObservePendingAbsence();Tick();}}
    private static void BothVisited()=>Check(Attempted.SequenceEqual(new ulong[]{11,12}),"pass did not visit both original candidates exactly once: "+string.Join(",",Attempted));
    public static void Run()
    {
        var cases=new List<(string Name,Action Test)>();
        void Add(string name,Action test)=>cases.Add((name,()=>{Reset();test();}));
        Add("one manual trigger completes two candidate visits without timer events",()=>{Manual();Drain();BothVisited();Check(Lua.Requests==2,"second request was lost");});
        Add("one loot trigger completes two candidate visits without another loot event",()=>{Lua.Events.Fire("LOOT_CLOSED");Drain();BothVisited();});
        Add("refused first pickup does not starve the second candidate",()=>{PickupResult=false;OnPickup=()=>PickupResult=true;Manual();Drain();BothVisited();Check(Lua.Requests==1,"refused pickup became a delete request");});
        Add("all refused candidates are visited once then the pass stops",()=>{PickupResult=false;Manual();Drain(remove:false);BothVisited();Check(Lua.Requests==0,"refused pass submitted deletion");int reads=ListReads;Drain(remove:false);Check(ListReads==reads,"finished pass kept scanning");});
        Add("items arriving during a pass wait for a fresh trigger",()=>{Manual();Tick();Check(Pending,"first request not admitted");player.BagItems=player.BagItems!.Concat(new[]{Item(13)}).ToArray();Drain();BothVisited();Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{11,12,13}),"new independent pass did not admit the later item");});
        Add("keep-list protection survives multi-item continuation",()=>{owner.KeepList.Add("item11");Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{12})&&player.BagItems!.Any(x=>x.Guid==11),"keep-list item was not preserved");});
        Add("quest-item protection survives multi-item continuation",()=>{player.BagItems![0].QuestItem=true;Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{12}),"quest item was submitted for deletion");});
        Add("unknown quest-item observation preserves the candidate",()=>{player.BagItems![0].QuestInfoKnown=false;Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{12}),"unknown quest status was treated as disposable");});
        Add("listed bags remain excluded",()=>{owner.BagList.Add("item11");Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{12}),"a listed bag was submitted");});
        Add("world loss during the scan blocks new deletion",()=>{OnSleep=()=>StyxWoW.IsInGame=false;Manual();Tick();Check(Pickups==0&&Lua.Requests==0,"lost world still submitted an item");});
        Add("same-GUID replacement player during scan blocks old candidates",()=>{OnSleep=()=>StyxWoW.Me=new LocalPlayer{Guid=player.Guid,BagItems=player.BagItems};Manual();Tick();Check(Pickups==0&&Lua.Requests==0,"old scan borrowed the replacement actor");});
        Add("missing player during scan cannot escape or submit",()=>{OnSleep=()=>StyxWoW.Me=null;Manual();Tick();Check(Pickups==0&&Lua.Requests==0,"missing player submitted an item");});
        Add("stop/start notifications revoke remaining scan candidates",()=>{Manual();Tick();BotEvents.Stop();BotEvents.Start();Lua.Cursor=0;int attempts=Pickups;Drain(remove:false);Check(Pickups==attempts,"old pass survived run restart");});
        Add("fresh manual pass remains usable after run restart",()=>{Manual();Tick();BotEvents.Stop();BotEvents.Start();Lua.Cursor=0;Attempted.Clear();Pickups=Lua.Requests=0;Manual();Drain();BothVisited();});
        Add("empty inventory does not create an endless scan",()=>{player.BagItems=Array.Empty<WoWItem>();Manual();Tick();int reads=ListReads;Drain();Check(Pickups==0&&ListReads==reads,"empty pass kept rescheduling itself");});
        Add("manual opening remains available with deletion disabled",()=>{MrItemRemover2Settings.Instance.EnableRemove="False";MrItemRemover2Settings.Instance.EnableOpen="True";owner.OpnList.Add("item11");player.BagItems![0].IsOpenable=true;Manual();Drain(remove:false);Check(Uses==2&&Pickups==0,"opening rules changed or disabled deletion was submitted");});
        Add("explicit removal list still respects disabled deletion",()=>{MrItemRemover2Settings.Instance.EnableRemove="False";owner.ItemName.Add("item11");Manual();Drain(remove:false);Check(Pickups==0,"disabled deletion used the explicit list");});
        Add("reordering remaining inventory does not lose its candidate GUIDs",()=>{player.BagItems=new[]{Item(11),Item(12),Item(13)};Manual();Tick();ObservePendingAbsence();player.BagItems=player.BagItems!.Reverse().ToArray();Drain();Check(Attempted.Count==3&&Attempted.Distinct().Count()==3&&Attempted.Contains(12)&&Attempted.Contains(13),"reordered pass skipped or repeated a candidate");});
        Add("returned first item does not starve later candidates or loop",()=>{Manual();Tick();Lua.Cursor=0;Tick();Drain();BothVisited();Check(player.BagItems!.Any(x=>x.Guid==11),"returned item was silently resubmitted in the same pass");});
        Add("unknown inventory between candidates is not acted on",()=>{Manual();Tick();ObservePendingAbsence();Tick();player.BagItems=null;Tick();Check(Pickups==1,"unknown inventory submitted another candidate");player.BagItems=new[]{Item(12)};Manual();Drain();Check(Attempted.SequenceEqual(new ulong[]{11,12}),"fresh inventory/pass failed to recover");});
        Add("opening is not repeated while later deletions continue",()=>{player.BagItems=new[]{Item(10),Item(11),Item(12)};player.BagItems[0].Quality=WoWItemQuality.Rare;player.BagItems[0].IsOpenable=true;owner.OpnList.Add("item10");MrItemRemover2Settings.Instance.EnableOpen="True";Manual();Drain();BothVisited();Check(Uses==2,"one candidate was opened repeatedly during continuation");});
        Add("run replacement during removal diagnostic revokes the old scan",()=>{ObserveLog=text=>{if(!text.Contains("Removing.",StringComparison.Ordinal))return;ObserveLog=null;BotEvents.Stop();BotEvents.Start();};Manual();Tick();Check(Pickups==0&&Lua.Requests==0,"old diagnostic continuation admitted a new-run deletion");});
        Add("loot trigger waits through combat without being discarded",()=>{player.Combat=true;Lua.Events.Fire("LOOT_CLOSED");Tick();Check(Pickups==0&&ListReads==0,"combat performed scan work");player.Combat=false;Drain();BothVisited();});
        Add("manual trigger waits through pause without being discarded",()=>{TreeRoot.IsPaused=true;Manual();Tick();Check(Pickups==0&&ListReads==0,"pause performed scan work");TreeRoot.IsPaused=false;Drain();BothVisited();});
        Add("manual trigger waits through casting without being discarded",()=>{player.IsCasting=true;Manual();Tick();Check(Pickups==0&&ListReads==0,"casting performed scan work");player.IsCasting=false;Drain();BothVisited();});
        Add("remaining scan resumes after combat without a second trigger",()=>{Manual();Tick();ObservePendingAbsence();Tick();player.Combat=true;Tick();Check(Pickups==1,"combat submitted another candidate");player.Combat=false;Drain();BothVisited();});
        Add("remaining scan resumes after pause without a second trigger",()=>{Manual();Tick();ObservePendingAbsence();Tick();TreeRoot.IsPaused=true;Tick();Check(Pickups==1,"pause submitted another candidate");TreeRoot.IsPaused=false;Drain();BothVisited();});
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try{c.Test();passed++;Console.WriteLine("PASS MIR scan: "+c.Name);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL MIR scan: "+c.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR MIR scan: "+c.Name+": "+e);}
        }
        Console.WriteLine($"MIR finite scan scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; tracked pulse/scan/protection/lifecycle; controlled inventory and client requests; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("MIR finite scan regression");
    }
    private static void Check(bool condition,string reason){if(!condition)throw new Failure(reason);}
}
""";

    private static string Root()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
}
