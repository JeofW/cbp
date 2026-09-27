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

// Complete tracked manager entrypoints and backend wrapper, plus the real
// ground factory/TreeSharp. Only executor, spellbook-readiness and world leaves
// are controlled. A completed local dispatch is NOT native/server acceptance.
internal static class SpellDispatchReceiptRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var manager = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetSpellByName", "Cast", "CastSpellById", "TryCastSpellById",
            "CastSpell", "CanBuff", "Buff", "CastRandom", "BuffRandom"
        };
        var methods = manager.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).ToArray();
        foreach (string required in names.Where(n => n != "TryCastSpellById"))
            if (!methods.Any(m => m.Identifier.ValueText == required))
                throw new InvalidOperationException("Missing tracked dispatch owner: " + required);
        var fieldNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "_knownSpells", "_cooldownSync", "_castVerificationUntilTicks",
            "CastAttemptVerificationDelayMs", "_spellRandom"
        };
        string fields = string.Join("\n", manager.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => fieldNames.Contains(v.Identifier.ValueText)))
            .Select(f => f.ToFullString()));
        string spells = manager.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "Spells").ToFullString();
        var singular = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"))).GetRoot();
        string ground = singular.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            m.Identifier.ValueText == "CastOnGround" && m.ParameterList.Parameters.Count == 3).ToFullString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-spell-dispatch-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                "public static class SpellManager {\n" + fields + spells +
                string.Join("\n", methods.Select(m => m.ToFullString())) + Cases + "}\n" +
                "public static class GroundProbe {\n" + ground + "}\n");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("SpellManager", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Prefix = """
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TreeSharp;
using CommonBehaviors.Actions;
using Action = TreeSharp.Action;

public delegate Point LocationRetriever(object context);
public delegate bool SimpleBooleanDelegate(object context);
public sealed class Point
{
    public double X;
    public double Distance(Point other) => Math.Abs(X - other.X);
}
public class WoWUnit
{
    public ulong Guid;
    public bool HasAura(string name) => World.Covered;
}
public sealed class LocalPlayer : WoWUnit
{
    public WoWUnit? CurrentTarget;
    public Point Location = new Point();
    public bool HasPendingSpell(string name)
    {
        World.PendingReads++;
        return World.PendingMatches;
    }
}
public sealed class WoWSpell
{
    public int Id = 12345;
    public string Name = "Selected";
    public uint AttributesEx;
    public uint CastTime = 1500;
    public double MaxRange = 40;
}
public static class World
{
    public static string Mode = "success";
    public static bool Ready, Covered, PendingMatches;
    public static int Resets, Sleeps, GlobalCooldownReads, Exceptions, PendingReads, Clicks;
}
public sealed class ExecutorRand
{
    public object AssemblyLock = new object();
    public readonly List<string> Lines = new List<string>();
    public int Clears, Additions, Attempts, Completions;
    public void Clear()
    {
        Clears++;
        if (World.Mode == "clear-exception") throw new InvalidOperationException("controlled preparation failure");
        Lines.Clear();
    }
    public void AddLine(string format, params object[] arguments)
    {
        Additions++;
        if (World.Mode == "add-exception") throw new InvalidOperationException("controlled assembly preparation failure");
        Lines.Add(string.Format(CultureInfo.InvariantCulture, format, arguments));
    }
    public void Execute()
    {
        Attempts++;
        if (World.Mode == "execute-exception" || World.Mode == "fail-first-execute" && Attempts == 1)
            throw new InvalidOperationException("controlled executor failure; native effects are unknown");
        Completions++;
    }
}
public static class ObjectManager { public static ExecutorRand? Executor; }
public static class StyxWoW
{
    public static LocalPlayer Me = new LocalPlayer();
    public static void ResetAfk() { World.Resets++; }
    public static void Sleep(int milliseconds) { World.Sleeps++; }
}
public static class Logging
{
    public static void WriteDebug(string format, params object[] arguments) { }
    public static void WriteException(Exception exception) { World.Exceptions++; }
}
// A deliberately fake backend address: these controls check unchanged argument
// order, not the address/ABI of a newly analyzed original-client function.
public static class Patchables
{
    public enum GlobalOffsets : uint { Spell_C__CastSpell = 0x00123450 }
}
public static class Unit { public static bool IsAreaEffectSafe(string spell, Point point) => true; }
public static class Logger { public static void Write(string format, params object[] arguments) { } }
public static class LegacySpellManager
{
    public static void ClickRemoteLocation(Point point) { World.Clicks++; }
}
""";

    private const string Cases = """
    // Readiness leaves are controlled; receipt production/propagation is not.
    public static bool CanCast(string name) => World.Ready && _knownSpells.ContainsKey(name);
    public static bool CanCast(string name, WoWUnit target, bool checkRange = false) => CanCast(name);
    public static bool CanCast(WoWSpell spell, WoWUnit target, bool checkRange = false) => CanCast(spell.Name);
    private static bool CanCastSpell(string name) => CanCast(name);
    public static bool ClickRemoteLocation(Point point) { LegacySpellManager.ClickRemoteLocation(point); return true; }
    private static bool GlobalCooldown
    {
        get { World.GlobalCooldownReads++; return false; }
    }
    private sealed class Failure : Exception { public Failure(string message) : base(message) { } }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
    private static ExecutorRand Reset(string mode)
    {
        World.Mode = mode;
        World.Ready = true;
        World.Covered = false;
        World.PendingMatches = true;
        World.Resets = World.Sleeps = World.GlobalCooldownReads = World.Exceptions = World.PendingReads = World.Clicks = 0;
        _knownSpells.Clear();
        _knownSpells.Add("Selected", new WoWSpell());
        _castVerificationUntilTicks.Clear();
        StyxWoW.Me = new LocalPlayer { Guid = 1, CurrentTarget = new WoWUnit { Guid = 0xABCDEF0198765432UL } };
        var executor = new ExecutorRand();
        ObjectManager.Executor = mode == "no-executor" ? null : executor;
        return executor;
    }
    private static string[] ExpectedLines(int id, ulong guid) => new[]
    {
        "push 0", "push 0", "push 0", "push 0",
        "push " + ((uint)(guid >> 32)).ToString(CultureInfo.InvariantCulture),
        "push " + ((uint)guid).ToString(CultureInfo.InvariantCulture),
        "push 0", "push " + id.ToString(CultureInfo.InvariantCulture),
        "call 1193040", "add esp, 0x20", "retn"
    };
    private static void CheckDispatch(ExecutorRand executor, string mode, int id, ulong guid)
    {
        bool complete = mode == "success";
        int attempted = complete || mode == "execute-exception" ? 1 : 0;
        Check(executor.Attempts == attempted && executor.Completions == (complete ? 1 : 0),
            "attempted and completed execution were conflated or dispatch was repeated");
        Check(_castVerificationUntilTicks.ContainsKey(id) == complete,
            "completed-dispatch verification throttle changed or was fabricated after failure");
        Check(World.Resets == 1, "existing AFK-reset behavior changed");
        Check(World.Exceptions == (mode.EndsWith("-exception", StringComparison.Ordinal) ? 1 : 0),
            "existing exception logging changed");
        if (complete)
            Check(executor.Lines.SequenceEqual(ExpectedLines(id, guid)), "native argument/stack sequence changed");
    }
    private static bool Invoke(string api, WoWSpell spell, WoWUnit target)
    {
        switch (api)
        {
            case "object": return Cast(spell, target);
            case "name": return Cast(spell.Name, target);
            case "id": return Cast(spell.Id, target);
            case "current-target": return Cast(spell);
            case "synchronous-immediate": return CastSpell(spell.Name, target.Guid, true);
            case "synchronous-wait": return CastSpell(spell.Name, target.Guid, false);
            case "buff": return Buff(spell, target);
            case "random-cast": return CastRandom(new[] { spell }, target);
            case "random-buff": return BuffRandom(new[] { spell }, target);
            default: throw new InvalidOperationException("Unknown fixture entrypoint");
        }
    }
    public static void Run()
    {
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, System.Action body)
        {
            total++;
            try { body(); passed++; Console.WriteLine("PASS spell dispatch receipt: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL spell dispatch receipt: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR spell dispatch receipt: " + name + ": " + e); }
        }
        string[] modes = { "no-executor", "clear-exception", "add-exception", "execute-exception", "success" };
        string[] apis = { "object", "name", "id", "current-target", "synchronous-immediate", "synchronous-wait", "buff", "random-cast", "random-buff" };
        foreach (string mode in modes)
        foreach (string api in apis)
        {
            Case(api + "/" + mode, () =>
            {
                var executor = Reset(mode);
                var spell = _knownSpells["Selected"];
                var target = StyxWoW.Me.CurrentTarget!;
                bool result = Invoke(api, spell, target);
                Check(result == (mode == "success"), "missing/failed local dispatch became caller success");
                CheckDispatch(executor, mode, spell.Id, target.Guid);
                bool waited = api == "synchronous-wait" && mode == "success";
                Check(World.Sleeps == (waited ? 1 : 0) && World.GlobalCooldownReads == (waited ? 1 : 0),
                    "failed dispatch entered synchronous waits or healthy wait behavior changed");
            });
        }
        foreach (bool buff in new[] { false, true })
        foreach (string mode in new[] { "fail-first-execute", "execute-exception" })
        {
            Case((buff ? "random-buff/" : "random-cast/") + mode + "/two-candidates", () =>
            {
                var executor = Reset(mode);
                var second = new WoWSpell { Id = 23456, Name = "Second" };
                _knownSpells.Add(second.Name, second);
                var candidates = new[] { _knownSpells["Selected"], second };
                bool result = buff ? BuffRandom(candidates, StyxWoW.Me.CurrentTarget!) : CastRandom(candidates, StyxWoW.Me.CurrentTarget!);
                Check(result == (mode == "fail-first-execute"), "random wrapper manufactured success when every dispatch failed");
                Check(executor.Attempts == 2 && executor.Completions == (mode == "fail-first-execute" ? 1 : 0),
                    "failed candidate prevented the remaining independent candidate from being tried");
                Check(_castVerificationUntilTicks.Count == executor.Completions,
                    "failed candidate gained a completed-dispatch throttle");
            });
        }
        foreach (uint attributes in new uint[] { 0, 0x10, 0x20, 0x40, 0x50 })
        {
            Case("existing-target-policy/" + attributes.ToString("X"), () =>
            {
                var executor = Reset("success");
                var spell = _knownSpells["Selected"];
                spell.AttributesEx = attributes;
                var explicitTarget = new WoWUnit { Guid = 0x1020304050607080UL };
                Check(Cast(spell, explicitTarget), "healthy explicit/combo dispatch failed");
                ulong expected = (attributes & 0x50U) != 0 ? StyxWoW.Me.CurrentTarget!.Guid : explicitTarget.Guid;
                CheckDispatch(executor, "success", spell.Id, expected);
            });
        }
        Case("combo-without-current-target-retains-explicit-target", () =>
        {
            var executor = Reset("success");
            var spell = _knownSpells["Selected"];
            spell.AttributesEx = 0x10;
            StyxWoW.Me.CurrentTarget = null;
            var target = new WoWUnit { Guid = 99 };
            Check(Cast(spell, target), "existing explicit-target fallback changed");
            CheckDispatch(executor, "success", spell.Id, target.Guid);
        });
        Case("ordinary-null-target-retains-zero-guid", () =>
        {
            var executor = Reset("success");
            var spell = _knownSpells["Selected"];
            Check(Cast(spell, null!), "existing zero-target dispatch changed");
            CheckDispatch(executor, "success", spell.Id, 0);
        });
        foreach (string denied in new[] { "null-spell", "unknown-name", "unknown-id", "not-ready", "covered-buff" })
        {
            Case("existing-admission/" + denied, () =>
            {
                var executor = Reset("success");
                var target = StyxWoW.Me.CurrentTarget!;
                bool result;
                if (denied == "null-spell") result = Cast((WoWSpell)null!, target);
                else if (denied == "unknown-name") result = Cast("Unknown", target);
                else if (denied == "unknown-id") result = Cast(999, target);
                else if (denied == "not-ready") { World.Ready = false; result = CastSpell("Selected", target.Guid, false); }
                else { World.Covered = true; result = BuffRandom(new[] { _knownSpells["Selected"] }, target); }
                Check(!result && executor.Attempts == 0 && World.Resets == 0 && World.Sleeps == 0,
                    "existing failed admission reached a backend or synchronous wait");
            });
        }
        foreach (string mode in modes)
        foreach (string signature in new[] { "uint", "int", "guid", "unit" })
        {
            Case("void-compatibility/" + signature + "/" + mode, () =>
            {
                var executor = Reset(mode);
                var target = StyxWoW.Me.CurrentTarget!;
                if (signature == "uint") CastSpellById(12345U);
                else if (signature == "int") CastSpellById(12345);
                else if (signature == "guid") CastSpellById(12345, target.Guid);
                else CastSpellById(12345, target);
                CheckDispatch(executor, mode, 12345, signature == "uint" || signature == "int" ? 0UL : target.Guid);
            });
        }
        Case("public-legacy-signatures-remain-void", () =>
        {
            var overloads = typeof(SpellManager).GetMethods().Where(m => m.Name == "CastSpellById").ToArray();
            Check(overloads.Length == 4 && overloads.All(m => m.ReturnType == typeof(void)),
                "public void compatibility signature changed");
        });
        foreach (string mode in modes)
        foreach (string pending in new[] { "ready", "empty", "foreign" })
        {
            Case("real-ground-chain/" + mode + "/" + pending, () =>
            {
                var executor = Reset(mode);
                World.PendingMatches = pending == "ready";
                Composite behavior = GroundProbe.CastOnGround("Selected", _ => new Point { X = 1 }, _ => true);
                behavior.Start(null);
                try
                {
                    RunStatus status = behavior.Tick(null);
                    if (mode != "success")
                    {
                        Check(status == RunStatus.Failure, "failed actual manager dispatch became ground-tree success/wait authority");
                        Check(World.PendingReads == 0 && World.Clicks == 0,
                            "failed dispatch crossed a pending-cursor or terrain boundary");
                    }
                    else
                    {
                        if (pending != "ready")
                        {
                            Check(status == RunStatus.Running && World.Clicks == 0,
                                "healthy delayed cursor no longer waits");
                            World.PendingMatches = true;
                            status = behavior.Tick(null);
                        }
                        Check(status == RunStatus.Success && World.Clicks == 1,
                            "healthy local ground chain no longer preserves its existing behavior");
                    }
                    CheckDispatch(executor, mode, 12345, StyxWoW.Me.CurrentTarget!.Guid);
                }
                finally { behavior.Stop(null); }
            });
        }
        Console.WriteLine($"Spell dispatch receipt scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked manager methods and ground factory; real TreeSharp; controlled executor/world; local completion only, no native/game/server execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Spell dispatch receipt regression");
    }
""";
}
