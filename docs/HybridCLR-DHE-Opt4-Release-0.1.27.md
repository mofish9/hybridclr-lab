# HybridCLR DHE opt4 toolchain 0.1.27 release report

## Status

This release is conditionally accepted for C# source distribution and the locked
Unity 2021, Unity 2022, and Tuanjie 2022 `StandaloneWindows64` evidence lane. It
proves that one current payload can serve six online Base Players spanning
different application generations without rebuilding those Players. It does not
claim Android or iOS device readiness, macOS host execution, CAT project readiness,
or new production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.27-opt4.12`. Its immutable
package ID is
`3af3d63e986470de6556c6023c438374e030c240aea3c9f2b168c1a0837c76fc`.
The manifest records `mode=Release`, `releaseReady=true`, 97 authenticated files,
40 commands, and clean source commit
`c7059691a2cb7c645f5e1072e958bce448250169` with tree
`333c0a13820e513a565162637f2e852d4efd3d88`. The package contains no PowerShell,
batch, command, or shell files.

## Scope

No HybridCLR runtime, IL2CPP hook, or `hybridclr_unity` runtime/package source
changed. The runtime branches and tags remain those recorded by 0.1.26; no runtime
tag or `hybridclr_unity` tag is created for this release.

The managed Player validation no longer assumes that every later payload changes
one fixed set of demo methods or returns one fixed business generation. It selects
an actually changed probe, retains an unchanged AOT control, and checks direct and
reflection execution across all four DHE assemblies. No-op validation is also
generation-neutral.

The lab fixture now derives a Base-generation current set from an authenticated
`DHE_CURRENT` seed without recompiling unchanged assemblies. Three secondary DLLs
are copied byte-for-byte; the AOT fixture preserves assembly/module identity and
changes exactly two methods. This is a regression fixture, not a production IL
rewriter for project assemblies.

The production regression accepts an explicit immutable genesis resource and
registry independently from its current N-1 to N pair. This removes the previous
two-revision assumption and keeps genesis, continuation, stale-head, registry
growth, and CAS checks valid as a release channel ages.

## Six-Base result

Registry revision 5 retains the five previously published Bases and adds the new
Unity 2021 Base `723f38655642600358d75cd0a7bc4fc21767310d97e1fd87f13dd63d6f109f8b`.
The single revision 3 payload passed static compatibility for all six Bases:

| Base group | Changed methods | Interpreter entries | AOT entries |
|---|---:|---:|---:|
| Unity 2021, Unity 2022, Tuanjie, legacy Unity 2022 | 29 each | 10 each | 37 each |
| Unity 2022 Base2 | 27 | 10 | 37 |
| New Unity 2021 generation | 2 | 3 | 60 |

All six fresh Player copies reported `passed=true`, selected the same current
assembly set
`c3f2d09c049e027e4073a90e51d381c0b3b3136f1a68623ab104af191f27bc15`,
kept an unchanged probe on AOT, entered the interpreter for changed methods, and
passed transaction rollback/retry. Four historical Players were authorized by
package `7757d0...`, Base2 by `3982ee...`, and the new Base by 0.1.26 package
`b23759...`.

The 0.1.26 aggregate gate authenticated all three authority identities, exact
six-Base coverage, the three-engine matrix, registry SHA-256
`11d7187b07439c00ae03413a2d900e530545ebcaeeff5ea02f49a830579b8853`,
and candidate ledger
`57c0e3fd01fad152501fce3b57338d3d522f28571da32be9f41efb43851fec37`.
Protected CAS promotion advanced `dhe-demo-base-growth` from revision 2 to 3.
The promoted head SHA-256 is
`45ce45555bd13ab916a04356f04b2a9048ceecc568cd082b09442be074cb00cd`.

## Evidence

The final clean regression passed 122/122 checks with six changed Player reports,
one no-op report, all three real Editor resolver reports, and all eight historical
Release authority packages. Release evidence binds 14 reports: eight fixed roles
and six changed Players. Native and resolver runs are revalidated historical
evidence; the six revision 3 Player executions and channel promotion are current.

- Regression: `artifacts/dhe-base-generation-c705969/regression-clean.json`
  (SHA-256 `31db64c4c7e327c271c34341946ce985f4d91b1a45c05d336edff5c3b3bfd893`).
- Release evidence:
  `artifacts/dhe-base-generation-c705969/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `261597b3873e1f285f3c3381fc24f6daba4117e958748f36c1ba8a692c7667db`).
- State-bound six-Base gate:
  `artifacts/dhe-six-base-candidate/release-gates/r3-current-base2-preserved.json`
  (SHA-256 `fa53217151d6325d16ca9407357173cece16bdf98d8e0f480f292bfeec3248f8`).
- Release package manifest SHA-256:
  `8eaf10b75c07598a0a3ad3329ad520110d1e1b515a10900a3f6bc17ced278f1e`.
- Package verify, doctor, and schema gate all passed; their report SHA-256 values
  are `ff146d993ae367ef0a8bf10f5543078d7335ff2538366772e627ea74d8e7598d`,
  `3333e42daa3dea957aa99a24f3239894c3c46c41d8ef2ba506e6b86e7527c0f1`,
  and `97f3dc76aa766ce7a5ab186064e2d9917996034915441464c63a86d0c458f200`.

## Remaining gates

- Android ARM64 device correctness, PSS/RSS, tail latency, temperature, and
  weak-core measurements remain incomplete.
- iOS Xcode generation, signing, staging, and device smoke remain incomplete.
- macOS host execution and non-NTFS channel-state semantics remain untested.
- The CAT project has not completed full-assembly preflight, Base bootstrap,
  resource catalog integration, device smoke, or performance gates.
- Existing value-type layout changes, inheritance/interface/vtable changes,
  unsupported field storage, and unsupported virtual/abstract/PInvoke evolution
  remain fail-closed and require a new Base Player.

## Rollback

Before adopting 0.1.27, pin 0.1.26 package ID
`b23759fccd900ba6f5ff6f250d75676c3b539f9af400ae2644412678183a3cf9`.
After revision 3 has been promoted, never move or reinitialize the protected
channel head. Roll back application behavior by publishing a forward revision
from the actual head while retaining every still-supported Base and required
historical authority package.
