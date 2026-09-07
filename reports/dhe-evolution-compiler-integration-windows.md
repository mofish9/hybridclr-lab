# DHE package compiler integration: Windows checkpoint

## Result and scope

Normal package-owned C# Base workflows now include the exception-correct AOT
compiler transaction. The frozen three-engine cold replay passes 220/220 cases
in each of nine actual Windows processes, with zero differences and no pre-touch.
This is candidate correctness evidence, not complete managed evolution, a
performance result, or Android/iOS qualification. No formal source branch,
runtime tag, Installer selection, published toolchain, channel or CAT changed.

## Frozen sources

| Component | Commit |
|---|---|
| HybridCLR | `a57999edeb67f20ef48832737a32662065226ca5` |
| Unity 2021 hooks | `3a0ce4d178033a9edd32b62988ff8a1bf0432150` |
| Unity 2022 hooks | `2cd3264524cf566a315f83222bebf6fdff31cc7a` |
| Tuanjie hooks | `00c3a97144111d8aca6029a6613c3192a9276f46` |
| Unity package | `2c04c2446a3ae9bd3c1c1f0e943271f97121c8a5` |
| Lab compiler integration and linked tests | `57af1e1` |
| Focused Unity 2021 replay | `503bbcb1ee4de66829f636d08355afc2ad28e8fb` |
| Three-engine replay | `51f71e1a9831569ef3446b56ad5d1ebc08b12e8d` |

The six source worktrees are clean. Native contract remains `dhe-runtime-v11`;
MV remains `DHEMETA1`, schema 1. Package tree SHA-256 is
`665FD0E7F5D0AB3DF03D4383672E225CC26961ACB58CC4C9F29896BEA951CF2E`.
The exploratory toolchain Package ID is
`54074bf4f67f9f636ed59a5a909158fd991c386d511439018c9d062f3de20c0e`;
its manifest explicitly records `releaseReady=false`.

## Implementation and tests

The package owns one dnlib compiler patch implementation, project-local compiler
locking/recovery, and scopes around generation, Player builds and native
finalization. The native manifest records compiler/DataModel hashes, patch
contract, Editor version and additional arguments, including divide checks.
Generated-source provenance must match before guard finalization. See
`docs/HybridCLR-DHE-Aot-Compiler-Integration.md` for lifecycle and rollback.

All artifact paths below are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `compiler-integration-tests-{u21,u22,tuanjie}-reviewed/report.json`: 33/33
  each, including actual subprocess interruption/recovery, lock contention,
  foreign input preservation, invalid journals/backups and stale provenance.
- `aot-codegen-package-tests-u21-reviewed/report.json` and
  `aot-codegen-package-tests-{u22,tuanjie}/report.json`: 9/9 each, using the
  shared package patch, including repeat-patch rejection with DataModel present.
- `native-compiler-integration/<profile>/native-gate.json`: all three pass,
  `mergeReady=true`, `surrogateExternalHeadersUsed=false`.
- `base-compiler-integration-{u21,u22,tuanjie}/project-workflow-report.json`:
  all three Base/no-op workflows and schema gates pass in Exploratory mode.
- `replay-compiler-integration-u21/report.json`: focused three-process pass.
- `replay-compiler-integration-cold/report.json`: nine-process pass at clean
  source `51f71e1`, covering every Base in `registry-compiler-integration.json`.

Each engine executes the same two current resource releases plus an independent
skipped-first-update run. Every run passes 40 structural assertions, explicitly
marks eight absent legacy probes inapplicable, and preserves ten immutable files.
All 220 case classifications match Base/current MV. Each retained-AOT run has
zero case-entry receipts; each interpreted or skipped-update run has 220.
The original test DLL/MV inputs, CLR references and golden remain unchanged.
No historical failed result or archived Player was overwritten.

| Engine | Retained AOT | Interpreted | Skipped first update |
|---|---|---|---|
| Unity 2021 | 220/220, diff 0 | 220/220, diff 0 | 220/220, diff 0 |
| Unity 2022 | 220/220, diff 0 | 220/220, diff 0 | 220/220, diff 0 |
| Tuanjie 2022 | 220/220, diff 0 | 220/220, diff 0 | 220/220, diff 0 |

## Artifact identity

| Artifact | SHA-256 |
|---|---|
| Three-Base registry | `AC48B03891A86FBF3B60B4339E94556418B1FE066886781C7B993E749083BD4B` |
| Retained-AOT resource manifest | `F54138759957ECAEA178D30A8B159325DBFD50186D0D6140BCDB6747D15011C5` |
| Interpreted resource manifest | `9C9DDCB9806A37711AC913B8D6F0F118EE2817CF04C396AE21A6B27CE86961D6` |
| Three-engine cold replay | `D02C2F87A54F3F6464155CE081DE95EEA1867E9DA2517DD86C166673FB77B4C8` |

| Engine | Base ID |
|---|---|
| Unity 2021 | `77afabcbbd3211a7ab6a759ca88d8b5effe0cfd2ec9d8e7e434ab4b5ad59cb6e` |
| Unity 2022 | `c23770c5dd1e7d19f866a978c6cb234c392a6bbb9f1d726df8f954f0c5503323` |
| Tuanjie | `4312971dca92e0ae4d1fa59881bc2525de32eaa3c4e99c1ad91602fa00a48c2a` |

## Preservation and remaining work

After all workflows, the three project-local compiler hashes equal their original
Editor hashes and no transaction journal remains. Disposable Demo projects retain
their four generated plugin DLL changes, plus Unity 2021 settings normalization.
Before compiler migration their earlier generated inputs were preserved in stashes:
Unity 2021 `7e855ff`, Unity 2022 `abf6175`, Tuanjie `24c3f53`. Older stashes remain.

The current Bases all already contain the structural fixture. Fresh original
managed generations on this same implementation and a six-Base cold replay are
the next gate; historical v8/v9 selected-structure passes are not its substitute.
Existing generic-type fields, field-address access, value-type layout,
interfaces/vtables and other rejected evolution still need implementation.
External AOT API availability, Unity-facing behavior, concurrency/GC/ABI,
startup, performance and memory remain required before the Windows handoff.
Mac compiler layout/atomic replacement and mobile runtime correctness are untested.
Native fixes require new Base identities; do not relabel old Players as repaired.
