using System.Text.Json;
using System.Threading;
using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;

// Full production QuestLog and PlayerQuest are linked, not decision substitutes.
// Shared fixture controls memory/cache/native/world boundaries. Native completion
// execution and scheduler/profile publication are deliberately outside this slice.
internal static class Program
{
    private const uint QuestId = 867;
    private sealed class AssertionFailure(string message) : Exception(message) { }
    private static void Check(bool ok, string why)
    {
        if (!ok) throw new AssertionFailure(why);
    }
    private static void Reset()
    {
        ObjectManager.Me = new(); ObjectManager.Wow = new();
        ObjectManager.IsInGame = true; ObjectManager.Executor = null; StyxWoW.Cache = new();
        QuestLog.GetCompletedQuestCacheStatusForIdentity(null!, null!);
    }
    private static void Accept(bool hydrate, bool completed = false)
    {
        ObjectManager.Wow!.Slots[0].Id = QuestId;
        ObjectManager.Wow.Slots[0].Flags = completed ? WoWDescriptorQuestFlags.Completed : WoWDescriptorQuestFlags.None;
        if (hydrate)
            StyxWoW.Cache.Entries[QuestId] = new WoWCache.InfoBlock { Quest = new WoWCache.QuestCacheEntry { Id = QuestId } };
    }
    private static void History(bool completed)
    {
        Check(QuestLog.TryGetAuthoritativeCompletedQuestsForIdentity("fixture", "realm",
            () => completed ? new List<uint> { QuestId } : new List<uint>(), out _), "Could not seed the actual completed-history owner.");
    }
    private static QuestCompletionSnapshot Observe() => StyxWoW.Me!.QuestLog.GetQuestCompletionSnapshot(QuestId);
    private static void Expect(QuestCompletionSnapshot actual, bool accepted, QuestCompletionState state)
    {
        Check(actual.IsAccepted == accepted && actual.State == state,
            $"Expected accepted={accepted}/{state}; observed accepted={actual.IsAccepted}/{actual.State}.");
    }
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !OperatingSystem.IsWindows() || IntPtr.Size != 4)
        {
            Console.Error.WriteLine("Requires Windows x86 .NET 10; arguments: new-results.json actual-owner-manifest.json. No cases executed.");
            return 2;
        }
        string output = Path.GetFullPath(args[0]);
        if (File.Exists(output)) throw new IOException("Refusing to overwrite evidence: " + output);
        using var provenance = JsonDocument.Parse(File.ReadAllText(args[1]));
        var cases = new List<(string Name, Action Run)>
        {
            ("uncached accepted quest does not borrow completed history", () => { Accept(false); History(true); Expect(Observe(), true, QuestCompletionState.Unknown); }),
            ("uncached accepted quest does not borrow incomplete history", () => { Accept(false); History(false); Expect(Observe(), true, QuestCompletionState.Unknown); }),
            ("invalid history does not erase uncached acceptance", () => { Accept(false); Expect(Observe(), true, QuestCompletionState.Unknown); }),
            ("hydrated accepted incomplete retains precedence over historical complete", () => { Accept(true); History(true); Expect(Observe(), true, QuestCompletionState.KnownIncomplete); }),
            ("hydrated accepted completed flag remains positive", () => { Accept(true, true); History(false); Expect(Observe(), true, QuestCompletionState.KnownComplete); }),
            ("genuinely absent quest can use completed history", () => { History(true); Expect(Observe(), false, QuestCompletionState.KnownComplete); }),
            ("genuinely absent quest can use valid incomplete history", () => { History(false); Expect(Observe(), false, QuestCompletionState.KnownIncomplete); }),
            ("genuinely absent quest with invalid history remains unknown", () => { Expect(Observe(), false, QuestCompletionState.Unknown); }),
            ("hydration creates new positive evidence without mutating prior unknown result", () => {
                Accept(false, true); History(true); var before = Observe(); Accept(true, true); var after = Observe();
                Expect(before, true, QuestCompletionState.Unknown); Expect(after, true, QuestCompletionState.KnownComplete);
            }),
            ("metadata eviction does not turn retained acceptance into historical completion", () => {
                Accept(true, true); History(true); var before = Observe(); StyxWoW.Cache.Entries.Clear(); var after = Observe();
                Expect(before, true, QuestCompletionState.KnownComplete); Expect(after, true, QuestCompletionState.Unknown);
            }),
            ("unaccepted cache metadata does not invent acceptance", () => {
                Accept(true, true); ObjectManager.Wow!.Slots[0].Id = 0; History(false); Expect(Observe(), false, QuestCompletionState.KnownIncomplete);
            }),
            ("descriptor cancellation propagates unchanged", () => {
                Accept(true); var expected = new ThreadInterruptedException("controlled descriptor interruption"); ObjectManager.Wow!.Failure = expected;
                try { Observe(); throw new AssertionFailure("Descriptor interruption was swallowed."); }
                catch (ThreadInterruptedException actual) { Check(ReferenceEquals(actual, expected), "Interruption identity changed."); }
            }),
            ("metadata cancellation propagates unchanged", () => {
                Accept(true); var expected = new ThreadInterruptedException("controlled cache interruption"); StyxWoW.Cache.Failure = expected;
                try { Observe(); throw new AssertionFailure("Cache interruption was swallowed."); }
                catch (ThreadInterruptedException actual) { Check(ReferenceEquals(actual, expected), "Interruption identity changed."); }
            }),
            ("zero quest ID is not an accepted empty slot", () => { History(false); Expect(StyxWoW.Me!.QuestLog.GetQuestCompletionSnapshot(0), false, QuestCompletionState.KnownIncomplete); }),
            ("legacy raw and materialized lookup APIs remain distinct", () => {
                Accept(false); var log = StyxWoW.Me!.QuestLog;
                Check(log.GetQuestId(0) == QuestId && log.ContainsQuest(QuestId) && log.GetQuestById(QuestId) == null && log.GetAllQuests().Count == 0,
                    "Legacy public lookup contract changed rather than guarding completion authority.");
            })
        };
        int failures = 0, errors = 0;
        var results = new List<object>();
        foreach (var test in cases)
        {
            Reset(); string status = "pass", detail = "";
            try { test.Run(); }
            catch (AssertionFailure error) { failures++; status = "assertion-failed"; detail = error.Message; }
            catch (Exception error) { errors++; status = "unexpected-error"; detail = error.ToString(); }
            results.Add(new { name = test.Name, status, detail });
            Console.WriteLine($"{status}: {test.Name}{(detail.Length == 0 ? "" : " — " + detail)}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new {
                source = provenance.RootElement.Clone(), executed_cases = cases.Count, assertion_failures = failures, unexpected_errors = errors,
                pointer_bytes = IntPtr.Size, framework = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), game_attached = false,
                scope = "Full production QuestLog/PlayerQuest and actual completed-history owner; controlled external boundaries; no native completion dispatch or scheduler publication", cases = results
            }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Completion-owner cases: {cases.Count - failures - errors}/{cases.Count}; assertions {failures}; unexpected {errors}; Windows x86; no client attached.");
        return errors > 0 ? 2 : failures > 0 ? 1 : 0;
    }
}
