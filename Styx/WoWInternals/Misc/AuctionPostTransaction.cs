using System;
using System.Globalization;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals.Misc
{
    internal static class AuctionPostTransaction
    {
        internal static bool TryPostAtLocation(
            int luaBag,
            int luaSlot,
            long minBid,
            long buyout,
            int duration,
            uint stackSize,
            uint numStacks)
        {
            WoWItem item = ResolveContainerItem(luaBag, luaSlot);
            return item != null &&
                TryPost(item, minBid, buyout, duration, stackSize, numStacks);
        }

        internal static bool TryPost(
            WoWItem item,
            long minBid,
            long buyout,
            int duration,
            uint stackSize,
            uint numStacks)
        {
            if (item == null || !item.IsValid || item.Guid == 0UL || item.Entry == 0U ||
                minBid < 0L || buyout < 0L || duration < 1 || duration > 3 ||
                stackSize == 0U || numStacks == 0U || !IsAuctionSellContextOpen())
                return false;

            ulong expectedGuid = item.Guid;
            uint expectedEntry = item.Entry;
            int sourceBag;
            int sourceSlot;
            if (!item.TryPickUp(out sourceBag, out sourceSlot))
                return false;

            if (item.Guid != expectedGuid || item.Entry != expectedEntry)
            {
                TryRestoreOwnedCursor(sourceBag, sourceSlot, expectedEntry);
                return false;
            }

            bool transferred = false;
            try
            {
                transferred = Lua.GetReturnVal<bool>(
                    BuildSellTransferLua(sourceBag, sourceSlot, expectedEntry), 0U);
            }
            catch (Exception error)
            {
                Logging.WriteDebug(
                    "Auction sell transfer for item {0} ({1}) failed safely: {2}",
                    expectedGuid, expectedEntry, error.Message);
            }

            if (!transferred)
            {
                TryRestoreOwnedCursor(sourceBag, sourceSlot, expectedEntry);
                TryCancelOwnedSell(expectedEntry);
                return false;
            }

            try
            {
                if (Lua.GetReturnVal<bool>(
                    BuildStartAuctionLua(
                        expectedEntry, minBid, buyout, duration, stackSize, numStacks), 0U))
                    return true;
            }
            catch (Exception error)
            {
                Logging.WriteDebug(
                    "Auction submission for item {0} ({1}) failed safely: {2}",
                    expectedGuid, expectedEntry, error.Message);
            }

            TryCancelOwnedSell(expectedEntry);
            return false;
        }

        private static bool IsAuctionSellContextOpen()
        {
            try
            {
                return Lua.GetReturnVal<bool>(
                    "return AuctionFrame and AuctionFrame:IsShown() and " +
                    "AuctionFrameAuctions and AuctionFrameAuctions:IsShown() and true or false", 0U);
            }
            catch
            {
                return false;
            }
        }

        private static WoWItem ResolveContainerItem(int luaBag, int luaSlot)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null || me.Inventory == null || me.Inventory.Backpack == null)
                return null;

            ulong[] backpack = me.Inventory.Backpack.ItemGuids;
            var bags = new ulong[11][];
            for (uint index = 0; index <= 10U; index++)
            {
                WoWContainer bag = me.GetBagAtIndex(index);
                bags[index] = bag != null ? bag.ItemGuids : Array.Empty<ulong>();
            }

            ulong guid;
            if (!TryResolveContainerGuid(luaBag, luaSlot, backpack, bags, out guid))
                return null;

            WoWItem item = ObjectManager.GetObjectByGuid<WoWItem>(guid);
            return item != null && item.IsValid && item.Guid == guid ? item : null;
        }

        private static bool TryResolveContainerGuid(
            int luaBag,
            int luaSlot,
            ulong[] backpack,
            ulong[][] bags,
            out ulong guid)
        {
            guid = 0UL;
            if (luaBag < 0 || luaBag > 11 || luaSlot <= 0)
                return false;

            int slotIndex = luaSlot - 1;
            if (luaBag == 0)
            {
                if (backpack == null || slotIndex >= backpack.Length)
                    return false;
                guid = backpack[slotIndex];
                return guid != 0UL;
            }

            int bagIndex = luaBag - 1;
            if (bags == null || bagIndex < 0 || bagIndex >= bags.Length ||
                bags[bagIndex] == null || slotIndex >= bags[bagIndex].Length)
                return false;

            guid = bags[bagIndex][slotIndex];
            return guid != 0UL;
        }

        private static string BuildSellTransferLua(
            int sourceBag,
            int sourceSlot,
            uint expectedEntry)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "if not (AuctionFrame and AuctionFrame:IsShown() and AuctionFrameAuctions and AuctionFrameAuctions:IsShown()) then return false end; " +
                "local cursorType,cursorItemId=GetCursorInfo(); " +
                "if cursorType~='item' or not CursorHasItem() or tonumber(cursorItemId)~={2} then return false end; " +
                "local sourceLink=GetContainerItemLink({0},{1}); " +
                "if sourceLink then local sourceId=tonumber(string.match(sourceLink,'item:(%d+)')); if sourceId=={2} then return false end end; " +
                "local expectedName,_,expectedQuality,_,_,_,_,_,_,expectedTexture=GetItemInfo({2}); " +
                "if not expectedName then return false end; " +
                "ClickAuctionSellItemButton(); " +
                "if CursorHasItem() then return false end; " +
                "if not (AuctionFrame and AuctionFrame:IsShown() and AuctionFrameAuctions and AuctionFrameAuctions:IsShown()) then return false end; " +
                "local name,texture,count,quality=GetAuctionSellItemInfo(); " +
                "return name==expectedName and texture==expectedTexture and quality==expectedQuality and tonumber(count or 0)>0",
                sourceBag, sourceSlot, expectedEntry);
        }

        private static string BuildStartAuctionLua(
            uint expectedEntry,
            long minBid,
            long buyout,
            int duration,
            uint stackSize,
            uint numStacks)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "if not (AuctionFrame and AuctionFrame:IsShown() and AuctionFrameAuctions and AuctionFrameAuctions:IsShown()) then return false end; " +
                "if CursorHasItem() then return false end; " +
                "local expectedName,_,expectedQuality,_,_,_,_,_,_,expectedTexture=GetItemInfo({0}); " +
                "if not expectedName then return false end; " +
                "local name,texture,count,quality=GetAuctionSellItemInfo(); " +
                "if name~=expectedName or texture~=expectedTexture or quality~=expectedQuality or tonumber(count or 0)<=0 then return false end; " +
                "StartAuction({1},{2},{3},{4},{5}); return true",
                expectedEntry, minBid, buyout, duration, stackSize, numStacks);
        }

        private static bool TryRestoreOwnedCursor(
            int sourceBag,
            int sourceSlot,
            uint expectedEntry)
        {
            try
            {
                string script = string.Format(
                    CultureInfo.InvariantCulture,
                    "local cursorType,cursorItemId=GetCursorInfo(); " +
                    "if not cursorType then return true end; " +
                    "if cursorType~='item' or not CursorHasItem() or tonumber(cursorItemId)~={2} then return false end; " +
                    "if GetContainerItemLink({0},{1}) then return false end; " +
                    "PickupContainerItem({0},{1}); return not CursorHasItem()",
                    sourceBag, sourceSlot, expectedEntry);
                return Lua.GetReturnVal<bool>(script, 0U);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCancelOwnedSell(uint expectedEntry)
        {
            try
            {
                string script = string.Format(
                    CultureInfo.InvariantCulture,
                    "if not (AuctionFrame and AuctionFrame:IsShown() and AuctionFrameAuctions and AuctionFrameAuctions:IsShown()) then return false end; " +
                    "local expectedName,_,expectedQuality,_,_,_,_,_,_,expectedTexture=GetItemInfo({0}); " +
                    "if not expectedName then return false end; " +
                    "local name,texture,count,quality=GetAuctionSellItemInfo(); " +
                    "if name~=expectedName or texture~=expectedTexture or quality~=expectedQuality or tonumber(count or 0)<=0 then return false end; " +
                    "CancelSell(); return not GetAuctionSellItemInfo()",
                    expectedEntry);
                return Lua.GetReturnVal<bool>(script, 0U);
            }
            catch
            {
                return false;
            }
        }
    }
}
