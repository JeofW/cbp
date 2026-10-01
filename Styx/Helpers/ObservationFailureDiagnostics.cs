using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace Styx.Helpers
{
    /// <summary>Bounded, session-scoped evidence for expected unavailable inputs.</summary>
    internal static class ObservationFailureDiagnostics
    {
        private const int MaximumOwners = 64;
        private static readonly long Interval = Stopwatch.Frequency * 30L;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        private static long _session;

        private sealed class Entry
        {
            internal string Observation = "", Consumer = "", Reason = "";
            internal DateTime FirstUtc, LastUtc;
            internal long Count, EmittedCount, LastEmission;
        }

        internal static void BeginSession()
        {
            EndSession();
            lock (Gate) _session++;
        }

        internal static void EndSession()
        {
            List<string> summaries = new();
            lock (Gate)
            {
                foreach (var entry in Entries.Values)
                    if (entry.Count != entry.EmittedCount) summaries.Add(Format(entry));
                Entries.Clear();
            }
            foreach (string summary in summaries) Emit(summary);
        }

        internal static void Report(ObservationUnavailableException error, string? consumer = null)
        {
            consumer = Bound(string.IsNullOrEmpty(consumer) ? Consumer(error) : consumer, 200);
            string observation = Bound(error.Observation, 80), reason = Bound(error.Message, 700);
            string key = observation + "|" + consumer + "|" + reason;
            string? output = null;
            long now = Stopwatch.GetTimestamp();
            DateTime utc = DateTime.UtcNow;
            lock (Gate)
            {
                if (!Entries.TryGetValue(key, out var entry))
                {
                    if (Entries.Count >= MaximumOwners)
                    {
                        // One overflow bucket bounds memory and first-occurrence
                        // output even when corrupt metadata yields many distinct IDs.
                        key = "overflow";
                        if (!Entries.TryGetValue(key, out entry))
                            Entries[key] = entry = new Entry { FirstUtc = utc, Consumer = "overflow" };
                    }
                    else Entries[key] = entry = new Entry { FirstUtc = utc, Consumer = consumer };
                }
                entry.Observation = observation;
                entry.Reason = key == "overflow" ? "last-consumer=" + consumer + "; " + reason : reason;
                entry.LastUtc = utc;
                if (entry.Count != long.MaxValue) entry.Count++;
                if (entry.EmittedCount == 0 || now - entry.LastEmission >= Interval)
                {
                    output = Format(entry);
                    entry.LastEmission = now;
                    entry.EmittedCount = entry.Count;
                }
            }
            if (output != null) Emit(output);
        }

        private static string Format(Entry entry) =>
            $"[Observation] status=UNKNOWN session={_session} observation={entry.Observation} " +
            $"consumer={entry.Consumer} count={entry.Count} first={entry.FirstUtc:O} last={entry.LastUtc:O} reason={entry.Reason}";

        private static string Consumer(Exception error)
        {
            var frames = new StackTrace(error, false).GetFrames();
            if (frames != null)
                for (int i = 0; i < Math.Min(frames.Length, 64); i++)
                {
                    MethodBase? method = frames[i].GetMethod();
                    string? name = method?.DeclaringType?.FullName;
                    if (name == null || name.StartsWith("System.", StringComparison.Ordinal)
                        || name == "Styx.WoWInternals.WoWObjects.WoWUnit"
                        || name.StartsWith("Styx.Helpers.Observation", StringComparison.Ordinal)) continue;
                    return name + "." + method!.Name;
                }
            return "unidentified-consumer";
        }

        private static string Bound(string text, int maximum)
        {
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }

        private static void Emit(string summary)
        {
            try { Logging.WriteDiagnostic(summary); }
            catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
            {
                ObservationUnavailableException.RethrowCancellation(error);
                // An ordinary UI/log subscriber failure cannot convert UNKNOWN
                // into worker termination. Retain the evidence through the file sink.
                Debug.WriteLine(summary);
                if (Logging.FileLogging) Logging.WriteToFileSync(LogLevel.Diagnostic, summary);
            }
        }
    }
}
