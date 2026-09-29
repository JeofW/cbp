using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual three tracked WoWUnit getters, actual managed Lua conversion and stock
// Lua5.1. Controlled native-return tuples follow the hash-bound build12340
// UnitCastingInfo/UnitChannelInfo disassembly, not a later client API.
internal static class CastLuaObservationRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        string? root = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) { root = directory.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var members = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWUnit.cs")))
            .GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(member => member.Identifier.ValueText is "CanInterruptCurrentSpellCast" or "CurrentCastTimeLeft" or "CurrentChannelTimeLeft").ToArray();
        if (members.Length != 3) throw new InvalidOperationException("Expected three actual cast observation properties.");
        string temporary = Path.Combine(Path.GetTempPath(), "cb-cast-lua-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            RewardLua51Boundary.WriteManagedBridge(temporary, File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs")));
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), Prefix + string.Join("\n", members.Select(member => member.ToString())) + "}\n");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temporary })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual cast getter compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            Execute((Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!, root);
        }
        finally { Directory.Delete(temporary, true); }
    }
    private static void Execute(Assembly assembly, string root)
    {
        using var lua = new RewardLua51Boundary.StockLua51(root);
        var bridge = assembly.GetType("RewardRecordedBridge", true)!;
        var observe = bridge.GetField("Observe")!;
        var loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
        Type owner = assembly.GetType("CastProbe", true)!;
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string property, string mode, object expected, string fault = "none", bool admission = true, bool mapped = true)
        {
            total++;
            string label = property + "/" + mode + "/" + fault + "/" + admission + "/" + mapped;
            try
            {
                object instance = Activator.CreateInstance(owner)!;
                owner.GetField("IsCasting")!.SetValue(instance, admission);
                owner.GetField("IsChanneling")!.SetValue(instance, admission);
                owner.GetField("UnitId")!.SetValue(instance, mapped ? "target" : "");
                int requests = 0;
                observe.SetValue(null, new Func<string, List<string>>(script =>
                {
                    requests++;
                    if (fault == "exception") throw new IOException("Controlled transport failure");
                    string executed = fault switch { "missing" => "return", "nil" => "return nil", "bad-number" => "return 'not a number'", _ => script };
                    var observation = lua.Execute(executed, loadSize(executed), mode, new[] { "", "" }, Setup);
                    if (observation.Load != 0 || observation.Call != 0)
                        throw new Failure("Generated getter Lua failed: " + observation.Error);
                    return observation.Values;
                }));
                object actual;
                try { actual = owner.GetProperty(property)!.GetValue(instance)!; }
                catch (TargetInvocationException error) when (error.InnerException != null)
                { throw new Failure("Observation escaped instead of failing closed: " + error.InnerException.GetType().Name); }
                if (!Equals(actual, expected)) throw new Failure("Observed " + actual + "; expected " + expected);
                if ((!admission || !mapped) && requests != 0) throw new Failure("Unavailable native unit still queried Lua");
                passed++;
                Console.WriteLine("PASS cast Lua observation: " + label);
            }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL cast Lua observation: " + label + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR cast Lua observation: " + label + ": " + error); }
            finally { observe.SetValue(null, null); }
        }
        foreach (string property in new[] { "CurrentCastTimeLeft", "CurrentChannelTimeLeft" })
        {
            foreach (string mode in new[] { "ordinary", "interruptible-id0", "interruptible-id255", "blocked", "channel", "channel-blocked" })
                Case(property, mode, property == "CurrentCastTimeLeft" && mode.StartsWith("channel", StringComparison.Ordinal) ? TimeSpan.Zero : TimeSpan.FromSeconds(2));
            foreach (string mode in new[] { "absent", "expired", "malformed-end", "nan-end", "infinite-end", "missing-end" })
                Case(property, mode, TimeSpan.Zero);
            foreach (string fault in new[] { "missing", "nil", "bad-number", "exception" })
                Case(property, "ordinary", TimeSpan.Zero, fault);
            Case(property, "ordinary", TimeSpan.Zero, admission: false);
            Case(property, "ordinary", TimeSpan.Zero, mapped: false);
        }
        foreach (string mode in new[] { "ordinary", "interruptible-id0", "interruptible-id255", "channel" })
            Case("CanInterruptCurrentSpellCast", mode, true);
        foreach (string mode in new[] { "blocked", "blocked-id0", "channel-blocked", "absent", "unknown-flag", "malformed-flag" })
            Case("CanInterruptCurrentSpellCast", mode, false);
        foreach (string fault in new[] { "missing", "nil", "bad-number", "exception" })
            Case("CanInterruptCurrentSpellCast", "ordinary", false, fault);
        Case("CanInterruptCurrentSpellCast", "ordinary", false, admission: false);
        Case("CanInterruptCurrentSpellCast", "ordinary", false, mapped: false);
        Console.WriteLine($"Cast Lua observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual getters/conversion and stock Lua5.1; controlled build12340 native tuples; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Cast Lua observation regression failures.");
    }
    private const string Prefix = """
global using Styx.Helpers;
using System;
public static class Lua {public static T GetReturnVal<T>(string code,uint index)=>RewardRecordedBridge.GetReturnVal<T>(code,index);}
public class CastProbe {
 public bool IsCasting=true,IsChanneling=true,IsValid=true;public int CastingSpellId=100;public ulong Guid=1;public string UnitId="target";
 protected string GetLuaUnitId()=>UnitId;
""";
    private const string Setup = """
clicks=0
function GetTime() return 10 end
function UnitGUID(unit) return '0x0000000000000001' end
local function values(channel)
 if scenario=='absent' then return end
 if not channel and (scenario=='channel' or scenario=='channel-blocked') then return end
 local ending=12000
 if scenario=='expired' then ending=9000 end
 if scenario=='malformed-end' then ending='invalid' end
 if scenario=='nan-end' then ending=0/0 end
 if scenario=='infinite-end' then ending=math.huge end
 if scenario=='missing-end' then ending=nil end
 local blocked=scenario=='blocked' or scenario=='blocked-id0' or scenario=='channel-blocked'
 if scenario=='unknown-flag' then blocked=nil end
 if scenario=='malformed-flag' then blocked=0 end
 if channel then return 'Heal','Rank 1','Channel display','Icon',8000,ending,false,blocked end
 local castId=(scenario=='interruptible-id0' or scenario=='blocked-id0') and 0 or (scenario=='interruptible-id255' and 255 or 7)
 return 'Heal','Rank 1','Cast display','Icon',8000,ending,false,castId,blocked
end
function UnitCastingInfo(unit) return values(false) end
function UnitChannelInfo(unit) return values(true) end
""";
}
