using Styx.Logic.Pathing;
using Styx.Logic.Inventory;
using Styx.Logic.Profiles;
using Styx.Logic;
using Styx.Database;
using Styx.Logic.Inventory.Frames.Merchant;
using System.Runtime.CompilerServices;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var location = new WoWPoint(-2376.27f, -1995.74f, 96.79f);
var blacklistedInstance = new Vendor(7714, "Innkeeper Byula", Vendor.VendorType.Food, location);
var recreatedInstance = new Vendor(7714, "Innkeeper Byula", Vendor.VendorType.Food, location);
var blacklist = new HashSet<Vendor> { blacklistedInstance };

Assert(
    blacklist.Contains(recreatedInstance),
    "A recreated automatic vendor must match the session-blacklisted vendor.");

static NpcResult CreateNpcResult(int entry, int mapId, float x, float y, float z)
{
    var result = (NpcResult)RuntimeHelpers.GetUninitializedObject(typeof(NpcResult));
    result.Entry = entry;
    result.MapId = mapId;
    result.X = x;
    result.Y = y;
    result.Z = z;
    return result;
}

var cachedNpc = CreateNpcResult(3310, 1, 1676.4f, -4315.7f, 61.5f);
var reloadedNpc = CreateNpcResult(3310, 1, 1676.4f, -4315.7f, 61.5f);
var npcNavigationCache = new Dictionary<NpcResult, bool> { [cachedNpc] = true };

Assert(
    npcNavigationCache.TryGetValue(reloadedNpc, out bool canNavigate) && canNavigate,
    "A database NPC reloaded on the next pulse must hit the navigation cache instead of pathfinding again.");

var factionPreference = typeof(NpcQueries).GetMethod(
    "GetFactionPreference",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
Assert(factionPreference != null,
    "Vendor discovery must expose one shared faction-preference rule.");
int FriendlyPreference() => (int)factionPreference!.Invoke(null, new object[] { Styx.WoWUnitReaction.Friendly })!;
int NeutralPreference() => (int)factionPreference!.Invoke(null, new object[] { Styx.WoWUnitReaction.Neutral })!;
int HostilePreference() => (int)factionPreference!.Invoke(null, new object[] { Styx.WoWUnitReaction.Hostile })!;
Assert(FriendlyPreference() > NeutralPreference(),
    "A same-faction service NPC must rank ahead of a neutral service NPC regardless of distance.");
Assert(NeutralPreference() == HostilePreference(),
    "Faction preference must not accidentally make hostile NPCs eligible; eligibility remains a separate safety check.");

var orderByFaction = typeof(NpcQueries).GetMethod(
    "OrderByFactionPreference",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
Assert(orderByFaction != null,
    "Vendor discovery must apply faction preference before consuming distance-ordered candidates.");
var neutralNearby = CreateNpcResult(1, 1, 1f, 0f, 0f);
var factionFarther = CreateNpcResult(2, 1, 20f, 0f, 0f);
var neutralFarther = CreateNpcResult(3, 1, 30f, 0f, 0f);
var factionOrdered = ((IEnumerable<NpcResult>)orderByFaction!.Invoke(null, new object[]
{
    new[] { neutralNearby, factionFarther, neutralFarther },
    (Func<NpcResult, Styx.WoWUnitReaction>)(npc => npc.Entry == 2
        ? Styx.WoWUnitReaction.Friendly
        : Styx.WoWUnitReaction.Neutral)
})!).Select(npc => npc.Entry).ToArray();
Assert(factionOrdered.SequenceEqual(new[] { 2, 1, 3 }),
    "A reachable faction vendor must precede neutral vendors while distance order stays stable within each faction tier.");

var parsedFood = ConsumableVendorPolicy.ParseTooltip(
    "Restores 552 health over 24 sec. Must remain seated while eating.");
Assert(parsedFood.Kind == ConsumableKind.Food, "A health-restoration tooltip must be classified as food.");
Assert(parsedFood.HealthRestored == 552, "Food restoration must come from the merchant tooltip.");

var parsedDrink = ConsumableVendorPolicy.ParseTooltip(
    "Restores 1,344 mana over 27 sec. Must remain seated while drinking.");
Assert(parsedDrink.Kind == ConsumableKind.Drink, "A mana-restoration tooltip must be classified as drink.");
Assert(parsedDrink.ManaRestored == 1344, "Thousands separators must not corrupt restoration amounts.");
Assert(
    ConsumableVendorPolicy.ParseItemId("|cffffffff|Hitem:1179:0:0:0:0:0:0:0|h[Ice Cold Milk]|h|r") == 1179,
    "Merchant item IDs must be parsed from the Lua item link instead of the unreliable native merchant layout.");
Assert(
    ConsumableVendorPolicy.ParseItemId(string.Empty) == 0,
    "A missing merchant link must not produce a fabricated item ID.");

var foodCandidates = new[]
{
    new ConsumableCandidate(1, 1001, "Small Food", 1, 243, 0, 10),
    new ConsumableCandidate(2, 1002, "Right-Sized Food", 5, 552, 0, 20),
    new ConsumableCandidate(3, 1003, "Oversized Food", 15, 874, 0, 30)
};

var rightSized = ConsumableVendorPolicy.SelectBest(
    foodCandidates, ConsumableKind.Food, playerLevel: 18, capacity: 600);
Assert(rightSized?.ItemId == 1002, "The buyer must choose the closest restoration that does not exceed capacity.");

var smallestOversized = ConsumableVendorPolicy.SelectBest(
    foodCandidates.Skip(1), ConsumableKind.Food, playerLevel: 18, capacity: 500);
Assert(smallestOversized?.ItemId == 1002, "If every option exceeds capacity, the buyer must choose the smallest one.");

var levelFiltered = ConsumableVendorPolicy.SelectBest(
    new[]
    {
        new ConsumableCandidate(1, 2001, "Usable Drink", 5, 0, 436, 10),
        new ConsumableCandidate(2, 2002, "Too High Level", 20, 0, 835, 20)
    },
    ConsumableKind.Drink,
    playerLevel: 18,
    capacity: 900);
Assert(levelFiltered?.ItemId == 2001, "The buyer must reject consumables above the player's level.");

Assert(
    ConsumableVendorPolicy.ShouldProtectFromSale(ConsumableKind.Food) &&
    ConsumableVendorPolicy.ShouldProtectFromSale(ConsumableKind.Drink),
    "Food and drink must remain protected even when SellWhite is enabled.");
Assert(
    !ConsumableVendorPolicy.ShouldProtectFromSale(ConsumableKind.None),
    "Ordinary white items must remain eligible for the profile's normal sell rules.");

var sellScript = MerchantFrame.BuildSellNextItemLua(
    ItemQuality.Poor | ItemQuality.Common,
    new[] { "Protected Name" },
    new uint[] { 12345 });
Assert(sellScript.Contains("for b=0,4 do"),
    "the sale scan must include the backpack and all equipped bags");
Assert(!sellScript.Contains("GetBagName"),
    "the backpack must not be skipped merely because it has no bag name");
Assert(sellScript.Contains("if locked then return 'ok',2 end"),
    "a locked eligible stack must keep the sale operation pending");
Assert(sellScript.Contains("MerchantFrame:IsShown()"),
    "the sale must atomically verify the merchant window before using a bag item");
Assert(sellScript.Contains("return 'ok',3"),
    "an atomically observed merchant closure must have a distinct result");
Assert(sellScript.Contains("if not name or quality==nil then return 'ok',2 end"),
    "uncached item metadata must keep the sale operation pending");
Assert(sellScript.Contains("UseContainerItem(b,s) return 'ok',1"),
    "each sale step must submit one stack and immediately yield to the server");
Assert(sellScript.EndsWith("return 'ok',0"),
    "a clean scan must explicitly report that all eligible stacks are gone");
Assert(!MerchantFrame.TryParseSellStepResult(Array.Empty<string>(), out _),
    "a failed Lua call must not be confused with an empty sale queue");
Assert(MerchantFrame.TryParseSellStepResult(new[] { "ok", "0" }, out int emptyResult) && emptyResult == 0,
    "only a marked empty result may complete a vendor sale");
Assert(typeof(Vendors).GetMethod(nameof(Vendors.SellAllItems))?.ReturnType == typeof(void),
    "the public Vendors.SellAllItems binary contract must remain void");
Assert(typeof(MerchantFrame).GetMethod(nameof(MerchantFrame.SellItemQualities))?.ReturnType == typeof(void),
    "the public MerchantFrame.SellItemQualities binary contract must remain void");

// Reproduce the live loop: die on a Sell trip, rebuild the profile, and
// recreate the same NPC at its new spawn position under a different service.
var dangerousVendor = new Vendor(5848, "Malgin Barleybrew", Vendor.VendorType.Sell, location);
Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi(dangerousVendor, Styx.Logic.POI.PoiType.Sell);
typeof(Styx.Logic.POI.BotPoi).GetMethod("OnPlayerDied",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
var rebuiltManager = new VendorManager(System.Xml.Linq.XElement.Parse(
    "<Vendors><Vendor Name='Malgin Barleybrew' Entry='5848' Type='Repair' X='1' Y='2' Z='3' />" +
    "<Vendor Name='Safe alternative' Entry='955' Type='Repair' X='4' Y='5' Z='6' /></Vendors>"));
Assert(!rebuiltManager.Vendors.SelectMany(group => group).Any(v => v.Entry == 5848),
    "A death during a vendor trip must exclude that NPC across profile rebuilds, positions, and service types.");
Assert(rebuiltManager.Vendors.SelectMany(group => group).Single().Entry == 955,
    "Skipping a deadly NPC must leave the next suitable service NPC available.");
Assert(Styx.Logic.POI.BotPoi.Current.Type == Styx.Logic.POI.PoiType.None,
    "Vendor death handling must still clear the POI for corpse recovery.");

rebuiltManager.ForcedVendors.Add(new Vendor(5848, "Malgin", Vendor.VendorType.Train, location));
Assert(rebuiltManager.GetClosestVendor(Vendor.VendorType.Train) == null,
    "Forced trainers must also honor a rejected NPC entry.");
rebuiltManager.Blacklist.Add(new Vendor(3392, "Khazgorm", Vendor.VendorType.Sell, location));
Assert(rebuiltManager.IsBlacklisted(new Vendor(3392, "New spawn", Vendor.VendorType.Repair, default)),
    "Legacy plugin blacklists must survive NPC movement and a change of requested service.");

foreach (var service in new[] { Styx.Logic.POI.PoiType.Sell, Styx.Logic.POI.PoiType.Repair,
    Styx.Logic.POI.PoiType.Buy, Styx.Logic.POI.PoiType.Train })
{
    Assert(VendorSafetyPolicy.IsInvalidServiceNpc(service, false, true, true, true, true),
        "Hostile NPCs must be rejected even when they advertise service flags.");
    Assert(VendorSafetyPolicy.IsInvalidServiceNpc(service, true, false, true, true, true),
        "Dead service NPCs must be rejected before interaction.");
    Assert(!VendorSafetyPolicy.IsInvalidServiceNpc(service, false, false, true, true, true),
        "Living non-hostile service NPCs, including neutral merchants, remain eligible.");
    Assert(VendorSafetyPolicy.IsInvalidServiceNpc(service, false, false, false, false, false),
        "Non-service NPCs must not become vendor destinations.");
}
Assert(!VendorSafetyPolicy.IsInvalidServiceNpc(Styx.Logic.POI.PoiType.Mail, false, false, false, false, false),
    "Mailbox handling must not require merchant flags.");
Assert(!VendorSafetyPolicy.Reject(0) && !VendorSafetyPolicy.IsRejected(0), "Unknown entries must not be rejected.");
Styx.Logic.POI.BotPoi.Current = new Styx.Logic.POI.BotPoi(Styx.Logic.POI.PoiType.Kill) { Entry = 955 };
typeof(Styx.Logic.POI.BotPoi).GetMethod("OnPlayerDied",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
Assert(!VendorSafetyPolicy.IsRejected(955), "An ordinary combat death must not blacklist a service NPC.");

var mixedTrainers = new VendorManager();
mixedTrainers.AllVendors.Add(new Vendor(5848, "Failed paladin trainer", Vendor.VendorType.Train, location) { TrainClass = Styx.Combat.CombatRoutine.WoWClass.Paladin });
mixedTrainers.AllVendors.Add(new Vendor(956, "Mage trainer", Vendor.VendorType.Train, location) { TrainClass = Styx.Combat.CombatRoutine.WoWClass.Mage });
var candidateSelector = typeof(VendorManager).GetMethod("GetEligibleVendors");
Assert(candidateSelector != null, "Vendor eligibility must be resolved before deciding whether automatic fallback is needed.");
var eligibleTrainers = (IEnumerable<Vendor>)candidateSelector!.Invoke(mixedTrainers,
    new object[] { Vendor.VendorType.Train, Styx.Combat.CombatRoutine.WoWClass.Paladin })!;
Assert(!eligibleTrainers.Any(), "Other-class trainers must not conceal exhaustion of the player's trainers.");
var fallbackPolicy = typeof(VendorManager).GetMethod("CanUseAutomaticFallback",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
Assert((bool)fallbackPolicy.Invoke(mixedTrainers,
    new object[] { Vendor.VendorType.Train, Styx.Combat.CombatRoutine.WoWClass.Paladin })!,
    "Exhausting the player's trainer must permit automatic fallback despite other-class profile trainers.");

var recoveryType = typeof(GrindSafetyPolicy).Assembly.GetType("Styx.Logic.CorpseRecoveryState");
Assert(recoveryType != null, "Corpse recovery must track confirmed resurrection followed by a nearby death.");
dynamic recovery = Activator.CreateInstance(recoveryType!)!;
var recoveryTime = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
recovery.Observe(false, 1u, location, recoveryTime);
recovery.Observe(false, 1u, location, recoveryTime.AddSeconds(10));
Assert(!recovery.RepeatedDeath, "Repeated dead/ghost pulses must not count as repeated deaths.");
recovery.Observe(true, 1u, location, recoveryTime.AddSeconds(20));
recovery.Observe(false, 1u, location, recoveryTime.AddSeconds(35));
Assert(recovery.RepeatedDeath, "Dying 15 seconds after resurrection in the same area must request healer recovery.");
dynamic distantDeath = Activator.CreateInstance(recoveryType!)!;
distantDeath.Observe(false, 1u, location, recoveryTime);
distantDeath.Observe(true, 1u, location, recoveryTime.AddSeconds(20));
distantDeath.Observe(false, 1u, new WoWPoint(location.X + 200f, location.Y, location.Z), recoveryTime.AddSeconds(35));
Assert(!distantDeath.RepeatedDeath, "A new death far from the resurrection must not be mistaken for a corpse camp.");
var oldDeath = new CorpseRecoveryState();
oldDeath.Observe(false, 1, location, recoveryTime);
oldDeath.Observe(true, 1, location, recoveryTime.AddSeconds(10));
oldDeath.Observe(false, 1, location, recoveryTime.AddMinutes(4));
Assert(!oldDeath.RepeatedDeath, "A later unrelated death must not retain corpse-camp escalation.");
var changedMap = new CorpseRecoveryState();
changedMap.Observe(false, 1, location, recoveryTime);
changedMap.Observe(true, 1, location, recoveryTime.AddSeconds(10));
changedMap.Observe(false, 530, location, recoveryTime.AddSeconds(20));
Assert(!changedMap.RepeatedDeath, "A map change must not reuse resurrection-area history.");
Assert(CorpseRecoveryState.ShouldUseHealer(true, false, false, true, false) &&
       CorpseRecoveryState.ShouldUseHealer(true, false, false, false, true),
    "Either a corpse-camp death or no safe point must choose the existing healer recovery.");
Assert(!CorpseRecoveryState.ShouldUseHealer(false, false, false, true, true) &&
       !CorpseRecoveryState.ShouldUseHealer(true, true, false, true, true) &&
       !CorpseRecoveryState.ShouldUseHealer(true, false, true, true, true) &&
       !CorpseRecoveryState.ShouldUseHealer(true, false, false, false, false),
    "Healer recovery must respect the setting, instance/BG recovery, and ordinary safe corpse retrieval.");
Assert(CorpseRecoveryState.CanRetrieve(true, true, false, true) &&
       !CorpseRecoveryState.CanRetrieve(true, false, false, true) &&
       !CorpseRecoveryState.CanRetrieve(true, true, true, true) &&
       !CorpseRecoveryState.CanRetrieve(true, true, false, false) &&
       !CorpseRecoveryState.CanRetrieve(false, true, false, true),
    "Only a safe ghost with no healer request and expired server delay may retrieve the corpse.");
Assert(!GrindSafetyPolicy.IsHostileSafeForResurrection(30, 35) &&
       GrindSafetyPolicy.IsHostileSafeForResurrection(40, 35),
    "High-level hostile aggro range must enlarge the resurrection safety margin.");
Assert(!GrindSafetyPolicy.ShouldRetrieveCorpse(true, false),
    "A wait timeout must never override unsafe resurrection surroundings.");
recovery.Reset();
recovery.Observe(false, 1u, location, recoveryTime.AddSeconds(36));
Assert(!recovery.RepeatedDeath, "Stopping and restarting must reset stale recovery history.");
Bots.Grind.LevelBot.CreateDeathBehavior();
Bots.Grind.LevelBot.ShouldUseSpiritHealer = true;
typeof(Bots.Grind.LevelBot).GetMethod("ResetCorpseRecovery",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { EventArgs.Empty });
Assert(!Bots.Grind.LevelBot.ShouldUseSpiritHealer,
    "The registered stop handler must clear a pending healer request before the next run.");

Console.WriteLine("Vendor core regression tests passed.");
