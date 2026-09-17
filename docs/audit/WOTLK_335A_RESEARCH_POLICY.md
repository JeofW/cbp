# Original WoW 3.3.5a research and implementation policy

Owner requirement reaffirmed: 17 September 2026.

## Fixed target

This fork targets **original World of Warcraft: Wrath of the Lich King 3.3.5a, build 12340**. Apply this requirement to every class and specialization, combat routine, talent, spell, aura, stat, item, loot roll, quest behavior and native/client API change.

Do not treat a page labelled only "WotLK" as sufficient version evidence. Wrath Classic 3.4.x, Cataclysm, Retail, Season of Discovery, pre-Wrath Classic and private-server custom rules are not interchangeable with this target. A shared name, spell ID or guide URL is not proof of identical mechanics.

## Research gate before a gameplay change

For every material mechanics claim, record the source URL or immutable source commit, author/project, publication/update date when available, explicit game version, exact supported claim and remaining uncertainty. Prefer original-era patch notes/client data and inspected 3.3.5a implementations or historical simulations. Emulator implementation is evidence about that implementation, not automatic proof of the owner's realm configuration.

Classify every source as original-3.3.5a applicable, separately corroborated compatible, comparison-only, or unresolved. A current Classic guide may suggest a test or comparison, but must not authorize a change until its relevant claim is independently verified for the fixed target. Preserve contradictory evidence and label inferences. Do not silently backport later Classic balance changes, glyph behavior, spell stacking, talent redesigns, modern role APIs or modern stat rules.

Where exact evidence is missing, retain a bounded conservative policy or an explicit configurable option. Do not invent coefficients, spell availability, buff magnitudes, item caps or quest mappings.

## Gear, equipping and Need rolls

Keep stat extraction, role/spec eligibility, scoring, item comparison and client roll-code translation separate. Numeric weights in existing configuration are implementation facts, not proof of optimal DPS. Strength, Agility, weapon damage, hit and expertise must not be reduced to an unsupported universal ordering. Any proposed coefficients must specify the supported level, build, gear, talents, attack table, caps and encounter assumptions.

Use the correct character specialization and learned abilities. Compare against the item(s) actually replaced, respecting weapon setup and slot constraints. A positive score alone must not bypass main-spec policy, eligibility, missing item information or the user's roll settings. Preserve the existing enum serialization contract when translating client roll values.

## Buff strength, contribution and exclusivity — every class/spec

Separate: (1) same-effect coverage, (2) effective strength, (3) one-active-at-a-time families, (4) complementary contributions, and (5) an intentional situation-driven transition. Do not globally freeze all aspect, stance, presence, seal, armor or shield changes because another family member exists.

Manual preference is not permission to repeatedly submit an impossible or already-covered buff. Recheck the intended recipient and applicable coverage after yielded setup and immediately before dispatch. Retain per-caster semantics where required. Do not treat a successful submission as server acknowledgement, nor a failed attempt as permission to spin indefinitely.

Compare effective rank/talent/scaling magnitude only when observed or reliably derived. Unknown strength is not evidence that our buff is stronger. A conservative defer policy must be labelled as such, not marketed as a DPS optimizer. Prevent conflicting routine owners from repeatedly replacing one another's singleton choice. Test legitimate transitions and recovery after coverage expires alongside negative no-spam cases.

## Quest and movement evidence

A legacy behavior file or ordinary objective parser does not establish automatic support for a special quest. Escort selection/start/follow/defend/reacquisition/completion require an explicit supported strategy and quest evidence. Player arrival or NPC disappearance alone is not server quest credit. Water-consumable admission is separate from validated breath recovery, obstacle navigation and safe shoreline selection.

## Verification and handoff rules

Re-read live branch heads and immutable source before recreating any interrupted work. For behavioral repairs, obtain intended assertion-level red, preserve the tests across the production repair, verify exact-source Windows results and retain failures as well as passes. Record which code was compiled/executed, which external observations were controlled, and what remains untested.

Code review, emulator-source inspection, offline decisions, host compilation, native dispatch and original-client acceptance are distinct evidence levels. Do not claim all-class coverage, maximum DPS, full escort support or exhaustive completion from a narrower passing group. Every continuation must read this policy together with AUDIT_RESUME.md and the newest checkpoint; update stale status pointers without discarding earlier evidence.
