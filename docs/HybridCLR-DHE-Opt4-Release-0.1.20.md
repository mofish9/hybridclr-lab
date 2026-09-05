# HybridCLR DHE opt4 toolchain 0.1.20 release report

## Status

This release is conditionally accepted for source distribution, the Unity 2021,
Unity 2022, and Tuanjie 2022 `StandaloneWindows64` Player workflow, and the
three-engine native compile/CTest matrix. It does not claim Android or iOS device
readiness, CAT project readiness, or production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.20-opt4.5`. Its immutable
package ID is
`3982eeb07d7204171fe5d2aa4b5435942e421bdbe8fe2e0e48c152fddda2183d`.
The manifest records `mode=Release`, `releaseReady=true`, 84 authenticated
files, and clean tracked source commit
`87c86eb855d82b57125c7124610ce5689f37b932` with tree
`cab3d45f59c63b927cabfd3209612cd83bf6e03a`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `87c86eb855d82b57125c7124610ce5689f37b932` | none |

All runtime branches and annotated tags resolve remotely to the commits above.
The package and DHE source branches were pushed before this report. This report
commit follows the toolchain source commit and does not replace the source
identity embedded in the released package.

## Three-Base resource proof

One current assembly set,
`23ff59b7d30329ac5b1298e971fa5619b7f596ce78daf85288ceb15c64d858e6`,
was generated once and consumed by these immutable Base Players:

| Engine workflow | Base ID | IL2CPP code generation | AOT metadata |
|---|---|---|---|
| `Unity2021Standard` | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | `OptimizeSpeed` | supplemental, 4 assemblies |
| `Unity2022Fgs` | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | `OptimizeSize` | empty set |
| `Tuanjie2022Fgs` | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | `OptimizeSize` | empty set |

The shared payload contains four assemblies. Every Base computed 27 changed or
added methods, recorded 10 interpreter entries and 37 AOT entries, validated
direct/reflection and multi-assembly probes, and passed transaction rollback and
same-process retry. Staging preserved the embedded Base MetaVersion, Player
executable, and `GameAssembly.dll`. The independent no-op Player recorded zero
changed methods, zero interpreter entries, and 37 AOT entries.

The final production regression passed 96/96 checks. It bound four workflow
outputs, all three real-Editor resolver reports (11/11 each), and all three
real-header native gates (`mergeReady=true`, no surrogate headers). Release
evidence contains eight fixed roles plus three distinct changed Base roles.
Both the source host and the packaged host passed `verify-package
-RequireRelease` using the immutable package ID above.

## Evidence

- Regression: `artifacts/multibase-build-config-final-87c/dhe-regression.json`.
- Release evidence: `artifacts/multibase-build-config-final-87c/release-evidence/dhe-toolchain-release-evidence.json`.
- Registry: `artifacts/multibase-build-config-release-fd76/registry/supported-bases-r3.json`.
- Resource release: `artifacts/multibase-build-config-release-fd76/resource-update-r4`.
- Changed Player evidence: the `resource-player-u21-r2`, `resource-player-u22-r2`,
  and `resource-player-tuanjie-r2` directories beside that resource release.

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

Pin the previous authenticated package ID and restore the previous package
commit/runtime locks. A project leaving DHE must remove DHE runtime-plan assets,
restore its ordinary HybridCLR loading path, and rebuild the Base Player; it must
not mix a previous DHE payload with a non-DHE Player.
