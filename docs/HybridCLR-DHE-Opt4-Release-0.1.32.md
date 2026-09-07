# HybridCLR DHE opt4 toolchain 0.1.32 release report

## Status

Toolchain 0.1.32 is the current Release line for the DHE workflow. The
immutable C# package is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.32-opt4.24`. Its Package ID is
`8cbaad0764f4c265d05bfe61527677714ab6d2b78c62295a5684ef1ed563faa9`.

The package records `mode=Release`, `releaseReady=true`, 105 authenticated
files, and the following source identity:

- source commit: `34b26c445005e911bb5a289d43a492f5d6ca8192`;
- source tree: `c819206a0aca61ec7e1d07b18e7421a700425c1f`;
- package manifest SHA-256: `6e69367d117fed8c56bab62023f96282118b282d2f0b759b23ce17ccc0ed2907`.

The package contains no PowerShell, batch, command, or shell launcher. The
workflow host and project adapters are C# and can be invoked by the Unity
Editor on Windows, macOS, and the corresponding CI agents.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE tool source | `optimize/dhe-android-device-v8.13.0` | `34b26c445005e911bb5a289d43a492f5d6ca8192` | none |

No runtime or Unity package source changed in this release. Runtime tags are
immutable and the Unity package remains a maintenance branch without a package
opt tag.

## Formal qualification

The clean source regression passed 153/153 checks. It covers the MetaVersion
compatibility contract, consecutive current derivation, archive and provenance
binding, three-engine resolver/native matrix, resource release gate, protected
channel CAS behavior, Android overlay contracts, and the target-independent iOS
Xcode export structure gate. The added check resolves the aggregate release
authority from the authenticated package set when the selected Base evidence is
a portable archive whose embedded package path is only a placeholder.

- Regression report SHA-256:
  `59b8058feea0327de5624e9c9859d8df68318c7720fcaa2caa265b5d273717e2`.
- Release evidence SHA-256:
  `25631727e3d9b54bdf82ffc32c0543fbf8b74f726db6d5ce25f30873a0c452ed`.
- Schema, package verification, and doctor gates passed for the exact package
  identity above.

The Windows qualification set includes ten changed Base Player reports, one
no-op report, and the Unity 2021, Unity 2022, and Tuanjie 2022 resolver/native
roles. All managed reports retain both AOT and interpreter dispatch where the
payload changes methods; the no-op report proves positive AOT dispatch with no
interpreter entries.

## Post-release revision 6

Using the immutable predecessor package `HybridCLRDhe-0.1.32-opt4.23`, a new
`current-next` payload was derived from the authenticated revision 5 current set
without rebuilding any Base Player. Package `opt4.24` revalidated this complete
result during its 153-check release regression. The
revision 6 resource update selected `windows` and `android` payload variants,
retained all ten active Bases, and passed compatibility and schema validation.

- resource update root:
  `C:/hybridclr_optimize/artifacts/resume-release6-resource-20260907`;
- resource revision: `6`;
- parent ledger SHA-256:
  `3281696217b977644c423402d52c6d4b151f939315c9716f6deefddf7fbd926d`;
- revision 6 ledger SHA-256:
  `24f6f3a81af3437f53478a8452f77663ca6f54621c3970b37c24255c52f3318f`;
- current assembly-set SHA-256:
  `6f4cae92afa11386dd118e10cdca43d4e93e0442c3882c202212434a42413ea7`;
- payload variant-set SHA-256:
  `e3fd59f17d7b00b8c33c2191dc2491a93d1db9108a9246f5e1f6132582122d7c`;
- aggregate gate SHA-256:
  `e49293f7263a075d158bcf7fa1df77fc94dbb477709110f5a11f15d290d47998`.

All ten Windows Base Players started and generated a passing
`resource-player-workflow-report.json`. The protected lab channel advanced to
revision 6 by CAS promotion; the promoted snapshot SHA-256 is
`2b4ebfb2d2be6f016c1e9bc20c9680e5ea3cc9251d173c379917ad1a84821dc5`, and the
new channel head SHA-256 is
`353fcfbb7b173e1da6460cf1d141e5615ea786f71beaa9c2b5618db7609562ef`.
This proves that one subsequent hotfix payload can serve multiple Base
generations and all three engine workflows without rebuilding the old Players.

## Remaining gates

- Android ARM64 still lacks device correctness, PSS/RSS, thermal, weak-core,
  and tail-latency evidence.
- iOS/macOS lacks Xcode generation/link/signing, IPA, device, and performance
  evidence. The current iOS check is export-structure integrity only.
- CAT still requires its own all-hotfix assembly registry, Base archives,
  catalog staging, one Player/device run per active Base, and production
  performance/memory gates.
- Value-type layout, inheritance/interface/vtable, unsupported ABI, GC, P/Invoke,
  and other unsupported declaration changes remain fail-closed and require a new
  Base Player.

Windows results must not be presented as Android or iOS production evidence.

## Rollback

For tool rollback, pin the immutable predecessor
`HybridCLRDhe-0.1.32-opt4.23`, Package ID
`d22b37ed90d5e25d164ed046dfff4d63c4ef74120b6012b05dbe0c8982db8528`.
Do not rewrite an already promoted channel head. Application rollback is a new
forward resource revision derived from the actual protected head; the revision
6 state and its content-addressed artifact remain immutable.

Earlier 0.1.32 package directories are retained immutably for audit and existing
pins. New project installs must select `opt4.24` by its exact Package ID; do not
select a package by directory name or toolchain version alone.
