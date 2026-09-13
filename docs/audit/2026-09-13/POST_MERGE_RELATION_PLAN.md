# Post-merge Wholesome relation identity repair

Approved combined fixes merged in PR #34 at 8382a7ec05a64212ea0a237159dca427a0767425. This is new, separate follow-up work; not included in that merge. The saved test-first history at 52bd941d08f291c810c1ef69ea22d01b8ae55be2 is retained as a parent, not restarted.

Actual baseline run 34759745360, artifact 10318791623, SHA-256 bc174eddf7c30c500196c1ca98479c0fc790c99bb6c646489b776928798c9ab0: actual host/Wholesome builds; 17/23 new relation cases fail and six controls pass. Other twelve integrated entries pass. The complete artifact was recovered and inspected after the approved merge.

Root cause crosses scheduler/profile/parser ownership. AddRelationWork deduplicates by entry alone; GetRelationSpawns chooses the creature map first regardless of declared type; ancestor pickup eligibility repeats that lookup; ProfileBuilder drops type while the host parser already supports it. Preserve the type through all four boundaries, use only the declared namespace and reject invalid types. Do not invent positions or change persistent recovery-key formats here.

The candidate patch changes two production files. It retains normal creature/object controls and all existing tests. The temporary exact-preimage/postimage workflow runs the unchanged complete integrated suite before making any source blobs available for reviewed promotion. No workflow changes refs, creates commits or deploys code. Preflight and final committed outcomes remain pending until inspected. No all-quest, native route, performance, or live interaction claim.
