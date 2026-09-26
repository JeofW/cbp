using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Exact tracked owner methods including acknowledgement/return/restore, which
// earlier context fixtures substituted. Controlled managed/Lua boundaries only;
// this does not establish physical cursor or popup identity in the client.
internal static class EquipItemContinuationBoundaryRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root, "runtime-snapshot", "Quest Behaviors", "EquipItem.cs"));
        string methods = string.Join("\n", new[]
        {
            "private RunStatus TickPendingEquip()", "private bool CanEquipNow()",
            "private bool OwnsPendingEquipContext()", "private bool SubmitOwnedCursorEquip()",
            "private void ConfirmOwnedEquipPopup()", "private bool IsPendingEquipAcknowledged()",
            "private bool ReturnDisplacedCursorToSource()", "private void RestoreOwnedCursorToSource()",
            "private static bool CursorHasAnyItem()", "private void ResetPendingEquip()"
        }.Select(marker => Method(source, marker)));
        string directory = Path.Combine(Path.GetTempPath(), "cb-equip-continuation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + methods + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked continuation compile: " + string.Join("; ", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("ContinuationProbe", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Method(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing tracked method " + marker);
        int brace = source.IndexOf('{', start), depth = 0;
        // Balanced braces in these selected format strings; copy unchanged bytes.
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("Unclosed tracked method " + marker);
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
using System.Linq;
public enum InventorySlot { None=0, HeadSlot=1 }
public enum RunStatus { Success, Failure, Running }
public sealed class WoWItem
{
    public uint Entry=100; public ulong Guid=200; public bool IsValid=true;
    public bool TryPickUp(out int bag,out int slot){bag=0;slot=1;return true;}
}
public sealed class Equipment { public WoWItem[] Items=new[]{new WoWItem()}; }
public sealed class Inventory { public Equipment Equipped=new Equipment(); }
public sealed class LocalPlayer
{
    public ulong Guid=7; public bool IsValid=true,IsAlive=true,IsGhost,Combat;
    public List<WoWItem> CarriedItems=new List<WoWItem>{new WoWItem()};
    public Inventory Inventory { get { InventoryReads++; return inventory; } }
    private readonly Inventory inventory=new Inventory();
    public static int InventoryReads;
}
public static class StyxWoW { public static LocalPlayer Me=new LocalPlayer();public static bool IsInGame=true; }
public static class TreeRoot { public static bool IsRunning=true; }
public static class Lua
{
    public static readonly List<string> Requests=new List<string>();
    public static Action DuringRequest;
    private static void Record(string script)
    {
        Requests.Add(script);var change=DuringRequest;DuringRequest=null;change?.Invoke();
    }
    public static void DoString(string format,params object[] args){Record(args.Length==0?format:string.Format(format,args));}
    public static T GetReturnVal<T>(string script,uint index){Record(script);return (T)(object)true;}
}
public sealed class ContinuationProbe
{
    private sealed class Failure(string message):Exception(message) { }
    private bool _isBehaviorDone,_isDisposed,_pendingEquipSubmitted;
    private ulong _pendingEquipGuid,_pendingEquipPlayerGuid;
    private uint _pendingEquipEntry;
    private LocalPlayer _pendingEquipPlayer;
    private InventorySlot _pendingEquipSlot=InventorySlot.None;
    private int _pendingSourceBag=-1,_pendingSourceSlot=-1;
    private DateTime _pendingEquipSince;
    private static readonly TimeSpan EquipTimeout=TimeSpan.FromSeconds(10);
    private int ItemId=>100;
    private int QuestId=>900001;
    private int QuestRequirementInLog=>1;
    private int QuestRequirementComplete=>0;
    private InventorySlot Slot=InventorySlot.HeadSlot;
    private bool QuestAllowed=true;
    private bool HasPendingEquip=>_pendingEquipGuid!=0&&_pendingEquipEntry!=0;
    private bool UtilIsProgressRequirementsMet(int quest,int inLog,int complete)=>QuestAllowed;
    private void LogMessage(string level,string format,params object[] args){}
""";

    private const string Suffix = """
    private void Change(string state)
    {
        switch(state)
        {
            case "stopped":TreeRoot.IsRunning=false;break;
            case "combat":StyxWoW.Me.Combat=true;break;
            case "dead":StyxWoW.Me.IsAlive=false;break;
            case "ghost":StyxWoW.Me.IsGhost=true;break;
            case "invalid":StyxWoW.Me.IsValid=false;break;
            case "world":StyxWoW.IsInGame=false;break;
            case "replacement":StyxWoW.Me=new LocalPlayer();break;
            case "guid":StyxWoW.Me.Guid=8;break;
            case "missing":StyxWoW.Me=null;break;
            case "disposed":_isDisposed=true;break;
            case "finished":_isBehaviorDone=true;break;
            case "quest":QuestAllowed=false;break;
        }
    }
    private static ContinuationProbe Fresh()
    {
        StyxWoW.Me=new LocalPlayer();StyxWoW.IsInGame=true;TreeRoot.IsRunning=true;
        Lua.Requests.Clear();Lua.DuringRequest=null;LocalPlayer.InventoryReads=0;
        var owner=new ContinuationProbe();owner.TickPendingEquip();
        Check(owner.HasPendingEquip&&owner._pendingEquipSubmitted,"initial admission failed");
        Lua.Requests.Clear();LocalPlayer.InventoryReads=0;
        return owner;
    }
    public static void Run()
    {
        int passed=0,assertions=0,unexpected=0,total=0;
        void Case(string name,Action test)
        {
            total++;
            try { test();passed++;Console.WriteLine("PASS equip continuation: "+name); }
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL equip continuation: "+name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR equip continuation: "+name+": "+e);}
        }
        foreach(string state in new[]{"active","combat","stopped","dead","ghost","invalid","world","replacement","guid","missing","disposed","finished","quest"})
        foreach(string operation in new[]{"acknowledge","return","restore"})
            Case(state+"/"+operation,()=>
            {
                var owner=Fresh();owner.Change(state);
                bool valid=state=="active"||state=="combat";
                if(operation=="acknowledge")
                {
                    Check(owner.IsPendingEquipAcknowledged()==valid,"acknowledgement accepted a revoked owner");
                    Check((LocalPlayer.InventoryReads>0)==valid,"revoked owner inspected equipment");
                    Check(Lua.Requests.Count==0,"acknowledgement mutated cursor state");
                }
                else
                {
                    if(operation=="return")Check(owner.ReturnDisplacedCursorToSource()==valid,"return reported success after revocation");
                    else owner.RestoreOwnedCursorToSource();
                    Check(Lua.Requests.Count==(valid?1:0),"cursor request crossed revoked actor/runtime/quest boundary");
                }
            });
        foreach(string state in new[]{"stopped","replacement","quest","world","disposed"})
            Case(state+" during confirmation",()=>
            {
                var owner=Fresh();Lua.DuringRequest=()=>owner.Change(state);owner.TickPendingEquip();
                Check(Lua.Requests.Count==1,"cursor-return request followed revocation during confirmation");
                Check(LocalPlayer.InventoryReads==0,"equipment was observed after confirmation revoked context");
                Check(!owner.HasPendingEquip&&!owner._isBehaviorDone,"revoked work was retained or marked completed");
            });
        Case("active acknowledged transaction completes",()=>
        {
            var owner=Fresh();owner.TickPendingEquip();
            Check(owner._isBehaviorDone&&!owner.HasPendingEquip&&Lua.Requests.Count==2,"valid acknowledgement/return lost normal completion");
        });
        Case("stop during confirmation allows later readmission",()=>
        {
            var owner=Fresh();Lua.DuringRequest=()=>owner.Change("stopped");owner.TickPendingEquip();
            TreeRoot.IsRunning=true;Lua.Requests.Clear();owner.TickPendingEquip();
            Check(!owner._isBehaviorDone&&owner.HasPendingEquip&&Lua.Requests.Count==1,"revocation became permanent completion instead of fresh readmission");
        });
        Console.WriteLine($"Equip continuation boundary scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact tracked C# tick/context/acknowledge/return/restore; controlled actor/inventory/Lua; no physical cursor or client proof.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Equip continuation boundary regression");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
""";
}
