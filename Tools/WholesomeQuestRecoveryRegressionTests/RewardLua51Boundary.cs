using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Test-only extension of the existing reward fixture. Executes its unchanged
// generated requests with stock Lua 5.1 C functions. The load-size expression
// and managed conversion methods are extracted from the tracked Lua.cs.
// Controlled frames/items are not a running client, native hook or server test.
internal static class RewardLua51Boundary
{
    private sealed class Failure(string message) : Exception(message) { }

    internal static void WriteManagedBridge(string directory, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var owner = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "Lua");
        var methods = owner.Members.OfType<MethodDeclarationSyntax>().ToArray();
        var request = methods.SingleOrDefault(m => m.Identifier.ValueText == "GetReturnValuesCore") ??
            methods.Single(m => m.Identifier.ValueText == "GetReturnValues" && m.ParameterList.Parameters.Count == 2);
        var calls = request.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression.ToString() == "executor.AddLine").ToArray();
        int first = Array.FindIndex(calls, i => i.ArgumentList.Arguments.Count == 2 &&
            i.ArgumentList.Arguments[1].Expression.ToString() == "fileNameOffset");
        if (first < 0 || first + 4 >= calls.Length ||
            calls[first + 1].ArgumentList.Arguments[0].Expression.ToString() != "\"push {0}\"" ||
            calls[first + 2].ArgumentList.Arguments[1].Expression.ToString() != "address" ||
            calls[first + 3].ArgumentList.Arguments[1].Expression.ToString() != "fullState" ||
            !calls[first + 4].ToString().Contains("GlobalOffsets.FrameScript_Load", StringComparison.Ordinal))
            throw new InvalidOperationException("Unrecognized production loadbuffer argument sequence");
        string length = calls[first + 1].ArgumentList.Arguments[1].Expression.ToFullString();
        string bytes = request.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Single(s => s.Declaration.Variables.Any(v => v.Identifier.ValueText == "bytes")).ToFullString();
        var conversions = methods.Where(m => m.Identifier.ValueText is
            "GetReturnVal" or "IsLuaIntegerType" or "ParseInteger").ToArray();
        if (conversions.Length != 4)
            throw new InvalidOperationException("Unrecognized production managed-conversion methods");
        string bridge = "using System; using System.Collections.Generic; using System.Globalization; using System.Text;\n" +
            "public static class RewardRecordedBridge {\n" +
            "public static Func<string,List<string>> Observe;\n" +
            "public static List<string> GetReturnValues(string lua) { return Observe(lua); }\n" +
            "public static uint LoadSize(string lua) { " + bytes + " return checked((uint)(" + length + ")); }\n" +
            string.Join("\n", conversions.Select(m => m.ToFullString())) + "\n}\n";
        File.WriteAllText(Path.Combine(directory, "RecordedBridge.cs"), bridge, new UTF8Encoding(false));
        Console.WriteLine("REWARD_BRIDGE_SOURCE sha256=" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant() +
            " load-size=" + length.Trim() + "; four conversion methods copied verbatim; executor not emulated.");
    }

    internal static void Run(Assembly probe, string repository)
    {
        using var lua = new StockLua51(repository);
        Type bridge = probe.GetType("RewardRecordedBridge", true)!;
        FieldInfo observe = bridge.GetField("Observe")!;
        var loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
        MethodInfo convert = bridge.GetMethods().Single(m => m.Name == "GetReturnVal" &&
            m.GetParameters()[1].ParameterType == typeof(uint));
        Type state = probe.GetType("RewardState", true)!;
        Type owner = probe.GetType("ActionSelectReward", true)!;
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action action)
        {
            total++;
            try { action(); passed++; Console.WriteLine("PASS reward Lua51: " + name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL reward Lua51: " + name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR reward Lua51: " + name + ": " + error); }
            finally { observe.SetValue(null, null); }
        }
        string[] PlainLinks() => new[] { Link(1000, "First"), Link(1001, "Second") };
        object? ConvertResult(Type type, string script) => convert.MakeGenericMethod(type)
            .Invoke(null, new object[] { script, 0U });

        // These controls distinguish numeric receipts from raw Lua booleans.
        // Success is not simulated by a configured C# return value.
        foreach (var test in new[] {
            (Script:"return 1", Expected:true), (Script:"return 0", Expected:false),
            (Script:"return true", Expected:false), (Script:"return false", Expected:false),
            (Script:"return nil", Expected:false), (Script:"return", Expected:false) })
        {
            Case("transport " + test.Script, () =>
            {
                Observation? result = null;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                    (result = lua.Execute(script, loadSize(script), "active", PlainLinks())).Values));
                bool actual = (bool)ConvertResult(typeof(bool), test.Script)!;
                Check(result != null && result.Load == 0 && result.Call == 0 && actual == test.Expected,
                    "native transport/managed conversion control failed");
            });
        }
        foreach (string value in new[] { "ASCII", "caf\u00e9", "\u5956\u52b1", "shield \U0001F6E1" })
        {
            Case("actual load-size " + value, () =>
            {
                Observation? result = null;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                    (result = lua.Execute(script, loadSize(script), "active", PlainLinks())).Values));
                string? actual = (string?)ConvertResult(typeof(string), "return '" + value + "'");
                Check(result != null && result.Load == 0 && result.Call == 0 && actual == value,
                    "production UTF-8 buffer did not survive its recorded load size: " + Describe(result));
            });
        }

        void Reward(string label, string mode, string boundary, string name, string expected, int clicks)
        {
            Case(label, () =>
            {
                state.GetMethod("Reset")!.Invoke(null, new object[] { mode });
                string[] links = { Link(1000, name + " 1"), Link(1001, name + " 2") };
                state.GetField("Links")!.SetValue(null, links);
                Observation? result = null;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                    (result = lua.Execute(script, loadSize(script), boundary, links)).Values));
                object instance = Activator.CreateInstance(owner)!;
                string actual = owner.GetMethod("Tick")!.Invoke(instance, null)!.ToString()!;
                var requests = (List<string>)state.GetField("Requests")!.GetValue(null)!;
                Check(requests.Count == 1 && result != null, "full owner did not reach the real Lua request boundary");
                Check(result!.Load == 0 && result.Call == 0 && result.Clicks == clicks && actual == expected,
                    "owner=" + actual + " expected=" + expected + "; " + Describe(result));
            });
        }
        foreach (string name in new[] { "Plain", "caf\u00e9", "\u5956\u52b1", "shield \U0001F6E1", "quote\"slash\\bell\u00079" })
        foreach (string mode in new[] { "active", "vendor" })
            Reward(mode + " " + name, mode, "active", name, "Success", 1);

        foreach (string mode in new[] { "frame-hidden", "reward-hidden", "quest-log", "not-choosing",
            "count-changed", "link-changed", "stack-changed", "reordered", "button-missing",
            "button-hidden", "button-type", "button-id", "click-refused" })
            Reward("final guard " + mode, "active", mode, "Plain", "Failure", mode == "click-refused" ? 1 : 0);

        Console.WriteLine($"Reward Lua51 boundary scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; full tracked reward owner, actual generated Lua, extracted production load-size and verbatim managed conversion; stock Lua5.1 C API; controlled UI; no original client/native hook/server.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Reward Lua51 boundary regression");
    }

    private static string Link(int id, string name) => "|Hitem:" + id.ToString(CultureInfo.InvariantCulture) + ":0:0:0:0:0:0:0|h[" + name + "]|h";
    private static string Describe(Observation? o) => o == null ? "no request" :
        $"load={o.Load}, call={o.Call}, clicks={o.Clicks}, UTF8-bytes={o.Bytes}, passed-size={o.Size}, error={o.Error}";
    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
    internal sealed class Observation
    {
        internal int Load, Call, Clicks, Bytes;
        internal uint Size;
        internal string Error = "";
        internal readonly List<string> Values = new();
    }

    internal sealed class StockLua51 : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewState();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StateAction(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadBuffer(IntPtr state, byte[] bytes, UIntPtr size, byte[] name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PCall(IntPtr state, int args, int results, int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetTop(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetTop(IntPtr state, int top);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ToStringPtr(IntPtr state, int index, out UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PushString(IntPtr state, byte[] bytes, UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Field(IntPtr state, int index, byte[] key);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ToInteger(IntPtr state, int index);
        private const int Globals = -10002; // Stock Lua 5.1 lua.h, not a client memory offset.
        private readonly IntPtr library;
        private readonly NewState create;
        private readonly StateAction open, close;
        private readonly LoadBuffer load;
        private readonly PCall call;
        private readonly GetTop top;
        private readonly SetTop setTop;
        private readonly ToStringPtr text;
        private readonly PushString push;
        private readonly Field setField, getField;
        private readonly ToInteger integer;

        internal StockLua51(string repository)
        {
            string? path = Environment.GetEnvironmentVariable("CB_REWARD_LUA51_DLL");
            if (string.IsNullOrEmpty(path)) path = Provision(repository);
            if (RuntimeInformation.ProcessArchitecture != Architecture.X86 || string.IsNullOrEmpty(path) ||
                !Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new InvalidOperationException("Required x86 stock Lua5.1 test DLL missing; provision build_reward_lua51.ps1 on the CI runner. No skip or simulated fallback.");
            library = NativeLibrary.Load(path);
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            try
            {
                create = Export<NewState>("luaL_newstate"); open = Export<StateAction>("luaL_openlibs"); close = Export<StateAction>("lua_close");
                load = Export<LoadBuffer>("luaL_loadbuffer"); call = Export<PCall>("lua_pcall");
                top = Export<GetTop>("lua_gettop"); setTop = Export<SetTop>("lua_settop");
                text = Export<ToStringPtr>("lua_tolstring"); push = Export<PushString>("lua_pushlstring");
                setField = Export<Field>("lua_setfield"); getField = Export<Field>("lua_getfield"); integer = Export<ToInteger>("lua_tointeger");
                var version = Execute("return _VERSION", (uint)Encoding.UTF8.GetByteCount("return _VERSION"), "active", new[] { "a", "b" });
                if (version.Load != 0 || version.Call != 0 || version.Values.Count != 1 || version.Values[0] != "Lua 5.1")
                    throw new InvalidOperationException("Stock Lua5.1 runtime/version control failed");
                Console.WriteLine("REWARD_LUA_RUNTIME version=Lua 5.1 architecture=x86 dll-sha256=" +
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            }
            catch { NativeLibrary.Free(library); throw; }
        }
        private static string Provision(string repository)
        {
            string? temporary = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
                string.IsNullOrEmpty(temporary))
                throw new InvalidOperationException("Missing CB_REWARD_LUA51_DLL; automatic native test provisioning is GitHub-hosted-only.");
            string script = Path.Combine(repository, "Tools", "WholesomeQuestRecoveryRegressionTests", "build_reward_lua51.ps1");
            string destination = Path.Combine(temporary, "cb-reward-lua51-" + Guid.NewGuid().ToString("N"));
            Console.WriteLine("REWARD_LUA_BUILD_SCRIPT_SHA256=" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(script))).ToLowerInvariant());
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Destination", destination })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Lua test preparation");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180000))
            {
                process.Kill(entireProcessTree: true); process.WaitForExit();
                throw new InvalidOperationException("Lua test preparation exceeded its three-minute bound");
            }
            Console.Write(output.GetAwaiter().GetResult()); Console.Error.Write(error.GetAwaiter().GetResult());
            if (process.ExitCode != 0) throw new InvalidOperationException("Lua test preparation failed: " + process.ExitCode);
            return Path.Combine(destination, "lua51.dll");
        }
        internal Observation Execute(string script, uint size, string mode, string[] links, string? setupScript = null)
        {
            IntPtr state = create();
            if (state == IntPtr.Zero) throw new InvalidOperationException("luaL_newstate failed");
            var result = new Observation { Bytes = Encoding.UTF8.GetByteCount(script), Size = size };
            try
            {
                open(state);
                void Global(string key, string value)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    push(state, bytes, (UIntPtr)(uint)bytes.Length);
                    setField(state, Globals, Encoding.ASCII.GetBytes(key + "\0"));
                }
                Global("scenario", mode); Global("link1", links[0]); Global("link2", links[1]);
                byte[] setup = Encoding.UTF8.GetBytes(setupScript ?? Setup);
                if (load(state, setup, (UIntPtr)(uint)setup.Length, Name) != 0 || call(state, 0, 0, 0) != 0)
                    throw new InvalidOperationException("Controlled original-frame setup failed: " + Read(state, -1));
                setTop(state, 0);
                byte[] request = Encoding.UTF8.GetBytes(script);
                if (size > request.Length) throw new InvalidOperationException("Production loader length exceeds the actual allocated bytes");
                result.Load = load(state, request, (UIntPtr)size, Name);
                if (result.Load == 0) result.Call = call(state, 0, -1, 0);
                if (result.Load != 0 || result.Call != 0) result.Error = Read(state, -1);
                else
                {
                    int count = top(state);
                    if (count > 64) throw new InvalidOperationException("Unexpected test return count");
                    for (int i = 1; i <= count; i++) result.Values.Add(Read(state, i));
                }
                getField(state, Globals, Encoding.ASCII.GetBytes("clicks\0"));
                result.Clicks = integer(state, -1);
                return result;
            }
            finally { close(state); }
        }
        // A retained Lua state lets ownership tests deliver events between actual
        // capture and mutation requests. Existing isolated Execute stays unchanged.
        internal Session BeginSession(string setup) => new(this, setup);
        internal sealed class Session : IDisposable
        {
            private readonly StockLua51 runtime;
            private IntPtr state;
            internal Session(StockLua51 runtime, string setup)
            {
                this.runtime = runtime;
                state = runtime.create();
                if (state == IntPtr.Zero) throw new InvalidOperationException("luaL_newstate failed");
                try
                {
                    runtime.open(state);
                    Observation result = Execute(setup, (uint)Encoding.UTF8.GetByteCount(setup));
                    if (result.Load != 0 || result.Call != 0) throw new InvalidOperationException("Persistent Lua setup: " + result.Error);
                }
                catch { Dispose(); throw; }
            }
            internal Observation Execute(string script, uint size)
            {
                if (state == IntPtr.Zero) throw new ObjectDisposedException(nameof(Session));
                runtime.setTop(state, 0);
                byte[] request = Encoding.UTF8.GetBytes(script);
                if (size > request.Length) throw new InvalidOperationException("Loader length exceeds request bytes");
                var result = new Observation { Bytes = request.Length, Size = size };
                result.Load = runtime.load(state, request, (UIntPtr)size, Name);
                if (result.Load == 0) result.Call = runtime.call(state, 0, -1, 0);
                if (result.Load != 0 || result.Call != 0) result.Error = runtime.Read(state, -1);
                else
                {
                    int count = runtime.top(state);
                    if (count > 80) throw new InvalidOperationException("Unexpected return count");
                    for (int i = 1; i <= count; i++) result.Values.Add(runtime.Read(state, i));
                }
                runtime.getField(state, Globals, Encoding.ASCII.GetBytes("clicks\0"));
                result.Clicks = runtime.integer(state, -1);
                return result;
            }
            public void Dispose()
            {
                if (state != IntPtr.Zero) { runtime.close(state); state = IntPtr.Zero; }
            }
        }
        private string Read(IntPtr state, int index)
        {
            IntPtr pointer = text(state, index, out UIntPtr length);
            if (pointer == IntPtr.Zero) return ""; // Same no-string observation; never a fabricated true.
            ulong count = length.ToUInt64();
            if (count > 4096) throw new InvalidOperationException("Unexpectedly large test result");
            byte[] bytes = new byte[(int)count]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        public void Dispose() => NativeLibrary.Free(library);
        private static readonly byte[] Name = Encoding.ASCII.GetBytes("reward-boundary.lua\0");
        private const string Setup = """
clicks=0
QuestFrame={IsShown=function() return scenario~='frame-hidden' end}
QuestFrameRewardPanel={IsShown=function() return scenario~='reward-hidden' end}
QuestInfoFrame={chooseItems=scenario~='not-choosing',questLog=scenario=='quest-log',itemChoice=0}
function GetNumQuestChoices() if scenario=='count-changed' then return 1 end return 2 end
function GetQuestItemLink(kind,i)
 if kind~='choice' then error('unexpected kind') end
 if scenario=='reordered' then i=3-i end
 if scenario=='link-changed' and i==2 then return 'different' end
 return i==1 and link1 or link2
end
function GetQuestItemInfo(kind,i)
 if kind~='choice' then error('unexpected kind') end
 local count=i
 if scenario=='stack-changed' and i==2 then count=count+1 end
 return nil,nil,count
end
local b={type=scenario=='button-type' and 'reward' or 'choice'}
function b:IsShown() return scenario~='button-hidden' end
function b:GetID() return scenario=='button-id' and 1 or 2 end
function b:Click() clicks=clicks+1; if scenario~='click-refused' then QuestInfoFrame.itemChoice=self:GetID() end end
if scenario~='button-missing' then QuestInfoItem2=b end
""";
    }
}
