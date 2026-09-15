using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.Logic;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;

// Calls actual MailAllItems and BuyItems with observed empty inventory, empty
// successful purchase requests and a non-vendor POI. Their terminal paths dispatch
// no mail/purchase/Lua. Callback counts and exact stop signals are the oracles.
internal static class VendorAuxiliaryCallbackRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Auxiliary vendor callback tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("empty mail control invokes each handler and completes without sending", f => Normal(f, true)),
            ("empty purchase control invokes each handler and performs no purchase", f => Normal(f, false)),
            ("mail cancellation stops later callbacks and preserves request", f => Stop(f, true, false, new OperationCanceledException("mail stop"))),
            ("mail interruption stops later callbacks and preserves request", f => Stop(f, true, false, new ThreadInterruptedException("mail stop"))),
            ("purchase cancellation stops later callbacks and preserves request", f => Stop(f, false, false, new OperationCanceledException("buy stop"))),
            ("purchase interruption stops later callbacks and preserves request", f => Stop(f, false, false, new ThreadInterruptedException("buy stop"))),
            ("ordinary mail error cannot mask a later cancellation", f => Stop(f, true, true, new OperationCanceledException("later mail stop"))),
            ("ordinary mail error cannot mask a later interruption", f => Stop(f, true, true, new ThreadInterruptedException("later mail stop"))),
            ("ordinary purchase error cannot mask a later cancellation", f => Stop(f, false, true, new OperationCanceledException("later buy stop"))),
            ("ordinary purchase error cannot mask a later interruption", f => Stop(f, false, true, new ThreadInterruptedException("later buy stop"))),
            ("ordinary mail failure retains later handler compatibility", f => Ordinary(f, true, false)),
            ("ordinary purchase failure discards partial arguments before next handler", f => Ordinary(f, false, true)),
            ("ordinary mail failure discards partial arguments before next handler", f => Ordinary(f, true, true)),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS vendor auxiliary callback: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL vendor auxiliary callback assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR vendor auxiliary callback fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Vendor auxiliary callback scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual public owners with empty terminal inputs; no mail/purchase/Lua dispatch.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Vendor auxiliary callback regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void Normal(Fixture f, bool mail)
    {
        int first = 0, second = 0;
        if (mail) { Vendors.OnMailItems = _ => first++; Vendors.OnMailItems += _ => second++; }
        else { Vendors.OnBuyItems = _ => first++; Vendors.OnBuyItems += _ => second++; }
        f.Call(mail);
        Check(first == 1 && second == 1, "empty control did not invoke both handlers");
        Check(mail ? !Vendors.ForceMail : Vendors.ForceBuy, "existing empty terminal/request semantics changed");
    }
    private static void Stop(Fixture f, bool mail, bool ordinaryFirst, Exception signal)
    {
        int ordinary = 0, stopped = 0, later = 0;
        if (mail)
        {
            if (ordinaryFirst) Vendors.OnMailItems = _ => { ordinary++; throw new InvalidOperationException("ordinary mail callback"); };
            Vendors.OnMailItems += _ => { stopped++; throw signal; };
            Vendors.OnMailItems += _ => later++;
        }
        else
        {
            if (ordinaryFirst) Vendors.OnBuyItems = _ => { ordinary++; throw new InvalidOperationException("ordinary buy callback"); };
            Vendors.OnBuyItems += _ => { stopped++; throw signal; };
            Vendors.OnBuyItems += _ => later++;
        }
        Exception? caught = null; try { f.Call(mail); } catch (Exception error) { caught = error; }
        Check(ordinary == (ordinaryFirst ? 1 : 0) && stopped == 1 && later == 0 && ReferenceEquals(caught, signal)
            && (mail ? Vendors.ForceMail : Vendors.ForceBuy), $"stopped={stopped}; later={later}; exact={ReferenceEquals(caught, signal)}; request retained={(mail ? Vendors.ForceMail : Vendors.ForceBuy)}");
    }
    private static void Ordinary(Fixture f, bool mail, bool partial)
    {
        int first = 0, later = 0; bool clean = false;
        if (mail)
        {
            Vendors.OnMailItems = args => { first++; if (partial) args.AdditionalItems.Add(null!); throw new InvalidOperationException("ordinary mail callback"); };
            // Record the actual boundary before clearing the sentinel to keep the
            // terminal input empty even on broken production. No real item is sent.
            Vendors.OnMailItems += args => { later++; clean = args.AdditionalItems.Count == 0; args.AdditionalItems.Clear(); };
        }
        else
        {
            Vendors.OnBuyItems = args => { first++; if (partial) args.BuyItemsIds.Add(190010291, 1); throw new InvalidOperationException("ordinary buy callback"); };
            Vendors.OnBuyItems += args => { later++; clean = args.BuyItemsIds.Count == 0; args.BuyItemsIds.Clear(); };
        }
        f.Call(mail);
        Check(first == 1 && later == 1 && clean, "ordinary callback failure leaked partial arguments or skipped a later handler");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly MailItemsEventHandler? mail = Vendors.OnMailItems;
        private readonly BuyItemsEventHandler? buy = Vendors.OnBuyItems;
        private readonly bool forceMail = Vendors.ForceMail, forceBuy = Vendors.ForceBuy;
        private readonly BotPoi poi = BotPoi.Current;
        private readonly object actual;
        internal Fixture()
        {
            actual = Activator.CreateInstance(typeof(VendorSessionBoundaryRegressionTests).GetNestedType("Fixture", BindingFlags.NonPublic)!, true)!;
            try
            {
                Check(ObjectManager.Me != null && ObjectManager.Me.CarriedItems.Count == 0 && InventoryManager.GetItemsToMail().Length == 0,
                    "fixture requires genuinely observed empty carried/mail items");
                BotPoi.Current = new BotPoi(WoWPoint.Zero, PoiType.None);
                Check(BotPoi.Current.AsVendor == null, "empty purchase control must have no vendor owner");
                Vendors.OnMailItems = null; Vendors.OnBuyItems = null; Vendors.ForceMail = true; Vendors.ForceBuy = true;
            }
            catch { Dispose(); throw; }
        }
        internal void Call(bool mail) { if (mail) Vendors.MailAllItems(); else Vendors.BuyItems(); }
        public void Dispose()
        {
            try { Vendors.OnMailItems = mail; Vendors.OnBuyItems = buy; Vendors.ForceMail = forceMail; Vendors.ForceBuy = forceBuy; BotPoi.Current = poi; }
            finally { ((IDisposable)actual).Dispose(); }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
