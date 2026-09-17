using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Styx.Logic;
using Styx.Logic.Pathing;

// Real XML constructor/serializer and level-filtered lookup. No game, native
// navigation, filesystem writes or substitute flight-node implementation.
internal static class FlightNodePersistenceRegressionTests
{
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string message) : base(message) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Flight-node persistence tests require Windows x86.");
        var cases = new List<(string Name, Action Test)>();
        foreach (int level in new[] { 0, 1, 40, 80, int.MaxValue, -1 })
        {
            int expected = level;
            cases.Add(("stored update level " + expected + " survives XML loading", () =>
                Check(Load(expected).UpdateLevel == expected, "stored UpdateLevel was lost")));
        }
        cases.Add(("missing update level retains the legacy zero default", () =>
            Check(new XmlFlightNode(new XElement("Node")).UpdateLevel == 0, "legacy default changed")));
        cases.Add(("normal constructor still records the observed update level", () =>
            Check(Create(80).UpdateLevel == 80, "direct constructor changed")));
        cases.Add(("serializer emits the observed update level", () =>
            Check((int?)Create(80).ToXml().Attribute("UpdateLevel") == 80, "serialized level changed")));
        cases.Add(("full node survives serialize parse reconstruct", () => RoundTrip("en-US")));
        cases.Add(("node round trip is independent of decimal-comma culture", () => RoundTrip("de-DE")));
        cases.Add(("node round trip is independent of Turkish culture", () => RoundTrip("tr-TR")));
        cases.Add(("repeated round trips do not erase the original update level", () =>
        {
            var node = Create(40);
            for (int i = 0; i < 4; i++) node = new XmlFlightNode(XElement.Parse(node.ToXml().ToString()));
            Check(node.UpdateLevel == 40, "repeated round trip lost level");
        }));
        cases.Add(("level-filtered lookup rejects a loaded node above the current level", () => Lookup(79, false)));
        cases.Add(("level-filtered lookup accepts a loaded node at its observed level", () => Lookup(80, true)));
        cases.Add(("level-filtered lookup accepts a loaded node below the current level", () => Lookup(81, true)));
        cases.Add(("nonnumeric stored level preserves explicit parse failure", () => ParseFails("not-a-level", false)));
        cases.Add(("out-of-range stored level preserves explicit overflow failure", () => ParseFails("2147483648", true)));
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Test(); passed++; Console.WriteLine("PASS flight-node persistence: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL flight-node persistence assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR flight-node persistence fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Flight-node persistence scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual XML and lookup owners; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException($"Flight-node persistence regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static XmlFlightNode Create(int level)
    {
        var node = new XmlFlightNode(54321U, level, "Flight & <Gate>", 1U, new WoWPoint(-12.5f, 30.25f, 71.75f));
        node.Connect("Destination A");
        node.Connect("Destination B");
        return node;
    }
    private static XmlFlightNode Load(int level) => new XmlFlightNode(new XElement("Node",
        new XAttribute("Name", "Saved"), new XAttribute("MasterEntry", 54321U), new XAttribute("UpdateLevel", level)));
    private static void RoundTrip(string culture)
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var original = Create(80);
            var loaded = new XmlFlightNode(XElement.Parse(original.ToXml().ToString()));
            Check(loaded.Name == original.Name && loaded.MasterEntry == original.MasterEntry && loaded.Continent == original.Continent,
                "identity did not round-trip");
            Check(loaded.Location.X.Equals(original.Location.X) && loaded.Location.Y.Equals(original.Location.Y) &&
                loaded.Location.Z.Equals(original.Location.Z) && loaded.Connections.SetEquals(original.Connections), "geometry or connections changed");
            Check(loaded.UpdateLevel == original.UpdateLevel, "round-trip lost UpdateLevel");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUiCulture; }
    }
    private static void Lookup(int level, bool expected)
    {
        var oldNodes = FlightPaths.XmlNodes;
        try
        {
            var loaded = new XmlFlightNode(XElement.Parse(Create(80).ToXml().ToString()));
            FlightPaths.XmlNodes = new List<XmlFlightNode> { loaded };
            var method = typeof(FlightPaths).GetMethod("FindNodeByMasterEntry", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Actual lookup method not found");
            var found = method.Invoke(null, new object[] { 54321U, level });
            Check(expected ? ReferenceEquals(found, loaded) : found == null, "loaded level changed actual lookup eligibility");
        }
        finally { FlightPaths.XmlNodes = oldNodes; }
    }
    private static void ParseFails(string value, bool overflow)
    {
        try { _ = new XmlFlightNode(new XElement("Node", new XAttribute("UpdateLevel", value))); }
        catch (FormatException) when (!overflow) { return; }
        catch (OverflowException) when (overflow) { return; }
        throw new AssertionFailure("invalid stored level was silently accepted");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
}
