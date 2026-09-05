# HybridCLR DHE opt4 toolchain 0.1.25 release report

## Status

This release is conditionally accepted for C# source distribution, the locked
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` evidence lane,
and the existing three-engine native compile/CTest matrix. It closes the local
release-head gap left by 0.1.24 with a C# `channel-state` command and the
`filesystem-cas-v1` protected-state contract. It does not claim Android or iOS
device readiness, macOS host execution, CAT project readiness, or production
performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.25-opt4.10`. Its immutable
package ID is
`7f5998a95c8474b46b0eb9487d7369bc055f52bf05e3e9bc5087e7a9902eb44d`.
The manifest records `mode=Release`, `releaseReady=true`, 92 authenticated
files, 40 commands, and clean tracked source commit
`a927b8b0f497a842431789761c37256cf9a3b6f7` with tree
`a6fc5cd84247f8ec31a625ca6c7fe8929fef6d69`. The package contains no
PowerShell, batch, or shell files.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `a927b8b0f497a842431789761c37256cf9a3b6f7` | none |

No runtime, IL2CPP hook, or Unity package source changed in this release. No new
runtime tag or `hybridclr_unity` tag is created. This report commit follows the
toolchain source commit and does not replace the source identity embedded in the
released package.

## Protected channel state

One channel snapshot now supplies the previous ledger, channel, next revision,
and expected head to both `resource-update` and `resource-release-gate`.
`adopt-existing` requires an explicit acknowledgement and installs an already
published ledger head once. `promote` fully regenerates the state-bound aggregate
gate, rejects any non-time field drift, copies resource and approval bytes under
content-addressed paths, and atomically replaces `head.json` only after comparing
the snapshot-bound head SHA while holding the exclusive channel lock.

The released package host adopted `dhe-demo-base-growth` revision 1 and promoted
the five-Base revision 2 continuation. The resulting state has registry revision
4, five Player reports, and all three engine workflows. Its ledger is
`b35265530e68ea4067d7c056e07bffb8a01e43b5c46ff82fd0c0845c14e5e8dc`;
the promoted head SHA-256 is
`480ac5511c94217021bb02d78306ca0691affe91ab0fc120fd0f033559ba7a2d`.
Two independent `dotnet` processes then competed on a fresh copy of the same
revision 1 state. Their exit codes were `1,0`; the loser reported that the head
changed, and the store contained exactly one revision 2 head.

The regression also covers an uninitialized genesis snapshot, snapshot and gate
tampering, stale replay, exact active-Base coverage, orphan staging directories,
and recovery when the head was committed but writing the requested external
snapshot failed. State, resource, gate, and Player inputs additionally reject
reparse points before traversal. Recovery reads the live head with
`channel-state -Operation snapshot`; it never replays the old gate.

`filesystem-cas-v1` requires a protected filesystem whose exclusive handles
coordinate all publishers and whose same-directory rename/replace is atomic.
Object storage and network filesystems with weaker semantics require an adapter
with immutable object keys and a conditional head write. A local state head and
a separately promoted CDN pointer are not one atomic transaction.

## Evidence

The final clean regression passed 120/120 checks with `sourceClean=true`, five
changed Base reports, one no-op report, and all three real Editor resolver
reports. Release evidence binds 13 reports: one regression, one no-op Player,
three real-header native gates, three real-Editor resolver gates, and five
changed Players. Existing Player and native executions are revalidated historical
evidence on the authenticated source ancestry; they were not rerun on Android,
iOS, or macOS for this release.

- Regression:
  `artifacts/dhe-channel-state-a927b8b/regression-a927b8b-clean.json`
  (SHA-256 `a45fcdec3a5531a82fd04b194e5ba5451ccb49380e4c826a241bc9f99507ac66`).
- Release evidence:
  `artifacts/dhe-channel-state-a927b8b/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `0260af5a701b6081d2c31fdc811e6729639067f70490d5cb7a6a728380fcb6b8`).
- State-bound continuation gate:
  `artifacts/dhe-channel-state-a927b8b/release-gates/state-bound-r2.json`
  (SHA-256 `57289e250805c7b1660a58063e0c5c2b0f43649e436b22976b93cf7946ca0122`).
- Release package manifest SHA-256:
  `49db54105201e5ae3371283b9b6591be61adffe7a4e6cdf49e7218380dc67520`.
- Release package doctor SHA-256:
  `5a6cbc03360d2a467cf8163371600db4375f9f93650a2a24af59d2d768d29413`.
- Release package schema gate SHA-256:
  `41dda46e9fd4c207fdd5eb190c6b243c004af27e370c987fedf495b13208c5c9`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- The built-in CAS backend has been exercised on local Windows NTFS. macOS host
  execution and candidate network-filesystem semantics have not been tested.
- S3, OSS, CDN, and other object-store publication requires a project-specific
  conditional-write adapter; this release does not provide a vendor adapter.
- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements are not complete for this release identity.
- iOS Xcode generation, signing, and device smoke are not complete.
- The CAT project has not completed its own full-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gate.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before adopting this release, pin the 0.1.24 package ID
`4e9178bb897dbc0899e6bab3e510112c08c96a2531baf38a215292bfdc05a179`.
After a channel is adopted, do not delete, move backward, or reinitialize its
head. Stop new promotion and keep serving a retained content-addressed artifact;
the next hotfix must be a forward revision from the actual protected head. A
project leaving DHE must remove DHE runtime-plan assets, restore its ordinary
HybridCLR loading path, and rebuild the Base Player.
