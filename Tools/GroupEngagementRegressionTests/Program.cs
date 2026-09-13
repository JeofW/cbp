using Singular.Helpers;
using Styx;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Policy=Singular.Helpers.DungeonEngagementPolicy;

var cases = new List<(string Name,Action Run)>();
void Test(string n,Action r)=>cases.Add((n,r));
void Check(bool good,string why){if(!good)throw new InvalidOperationException(why);}
WoWUnit Enemy(bool combat=false) {var u=new WoWUnit {Guid=100,Combat=combat,Location=new WoWPoint(1,0,0)};ObjectManager.Objects.Add(u);return u;}
WoWPlayer Member(bool tank=false) {var p=new WoWPlayer {Guid=(ulong)(StyxWoW.Me.PartyMembers.Count+10),IsFriendly=true,Combat=true};StyxWoW.Me.PartyMembers.Add(p);if(tank)Group.Tanks.Add(p);return p;}
Test("assist-only selection is not evidence",()=>Check(!Policy.IsEngaged(true,false,false,false,false,false,false,false,true),"assist selection authorized a pull"));
Test("a fighting leader selecting a fresh pack cannot authorize it",()=>{var e=Enemy();var p=Member();p.CurrentTarget=e;RaFHelper.Leader=p;Check(!e.IsEligibleDungeonCombatTarget(),"leader's combat was borrowed by a fresh enemy");});
Test("a fighting tank selecting a fresh pack cannot authorize it",()=>{var e=Enemy();Member(true).CurrentTarget=e;Check(!e.IsEligibleDungeonCombatTarget(),"tank selection borrowed combat evidence");});
Test("unrelated enemy combat plus leader selection is insufficient",()=>{var e=Enemy(true);var p=Member();p.CurrentTarget=e;RaFHelper.Leader=p;Check(!e.IsEligibleDungeonCombatTarget(),"unrelated combat plus selection authorized a pull");});
Test("actual positive party threat permits target without leader selection",()=>{var e=Enemy(true);e.Threats[Member().Guid]=1;Check(e.IsEligibleDungeonCombatTarget(),"actual group threat must authorize assistance");});
Test("positive raid threat works without a party flag",()=>{var e=Enemy(true);StyxWoW.Me.IsInParty=false;StyxWoW.Me.IsInRaid=true;var p=new WoWPlayer{Guid=40,IsFriendly=true};StyxWoW.Me.RaidMembers.Add(p);e.Threats[p.Guid]=1;Check(e.IsEligibleDungeonCombatTarget(),"raid engagement missing");});
Test("stale tag on a reset enemy cannot authorize a new pull",()=>{var e=Enemy();e.TaggedByMe=true;Check(!e.IsEligibleDungeonCombatTarget(),"reset enemy retained permission");});
Test("party targeting while out of combat is not engagement",()=>{var e=Enemy();e.IsTargetingMyPartyMember=true;Check(!e.IsEligibleDungeonCombatTarget(),"mere target selection is not a fight");});
Test("stale threat on reset enemy cannot authorize a pull",()=>{var e=Enemy();e.Threats[Member().Guid]=8;Check(!e.IsEligibleDungeonCombatTarget(),"combat reset must revoke old evidence");});
Test("enemy fighting our party is permitted",()=>{var e=Enemy(true);e.IsTargetingMyPartyMember=true;Check(e.IsEligibleDungeonCombatTarget(),"party-defense control failed");});
Test("enemy fighting our raid is permitted",()=>{var e=Enemy(true);e.IsTargetingMyRaidMember=true;Check(e.IsEligibleDungeonCombatTarget(),"raid-defense control failed");});
Test("self-defense is permitted",()=>{var e=Enemy(true);e.Aggro=true;Check(e.IsEligibleDungeonCombatTarget(),"self-defense control failed");});
Test("pet defense is permitted",()=>{var e=Enemy(true);e.PetAggro=true;Check(e.IsEligibleDungeonCombatTarget(),"pet-defense control failed");});
Test("no group evidence remains denied",()=>Check(!Enemy(true).IsEligibleDungeonCombatTarget(),"unrelated combat must remain denied"));
Test("zero threat remains unknown",()=>{var e=Enemy(true);e.Threats[Member().Guid]=0;Check(!e.IsEligibleDungeonCombatTarget(),"zero threat must not be positive evidence");});
Test("dead enemy is rejected even with stale party target",()=>{var e=Enemy(true);e.IsAlive=false;e.IsTargetingMyPartyMember=true;Check(!e.IsEligibleDungeonCombatTarget(),"dead hostile authorized");});
Test("invalid enemy is rejected even with stale party target",()=>{var e=Enemy(true);e.IsValid=false;e.IsTargetingMyPartyMember=true;Check(!e.IsEligibleDungeonCombatTarget(),"invalid hostile authorized");});
Test("solo Wholesome quest pulls are unchanged",()=>{BotManager.Current.Name="Wholesome AutoQuest";Check(Enemy().IsEligibleDungeonCombatTarget(),"restriction leaked to quest pulls");});
Test("outdoor Combat Bot is unchanged",()=>{StyxWoW.Me.CurrentMap.IsDungeon=false;Check(Enemy().IsEligibleDungeonCombatTarget(),"restriction leaked outside instances");});
Test("AoE rejects fresh nearby pack even when tank selects it",()=>{var fresh=Enemy();Member(true).CurrentTarget=fresh;var active=new WoWUnit{Guid=101,Combat=true,Aggro=true};ObjectManager.Objects.Add(active);Check(!Unit.IsAreaEffectSafe("Divine Storm",active),"AoE borrowed target-selection permission");});
Test("AoE permits only already-engaged nearby enemies",()=>{var e=Enemy(true);e.Aggro=true;Check(Unit.IsAreaEffectSafe("Divine Storm",e),"engaged-only area control failed");});
Test("AoE does not veto unrelated enemies outside effect range",()=>{var fresh=Enemy();fresh.Location=new WoWPoint(100,100,0);Check(Unit.IsAreaEffectSafe("Consecration",StyxWoW.Me),"far enemy should not veto local effect");});
Test("eligibility is revoked when enemy resets between decisions",()=>{var e=Enemy(true);e.IsTargetingMyPartyMember=true;Check(e.IsEligibleDungeonCombatTarget(),"setup");e.Combat=false;Check(!e.IsEligibleDungeonCombatTarget(),"permission survived reset");});
Test("departed member's historical threat is not group evidence",()=>{var e=Enemy(true);var p=Member();e.Threats[p.Guid]=1;StyxWoW.Me.PartyMembers.Clear();Check(!e.IsEligibleDungeonCombatTarget(),"left roster retained permission");});
Test("null target is denied",()=>Check(!Unit.IsEligibleDungeonCombatTarget(null!),"null allowed"));
var failed=new List<string>();
foreach(var t in cases){StyxWoW.Me=new LocalPlayer{Guid=1,IsFriendly=true};BotManager.Current=new();RaFHelper.Leader=null;Group.Tanks.Clear();ObjectManager.Objects.Clear();try{t.Run();Console.WriteLine("PASS engagement: "+t.Name);}catch(Exception e){failed.Add(t.Name+": "+e.Message);Console.Error.WriteLine("FAIL engagement: "+failed[^1]);}}
Console.WriteLine($"Group engagement: {cases.Count-failed.Count}/{cases.Count}; actual linked Unit/policy; controlled observations; no client attached.");
if(failed.Count!=0)Environment.ExitCode=1;
