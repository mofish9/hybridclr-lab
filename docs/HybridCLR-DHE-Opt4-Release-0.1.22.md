# HybridCLR DHE opt4 toolchain 0.1.22 release report

## Status

This release is conditionally accepted for C# source distribution, the existing
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` Player workflow,
and the three-engine native compile/CTest matrix. It adds the release-ledger
workflow and proves a four-to-five-Base registry transition. It does not claim
Android or iOS device readiness, CAT project readiness, or production performance
and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.22-opt4.7`. Its immutable
package ID is
`78299b3850296103114f866c46b681fcdcc8ef771a430e58a6a92e002e68887f`.
The manifest records `mode=Release`, `releaseReady=true`, 87 authenticated files,
and clean tracked source commit
`085a0a92e4ec6757d211b76e8f79eb471f15f667` with tree
`41991692215a28422c67da26e886c33e49c5ca44`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `22fb364b2e87e74602c903fd155731abd6899270` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `085a0a92e4ec6757d211b76e8f79eb471f15f667` | none |

No runtime, IL2CPP hook, or Unity package source changed in this release, so no
new runtime tag or `hybridclr_unity` tag is created. This report commit follows
the toolchain source commit and does not replace the source identity embedded in
the released package.

## Release ledger proof

The one-time channel genesis uses Base registry revision 3 with four active
Bases. Its registry SHA-256 is
`3c56e07460bfc528409029c73a8a560bc08e5c384e75676a3dd5f8f8b1bcc792`;
its ledger revision 1 SHA-256 is
`47bf52c6e9db1441df957bd27c0e38d949cb0c776c5060e6a542fb8404c63770`.

The next release passes that exact ledger hash from protected release state and
advances to registry revision 4. The new registry has five active Bases, SHA-256
`28bb0d3fd42937f7a12db2514f10e73778a5b2f77121a8c9b81d61ce64042e36`,
and names revision 3 as its direct parent. Ledger revision 2 has SHA-256
`590b2d81df243420417c835adef26ce4ca62931fefadff61fb0fc08594d77df5`
and names the revision 1 ledger as its direct parent. Its current assembly set
`c024966bae3a0eaedb80cd44136b1c04b38a8072e3645977523ba6624768e5c2`
was accepted by all five Bases.

Both releases were staged consecutively into the same archived Unity 2021 Base.
The selected Base ID and AOT metadata set stayed identical, and the embedded Base
MetaVersion tree was byte-identical before and after both stages. Reset registry,
stale ledger head, missing ledger fields, inconsistent Release mode, ledger
tamper, and release-evidence identity tamper cases were rejected.

Initialization remains a privileged migration boundary: the release owner must
audit every live Base before genesis, and protected CI must prohibit another
initialization after a channel head exists. A local tool cannot infer global
publication history.

## Player and matrix evidence

The current host regenerated ledger-aware stage/evidence reports around the five
existing Windows Player smokes. They all consume the same five-Base registry and
current assembly set
`47d4bcd5686b699133438d1ff4beb98ceebff7bd3e0b84b940aa687849b0e963`:

| Engine workflow | Base ID | Changed methods | Interpreter/AOT entries |
|---|---|---:|---:|
| `Unity2021Standard` | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | 27 | 10/37 |
| `Unity2022Fgs` | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | 27 | 10/37 |
| `Tuanjie2022Fgs` | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | 27 | 10/37 |
| `Unity2022Fgs` historical Base | `adfc4294ad4912dda9379b1556921dcd0a827f8b39e33ab2ddebea82ba3097ae` | 27 | 10/37 |
| `Unity2022Fgs` new Base | `e0d1682388719a48c8840d0f5e7b0513b45ddf7cf8b2898af46d1c4093024e34` | 29 | 10/37 |

These are revalidated historical Player executions, not a claim that ledger
revision 2 was executed on a new device run. The independent no-op Player still
records zero changed methods, zero interpreter entries, and 37 AOT entries.

The final clean regression passed 117/117 checks with `sourceClean=true` at the
source identity above. Release evidence binds 13 reports: one regression, one
no-op Player, three real-header native gates, three real-Editor resolver gates,
and five changed Players. Every native gate retains `mergeReady=true` and
`surrogateExternalHeadersUsed=false`. The Release package passed package-ID,
schema, doctor, and `verify-package -RequireRelease true` gates.

## Evidence

- Regression: `artifacts/release-ledger-candidate/regression-085a0a9-clean.json`.
- Release evidence: `artifacts/release-ledger-candidate/release-evidence-085a0a9/dhe-toolchain-release-evidence.json`.
- Four-to-five-Base releases: `artifacts/release-ledger-candidate/release-r1-four-base` and `release-r2-five-base-growth`.
- Ledger-aware Player reports: `artifacts/release-ledger-candidate/five-base-evidence-085a0a9`.
- Release package: `releases/HybridCLRDhe-0.1.22-opt4.7`.

These paths are workspace evidence locations, not portable package inputs. The
release-evidence document and package manifest contain the authoritative hashes.

## Remaining gates

- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements are not complete for this release identity.
- macOS host execution, iOS Xcode generation, signing, and device smoke are not
  complete. The shipped workflow remains C#-only and contains no PowerShell
  product dependency.
- The CAT project has not completed its own full-assembly preflight, Base
  bootstrap, resource catalog integration, device smoke, or performance gate.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before channel adoption, pin the 0.1.21 package ID
`f17795a3f9105cae2112fce220e187f389638170f45cee3e700ed59782483e0c`.
After a ledger channel exists, stop publication and continue serving the last
validated resource directory; do not publish a production continuation with
0.1.21 and do not discard or reinitialize the protected ledger head. A project
leaving DHE must remove DHE runtime-plan assets, restore its ordinary HybridCLR
loading path, and rebuild the Base Player.
