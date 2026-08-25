# HybridCLR Lab

Correctness, differential testing, and performance benchmarking for the
HybridCLR community runtime on Tuanjie 1.10.0.

## Fixed baseline

- Tuanjie: `1.10.0` (`2022.3.62t12`)
- HybridCLR package: `v8.13.0`
- HybridCLR runtime: `v8.13.0`
- il2cpp_plus: `v2022-tuanjie-8.13.0`

The runtime source repositories live beside this repository under `../repos`.
Their immutable inputs are recorded in `manifests/repo-lock.json`.

## Current phase

Phase 0 is complete. The engine-independent correctness contract and the real
Player gate now provide:

1. Build `managed-cases/HybridCLR.ManagedCases`.
2. Run the same case registry with the .NET reference runner.
3. Execute the same manifest in a clean Tuanjie/IL2CPP Player.
4. Compare semantic results, including case layer and feature coverage.

The manifest generator emits the schema-v2 JSON manifest plus deterministic
ID and contract indexes. Both runners reject missing, duplicate, or mismatched
case metadata, so adding a case without registering it cannot silently pass.

The managed-cases suite currently contains 220 deterministic cases (174
`managed-core` cases and 46 `player-boundary` cases). It is split into
`managed-core` cases that can run without Unity and
`player-boundary` cases that exercise AOT/interpreter calls, generic sharing,
delegates, structs, references, and exceptions through the real Player.
The registry covers numeric and control flow, arrays and collections, strings,
generic/value-type behavior, reflection, exceptions, iterators, static
constructors, async, threads, native P/Invoke/Reverse P/Invoke, and AOT
boundary contracts.

The standalone C++ suite has 6 Clean Baseline groups covering metadata decoding
and index utilities, opcode tables and switch decoding, basic-block splitting,
temporary memory arenas, and interpreter stack-copy helpers. Candidate adds a
second instruction-combiner group for IR layout, matching rules, and branch-copy
propagation.

Run the reference suite from this repository root:

```powershell
./scripts/run-reference.ps1
./scripts/run-native-tests.ps1
./scripts/summarize-coverage.ps1 -Player reports/baseline-clean-player-result.json
```

The Clean Baseline passes 185/185 reference cases and, when supplemental AOT
metadata is loaded, 185/185 Player cases with zero differential mismatches.
The first formal performance run also
compares the exact same 15 workloads as a dynamically loaded HybridCLR
assembly and as an IL2CPP AOT assembly. Across ten independent processes per
workload, AOT is 25.32x faster on the core interpreter/boundary subset and
13.04x faster across all workloads by geometric mean. See
`reports/baseline-clean-hybridclr-vs-aot.json`.

The instrumented build was used only to select optimization targets. The
current Candidate combines four community-runtime-only transformations, all
enabled only when no PDB mapping is required:

1. Two adjacent `LdlocVarVar` instructions become the 16-byte
   `LdlocVarVar_2` superinstruction while preserving copy order.
2. `LdcVarConst_4` followed by integer add becomes
   `LdcVarConst_4_Add_i4`.
3. `ConvertVarVar_i4_i8` followed by 64-bit add becomes
   `ConvertVarVar_i4_i8_Add_i8` with explicit sign extension.
4. `LdlocVarVar` or `LdlocVarVar_2` immediately before
   `BranchVarVar_Clt_i4` is copy-propagated in place, preserving the branch
   offset patch pointer and handling sequential-copy aliases.

Candidate 4 passes the full gate (`185/185` reference, `185/185` Player,
differential `0`, native CTest `8/8`). The repeated 10-process steady report
  shows a `1.235x` core geometric-mean speedup over Clean and no Candidate
  regression verdicts (`9/15` workloads exceed the current noise threshold).
  In the same Candidate 4 build, AOT is `20.540x` faster on the core
  interpreter/boundary subset. The final three-way report is
  `reports/candidate4-triple-performance-comparison-repeat.json`; the first run
is retained as `reports/candidate4-triple-performance-comparison.json`.
The Candidate 4 branch optimization initially exposed an offset-pointer bug
that caused a 300-second benchmark timeout; the fix and the
`performance_arithmetic_loop_regression` correctness case are now part of the
gate. Instrumented timing is diagnostic only and is never used as a performance
result.

## Reproduce the baseline

From this repository root, use the following order:

```powershell
./scripts/check-build-environment.ps1 -Target StandaloneWindows64
./scripts/assemble-runtime.ps1 -Profile Baseline-Clean -AllowDirty
./scripts/build-clean-baseline.ps1 -Profile Baseline-Clean -AllowDirty
```

Once the clean runtime has been assembled, the complete gate can be run with:

```powershell
./scripts/run-correctness-gate.ps1 -Profile Baseline-Clean -SkipAssembly
```

Reproduce or inspect the formal baseline with:

```powershell
./scripts/run-benchmark-player.ps1 -Profile Baseline-Clean -Mode steady -BenchmarkRuntime hybridclr
./scripts/run-benchmark-player.ps1 -Profile Baseline-Clean -Mode steady -BenchmarkRuntime aot
./scripts/compare-benchmark-runtimes.ps1
```

Reproduce the current Candidate and three-way comparison:

```powershell
./scripts/run-correctness-gate.ps1 -Profile Candidate -SkipAssembly
./scripts/run-benchmark-player.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime hybridclr -Processes 10 -Output reports/candidate4-player-hybridclr-steady-repeat-benchmark.json
./scripts/run-benchmark-player.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime aot -Processes 10 -Output reports/candidate4-player-aot-steady-benchmark.json
./scripts/run-benchmark-player.ps1 -Profile Candidate -Mode cold -BenchmarkRuntime hybridclr -Processes 10 -Output reports/candidate4-player-hybridclr-cold-benchmark.json
./scripts/run-benchmark-player.ps1 -Profile Candidate -Mode cold -BenchmarkRuntime aot -Processes 10 -Output reports/candidate4-player-aot-cold-benchmark.json
./scripts/compare-performance-profiles.ps1 -BaselineSteady reports/baseline-clean-player-hybridclr-steady-benchmark.json -CandidateSteady reports/candidate4-player-hybridclr-steady-repeat-benchmark.json -CandidateAotSteady reports/candidate4-player-aot-steady-benchmark.json -BaselineCold reports/baseline-clean-player-hybridclr-cold-benchmark.json -CandidateCold reports/candidate4-player-hybridclr-cold-benchmark.json -CandidateAotCold reports/candidate4-player-aot-cold-benchmark.json -Output reports/candidate4-triple-performance-comparison-repeat.json
```

When a new instruction needs execution evidence, run correctness with the
instrumented profile and require the generic opcode names explicitly:

```powershell
./scripts/run-correctness-gate.ps1 -Profile Baseline-Instrumented -AllowDirty -RequiredOpcode SetArrayElementVarVar_i4_NoNullNoBounds,NewValueTypeCtor_4_scalar
```

The comparison keeps Clean, Candidate, and AOT build manifests separate. It
only aligns workload contracts and benchmark policy; the Candidate and its AOT
column must share the same Candidate build manifest. Steady results are the
primary optimization metric. Cold results are recorded separately because
their process-start variance is too large for small optimization claims.

The build script compiles the engine-independent managed cases, installs the
merged `libil2cpp` through the package Installer, runs `HybridCLR/Generate/All`,
builds a Release Player, stages the stripped AOT metadata assemblies required
for generic sharing, and executes the Player runner with a per-case timeout.
Generated runtime copies, IL2CPP caches, Player builds, and StreamingAssets
DLLs stay outside Git.

## Generic sharing and metadata gate

The production merge design is in
`docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md`. One HybridCLR runtime
source now supports three explicit workflows: Tuanjie 2022 and Unity 2022 with
full generic sharing and no supplemental AOT metadata, plus Unity 2021 with
standard generic sharing and supplemental metadata. Run the cross-version
native gate and all three real Player gates with:

```powershell
./scripts/run-runtime-compatibility-matrix.ps1 -HybridClrSource ../repos/hybridclr -AllowDirty
./scripts/run-engine-workflow-gate.ps1 -EngineWorkflow Unity2021Standard -HybridClrSource ../repos/hybridclr -AllowDirty
./scripts/run-engine-workflow-gate.ps1 -EngineWorkflow Unity2022Fgs -HybridClrSource ../repos/hybridclr -AllowDirty
./scripts/run-engine-workflow-gate.ps1 -EngineWorkflow Tuanjie2022Fgs -HybridClrSource ../repos/hybridclr -AllowDirty
```

When an Editor is unavailable, surrogate external headers are rejected by
default. `-AllowSurrogateExternalHeaders` permits an explicit native-only run,
but its report sets `mergeReady=false` and cannot replace that engine's Player
gate.

Metadata-only changes are validated against all three engine ABI surfaces without
requiring the separate FGS bridge symbols:

```powershell
./scripts/run-runtime-compatibility-matrix.ps1 -CompatibilityScope Metadata -HybridClrSource ../worktrees/hybridclr-metadata-v8.13.0 -AllowDirty
```

Use the dedicated `Metadata-Unity2021` and `Metadata-Unity2022` profiles for
metadata work. This keeps their staging separate from the full-generic-sharing
compatibility lanes.

The two matrix scopes retain independent reports in
`reports/runtime-compatibility-matrix-workflow.json` and
`reports/runtime-compatibility-matrix-metadata.json`.

The metadata touch curve separates selective reflection from the exhaustive
worst case. Each point runs paired independent Player processes; selective
points directly resolve a deterministic spread of generated types and do not
call `Assembly.GetTypes()` first:

```powershell
./scripts/run-metadata-touch-curve.ps1 -Pairs 10 -Output reports/metadata-touch-curve.json
```

The default curve covers 1, 10, 100, 500, and 1024 outer stress types plus the
existing exhaustive all-types/all-members/all-attributes contract. Exhaustive
results remain the release hard gate; selective points describe how much of
the Assembly.Load benefit survives realistic partial metadata use. The curve
also records paired median, minimum, and maximum deltas for each stage so short
Reflection phases can be separated from cross-process noise.

The Tuanjie gate runs all six production-equivalent Candidate combinations and
then runs `Fgs-Diagnostic/OptimizeSize/exclude/none` to prove both FGS bridge
selection and entry into an inflated generic interpreter target. The Tuanjie
production contract is `OptimizeSize/exclude/none`; the Unity 2021 contract is
`OptimizeSpeed/include/supplemental`. Production profiles have diagnostics
disabled. Reused runtime/build artifacts must match source path, commit, and
tree SHA, including dirty source content. The current 2026-08-25 run passes all
six Tuanjie Candidate combinations, the diagnostic production combination, and
Unity 2021 at `220/220` with zero differential mismatches. Earlier research
measurements and the separate lazy-vtable experiment remain in
`docs/HybridCLR-Generic-Metadata-Analysis.md` as a historical snapshot.

## Android ARM64 gate

Android ARM64 is a separate final-validation lane rather than a replacement
for the Windows iteration loop. The lab builds Release IL2CPP APKs containing
only `arm64-v8a`, cross-compiles the native unit suite as an AArch64 ELF, runs
the full Player differential gate on a real device, and collects the
same 21 performance workloads with device and thermal-state evidence.

```powershell
./scripts/build-android-arm64.ps1 -Profile Baseline-Clean -SkipAssembly
./scripts/build-android-arm64.ps1 -Profile Candidate -SkipAssembly -AllowDirty
./scripts/run-native-tests-android-arm64.ps1 -Profile Candidate
./scripts/run-android-arm64-correctness.ps1 -Profile Candidate
./scripts/run-android-arm64-paired-benchmark.ps1 -Pairs 10
./scripts/run-android-arm64-benchmark.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime aot
```

The paired runner alternates Baseline/Candidate order to balance thermal and
time drift. An x86 emulator is not accepted as ARMv8 performance evidence.
The full protocol is in `docs/HybridCLR-Android-ARM64-Test-Standard.md`.
