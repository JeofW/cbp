using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Styx.Logic.Profiles;

// Actual public Profile XML parser and actual protected/forced-mail lookups.
// Reflection only installs/restores the active profile for lookup; this is not
// host publication, vendor acknowledgment, Lua dispatch or native game acceptance.
internal static class ProfileItemCultureRegressionTests
{
    private const string Name = "W42-PROFILE-ITEM-190010071";
    private const string Lower = "w42-profile-item-190010071";
    private const string Other = "W42-PROFILE-CHILD-190010072";
    private const uint Id = 190010071;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string text) : base(text) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Profile item culture tests require Windows x86.");
        var cases = new List<(string Name, Action<Fixture> Test)>
        {
            ("English protected name uses the canonical key", f =>
            { var p = f.Parse("en-US"); Check(p.ProtectedItems.Contains(Lower), "English parser control failed"); }),
            ("Turkish protected name uses the canonical key", f =>
            { var p = f.Parse("tr-TR"); Check(p.ProtectedItems.Contains(Lower), "protected parser key depends on culture"); }),
            ("Turkish forced-mail name uses the canonical key", f =>
            { var p = f.Parse("tr-TR"); Check(p.ForceMail.Contains(Lower), "forced-mail parser key depends on culture"); }),
            ("Turkish parse and Turkish protection lookup agree", f =>
            { f.Parse("tr-TR"); Check(ProtectedItemsManager.Contains(Name), "same-culture profile protection disappeared"); }),
            ("Turkish parse and English protection lookup agree", f =>
            { f.Parse("tr-TR"); Culture("en-US"); Check(ProtectedItemsManager.Contains(Name), "profile protection changed across cultures"); }),
            ("English parse and Turkish protection lookup agree", f =>
            { f.Parse("en-US"); Culture("tr-TR"); Check(ProtectedItemsManager.Contains(Name), "invariant manager control failed"); }),
            ("Turkish parse and Turkish forced-mail lookup agree", f =>
            { f.Parse("tr-TR"); Check(ForceMailManager.Contains(Name), "same-culture forced-mail identity disappeared"); }),
            ("Turkish parse and English forced-mail lookup agree", f =>
            { f.Parse("tr-TR"); Culture("en-US"); Check(ForceMailManager.Contains(Name), "forced-mail identity changed across cultures"); }),
            ("English parse and Turkish forced-mail lookup agree", f =>
            { f.Parse("en-US"); Culture("tr-TR"); Check(ForceMailManager.Contains(Name), "forced-mail invariant control failed"); }),
            ("protected vendor snapshot exposes the canonical profile key", f =>
            { f.Parse("tr-TR"); Check(ProtectedItemsManager.GetAllItemNames().Contains(Lower), "profile key missing from protected snapshot"); }),
            ("forced-mail snapshot exposes the canonical profile key", f =>
            { f.Parse("tr-TR"); Check(ForceMailManager.GetAllItemNames().Contains(Lower), "profile key missing from forced-mail snapshot"); }),
            ("empty child inherits protected and forced-mail profile identities", f =>
            {
                var parent = f.Parse("tr-TR"); var child = new Profile(new XElement("SubProfile"), parent); f.Activate(child);
                Check(ReferenceEquals(child.ProtectedItems, parent.ProtectedItems) && ReferenceEquals(child.ForceMail, parent.ForceMail), "inheritance control changed");
                Check(ProtectedItemsManager.Contains(Name) && ForceMailManager.Contains(Name), "inherited profile identity disappeared");
            }),
            ("nonempty child retains override rather than merging the parent", f =>
            {
                var parent = f.Parse("en-US"); Culture("tr-TR"); var child = new Profile(Xml(Other), parent); f.Activate(child);
                Check(!child.ProtectedItems.Contains(Lower) && !child.ForceMail.Contains(Lower), "child override became an unintended union");
                Check(ProtectedItemsManager.Contains(Other) && ForceMailManager.Contains(Other), "child override key changed with culture");
            }),
            ("numeric protected and mail declarations retain uppercase aliases", f =>
            {
                Culture("tr-TR"); var p = new Profile(XElement.Parse($"<HBProfile><ProtectedItems><ITEM ID=\"{Id}\"/></ProtectedItems><ForceMail><ITEM ENTRY=\"{Id}\"/></ForceMail></HBProfile>"), null); f.Activate(p);
                Check(ProtectedItemsManager.Contains(Id) && ForceMailManager.Contains(Id), "numeric alias control failed");
            }),
            ("independent profiles parse identical keys in both cultures", f =>
            {
                var english = f.Parse("en-US"); var turkish = f.Parse("tr-TR");
                Check(english.ProtectedItems.HashSet2.SetEquals(turkish.ProtectedItems.HashSet2)
                    && english.ForceMail.HashSet2.SetEquals(turkish.ForceMail.HashSet2), "identical XML produced different item identities");
            }),
            ("already lowercase declarations remain valid in Turkish", f =>
            {
                Culture("tr-TR"); f.Activate(new Profile(Xml(Lower), null));
                Check(ProtectedItemsManager.Contains(Name) && ForceMailManager.Contains(Name), "lowercase compatibility control failed");
            }),
        };
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { using var fixture = new Fixture(); item.Test(fixture); passed++; Console.WriteLine("PASS profile-item culture: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL profile-item culture assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR profile-item culture fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Profile-item culture scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual public parser and manager lookups; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Profile-item culture regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static XElement Xml(string name) => new("HBProfile",
        new XElement("ProtectedItems", new XElement("Item", new XAttribute("Name", name))),
        new XElement("ForceMail", new XElement("Item", new XAttribute("Name", name))));
    private static void Culture(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
    private sealed class Fixture : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly FieldInfo active = typeof(ProfileManager).GetField("_currentProfile", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo profileless = typeof(ProfileManager).GetField("_profileless", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? savedActive, savedProfileless;
        internal Fixture()
        {
            savedActive = active.GetValue(null); savedProfileless = profileless.GetValue(null);
            try
            {
                Culture("en-US"); profileless.SetValue(null, true); Activate(new Profile());
                Check(!ProtectedItemsManager.Contains(Name) && !ForceMailManager.Contains(Name)
                    && !ProtectedItemsManager.Contains(Other) && !ForceMailManager.Contains(Other), "unique profile names already owned outside profile layer");
            }
            catch { Dispose(); throw; }
        }
        internal Profile Parse(string language)
        {
            Culture(language); var profile = new Profile(Xml(Name), null); Activate(profile); return profile;
        }
        internal void Activate(Profile profile)
        {
            active.SetValue(null, profile);
            Check(ReferenceEquals(ProfileManager.CurrentProfile, profile), "active profile fixture did not bind");
        }
        public void Dispose()
        {
            try { active.SetValue(null, savedActive); profileless.SetValue(null, savedProfileless); }
            finally { CultureInfo.CurrentCulture = culture; }
        }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new AssertionFailure(text); }
}
