# HybridCLR DHE opt4 toolchain 0.1.21 release report

## Status

This release is conditionally accepted for source distribution, the Unity 2021,
Unity 2022, and Tuanjie 2022 `StandaloneWindows64` Player workflow, and the
three-engine native compile/CTest matrix. It does not claim Android or iOS device
readiness, CAT project readiness, or production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.21-opt4.6`. Its immutable
package ID is
`f17795a3f9105cae2112fce220e187f389638170f45cee3e700ed59782483e0c`.
The manifest records `mode=Release`, `releaseReady=true`, 84 authenticated
files, and clean tracked source commit
`62db9b599b04a24088616941c3cdf88663cc3a19` with tree
`633dbfdfdd74ddcdc8521fc9a6b6fd0032067c1c`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `62db9b599b04a24088616941c3cdf88663cc3a19` | none |

No runtime or package source changed in this release, so no new runtime tag or
`hybridclr_unity` tag is created. This report commit follows the toolchain source
commit and does not replace the source identity embedded in the released package.

## Five-Base resource proof

Registry `dhe-three-engine-bases` revision 4 has SHA-256
`28bb0d3fd42937f7a12db2514f10e73778a5b2f77121a8c9b81d61ce64042e36`,
binds parent registry SHA-256
`3c56e07460bfc528409029c73a8a560bc08e5c384e75676a3dd5f8f8b1bcc792`,
and contains five active and zero retired Bases. One four-assembly current set,
`47d4bcd5686b699133438d1ff4beb98ceebff7bd3e0b84b940aa687849b0e963`,
was generated once and accepted by all five Bases:

| Engine workflow | Base ID | Changed methods | Interpreter/AOT entries |
|---|---|---:|---:|
| `Unity2021Standard` | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | 27 | 10/37 |
| `Unity2022Fgs` | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | 27 | 10/37 |
| `Tuanjie2022Fgs` | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | 27 | 10/37 |
| `Unity2022Fgs` historical Base | `adfc4294ad4912dda9379b1556921dcd0a827f8b39e33ab2ddebea82ba3097ae` | 27 | 10/37 |
| `Unity2022Fgs` new Base | `e0d1682388719a48c8840d0f5e7b0513b45ddf7cf8b2898af46d1c4093024e34` | 29 | 10/37 |

All changed Players passed dispatch, multi-assembly, capability, transaction
rollback, and same-process retry checks. The independent no-op Player for the
new Base recorded zero changed methods, zero interpreter entries, and 37 AOT
entries.

The registry lifecycle retains every online Base by default, requires an exact
Base ID and reason for retirement, prohibits reactivation and in-place parent
overwrite, and requires an exact parent registry for revision 2 and later.
Staging archives and revalidates the current and parent registries. The regression
rejects parent, active-workflow, fabricated-retirement, payload, identity, and
runtime-plan tampering. Historical evidence is accepted only on the verified Git
chain from its authenticated Release authority through the evidence commit to
the current release commit.

The final clean regression passed 107/107 checks. Release evidence binds 13
authenticated reports: one regression, one no-op Player, three real-header native
gates, three real-Editor resolver gates, and five changed Players. Every native
gate has `mergeReady=true` and `surrogateExternalHeadersUsed=false`. The Release
package passed `verify-package -RequireRelease true` with the immutable package
ID above.

## Evidence

- Regression: `artifacts/multibase-lifecycle-a9dd/regression-62db9b5-five-base.json`.
- Release evidence: `artifacts/multibase-lifecycle-a9dd/release-evidence-62db9b5/dhe-toolchain-release-evidence.json`.
- Registry: `artifacts/multibase-lifecycle-a9dd/registry/supported-bases-r4-five-base.json`.
- Resource release: `artifacts/multibase-lifecycle-a9dd/resource-update-five-base-r4`.
- New Base archive and Player: `artifacts/multibase-lifecycle-a9dd/u22-base2`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements are not complete for this release identity.
- macOS host execution, iOS Xcode generation, signing, and device smoke are not
  complete.
- The CAT project has not yet completed its own full-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gate.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Pin package `0.1.20` ID
`3982eeb07d7204171fe5d2aa4b5435942e421bdbe8fe2e0e48c152fddda2183d`
and restore the previous registry/resource release. A project leaving DHE must
remove DHE runtime-plan assets, restore its ordinary HybridCLR loading path, and
rebuild the Base Player; it must not mix a DHE payload with a non-DHE Player.
