# Unity 2022 package-owned DHE tool delivery

The required build tool now ships with `hybridclr_unity` maintenance branch
`optimize/v8.13.0`, commit `02dc136e4f2eb7d32990486e7bdbf58bbe1772fd`.
It stays on upstream 8.13.0. There is no new package or runtime tag.
HybridCLR remains `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`
(`v8.13.0-opt5`); Unity 2022 IL2CPP remains
`658aa64923e568a497e11640b340f316704d02f9` (`v2022-8.14.0-opt5`).

The package's canonical source tree hash is
`1A4F18C4FAE18375C2C6CC0BD4907D96221DF6CA241184F53C5229ADDE191432`,
using the existing Git/importer-meta exclusion. Tool source was built from clean
Lab commit `ad93b9dbc5103b3c3141c1d2dd897c63218c9ea2`.
The binary bundle ID is
`713b0e7d2da71eeebfc5df21b6897861e89b571e3e2d781235cde0cb1bec9b65`;
the executable DLL hash is
`E915A090C80CA421F31989EEC01FAC775AF8317A3BFB1B6AD9DBA0813E2BE491`.

## Distribution and review

Old distribution: 308 manifest-listed files plus its manifest (309 physical
files), 4,974,176 bytes. New distribution: 83 physical files, 3,014,363 bytes.
The earlier progress count of 308 excluded the manifest; these totals include it.
The new bundle consists of four executable/dependency/config files, 64 DHE JSON
schemas, seven templates, three policy/layout manifests, two notice documents,
build provenance, source boundary and the tool manifest.

Tests, fixture sources, historical runtime patches, compiler projects, PDB and
Windows apphost are excluded. The portable DLL is under Unity-ignored `Tools~`,
so neither it nor its duplicated dnlib dependency enters a Player or the Unity
script compilation graph. The Editor API verifies files before launching Unity's
bundled .NET 6 runtime; project build machines need no SDK. The Lab compiler and
source/test history remain reproducible and independently reviewable.

Review addressed the original project-external dependency and missing binary
distribution verification. Exact manifest file-set/hash validation and the
existing Release rejection remain enabled. Lab command access is disabled in
the compiled package profile; in-Editor calls additionally reject workflows
that could launch a second Editor on the same project. The first real Editor
compile exposed an ambiguous PackageInfo type; the committed alias fixes it.
No native runtime, ABI, synchronization or game execution code changed.

## Current evidence

All paths below are relative to `F:/hybridclr_artifacts/dhe-package-tool`.

| Check | Result | Evidence |
|---|---|---|
| Portable bundle with Unicode/spaces, real argument roundtrip, MV, missing/tampered/extra files, nonzero exits, Release rejection, Lab-command restriction | 13/13 | `committed-test/result.json` |
| Managed execution-plan and public-loading regression | 126/126 | `execution-committed-result.json`; records clean package 02dc136 and Lab ad93b9d |
| Real Unity 2022.3.62f3 compile and invocation | exit 0 | `unity-verify-fixed.log`; final code matches committed source |
| worker3 refresh/compile and bundled tool verification | exit 0 | `worker3-refresh.log`; migrated package source hash matches |
| Current-73 generation against the existing v33 Base using Unity's .NET host | passed | `resource-final-73/dhe-resource-update-validation.json` |
| New vs previous tool payload | 11 files, zero hash differences | `resource-final-73/payload` vs previous `dhe-opt5-review/resource-73/payload` |
| Runtime plan parity | identical SHA-256 | `3484C993B7E5E01181257E3FA916EC08297923210202E9B554C8C216573A5CC3` |
| Resource staging, frozen Base MV and immutable Player/GameAssembly checks | passed | `stage-result.json` |
| Binary tool manifest schema | passed | `manifest-schema.json` |

Evidence hashes:

- portable checks: `D317DDCE67C71704F8B48ED2F9651F00F6C01F970B56F9559AB19477172480B0`
- managed regression: `8AB8CF6A2721CD2F876B3DE613BA761FD62F48D234708942166090A912737302`
- resource staging: `C9917B5E73597DEEE58F29BF8F00CCE8257E86071323554606AD614C588B1129`
- worker3 refresh log: `5DC67F60263DC1D2183AEF0D960E6CD3C958EEB279D766D929D51CC78BF566F9`

This is tool delivery qualification on Windows. No new Player was built or
executed in this change; the previous 59-to-73 native Player and correctness
results remain tied to package 044d553 and their original artifacts. macOS host
path handling is implemented but untested; Android/iOS and performance/memory
qualification are not claimed. Unity 2021 and Tuanjie maintenance are unchanged.
The bundle remains Exploratory and is correctly rejected by RequireRelease.

## Project state and rollback

worker3 retains `com.code-philosophy.hybridclr@8.13.0` and records the new package
and tool identities in `ProjectSettings/HybridCLRSourceLock.json`. Its Assets
remain clean. The existing DHE assembly/build/provider integration is still not
enabled; packaging the tool does not implement those game-specific callbacks.
SVN changes are scheduled locally, not committed.

The old source distribution was verified intact, its SVN add scheduling
reverted, and the exact directory moved to
`F:/hybridclr_artifacts/worker3-opt5-migration/retired-source-toolchain`.
The prior package is saved as `package-before-bundled-tool` in the same parent.
Revert package commit 02dc136 and restore the previous package/tool/source lock
as one set to recover the old delivery path. Runtime opt5 is unaffected.
No stash was created; no unrelated changes were discarded.
