# HybridCLR DHE opt4 toolchain 0.1.24 release report

## Status

This release is conditionally accepted for C# source distribution, the locked
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` evidence lane,
and the existing three-engine native compile/CTest matrix. It adds a standalone
project-facing `resource-release-gate` command that approves one resource
release only after every active Base has executed the exact candidate ledger
head. It does not claim Android or iOS device readiness, CAT project readiness,
or production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.24-opt4.9`. Its immutable
package ID is
`4e9178bb897dbc0899e6bab3e510112c08c96a2531baf38a215292bfdc05a179`.
The manifest records `mode=Release`, `releaseReady=true`, 89 authenticated
files, 39 commands, and clean tracked source commit
`3ec3fb738457926919034c56e2bae4bb8120f5d6` with tree
`a15a703d0a9aad6c4e0e5afb186ba775dc7fb691`. The package contains no
PowerShell files.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `3ec3fb738457926919034c56e2bae4bb8120f5d6` | none |

No runtime, IL2CPP hook, or Unity package source changed in this release. No new
runtime tag or `hybridclr_unity` tag is created. This report commit follows the
toolchain source commit and does not replace the source identity embedded in the
released package.

## Resource release gate

`resource-release-gate` is the publication boundary between per-Base Player
evidence and the external release service. It revalidates the candidate
manifest, compatibility report, release ledger, Base registry, runtime plans,
payload variants, AOT metadata selection, build/native identity, staging output,
and Player execution. The Player Base IDs must exactly equal the candidate's
active Base set.

The expected channel, next revision, and previous ledger SHA must come from
protected release state. Revision 1 requires explicit
`-InitializeReleaseLedger`; revision 2 and later require the exact protected
parent and reject reinitialization. The command removes a stale external output
before validation and rejects outputs inside resource, toolchain, validation,
schema, or Player evidence roots. It does not perform the external atomic
compare-and-swap that promotes the protected channel head.

The released package host independently passed both lifecycle cases:

| Case | Channel/revision | Ledger / parent | Active Base reports |
|---|---|---|---:|
| genesis | `dhe-demo-production` / 1 | `f4818d751f1243db8c5b84a7b1245e355bf1c1e09b3d8d75c5675d97030a3426` / null | 5 |
| continuation | `dhe-demo-base-growth` / 2 | `b35265530e68ea4067d7c056e07bffb8a01e43b5c46ff82fd0c0845c14e5e8dc` / `47bf52c6e9db1441df957bd27c0e38d949cb0c776c5060e6a542fb8404c63770` | 5 |

Both reports cover all three engine workflows. The regression also rejects a
missing Base, duplicate/foreign release identity, wrong candidate or parent
ledger, wrong channel or revision, continuation reinitialization, invalid
rollback evidence, and output under a protected input root. A genesis invocation
without explicit initialization failed and produced no report.

## Evidence

The final clean regression passed 119/119 checks with `sourceClean=true`, five
changed Base reports, one no-op report, and all three real Editor resolver
reports. Release evidence binds 13 reports: one regression, one no-op Player,
three real-header native gates, three real-Editor resolver gates, and five
changed Players. Existing Player and native executions are revalidated historical
evidence on the authenticated source ancestry; they were not rerun on Android or
iOS for this release.

- Regression:
  `artifacts/dhe-resource-release-gate-3ec3fb7/regression-3ec3fb7-clean.json`
  (SHA-256 `f6e216d426e3f6b440412343780c427b30d2b635c8e451e9fb417c497e7e6162`).
- Release evidence:
  `artifacts/dhe-resource-release-gate-3ec3fb7/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `3a18f313856fdd69998b2e0d0b9a402cbad0b9827840b5c6a3dbef4f503b07c1`).
- Genesis aggregate report:
  `artifacts/dhe-resource-release-gate-3ec3fb7/release-package-r1-genesis.json`
  (SHA-256 `73f9998ba4350fa38cd0a0477d9b961d0c3c5ee4ea09d96cb4048dcc7d53b8a8`).
- Continuation aggregate report:
  `artifacts/dhe-resource-release-gate-3ec3fb7/release-package-r2-continuation.json`
  (SHA-256 `9ed6c49dc10eb24b16fd5944fdbb45a3011140251a87c050941e67a3791007c7`).
- Release package manifest SHA-256:
  `fe3406abcb28b5fab39890d580d439873fc640f8d528195ae99954af6454b777`.
- Release package doctor SHA-256:
  `e9ebbb7d8a6acfba89c20b9d634af89a9a2829b6e904d07d1f5140f491362799`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- The protected release-state compare-and-swap and artifact pointer promotion
  remain responsibilities of the external CI/release service.
- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements are not complete for this release identity.
- macOS host execution, iOS Xcode generation, signing, and device smoke are not
  complete. The shipped workflow is C#-only and contains no PowerShell product
  dependency.
- The CAT project has not completed its own full-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gate.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before adopting this release, pin the 0.1.23 package ID
`c8b496c6a126d80997b75c39f9e0c111c5d02f844c6f3234909884e1360027ba`.
After a release-ledger channel exists, stop new publication and continue serving
the last validated resource directory; do not discard or reinitialize the
protected ledger head. Reverting the build tool does not roll back an already
published resource release. A project leaving DHE must remove DHE runtime-plan
assets, restore its ordinary HybridCLR loading path, and rebuild the Base Player.
