using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.WoWInternals;

internal static class AuctionPostOwnershipRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string legacyPath = Path.Combine(root, "Styx", "WoWInternals", "Misc", "AuctionHouse.cs");
        string framePath = Path.Combine(root, "Styx", "Logic", "Inventory", "Frames", "AuctionHouse", "AuctionHouse.cs");
        string ownerPath = Path.Combine(root, "Styx", "WoWInternals", "Misc", "AuctionPostTransaction.cs");
        string legacy = File.ReadAllText(legacyPath);
        string frame = File.ReadAllText(framePath);
        string owner = File.Exists(ownerPath) ? File.ReadAllText(ownerPath) : string.Empty;

        Type? ownerType = typeof(Lua).Assembly.GetType("Styx.WoWInternals.Misc.AuctionPostTransaction", false);
        MethodInfo? resolver = ownerType?.GetMethod("TryResolveContainerGuid", Hidden);
        MethodInfo? transferBuilder = ownerType?.GetMethod("BuildSellTransferLua", Hidden);
        MethodInfo? startBuilder = ownerType?.GetMethod("BuildStartAuctionLua", Hidden);

        var cases = new List<(string Name, Action Test)>
        {
            ("shared AuctionPostTransaction owner exists", () =>
                Check(ownerType != null && owner.Length > 0,
                    "Auction posting still has no shared transaction owner")),

            ("legacy AuctionHouse wrapper delegates instead of mutating cursor directly", () =>
            {
                string region = MethodRegion(legacy, "public static void PostAuction(long minBid");
                Check(region.Contains("AuctionPostTransaction", StringComparison.Ordinal)
                    && !region.Contains("ClearCursor", StringComparison.Ordinal)
                    && !region.Contains("Thread.Sleep", StringComparison.Ordinal)
                    && !region.Contains("ClickAuctionSellItemButton", StringComparison.Ordinal)
                    && !region.Contains("StartAuction(", StringComparison.Ordinal),
                    "legacy PostAuction still owns raw cursor/sell/start mutations");
            }),

            ("frame AuctionHouse wrapper resolves a stable item owner instead of raw bag mutation", () =>
            {
                string region = MethodRegion(frame, "public static void PostAuction(int bag");
                Check(region.Contains("AuctionPostTransaction", StringComparison.Ordinal)
                    && !region.Contains("PickupContainerItem", StringComparison.Ordinal)
                    && !region.Contains("ClickAuctionSellItemButton", StringComparison.Ordinal)
                    && !region.Contains("StartAuction(", StringComparison.Ordinal),
                    "frame PostAuction still posts directly from caller bag/slot coordinates");
            }),

            ("coordinate resolver exists", () =>
                Check(resolver != null, "shared owner lacks a pure container GUID resolver")),

            ("coordinate resolver maps backpack slot with one-based Lua indexing", () =>
            {
                ulong guid;
                Check(Resolve(resolver, 0, 2, new ulong[] { 11, 22, 0 }, Array.Empty<ulong[]>(), out guid)
                    && guid == 22,
                    "bag0 slot2 did not resolve the physical item GUID");
            }),

            ("coordinate resolver maps equipped bag numbering", () =>
            {
                ulong guid;
                var bags = new[] { new ulong[] { 33 }, new ulong[] { 44, 55 } };
                Check(Resolve(resolver, 1, 1, Array.Empty<ulong>(), bags, out guid) && guid == 33,
                    "Lua bag1 did not map to first physical bag");
                Check(Resolve(resolver, 2, 2, Array.Empty<ulong>(), bags, out guid) && guid == 55,
                    "Lua bag2 slot2 did not map to the expected GUID");
            }),

            ("coordinate resolver fails closed for zero/out-of-range/empty slots", () =>
            {
                ulong guid;
                Check(!Resolve(resolver, 0, 0, new ulong[] { 11 }, Array.Empty<ulong[]>(), out guid), "slot0 became valid");
                Check(!Resolve(resolver, 0, 2, new ulong[] { 11 }, Array.Empty<ulong[]>(), out guid), "out-of-range backpack slot became valid");
                Check(!Resolve(resolver, 1, 1, Array.Empty<ulong>(), new[] { new ulong[] { 0 } }, out guid), "empty bag slot became an item");
                Check(!Resolve(resolver, 12, 1, Array.Empty<ulong>(), Array.Empty<ulong[]>(), out guid), "unsupported bag became valid");
            }),

            ("item posting uses validated TryPickUp source ownership", () =>
            {
                Check(owner.Contains("TryPickUp(out", StringComparison.Ordinal)
                    && owner.Contains("expectedGuid", StringComparison.Ordinal)
                    && owner.Contains("expectedEntry", StringComparison.Ordinal),
                    "shared owner does not retain GUID/entry through validated pickup");
            }),

            ("sell-transfer Lua builder exists", () =>
                Check(transferBuilder != null, "shared owner lacks sell-transfer builder")),

            ("sell transfer validates exact cursor entry before ClickAuctionSellItemButton", () =>
            {
                string lua = BuildTransfer(transferBuilder, 2, 3, 7586);
                int cursor = lua.IndexOf("GetCursorInfo", StringComparison.Ordinal);
                int expected = lua.IndexOf("7586", StringComparison.Ordinal);
                int click = lua.IndexOf("ClickAuctionSellItemButton", StringComparison.Ordinal);
                Check(cursor >= 0 && expected > cursor && click > expected
                    && lua.Contains("CursorHasItem", StringComparison.Ordinal),
                    "sell transfer can click before proving the expected item owns the cursor");
            }),

            ("sell transfer requires live AuctionFrame auctions context before mutation", () =>
            {
                string lua = BuildTransfer(transferBuilder, 2, 3, 7586);
                int frameCheck = lua.IndexOf("AuctionFrame", StringComparison.Ordinal);
                int auctionsPanel = lua.IndexOf("AuctionFrameAuctions", StringComparison.Ordinal);
                int click = lua.IndexOf("ClickAuctionSellItemButton", StringComparison.Ordinal);
                Check(frameCheck >= 0 && auctionsPanel > frameCheck && click > auctionsPanel
                    && lua.Contains("IsShown", StringComparison.Ordinal),
                    "sell transfer does not prove the original 3.3.5 Auctions panel is active before mutation");
            }),

            ("sell transfer acknowledges cursor release and expected sell-item metadata", () =>
            {
                string lua = BuildTransfer(transferBuilder, 2, 3, 7586);
                int click = lua.IndexOf("ClickAuctionSellItemButton", StringComparison.Ordinal);
                int sellInfo = lua.IndexOf("GetAuctionSellItemInfo", click < 0 ? 0 : click, StringComparison.Ordinal);
                int itemInfo = lua.IndexOf("GetItemInfo", StringComparison.Ordinal);
                Check(click >= 0 && sellInfo > click && itemInfo >= 0
                    && lua.Contains("CursorHasItem", StringComparison.Ordinal)
                    && lua.Contains("7586", StringComparison.Ordinal),
                    "sell transfer has no acknowledged sell-slot identity after cursor release");
            }),

            ("sell transfer never clears an unowned cursor", () =>
            {
                string lua = BuildTransfer(transferBuilder, 2, 3, 7586);
                Check(!lua.Contains("ClearCursor", StringComparison.Ordinal),
                    "sell-transfer owner still uses ClearCursor");
            }),

            ("start-auction Lua builder exists", () =>
                Check(startBuilder != null, "shared owner lacks late checked StartAuction builder")),

            ("StartAuction is gated by late frame/sell-item revalidation", () =>
            {
                string lua = BuildStart(startBuilder, 7586, 100, 200, 2, 5, 3);
                int frameCheck = lua.IndexOf("AuctionFrame", StringComparison.Ordinal);
                int sellInfo = lua.IndexOf("GetAuctionSellItemInfo", StringComparison.Ordinal);
                int start = lua.IndexOf("StartAuction(", StringComparison.Ordinal);
                Check(frameCheck >= 0 && sellInfo > frameCheck && start > sellInfo
                    && lua.Contains("AuctionFrameAuctions", StringComparison.Ordinal)
                    && lua.Contains("GetItemInfo", StringComparison.Ordinal)
                    && lua.Contains("7586", StringComparison.Ordinal),
                    "StartAuction can run without late context and selected-item acknowledgement");
            }),

            ("StartAuction submission does not use sleep as acknowledgement", () =>
            {
                Check(!owner.Contains("Thread.Sleep", StringComparison.Ordinal),
                    "auction owner still treats fixed sleep as posting acknowledgement");
            }),

            ("failure cleanup is ownership-scoped", () =>
            {
                Check(owner.Contains("TryRestoreOwnedCursor", StringComparison.Ordinal)
                    && owner.Contains("TryCancelOwnedSell", StringComparison.Ordinal)
                    && !owner.Contains("ClearCursor", StringComparison.Ordinal),
                    "failed auction transfer has no ownership-scoped cursor/sell cleanup");
            }),

            ("query bid buyout and cancel owners remain outside cursor repair", () =>
            {
                Check(legacy.Contains("PerformSearch", StringComparison.Ordinal)
                    && legacy.Contains("CancelAuction", StringComparison.Ordinal)
                    && frame.Contains("PlaceBid", StringComparison.Ordinal)
                    && frame.Contains("Buyout", StringComparison.Ordinal)
                    && frame.Contains("CancelAuction", StringComparison.Ordinal),
                    "cursor repair removed unrelated AuctionHouse operations");
            })
        };

        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var c in cases)
        {
            try { c.Test(); passed++; Console.WriteLine("PASS auction post ownership: " + c.Name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL auction post ownership: " + c.Name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR auction post ownership: " + c.Name + ": " + e); }
        }

        Console.WriteLine($"Auction post ownership scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; source/pure-owner contract; no auction/game attached.");
        if (assertions + unexpected != 0)
            throw new InvalidOperationException("Auction post ownership regression");
    }

    private static bool Resolve(MethodInfo? method, int bag, int slot, ulong[] backpack, ulong[][] bags, out ulong guid)
    {
        if (method == null) throw new Failure("TryResolveContainerGuid is missing");
        object?[] args = { bag, slot, backpack, bags, 0UL };
        bool ok = (bool)(method.Invoke(null, args) ?? false);
        guid = (ulong)args[4]!;
        return ok;
    }

    private static string BuildTransfer(MethodInfo? method, int bag, int slot, uint entry)
    {
        if (method == null) throw new Failure("BuildSellTransferLua is missing");
        return Convert.ToString(method.Invoke(null, new object[] { bag, slot, entry }), CultureInfo.InvariantCulture) ?? "";
    }

    private static string BuildStart(MethodInfo? method, uint entry, long bid, long buyout, int duration, uint stack, uint count)
    {
        if (method == null) throw new Failure("BuildStartAuctionLua is missing");
        return Convert.ToString(method.Invoke(null, new object[] { entry, bid, buyout, duration, stack, count }), CultureInfo.InvariantCulture) ?? "";
    }

    private static string MethodRegion(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new Failure(marker + " is missing");
        int brace = source.IndexOf('{', start);
        if (brace < 0) return source.Substring(start, Math.Min(5000, source.Length - start));
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(start, i - start + 1);
            }
        }
        return source.Substring(start);
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new Failure("tracked checkout required");
    }

    private static void Check(bool ok, string why)
    {
        if (!ok) throw new Failure(why);
    }
}
