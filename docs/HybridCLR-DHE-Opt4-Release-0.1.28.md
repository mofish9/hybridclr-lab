# HybridCLR DHE opt4 toolchain 0.1.28 release report

## Status

This release is conditionally accepted for C# source distribution, the locked
Unity 2021, Unity 2022, and Tuanjie 2022 native compile/CTest matrix, and the
six-Base `StandaloneWindows64` target-specific payload evidence lane. It proves
that one resource release can carry several managed payload variants and that
each Base selects and stages only its authenticated variant. It does not claim
Android or iOS device readiness, macOS host execution, CAT project readiness,
or new production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.28-opt4.13`. Its immutable
package ID is
`b054796c7d97f8c6a7399e1d8b25fdb70fff664901f93248d5ab1afed32164fb`.
The manifest records `mode=Release`, `releaseReady=true`, 97 authenticated
files, 40 commands, and clean tracked source commit
`f7f7eef4c8ad36e331d16d2a5e4bb4f65d533cef` with tree
`5bd0e6e1a7bd286c54ba863ea2bf7e1a75825a4f`. The package contains no
PowerShell, batch, command, or shell files.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `f7f7eef4c8ad36e331d16d2a5e4bb4f65d533cef` | none |

No HybridCLR or il2cpp_plus runtime source changed in this release, so no new
runtime tag is created. The `hybridclr_unity` maintenance branch advances to
`18abd01`; package tags remain prohibited by policy. This report commit follows
the toolchain source commit and does not replace the identity embedded in the
release package.

## Target-specific payloads

`resource-update` now accepts one primary `CurrentRoot` plus a named
`CurrentVariantId` and a JSON `CurrentVariantRoots` map. A release may contain
only `windows` and `android`, for example, without manufacturing an unused
`default` payload. Every non-default variant must be selected by at least one
Base registry entry. Invalid or duplicate IDs, a missing selected variant, a
tampered selected payload, and a changed variant-set hash all fail closed.

Each Base binds `payloadVariantId`, its selected current assembly-set SHA-256,
and the complete variant-set SHA-256. Staging copies only that Base's selected
variant. All assemblies in the configured DHE set still go through compatibility,
native guard, runtime plan, and Player dispatch validation; target variants do
not bypass unsupported metadata-evolution checks.

A long-lived Base may have been built by one historical toolchain while its
runtime manifest was assembled from another Release contract. The aggregate
gate now resolves both identities independently. `EvidenceToolchainRoots` are
first authenticated as Release packages; the runtime lock must match byte for
byte, and repository/workflow commits and live trees are then revalidated. The
gate records runtime-contract-only packages so `channel-state promote` can
regenerate the same decision. An unknown, unused, duplicate, revoked, or
wrong-ID root remains rejected.

## Evidence

The final clean regression passed 128/128 checks with `sourceClean=true`, six
changed Base reports, one no-op report, all three real Editor resolver reports,
and all nine historical authority packages. Release evidence binds 14 reports:
one regression, one no-op Player, three current real-header native gates, three
real-Editor resolver gates, and six changed Players.

Three Bases selected `windows` and three selected `android`. The two current
assembly-set hashes differ. All six fixed Windows Players exited successfully,
validated the selected payload hash and Base identity, entered the interpreter
for changed methods, retained AOT dispatch for unchanged methods, and passed
transaction rollback/retry. A Windows Player consuming the bytes named
`android` is a selection/dispatch proof, not Android device evidence.

Current native compile/CTest was rerun against runtime lock
`3d82bb9cb1e621a3bcfecbcef081435be1d1bc90449f0fa9c1a56d639417d807`.
Unity 2021, Unity 2022, and Tuanjie 2022 each report `mergeReady=true`, real
Editor headers, no surrogate headers, and CTest 1/1 passed.

The three real Editors also built ARM64 APKs from the current package/runtime
source: Unity `2021.3.45f2`, Unity `2022.3.62f3`, and Tuanjie
`2022.3.62t12`. Each lane passed 4/4 DHE assembly preflight with 26,287 native
guards and zero unsupported changes, packaged only `arm64-v8a/libil2cpp.so`,
and matched its APK native hash to Bee/Gradle staging. These workflows stopped
at the intentionally missing device-produced `dhe-player-result.json`.

- Regression: `artifacts/dhe-cross-target-formal-f7f7eef/regression-clean.json`
  (SHA-256 `1493a44296a3c427234ba11fd3ebb89a5edfed8111b5cd2dfe8b00a8f9d08343`).
- Release evidence:
  `artifacts/dhe-cross-target-formal-f7f7eef/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `93388c45933096cd0e0935e9c2f1b07d6856e56d4097cae49be9171484c21c35`).
- Release package manifest SHA-256:
  `d8af4aa2e42465ae83c737597be4a19476fae46d7d69d686ac1634ab374f935d`.
- Release package verify, doctor, and schema-gate report SHA-256 values:
  `dd160ccc016b7db8eba2cfda48e3d5c67a7bdc26c5d2c5639e90a7ef38072caf`,
  `c2bcad21401482a40fd1cf18a6d108180a5ebbc629fa013c8afe945ccf6409d2`,
  and `234af269b3332aab3da6fe65ba99851ea5ed5f41db71a09c65b4f68606f9e495`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements remain incomplete. APK construction is not a device
  result.
- iOS Xcode generation, signing, native compilation, staging, and device smoke
  remain incomplete. No Windows result is used as iOS evidence.
- macOS host execution and non-NTFS channel-state semantics remain untested.
- The CAT project has not completed its own all-hotfix-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gates.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before adopting 0.1.28, pin the 0.1.27 package ID
`3af3d63e986470de6556c6023c438374e030c240aea3c9f2b168c1a0837c76fc`
and the `hybridclr_unity` maintenance commit `22fb364`. After a target-variant
release has been promoted, never move or reinitialize its protected channel
head. Roll back behavior with a forward revision from the actual head, retaining
every still-supported Base, its selected variant, and every referenced historical
tool/runtime authority package.
