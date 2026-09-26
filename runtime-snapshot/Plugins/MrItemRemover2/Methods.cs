using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Styx;
using Styx.CommonBot;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace MrItemRemover2
{
    public partial class MrItemRemover2
    {
        private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(10);
        private ulong _pendingDeleteGuid;
        private uint _pendingDeleteEntry;
        private DateTime _pendingDeleteSince;
        private bool _pendingDeleteRequested;
        private object _deleteLifetime;
        private object _pendingDeleteLifetime;
        private object _pendingDeleteToken;
        private LocalPlayer _pendingDeletePlayer;
        private ulong _pendingDeletePlayerGuid;

        // One trigger owns one finite candidate set. A refused/returned item is
        // visited once; later arrivals belong to a new independently triggered pass.
        private sealed class ItemScan
        {
            internal LocalPlayer Player;
            internal ulong PlayerGuid;
            internal object Lifetime;
            internal Queue<WoWItem> Remaining;
        }
        private ItemScan _itemScan;
        private bool _isScanningItems;

        private bool HasPendingDelete
        {
            get { return _pendingDeleteGuid != 0 && _pendingDeleteEntry != 0; }
        }

        public void SellVenderItems(object sender, LuaEventArgs args)
        {
            if (MerchantFrame.Instance.IsVisible && IsInitialized &&
                MrItemRemover2Settings.Instance.EnableSell == "True")
            {
                LoadList(FoodList, _foodListPath);
                LoadList(DrinkList, _drinkListPath);
                LoadList(ItemNameSell, _sellListPath);
                LoadList(BagList, _bagListPath);

                foreach (WoWItem item in Me.BagItems)
                {
                    if (MrItemRemover2Settings.Instance.SellSoulbound == "False")
                    {
                        if (!item.IsSoulbound && !KeepList.Contains(item.Name) && !FoodList.Contains(item.Name) &&
                            !DrinkList.Contains(item.Name))
                        {
                            if (item.Quality == WoWItemQuality.Poor &&
                                MrItemRemover2Settings.Instance.SellGray == "True")
                            {
                                Slog("Selling Gray Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (item.Quality == WoWItemQuality.Common &&
                                MrItemRemover2Settings.Instance.SellWhite == "True" && !FoodList.Contains(item.Name) &&
                                !DrinkList.Contains(item.Name))
                            {
                                Slog("Selling White Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (item.Quality == WoWItemQuality.Uncommon &&
                                MrItemRemover2Settings.Instance.SellGreen == "True")
                            {
                                Slog("Selling Green Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (item.Quality == WoWItemQuality.Rare &&
                                MrItemRemover2Settings.Instance.SellBlue == "True")
                            {
                                Slog("Selling Blue Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (ItemNameSell.Contains(item.Name))
                            {
                                Slog("Item Matched List Selling {0}", item.Name);
                                item.UseContainerItem();
                            }

                            if (item.Quality == WoWItemQuality.Common && FoodList.Contains(item.Name) &&
                                MrItemRemover2Settings.Instance.SellFood == "True" && item.ItemInfo.RequiredLevel <= MrItemRemover2Settings.Instance.ReqRefLvl)
                            {
                                Slog("Item Matched Selling Food List {0}", item.Name);
                                item.UseContainerItem();
                            }

                            if (item.Quality == WoWItemQuality.Common && DrinkList.Contains(item.Name) &&
                                MrItemRemover2Settings.Instance.SellDrinks == "True" && item.ItemInfo.RequiredLevel <= MrItemRemover2Settings.Instance.ReqRefLvl)
                            {
                                Slog("Item Matched Selling Food List {0}", item.Name);
                                item.UseContainerItem();
                            }
                        }
                    }

                    if (MrItemRemover2Settings.Instance.SellSoulbound == "True")
                    {
                        if (!KeepList.Contains(item.Name) && !FoodList.Contains(item.Name) &&
                            !DrinkList.Contains(item.Name))
                        {
                            if (item.Quality == WoWItemQuality.Poor &&
                                MrItemRemover2Settings.Instance.SellGray == "True")
                            {
                                Slog("Selling Gray Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if ((item.Quality == WoWItemQuality.Common &&
                                 MrItemRemover2Settings.Instance.SellWhite == "True" && !FoodList.Contains(item.Name) &&
                                !DrinkList.Contains(item.Name)))
                            {
                                Slog("Selling White Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (item.Quality == WoWItemQuality.Uncommon &&
                                MrItemRemover2Settings.Instance.SellGreen == "True")
                            {
                                Slog("Selling Green Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (item.Quality == WoWItemQuality.Rare &&
                                MrItemRemover2Settings.Instance.SellBlue == "True")
                            {
                                Slog("Selling Blue Item {0}", item.Name);
                                item.UseContainerItem();
                            }
                            if (ItemNameSell.Contains(item.Name))
                            {
                                Slog("Item Matched List Selling {0}", item.Name);
                                item.UseContainerItem();
                            }

                            if (item.Quality == WoWItemQuality.Common && FoodList.Contains(item.Name) &&
                                MrItemRemover2Settings.Instance.SellFood == "True" && item.ItemInfo.RequiredLevel <= MrItemRemover2Settings.Instance.ReqRefLvl)
                            {
                                Slog("Item Matched Selling Food List {0}", item.Name);
                                item.UseContainerItem();
                            }

                            if (item.Quality == WoWItemQuality.Common && DrinkList.Contains(item.Name) &&
                                MrItemRemover2Settings.Instance.SellDrinks == "True" && item.ItemInfo.RequiredLevel <= MrItemRemover2Settings.Instance.ReqRefLvl)
                            {
                                Slog("Item Matched Selling Drink List {0}", item.Name);
                                item.UseContainerItem();
                            }
                        }
                    }
                }
            }
        }

        private void DeleteItemConfirmPopup(object sender, LuaEventArgs args)
        {
            if (!HasPendingDelete || !OwnsPendingDeleteContext())
                return;

            object token = _pendingDeleteToken;
            try
            {
                int popup = Lua.GetReturnVal<int>(
                    "local good=StaticPopup_FindVisible('DELETE_GOOD_ITEM'); " +
                    "if good and good.which=='DELETE_GOOD_ITEM' then return 2 end; " +
                    "local normal=StaticPopup_FindVisible('DELETE_ITEM'); " +
                    "if normal and normal.which=='DELETE_ITEM' then return 1 end; return 0", 0U);
                if (popup == 0 || !ReferenceEquals(token, _pendingDeleteToken) || !OwnsPendingDeleteContext())
                    return;

                int confirmation = TryConfirmPendingDelete();
                if (confirmation > 0 && ReferenceEquals(token, _pendingDeleteToken) && OwnsPendingDeleteContext())
                    Slog("Confirming owned removal for item entry {0}", _pendingDeleteEntry);
            }
            catch (Exception error)
            {
                Dlog("Delete confirmation event failed safely: {0}", error.Message);
            }
        }

        private void BeginDelete(WoWItem item)
        {
            if (HasPendingDelete || !CanDeleteNow() || item == null || !item.IsValid ||
                item.Guid == 0 || item.Entry == 0)
                return;

            LocalPlayer player = Me;
            ulong expectedGuid = item.Guid;
            uint expectedEntry = item.Entry;
            object token = new object();
            _pendingDeleteToken = token;
            _pendingDeleteLifetime = _deleteLifetime;
            _pendingDeletePlayer = player;
            _pendingDeletePlayerGuid = player.Guid;
            _pendingDeleteGuid = expectedGuid;
            _pendingDeleteEntry = expectedEntry;
            _pendingDeleteSince = DateTime.UtcNow;
            _pendingDeleteRequested = false;

            try
            {
                if (!OwnsPendingDeleteContext())
                {
                    ReleasePendingDelete(token);
                    return;
                }
                if (!item.TryPickUp())
                {
                    ReleasePendingDelete(token);
                    Dlog("Delete pickup refused for {0} ({1}); cursor/slot ownership was not changed.",
                        item.Name, expectedEntry);
                    return;
                }
                if (!ReferenceEquals(token, _pendingDeleteToken) || !OwnsPendingDeleteContext() ||
                    !item.IsValid || item.Guid != expectedGuid || item.Entry != expectedEntry)
                {
                    ReleasePendingDelete(token);
                    return;
                }
                TryIssueDeleteRequest();
            }
            catch
            {
                ReleasePendingDelete(token);
                throw;
            }
        }

        private void TickPendingDelete()
        {
            if (!HasPendingDelete)
                return;

            object token = _pendingDeleteToken;
            if (!OwnsPendingDeleteContext())
            {
                ReleasePendingDelete(token);
                return;
            }

            if (DateTime.UtcNow - _pendingDeleteSince >= DeleteTimeout)
            {
                Slog("Delete transaction for entry {0} timed out; cursor ownership is left untouched.",
                    _pendingDeleteEntry);
                ReleasePendingDelete(token);
                return;
            }

            int cursorState = ReadOwnedCursorState(_pendingDeleteEntry);
            if (!ReferenceEquals(token, _pendingDeleteToken))
                return;
            if (!OwnsPendingDeleteContext())
            {
                ReleasePendingDelete(token);
                return;
            }
            if (cursorState == 0)
            {
                bool? stillObserved = PendingDeleteItemStillObserved();
                if (!ReferenceEquals(token, _pendingDeleteToken))
                    return;
                if (!OwnsPendingDeleteContext())
                {
                    ReleasePendingDelete(token);
                    return;
                }
                if (!stillObserved.HasValue)
                    return;

                if (!stillObserved.Value)
                {
                    if (_pendingDeleteRequested)
                        Slog("Requested item {0} ({1}) is no longer observed; ending local delete tracking.",
                            _pendingDeleteGuid, _pendingDeleteEntry);
                    else
                        Dlog("Item {0} ({1}) is no longer observed without a local delete request; revoking pending intent.",
                            _pendingDeleteGuid, _pendingDeleteEntry);
                    ReleasePendingDelete(token);
                    return;
                }

                Dlog("Item {0} ({1}) returned to inventory; retry will require a fresh pickup.",
                    _pendingDeleteGuid, _pendingDeleteEntry);
                ReleasePendingDelete(token);
                return;
            }

            if (cursorState != 1)
                return;

            if (!_pendingDeleteRequested)
                TryIssueDeleteRequest();
            else
                TryConfirmPendingDelete();
        }

        private bool CanDeleteNow()
        {
            try
            {
                LocalPlayer player = Me;
                object lifetime = _deleteLifetime;
                if (!IsInitialized || lifetime == null ||
                    !Styx.Logic.BehaviorTree.TreeRoot.IsRunning || Styx.Logic.BehaviorTree.TreeRoot.IsPaused ||
                    !StyxWoW.IsInGame || player == null || !player.IsValid || player.Guid == 0 ||
                    !player.IsAlive || player.IsGhost || player.Combat || player.IsCasting ||
                    MrItemRemover2Settings.Instance.EnableRemove != "True")
                    return false;

                ulong guid = player.Guid;
                return ReferenceEquals(Me, player) && player.IsValid && player.Guid == guid &&
                    player.IsAlive && !player.IsGhost && !player.Combat && !player.IsCasting &&
                    IsInitialized && ReferenceEquals(lifetime, _deleteLifetime) &&
                    Styx.Logic.BehaviorTree.TreeRoot.IsRunning && !Styx.Logic.BehaviorTree.TreeRoot.IsPaused &&
                    StyxWoW.IsInGame;
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                error is not System.Threading.ThreadInterruptedException)
            {
                return false;
            }
        }

        private bool OwnsPendingDeleteContext()
        {
            object token = _pendingDeleteToken;
            object lifetime = _pendingDeleteLifetime;
            LocalPlayer player = _pendingDeletePlayer;
            ulong guid = _pendingDeletePlayerGuid;
            return HasPendingDelete && token != null && lifetime != null && player != null && guid != 0 &&
                ReferenceEquals(lifetime, _deleteLifetime) && CanDeleteNow() &&
                ReferenceEquals(token, _pendingDeleteToken) && ReferenceEquals(lifetime, _deleteLifetime) &&
                ReferenceEquals(player, _pendingDeletePlayer) && ReferenceEquals(Me, player) &&
                player.Guid == guid && _pendingDeletePlayerGuid == guid;
        }

        private void ReleasePendingDelete(object token)
        {
            // A callback result can retire only the operation that requested it.
            if (token != null && ReferenceEquals(token, _pendingDeleteToken))
                ResetPendingDelete();
        }

        private void ResetDeleteLifetime(EventArgs args)
        {
            _deleteLifetime = new object();
            _itemScan = null;
            ResetPendingDelete();
            EnableCheck = false;
            ManualCheckRequested = false;
        }

        private bool? PendingDeleteItemStillObserved()
        {
            // A failed/incomplete read is not an empty inventory. Capture one view
            // and reject observations that outlive their player or pending item.
            try
            {
                if (!HasPendingDelete)
                    return null;

                object token = _pendingDeleteToken;
                LocalPlayer player = Me;
                if (player == null || !player.IsValid || player.Guid == 0)
                    return null;

                ulong playerGuid = player.Guid;
                ulong expectedGuid = _pendingDeleteGuid;
                uint expectedEntry = _pendingDeleteEntry;
                var inventory = player.BagItems;
                if (inventory == null)
                    return null;

                WoWItem[] items = inventory.ToArray();
                bool? observed;
                if (items.Any(item => item == null || !item.IsValid || item.Guid == 0))
                    observed = null;
                else if (items.Any(item => item.Guid == expectedGuid))
                    observed = true;
                else
                {
                    WoWItem candidate = ObjectManager.GetObjectByGuid<WoWItem>(expectedGuid);
                    observed = candidate == null ? (bool?)false
                        : candidate.IsValid && candidate.Guid == expectedGuid ? true : (bool?)null;
                }

                return ReferenceEquals(token, _pendingDeleteToken) &&
                    ReferenceEquals(Me, player) && player.IsValid && player.Guid == playerGuid &&
                    _pendingDeleteGuid == expectedGuid && _pendingDeleteEntry == expectedEntry
                    ? observed : null;
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                error is not System.Threading.ThreadInterruptedException)
            {
                return null;
            }
        }

        private static int ReadOwnedCursorState(uint expectedEntry)
        {
            if (expectedEntry == 0)
                return 2;

            string lua = string.Format(
                CultureInfo.InvariantCulture,
                "local cursorType,cursorItemId=GetCursorInfo(); " +
                "if not cursorType then return 0 end; " +
                "if cursorType=='item' and CursorHasItem() and tonumber(cursorItemId)=={0} then return 1 end; " +
                "return 2",
                expectedEntry);
            try
            {
                return Lua.GetReturnVal<int>(lua, 0U);
            }
            catch
            {
                return 2;
            }
        }

        private void TryIssueDeleteRequest()
        {
            if (!HasPendingDelete || !OwnsPendingDeleteContext())
                return;

            object token = _pendingDeleteToken;
            try
            {
                bool submitted = Lua.GetReturnVal<bool>(
                    BuildOwnedDeleteRequestLua(_pendingDeleteEntry), 0U);
                if (submitted && ReferenceEquals(token, _pendingDeleteToken) && OwnsPendingDeleteContext())
                    _pendingDeleteRequested = true;
            }
            catch (Exception error)
            {
                Dlog("Owned delete request failed safely: {0}", error.Message);
            }
        }

        private int TryConfirmPendingDelete()
        {
            if (!HasPendingDelete || !_pendingDeleteRequested || !OwnsPendingDeleteContext())
                return 0;

            object token = _pendingDeleteToken;
            try
            {
                int confirmation = Lua.GetReturnVal<int>(
                    BuildOwnedDeleteConfirmationLua(_pendingDeleteEntry), 0U);
                return ReferenceEquals(token, _pendingDeleteToken) && OwnsPendingDeleteContext()
                    ? confirmation : 0;
            }
            catch (Exception error)
            {
                Dlog("Owned delete confirmation failed safely: {0}", error.Message);
                return 0;
            }
        }

        private static string BuildOwnedDeleteRequestLua(uint expectedEntry)
        {
            if (expectedEntry == 0)
                throw new ArgumentOutOfRangeException("expectedEntry");

            return string.Format(
                CultureInfo.InvariantCulture,
                "local cursorType,cursorItemId=GetCursorInfo(); " +
                "if cursorType~='item' or not CursorHasItem() or tonumber(cursorItemId)~={0} then return false end; " +
                "if StaticPopup_FindVisible('DELETE_ITEM') or StaticPopup_FindVisible('DELETE_GOOD_ITEM') then return false end; " +
                "DeleteCursorItem(); return true",
                expectedEntry);
        }

        private static string BuildOwnedDeleteConfirmationLua(uint expectedEntry)
        {
            if (expectedEntry == 0)
                throw new ArgumentOutOfRangeException("expectedEntry");

            return string.Format(
                CultureInfo.InvariantCulture,
                "local cursorType,cursorItemId=GetCursorInfo(); " +
                "if cursorType~='item' or not CursorHasItem() or tonumber(cursorItemId)~={0} then return 0 end; " +
                "local good=StaticPopup_FindVisible('DELETE_GOOD_ITEM'); " +
                "if good and good.which=='DELETE_GOOD_ITEM' then " +
                " if not good.editBox or not good.button1 then return -1 end; " +
                " good.editBox:SetText(DELETE_ITEM_CONFIRM_STRING); " +
                " if good.button1:IsEnabled()==1 then good.button1:Click(); return 2 end; return 1 end; " +
                "local normal=StaticPopup_FindVisible('DELETE_ITEM'); " +
                "if normal and normal.which=='DELETE_ITEM' then " +
                " if not normal.button1 then return -1 end; normal.button1:Click(); return 2 end; " +
                "return 0",
                expectedEntry);
        }

        private void ResetPendingDelete()
        {
            _pendingDeleteToken = null;
            _pendingDeleteLifetime = null;
            _pendingDeletePlayer = null;
            _pendingDeletePlayerGuid = 0;
            _pendingDeleteGuid = 0;
            _pendingDeleteEntry = 0;
            _pendingDeleteSince = DateTime.MinValue;
            _pendingDeleteRequested = false;
        }

        public void PrintSettings()
        {
            Dlog("Mr.ItemRemover2 Settings");
            Dlog("------------------------------------------");
            foreach (var setting in MrItemRemover2Settings.Instance.GetSettings())
            {
                string key = setting.Key;
                object value = setting.Value;
                Dlog(string.Format("{0} - {1}", key, value));
            }
            Dlog("------------------------------------------");
            Dlog(" ");
        }

        public void CheckForItems()
        {
            if (_isScanningItems)
                return;

            _isScanningItems = true;
            ItemScan scan = _itemScan;
            try
            {
                if (HasPendingDelete)
                {
                    TickPendingDelete();
                    return;
                }

                if (scan == null)
                {
                    LocalPlayer player = Me;
                    scan = new ItemScan
                    {
                        Player = player,
                        PlayerGuid = player != null ? player.Guid : 0,
                        Lifetime = _deleteLifetime
                    };
                    _itemScan = scan;
                    if (!OwnsItemScan(scan))
                    {
                        EndItemScan(scan);
                        return;
                    }

                    LoadList(ItemName, _removeListPath);
                    LoadList(BagList, _bagListPath);
                    var inventory = player.BagItems;
                    WoWItem[] snapshot = inventory != null ? inventory.ToArray() : null;
                    if (!OwnsItemScan(scan) || snapshot == null ||
                        snapshot.Any(item => item == null || !item.IsValid || item.Guid == 0))
                    {
                        EndItemScan(scan);
                        Dlog("Item scan deferred because its inventory snapshot is unavailable or incomplete.");
                        return;
                    }
                    scan.Remaining = new Queue<WoWItem>(snapshot.GroupBy(item => item.Guid).Select(group => group.First()));
                }

                while (scan.Remaining != null && scan.Remaining.Count > 0)
                {
                    if (!OwnsItemScan(scan))
                    {
                        EndItemScan(scan);
                        return;
                    }
                    WoWItem item = scan.Remaining.Dequeue();
                    if (item == null || !item.IsValid)
                        continue;
                    ulong itemGuid = item.Guid;
                    uint itemEntry = item.Entry;
                    StyxWoW.SleepForLagDuration();
                    if (!OwnsItemScan(scan))
                    {
                        EndItemScan(scan);
                        return;
                    }
                    var currentInventory = scan.Player.BagItems;
                    if (currentInventory == null)
                    {
                        EndItemScan(scan);
                        Dlog("Item scan deferred because current inventory is unavailable.");
                        return;
                    }
                    if (!item.IsValid || item.Guid != itemGuid || item.Entry != itemEntry ||
                        !currentInventory.Any(candidate => ReferenceEquals(candidate, item)))
                        continue;

                    bool isQuestItem = IsQuestItem(item);
                    if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                    {
                        EndItemScan(scan);
                        return;
                    }

                    if (BagList.Contains(item.Name))
                    {
                        Slog("{0} is a bag, ignoring.", item.Name);
                        continue;
                    }

                    if (OpnList.Contains(item.Name) && item.IsOpenable &&
                        MrItemRemover2Settings.Instance.EnableOpen == "True")
                    {
                        Slog("{0} can be opened. Opening.", item.Name);
                        if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                        {
                            EndItemScan(scan);
                            return;
                        }
                        Lua.DoString("UseItemByName(\"" + item.Name + "\")");
                    }

                    if (OpnList.Contains(item.Name) && item.StackCount == 1)
                    {
                        Slog("{0} can be opened, so we're opening it.", item.Name);
                        if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                        {
                            EndItemScan(scan);
                            return;
                        }
                        Lua.DoString("UseItemByName(\"" + item.Name + "\")");
                    }

                    if (Combine3List.Contains(item.Name) && item.StackCount >= 3)
                    {
                        uint timesToUse = (uint)(Math.Floor((double)(item.StackCount / 3)));
                        Slog("{0} can be combined {1} times, so we're combining it.", item.Name, timesToUse);
                        for (uint timesUsed = 0; timesUsed < timesToUse; timesUsed++)
                        {
                            if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                            {
                                EndItemScan(scan);
                                return;
                            }
                            Lua.DoString("UseItemByName(\"" + item.Name + "\")");
                            Thread.Sleep(SpellManager.GlobalCooldownLeft);
                        }
                    }

                    if (Combine5List.Contains(item.Name) && item.StackCount >= 5)
                    {
                        uint timesToUse = (uint)(Math.Floor((double)(item.StackCount / 5)));
                        Slog("{0} can be combined {1} times, so we're combining it.", item.Name, timesToUse);
                        for (uint timesUsed = 0; timesUsed < timesToUse; timesUsed++)
                        {
                            if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                            {
                                EndItemScan(scan);
                                return;
                            }
                            Lua.DoString("UseItemByName(\"" + item.Name + "\")");
                            Thread.Sleep(SpellManager.GlobalCooldownLeft);
                        }
                    }

                    if (Combine10List.Contains(item.Name) && item.StackCount >= 10)
                    {
                        uint timesToUse = (uint)(Math.Floor((double)(item.StackCount / 10)));
                        Slog("{0} can be combined {1} times, so we're combining it.", item.Name, timesToUse);
                        for (uint timesUsed = 0; timesUsed < timesToUse; timesUsed++)
                        {
                            if (!OwnsItemScan(scan, item, itemGuid, itemEntry))
                            {
                                EndItemScan(scan);
                                return;
                            }
                            Lua.DoString("UseItemByName(\"" + item.Name + "\")");
                            Thread.Sleep(SpellManager.GlobalCooldownLeft);
                        }
                    }

                    if (MrItemRemover2Settings.Instance.EnableRemove == "True" &&
                        MrItemRemover2Settings.Instance.RemoveFood == "True")
                    {
                        if (!KeepList.Contains(item.Name) && FoodList.Contains(item.Name))
                        {
                            Slog("{0} was in the Food List and We want to Remove Food. Removing.", item.Name);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }

                    if (MrItemRemover2Settings.Instance.EnableRemove == "True" &&
                        MrItemRemover2Settings.Instance.RemoveDrinks == "True")
                    {
                        if (!KeepList.Contains(item.Name) && DrinkList.Contains(item.Name))
                        {
                            Slog("{0} was in the Drink List and We want to Remove Drinks. Removing.", item.Name);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }

                    //if item name Matches whats in the text file / the internal list (after load)
                    if (ItemName.Contains(item.Name) && !KeepList.Contains(item.Name))
                    {
                        //probally not needed, but still user could be messing with thier inventory.
                        //Printing to the log, and Deleting the Item.
                        Slog("{0} Found Removing Item", item.Name);
                        TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                        return;
                        //a small Sleep, might not be needed. 
                    }

                    if (MrItemRemover2Settings.Instance.DeleteQuestItems == "True" && item.ItemInfo.BeginQuestId != 0 &&
                        !KeepList.Contains(item.Name))
                    {
                        Slog("{0}'s Began a Quest. Removing", item.Name);
                        TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                        return;
                    }

                    

                    //Process all Gray Items if enabled. 
                    if (MrItemRemover2Settings.Instance.DeleteAllGray == "True" && item.Quality == WoWItemQuality.Poor && !BagList.Contains(item.Name))
                    {
                        //Gold Format, goes in GXX SXX CXX 
                        string goldString = MrItemRemover2Settings.Instance.GoldGrays.ToString(CultureInfo.InvariantCulture);
                        int goldValue = goldString.ToInt32() * 10000;
                        string silverString =
                            MrItemRemover2Settings.Instance.SilverGrays.ToString(CultureInfo.InvariantCulture);
                        int silverValue = silverString.ToInt32() * 100;
                        string copperString =
                            MrItemRemover2Settings.Instance.CopperGrays.ToString(CultureInfo.InvariantCulture);
                        int copperValue = copperString.ToInt32();

                        //slog("Value of input sell string - " + (goldValue + silverValue + copperValue));

                        if (item.BagSlot != -1 && !isQuestItem &&
                            item.ItemInfo.SellPrice <= (goldValue + silverValue + copperValue) &&
                            !KeepList.Contains(item.Name) && !BagList.Contains(item.Name))
                        {
                            Slog("{0}'s Item Quality was Poor and only worth {1} copper. Removing.", item.Name,
                                item.ItemInfo.SellPrice);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }

                    //Process all White Items if enabled.
                    if (MrItemRemover2Settings.Instance.DeleteAllWhite == "True" && item.Quality == WoWItemQuality.Common && !BagList.Contains(item.Name))
                    {
                        if (item.BagSlot != -1 && !isQuestItem && !KeepList.Contains(item.Name) &&
                            !BagList.Contains(item.Name) && !FoodList.Contains(item.Name) &&
                            !DrinkList.Contains(item.Name))
                        {
                            Slog("{0}'s Item Quality was Common. Removing.", item.Name);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }

                    //Process all Green Items if enabled.
                    if (MrItemRemover2Settings.Instance.DeleteAllGreen == "True" && item.Quality == WoWItemQuality.Uncommon && !BagList.Contains(item.Name))
                    {
                        if (item.BagSlot != -1 && !isQuestItem &&
                            !KeepList.Contains(item.Name) && !BagList.Contains(item.Name))
                        {
                            Slog("{0}'s Item Quality was Uncommon. Removing.", item.Name);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }

                    //Process all Blue Items if enabled.
                    if (MrItemRemover2Settings.Instance.DeleteAllBlue == "True" && item.Quality == WoWItemQuality.Rare && !BagList.Contains(item.Name))
                    {
                        if (item.BagSlot != -1 && !isQuestItem &&
                            !KeepList.Contains(item.Name) && !BagList.Contains(item.Name))
                        {
                            Slog("{0}'s Item Quality was Rare. Removing.", item.Name);
                            TryDeleteScannedItem(scan, item, itemGuid, itemEntry);
                            return;
                        }
                    }    

                }
                EndItemScan(scan);
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                error is not System.Threading.ThreadInterruptedException)
            {
                EndItemScan(scan);
                Dlog("Item scan deferred after an observation failure: {0}", error.Message);
            }
            finally
            {
                _isScanningItems = false;
            }
        }

        private bool OwnsItemScan(ItemScan scan, WoWItem item = null, ulong itemGuid = 0, uint itemEntry = 0)
        {
            try
            {
                LocalPlayer player = scan != null ? scan.Player : null;
                if (scan == null || !ReferenceEquals(_itemScan, scan) || !IsInitialized ||
                    scan.Lifetime == null || !ReferenceEquals(scan.Lifetime, _deleteLifetime) ||
                    player == null || !ReferenceEquals(Me, player) || !player.IsValid ||
                    scan.PlayerGuid == 0 || player.Guid != scan.PlayerGuid || !player.IsAlive ||
                    player.IsGhost || player.Combat || player.IsCasting || !StyxWoW.IsInGame ||
                    !Styx.Logic.BehaviorTree.TreeRoot.IsRunning || Styx.Logic.BehaviorTree.TreeRoot.IsPaused)
                    return false;

                if (item != null && (!item.IsValid || itemGuid == 0 || item.Guid != itemGuid ||
                    item.Entry != itemEntry || player.BagItems == null ||
                    !player.BagItems.Any(candidate => ReferenceEquals(candidate, item))))
                    return false;

                return ReferenceEquals(_itemScan, scan) && ReferenceEquals(Me, player) &&
                    ReferenceEquals(scan.Lifetime, _deleteLifetime) && player.IsValid &&
                    player.Guid == scan.PlayerGuid && player.IsAlive && !player.IsGhost &&
                    !player.Combat && !player.IsCasting && IsInitialized && StyxWoW.IsInGame &&
                    Styx.Logic.BehaviorTree.TreeRoot.IsRunning && !Styx.Logic.BehaviorTree.TreeRoot.IsPaused;
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                error is not System.Threading.ThreadInterruptedException)
            {
                return false;
            }
        }

        private void EndItemScan(ItemScan scan)
        {
            if (ReferenceEquals(_itemScan, scan))
            {
                _itemScan = null;
                EnableCheck = false;
            }
        }

        private void TryDeleteScannedItem(ItemScan scan, WoWItem item, ulong itemGuid, uint itemEntry)
        {
            // Diagnostics and quest-item observations may have ended this run.
            // BeginDelete still owns request/confirmation context and eligibility.
            if (OwnsItemScan(scan, item, itemGuid, itemEntry))
                BeginDelete(item);
        }

        public string GetTime(DateTime input)
        {
            int hour = input.Hour;
            int min = input.Minute;
            int sec = input.Second;

            string timeInString = (hour < 10)
                ? "0" + hour.ToString(CultureInfo.InvariantCulture)
                : hour.ToString(CultureInfo.InvariantCulture);
            timeInString += ":" +
                            ((min < 10)
                                ? "0" + min.ToString(CultureInfo.InvariantCulture)
                                : min.ToString(CultureInfo.InvariantCulture));
            
            return
                timeInString + (":" +
                                ((sec < 10)
                                    ? "0" + sec.ToString(CultureInfo.InvariantCulture)
                                    : sec.ToString(CultureInfo.InvariantCulture)));
        }

        private bool IsQuestItem(WoWItem item)
        {
            if (item == null || !item.IsValid)
                return true;

            bool isQuestItem;
            int questId;
            bool isActive;
            if (!item.TryGetContainerItemQuestInfo(
                    out isQuestItem, out questId, out isActive))
            {
                Dlog("Quest-item protection could not validate the current container slot for {0}; preserving item.",
                    item.Name);
                return true;
            }

            return isQuestItem || questId > 0;
        }
    }
}