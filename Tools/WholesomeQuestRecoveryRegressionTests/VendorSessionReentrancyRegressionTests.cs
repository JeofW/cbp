using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Styx.Logic;
using Styx.Logic.Profiles;

// Reuses the independently observed, empty-inventory vendor fixture. These tests
// invoke the real session candidate/Reset owners, never a merchant or Lua sale.
internal static class VendorSessionReentrancyRegressionTests
{
    private const uint OldItem = 190010241, NewItem = 190010242;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Vendor session reentrancy requires Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("normal candidate publishes once and an active session is not restarted", f =>
            {
                int calls = 0; Vendors.OnVendorItems = args => { calls++; args.IdExceptions.Add(OldItem); };
                Check(f.Start() && f.Active && f.Ids.Contains(OldItem), "first session did not publish");
                var ids = f.Ids; Check(f.Start() && calls == 1 && ReferenceEquals(ids, f.Ids), "active owner was rebuilt");
            }),
            ("profile replacement stops the obsolete callback chain", f => ChangedProfile(f, new Profile())),
            ("profile removal stops the obsolete callback chain", f => ChangedProfile(f, null)),
            ("reset during admission invalidates the pending candidate", f =>
            {
                int calls = 0, later = 0;
                Vendors.OnVendorItems = _ => { calls++; f.Reset(); };
                Vendors.OnVendorItems += _ => later++;
                bool result = f.Start();
                Check(calls == 1 && later == 0 && !result && !f.Active && f.Ids == null,
                    "reset candidate resumed callbacks or published a session");
            }),
            ("same-profile nested publication is not overwritten by the old candidate", f => Replacement(f, false, false, false)),
            ("new-profile nested publication is not borrowed by the old candidate", f => Replacement(f, true, false, false)),
            ("reset then nested publication retains only the replacement owner", f => Replacement(f, false, true, false)),
            ("ordinary failure after nested publication cannot resume the obsolete candidate", f => Replacement(f, false, false, true)),
            ("cancellation after replacement preserves the new session and exact signal", f => SignalAfterReplacement(f, new OperationCanceledException("old candidate cancelled"))),
            ("interruption after replacement preserves the new session and exact signal", f => SignalAfterReplacement(f, new ThreadInterruptedException("old candidate interrupted"))),
            ("handler registration changes take effect on a later candidate only", f =>
            {
                int first = 0, added = 0;
                VendorItemsEventHandler extra = _ => added++;
                Vendors.OnVendorItems = _ => { first++; Vendors.OnVendorItems += extra; };
                Check(f.Start() && first == 1 && added == 0, "invocation snapshot was not retained");
                f.Reset(); Vendors.OnVendorItems = extra;
                Check(f.Start() && added == 1, "next candidate lost newly registered handler");
            }),
            ("reset outside callbacks allows a fresh candidate", f =>
            {
                Vendors.OnVendorItems = args => args.IdExceptions.Add(OldItem); Check(f.Start(), "initial start failed");
                f.Reset(); Vendors.OnVendorItems = args => args.IdExceptions.Add(NewItem);
                Check(f.Start() && f.Active && f.Ids.Contains(NewItem) && !f.Ids.Contains(OldItem), "fresh owner inherited obsolete exclusions");
            }),
            ("rejected profile candidate does not prevent a later successful start", f =>
            {
                Vendors.OnVendorItems = _ => f.Activate(new Profile()); Check(!f.Start(), "obsolete candidate was accepted");
                Vendors.OnVendorItems = args => args.IdExceptions.Add(NewItem);
                Check(f.Start() && f.Active && f.Ids.Contains(NewItem), "rejection stranded the next candidate");
            }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS vendor-session reentrancy: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL vendor-session reentrancy assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR vendor-session reentrancy fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Vendor-session reentrancy scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual callback/reset/publication owners; no merchant/Lua dispatch or game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Vendor-session reentrancy regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void ChangedProfile(Fixture f, Profile? profile)
    {
        int first = 0, later = 0;
        Vendors.OnVendorItems = _ => { first++; f.Activate(profile); };
        Vendors.OnVendorItems += _ => later++;
        Check(!f.Start() && first == 1 && later == 0 && f.InactiveRetained(), "obsolete callbacks ran or candidate state changed after profile replacement");
    }

    private static void Replacement(Fixture f, bool replaceProfile, bool reset, bool ordinaryError)
    {
        int first = 0, fresh = 0, later = 0;
        List<uint>? replacementIds = null;
        Vendors.OnVendorItems = args =>
        {
            first++; args.IdExceptions.Add(OldItem);
            if (reset) f.Reset();
            if (replaceProfile) f.Activate(new Profile());
            Vendors.OnVendorItems = next => { fresh++; next.IdExceptions.Add(NewItem); };
            Check(f.Start() && f.Active, "nested replacement did not actually publish");
            replacementIds = f.Ids;
            if (ordinaryError) throw new InvalidOperationException("obsolete callback failure");
        };
        Vendors.OnVendorItems += _ => later++;
        bool result = f.Start();
        Check(first == 1 && fresh == 1 && replacementIds != null, "nested publication oracle was not reached");
        Check(!result && later == 0 && f.Active && ReferenceEquals(replacementIds, f.Ids)
            && f.Ids.Contains(NewItem) && !f.Ids.Contains(OldItem), "obsolete candidate borrowed, resumed after, or overwrote replacement publication");
    }

    private static void SignalAfterReplacement(Fixture f, Exception signal)
    {
        int old = 0, fresh = 0, later = 0; List<uint>? replacementIds = null;
        Vendors.OnVendorItems = _ =>
        {
            old++; Vendors.OnVendorItems = args => { fresh++; args.IdExceptions.Add(NewItem); };
            Check(f.Start() && f.Active, "nested replacement did not publish"); replacementIds = f.Ids;
            throw signal;
        };
        Vendors.OnVendorItems += _ => later++;
        Exception? observed = null; try { f.Start(); } catch (Exception error) { observed = error; }
        Check(old == 1 && fresh == 1 && later == 0 && ReferenceEquals(signal, observed)
            && f.Active && ReferenceEquals(replacementIds, f.Ids) && f.Ids.Contains(NewItem), "cancellation mutated replacement state or was not propagated exactly");
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object actual;
        internal Fixture() => actual = Activator.CreateInstance(typeof(VendorSessionBoundaryRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
        internal bool Start() => (bool)Invoke(actual.GetType().GetMethod("Start", All)!, actual, null)!;
        internal void Reset() => Invoke(typeof(Vendors).GetMethod("ResetSellSession", BindingFlags.Static | BindingFlags.NonPublic)!, null, null);
        internal void Activate(Profile? profile) => Invoke(actual.GetType().GetMethod("Activate", All)!, actual, new object?[] { profile });
        internal bool InactiveRetained() => (bool)Invoke(actual.GetType().GetMethod("InactiveRetained", All)!, actual, null)!;
        internal bool Active => (bool)actual.GetType().GetProperty("Active", All)!.GetValue(actual)!;
        internal List<uint> Ids => (List<uint>)actual.GetType().GetProperty("Ids", All)!.GetValue(actual)!;
        public void Dispose() => ((IDisposable)actual).Dispose();
        private static object? Invoke(MethodInfo method, object? target, object?[]? args)
        {
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
