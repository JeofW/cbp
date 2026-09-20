using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

// Execute unchanged tracked pending/observation method bodies. Inventory, cursor,
// logging and final requests are controlled boundaries; no native deletion occurs.
internal static class DeleteInventoryObservationRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root, "runtime-snapshot", "Plugins", "MrItemRemover2", "Methods.cs"));
        string tick = Method(source, @"private\s+static\s+void\s+TickPendingDelete\s*\(");
        string observation = Method(source, @"private\s+static\s+bool\??\s+PendingDeleteItemStillObserved\s*\(");
        string directory = Path.Combine(Path.GetTempPath(), "cb-delete-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + tick + "\n" + observation + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", Hidden)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked delete observation compile failed: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            try { assembly.GetType("DeleteObservationCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Method(string source, string pattern)
    {
        Match match = Regex.Match(source, pattern);
        if (!match.Success) throw new InvalidOperationException("Tracked declaration missing: " + pattern);
        int brace = source.IndexOf('{', match.Index), depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(match.Index, i - match.Index + 1);
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
using System.IO;
using System.Linq;
public static class DeleteObservationCases
{
    private sealed class Failure(string message) : Exception(message) { }
    private sealed class ControlledReadFailure : IOException { }
    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(10);
    private static ulong _pendingDeleteGuid;
    private static uint _pendingDeleteEntry;
    private static DateTime _pendingDeleteSince;
    private static bool _pendingDeleteRequested;
    private static bool HasPendingDelete => _pendingDeleteGuid != 0 && _pendingDeleteEntry != 0;
    private static LocalPlayer Me => Player;
    private static LocalPlayer Player = null!;
    private static WoWItem? Candidate;
    private static int Cursor, Resets, Requests, Confirms;
    private static Action? DuringLookup;
    private static readonly List<string> Logs = new();
    private sealed class WoWItem { public ulong Guid; public bool IsValid = true; }
    private sealed class LocalPlayer
    {
        public ulong Guid = 7;
        public bool IsValid = true;
        public WoWItem[]? Items = Array.Empty<WoWItem>();
        public bool ThrowRead;
        public Action? DuringRead;
        public WoWItem[]? BagItems
        {
            get
            {
                if (ThrowRead) throw new ControlledReadFailure();
                var action = DuringRead; DuringRead = null; action?.Invoke();
                return Items;
            }
        }
    }
    private static class ObjectManager
    {
        public static T? GetObjectByGuid<T>(ulong guid) where T : class
        {
            var action = DuringLookup; DuringLookup = null; action?.Invoke();
            return Candidate as T;
        }
    }
    private static int ReadOwnedCursorState(uint entry) => Cursor;
    private static void TryIssueDeleteRequest() { Requests++; }
    private static int TryConfirmPendingDelete() { Confirms++; return 1; }
    private static void Slog(string format, params object[] args) => Logs.Add(string.Format(format, args));
    private static void Dlog(string format, params object[] args) => Logs.Add(string.Format(format, args));
    private static void ResetPendingDelete()
    {
        Resets++; _pendingDeleteGuid = 0; _pendingDeleteEntry = 0; _pendingDeleteRequested = false;
    }
""";

    private const string Cases = """
    public static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        void Add(string name, Action test) => cases.Add((name, () => { Reset(); test(); }));
        Add("no pending operation is a no-op", () => { _pendingDeleteGuid=0; Tick(); Check(Resets==0&&Logs.Count==0,"idle state changed"); });
        Add("requested observed absence ends only the local pending state", () => { Tick(); Check(Resets==1&&!HasPendingDelete,"observed absence never released local pending state"); });
        Add("present item is returned inventory, not a completed deletion", () => { Player.Items=new[]{new WoWItem{Guid=11}}; Tick(); Check(Resets==1&&Logs.Any(x=>x.Contains("returned to inventory")),"present item was not retained as returned"); NoConfirmedDeletion(); });
        Add("known expected cursor services existing confirmation", () => { Cursor=1; Tick(); Check(Confirms==1&&Resets==0,"valid confirmation continuation changed"); });
        Add("known expected cursor without request can retry the request", () => { Cursor=1; _pendingDeleteRequested=false; Tick(); Check(Requests==1&&Confirms==0,"request and confirmation were conflated"); });
        Add("foreign cursor is not consumed", () => { Cursor=2; Tick(); Unresolved(); });
        Add("missing player is unknown, not removed", () => { Player=null!; Tick(); Unresolved(); });
        Add("invalid player is unknown, not removed", () => { Player.IsValid=false; Tick(); Unresolved(); });
        Add("missing inventory collection is unknown", () => { Player.Items=null; Tick(); Unresolved(); });
        Add("inventory read failure remains pending", () => { Player.ThrowRead=true; Tick(); Unresolved(); });
        Add("null inventory member prevents an absence claim", () => { Player.Items=new WoWItem[]{null!}; Tick(); Unresolved(); });
        Add("invalid inventory member prevents an absence claim", () => { Player.Items=new[]{new WoWItem{Guid=99,IsValid=false}}; Tick(); Unresolved(); });
        Add("invalid looked-up candidate is unknown", () => { Candidate=new WoWItem{Guid=11,IsValid=false}; Tick(); Unresolved(); });
        Add("disappearance without local request is not confirmed deletion", () => { _pendingDeleteRequested=false; Tick(); NoConfirmedDeletion(); Check(Resets==1,"unrequested disappearance did not revoke pending intent"); });
        Add("unknown observation can recover on a later tick", () => { Player.Items=null; Tick(); Unresolved(); Player.Items=Array.Empty<WoWItem>(); Tick(); Check(Resets==1,"fresh known observation did not recover"); });
        Add("replacement player during inventory read cannot complete old observation", () => { Player.DuringRead=()=>Player=new LocalPlayer(); Tick(); Unresolved(); });
        Add("replacement transaction during lookup is not reset by old observation", () => { DuringLookup=()=>{_pendingDeleteGuid=33;_pendingDeleteEntry=44;}; Tick(); Check(Resets==0&&_pendingDeleteGuid==33,"old observation reset replacement transaction"); NoConfirmedDeletion(); });
        Add("existing timeout still releases unknown observations", () => { Player.ThrowRead=true; _pendingDeleteSince=DateTime.UtcNow-TimeSpan.FromSeconds(11); Tick(); Check(Resets==1&&Logs.Any(x=>x.Contains("timed out")),"timeout was lost"); NoConfirmedDeletion(); });
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try { c.Test(); passed++; Console.WriteLine("PASS delete inventory observation: "+c.Name); }
            catch(Failure e) { assertions++; Console.Error.WriteLine("FAIL delete inventory observation: "+c.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR delete inventory observation: "+c.Name+": "+e); }
        }
        Console.WriteLine($"Delete inventory observation scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; exact tracked pending and observation methods; controlled inventory/cursor; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException("Delete inventory observation regression");
    }
    private static void Reset()
    {
        Player=new LocalPlayer(); Candidate=null; DuringLookup=null;
        Cursor=Resets=Requests=Confirms=0; Logs.Clear();
        _pendingDeleteGuid=11; _pendingDeleteEntry=22; _pendingDeleteRequested=true;
        _pendingDeleteSince=DateTime.UtcNow;
    }
    private static void Tick()
    {
        try { TickPendingDelete(); }
        catch(ControlledReadFailure) { throw new Failure("the deliberately injected inventory read error escaped pending observation"); }
    }
    private static void Unresolved()
    {
        Check(HasPendingDelete&&Resets==0&&Requests==0&&Confirms==0,"unknown/foreign observation consumed pending state or submitted a request");
        NoConfirmedDeletion();
    }
    private static void NoConfirmedDeletion() => Check(!Logs.Any(x=>x.Contains("Confirmed removal",StringComparison.Ordinal)),"observation was reported as confirmed deletion");
    private static void Check(bool value,string reason) { if(!value) throw new Failure(reason); }
}
""";
}
