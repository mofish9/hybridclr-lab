# HybridCLR DHE opt4 toolchain 0.1.33 release report

## Status

Toolchain 0.1.33 is the current conditionally accepted DHE Release line. The
immutable C# package is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.33-opt4.25`. Its Package ID is
`1293dc857990633be5ac13d735d0e9012cc06c10923ddaf9cd8b247b6f63d3b7`.

The package records `mode=Release`, `releaseReady=true`, 105 authenticated
files, and no PowerShell, batch, command, or shell launcher. Its exact identity
is:

- source commit: `bfa74fddc86a61c220aa743b1348231380c8592b`;
- source tree: `d550479fa2ec318ae9c22abc6f14904ffd2efa11`;
- package manifest SHA-256:
  `e7a78da0483cc98a5119b9209ef01d86f075b680904318a1606a6e7f39f8ff34`.

External package-source compilation completed with zero warnings and zero
errors. Package verification, doctor, and schema gates passed.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE tool source | `optimize/dhe-android-device-v8.13.0` | `bfa74fddc86a61c220aa743b1348231380c8592b` | none |

No runtime or Unity package source changed in this release. Existing runtime
tags remain immutable, and `hybridclr_unity` remains a maintenance branch
without a package opt tag.

## Archive correction

The portable Base archive now preserves two distinct native-manifest roles.
The archived workflow points to the normalized portable manifest, while the
build identity points to the immutable original manifest. The normalized
manifest binds the original through `sourceManifestSha256`. The previous
implementation incorrectly rewrote both references to the original manifest,
which made independently relocated archive revalidation fail.

The clean source regression passed 153/153 checks and directly exercises
`BindArchivedNativeIdentity`:

- regression report SHA-256:
  `9c72377b25228422a264c348610183ac525d2db08ac5e8bd9341fc0ff4f5c234`;
- release evidence SHA-256:
  `c6420993b4a7aebc6a1f0291b63e9c42bac1eb0513534cc0562f67f1ce647c80`.

## Revision 6 Base onboarding

Three real Editors built and started new revision 6
`StandaloneWindows64` Base Players. Each workflow was archived and then
revalidated from its portable archive with the package above.

| Engine workflow | Base ID | AOT metadata policy |
|---|---|---|
| Unity 2021 Standard | `5c431115876f2c1ef7529e862e6dd326816af8976cc0dc705760d6d3524caa4e` | independent supplemental set |
| Unity 2022 FGS | `83fcdfb9480859af45fe751503f0d3cece583775218e08b3b1c0335b6e202bf0` | authenticated empty set |
| Tuanjie 2022 FGS | `79d8bacfa650b7536c1a2c03f779792e348420258e3c528842201ea366678b8d` | authenticated empty set |

All three workflows report `releaseReady=true` and
`offlineReleaseRevalidated=true`. Their managed assembly set and Base
MetaVersion set agree across the three engines, and their project worktrees
were clean at qualification time.

## Revision 7 thirteen-Base release

One config-driven `resource-release-build` advanced the authenticated Base
registry from revision 3 to 4, retained the ten existing Bases, and added the
three revision 6 Bases. It emitted one resource release containing both
`windows` and `android` managed payload variants. The important identities are:

- release channel: `dhe-builder-six-base-e2e`;
- release revision: `7`;
- registry SHA-256:
  `a5179eab94d83abbbe6e3c2c64d615604615cd0834e3a46d901882213203fe58`;
- parent registry SHA-256:
  `4dd2a9b969609f85c94c2faa480c4afebf4f4194b862a46e6817423cc7df24d4`;
- ledger SHA-256:
  `b97ca6b9f1437cb45c9b119a6c2e0ad53e615e22e2810a0c3901ff95b9ca782d`;
- parent ledger SHA-256:
  `24f6f3a81af3437f53478a8452f77663ca6f54621c3970b37c24255c52f3318f`;
- payload variant-set SHA-256:
  `487f5773e95595457a0fc7ffa484c734251ae50d119af9fba03fba313005a8ac`.

All 13 resource stages passed without changing a Base Player binary. All 13
Windows Players then started as distinct processes, exited successfully, and
produced passing resource Player workflow evidence. The three new Bases each
reported exactly two changed methods, `changedMethod=interpreter`,
`unchangedMethod=aot`, three interpreter probe entries, and 60 AOT entries.
The older Bases reported 3 or 29 changed methods according to their own
immutable Base MetaVersion, exactly matching their expected counts.

The aggregate gate has SHA-256
`d576a3992f32734ae304ab8cfa2472e1b98628ec2e42d1c145196cda81c7b6a3`.
It records `activeBaseCount=13`, `exactActiveBaseCoverage=true`,
`engineMatrixCovered=true`, and no errors or warnings. Historical Base reports
were accepted only through the four explicitly authenticated package roots
actually referenced by the active registry.

CAS promotion advanced the protected channel from revision 6 head
`353fcfbb7b173e1da6460cf1d141e5615ea786f71beaa9c2b5618db7609562ef`
to revision 7 head
`56b694ea8f39f623dfba10a4a248423bb73b579a35bf3d5983354e84cd40fbb4`.
The promoted snapshot SHA-256 is
`bf65e50c104faa13b11db7adcde3e99c3fbb16d505c1cb6bd99457a8d4236e24`.
The next legal release revision is 8.

## Remaining gates

- The `android` payload selections above are cross-target managed payload
  validation in Windows Players. Android ARM64 still lacks device correctness,
  PSS/RSS, thermal, weak-core, and tail-latency evidence.
- iOS/macOS still lacks Xcode generation/link/signing, IPA, device, and
  performance evidence. The existing iOS gate covers export structure only.
- CAT still requires its own assembly registry, Base archives, resource catalog
  staging, one Player/device run per active Base, and production performance
  and memory qualification.
- Unsupported ABI, native layout, vtable, GC, P/Invoke, and declaration changes
  continue to fail closed and require a new Base Player.

This release is therefore conditionally accepted for the proven three-engine
Windows workflow. It is not Android, iOS, or CAT production evidence.

## Rollback

For tool rollback, pin `HybridCLRDhe-0.1.32-opt4.24`, Package ID
`8cbaad0764f4c265d05bfe61527677714ab6d2b78c62295a5684ef1ed563faa9`.
Do not rewrite the promoted revision 7 channel head. Application rollback is a
new forward resource revision derived from the actual protected head. The
revision 6 and revision 7 heads and content-addressed artifacts remain
immutable.
