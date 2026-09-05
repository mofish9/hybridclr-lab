# HybridCLR DHE opt4 toolchain 0.1.23 release report

## Status

This release is conditionally accepted for C# source distribution, the locked
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` evidence lane,
and the existing three-engine native compile/CTest matrix. It closes the gap
between a consecutive resource release and its Player matrix: every active Base
must execute the exact candidate release head. It does not claim Android or iOS
device readiness, CAT project readiness, or production performance and memory
results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.23-opt4.8`. Its immutable
package ID is
`c8b496c6a126d80997b75c39f9e0c111c5d02f844c6f3234909884e1360027ba`.
The manifest records `mode=Release`, `releaseReady=true`, 87 authenticated files,
and clean tracked source commit
`8c4e05b3e979607c17df0b6b8cc074f69c7c56b0` with tree
`8614bcb7679c372c9f2dcc3bfebb55762f9ebe08`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `8c4e05b3e979607c17df0b6b8cc074f69c7c56b0` | none |

No runtime, IL2CPP hook, or Unity package source changed in this release. No new
runtime tag or `hybridclr_unity` tag is created. This report commit follows the
toolchain source commit and does not replace the source identity embedded in the
released package.

## Consecutive release proof

The preceding channel head is release-ledger revision 1 with SHA-256
`47bf52c6e9db1441df957bd27c0e38d949cb0c776c5060e6a542fb8404c63770`.
The qualified candidate is its direct revision 2 successor:

- resource manifest SHA-256:
  `8607d7a4edca75a7cee7a3c46b99915d72fd10435589d2f91e5b6c6acf2be351`;
- release ledger SHA-256:
  `b35265530e68ea4067d7c056e07bffb8a01e43b5c46ff82fd0c0845c14e5e8dc`;
- active Base registry SHA-256:
  `28bb0d3fd42937f7a12db2514f10e73778a5b2f77121a8c9b81d61ce64042e36`;
- current assembly set SHA-256:
  `47d4bcd5686b699133438d1ff4beb98ceebff7bd3e0b84b940aa687849b0e963`;
- payload variant set SHA-256:
  `6663966b61c1946d2f571c3a8b77d68ff22e3e360db764b135cd728ff3767c6e`.

The regression records all of these fields in `validatedResourceRelease`,
including the parent ledger hash and active Base count. Release-evidence reads
the five Player reports again and compares the same fields. A four-report subset
was rejected because it omitted one active Base. Five reports from another
release channel/revision were rejected because they did not match the candidate
head.

The gate accepts an unchanged registry for ordinary consecutive hotfixes. If the
active Base set changes, it requires the registry's authenticated direct
successor and applies the existing explicit retirement rules. The regression no
longer assumes that every release must perform the demo's four-to-five-Base
transition.

## Player and matrix evidence

The same revision 2 resource directory was actually executed by five isolated
Windows Base Player copies:

| Engine workflow | Base ID | Changed methods | Interpreter/AOT entries |
|---|---|---:|---:|
| `Unity2021Standard` | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | 27 | 10/37 |
| `Unity2022Fgs` | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | 27 | 10/37 |
| `Tuanjie2022Fgs` | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | 27 | 10/37 |
| `Unity2022Fgs` historical Base | `adfc4294ad4912dda9379b1556921dcd0a827f8b39e33ab2ddebea82ba3097ae` | 27 | 10/37 |
| `Unity2022Fgs` new Base | `e0d1682388719a48c8840d0f5e7b0513b45ddf7cf8b2898af46d1c4093024e34` | 29 | 10/37 |

All five reports prove dispatch, multi-assembly behavior, capability checks, and
rollback/retry. The independent no-op Player records zero changed methods, zero
interpreter entries, and 37 AOT entries.

The Player executions and the native/Editor matrices use the unchanged locked
runtime and package identities. They are pre-existing real executions, not new
device runs produced by commit `8c4e05b`; the clean 0.1.23 host revalidates their
complete source, runtime, header, report, manifest, and ledger chains. The three
native reports have `mergeReady=true` and `surrogateHeadersAllowed=false`.

The final clean regression passed 118/118 checks with `sourceClean=true`.
Release evidence binds 13 reports: one regression, one no-op Player, three
real-header native gates, three real-Editor resolver gates, and five changed
Players. The Release package passed package-ID recomputation, schema, doctor,
and `verify-package -RequireRelease true` gates. It contains no PowerShell files.

## Evidence

- Regression: `artifacts/dhe-consecutive-player-8c4e05b/regression-8c4e05b-clean.json`
  (SHA-256 `137e08c10a8f69d45e9cefec024d182649f4d062fae665a8f9831bd26239710f`).
- Release evidence:
  `artifacts/dhe-consecutive-player-8c4e05b/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `0fc2fdcf6937790639d473b120049c17129927c5464cf126a72397f30b4688b0`).
- Actual revision 2 Player matrix:
  `artifacts/release-ledger-r2-player-matrix/evidence`.
- Consecutive releases: `artifacts/release-ledger-candidate/release-r1-four-base`
  and `artifacts/release-ledger-r2-player-matrix/release-r2-five-base`.
- Release package: `releases/HybridCLRDhe-0.1.23-opt4.8`; manifest SHA-256
  `60bf6c682cc820c9dcc6b2917974e3623bc3b93cca0d67ae80ad348190176135`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

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

Before adopting this release, pin the 0.1.22 package ID
`78299b3850296103114f866c46b681fcdcc8ef771a430e58a6a92e002e68887f`.
After a release-ledger channel exists, stop new publication and continue serving
the last validated resource directory; do not discard or reinitialize the
protected ledger head. Reverting the build tool does not roll back an already
published resource release. A project leaving DHE must remove DHE runtime-plan
assets, restore its ordinary HybridCLR loading path, and rebuild the Base Player.
