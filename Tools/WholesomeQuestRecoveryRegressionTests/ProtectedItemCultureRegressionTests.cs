using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Styx.Logic.Profiles;

// Real manager and XML files in a unique test-output directory. A thread's
// current culture must not change an inventory-protection declaration's identity.
// No merchant, Lua dispatch, installed client files or native game is exercised.
internal static class ProtectedItemCultureRegressionTests
{
    private const uint Item = 190010011;
    private const string Name = "W42-ITEM-CULTURE-190010011";
    private const string Lower = "w42-item-culture-190010011";
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Protected-item culture tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("ordinary English XML declaration retains ID and name", f =>
            { Culture("en-US"); f.Load($"<Item Id=\"{Item}\" Name=\"{Name}\"/>"); Present(); }),
            ("uppercase recognized file name is independent of Turkish culture", f =>
            { Culture("tr-TR"); f.Load($"<item id=\"{Item}\"/>", "PROTECTEDITEMS.xml"); Check(ProtectedItemsManager.Contains(Item), "recognized file was silently skipped"); }),
            ("uppercase ITEM element is independent of Turkish culture", f =>
            { Culture("tr-TR"); f.Load($"<ITEM id=\"{Item}\"/>"); Check(ProtectedItemsManager.Contains(Item), "recognized ITEM declaration was silently skipped"); }),
            ("uppercase ID attribute is independent of Turkish culture", f =>
            { Culture("tr-TR"); f.Load($"<item ID=\"{Item}\"/>"); Check(ProtectedItemsManager.Contains(Item), "ID attribute was silently skipped"); }),
            ("ENTRY alias remains valid under Turkish culture", f =>
            { Culture("tr-TR"); f.Load($"<item ENTRY=\"{Item}\"/>"); Check(ProtectedItemsManager.Contains(Item), "ENTRY alias changed"); }),
            ("XML text-value ID remains valid under Turkish culture", f =>
            { Culture("tr-TR"); f.Load($"<item>{Item}</item>"); Check(ProtectedItemsManager.Contains(Item), "text-value ID changed"); }),
            ("FILE name loaded in Turkish remains protected in English", f =>
            { Culture("tr-TR"); f.Load($"<item name=\"{Name}\"/>"); Culture("en-US"); Check(ProtectedItemsManager.Contains(Name), "FILE name identity changed with caller culture"); }),
            ("FILE name loaded in English remains protected in Turkish", f =>
            { Culture("en-US"); f.Load($"<item name=\"{Name}\"/>"); Culture("tr-TR"); Check(ProtectedItemsManager.Contains(Name), "FILE lookup changed with caller culture"); }),
            ("manual English name remains protected in Turkish", f =>
            { Culture("en-US"); Check(ProtectedItemsManager.Add(Name), "manual setup failed"); Culture("tr-TR"); Check(ProtectedItemsManager.Contains(Name), "manual lookup changed identity"); }),
            ("manual Turkish name remains protected in English", f =>
            { Culture("tr-TR"); Check(ProtectedItemsManager.Add(Name), "manual setup failed"); Culture("en-US"); Check(ProtectedItemsManager.Contains(Name), "manual registration changed identity"); }),
            ("duplicate manual registration stays idempotent across cultures", f =>
            { Culture("en-US"); Check(ProtectedItemsManager.Add(Name), "manual setup failed"); Culture("tr-TR"); Check(!ProtectedItemsManager.Add(Name), "culture switch created a second manual identity"); }),
            ("manual removal releases the same identity across cultures", f =>
            { Culture("tr-TR"); Check(ProtectedItemsManager.Add(Name), "manual setup failed"); Culture("en-US"); Check(ProtectedItemsManager.Remove(Name) && !ProtectedItemsManager.Contains(Name), "culture switch prevented manual release"); }),
            ("vendor-facing name snapshots use a stable canonical spelling", f =>
            { Culture("tr-TR"); f.Load($"<item name=\"{Name}\"/>"); Check(ProtectedItemsManager.GetAllItemNames().Contains(Lower), "FILE snapshot changed canonical item spelling"); }),
            ("successful culture-changed reload retains recognized protection and runtime owner", f =>
            {
                Culture("en-US"); f.Load($"<Item Id=\"{Item}\" Name=\"{Name}\"/>"); Present();
                using var owner = ProtectedItemsManager.Acquire(Item + 1);
                Culture("tr-TR"); ProtectedItemsManager.ReloadProtectedItems(); Culture("en-US");
                Present(); Check(ProtectedItemsManager.Contains(Item + 1), "FILE reload disturbed runtime owner");
            }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS protected-item culture: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL protected-item culture assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR protected-item culture fixture/owner: " + item.Name + ": " + error); }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }
        Console.WriteLine($"Protected-item culture scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual XML and manager; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Protected-item culture regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void Culture(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
    private static void Present() => Check(ProtectedItemsManager.Contains(Item) && ProtectedItemsManager.Contains(Name), "complete FILE protection was lost");
    private sealed class Fixture : IDisposable
    {
        private readonly CultureInfo savedCulture = CultureInfo.CurrentCulture;
        private readonly FieldInfo fileField = typeof(ProtectedItemsManager).GetField("_fileProtectedItems", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object savedLayer;
        private readonly DualHashSet<uint, string> runtime;
        private readonly string[] savedRuntimeNames;
        private readonly string directory;
        internal Fixture()
        {
            Culture("en-US"); _ = ProtectedItemsManager.Contains(Item);
            savedLayer = fileField.GetValue(null)!;
            runtime = (DualHashSet<uint, string>)typeof(ProtectedItemsManager).GetField("_runtimeProtectedItems", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            savedRuntimeNames = runtime.HashSet2.ToArray();
            Check(!ProtectedItemsManager.Contains(Item) && !ProtectedItemsManager.Contains(Name), "unique culture fixture identity already in use");
            directory = Path.Combine(Path.GetDirectoryName(typeof(ProtectedItemsManager).Assembly.Location)!, "W42Culture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        internal void Load(string item, string file = "protecteditems.xml")
        {
            File.WriteAllText(Path.Combine(directory, file), "<ProtectedItems>" + item + "</ProtectedItems>");
            ProtectedItemsManager.ReloadProtectedItems();
        }
        public void Dispose()
        {
            try { Directory.Delete(directory, true); }
            finally
            {
                // Restore exactly the fixture-owned changes, including a failed red
                // that created a culture-dependent spelling the public API cannot find.
                fileField.SetValue(null, savedLayer);
                runtime.HashSet2.Clear(); runtime.HashSet2.UnionWith(savedRuntimeNames);
                CultureInfo.CurrentCulture = savedCulture;
            }
        }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
