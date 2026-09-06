# HybridCLR DHE opt4 toolchain 0.1.30 release report

## Status

This release supersedes 0.1.29 before project adoption. It retains the complete
config-driven Base-onboarding workflow and closes the historical-authority gap:
a Base built by either 0.1.28 or 0.1.29 remains admissible after upgrading the
publishing toolchain. The already-created 0.1.29 package is retained as immutable
history and is not rewritten.

The released C# toolchain is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.30-opt4.15`. Its immutable
Package ID is
`aa7aef3ec649ad0cca34122bc357a7779d40d6262b45920609ebb222392d8e16`.
The manifest records `mode=Release`, `releaseReady=true`, 102 authenticated
files, 41 commands, and clean tracked source commit
`610464ef0d31439f12bfcc6e18fd10e378cefc74` with tree
`b6ac07b91a8d29e967fc9c6eed6410ae573a29b2`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE toolchain source | `optimize/dhe-base-onboarding-v8.13.0` | `610464ef0d31439f12bfcc6e18fd10e378cefc74` | none |

No runtime or Unity package source changed. No new runtime tag or package tag is
created. The runtime/package locks remain those of 0.1.28.

## Authority correction

The explicit authority manifest now includes the immutable 0.1.28 and 0.1.29
Release identities in addition to the nine earlier authorities. Regression has
a mandatory `evidence-immediate-predecessor-authorized` check derived from the
current semantic version. Future releases therefore fail before publication if
the immediately preceding official package is omitted.

This matters independently of runtime compatibility. A package authenticates
itself only while it is the current externally pinned publisher. In its successor
it must be an explicitly authorized historical package, otherwise a still-online
Base built by that package cannot pass `resource-release-gate`.

## Workflow and evidence

`resource-release-build` remains the single pure-C# entry point. It can create an
initial registry, atomically add multiple Base archives through one direct
successor, or reuse a registry without increasing its revision. It binds named
current variants, a live SHA-256-pinned channel snapshot, resource manifest,
validation, runtime plan, and release ledger, then publishes the complete wrapper
only after exact Base coverage and schema gates pass.

The clean regression passed 140/140 checks with `sourceClean=true`, all 11
historical authority packages, six changed Base reports, one no-op report, and
three real Editor resolver reports. The protected Release test reproduced the
next channel revision through the new builder. The three native profiles were
rerun against the unchanged locked runtime and each reports `passed=true`,
`mergeReady=true`, real Editor headers, no surrogate headers, and CTest 1/1.

- Regression: `artifacts/dhe-base-onboarding-formal-610464e/regression-clean.json`
  (SHA-256 `f7cf40a3fc3284791b1c2b6f4e7fd326287c3357206d2616f083d22d1b644d6e`).
- Release evidence:
  `artifacts/dhe-base-onboarding-formal-610464e/release-evidence/dhe-toolchain-release-evidence.json`
  (SHA-256 `cb85d18a6ab96c1ba2a9573c21eb944c94be5914ad79486766ed45ba5a36bbe8`).
- Release manifest SHA-256:
  `c7fdff07c3dc91742d338fbd0856bd7d1084149c79533dad7bf1be901a7365ca`.
- Release verify, doctor, and schema-gate SHA-256 values:
  `93e1999474179130edd8e9a09527097dbfe6d6e650a5eb17449f05a075b523f7`,
  `102b98571257a54105cdba3181280afe4e25c768006249116aa156d5ab8129b4`,
  and `bcc3878585cbd23503f94dfbe9a9bbdc3ccdd845ab1190a7c1711bec89804081`.

## Remaining gates

- Android ARM64 and iOS device correctness, memory, and tail-latency evidence
  remain incomplete. Windows evidence is not substituted for either platform.
- macOS host and iOS/Xcode execution remain untested, although the product path
  contains only C# and no PowerShell, batch, command, or shell files.
- CAT still needs its own Base bootstrap, all-hotfix preflight, resource catalog
  staging, per-Base Player/device smoke, and performance gates.
- Existing unsupported value-type layout, inheritance/interface/vtable, ABI,
  GC, and unsupported field-storage changes remain fail-closed and require a new
  Base Player.

## Post-release additive evidence

The released 0.1.30 package subsequently requalified the same resource payload
on six distinct Windows Base Players across all three engine workflows. See
`docs/HybridCLR-DHE-Opt4-Post-Release-Multi-Base-Player-Rerun.md`. This additive
rerun does not alter the package identity or replace the original release
evidence, and it does not stand in for Android or iOS device gates.

## Rollback

Before adoption, pin the 0.1.28 Package ID
`b054796c7d97f8c6a7399e1d8b25fdb70fff664901f93248d5ab1afed32164fb`
and use the explicit `base-registry` plus `resource-update` workflow. After a
resource head is promoted, rollback is a forward release from the actual protected
head; never rewrite a package, registry revision, or channel ledger.
