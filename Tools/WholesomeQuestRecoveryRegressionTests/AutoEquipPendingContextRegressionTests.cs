using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Exact tracked capture, pending tick, submission, confirmation and reset methods.
// Actor/runtime and Lua boundaries are controlled; no game or cursor is attached.
internal static class AutoEquipPendingContextRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string source = File.ReadAllText(Path.Combine(Root(), "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs"));
        var markers = new[]
        {
            "private void BeginEquip(", "private void TickPendingEquip()",
            "private bool SubmitOwnedCursorEquip()", "private void ConfirmOwnedEquipPopup()",
            "private void ResetPendingEquip()"
        };
        string methods = string.Join("\n", markers.Select(m => Method(source, m)));
        foreach (string optional in new[] { "private bool CanEquipNow()", "private bool OwnsPendingEquipContext()" })
            if (source.Contains(optional, StringComparison.Ordinal)) methods += "\n" + Method(source, optional);
        string directory = Path.Combine(Path.GetTempPath(), "cb-equip-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + methods + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var results = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = results.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked context compile: " + string.Join(";", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            MethodInfo execute = assembly.GetType("AutoEquipContextProbe", true)!.GetMethod("Execute")!;
            foreach (string state in new[] { "active", "stopped", "combat", "dead", "ghost", "invalid", "world", "battleground", "replacement", "guid", "missing", "disposed" })
            foreach (string operation in new[] { "tick", "confirm", "submit" })
            {
                total++;
                try
                {
                    int[] actual = (int[])execute.Invoke(null, new object[] { state, operation })!;
                    int expectedRequests = state == "active" ? 1 : 0;
                    if (actual[0] != expectedRequests)
                        throw new Failure("Lua requests=" + actual[0] + "; expected=" + expectedRequests);
                    if (operation == "tick" && actual[1] != (state == "active" ? 1 : 0))
                        throw new Failure("invalid pending context was not revoked");
                    if (actual[2] != 0) throw new Failure("context revocation attempted cursor restoration");
                    passed++; Console.WriteLine("PASS AutoEquip context: " + state + "/" + operation);
                }
                catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL AutoEquip context: " + state + "/" + operation + ": " + error.Message); }
                catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR AutoEquip context: " + state + "/" + operation + ": " + error); }
            }
            foreach (string state in new[] { "active", "stopped", "combat", "dead", "ghost", "invalid", "world", "battleground", "missing", "disposed" })
            {
                total++;
                try
                {
                    int[] actual = (int[])execute.Invoke(null, new object[] { state, "begin" })!;
                    if (actual[0] != (state == "active" ? 1 : 0) || actual[1] != (state == "active" ? 1 : 0))
                        throw new Failure("invalid context admitted pickup/equip");
                    passed++; Console.WriteLine("PASS AutoEquip context: " + state + "/begin");
                }
                catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL AutoEquip context: " + state + "/begin: " + error.Message); }
                catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR AutoEquip context: " + state + "/begin: " + error); }
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"AutoEquip pending context scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; exact tracked C# capture/tick/request methods; controlled world/Lua; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("AutoEquip pending context regression");
    }

    private const string Prefix = """
using System;
using System.Collections.Generic;
public enum InventorySlot { None=0, MainHandSlot=16 }
public sealed class LocalPlayer
{
    public ulong Guid=7;public bool IsValid=true,IsAlive=true,Combat,IsGhost;
}
public static class ObjectManager { public static LocalPlayer Me=new LocalPlayer(); }
public static class StyxWoW { public static bool IsInGame=true; }
public static class TreeRoot { public static bool IsRunning=true; }
public static class Battlegrounds { public static bool IsInsideBattleground=false; }
public sealed class WoWItem
{
    public bool IsValid=>true;public ulong Guid=>200;public uint Entry=>100;
    public bool TryPickUp(out int bag,out int slot){bag=0;slot=1;return true;}
}
public static class Lua
{
    public static int Requests;
    public static void DoString(string format,params object[] args){Requests++;}
    public static T GetReturnVal<T>(string script,uint index){Requests++;return (T)(object)true;}
}
public sealed class AutoEquipContextProbe
{
    private bool _isDisposed,_pendingEquipSubmitted;
    private ulong _pendingEquipGuid,_pendingEquipPlayerGuid;
    private uint _pendingEquipEntry;
    private LocalPlayer _pendingEquipPlayer;
    private InventorySlot _pendingEquipSlot=InventorySlot.None;
    private int _pendingSourceBag=-1,_pendingSourceSlot=-1;
    private DateTime _pendingEquipSince;
    private static readonly TimeSpan EquipTimeout=TimeSpan.FromSeconds(10);
    private bool HasPendingEquip=>_pendingEquipGuid!=0&&_pendingEquipEntry!=0;
    private bool IsPendingEquipAcknowledged()=>false;
    private bool ReturnDisplacedCursorToSource()=>false;
    private int Restores;
    private void RestoreOwnedCursorToSource(){Restores++;}
    private void Log(string format,params object[] args){}
    private void LogDebug(string format,params object[] args){}
""";
    private const string Suffix = """
    private void Change(string state)
    {
        switch(state)
        {
            case "stopped":TreeRoot.IsRunning=false;break;
            case "combat":ObjectManager.Me.Combat=true;break;
            case "dead":ObjectManager.Me.IsAlive=false;break;
            case "ghost":ObjectManager.Me.IsGhost=true;break;
            case "invalid":ObjectManager.Me.IsValid=false;break;
            case "world":StyxWoW.IsInGame=false;break;
            case "battleground":Battlegrounds.IsInsideBattleground=true;break;
            case "replacement":ObjectManager.Me=new LocalPlayer();break;
            case "guid":ObjectManager.Me.Guid=8;break;
            case "missing":ObjectManager.Me=null;break;
            case "disposed":_isDisposed=true;break;
        }
    }
    public static int[] Execute(string state,string operation)
    {
        ObjectManager.Me=new LocalPlayer();StyxWoW.IsInGame=true;TreeRoot.IsRunning=true;Battlegrounds.IsInsideBattleground=false;
        var owner=new AutoEquipContextProbe();Lua.Requests=0;
        if(operation=="begin")
        {
            owner.Change(state);owner.BeginEquip(new WoWItem(),InventorySlot.MainHandSlot,false);
        }
        else
        {
            owner.BeginEquip(new WoWItem(),InventorySlot.MainHandSlot,false);
            if(!owner.HasPendingEquip||!owner._pendingEquipSubmitted)throw new InvalidOperationException("valid admission failed before controlled mutation");
            Lua.Requests=0;owner.Change(state);
            if(operation=="tick")owner.TickPendingEquip();
            else if(operation=="confirm")owner.ConfirmOwnedEquipPopup();
            else owner.SubmitOwnedCursorEquip();
        }
        return new[]{Lua.Requests,owner.HasPendingEquip?1:0,owner.Restores};
    }
}
""";
    private static string Method(string source, string marker)
    {
        int start=source.IndexOf(marker,StringComparison.Ordinal);
        if(start<0)throw new InvalidOperationException("Missing tracked method "+marker);
        int brace=source.IndexOf('{',start),depth=0;
        for(int i=brace;i<source.Length;i++)
        {
            if(source[i]=='{')depth++;
            else if(source[i]=='}'&&--depth==0)return source.Substring(start,i-start+1);
        }
        throw new InvalidOperationException("Unclosed tracked method "+marker);
    }
    private static string Root()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
}
