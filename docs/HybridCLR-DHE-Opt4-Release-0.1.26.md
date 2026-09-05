# HybridCLR DHE opt4 toolchain 0.1.26 release report

## Status

This release is conditionally accepted for C# source distribution, the locked
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` evidence lane,
and the existing three-engine native compile/CTest matrix. It removes the DHE
tool-repository ancestry dependency when a long-lived channel contains Base
Players created by several explicitly authorized historical Release packages.
It does not claim Android or iOS device readiness, macOS host execution, CAT
project readiness, or new production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.26-opt4.11`. Its immutable
package ID is
`b23759fccd900ba6f5ff6f250d75676c3b539f9af400ae2644412678183a3cf9`.
The manifest records `mode=Release`, `releaseReady=true`, 96 authenticated
files, 40 commands, and clean tracked source commit
`fa780ee9e8e8679d213f5a98dcf86ff578d365d4` with tree
`d142fc251ce830c73327c388c6e01670a392a4aa`. The package contains no
PowerShell, batch, command, or shell files.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `fa780ee9e8e8679d213f5a98dcf86ff578d365d4` | none |

No runtime, IL2CPP hook, or Unity package source changed in this release. No
runtime or `hybridclr_unity` tag is created. This report commit follows the
toolchain source commit and does not replace the identity embedded in the package.

## Portable evidence authorities

The authenticated `manifests/dhe-toolchain-evidence-authorities.json` uses
`explicit-package-id-set-v1` and binds seven historical Release packages by
toolchain version, Package ID, source commit, and source tree. The current
package remains externally pinned and cannot authorize itself through the set.
The production regression independently recomputed and matched all seven package
identities before release.

For project publication, `resource-release-gate -EvidenceToolchainRoots` accepts
only historical packages actually referenced by active Base evidence. It resolves
roots by Package ID, rejects missing, wrong, duplicate, current-package, and
unused roots, and refuses Git fallback for any unknown or revoked ID once the
explicit set exists. The gate records the policy and authority-set SHA, all
evidence Package IDs, every resolved package root/version/head/tree, and each
Player's authority mode. `channel-state promote` reconstructs those roots,
reruns the gate, and compares every non-time field before CAS publication.

The formal portable test copied the two 0.1.20 Release packages to new directories
with the C# `install` command. The 0.1.26 Release package then qualified the
existing five-Base revision 2 resource without `ValidationSourceRoot`: four
Players resolved package `7757d0...`, one resolved `3982ee...`, all five used
`authorized-historical-package`, and `portableHistoricalToolEvidenceAccepted`
was true. Promotion regenerated the same gate and committed revision 2. The
protected head binds the 0.1.26 Package ID, five Player reports, all three engine
workflows, ledger
`b35265530e68ea4067d7c056e07bffb8a01e43b5c46ff82fd0c0845c14e5e8dc`,
and exact active-Base coverage.

## Evidence

The final clean regression passed 121/121 checks with `sourceClean=true`, five
changed Base reports, one no-op report, all three real Editor resolver reports,
and exact validation of all seven historical authority packages. Release evidence
binds 13 reports: one regression, one no-op Player, three real-header native
gates, three real-Editor resolver gates, and five changed Players. Existing
Player and native executions are revalidated historical evidence; they were not
rerun on Android, iOS, or macOS for this release.

- Regression: `artifacts/dhe-evidence-authority-fa780ee/regression-fa780ee-clean.json`
  (SHA-256 `abdcdbdddd2475957612af58932cfe943396318d89aec8c719797cf9d7a8fc49`).
- Release evidence: `artifacts/dhe-evidence-authority-fa780ee/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `12f77d4b69576e6df4120c2648b4940178212f9120f178887b927916d94fc025`).
- Portable state-bound gate: `artifacts/dhe-evidence-authority-fa780ee/portable-e2e/release-gates/portable-r2.json`
  (SHA-256 `9c74ed1de9ece7e58781e35adf3f923e51bb32c2c8bd7b81ab05c64989e949ed`).
- Promoted channel head SHA-256:
  `2cc796c3f23d3e8e7b7c9865634b8eb3ccaaca567260ec543ae48e5f1165390a`.
- Release package manifest SHA-256:
  `b352237d4c669ccc45d68a91e00dbc8a33c6446f41d3b86fa0c5aa5aef4a52e8`.
- Release package doctor SHA-256:
  `d01a339e286c47e795ddb56cfa260eea65b3df36fd7f4d7caa9f75aefa10a278`.
- Release package schema gate SHA-256:
  `cc6f596047dd81323bfc515f10b7e7d722d450a4a9b77974b5a3404cb8651589`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- The feature removes the DHE tool Git ancestry requirement. Historical Player
  runtime/source/header evidence remains subject to the existing archive contract
  and must still be accessible and byte-identical during qualification.
- The built-in CAS backend and package relocation were exercised on Windows NTFS.
  macOS host execution and candidate network-filesystem semantics remain untested.
- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements are incomplete for this release identity.
- iOS Xcode generation, signing, and device smoke are incomplete.
- No Base Player has yet been bootstrapped with the 0.1.26 package; the current
  package path is covered by package/gate logic, while the five formal Players
  intentionally exercise the historical-package path.
- The CAT project has not completed its own full-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gate.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before adopting this release, pin 0.1.25 Package ID
`7f5998a95c8474b46b0eb9487d7369bc055f52bf05e3e9bc5087e7a9902eb44d`;
historical evidence then requires the earlier Git-ancestry workflow. After a
channel has promoted a 0.1.26-approved head, never move or reinitialize the head.
A rollback tool must publish a forward revision from the actual protected head
and retain the Git history required by 0.1.25. Removing an authority ID is an
explicit revocation: retire or replace every active Base that depends on it
before the next gate. Keep all content-addressed artifacts and channel history.
