using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Styx.WoWInternals.WoWObjects;

// Actual retained pickup dispatch method and actual generated Lua. The external
// item/world/UI observations are controlled; no native item is used in a game.
internal static class QuestItemStarterDispatchRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string directory = Path.Combine(Path.GetTempPath(), "item-quest-dispatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action test)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS item quest dispatch: " + name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL item quest dispatch: " + name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR item quest dispatch: " + name + ": " + error); }
        }
        try
        {
            Case("item-cache StartQuestId aliases BeginQuestId rather than stationery", () =>
            {
                var entry = new Styx.WoWInternals.WoWCache.WoWCache.ItemCacheEntry { BeginQuestId = 950001, BookStationaryId = 9 };
                Check(System.Runtime.InteropServices.Marshal.OffsetOf<Styx.WoWInternals.WoWCache.WoWCache.ItemCacheEntry>("BeginQuestId").ToInt32() == 396,
                    "item-cache field does not match original-client IDA");
                Check(entry.StartQuestId == 950001, "compatibility alias returned stationery as a quest ID");
            });
            Case("missing item defers behavior creation instead of retaining a stopped pickup", () =>
            {
                using var fixture = new QuestDatasetObservationFixture();
                fixture.SetQuest(950001, "item route", 60, new int[4], new int[4], new int[6], new int[6]);
                fixture.SetAccepted(false);
                var node = new Styx.Logic.Profiles.Quest.PickUpNode(Styx.Logic.Pathing.WoWPoint.Zero, 950020, "missing item",
                    Styx.Logic.Profiles.Quest.QuestObjectType.Item, 950001, "item route");
                object? created = typeof(Bots.Quest.Actions.ForcedBehaviorExecutor).GetMethod("CreateQuestPickUp", Hidden)!.Invoke(null, new object[] { node });
                Check(created == null, "missing carried item still produced a pickup after stopping the tree");
            });
            string source = File.ReadAllText(Path.Combine(root, "Bots/Quest/QuestOrder/ForcedQuestPickUp.cs"));
            string method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(value => value.Identifier.ValueText == "UseQuestItem").ToFullString();
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), "using System;using TreeSharp;" +
                "public class WoWItem{public uint Entry=950020;public bool Allowed=true;public int Uses;public void UseContainerItem(){Uses++;}" +
                "public bool TryUseQuestStartingItem(uint id){if(!Allowed||id!=950001)return false;Uses++;return true;}}" +
                "public class Probe{public uint QuestId=950001,GiverId=950020;private bool _shownTitleUniquelyResolved;public int Cycles;" +
                "private void BeginInteractionCycle(){Cycles++;}" + method + "}");
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile", Hidden)!.Invoke(compiler, null)!;
            string[] errors = result.Errors.Cast<CompilerError>().Where(value => !value.IsWarning).Select(value => value.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual dispatch method fixture did not compile: " + string.Join(";", errors));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", Hidden)!.GetValue(compiler)!;
            foreach (var scenario in new[] { (Name: "refused submission does not start an interaction cycle", Allowed: false, Entry: 950020U, Uses: 0),
                (Name: "matching guarded submission starts one cycle", Allowed: true, Entry: 950020U, Uses: 1),
                (Name: "another item entry cannot dispatch", Allowed: true, Entry: 950099U, Uses: 0) })
            {
                var row = scenario;
                Case(row.Name, () =>
                {
                    object owner = Activator.CreateInstance(assembly.GetType("Probe")!)!;
                    object item = Activator.CreateInstance(assembly.GetType("WoWItem")!)!;
                    item.GetType().GetField("Allowed")!.SetValue(item, row.Allowed);
                    item.GetType().GetField("Entry")!.SetValue(item, row.Entry);
                    owner.GetType().GetMethod("UseQuestItem", Hidden)!.Invoke(owner, new[] { item });
                    Check((int)item.GetType().GetField("Uses")!.GetValue(item)! == row.Uses, "item use count was not guarded");
                    Check((int)owner.GetType().GetField("Cycles")!.GetValue(owner)! == row.Uses, "refusal was counted as a started interaction");
                });
            }
            MethodInfo? builder = typeof(WoWItem).GetMethod("BuildValidatedQuestStartingItemLua", Hidden);
            Case("original-client item quest submission has a guarded Lua boundary", () => Check(builder != null, "quest-specific container submission guard is absent"));
            if (builder != null)
            {
                using var lua = new RewardLua51Boundary.StockLua51(root);
                foreach (var scenario in new[] { ("valid", 1), ("wrong-item", 0), ("wrong-quest", 0), ("active", 0),
                    ("wrong-player", 0), ("no-player", 0), ("full", 0), ("unknown-count", 0), ("fraction-count", 0),
                    ("missing-api", 0), ("nil-quest", 0), ("bonding-only", 0) })
                {
                    var row = scenario;
                    Case("Lua: " + row.Item1, () =>
                    {
                        string script = (string)builder.Invoke(null, new object[] { 0, 1, 950020U, 950001U, 77UL })!;
                        var observation = lua.Execute(script, (uint)Encoding.UTF8.GetByteCount(script), row.Item1, new[] { "", "" }, Setup);
                        Check(observation.Load == 0 && observation.Call == 0, "guard raised a Lua error: " + observation.Error);
                        Check(observation.Clicks == row.Item2 && observation.Values.SequenceEqual(new[] { row.Item2.ToString() }), "guarded submission or receipt disagreed");
                    });
                }
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Item quest dispatch scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual retained dispatch and Lua5.1; controlled item observations.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Item quest dispatch regression");
    }
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Checkout required");
    }
    private static void Check(bool value, string reason) { if (!value) throw new Failure(reason); }
    private const string Setup = """
clicks=0
function UnitGUID(unit)
 if scenario=='no-player' then return nil end
 return scenario=='wrong-player' and '0x000000000000004E' or '0x000000000000004D'
end
function GetContainerItemLink(bag,slot) return scenario=='wrong-item' and '|Hitem:99:0|h[Other]|h' or '|Hitem:950020:0|h[Quest item]|h' end
function GetContainerItemQuestInfo(bag,slot)
 if scenario=='nil-quest' then return nil,nil,nil end
 if scenario=='bonding-only' then return true,nil,false end
 return nil,scenario=='wrong-quest' and 99 or 950001,scenario=='active'
end
function GetNumQuestLogEntries()
 if scenario=='unknown-count' then return nil,nil end
 if scenario=='fraction-count' then return 2,1.5 end
 return 30,scenario=='full' and 25 or 24
end
function UseContainerItem(bag,slot) assert(bag==0 and slot==1);clicks=clicks+1 end
if scenario=='missing-api' then GetContainerItemQuestInfo=nil end
""";
}
