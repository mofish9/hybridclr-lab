# HybridCLR DHE opt4 toolchain 0.1.31 release report

## Status

This release fixes a multi-Base qualification defect found after the 0.1.30
release. When the current managed payload is byte-equivalent to one selected
Base, that Player correctly reports zero changed methods and zero interpreter
entries while retaining AOT execution. The previous host rejected this valid
result before evaluating its complete no-op proof.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.31-opt4.16`. Its immutable
Package ID is
`5af090eca274468623a7b4e8bab3ceeb669f378f7293bfeb3d1498c69ba7d960`.
The manifest records `mode=Release`, `releaseReady=true`, 102 authenticated
files, 41 commands, and clean tracked source commit
`84acc00b33b6277239c4f52a8dab14e572900497` with tree
`78dbaf3e35c388529808ef3a8844e58b60298bf9`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE toolchain source | `optimize/dhe-multibase-noop-evidence-v8.13.0` | `84acc00b33b6277239c4f52a8dab14e572900497` | none |

No runtime or Unity package source changed. No runtime tag or package tag is
created in this release. The 0.1.30 Package ID was added to the authenticated
historical authority set so its Base evidence remains usable after the upgrade.

## No-op contract

`resource-player-evidence` now accepts `expectedChangedMethodCount=0` with no
interpreter-only assembly. This is not an empty-evidence bypass. The Player must
still prove all of the following:

- the selected Base, payload variant, registry, resource manifest, validation,
  runtime plan, ledger, build identity, and native manifest identities agree;
- the complete planned assembly set loaded and retained a positive AOT entry
  count with zero interpreter dispatch;
- dispatch, direct/reflection capability, and secondary-assembly probes pass;
- no changed or unchanged probe was falsely classified; and
- `transactionStatus=notApplicable` and `noOpAotBehaviorValidated=true`.

The mandatory `resource-player-selected-base-noop` regression accepts a complete
proof and rejects the same document when its no-op validation flag is false. The
existing interpreter-only no-op regression remains independent.

## Formal qualification

The clean regression passed 141/141 checks with `sourceClean=true`, six changed
Base reports, one no-op report, all 12 historical authority packages, and three
real Editor resolver reports. Release evidence binds the regression, six Player
reports, the no-op report, three resolver reports, and the unchanged three-engine
native reports.

The native/runtime repositories did not change. Their previously locked reports
remain historical evidence for the same runtime commits and report
`passed=true`, `mergeReady=true`, real Editor headers, no surrogate headers, and
CTest 1/1. They were not rerun on a new runtime identity in this tool-only release.

- Regression:
  `artifacts/dhe-selected-base-noop-formal-84acc00/regression-clean.json`
  (SHA-256 `4c6208687a08da532f3c190c3e8575dd2cdd4e8d7c7ffef0ce86dff5244c8f81`).
- Release evidence:
  `artifacts/dhe-selected-base-noop-formal-84acc00/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `cc49700822b9d49a049d36142886b5b976e3bec7951e24d99eac547ff47a724c`).
- Release manifest SHA-256:
  `fb8d3e44a3a857fe6bbe6d02bf82cc56d5467f6d7d82313dd274ef6a9c0f8a57`.
- Release verify, doctor, and schema-gate SHA-256 values:
  `bb6f01de107e175f87daa0e6627d69145d37bd088b51364ae3eb732f3ea05568`,
  `91416ed512d385100ef180d5e7dd65d50c2c1fe85e5ec2403c8cb3166fe305ed`,
  and `857bf746ac69e9b1cfb108f4e5d385d20691ed1e50cfffce924154aa0c95bafe`.

The released source was also compiled with its intermediate and output trees
outside the immutable package. Compilation completed with zero warnings and zero
errors, and package verification passed again afterward.

## Six-Base continuation

The released 0.1.31 host regenerated all six Player evidence reports for a real
second resource revision without rebuilding a Base Player. The same registry
revision 1 selected `windows` and `android` payload variants across Unity 2021,
Unity 2022, and Tuanjie 2022.

| Engine workflow | Base label | Variant | Changed methods | Interpreter/AOT entries |
|---|---|---|---:|---:|
| `Unity2021Standard` | `u21` | `windows` | 27 | 10/37 |
| `Unity2021Standard` | `u21-new` | `android` | 0 | 0/60 |
| `Unity2022Fgs` | `u22` | `android` | 27 | 10/37 |
| `Unity2022Fgs` | `u22-base2` | `windows` | 29 | 10/37 |
| `Unity2022Fgs` | `u22-legacy` | `android` | 27 | 10/37 |
| `Tuanjie2022Fgs` | `tuanjie` | `windows` | 27 | 10/37 |

The `u21-new` result has `noOpAotBehaviorValidated=true`; the other five prove
changed interpreter dispatch and retained AOT entries. All six reports passed
the aggregate gate with exact Base coverage and the required engine matrix.

Release revision 2 is bound to:

- resource manifest SHA-256
  `2c62ee3224fe75bb27e362656f1554e2eef19fc6d16ffc161f0fed390328633c`;
- validation SHA-256
  `29569a7fd3692b5284bc1510e6c8762d29c5a6fb965125469349c5dad8b785ed`;
- release ledger SHA-256
  `8442ea2f727e00551286cf323b9e7d346fe449f5630071d273daaefe20352ef2`;
- parent ledger SHA-256
  `9a4c87fdba035d90fc58902de2f869dff2d552e10f3a293827956595c60ba242`;
- Base registry SHA-256
  `df2fdde424cd2142f3922564344c30a4e16b84ffa0f12ed5b8d4c3582612589f`;
- current assembly-set SHA-256
  `47d4bcd5686b699133438d1ff4beb98ceebff7bd3e0b84b940aa687849b0e963`.

The state-bound aggregate gate is
`artifacts/dhe-builder-six-base-player-e2e-20260906-gate-r2-0.1.31/resource-release-gate.json`
(SHA-256 `dbe4293c374cd18f00e08b2315db8307c5494106b9a15f94c214d9e6632bd822`).
Promotion revalidated the gate and advanced the isolated lab channel from
revision 1 to revision 2. The resulting snapshot is
`artifacts/dhe-builder-six-base-player-e2e-20260906-promotion-r2-0.1.31/promoted-snapshot.json`
(SHA-256 `a1d8ffa79a2ef14885b0b90992bac64429776585807bfae869d37701dcc77128`),
and the new channel-head SHA-256 is
`edbee27aed5829640cb982a92a02a7b585bfb292671cf7115d75b50713f3480f`.
This is an isolated lab channel, not CAT production content.

After release, the same package also appended a seventh archived Tuanjie Base
through a direct-successor registry and qualified one revision 3 resource build
on all seven old and new Windows Players. See
`docs/HybridCLR-DHE-Opt4-Post-Release-Seven-Base-Onboarding.md`. This additive
evidence does not change the 0.1.31 package identity.

## Remaining gates

- Android ARM64 and iOS device correctness, memory, tail latency, temperature,
  and weak-core evidence remain incomplete. Windows execution is not substituted
  for either platform.
- macOS filesystem behavior and iOS/Xcode generation, linking, signing, and
  device execution remain untested. The product path contains only C#; this does
  not replace platform validation.
- CAT still needs its own Base bootstrap, all-hotfix preflight, resource catalog
  staging, one Player/device smoke per active Base, and performance gates.
- Existing unsupported value-type layout, inheritance/interface/vtable, ABI,
  GC, and unsupported field-storage changes remain fail-closed and require a new
  Base Player.

## Rollback

Before adopting 0.1.31, pin the 0.1.30 Package ID
`aa7aef3ec649ad0cca34122bc357a7779d40d6262b45920609ebb222392d8e16`.
After a resource head is promoted, rollback is a forward release from the actual
protected head; never rewrite a package, registry revision, or channel ledger.
