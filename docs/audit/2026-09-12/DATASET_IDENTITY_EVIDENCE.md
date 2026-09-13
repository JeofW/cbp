# Q01 — content identity and atomic dataset publication

Test baseline: integrated c19e730b774bb2cc982bce0c739b25515f86c6a9. Production source: runtime-snapshot/Bots/WholesomeAutoQuest-master/DataLoader.cs, baseline blob 5dea09310ae6b299c95c3c43eea49e9583a31d0f.

The existing helper hashes absolute path, size and modification time rather than bytes. In addition, Load assigns _database before fingerprinting and publishing prerequisites. A publication failure can leave cached partial state, and a separate file read for the fingerprint could identify bytes other than those deserialized.

Reproduction uses the actual DataLoader and manifest helper. It covers same-size/same-time edits, metadata-only changes, relocation, input order, role-preserving content association, ambiguous duplicate roles, empty manifests, missing reads, failed publication retry, cached snapshot identity and BOM-aware decoding. This commit contains tests and evidence only; red results must be inspected before repair.

Design: versioned content manifest using normalized logical filenames and streamed SHA-256, not absolute paths. Duplicate roles fail explicitly. Load hashes and parses one byte snapshot, preserving BOM decoding, and publishes cached database/fingerprint only after prerequisite publication succeeds. The 4.4 MB global file is read at dataset load, not every pulse. Existing cached-load semantics remain. This changes the fingerprint once on upgrade, intentionally invalidating old metadata-derived recovery context; no quest data is rewritten.
