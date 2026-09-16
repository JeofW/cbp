# W47 continuation checkpoint — verified through ef2e639e

## Attribution and repository state

Master71c79d1c is the prior completed PR46 merge. This continuation found newer PR47 work at7d03c70a and preserved it, rather than replaying the old W42 handoff. PR47 is on audit/next-47-flight-owner-boundaries-20260916 and remains draft/unmerged. Native writes and real Windows push runs succeeded. No local C# runtime, game attachment, native binary replacement, deployment or new master merge is claimed.

Latest completely verified checkpoint: ef2e639eed8687038406fb5b0587ea36c83c5a66, treeb99b1ef24de0fa5859fcdb009dbca53cf7c45e37. Current focused4/integrated17 entries pass; host exit0,3296warnings,0errors,tests_run:false. All51 Wholesome+3 QuestLog aggregate inventories agree;1727 source/config inputs,66 generated members and89 original exported sources verified. Full outer SHA256/CRC and internal manifests checked. Host archive has no internal manifest; its outer digest, commit and build result are checked instead.

## New executed repairs

| Slice | Clean test-only revision | Assertions before fix | Production repair | Unchanged cases after |
|---|---|---:|---|---:|
| Flight-node XML UpdateLevel persistence |2d6e8a6c|12;6/18pass;0unexpected|6ae2d322|18/18|
| Nonrecursive owned flight/POI reset |7cacfd02|44;4/48pass;0unexpected|c62468f9|48/48|
| Typed quest targets through factory/POI/object selection |8ca8c809|41;17/58pass;0unexpected|d53e0fdb|58/58|
| POI object subscription lifetime |b8c21618|19;8/27pass;0unexpected|ef2e639e|27/27|

Every row has actual focused and integrated red/green evidence with all retained groups passing. Counts are overlapping scenarios, not independent bug counts. The first typed-POI test69f6cd6b had14pass/31assertions/13fixture errors: it traversed Composite.Children instead of GroupComposite's executable hidden child list. Only that test traversal was corrected; 8ca8c809 is the clean red revision. Do not mislabel the mixed first result.

Persistence repair restores one XML hydration assignment accidentally removed by7d03c70a. Reset/Clear repair shares one owned cleanup lifetime instead of mutual recursion; it detaches flight state before callbacks and avoids clearing replacement POIs/navigation. Typed-POI repair carries declared type through all turn-in factory constructors and actual POI leaves, refreshes changed quest/type/location ownership, preserves declared coordinates and rejects malformed explicit type text. The five-argument turn-in constructor and unspecified legacy lookup remain available. Subscription repair binds one callback per observed object lifetime, releases retired wrappers and prevents stale multicast tails from clearing replacements. No global logger suppression or new native dispatch was introduced.

## Exact execution artifacts

| Slice / role | Run | Artifact |
|---|---:|---:|
| Persistence red focused |35035552803|10423640175|
| Persistence red integrated |35035552810|10423496103|
| Persistence green focused |35036199062|10423676063|
| Persistence green integrated |35036198998|10423611597|
| Persistence green host |35036199008|10423038117|
| Reset red focused |35037130970|10423692793|
| Reset red integrated |35037131008|10423847250|
| Reset green focused |35037863206|10424042814|
| Reset green integrated |35037863187|10424585336|
| Reset green host |35037863244|10423707416|
| Typed-POI clean red focused |35039483957|10424601834|
| Typed-POI clean red integrated |35039484002|10425000882|
| Typed-POI green focused |35040815292|10424663267|
| Typed-POI green integrated |35040815278|10425117993|
| Typed-POI green host |35040815271|10424908557|
| Subscription red focused |35041672967|10425199051|
| Subscription red integrated |35041672942|10425069418|
| Subscription green focused |35042203558|10425347650|
| Subscription green integrated |35042203551|10425239790|
| Subscription green host |35042203714|10425493281|

Latest three outer SHA256 digests:
- current a71c8f077640851678a24de0a40b89acc99d56a39dc0bfc42f7b8c41a8a4d238
- integrated cbbe78f0b25d193fd2e695182924f734bc3f47a5b2596bc55ec88cb91d817c31
- host 512d7a1ab0ba46ff6177315974f368d125413b6f4675ba4ecda37e9446c5b074

The original-source ZIP is a narrow fixture/owner export; BotPoi and several other edited production files are not included there. Their prepared native blob payloads are checked against the authenticated whole-checkout source-hash manifest, not falsely described as contained in that ZIP. All original/generated tests and workflows are unchanged within each clean red-to-green repair.

## Wider corpus and counterevidence

Recovered pinned6ae2d322 corpus run35036199057/artifact10422698741, SHA25624f1f1f364031f59ae91fcb88f7448675aae7d7bcf9f85bddef94871d7d573c4:10181trackedblobs;1640 C# files,54985 syntax nodes,110657 syntax edges,94 parser-error files;62 recorded logs;4335 unique quests,33134 spawns,106 zones,200 Singular behavior declarations. These are versioned static/census records, not type-bound calls or complete behavioral coverage. The94 parse-error files are not automatically94 build failures.

All62 original logs were individually SHA256/Git-blob checked against the capture/track manifests;20934843 bytes. They are historical logs, not post-fix acceptance. Warning-only latency populations are censored and cannot support an average whole-bot speedup. Retained significant families include slow root/plugin ticks, exceptions/null references, repeated spellbook work, partial navigation, stuck/path generation failures, blackspots, mount cancellation and thread interruption. Existing repaired paths must be distinguished from remaining causal hypotheses.

The bounded source export at0e9e7cfa (run34993973023/artifact10406607020) contains4059files/104619289bytes; every file SHA256 and Git-blob hash was checked. It excludes meshes/runtime-logs/output.zip and is not a full authenticated checkout. Overlay only exact verified newer source blobs before using it for current review.

Dataset relation counterevidence:25 IDs occur in both creature and gameobject spawn namespaces,19 participate in relations,18 have same-map pairs, but no pair is within200yards (nearest about532.05yards). The controlled collision fixture confirms the code contract failure; it does not prove a recorded live collision caused any original symptom. A literal scan of1767 XML files found no invalid explicit TurnInType values; attribute counts include commented examples and are not execution counts.

## Active next slice: real MeshNavigator cleanup

This checkpoint accompanies test-only MeshClearOwnershipRegressionTests.cs with45 proposed cases, SHA25605fd1aaaa86b69405063ad2c15659881c5a0a9631d209ef1af3922dfd61d695a. It calls real MeshNavigator.Clear with controlled mover/stuck boundaries and real managed route/elevator state. Normalizer preflight51->52groups preserves57 generated sources and7 migrations. No Windows result is asserted here at document authorship.

Source-reviewed hypotheses: MeshNavigator.Clear swallows stuck-handler stop signals, can leave state uncleared after a throwing mover, calls MoveStop twice when both local/elevator movement flags are active, and mutates route state after external callbacks can publish a replacement. Require actual assertion-level red with no fixture errors before repair. The prior48 public reset cases used a ProbeMesh boundary and do not close these internals.

## Review / protection / unclosed requirements

Implementing-assistant source review is not independent approval. A native Copilot reviewer request returned without an error, but a subsequent requested-reviewers read had empty user/team arrays; no assignment or review is inferred. GitHub writer errors documented in older handoffs are historical, not reproduced here.

Retain master71c79d1c, backupc43c50d8 and pre-W42 backup8382a7ec; exclude25/43/45. No force push, deletion, merge/deployment/installed binary or mesh replacement, runtime-capture rewrite, Lua modification, credential export or privileged staging bypass. Continue native session/frame/cache/ABA and typed metadata, actual merchant acknowledgement/destructive operations, full XYZ/cliff/safe-lift/wind-rider route coverage, all-class/spec behavior scorecards, latency causality and supervised original WoW3.3.5a build12340 acceptance. No exhaustive-audit completion or all-route guarantee is claimed.
