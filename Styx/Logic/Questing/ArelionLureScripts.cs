using System;
using System.Globalization;

namespace Styx.Logic.Questing;

/// <summary>Original build12340 UI/item requests. Returned tokens are local dispatch only.</summary>
public static class ArelionLureScripts
{
    private static string GuidText(ulong guid) => guid != 0 ? "0X" + guid.ToString("X16", CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(guid));
    private const string Finite = "local function finite(x) return type(x)=='number' and x==x and x~=math.huge and x~=-math.huge end local function yes(x) return x==true or x==1 end ";
    private static string Guard(ulong actor, ulong target, string unit) => Finite +
        "for _,n in ipairs({'UnitGUID','UnitAffectingCombat','GetTime','GetUnitSpeed','IsMounted','IsFlying','IsFalling','IsSwimming','UnitOnTaxi','UnitInVehicle','UnitCastingInfo','UnitChannelInfo'}) do if type(_G[n])~='function' then return 'rejected' end end " +
        "local recipient=UnitGUID('" + unit + "');if string.upper(UnitGUID('player') or '')~='" + GuidText(actor) + "' or string.upper(recipient or '')~='" + GuidText(target) + "' then return 'rejected' end " +
        "if UnitAffectingCombat('player') or IsMounted() or IsFlying() or IsFalling() or IsSwimming() or UnitOnTaxi('player') or UnitInVehicle('player') or UnitCastingInfo('player') or UnitChannelInfo('player') then return 'rejected' end " +
        "local speed=GetUnitSpeed('player'); if not finite(speed) or speed~=0 then return 'rejected' end ";
    private static string Lease(string action, double duration) =>
        "local now=GetTime(); if not finite(now) or now<0 then return 'rejected' end " +
        "local leases=_G.CopilotBuddy_Quest9472; if leases~=nil and (type(leases)~='table' or leases.schema~='quest9472-v1') then return 'rejected' end " +
        "if leases==nil then leases={schema='quest9472-v1'};_G.CopilotBuddy_Quest9472=leases end " +
        "local key='" + action + "'; local old=leases[key]; local actor=UnitGUID('player'); " +
        "if old~=nil then if type(old)~='table' or type(old.actor)~='string' or type(old.recipient)~='string' or not finite(old.started) or not finite(old.untilAt) or math.abs(old.untilAt-old.started-" + duration.ToString(CultureInfo.InvariantCulture) +
        ")>.001 or now<old.started then return 'rejected' end if old.actor==actor and now<old.untilAt then if old.recipient~=recipient then return 'rejected' end return 'pending' end end " +
        "leases[key]={actor=actor,recipient=recipient,started=now,untilAt=now+" + duration.ToString(CultureInfo.InvariantCulture) + "}; ";

    public static string Stock(ulong actor) => Finite +
        "if type(UnitGUID)~='function' or type(GetItemCount)~='function' or string.upper(UnitGUID('player') or '')~='" + GuidText(actor) + "' then return 'unknown' end " +
        "local wine=GetItemCount(29112,false);local scroll=GetItemCount(23693,false); if not finite(wine) or not finite(scroll) or wine<0 or scroll<0 or wine%1~=0 or scroll%1~=0 then return 'unknown' end return 'stock',wine,scroll";

    public static string BuyWine(ulong actor, ulong vendor) => Guard(actor, vendor, "npc") +
        "for _,n in ipairs({'GetMerchantNumItems','GetMerchantItemLink','GetMerchantItemInfo','GetItemCount','GetMoney','BuyMerchantItem'}) do if type(_G[n])~='function' then return 'rejected' end end " +
        "if not MerchantFrame or not MerchantFrame:IsVisible() then return 'rejected' end " +
        "local wine=GetItemCount(29112,false);local count=GetMerchantNumItems();local money=GetMoney(); " +
        "if not finite(wine) or wine~=0 or not finite(count) or count<0 or count>100 or count%1~=0 or not finite(money) or money<0 then return 'rejected' end " +
        "for i=1,count do local link=GetMerchantItemLink(i);local id=link and tonumber(string.match(link,'item:(%d+)'));if id==29112 then " +
        "local _,_,price,bundle,available,usable=GetMerchantItemInfo(i);if not finite(price) or price<0 or not finite(bundle) or bundle~=1 or not finite(available) or (available~=-1 and available<1) or money<price then return 'rejected' end " +
        Lease("wine", 12) + "BuyMerchantItem(i,1);return 'submitted' end end return 'rejected'";

    public static string OpenVendor(ulong actor, ulong vendor) => Guard(actor, vendor, "npc") +
        "if type(GetGossipOptions)~='function' or type(SelectGossipOption)~='function' or not GossipFrame or not GossipFrame:IsVisible() then return 'rejected' end " +
        "local options={GetGossipOptions()};if #options>128 or #options%2~=0 then return 'rejected' end " +
        "for i=2,#options,2 do if options[i]=='vendor' then " + Lease("vendor-dialog", 3) + "SelectGossipOption(i/2);return 'submitted' end end return 'rejected'";

    public static string SelectLure(ulong actor, ulong viera, int index, int expectedCount)
    {
        if (index < 0 || index >= expectedCount || expectedCount > 10) throw new ArgumentOutOfRangeException(nameof(index));
        return Guard(actor, viera, "npc") +
            "if type(GetNumGossipAvailableQuests)~='function' or type(SelectGossipAvailableQuest)~='function' or not GossipFrame or not GossipFrame:IsVisible() or GetNumGossipAvailableQuests()~=" + expectedCount + " then return 'rejected' end " +
            Lease("lure-select", 3) + "SelectGossipAvailableQuest(" + (index + 1) + ");return 'submitted'";
    }

    public static string AdvanceLure(ulong actor, ulong viera, bool reward) => Guard(actor, viera, "npc") +
        "if type(GetItemCount)~='function' or not QuestFrame or not QuestFrame:IsVisible() then return 'rejected' end " +
        "local wine=GetItemCount(29112,false);if not finite(wine) or wine<1 then return 'rejected' end " +
        (reward
            ? "local button=QuestFrameCompleteQuestButton;if type(GetNumQuestChoices)~='function' or GetNumQuestChoices()~=0 or not button or not yes(button:IsVisible()) or not yes(button:IsEnabled()) then return 'rejected' end " +
                Lease("lure-reward", 150) + "button:Click();return 'reward-submitted'"
            : "local button=QuestFrameCompleteButton;if QuestFrameCompleteQuestButton and yes(QuestFrameCompleteQuestButton:IsVisible()) then return 'rejected' end if not button or not yes(button:IsVisible()) or not yes(button:IsEnabled()) then return 'rejected' end " +
                Lease("lure-progress", 3) + "button:Click();return 'progress-submitted'");

    public static string Scroll(ulong actor, ulong viera, int bag, int slot)
    {
        if (bag < 0 || bag > 4 || slot <= 0 || slot > 100) throw new ArgumentOutOfRangeException(nameof(bag));
        return Guard(actor, viera, "target") +
            "for _,n in ipairs({'GetContainerItemLink','GetContainerItemCooldown','UseContainerItem','SpellIsTargeting'}) do if type(_G[n])~='function' then return 'rejected' end end " +
            "if SpellIsTargeting() or (MerchantFrame and MerchantFrame:IsVisible()) or (GossipFrame and GossipFrame:IsVisible()) or (QuestFrame and QuestFrame:IsVisible()) then return 'rejected' end " +
            "local link=GetContainerItemLink(" + bag + "," + slot + ");local id=link and tonumber(string.match(link,'item:(%d+)'));if id~=23693 then return 'rejected' end " +
            "local start,duration,enabled=GetContainerItemCooldown(" + bag + "," + slot + ");local now=GetTime();if not finite(start) or not finite(duration) or not finite(now) or start<0 or duration<0 or enabled~=1 or start+duration>now then return 'rejected' end " +
            Lease("scroll", 15) + "UseContainerItem(" + bag + "," + slot + ");return 'submitted'";
    }
}
