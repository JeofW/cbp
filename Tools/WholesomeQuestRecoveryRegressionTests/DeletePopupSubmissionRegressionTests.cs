using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Executes tracked C# admission and callbacks. Lua is a recording boundary,
// not a simulated client, popup implementation or physical cursor proof.
internal static class DeletePopupSubmissionRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Plugins/MrItemRemover2/Methods.cs"));
        string[] markers =
        {
            "private static void TryIssueDeleteRequest()",
            "private static int TryConfirmPendingDelete()",
            "private static void DeleteItemConfirmPopup(object sender, LuaEventArgs args)",
            "private static string BuildOwnedDeleteRequestLua(uint expectedEntry)",
            "private static string BuildOwnedDeleteConfirmationLua(uint expectedEntry)",
            "private static void ResetPendingDelete()"
        };
        string directory = Path.Combine(Path.GetTempPath(), "cb-delete-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"),
                Prefix + string.Join("\n", markers.Select(marker => Method(source, marker))) + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException("Tracked delete admission compilation: " + string.Join(";", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            MethodInfo execute = assembly.GetType("DeleteRequestProbe", true)!.GetMethod("Execute")!;
            foreach (var c in new[]
            {
                (Name: "no pending identity", Guid: 0UL, Entry: 0U, Actions: "C", Calls: 0),
                (Name: "missing guid", Guid: 0UL, Entry: 100U, Actions: "SC", Calls: 0),
                (Name: "missing entry", Guid: 200UL, Entry: 0U, Actions: "SE", Calls: 0),
                (Name: "pending intent direct confirmation", Guid: 200UL, Entry: 100U, Actions: "C", Calls: 0),
                (Name: "pending intent popup callback", Guid: 200UL, Entry: 100U, Actions: "E", Calls: 0),
                (Name: "refused request direct confirmation", Guid: 200UL, Entry: 100U, Actions: "FC", Calls: 0),
                (Name: "refused request popup callback", Guid: 200UL, Entry: 100U, Actions: "FE", Calls: 0),
                (Name: "throwing request direct confirmation", Guid: 200UL, Entry: 100U, Actions: "XC", Calls: 0),
                (Name: "throwing request popup callback", Guid: 200UL, Entry: 100U, Actions: "XE", Calls: 0),
                (Name: "repeated refused requests", Guid: 200UL, Entry: 100U, Actions: "FEFE", Calls: 0),
                (Name: "successful request direct confirmation", Guid: 200UL, Entry: 100U, Actions: "SC", Calls: 1),
                (Name: "successful request popup callback", Guid: 200UL, Entry: 100U, Actions: "SE", Calls: 1),
                (Name: "successful retry after refusal", Guid: 200UL, Entry: 100U, Actions: "FSE", Calls: 1),
                (Name: "reset revokes confirmed request", Guid: 200UL, Entry: 100U, Actions: "SRE", Calls: 0)
            })
            {
                total++;
                try
                {
                    string[] actual = (string[])execute.Invoke(Activator.CreateInstance(execute.DeclaringType!), new object[] { c.Guid, c.Entry, c.Actions })!;
                    if (int.Parse(actual[0]) != c.Calls)
                        throw new Failure("confirmation requests=" + actual[0] + "; expected=" + c.Calls);
                    if (c.Calls > 0 && (!actual[1].Contains("tonumber(cursorItemId)~=100", StringComparison.Ordinal)
                        || !actual[1].Contains("DELETE_GOOD_ITEM", StringComparison.Ordinal)
                        || !actual[1].Contains("DELETE_ITEM_CONFIRM_STRING", StringComparison.Ordinal)))
                        throw new Failure("retained entry/type confirmation predicates changed");
                    passed++;
                    Console.WriteLine("PASS delete popup request: " + c.Name);
                }
                catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL delete popup request: " + c.Name + ": " + error.Message); }
                catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR delete popup request: " + c.Name + ": " + error); }
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Delete popup request scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; verbatim C# owner methods; recording Lua only; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Delete popup request regression");
    }

    private const string Prefix = """
using System;
using System.Globalization;
public sealed class LuaEventArgs { }
public static class Lua
{
    public static bool Submit, ThrowRequest;
    public static int Confirmations;
    public static string LastConfirmation="";
    public static T GetReturnVal<T>(string script,uint index)
    {
        if(typeof(T)==typeof(bool))
        {
            if(ThrowRequest)throw new InvalidOperationException("Controlled request observation failure");
            return (T)(object)Submit;
        }
        if(script.Contains("button1:Click()"))
        {
            Confirmations++;LastConfirmation=script;return (T)(object)2;
        }
        return (T)(object)1;
    }
}
public sealed class DeleteRequestProbe
{
    private static ulong _pendingDeleteGuid;
    private static uint _pendingDeleteEntry;
    private static DateTime _pendingDeleteSince;
    private static bool _pendingDeleteRequested;
    private static object? _pendingDeleteToken, _pendingDeleteLifetime, _pendingDeletePlayer;
    private static ulong _pendingDeletePlayerGuid;
    // This existing fixture controls runtime admission; W86 exercises the real helper separately.
    private static bool OwnsPendingDeleteContext() => true;
    private static bool HasPendingDelete=>_pendingDeleteGuid!=0&&_pendingDeleteEntry!=0;
    private static void Dlog(string message,params object[] args){}
    private static void Slog(string message,params object[] args){}
""";
    private const string Suffix = """
    public string[] Execute(ulong guid,uint entry,string actions)
    {
        ResetPendingDelete();_pendingDeleteGuid=guid;_pendingDeleteEntry=entry;
        _pendingDeleteToken=new object();
        _pendingDeleteSince=DateTime.UtcNow;Lua.Confirmations=0;Lua.LastConfirmation="";
        Lua.Submit=false;Lua.ThrowRequest=false;
        foreach(char action in actions)
        {
            switch(action)
            {
                case 'F':Lua.Submit=false;Lua.ThrowRequest=false;TryIssueDeleteRequest();break;
                case 'S':Lua.Submit=true;Lua.ThrowRequest=false;TryIssueDeleteRequest();break;
                case 'X':Lua.Submit=false;Lua.ThrowRequest=true;TryIssueDeleteRequest();Lua.ThrowRequest=false;break;
                case 'C':TryConfirmPendingDelete();break;
                case 'E':DeleteItemConfirmPopup(null,null);break;
                case 'R':ResetPendingDelete();break;
                default:throw new ArgumentOutOfRangeException("action");
            }
        }
        return new[]{Lua.Confirmations.ToString(),Lua.LastConfirmation};
    }
}
""";
    private static string Method(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) start = source.IndexOf(marker.Replace("private static ", "private "), StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing tracked method " + marker);
        int brace = source.IndexOf('{', start), depth = 0;
        // The selected format strings contain balanced braces. Preserve source
        // statements exactly; any extraction/compile error is not assertion red.
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
}
