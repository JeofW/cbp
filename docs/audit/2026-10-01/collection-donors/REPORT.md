# Apply ordinary collection donors after source-state review

The prior collection-source PR78 is merged at master `19a09fc15afc021c4920d3546567d36e3268adeb`; its reviewed head is `1366dd14a489b3bad499457872156c143d39d128`. This separate donor branch starts from that reviewed head. Production remains the verified PR61 package and is not deployed by this scope.

## Defect and applied change

The collection exporter checked loot paths, scripts, maps and geometry but did not validate a proposed donor's static flags. Missing state could be treated like an ordinary default. The new source-state gate requires explicit integer metadata and permits only ordinary reference flags: CAN_SWIM (`0x8000`) and REGENERATE_POWER (`0x800`), with no static NPC/dynamic special state. It rejects unknown, player-immune/nonattackable, restricted and malformed states rather than proposing a generic source. Live hostility, attack permission, range, navigation, recovery and loot ownership remain independent runtime gates.

The original donor review request recovered through the supported tool. Pinned `UnitDefines.h` identifies flag `0x40` as unknown. Donors8924 and15591 therefore remain excluded. The regenerated proposal replaces one donor with a different ordinary source and leaves one quest unresolved:24 route records across22quests, with11 replacements and13 appended alternatives, plus19 source-bound creature entries containing262 reference points. Every new loot path is positive, ungrouped and unconditional; the still-blocked aggregate loot-group review is not used as evidence for these routes.

The runtime repair pack now contains those24 reviewed records. Replacements require the exact original quest/objective/type/target/item/count/index, and appends preserve the old valid source. Existing auxiliary item objectives, delivery/supplemental contracts, external dependencies, all81availability contracts/146predicates and all other repair families are unchanged. The base dataset is byte-identical. Only the existing9066/9447 strategy recipes remain vetted, rebound to the exact new repair bytes.

## Actual behavior and classification

The tracked-data regression exercises every reviewed route through the actual DataLoader, typed profile generation/parser and CollectItemObjective. Zero and partial inventory do not become completion; full observed inventory is acknowledged independently. Appended alternatives preserve the old valid collector. These25 cases pass after24 intended assertions failed before the data was applied. The exporter suite passes19 tests after34 intended assertions exposed the missing state checks. All400 analyzer tests pass with one expected Windows permission skip; the existing build warnings remain recorded.

The unchanged dataset test implementation now passes261209 checks across4335quests, plus39 strategy lifecycle checks. Seventeen quests reach controlled generic proof. Five repaired quests retain other obligations in `repaired-quests-still-unresolved.json`. The exclusive classes are3028generic/2strategy/921data/311source/16live/57scripted=4335, with1305remaining IDs and regenerated source correlations.

The152 net additional dataset checks comprise166 newly emitted passing cases minus14 old case names for quests6031/13906. Their newly valid first collector moves execution from index1 to index0 and moves the completed-item alternative suppression to index1. All14 equivalent new names pass; no assertion source is removed and no same-named case changes status. `simulation-count-delta.json` records every affected name. The separate tracked-data test exercises both repaired alternatives independently.

## Evidence and remaining limits

All data is pinned to TC335 `95657f54779467effea8a1749a61ff93abc1d707`, TDB335.25101 SQL `e72c0105ca27779ea3b08792b247210a44d9004fc6ab55cd1b0099d3b10779a9`. The donor flag header SHA256 is `327f11538e9b596da5788309fc3f98d9c3de10678e4a34f8c561fe3562adb1f3`. Exact row, loot, spawn and state evidence is retained. Source possibility does not prove configured-realm phase availability, travel, attackability, drop receipt or completion.

Enabled read-only IDA reverified the original32-bit build12340 binary and inspected bank API registration and item enumeration. A subsequent bank slot-selector/initial-bank-content source probe was blocked twice before execution and stopped. No bank-inclusive absence API or bank condition was added. The independent aggregate loot-group and remaining source-conflict review requests also remain stopped after their identical retries failed; exact payloads are retained without alternate-route execution. The earlier frontier identified43 conflicting availability subjects and one source-matched bank-inclusive subject4023; those remain unresolved. This does not label remaining novel script/crafting/vendor/source work exhausted.

The current output is a comparison fixture until the actual final containing commit passes fresh complete local and Windows/x86 hosted validation. Final publication and merge identities must be read from external `DONOR_*` receipts. Keep all remaining per-quest secondary source/data/script/live obligations, the stopped requests and the clean production milestone. No game was attached and no live quest completion is claimed.
