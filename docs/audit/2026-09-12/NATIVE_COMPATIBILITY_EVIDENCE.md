# Native asset compatibility — investigation before route-policy changes

Private host branch: audit/next-07-integrated-mesh-20260912. The old PR fixes were combined with master c43c50d8d5d6775055f19bf018b52930a264d4a4 without changing master. Combined regression run 34702201008 at 43bc3ef404be737f6b822bc8238ae0946a31a768 passed all eight entries: five quest/vendor executables, explicit Singular compatibility, portable elevator tests and 33 analyzer regressions. Complete private artifact 10300204674 SHA-256 18f118cae438cf942a978fab0d4278639376e8602a76abd3284c0eefac6aa72b was inspected. This closes the old sibling-branch integration gap for those source bytes, not live acceptance.

## Actual upload and failed native controls

Native runs 34701137183 and 34702200992 retrieved actual LFS objects, not just pointer text. The inventory has 6,054 files totaling 2,985,832,908 bytes. Git LFS fsck passed. Native DLL SHA-256: 465cda3a863f2f0742d64ba03bfe221e44e5f33ced87fe3e4b9bc1587010998b; x86 .NET runtime 10.0.12.

Twenty direct FindPath requests (two captured origins to Grod, a reversed request, and two nearby controls, each repeated four times) returned no points, status 0x80000008, raw fail-step 2. No tile-loaded callbacks occurred; three nearest-poly queries also failed. These are loader/query failures, not successful route replays or performance improvements.

The first harness used the public raw FindPath API without the higher-level host's explicit endpoint EnsureTiles calls. That is a harness coverage limitation, not proof of production failure. The subsequent explicit EnsureTilesAroundPosition control also loaded zero tiles and returned no path. Further controls tested explicit native LoadMaps, Default filters and unrestricted filters; all still loaded zero tiles. Unrestricted filtering is diagnostic only, never a proposed safe route policy.

Run 34702200992 artifact 10300915336 (SHA-256 c1f10b0a1ac46cb4503400dfa6d0e4852fd5f89f5e9e8fafdf75f74fab929faa) records the actual loaded DLL path, working directory, file presence and first 128 bytes. The correct 001.mmap and sampled 0013431.mmtile / 0013134.mmtile are visible next to the loaded DLL through the explicit mmaps junction. Thus wrong working directory, missing files, missing explicit initialization and faction filtering do not explain the zero-tile result in these controls.

## Confirmed format gate mismatch

The sampled tiles begin with little-endian words MMAP magic 0x4d4d4150, Detour version 7, and mmap version 6. The checked-in DLL's tile reader reads these three words, then branches only for mmap version 5 or 4. Format 6 takes the rejection path before the Detour tile can load.

Binary provenance for the exact DLL hash above, image base 0x10000000:
- RVA 0x10f09–0x10f33: three-word fread, magic check, third-word version read.
- RVA 0x10f5e–0x10f61: compare version against 5, otherwise branch to RVA 0x11113.
- RVA 0x11113–0x11116: compare against 4, otherwise reject via RVA 0x1130f.

This is a confirmed compatibility mismatch between the checked-in DLL and the inspected format-6 assets. It is NOT evidence that the uploaded geometry is corrupt, nor proof that the historical installed DLL was byte-identical to this DLL. Existing logs show prior successful tile loads; deployment identity remains important.

## Newly located canonical upstream source

Public Likon69/Navigation-C- at pinned commit 221dfe2877fa3f749ada49c98687e99fac74d437 contains Navigation/MoveMapSharedDefines.h blob f42b7d3065729cb2d4c617e91d2fe3e36e01e310 defining MMAP_MULTI_TILE_VERSION 6. Its old v5 comments/README must not override the actual definition. Capture and inspect this source, its format reader, ABI, build inputs and license before rebuilding or changing the private host dependency.

The source acquisition workflow only archives the pinned public source. It does not run upstream code, change the checked-in DLL, rewrite mesh version headers, modify assets, or deploy. A source-built format-6 candidate must pass ABI, geometry and regression gates before being selected for any production build. No compatibility or route success is claimed from source availability alone.
