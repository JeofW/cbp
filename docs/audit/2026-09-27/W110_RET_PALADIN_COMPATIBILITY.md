# W110 Ret Paladin and group-support evidence matrix

27 September 2026. Original WoW **3.3.5a, build 12340** only. This is a new source/core audit, not a Classic/Retail rotation guide or a claim of optimal DPS. The entry Ret/support/settings files are unchanged from W109. W110 separately repairs shared spell-dispatch and ground-completion receipt propagation; their exact final commits and hosted gates belong to W110_CHECKPOINT.md and W110_EVIDENCE.json.

## Evidence and decision standard

The actual three Ret factories, helpers, support owner, settings and linked regression fixtures were read, rather than inferring behavior from comments or old reports. Primary core: TrinityCore `8fda442f6c30ca21a622638063ab8b28376f1b25`. Secondary: AzerothCore `8337a378ac325e62a6a91e00c6a5e944205e8536`. Eight immutable source files, their exact URLs/SHA256 values, 113 parsed AC groups, 69 rules and 25 expanded category records are retained in `W110_CORE_EVIDENCE_20260927.json`. Reference commits do not identify the user's realm, database customizations or active character talents.

Evidence levels used below are deliberately different: **source-established** describes executed branches in the inspected source; **controlled verification** means the real owner/TreeSharp ran against explicitly controlled leaves in hosted Windows/x86; **core-established** describes the pinned server implementation or data; **external acceptance required** means current native semantics, actual effect magnitude, realm state, encounter behavior or independent review is still missing. A controlled native return is not a new IDA observation, and a server source file does not prove that the client API exposes its internal state.

No new IDA call was made. The earlier provider-refused health transaction has no current result and was not replayed. This does not establish global IDA unavailability. A separate provider-refused TC recursive tree-listing operation is preserved exactly in W110 evidence; the retained sources were obtained through independently justified known-path reads, not a recovered tree listing.

## 1. FCFS and context behavior

`ClassSpecific/Paladin/Retribution.cs:95-260` constructs three distinct priority selectors. In each, target/LOS/facing, autoattack and shared interruption precede offensive selection. Defensive actions, seal maintenance and mana recovery also precede the damage list. The table gives **current source order**, not a universal optimization prescription.

| Context | Current damage priority after preceding support/cooldown actions | Established controls and remaining limit |
|---|---|---|
| Normal solo/open-world | Art of War Exorcism on undead/demons; Hammer of Wrath at <=20%; Crusader Strike; Divine Storm; selected Judgement; Art of War Exorcism on other targets; non-proc Exorcism; Holy Wrath; Consecration; movement last | Full linked tree controls retain known/ready/melee/action independence and proc windows. Actual swing loss, latency, encounter immunity and DPS are not measured. |
| Instances | Undead/demon proc Exorcism; Hammer of Wrath; other proc Exorcism; Crusader Strike; Divine Storm; Judgement; non-proc Exorcism; Holy Wrath; Consecration; movement last | The ordinary full factory remains registered for Pull and Combat. The optional isolation opener cannot replace it. This is not a raid-gearing/talent/glyph simulator. |
| Battlegrounds | Hammer of Wrath; proc Exorcism; Crusader Strike with the existing throttle; Divine Storm; Judgement; non-proc Exorcism; Holy Wrath; Consecration; movement last | Player-target seal and flag-preserving shield decisions have controlled coverage. No arena strategy, diminishing-return model, opponent-spec prediction or PvP performance certification is implied. |

The Normal factory explicitly calls `WaitForCast(false,false)` before movement. The other two do not have that same wrapper at that position, but shared melee/target approach also checks `!IsCasting`, LOS setup rejects casting/channeling, and spell readiness checks the native/API boundary. Merely spotting the absent wrapper is therefore insufficient to prove that those factories interrupt casts. The movement/facing fixtures and source are retained; current original-client proc/cast/channel observations are still needed for complete acceptance.

The damage tree does not add Righteous Fury, Hand of Reckoning or a Protection-only threat rotation to Ret. Divine Shield and Divine Protection are defensive choices, not evidence of a tank specialization switch. Learned-spell/readiness checks remain necessary; class names, spell constants and old compatibility comments do not establish learnability on an original client.

The FCFS table has an important resource override: `CreateManaRecoveryBehavior` at `Retribution.cs:381-388` runs before the damage list and tries the selected Judgement when observed mana is <=15%. Divine Plea then requires mana below the configured threshold and health above 70%. Justice also applies to an observed fleeing NPC when learned, not only a moving/flag-carrying player. These are actual current branches, not a fixed rotation that ignores mana or target state.

### Action legality, mana and procs

`Retribution.cs:313-478`, shared `Helpers/Spell.cs`, and `Styx/Logic/Combat/SpellManager.cs` were inspected together.

| Action | Source policy actually present | What must not be inferred |
|---|---|---|
| Crusader Strike / Divine Storm | Separate candidates; unavailable/unknown Divine Storm cannot remove a ready Crusader Strike. Single-target Divine Storm remains permitted; area-safety/range still gate it. | A broad target count is not proof that a cleave is safe or optimal. |
| Hammer of Wrath | Target health <=20 in all three factories. | Avenging Wrath is not treated as permission to use the later-expansion above-20% rule. |
| Exorcism | Disjoint proc and non-proc windows with existing retry throttling. An observed Art of War proc is distinct from merely knowing the talent. Non-proc filler requires the existing stationary/non-active-melee policy; learned talent without a proc does not authorize a hard cast. | No proof that every rank/proc makes the original native cast-time reader report an instant, or that the current observer distinguishes every talent rank/modifier. The fixture's controlled proc is not that proof. |
| Judgements | Justice for the observed fleeing/moving player/flag situation; otherwise existing health and group complementary Light/Wisdom policy. Same-name auras require actual caster/expiry evidence for the complementary choice. | A group member's spec, damage contribution or assignment is not guessed. No generalized Judgement overwrite-strength optimizer was added. |
| Holy Wrath | Restricted to observed eligible nearby undead/demon targets by the source helper. | Not a generic all-target stun, immunity model or PvP interrupt solution. |
| Consecration | Valid alive target, source area-safety policy, stationary/melee conditions, mana threshold and boss/nearby-target requirement. | A ready ground spell still needs request/cursor/terrain acceptance; W110 receipt fixes do not supply exclusive origin. |
| Divine Plea | Existing configured mana threshold and health safeguard; not an unlimited healing-efficiency optimizer. | No new talent, glyph, healing-penalty or encounter override was invented. |

Shared readiness reads original-client `GetSpellInfo`, `GetSpellCooldown` and `IsUsableSpell` through a validated response marker, alongside known-spell and world conditions. DBC/base spell data, server script rules and this API response are separate evidence. W110's first repair stops a missing/failed local executor from being reported as a successful cast through the actual manager; it does not reinterpret an unverified native return register or prove server acceptance.

The retained linked Paladin decision suite already executes the full factory, real TreeSharp and controlled world/dispatch; its melee availability matrix and Exorcism matrix are not native/game execution. The registration fixture explicitly proves CLR attributes only, not rotation ticks. W110 final integrated artifacts report which of these groups actually ran.

## 2. All original seal choices and precedence

The inspected enum exposes Auto and **Command, Corruption, Justice, Light, Righteousness, Vengeance, Wisdom**. `Retribution.cs:313-350` gives a learned explicit selection precedence over Auto and captures/revalidates actor, target and Ret specialization around the tactic. An explicit unavailable choice does not authorize a fabricated spell or arbitrary alternative cast.

| Situation | Actual Auto/manual behavior | Boundary / exact next input |
|---|---|---|
| Explicit learned seal, including Wisdom/Light/Justice/Command | The user's manual setting takes precedence, including player combat. Existing matching self seal is retained instead of repeatedly recast. | Verify learned rank and real client aura/application; do not assume an unavailable opposite-faction spell exists. |
| Ordinary NPC single target | Prefer known Corruption, otherwise known Vengeance; use learned fallback policy if no stacking seal is available. | This is a source heuristic, not measured short-fight versus boss break-even. A relevant character/talent/gear/target-duration profile is needed to optimize ramp. |
| Player target | Auto prefers learned Righteousness rather than unproven cleave or stacking-ramp policy. Explicit Command remains an explicit choice. | No claim that this is optimal for every PvP composition, immunity or encounter. |
| Safe observed multi-target NPC situation | With a stacking seal available, learned Command requires the existing area-safety, non-boss and target-count policy; existing Command has its own stability threshold. When no stacking seal is learned, the source also permits learned Command as its safe fallback without that target-count condition. | Actual nearby neutral/CC/LOS/chain-hit behavior must be accepted on the original client/core. Count alone is not safety. |
| Caster or target changes during setup | Captured actor/target GUID or specialization changes revoke the tactic's dispatch in the linked controls. | Unobserved native ABA continuity and cross-frame memory freshness are not established by managed reference checks. |
| Vengeance/Corruption stacks from another paladin | The pinned TC seal script uses the caster's own DoT/stack contribution; another paladin's stacks are not automatically ours. | Do not add a stack-based optimizer until own-caster stack observation and the actual realm spell chain are proven. |

The pinned source's constants for historical/NPC spells do not prove an original player's available seal. No Blood/Martyr or later-expansion ability was enabled merely because a constant or profile mentions it. Automatic resource-seal cycling, a full time-to-die predictor and encounter-specific seal assignments are **not implemented or certified** by this review.

Per-caster seal mutual exclusion is core-established, and current known-spell/API readiness is the source's resource/GCD gate. Exact learned-rank cost/GCD under talents, faction spellbook availability, stack expiry/persistence across seal or target changes, and every seal/Judgement proc interaction are not newly measured here. The next input is the original client's identified spellbook/DBC/API and caster-bound aura observations, corroborated with the actual realm's rank chains and scripts; a spell ID, opposite-faction constant or retained base cast time alone is insufficient. No hard-coded resource, expiry or faction rule was added on that incomplete basis.

## 3. Blessings, Greater variants and multiple paladins

`PaladinSupport.cs:176-347` considers current friendly alive recipients, source range/LOS, self and current group membership. It distinguishes same-family normal/Greater auras and actual caster identities. Automatic selection prefers useful stable contributions rather than every paladin choosing and abandoning the same buff at once. It has explicit class-capability defaults, observed Druid forms and our own known Paladin specialization; it does not inspect or establish every teammate's specialization or assignment.

**All four normal/Greater families are retained:** Kings, Might, Wisdom and Sanctuary. `MatchesBlessing` recognizes a normal name and its Greater variant. Auras from unknown casters or an existing external same-family contribution are not treated as an invitation to overwrite. A base spell rank, level field or spell ID is not an observed talent-adjusted amount.

Greater blessings remain opt-in, out of combat, learned and castable, with positive observed reagent availability and compatible same-class group recipients. Actual same-class range/LOS and requested-family compatibility matter; one suitable recipient does not authorize a class-wide replacement that conflicts with another. The script's reagent observation, class coverage and dispatch are not a supervised reagent-consumption/server-application acceptance result.

For Might versus Battle Shout, the existing policy is deliberately conservative: observed active Shout can defer Might, including manual-Might paths and uncertain caster strength, while Auto may choose another useful contribution. It does not assume our nominal rank is stronger than an external talented shout. The retained contribution fixture covers expiration, repeated pulses, manual selection, unknown caster, stable own buffs, role distinctions and percentage-AP non-equivalence.

The full arbitrary external effective-amount observer is **not present**. `WoWAura` and its collection expose names/IDs, caster, flags, level/stacks and timing, not a universal current effect magnitude. Reading base DBC values would not close talent, rank, proc or realm modifications. The correct next input for strength-aware replacement is an original-client observable contract or independently bound server/aura data plus rank/talent-aware hosted tests and supervised two-caster acceptance—not guessed numbers.

### All ten playable class recipients

This table records the actual fallback orders in `SelectNormalBlessing`, not a claim that class alone proves specialization. Manual selection or a verified PallyPower assignment supersedes the fallback. Stable useful own coverage, existing external/unknown coverage, learned/castable state and active Battle Shout can prevent a candidate. Wisdom is filtered using observed `MaxMana <= 0`, not a fabricated teammate talent or role.

| Recipient class | Current Auto fallback before coverage/readiness filters | Role/capability limit |
|---|---|---|
| Warrior | Kings, then Might, then Wisdom if the observed mana gate permits it | No tank/damage assignment inferred; normal original non-mana observation excludes Wisdom. |
| Rogue | Kings, then Might, then Wisdom if the observed mana gate permits it | No talent/spec inference; the observed resource gate excludes an unusable mana contribution. |
| Death Knight | Kings, then Might, then Wisdom if the observed mana gate permits it | Not assumed to be a tank; existing Horn or percentage-AP coverage is not substituted for all of Kings/Might. |
| Hunter | Kings, then Might, then Wisdom | Current mana is observed; no pet/spec or mathematically best DPS blessing is guessed. |
| Mage | Kings, then Wisdom | Explicit caster-capability default, not a talent inspection or effective-magnitude result. |
| Priest | Kings, then Wisdom | Does not guess healing versus damage specialization or raid assignment. |
| Warlock | Kings, then Wisdom | Does not infer pet/talent or collapse Fel Intelligence into a whole-blessing replacement. |
| Shaman | Kings, then Might, then Wisdom | The fallback does not claim knowledge of Enhancement, Elemental or Restoration. Verified assignments/observed coverage must provide more specificity. |
| Druid | Observed Moonkin Form or Tree of Life uses Kings, then Wisdom; otherwise Kings, then Might, then Wisdom | Forms are observed rather than inferred; missing form is not proof of feral/tank role. |
| Paladin | Our own known Holy uses Kings, then Wisdom. Our own grouped Ret outside battlegrounds without Battle Shout uses Might, Kings, Wisdom. Other cases use Kings, Might, Wisdom | Another Paladin's class is not proof of Holy/Ret/Protection. Sanctuary remains available through explicit or verified assignment, not an invented tank designation. |

Join/leave/death and unavailable-recipient handling depend on the current friendly/group/alive object and subsequent revalidation; stale or missing data is not permission for mass rebuff. Greater selection checks a bounded whole same-class roster, rejects unobservable/conflicting recipients and keeps normal casting available. Existing controlled tests verify these decisions, not an atomic server roster or online-status snapshot. Actual roster churn, range/expiry and two-Paladin assignment application remain supervised acceptance inputs.

### PallyPower and explicit settings

PallyPower use is opt-in and read-only. The actual query requires metadata version **v3.2.21**, `PallyPower.IsWrath == true`, exact Wrath assignment tables, current player/class indices and bounded valid slots. It does not accept an arbitrary modern addon that happens to have similar global names. A verified empty assignment is distinct from absent addon; uncertain schema/version/response defers instead of inventing an assignment. No addon table is written. Manual Singular choices and Auto/assignment ownership remain distinct.

Original-client research policy and the previously retained addon provenance still govern this compatibility mode. This W110 read of the adapter is not a fresh installed-addon identity verification or permission to relax the strict version gate.

## 4. Cross-class categories: spell effect is not whole-buff equivalence

Pinned TC `SpellInfo.cpp:1406-1455,2183-2199` makes seals/hands/auras/judgements exclusive **per caster**, not globally one paladin contribution across the group. `SpellMgr.cpp:365-505,1269-1498` normalizes rank chains, expands subgroups and resolves SAME_EFFECT using actual effect types and strongest absolute amounts. Shared nested subgroups have precedence. `SpellAuras.cpp:1824-2040` treats EXCLUSIVE/HIGHEST and SAME_CASTER differently from SAME_EFFECT; matching stat effects can be non-additive while both auras remain.

The following is a **reference-category map**, not new runtime suppression code. AC data supplies the exact listed groups; corresponding TC algorithms explain the semantics, but do not prove that the user's TC realm has identical AC rows. The pinned enum is authoritative over a conflicting current wiki flag table.

| Coverage or interaction | Pinned AC group/rule and source interpretation | Current paladin handling / remaining evidence |
|---|---|---|
| Might and Battle Shout: flat attack power | 1004 combines 1002/1003; rule 4 HIGHEST. | Existing conservative Shout-aware contribution policy retained. Effective stronger/weaker talent values remain unknown. |
| Trueshot Aura, Unleashed Rage, Abomination's Might: percentage attack power | 1029, rule 1 EXCLUSIVE. | Not substituted for flat Might. Source/tests preserve that distinction; character/realm magnitudes remain external. |
| Wisdom and Mana Spring: mana regeneration | 1105 includes Wisdom subgroup 1005 and 5677; rule 3 SAME_EFFECT. | No blind copy of Might/Shout suppression: a temporary totem overlap does not by itself prove that maintaining Wisdom removes another whole aura. Amount, reach, expiry and realm behavior are unproven. |
| Kings and Sanctuary's relevant stat sub-effect | 1038 includes Kings family plus 67480; rule 3 SAME_EFFECT. Sanctuary itself has separate effects/owner script. | Do not collapse entire Kings and Sanctuary into equivalent buffs or remove Sanctuary's other benefits. Same-family/caster protection remains. |
| Intellect buffs / intellect scroll family | 1083, rule 4; actual first-rank IDs retained. | Not a Kings replacement. No generalized external intellect amount optimization added. |
| Spirit buffs / spirit scroll family | 1085, rule 4. | Not a Wisdom/MP5 replacement merely because both benefit casters. |
| Fel Intelligence overlap | 1124/1125, rule 3; one expanded group contains spirit-family IDs and the other intellect-family IDs, with 54424 shared. | Whole Fel Intelligence is not identical to either single-stat family. The SQL descriptions do not substitute for actual membership/effect types; their labels must not silently reverse the observed groups. |
| Fortitude / Prayer / stamina scroll family | 1084, rule 4. | Distinct from all-stat percentage Kings and flat-health effects. No arbitrary stronger-buff assertion. |
| Mark / Gift of the Wild family | 1089, rule 4. | A separate all-stat/armor/resistance contribution, not automatically identical to Kings. Actual effect magnitudes and partial overlaps require their own evidence. |
| Commanding Shout / Blood Pact | 1093, rule 4. | Flat health is not percentage stamina/all stats. Warrior shout exclusivity is separately modeled by group 1011, rule 2 SAME_CASTER. |
| Strength of Earth / Horn of Winter / strength-agility scroll effects | 1088, rule 4, exact IDs retained. | Not percentage AP and not all of Kings. No blanket paladin-buff suppression by their names. |
| Stoneskin / armor scroll | 1086, rule 4. | This row alone does not prove every Devotion Aura interaction. The missing direct Devotion ID in these selected rows is not proof of stacking or nonstacking through other effects/scripts. |
| Improved Devotion healing-received component / Tree contribution | 1094, rule 3. | This concerns a component, not complete equivalence of base Devotion and Tree form. TC's aura-script uncertainty comment is retained rather than promoted to certainty. |
| Shadow Protection family | 1098, rule 4. | The selected data does not establish a universal rule for paladin fire/frost/shadow resistance aura versus totem/other effects. Need exact rank/effect/realm evidence; no invented unified Resistance Aura. |
| Bloodlust / Heroism / Power Infusion | 1122, rule 3, with separate PI/Arcane Power group 1123 rule 4. | Not interchangeable whole cooldowns. No raid-cooldown coordinator or caster-target planner was added. |
| Temporary damage multipliers including Avenging Wrath | 1107, rule 4; exact members retained. | Category data alone does not authorize a new best-timing rotation, every possible buff combination or assumed learned racial/profession spell. |

Rows deliberately include Mage, Priest, Druid, Warrior, Warlock/pet, Shaman, Hunter and Death Knight contributions relevant to the saved goal. Rogue utilities and debuffs are not reclassified as direct replacements for a paladin stat blessing. Other encounter-specific effects are not certified by omission from this matrix. The exact member IDs are retained in the JSON, so future realm comparisons need not rely on translated names or this prose summary.

## 5. Auras, travel and multiple paladins

Actual support factories list **Devotion, Retribution, Concentration, Shadow Resistance, Frost Resistance, Fire Resistance and Crusader Aura**. Auto chooses among useful learned role-appropriate auras, retains a useful own contribution, and uses a stable known-caster GUID ordering for duplicate coordination. Unknown caster identity is not fabricated. This avoids uncontrolled simultaneous switching under the observed model; it is not an effective-strength optimizer or a native atomic roster snapshot.

Explicit Aura settings apply **while not mounted**, as stated in the actual settings description. `Flightor.cs:349-354` separately requests learned/castable Crusader Aura for a sufficiently long mounted route. Ordinary support is gated while mounted. Consequently, the presence of travel Crusader Aura is not by itself evidence that the configured unmounted aura is ignored. Real dismount, subsequent support pulse, range and competing paladins still need supervised acceptance.

The legacy enum named Resistance maps to **Shadow Resistance Aura** for compatibility; it is not a later-expansion combined resistance spell. The three original resistance auras remain distinct explicit choices. Their exact overlap with totems, prayer effects, rank/talent bonuses and encounter requirements must be proven independently before automatic strength-aware replacement.

## 6. Racials, professions, hands, defensive lockouts and dispels

The three Ret factories attempt Blood Fury, Berserking and Lifeblood only under the existing learned-spell/readiness and Avenging Wrath conditions. A non-paladin racial entry in a shared source file, or a spell constant from a different patch, does not grant this character the ability. This is retained compatibility logic, not a recommendation to change character race or proof of original-client availability for every named cooldown. No engineering glove, item-use or profession-availability guess was added.

Pinned TC `spell_paladin.cpp:936-985` checks both Forbearance and the Avenging Wrath marker for Divine Shield/Protection/Hand of Protection paths. The self-target Lay on Hands check at `1381-1423` additionally considers the immune-shield marker; that self-only condition must not be blindly applied to every friendly recipient. Avenging Wrath marker handling and Sanctuary sub-effect ownership are retained in the full pinned script. No lockout duration was inferred from a name alone.

The actual rotation checks health, Forbearance and flag protection for shield use, the configured Ret Protection threshold, and existing hand-aura/immobility policy. Shared `IsUsableSpell` readiness participates before native dispatch. Therefore, a missing explicit marker name in a Ret predicate does **not alone prove an illegal cast**: source readiness and actual API behavior must be examined together. Conversely, the server script alone does not prove that the original API reports every lockout perfectly. The exact next input is current original-client usability/cooldown/marker observations under authorized self/friendly scenarios, bound to a known core/realm, then regression-first repair for any demonstrated mismatch.

Hand of Freedom is not a universal break for every control mechanic; the current condition can request it under a broad observed mechanic set, while actual spell legality/effect belongs to the original client/core. PvP and Instance guards avoid replacing the caster's observed existing Hand contribution indiscriminately. No complete hand assignment coordinator, raid mitigation scheduler or Protection role was introduced.

Purify/Cleanse remain learned-mask and recipient guarded. The explicit unsafe-removal list includes Mutating Injection, Necrotic Plague variants, Unstable Affliction and Vampiric Touch. The source itself says this is **not a complete encounter policy** and exposes a setting to disable automatic dispels for assigned timing. This review preserves those controls and does not claim all raids/debuffs are safe.

## 7. Review decisions and acceptance handoff

The concrete shared receipt defects were implemented through hosted red/green, not relabeled as missing live evidence. No new Ret FCFS reorder, guessed buff strength, artificial era spell, talent/rank assumption or all-class certification was introduced. Current group contribution and Greater/assignment assertions remain intact.

To close the remaining gameplay boundary, the next evidence must identify: exact authorized development client and realm/core/database; learned spells, ranks, talent/glyph/gear and addon versions; original API return shapes for proc-adjusted cast time and usability; caster-bound effective aura amounts or a proven equivalent contract; supervised two-paladin and cross-class expiry/replacement observations; and encounter/target-duration assumptions for any optimization. Such evidence must precede new behavioral-red fixtures and minimal repairs. The supervised and independent gates remain distinct from this implementer's source review and hosted controlled tests.
