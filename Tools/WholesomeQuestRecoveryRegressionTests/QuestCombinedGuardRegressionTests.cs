using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using WholesomeAQ;

// Existing observation groups test each guard's actual input readers. This
// integration group proves their conjunction on the real published work owner.
internal static class QuestCombinedGuardRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Probe> Test)>();
        foreach (bool inventory in new[] { false, true })
            foreach (bool availability in new[] { false, true })
            {
                bool i = inventory, a = availability;
                cases.Add(($"inventory={i}, availability={a} require their conjunction", p =>
                {
                    int first = 0, second = 0;
                    p.Set("InventoryCurrent", () => { first++; return i; });
                    p.Set("AvailabilityCurrent", () => { second++; return a; });
                    bool result = p.Check();
                    Check(result == (i && a) && first == 1 && second == (i ? 1 : 0), "one guard replaced or bypassed the other");
                    if (result) p.Retained(); else p.Revoked();
                }));
            }
        cases.Add(("unconstrained work retains both optional guard defaults", p =>
        { p.Set("InventoryCurrent", null); p.Set("AvailabilityCurrent", null); Check(p.Check(), "optional guard absence blocked legacy work"); p.Retained(); }));
        cases.Add(("inventory-only work still revokes on lost stock", p =>
        { p.Set("InventoryCurrent", () => false); p.Set("AvailabilityCurrent", null); Check(!p.Check(), "stock-only guard bypassed"); p.Revoked(); }));
        cases.Add(("availability-only work still revokes on lost history", p =>
        { p.Set("InventoryCurrent", null); p.Set("AvailabilityCurrent", () => false); Check(!p.Check(), "history-only guard bypassed"); p.Revoked(); }));
        foreach (string guard in new[] { "InventoryCurrent", "AvailabilityCurrent" })
        {
            string selected = guard;
            cases.Add((selected + " unreadable input revokes without manufacturing success", p =>
            {
                p.Set("InventoryCurrent", () => true); p.Set("AvailabilityCurrent", () => true);
                p.Set(selected, () => throw new IOException("controlled unavailable observation"));
                Check(!p.Check(), "ordinary observation error retained permission"); p.Revoked();
            }));
            cases.Add((selected + " cancellation remains a cancellation", p =>
            {
                p.Set("InventoryCurrent", () => true); p.Set("AvailabilityCurrent", () => true);
                var error = new OperationCanceledException("controlled observation cancellation");
                p.Set(selected, () => throw error);
                try { p.Check(); throw new Failure("cancellation was swallowed"); }
                catch (OperationCanceledException actual) { Check(ReferenceEquals(actual, error), "cancellation identity changed"); }
            }));
        }
        cases.Add(("a revoked publication never revives when both callbacks later pass", p =>
        {
            p.Set("InventoryCurrent", () => true); p.Set("AvailabilityCurrent", () => false);
            Check(!p.Check(), "first invalidation failed"); p.Revoked();
            p.Set("AvailabilityCurrent", () => true);
            Check(!p.Check(), "detached work regained execution permission");
        }));
        int passed = 0, failures = 0, errors = 0;
        foreach (var test in cases)
        {
            try { using var probe = new Probe(); test.Test(probe); passed++; Console.WriteLine("PASS combined guards: " + test.Name); }
            catch (Failure e) { failures++; Console.Error.WriteLine("FAIL combined guards: " + test.Name + ": " + e.Message); }
            catch (Exception e) { errors++; Console.Error.WriteLine("ERROR combined guards: " + test.Name + ": " + e); }
        }
        Console.WriteLine($"Combined guard scenarios: {passed}/{cases.Count}; assertions={failures}; unexpected={errors}; actual loaded publication owner, independent observation callbacks; no game.");
        if (failures + errors != 0) throw new InvalidOperationException("Combined guard regression");
    }
    private sealed class Probe : IDisposable
    {
        private readonly IDisposable fixture;
        private readonly QuestScheduler scheduler;
        private readonly object work;
        private readonly Func<bool> permission;
        internal Probe()
        {
            Type type = typeof(QuestSchedulerRawPublicationRegressionTests).GetNestedType("Case", Hidden)!;
            fixture = (IDisposable)Activator.CreateInstance(type, Hidden, null, new object[] { false }, null)!;
            try
            {
                Invoke(type.GetMethod("Scan", Hidden)!, fixture);
                Invoke(type.GetMethod("Published", Hidden)!, fixture);
                scheduler = (QuestScheduler)type.GetField("Scheduler", Hidden)!.GetValue(fixture)!;
                work = typeof(QuestScheduler).GetField("_publishedWork", Hidden)!.GetValue(scheduler)!;
                permission = (Func<bool>)Invoke(typeof(QuestScheduler).GetMethod("CaptureExecutionPermission", Hidden)!, scheduler)!;
                QuestCombinedGuardRegressionTests.Check(work != null && permission != null, "actual publication was not established");
            }
            catch { fixture.Dispose(); throw; }
        }
        internal void Set(string field, Func<bool>? callback)
        {
            FieldInfo? target = work.GetType().GetField(field, Hidden);
            QuestCombinedGuardRegressionTests.Check(target != null, "combined publication is missing " + field);
            target!.SetValue(work, callback);
        }
        internal bool Check() => permission();
        internal void Retained() => QuestCombinedGuardRegressionTests.Check(scheduler.LastSchedule.Selected.Count > 0 && scheduler.CurrentProfilePath != null, "valid work was revoked");
        internal void Revoked() => QuestCombinedGuardRegressionTests.Check(scheduler.LastSchedule.Selected.Count == 0 && scheduler.CurrentProfilePath == null, "invalid work retained profile ownership");
        public void Dispose() => fixture.Dispose();
    }
    private static object? Invoke(MethodInfo method, object? target)
    {
        try { return method.Invoke(target, Array.Empty<object>()); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Check(bool result, string message) { if (!result) throw new Failure(message); }
}
