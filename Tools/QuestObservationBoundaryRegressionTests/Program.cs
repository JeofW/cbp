// Baseline-first behavioral assertions: NOT EXECUTED when this checkpoint was made.
// A failure here must be inspected; compile/extraction errors do not count as red.
using System.Text.Json;
using System.Threading;
using Styx;
using Styx.Logic.Questing;
using W42Fixture;
using WholesomeAQ;

internal sealed class AssertionFailure : Exception
{
    public AssertionFailure(string message) : base(message) { }
}

internal static class Program
{
    private const uint Q1 = 1001, Q2 = 1002, I1 = 91001, I2 = 91002;
    private static readonly List<object> Results = new();
    private static int failed, errored;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new AssertionFailure(message);
    }

    private static WholesomeAutoQuest Bot()
    {
        var bot = new WholesomeAutoQuest();
        bot._dataLoader.Database.Quests.Add(new QuestEntry
        {
            Id = (int)Q1, Objectives = new() { new Objective { ItemId = (int)I1 } }
        });
        bot._dataLoader.Database.Quests.Add(new QuestEntry
        {
            Id = (int)Q2, Objectives = new() { new Objective { ItemId = (int)I2 } }
        });
        StyxWoW.Me.BagItems.Add(new Item { Entry = I1 });
        StyxWoW.Me.BagItems.Add(new Item { Entry = I2 });
        return bot;
    }

    private static void Accept(uint id, int slot = 0, bool hydrate = true)
    {
        StyxWoW.Me.Slots[slot] = id;
        if (hydrate) StyxWoW.Cache.Quests.Hydrate(id);
    }

    private static bool SafeFor(params uint[] ids) => MerchantFrame.Instance.Calls == 0 ||
        ids.All(id => MerchantFrame.Instance.Ids.Contains(id));

    private static void Check(string name, string kind, Action body)
    {
        World.Reset();
        string status = "pass", detail = "";
        try { body(); }
        catch (AssertionFailure error) { failed++; status = "assertion-failed"; detail = error.Message; }
        catch (Exception error) { errored++; status = "fixture-or-unexpected-error"; detail = error.ToString(); }
        Results.Add(new { name, kind, status, detail });
        Console.WriteLine($"{status}: {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: <x86-dotnet> <test.dll> <new-results.json-path>");
            return 2;
        }
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
        {
            Console.Error.WriteLine("This acceptance fixture requires Windows and an x86 .NET host. No cases were executed.");
            return 2;
        }
        string resultPath = Path.GetFullPath(args[0]);
        if (File.Exists(resultPath))
        {
            Console.Error.WriteLine("Refusing to overwrite existing evidence: " + resultPath);
            return 2;
        }
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "source-manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("Pinned source manifest missing; this is not a behavioral red result.");
            return 2;
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Check("raw slot remains occupied when materialization is missing", "characterization", () =>
        {
            Accept(Q1, hydrate: false);
            Require(StyxWoW.Me.QuestLog.GetQuestId(0) == Q1, "Raw ID was not observed.");
            Require(StyxWoW.Me.QuestLog.ContainsQuest(Q1), "Raw acceptance must be observable.");
            Require(StyxWoW.Me.QuestLog.GetQuest(0) == null, "Fixture did not produce a cache miss.");
            Require(StyxWoW.Me.QuestLog.GetAllQuests().Count == 0, "Baseline omission changed; review characterization, not the safety tests.");
        });
        Check("genuine empty log preserves ordinary sale", "control", () =>
        {
            Bot().RunSale();
            Require(MerchantFrame.Instance.Calls == 1, "A genuine stable empty log should allow ordinary selling.");
        });
        Check("hydrated accepted quest retains item exclusion", "control", () =>
        {
            Accept(Q1); Bot().RunSale();
            Require(MerchantFrame.Instance.Calls == 1 && MerchantFrame.Instance.Ids.Contains(I1), "Existing accepted-item protection regressed.");
        });
        Check("uncached accepted quest cannot authorize sale of its required item", "safety", () =>
        {
            Accept(Q1, hydrate: false); Bot().RunSale();
            Require(SafeFor(I1), "Sale was dispatched without the uncached accepted quest's item exclusion.");
        });
        Check("uncached accepted quest absent from dataset vetoes sale", "safety", () =>
        {
            Accept(Q1, hydrate: false);
            var bot = Bot(); bot._dataLoader.Database.Quests.Clear(); bot.RunSale();
            Require(MerchantFrame.Instance.Calls == 0, "Unknown accepted requirements allowed destructive dispatch.");
        });
        Check("mixed hydrated and unhydrated accepted quests remain protected", "safety", () =>
        {
            Accept(Q1); Accept(Q2, 1, false); Bot().RunSale();
            Require(SafeFor(I1, I2), "Only the hydrated accepted quest was protected.");
        });
        Check("all 25 occupied uncached slots are not an authoritative empty log", "safety", () =>
        {
            for (int i = 0; i < 25; i++) Accept((uint)(Q1 + i), i, false);
            Bot().RunSale();
            Require(MerchantFrame.Instance.Calls == 0, "Unknown requirements in a full raw log allowed sale.");
        });
        Check("hydration after an unknown observation never leaves an unsafe first sale", "sequence", () =>
        {
            Accept(Q1, hydrate: false);
            var bot = Bot(); bot.RunSale(); bool safeBefore = SafeFor(I1);
            StyxWoW.Cache.Quests.Hydrate(Q1); MerchantFrame.Instance = new(); bot.RunSale();
            Require(safeBefore && SafeFor(I1), "Pre-hydration dispatch was unsafe even though the hydrated retry was protected.");
        });
        Check("metadata loss on a later invocation revokes sale permission", "sequence", () =>
        {
            Accept(Q1); var bot = Bot(); bot.RunSale(); bool safeBefore = SafeFor(I1);
            StyxWoW.Cache.Quests.Entries.Clear(); MerchantFrame.Instance = new(); bot.RunSale();
            Require(safeBefore && SafeFor(I1), "Cache eviction turned retained acceptance into unprotected sale permission.");
        });
        Check("scheduled protection is retained even with unavailable materialization", "control", () =>
        {
            Accept(Q1, hydrate: false);
            var bot = Bot(); bot._scheduler.ActiveQuestIds.Add((int)Q1); bot.RunSale();
            Require(SafeFor(I1), "Existing scheduled protection was lost.");
        });
        Check("unknown accepted completion cannot borrow historical complete", "safety", () =>
        {
            Accept(Q1, hydrate: false); World.HistoricalCompletions.Add(Q1);
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(state.IsAccepted && state.State == QuestCompletionState.Unknown,
                $"Expected accepted/Unknown, got accepted={state.IsAccepted}/{state.State}.");
        });
        Check("unknown accepted completion cannot borrow historical incomplete", "safety", () =>
        {
            Accept(Q1, hydrate: false);
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(state.IsAccepted && state.State == QuestCompletionState.Unknown,
                $"Expected accepted/Unknown, got accepted={state.IsAccepted}/{state.State}.");
        });
        Check("invalid history does not erase raw acceptance", "safety", () =>
        {
            Accept(Q1, hydrate: false); World.HistoryValid = false;
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(state.IsAccepted && state.State == QuestCompletionState.Unknown,
                $"Expected accepted/Unknown, got accepted={state.IsAccepted}/{state.State}.");
        });
        Check("hydrated accepted incomplete overrides historical complete", "control", () =>
        {
            Accept(Q1); World.HistoricalCompletions.Add(Q1);
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(state.IsAccepted && state.State == QuestCompletionState.KnownIncomplete && World.HistoryReads == 0,
                "Current hydrated acceptance did not remain authoritative.");
        });
        Check("hydrated accepted complete retains positive completion", "control", () =>
        {
            Accept(Q1); World.CurrentCompleted.Add(Q1);
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(state.IsAccepted && state.State == QuestCompletionState.KnownComplete, "Positive current completion regressed.");
        });
        Check("nonaccepted historical complete remains usable", "control", () =>
        {
            World.HistoricalCompletions.Add(Q1);
            var state = StyxWoW.Me.QuestLog.GetQuestCompletionSnapshot(Q1);
            Require(!state.IsAccepted && state.State == QuestCompletionState.KnownComplete, "Existing nonaccepted completed history regressed.");
        });
        Check("same-count quest replacement before dispatch revokes stale authorization", "sequence", () =>
        {
            Accept(Q1); StyxWoW.Cache.Quests.Hydrate(Q2);
            Consumable.BeforeFood = () => StyxWoW.Me.Slots[0] = Q2;
            Bot().RunSale();
            Require(SafeFor(I2), "Unchanged player and quest count concealed a different accepted identity at dispatch.");
        });
        Check("new acceptance before dispatch requires new protection", "sequence", () =>
        {
            Accept(Q1); StyxWoW.Cache.Quests.Hydrate(Q2);
            Consumable.BeforeFood = () => StyxWoW.Me.Slots[1] = Q2;
            Bot().RunSale();
            Require(SafeFor(I2), "A newly accepted quest was unprotected at dispatch.");
        });
        Check("player replacement retains the existing final sale veto", "control", () =>
        {
            Accept(Q1); Consumable.BeforeFood = () => StyxWoW.Me = new Player();
            Bot().RunSale(); Require(MerchantFrame.Instance.Calls == 0, "Player replacement bypassed existing sale guard.");
        });
        Check("merchant disappearance retains the existing final sale veto", "control", () =>
        {
            Accept(Q1); Consumable.BeforeFood = () => MerchantFrame.Instance.IsVisible = false;
            Bot().RunSale(); Require(MerchantFrame.Instance.Calls == 0, "Merchant disappearance bypassed existing sale guard.");
        });
        Check("disabled quality selling still performs no dispatch", "control", () =>
        {
            Accept(Q1); var bot = Bot(); bot._settings.SellWhite = false;
            bot.RunSale(); Require(MerchantFrame.Instance.Calls == 0, "Disabled selling dispatched.");
        });
        Check("descriptor interruption propagates before sale", "control", () =>
        {
            Accept(Q1); var expected = new ThreadInterruptedException("controlled descriptor interruption");
            StyxWoW.Me.BeforeDescriptorRead = _ => throw expected;
            bool propagated = false;
            try { Bot().RunSale(); }
            catch (ThreadInterruptedException actual) { propagated = ReferenceEquals(actual, expected); }
            Require(propagated && MerchantFrame.Instance.Calls == 0, "Interruption was swallowed or sale dispatched.");
        });
        Check("cache interruption propagates before sale", "control", () =>
        {
            Accept(Q1); var expected = new ThreadInterruptedException("controlled cache interruption");
            StyxWoW.Cache.Quests.BeforeRead = _ => throw expected;
            bool propagated = false;
            try { Bot().RunSale(); }
            catch (ThreadInterruptedException actual) { propagated = ReferenceEquals(actual, expected); }
            Require(propagated && MerchantFrame.Instance.Calls == 0, "Cache interruption was swallowed or sale dispatched.");
        });
        Check("hydrated accepted quest missing dataset keeps the existing veto", "control", () =>
        {
            Accept(Q1); var bot = Bot(); bot._dataLoader.Database.Quests.Clear();
            bot.RunSale(); Require(MerchantFrame.Instance.Calls == 0, "Missing accepted dataset record bypassed existing guard.");
        });

        var result = new
        {
            source = manifest.RootElement.Clone(),
            game_attached = false,
            environment = new { os = Environment.OSVersion.ToString(), framework = Environment.Version.ToString(), pointer_bytes = IntPtr.Size },
            executed_cases = Results.Count, assertion_failures = failed, unexpected_errors = errored,
            scope = "Extracted lookup/materialization/completion/sale owners; controlled native/world/history/merchant boundaries; scheduler not executed",
            cases = Results
        };
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        using (var stream = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Executed {Results.Count}; assertion failures {failed}; unexpected errors {errored}; pointer bytes {IntPtr.Size}; no client attached.");
        return errored > 0 ? 2 : failed > 0 ? 1 : 0;
    }
}
