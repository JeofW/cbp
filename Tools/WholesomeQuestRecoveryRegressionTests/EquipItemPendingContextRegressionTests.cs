using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Compile the exact tracked EquipItem tick, submit, confirm and reset methods.
// Current player/quest/runtime and Lua are controlled boundaries, not a game.
internal static class EquipItemPendingContextRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [ModuleInitializer]
    internal static void Run()
    {
        string source = File.ReadAllText(Path.Combine(Root(), "runtime-snapshot", "Quest Behaviors", "EquipItem.cs"));
        string methods = string.Join("\n", new[]
        {
            "private RunStatus TickPendingEquip()", "private bool SubmitOwnedCursorEquip()",
            "private void ResetPendingEquip()"
        }.Select(marker => Method(source, marker)));
        foreach (string optional in new[] { "private bool CanEquipNow()", "private bool OwnsPendingEquipContext()" })
            if (source.Contains(optional, StringComparison.Ordinal)) methods += "\n" + Method(source, optional);
        string directory = Path.Combine(Path.GetTempPath(), "cb-quest-equip-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + methods + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked EquipItem context compile failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            try { assembly.GetType("EquipItemContextProbe", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Method(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Tracked declaration missing: " + marker);
        int brace = source.IndexOf('{', start), depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("Unterminated tracked method");
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
public sealed class LocalPlayer
{
    public ulong Guid=7;
    public bool IsValid=true,IsAlive=true,IsGhost,Combat;
    public List<WoWItem> CarriedItems=new List<WoWItem>{new WoWItem()};
}
public sealed class WoWItem
{
    public uint Entry=100;
    public ulong Guid=200;
    public bool IsValid=true;
    public static int Pickups;
    public static bool PickupAllowed=true;
    public static Action DuringPickup;
    public bool TryPickUp(out int bag,out int slot)
    {
        Pickups++;bag=0;slot=1;
        var action=DuringPickup;DuringPickup=null;action?.Invoke();
        return PickupAllowed;
    }
}
public static class StyxWoW { public static LocalPlayer Me=new LocalPlayer();public static bool IsInGame=true; }
public static class TreeRoot { public static bool IsRunning=true; }
public static class Lua
{
    public static int Requests;
    public static bool TryEquipCursorItem(ulong guid,uint entry,int slot){Requests++;return true;}
    public static void DoString(string format,params object[] args){Requests++;}
    public static T GetReturnVal<T>(string script,uint index){Requests++;return typeof(T)==typeof(int)?(T)(object)1:(T)(object)true;}
}
public sealed class EquipItemContextProbe
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
    private int Restores;
    private bool UtilIsProgressRequirementsMet(int quest,int inLog,int complete)=>QuestAllowed;
    private bool IsPendingEquipAcknowledged()=>false;
    private bool ReturnDisplacedCursorToSource()=>false;
    private void RestoreOwnedCursorToSource(){Restores++;}
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
    private static EquipItemContextProbe Fresh()
    {
        StyxWoW.Me=new LocalPlayer();StyxWoW.IsInGame=true;TreeRoot.IsRunning=true;
        Lua.Requests=0;WoWItem.Pickups=0;WoWItem.PickupAllowed=true;WoWItem.DuringPickup=null;
        return new EquipItemContextProbe();
    }
    private void Tick()
    {
        try { TickPendingEquip(); }
        catch(NullReferenceException) when(StyxWoW.Me==null)
        { throw new Failure("missing current player was dereferenced instead of rejecting admission"); }
    }
    public static void Run()
    {
        int passed=0,assertions=0,unexpected=0,total=0;
        void Case(string name,Action test)
        {
            total++;
            try { test();passed++;Console.WriteLine("PASS EquipItem context: "+name); }
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL EquipItem context: "+name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR EquipItem context: "+name+": "+e);}
        }
        foreach(string state in new[]{"active","combat","stopped","dead","ghost","invalid","world","replacement","guid","missing","disposed","finished","quest"})
        foreach(string operation in new[]{"tick","submit"})
        {
            Case(state+"/"+operation,()=>
            {
                var owner=Fresh();owner.Tick();
                Check(owner.HasPendingEquip&&owner._pendingEquipSubmitted,"valid initial explicit-slot admission failed");
                Lua.Requests=0;owner.Change(state);
                if(operation=="tick")owner.Tick();
                else owner.SubmitOwnedCursorEquip();
                bool valid=state=="active"||state=="combat";
                Check(Lua.Requests==(valid&&operation=="submit"?1:0),"request did not retain actor/runtime/quest admission");
                if(operation=="tick")Check(owner.HasPendingEquip==valid,"revoked pending identity was retained");
                Check(owner.Restores==0,"context revocation tried to restore a cursor");
            });
        }
        foreach(string state in new[]{"active","combat","stopped","dead","ghost","invalid","world","missing","disposed","finished","quest"})
        foreach(bool byName in new[]{false,true})
        {
            Case(state+"/begin/"+(byName?"by-name":"explicit-slot"),()=>
            {
                var owner=Fresh();if(byName)owner.Slot=InventorySlot.None;owner.Change(state);owner.Tick();
                bool valid=state=="active"||state=="combat";
                Check(Lua.Requests==(valid?1:0)&&owner.HasPendingEquip==valid,"invalid new transaction was admitted");
                Check(WoWItem.Pickups==(valid&&!byName?1:0),"invalid context crossed item pickup boundary");
            });
        }
        foreach(string change in new[]{"replacement","quest"})
            Case(change+" during pickup",()=>
            {
                var owner=Fresh();WoWItem.DuringPickup=()=>owner.Change(change);owner.Tick();
                Check(Lua.Requests==0,"setup callback revoked ownership but equip request still ran");
                owner.Tick();Check(!owner.HasPendingEquip&&owner.Restores==0,"revoked transaction was not released safely");
            });
        Case("refused pickup stays unsubmitted",()=>
        {
            var owner=Fresh();WoWItem.PickupAllowed=false;owner.Tick();
            Check(!owner.HasPendingEquip&&Lua.Requests==0,"refused pickup retained a pending equip request");
        });
        Case("expired stopped operation does not restore foreign runtime state",()=>
        {
            var owner=Fresh();owner.Tick();Lua.Requests=0;owner._pendingEquipSince=DateTime.UtcNow-TimeSpan.FromSeconds(11);owner.Change("stopped");owner.Tick();
            Check(!owner.HasPendingEquip&&owner.Restores==0&&Lua.Requests==0,"timeout cleanup ran after runtime ownership was lost");
        });
        Case("active pending deadline is retained",()=>
        {
            var owner=Fresh();owner.Tick();owner._pendingEquipSince=DateTime.UtcNow-TimeSpan.FromSeconds(11);owner.Tick();
            Check(!owner.HasPendingEquip&&owner.Restores==1,"existing active deadline behavior changed");
        });
        Case("stopped same instance can begin a fresh transaction after restart",()=>
        {
            var owner=Fresh();owner.Tick();owner.Change("stopped");owner.Tick();
            Check(!owner.HasPendingEquip,"stop did not revoke pending state");
            TreeRoot.IsRunning=true;Lua.Requests=0;owner.Tick();
            Check(owner.HasPendingEquip&&Lua.Requests==1,"transient stop became permanent quest completion");
        });
        Console.WriteLine($"EquipItem pending context scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact tracked C# tick/submit/confirm/reset; controlled actor/quest/Lua; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("EquipItem pending context regression");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
""";
}
