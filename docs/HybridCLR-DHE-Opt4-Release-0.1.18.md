# HybridCLR DHE opt4 toolchain 0.1.18 release report

## Status

This release is conditionally accepted for source distribution, the Unity 2021
Windows reference Player, and the three-engine native compile/CTest matrix. It
does not claim Android or iOS device readiness, Unity 2022 or Tuanjie Player
correctness, or production performance and memory results.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.18-opt4.1`. Its immutable
package ID is
`494685a553b71c1f187f88e89554c8f606d3cbcc23cac9f53534cff448bd0a2e`.
The manifest records `mode=Release`, `releaseReady=true`, 80 authenticated
files, and clean tracked source commit
`9268601f47c1a4125a60f16dd29293b66af9fdd6` with tree
`ba5fdda58bfd1165da5bbb5378c38e2a6980fb36`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `f777ed77eafd3eaa7262115c0c135f7418f83b74` | `v8.13.0-opt4.1` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `71a2e7b11e46119c649437d5afec02f2f2b82197` | none by policy |
| DHE toolchain source | `optimize/dhe-opt4-v8.13.0` | `9268601f47c1a4125a60f16dd29293b66af9fdd6` | none |

All runtime branches and annotated tags resolve remotely to the commits above.
The package branch was pushed before this report. The report commit is expected
to follow the toolchain source commit and does not replace the source identity
embedded in the released package.

## Multi-Base Player evidence

One current assembly set,
`808f854c3e2171fe2dd932aa7dd8fff4999faccc98c6698aa1cb26143e46f318`,
was generated once and validated against two independently built Unity 2021
StandaloneWindows64 Base Players:

| Base ID | Changed methods | Direct interpreter entries | Direct AOT entries | Result |
|---|---:|---:|---:|---|
| `9ef5603f40f3f4763c19350faf7892d84ddea77fa9188b34d2bfdfc432bbc45a` | 72 | 32 | 61 | passed |
| `871612c4c09b5176707dd2d6cb8517ed703e17445b3100543a7bf7f8ffbd2f63` | 72 | 32 | 61 | passed |

Both Players selected their own embedded Base MetaVersion, loaded the same four
current DHE assemblies, preserved the Player executable and GameAssembly,
validated structural and reflection behavior, retained AOT execution, and
passed atomic multi-assembly registration rollback and retry. Each Base also
passed its independent no-op AOT Player, release, archive, and schema gates.

## Evidence identities

| Evidence | SHA-256 |
|---|---|
| Release package manifest | `b0d00726303f54b8ac811e4252658e8d6e886a0040f2f214992b19aad6e7f9b2` |
| Seven-role release evidence | `dfa25b44adbbc29571c8d312040fe68185a48dfdc2135aa5bb63a438d42b73d6` |
| Formal regression, 60 checks and three real workflow outputs | `8268dd7e2e16ec927c417f8d0934e5c3fa7f92e5008af6fb3e3800c26a627da9` |
| Changed Base 1 workflow | `41f0667b2470e69d40ae34d769aa87d46d2852dbd7a7a1109674df7cc866b50c` |
| Changed Base 2 workflow | `497f363315d061642befad53f7f4ca3135db892df642e7e9782182d56d3a39b6` |
| No-op Base workflow | `6427db88982ca7298af298b67732e7a8356c92c2211ccecb8f14052c9c49e003` |
| Unity 2021 native gate | `bf085e5414a4865fc932065e23f14823f7433858c21ea480499edc0bd6daf47e` |
| Unity 2022 native gate | `b3cfeb223ee09eb5b6aec89b12c1fb31775a3ee4f97224e7a38076d7949caff7` |
| Tuanjie 2022 native gate | `eda3a967794e83d8dd7248daa171e6f5f4e6bbc36d262282b90d632171e923f8` |
| Source-host package verification | `21dcd57408a674f21fc48ca088c12b4d033427d9f01e1d17a27c83309ce177a2` |
| Package-host package verification | `0cd79c5be937ada018b16402d2e68133c23ba9eb44c092c1ac6568f032e183a6` |

The native gates use Release configuration and real Editor headers with
`mergeReady=true` and `surrogateHeadersAllowed=false`. Source-host and
package-host `verify-package -RequireRelease` both passed; the package-host
also validated the package manifest, batch report, and formal regression
against the schemas distributed in the package.

## Remaining gates

- Build and execute equivalent Base/no-op/changed Player lanes under Unity 2022
  and Tuanjie 2022 rather than extrapolating from native compilation.
- Run Android ARM64 Player/device correctness, PSS/RSS, tail latency,
  temperature, and weak-core measurements.
- Run the same C# workflow on macOS, then complete iOS Xcode, signing, and
  device smoke. Windows evidence is not iOS evidence.
- Measure DHE steady-state and first-touch performance before removing the
  project's BattleAOT fallback.
- Keep unsupported value-type layout, inheritance/interface/vtable, virtual
  declaration, and unsupported field-storage changes fail-closed.

## Rollback

Projects can disable DHE and restore the previous HybridCLR package/runtime
selection without changing an already released Base Player. A resource update
is rolled back by restoring the previous signed resource-catalog pointer; Base
MetaVersion files, Player binaries, and GameAssembly are immutable and are not
rewritten by resource staging. The exact source rollback boundary is toolchain
commit `9268601`, package commit `71a2e7b`, HybridCLR commit `f777ed7`, and the
three il2cpp_plus commits listed above.
