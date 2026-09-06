# HybridCLR DHE opt4 toolchain 0.1.29 release report

## Status

This release is conditionally accepted for the C# Base-onboarding and resource
release workflow, the locked Unity 2021, Unity 2022, and Tuanjie 2022 native
compile/CTest matrix, and the existing six-Base Windows Player evidence lane.
It proves that one config-driven operation can retain or extend the active Base
registry and build one multi-variant resource candidate without publishing a
registry-only or resource-only partial result. It does not claim Android/iOS
device readiness, macOS filesystem behavior, CAT project readiness, or new
performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.29-opt4.14`. Its immutable
Package ID is
`0f91d04026c1c98e7ea4b311037f726a3515d3a033fa734d65be9aee0f1e6368`.
The manifest records `mode=Release`, `releaseReady=true`, 102 authenticated
files, 41 commands, and clean tracked source commit
`b4d1c07eb7af91718882d5413f2d7e7a746ce5f2` with tree
`bf4a60924dbc8ddecb354179a18be72a31dc2dc0`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE toolchain source | `optimize/dhe-base-onboarding-v8.13.0` | `b4d1c07eb7af91718882d5413f2d7e7a746ce5f2` | none |

No runtime or Unity package source changed. No new runtime tag or package tag is
created. The existing runtime and package identities remain the 0.1.28 locks.

## Workflow contract

The new `resource-release-build` command consumes one schema-validated,
config-relative JSON document. It supports three explicit registry outcomes:

- first release creates revision 1 from immutable Base archives;
- a new main package creates one direct successor and retains every old Base;
- an ordinary hotfix reuses the exact registry without increasing its revision.

Current assemblies are named variants with exactly one primary variant. Every
active Base must select one supplied variant, every supplied variant must be in
use, and the resulting manifest, validation, runtime plan, ledger, registry, and
selection sets are re-read and cross-checked before publication. Release mode
requires a live SHA-256-pinned channel snapshot. The command writes to a unique
sibling directory and performs the final rename only after a complete schema
gate. `-ForceOutput` first backs up the previous directory and restores it if the
final move fails.

The output is still a candidate. Projects must stage `<outputRoot>/resource`,
run one Player result per active Base, execute `resource-release-gate`, and use
`channel-state promote`. The builder does not upload content or advance a head.

## Evidence

The clean regression passed 139/139 checks with `sourceClean=true`, six changed
Base reports, one no-op report, and all three real Editor resolver reports. The
11 new workflow checks cover initial registry creation, a three-engine registry
followed by multi-Base onboarding, preservation of old Bases, no-revision reuse,
per-Base variant selection, exact manifest coverage, stale snapshots, missing
variants, duplicate Bases, partial-output rejection, replacement recovery, and
a positive protected Release continuation.

The positive Release regression adopted revision 1, used its protected snapshot,
and rebuilt revision 2 with the exact parent ledger, channel ID, six-Base registry,
and two target variants. A separate direct run continued the retained protected
channel from revision 2 to revision 3. Neither result was promoted as production
game content.

Unity 2021, Unity 2022, and Tuanjie 2022 native compile/CTest were rerun against
the unchanged locked runtime. All three profiles report `passed=true`,
`mergeReady=true`, real Editor headers, no surrogate headers, and CTest 1/1.
Release evidence binds 14 reports: regression, no-op Player, three native gates,
three resolver gates, and six changed Players.

- Regression: `artifacts/dhe-base-onboarding-formal-b4d1c07/regression-clean.json`
  (SHA-256 `e80bc79a46e069bed8f4367ef82751c6e2d2ee680a127f0f535a5ef5849721f5`).
- Release evidence:
  `artifacts/dhe-base-onboarding-formal-b4d1c07/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `4c35b9dca53b83213aac2565871c7ab5a2a112c223afcbdee4f1c9d046da0f2f`).
- Release manifest SHA-256:
  `75c228cb89689f747b7c6b03fe79a8a63eef73626fd560da4af2b8a6f509b71f`.
- Release verify, doctor, and schema-gate SHA-256 values:
  `66f3a54104ab9e872e90445911ab936d137048040df3b773acb07ab71039f1fa`,
  `31680f9a66604a43ae4622124fec64cf9abd1722edd6c58828ecca2bffb2f1aa`,
  and `c742767fb69bfd539f2e0f681271a31463522c6dc251d7b313ae4295570eeb7e`.

## Remaining gates

- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements remain incomplete. The previously built APKs are not
  device evidence.
- iOS Xcode generation/signing, native linking, device correctness, memory, and
  tail latency remain incomplete. The C# host path is platform-neutral, but no
  macOS/iOS environment was available.
- Atomic rename and exclusive-lock behavior has been validated on the current
  Windows filesystem only. A production network/object store needs an adapter
  with the same conditional-head contract.
- CAT has not run its own Base bootstrap, all-hotfix preflight, catalog staging,
  device smoke, or performance gates.
- Unsupported existing value-type layout, inheritance/interface/vtable, ABI,
  GC, and unsupported field-storage changes continue to fail closed and still
  require a new Base Player.

## Rollback

Before adopting 0.1.29, pin the 0.1.28 Package ID
`b054796c7d97f8c6a7399e1d8b25fdb70fff664901f93248d5ab1afed32164fb`
and use the explicit `base-registry` plus `resource-update` sequence. After a
resource head is promoted, rollback remains a forward release from the actual
protected head; never move or reinitialize a channel ledger.
