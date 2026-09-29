# PR51 and upstream integration release notes

Release label: **1.6.7.5 PR51**. This is the audited JeofW/cbp fork, not the upstream prebuilt distribution. FileVersion1.6.7.5 and informational label1.6.7.5-pr51.20260929 identify this source line; the exact commit and compiled-file hashes belong in the package manifest.

## Behavior changes

Quest profiles now recognize reviewed case-insensitive attribute/type aliases, reject conflicting or malformed identity, and retain the existing explicit index-only form. Original-client objective completion scans the actual quest log in one Lua request, uses strict completion values and checks actor/quest continuity. Quest disposal still cannot publish successful completion.

Threat observation retains membership when a mob has no current victim and bounds corrupt linked-list cycles. Flying mounts use the original207/208/209/211 aura types rather than later-client MiscValueB categories or speed guesses. Terrain height observations accept valid map0, reject missing actors/nonfinite results/context changes, prepare unloaded tiles, and quest-area generation avoids duplicates and retries unavailable heights. Swimming uses the normal path pipeline; an absent or long ground route cannot authorize direct straight-line movement.

Vendor discovery compares faction templates, rejects unknown templates, preserves the legacy runtime ABI and existing caller vetoes, and allows missing service types without overriding forced profile choices. Dungeon boss state follows the selected encounter and optional-boss setting, with valid bounded path observations. Flight route persistence preserves full comma-containing destination names. Specialty-bag pressure can trigger mail independently of selling; the nonfunctional detection-range slider was removed while serialized compatibility remains.

Owned loot attempts retain captured actor/recipient/frame/combat and cleanup guards, use bounded3-second loot/skin frame waits,10seconds for harvesting, and a2-second skin-readiness wait only following loot. Exact maximum skinnable levels remain eligible. No post-clear sleep or duplicate corpse-skin wait was added. Mining Pick2901 is in both maintained protected-item defaults.

## Existing PR51 runtime changes to include

The release must contain the compiled host **and** the tracked runtime sources. WholesomeAutoQuest-master, Singular wotlk, AutoEquip2, MrItemRemover2, SmartLootRoller and the audited CollectThings/DeleteItems/EquipItem/ForcedDismount/GossipEvent/InteractWith/UseItemOn behaviors must not remain at older installed versions. Ret seal/Judgement/manual/Light policies and support safeguards remain the verified W110 behavior. Copying only CopilotBuddy.exe is insufficient.

## Upgrade and rollback

Stop the bot before deployment; do not start or modify WoW.exe. Inventory the approved production folder and preserve a complete backup plus per-file hashes outside it before replacements. Retain user Settings, profile customizations, saved flight data, blacklists, quest progression/recovery state and verified realm data. Code source files named Settings are code and must be updated; per-character/user settings files are not interchangeable with those sources.

Structured flight connections cannot recover ambiguous old comma-joined names; those edges are safely relearned instead of guessed. Preserve the old cache in the backup. Merge protected defaults with user additions or install a separately named recognized protected-item layer; do not erase custom protected items. Preserve existing force-sell/force-mail lists.

The repository carries native navigation alternatives and mesh files. Do not replace an existing selected1x1/4x4 engine or meshes without matching its production configuration. Dataset files are shipped with exact hashes and their current provenance classification; the release does not certify a different realm dataset. Avoid retaining obsolete duplicate compiled/plugin/source copies that could shadow the new code, but move identified conflicts into the backup rather than deleting unrelated user content.

A clean Windows x86 publish from the verified merge commit supplies the host and dependencies. Runtime compiler checks against that package and a deployment readback must follow. The package is not an obfuscated upstream binary. Release notes, source/docs/merge identities, manifests, acceptance receipts and rollback instructions accompany the package. Production remains unchanged while its folder is outside CoS's approved roots.

## Limits retained

Local17/17, optimized203, hosted17/17 and runtime compilation do not establish supervised live acceptance. Dense pull stays disabled. Scripted escort/object-ground item/BelowHp recipes without implementation and provenance remain excluded. Native/server/world/protocol/independent inputs and synchronized live HoJ evidence remain as recorded in the55-row map.
