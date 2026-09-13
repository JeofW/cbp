# Paladin support: blessings, auras and cleansing

Baseline d62f2c7b73305087dd23f45d8e881ae38d3ab3e7. This is a focused owner-requested continuation, not completion of the whole audit. Nothing is merged or installed.

## Reproduced owners

Common's separate blessing selectors treated Mark of the Wild as Kings coverage, omitted Greater variants and only allowed Wisdom when manually selected. Aura selection ignored external coverage. Retribution's Heal composition contained no cleansing action even though a separate class-capability table mentioned dispels.

The new shared support owner chooses a current eligible recipient and a useful learned spell, then revalidates before dispatch. Blessings and aura coverage remain separate decisions. Full aura enumeration retains caster ownership rather than collapsing duplicate names. Normal/Greater variants are one coverage category. The caster preserves its own useful contribution, prefers missing Kings, selects Wisdom for observed caster classes/forms and Might for physical recipients, and avoids Wisdom on non-mana recipients. Explicit choices remain available. This is observed coverage, not a negotiated raid assignment or knowledge of every other player's talents/rank.

Retribution now invokes learned Purify/Cleanse after emergency Lay on Hands and before routine self heals. Precombat support also invokes cleansing before maintenance buffs. Actual spell availability and removable types decide which action is attempted. Purify cannot remove Magic/Curse; Cleanse's full removable mask is checked so it cannot incidentally remove protected Magic merely to cure a Poison. Conservative protected-effect IDs and enable/self-versus-group settings retain manual encounter control. The protected-effect list is not a complete encounter database.

Mounted/transport travel, casting/channeling and Food/Drink suppress maintenance. Dead, hostile, out-of-range, out-of-sight and departed recipients are ineligible; a blocked first recipient does not hide the next eligible one. Existing shared spell dispatch still reports attempted backend submission, not server-confirmed removal.

## Actual red and green

- Test-only ad5c226defa88c7788d5f020aa3c8120f147c8dd, run 34748812754, artifact 10315590688: actual linked Common/Retribution/TreeSharp compiled; 29 of 44 scenarios failed, 15 controls passed. Complete ZIP inspected.
- First preflight 34749108177 stopped at a strict source hash check because Windows decoded a retained UTF-8 comment using its default code page. No source blobs were promoted. PYTHONUTF8 fixed the preparation environment without changing expected source hashes.
- Preflight 34749360464: 44/44 controlled support scenarios passed but full Singular compilation caught LocalPlayer-only roster properties used through WoWPlayer. The production adapter was corrected; this failure was not ignored or hidden by the portable fixture.
- Exact-source preflight 34749660863 at 64191e13eef668a082d71c14db57d2146c4b7b0f: all twelve combined entries built/executed with exit 0, all 44 support cases pass, and all 99 tracked Singular files compile. Complete artifact 10315088288 downloaded and inspected, SHA-256 fef87874cd5499389a17420ac91de7625b33142bb964a418a7b5634479019a85. All six promoted source blobs match verified-blobs.json.

The portable suite controls only external world observations/dispatch. It links the actual support composition and tests chosen spell/recipient plus absence of swallowed exceptions. Full host/routine compilation is a separate gate. No attached client or damage/dispel benchmark is claimed. The temporary preparation and write-token preflight workflow are removed in the production promotion. The committed-source combined run remains to be inspected before a final PR PASS assertion.

## Compatibility and remaining gates

No new Lua command or modern role/spell/aura API is introduced. This uses existing original-client managed adapters, learned spell names and 3.3.5a aura/dispel metadata. Native dispatch, actual server dispel acceptance, manual raid assignments, simultaneous Paladin maintenance and client latency require live acceptance. Do not claim universal blessing optimization or every disease is safe to dispel.

Self review only; independent review is pending. Rollback is a reviewed revert of the support owner/composition/settings while retaining the failing/passing evidence. Other no-pull, Wholesome and native route work is separate.
