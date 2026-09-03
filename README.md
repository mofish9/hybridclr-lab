# HybridCLR Lab

Correctness, differential testing, and performance benchmarking for the
HybridCLR community runtime on Tuanjie 1.10.0.

## Fixed baseline

- Tuanjie: `1.10.0` (`2022.3.62t12`)
- HybridCLR package: `v8.13.0` (`optimize/v8.13.0@749eaee`, no package opt tag)
- HybridCLR runtime: `v8.13.0-opt4.1` (`f777ed7`)
- il2cpp_plus: `v2022-tuanjie-8.13.0-opt4.1` (`52968ad`)

The runtime source repositories live beside this repository under `../repos`.
Their immutable inputs are recorded in `manifests/repo-lock.json`.

## Current phase

The DHE workflow is maintained as a separate, explicit validation lane. Its
integrated opt4.1 source line is documented in
`docs/HybridCLR-DHE-Opt4-Integrated-Design.md`.
Its formal entry points, generated-output boundary, and complete four-assembly
Player evidence are documented in
`docs/HybridCLR-DHE-Toolchain.md`,
`docs/HybridCLR-DHE-Workflow-Review.md` and
`docs/HybridCLR-DHE-Formal-Project-Validation.md`. Toolchain `0.1.18` is the
conditionally accepted source/Windows reference release; its exact evidence
and remaining platform gates are recorded in
`docs/HybridCLR-DHE-Opt4-Release-0.1.18.md`. Projects must install an
authenticated package whose manifest has `releaseReady=true` and pin its exact
package ID.
Toolchain `0.1.19` is the active candidate. It replaces generated-function
name guessing with IL2CPP MethodDef/MethodSpec table resolution and requires a
source-bound resolver regression report from each of the three locked Editors
before it can be published as Release.
The workflow also emits MetaVersion/native/workflow schemas and an independent
artifact validator; it verifies the exact supported+unsupported changed-token set,
and the versioned `il2cpp-generated-cpp-signature-v2` ABI adapter contract.
The current MetaVersion Player result covers method bodies, field/type/method evolution,
logical property/event metadata, custom attributes, reflection, and
cross-assembly calls. Two independently built Unity 2021 Base Players consumed
the same four-assembly current payload while computing 72 changed methods each.
The 0.1.19 release contract extends this proof to an arbitrary changed-Player
evidence list and requires Unity 2021, Unity 2022, and Tuanjie 2022 coverage
over one shared current payload.
`releaseReady=true` additionally requires `Mode Release`, clean
and tracked project and tool source identities (each bound to its Git commit,
HEAD tree, and source-boundary hash), clean locked runtime sources, and matching
non-surrogate engine headers.
The embedded package is locked by `manifests/dhe-package-lock.json`; the workflow
checks its full tree hash. Runtime assembly uses the published integrated
package, common-runtime, and engine-specific commits. It verifies every exact
commit and content-tree SHA-256 and retains the old patches only as authenticated
migration/audit records; integrated assembly never applies them.
Runtime assembly is an external, target-specific prerequisite. The C# DHE host
requires its staged runtime/baseline manifests as explicit inputs and never
silently substitutes a native-only runtime.

The fast, Unity-independent C# host checks are:

```text
dotnet build HybridCLR.Lab.sln --configuration Release --no-restore
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- \
  publish -LabRoot . -OutputRoot artifacts/dhe-toolchain -Mode Exploratory -ForceOutput
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- \
  verify-package -PackageRoot artifacts/dhe-toolchain
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- \
  doctor -Root artifacts/dhe-toolchain -Output artifacts/dhe-toolchain-doctor.json
```

The published package is authenticated by its manifest and complete file set.
`doctor` reports the installed .NET host, package
identity, and optional Unity project readiness. These checks do not claim
Player or native ABI coverage. Native compile/CTest and Unity/Xcode remain
separate environment gates.

JSON schemas under `schemas/` are enforced by the distributed `schema-validate`
and `schema-gate` commands. The `workflow` command runs the complete output tree
through this gate before returning success. The gate rejects unknown DHE formats
and unsupported schema assertions, and validates its own report before returning
success.

The formal DHE lane is reproducible from clean integrated runtime inputs. Assemble the
locked runtime (the `-ReposRoot` path may point at a new checkout), then run the
Demo through the reusable project orchestrator:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- \
  workflow -ProjectPath ./unity2021-dhe-demo \
  -SettingsFile ./unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot ./artifacts/dhe-project-workflow \
  -BaselineAotRoot ./releases/previous/stripped-aot \
  -Target StandaloneWindows64 \
  -AdapterMethod HybridCLR.Lab.Editor.HybridCLRDheWorkflowBuild.Prepare \
  -Unity /path/to/Unity \
  -Mode Release -RunPlayer
```

For a project-owned setup, keep these values in a versioned
`dhe-workflow-config.json` and run `workflow -Config <path>`. The host never
assumes the Demo adapter; the adapter method and project-specific Unity
arguments are configuration inputs.

`Release` is intentionally usable only from a clean checkout after the formal
sources have been committed. During tool development, substitute
`-Mode Exploratory`; it records `releaseReady=false`. `-RunPlayer` invokes the
project adapter's C# methods for runtime-plan staging, resource evidence,
scripts-only, and final Player builds. Release decisions must use current
MetaVersion reports bound to the exact Base identity and current payload. The
current MetaVersion
Player suite covers direct/reflection generic calls, a null generic reference,
generic virtual dispatch, value-type state-machine paths, secondary assemblies,
new top-level types and methods, and invalid-MV same-process retry. A new release
must reproduce this evidence after migration. The no-op lane executes and validates baseline
behavior across all four AOT assemblies; a zero changed-method count alone is not
accepted as proof. The Player also checks the snapshot hash compiled
into `HybridCLRDheBuildIdentity`, so a mutable
`StreamingAssets` snapshot cannot stand in for the actual AOT build. The workflow
also archives a self-contained `runtime-plan/` directory and validates its
payload hashes independently of the ignored Unity `Assets/StreamingAssets` cache.
The host verifies output safety and fails closed on missing or incompatible
assemblies before Unity generation. Release mode additionally requires a
Git-clean source root and tracked formal boundary sources. The project and
boundary file must belong to that same repository; an unrelated clean Git root
is rejected.
Runtime preflight also binds `ProjectSettings/ProjectVersion.txt` to the exact
engine version recorded by the runtime manifest; a matching executable family
alone is insufficient.
The manifest also records the staged external-header tree hash, which the
source and native gates recompute before accepting the runtime.

The host `archive` command produces a sibling archive directory. The same
command can be run manually for an existing workflow output. It copies generated C++ and all
managed DHE payloads, the project settings file, and all reports, rewrites
project-plan/batch/identity/manifest references to archive-relative paths, and
revalidates the copy from a different working directory. A copied
`runtime-plan/` directory alone is not a complete archive.
When the workflow report includes `packageLock`, the matching lock is copied
under `provenance/dhe-package-lock.json`; projects using a registry-managed
HybridCLR package can omit this optional package provenance.
The archived runtime manifest is provenance-only: it uses `archive-relative-v1`,
binds the runtime lock to `provenance/dhe-runtime-lock.json`, and removes local
editor, source-repository, and staged-runtime paths. The archive gate recursively
rejects Windows drive and UNC paths in every archived JSON document; retained
project/tool identities contain hashes and relative provenance only. The archive
is evidence for handoff, not a replacement for assembling the runtime on the
destination build machine.

For a project-independent offline preflight, resolve the project's configured
DHE AOT set from its own settings and generate strict MV artifacts in one
command. To cover every hot-update assembly, keep `dheAotAssemblies` equal to
the complete `hotUpdateAssemblies` list:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- \
  preflight -SettingsFile C:/path/to/project/ProjectSettings/HybridCLRSettings.asset \
  -BaselineRoot C:/path/to/project/stripped-aot \
  -CurrentRoot C:/path/to/project/current-hot-update \
  -OutputRoot ./artifacts/project-dhe-preflight \
  -RequireCompleteCoverage -RequireDheEqualsHotUpdate
```

The preflight validates every assembly with the same C# host. Native
ABI coverage remains a separate Unity/IL2CPP stage; this offline report cannot
prove Player dispatch. Without `-RequireCompleteCoverage`,
`generationPassed`/`validationPassed` describe tool and artifact health, while
`coverageComplete` and `artifactReady` distinguish an exploratory result from a
complete artifact result. The offline report always keeps `releaseReady=false`;
only the final Player release gate can set that status. For a project outside
the demo, pass its package's `dnlib.dll` with `-DnlibPath`, or keep the package
embedded at `Packages/com.code-philosophy.hybridclr`. Project workflows fail
closed instead of silently borrowing the demo's dnlib assembly.
The generated project plan records the hot-update and DHE assembly sets. The
Final C# release validation additionally requires
`requireDheEqualsHotUpdate=true`, a complete Player report, and exact matches
between the plan's assemblies and the assemblies loaded/AOT-compiled as DHE.
The plan's `settingsFile`, `baselineRoot`, `currentRoot`, and `batchReport` are
authoritative material references; the plan validator checks their existence,
report format, and per-assembly agreement. Relative references are resolved
from the report that contains them, so archived batch records use
`../payload/...` while the archived project plan uses `payload/...`.
The Player transaction probe also loads the main DHE assembly with an invalid
method token, verifies DHE_MV_REGISTRATION_FAILED, and then retries the valid MV
in the same process; the result is recorded under transaction in the workflow
report.

For a production project, use the C# host `workflow` command with a
project adapter. The adapter's `Prepare` action supplies the exact stripped-AOT
baseline/current roots and writes `adapter/prepare.json`; its `Player` action
consumes the validated project plan and writes the standard `workflow-report.json`.
The runner owns source preflight, strict MV/artifact validation, archive, and
Release gating. `-StopAfterPreflight` is available for adapter contract tests;
it does not claim Player or release coverage. The complete adapter contract is
in `docs/HybridCLR-DHE-Formal-Project-Validation.md`.
The host starts Unity directly with `ProcessStartInfo`, so Android and iOS use
the same C# entry point. Set `-UnityTimeoutSeconds` to bound an Editor phase;
logs are written below the output root and never back into the project source.

Release additionally requires separate clean project and tool source identities.
The project `-GitRoot` defaults to `-ProjectPath`; use `-ProjectVcs Svn` for an
SVN working copy. Git identities record HEAD/HEAD tree; SVN identities record the
concrete working-copy URL, numeric revision, and `svnversion` spec. Both identities
must have tracked boundary sources and boundary SHA-256. Runtime source
commit/dirty provenance, engine workflow/ProductVersion, and embedded package
locks are verified independently. Unknown MV wire-format flags fail closed.
Exploratory mode can request the same checks with `-RequireGitClean`,
`-RequireTrackedSources`, and `-RequireCleanRuntimeSources`; Release also requires
tracked-source coverage using `-SourceBoundaryPath` (or the project's default
boundary manifest).

The optimization and Android sections below preserve earlier lab results. Some
of their historical helper commands are not part of this DHE-focused checkout;
the commands above and the three DHE toolchain/validation documents are the authoritative
current entry points.

> **Scope note:** everything below this note is historical optimization and
> compatibility evidence. It is retained for audit context, not as an executable
> DHE release workflow. In particular, commands mentioning Candidate, benchmark,
> or Android helper scripts may refer to files from the earlier lab checkout.
> Use `docs/HybridCLR-DHE-Toolchain.md` for distribution and installation,
> `docs/HybridCLR-DHE-Formal-Project-Validation.md` for the project contract,
> and the C# `preflight`/`workflow` commands for the source-checkout entry
> points.

## Historical lab results

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

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- generate-test-manifest -LabRoot .
dotnet run --project tool/HybridCLR.DheTool.csproj -- reference -LabRoot .
dotnet run --project tool/HybridCLR.DheTool.csproj -- compare-results -LabRoot . -Actual reports/player-result.json
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

## Historical baseline records

The following performance and compatibility numbers were produced by an older
lab harness. Its PowerShell entry points are retired; use the C# host commands
above for current DHE validation. The records remain here only to preserve the
identity of earlier opt1-3 experiments.

From this repository root, use the following order:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- check-environment -LabRoot . -Target StandaloneWindows64
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime -LabRoot . -Profile Baseline-Clean
dotnet run --project tool/HybridCLR.DheTool.csproj -- build-managed-cases -LabRoot . -Target StandaloneWindows64
```

The old correctness and benchmark runners were experiment-only PowerShell
programs and are no longer distributed. Their reports remain historical
records; current validation uses the C# `workflow`, `reference`,
`compare-results`, and `native-tests` commands described above. This keeps the
document from advertising commands that cannot be executed from a clean
checkout.

The comparison keeps Clean, Candidate, and AOT build manifests separate. It
only aligns workload contracts and benchmark policy; the Candidate and its AOT
column must share the same Candidate build manifest. Steady results are the
primary optimization metric. Cold results are recorded separately because
their process-start variance is too large for small optimization claims.

The C# host workflow compiles the engine-independent managed cases, installs
the merged `libil2cpp` through the package Installer, runs
`HybridCLR/Generate/All`, builds a Release Player, stages the stripped AOT
metadata assemblies required for generic sharing, and executes the Player
runner with a per-case timeout.
Generated runtime copies, IL2CPP caches, Player builds, and StreamingAssets
DLLs stay outside Git.

## Historical generic sharing records

This section documents older opt2/opt3 compatibility runs. The current DHE
lane uses `assemble-runtime`, `native-tests`, and `workflow` from the C# host;
the old matrix runner is no longer present.

The production merge design is in
`docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md`. One HybridCLR runtime
source now supports three explicit workflows: Tuanjie 2022 and Unity 2022 with
full generic sharing and no supplemental AOT metadata, plus Unity 2021 with
standard generic sharing and supplemental metadata. Run the cross-version
native gate and all three real Player gates with:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime -LabRoot . -Profile DHE-Unity2021 -EngineWorkflow Unity2021Standard
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests -LabRoot . -Profile DHE-Unity2021
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime -LabRoot . -Profile DHE-Unity2022 -EngineWorkflow Unity2022Fgs
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests -LabRoot . -Profile DHE-Unity2022
```

When an Editor is unavailable, surrogate external headers are rejected by
default. `-AllowSurrogateExternalHeaders` permits an explicit native-only run,
but its report sets `mergeReady=false` and cannot replace that engine's Player
gate.

Metadata-only changes use the dedicated `Metadata-Unity2021` and
`Metadata-Unity2022` profiles with the same C# `assemble-runtime` and
`native-tests` commands. This keeps their staging separate from the
full-generic-sharing compatibility lanes.

The metadata touch curve separates selective reflection from the exhaustive
worst case. Each point runs paired independent Player processes; selective
points directly resolve a deterministic spread of generated types and do not
call `Assembly.GetTypes()` first:

The curve generator from the retired experiment harness is not part of the
formal toolchain. Use `generate-metadata-stress-source` and the project-owned
workflow when a metadata stress run is required.

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
measurements and the separate lazy-vtable experiment are historical material;
the current optimization design is retained in
`docs/HybridCLR-Optimization-Design.md`.

## Historical Android ARM64 records

The Android measurements below belong to the retired experiment harness. The
formal DHE Android/iOS entry point is the cross-platform C# `workflow` command;
device signing and smoke remain project adapter responsibilities.

Android ARM64 is a separate final-validation lane rather than a replacement
for the Windows iteration loop. The lab builds Release IL2CPP APKs containing
only `arm64-v8a`, cross-compiles the native unit suite as an AArch64 ELF, runs
the full Player differential gate on a real device, and collects the
same 21 performance workloads with device and thermal-state evidence.

The old Android ARM64 runners were also experiment-only scripts. Android and
iOS builds now enter through the cross-platform C# `workflow`; signing,
deployment, device smoke, and platform-specific performance collection belong
to the project's C# adapter.

The paired runner alternates Baseline/Candidate order to balance thermal and
time drift. An x86 emulator is not accepted as ARMv8 performance evidence.
The Android protocol is outside this DHE-focused checkout and is not a release
claim for the workflow described above.
# DHE workflow implementation note

The formal DHE toolchain is C#-only. Use `tool/HybridCLR.DheTool.csproj` with
`dotnet`; the repository and distributed package contain no PowerShell scripts.
The final DHE Player path also stays in C#: the package injects guards into the
last IL2CPP-generated source snapshot and invokes the editor-owned Bee backend
directly before runtime smoke validation.
All DHE lab helpers used by the current workflow have corresponding host
commands. Older opt1-3 benchmark/matrix runners are retired with their scripts.
See
`docs/HybridCLR-DHE-Toolchain.md` for the current workflow and the explicit
platform prerequisites for Android and iOS.
