using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Profiles;

// Real XML files under a unique owned build-output subdirectory and the actual
// production reload/Contains/list methods. No installed client or user files change.
internal static class ProtectedItemReloadRegressionTests
{
    private const uint Old = 190009001, Next = 190009002, Manual = 190009003;
    private const string OldName = "w42-old-protected-name", NextName = "w42-next-protected-name";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Protected-item reload tests require Windows x86.");
        var cases = new List<(string Name, Action Test)>
        {
            ("valid XML ID and name remain discoverable", () => With(f =>
            { f.Seed(); Check(ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(OldName), "valid file entries absent"); })),
            ("malformed replacement retains prior FILE IDs and names", () => With(f =>
            { f.Seed(); f.Write("<ProtectedItems><Item"); f.Reload(); f.OldRetained(); })),
            ("one bad file aborts the entire proposed FILE replacement", () => With(f =>
            { f.Seed(); f.Write(f.Xml(Next, NextName)); f.Write("<broken", "protitems.xml"); f.Reload(); f.OldRetained(); Check(!ProtectedItemsManager.Contains(Next), "partial new file layer was published"); })),
            ("exclusive file lock preserves prior FILE protection without escaping IO failure", () => With(f =>
            { f.Seed(); using var locked = new FileStream(f.Pathname, FileMode.Open, FileAccess.ReadWrite, FileShare.None); f.Reload(); f.OldRetained(); })),
            ("successful reload replaces only FILE while retaining manual runtime items", () => With(f =>
            { f.Seed(); ProtectedItemsManager.Add(Manual); f.Write(f.Xml(Next, NextName)); f.Reload(); Check(!ProtectedItemsManager.Contains(Old) && !ProtectedItemsManager.Contains(OldName) && ProtectedItemsManager.Contains(Next) && ProtectedItemsManager.Contains(Manual), "successful FILE replacement changed unrelated ownership"); })),
            ("intentional removal of all owned files clears only their FILE entries", () => With(f =>
            { f.Seed(); ProtectedItemsManager.Add(Manual); File.Delete(f.Pathname); f.Reload(); Check(!ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(Manual), "empty successful reload did not separate FILE and runtime"); })),
            ("vendor snapshots retain prior layer after malformed reload", () => With(f =>
            { f.Seed(); f.Write("<bad"); f.Reload(); Check(ProtectedItemsManager.GetAllItemIds().Contains(Old) && ProtectedItemsManager.GetAllItemNames().Contains(OldName), "list consumers observed protection loss"); })),
            ("error-log reentrant reads see the old complete snapshot", () => With(f =>
            {
                f.Seed(); f.Write("<broken"); int callbacks = 0; bool retained = false;
                Logging.LogMessageDelegate log = messages => { if (messages.Any(m => m.Message.Contains(f.DirectoryName))) { callbacks++; retained = ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(OldName); } };
                Logging.OnLogMessage += log;
                try { f.Reload(); } finally { Logging.OnLogMessage -= log; }
                Check(callbacks == 1 && retained, "error callback observed cleared or partial FILE state");
            })),
            ("error-log cancellation propagates without publishing a partial FILE layer", () => LogSignal(new OperationCanceledException("reload cancellation"))),
            ("error-log interruption propagates without publishing a partial FILE layer", () => LogSignal(new ThreadInterruptedException("reload interruption"))),
            ("failed reload followed by valid retry replaces the retained snapshot", () => With(f =>
            { f.Seed(); f.Write("<bad"); f.Reload(); f.OldRetained(); f.Write(f.Xml(Next, NextName)); f.Reload(); Check(!ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(Next), "valid retry did not replace old layer"); })),
            ("successful and failed FILE reloads retain an independent runtime lease", () => With(f =>
            { f.Seed(); using var lease = ProtectedItemsManager.Acquire(Manual); f.Write("<bad"); f.Reload(); Check(ProtectedItemsManager.Contains(Manual), "failed reload erased runtime lease"); f.Write(f.Xml(Next, NextName)); f.Reload(); Check(ProtectedItemsManager.Contains(Manual) && ProtectedItemsManager.Contains(Next), "successful reload erased runtime lease"); lease.Dispose(); Check(!ProtectedItemsManager.Contains(Manual), "disposed reload control lease leaked"); })),
            ("a newer publication created by an error callback survives the obsolete failed reload", () => With(f =>
            {
                f.Seed(); f.Write("<bad"); int callbacks = 0;
                Logging.LogMessageDelegate log = messages => { if (messages.Any(m => m.Message.Contains(f.DirectoryName))) { callbacks++; f.Write(f.Xml(Next, NextName)); ProtectedItemsManager.ReloadProtectedItems(); } };
                Logging.OnLogMessage += log;
                try { f.Reload(); } finally { Logging.OnLogMessage -= log; }
                Check(callbacks == 1 && ProtectedItemsManager.Contains(Next) && !ProtectedItemsManager.Contains(Old), "obsolete failed reload changed its reentrant replacement");
            })),
            ("duplicate valid files keep set membership and recognized aliases", () => With(f =>
            { f.Write($"<ProtectedItems><Item Entry=\"{Old}\"/><Item>{Next}</Item></ProtectedItems>"); f.Write(f.Xml(Old, OldName), "protected items.xml"); f.Reload(); Check(ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(Next) && ProtectedItemsManager.GetAllItemIds().Count(x => x == Old) == 1, "entry/value aliases or deduplication changed"); })),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS protected-item reload: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL protected-item reload assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR protected-item reload fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Protected-item reload scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual XML filesystem and manager, including reentrant logging; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Protected-item reload regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void LogSignal(Exception expected)
    {
        With(f =>
        {
            f.Seed(); f.Write("<broken"); int callbacks = 0; Exception? actual = null;
            Logging.LogMessageDelegate log = messages => { if (messages.Any(m => m.Message.Contains(f.DirectoryName))) { callbacks++; throw expected; } };
            Logging.OnLogMessage += log;
            try { f.Reload(); } catch (Exception error) { actual = error; } finally { Logging.OnLogMessage -= log; }
            Check(callbacks == 1 && ReferenceEquals(actual, expected), "logging stop signal was not reached/preserved"); f.OldRetained();
        });
    }
    private sealed class Fixture : IDisposable
    {
        private readonly uint[] savedIds;
        private readonly string[] savedNames;
        internal readonly string DirectoryName;
        internal string Pathname => Path.Combine(DirectoryName, "protecteditems.xml");
        private static DualHashSet<uint, string> FileLayer => (DualHashSet<uint, string>)typeof(ProtectedItemsManager)
            .GetField("_fileProtectedItems", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        internal Fixture()
        {
            _ = ProtectedItemsManager.Contains(Old);
            savedIds = FileLayer.HashSet1.ToArray(); savedNames = FileLayer.HashSet2.ToArray();
            Check(!ProtectedItemsManager.Contains(Old) && !ProtectedItemsManager.Contains(Next) && !ProtectedItemsManager.Contains(Manual), "unique reload IDs already in use");
            DirectoryName = Path.Combine(Path.GetDirectoryName(typeof(ProtectedItemsManager).Assembly.Location)!, "W42Reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryName);
        }
        internal string Xml(uint id, string name) => $"<ProtectedItems><Item Id=\"{id}\" Name=\"{name}\" /></ProtectedItems>";
        internal void Write(string xml, string name = "protecteditems.xml") => File.WriteAllText(Path.Combine(DirectoryName, name), xml);
        internal void Seed() { Write(Xml(Old, OldName)); Reload(); Check(ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(OldName), "initial real FILE load failed"); }
        internal void OldRetained() => Check(ProtectedItemsManager.Contains(Old) && ProtectedItemsManager.Contains(OldName), "failed reload erased prior FILE protection");
        internal void Reload()
        {
            try { ProtectedItemsManager.ReloadProtectedItems(); }
            catch (IOException error) { throw new AssertionFailure("reload leaked expected IO failure: " + error.GetType().Name); }
            catch (UnauthorizedAccessException error) { throw new AssertionFailure("reload leaked expected access failure: " + error.GetType().Name); }
        }
        public void Dispose()
        {
            try { Directory.Delete(DirectoryName, true); }
            finally
            {
                var layer = FileLayer; layer.Clear(); layer.HashSet1.UnionWith(savedIds); layer.HashSet2.UnionWith(savedNames);
                ProtectedItemsManager.Remove(Manual);
            }
        }
    }
    private static void With(Action<Fixture> action) { using var fixture = new Fixture(); action(fixture); }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
