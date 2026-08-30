# DHE Workflow And Project Validation

This document defines the reusable DHE workflow and the validation boundary
for any Unity + HybridCLR project. A project contributes its build artifacts
and assembly list; it does not change the algorithm or the `mv` format.

The repository source boundary is itself checked by
`scripts/run-dhe-source-boundary-gate.ps1`, using
`manifests/dhe-source-boundary.json`. Run it before creating a release commit;
it rejects non-ignored untracked files outside the allowlist and verifies that
generated output, Unity caches, and historical probes remain ignored.

The core orchestrator and report contracts accept an opaque, validated target
identifier and do not encode a platform list. The checked-in Demo adapter is the
Windows implementation and intentionally accepts only `StandaloneWindows64`
with the matching Tuanjie/Unity editor. Android and mini-game targets are not
claimed by this Demo lane yet: each needs an adapter that owns its generated-code
path, asset loading, Player build, and target-specific assertions.

## Stage 0: source/runtime preflight

在任何 Unity 生成步骤前，先验证 clean checkout 的正式输入和 runtime provenance：

```powershell
./scripts/run-dhe-source-preflight.ps1 `
  -ProjectPath C:/path/to/project `
  -RuntimeSource C:/path/to/staging/runtime/DHE-Tuanjie2022/libil2cpp `
  -PackageLockPath C:/path/to/dhe-package-lock.json `
  -OutputRoot ./artifacts/source-preflight `
  -RequireRuntime `
  -RequireDheEqualsHotUpdate `
  -RequireEmbeddedPackage `
  -RequireNonSurrogateExternalHeaders `
  -ForceOutput
```

该阶段校验 DHE patch lock、可选的 embedded package tree hash、Unity package manifest、
`dheAotAssemblies` 覆盖范围、zeroed build identity 模板和 runtime manifest。它输出
`source-preflight-report.json`；缺失 runtime、stale manifest 或本机绝对输入路径会在
Unity 启动前失败。

`RequireNonSurrogateExternalHeaders` 是 Release 约束：runtime manifest 必须证明
external headers 来自匹配的引擎安装。只有 exploratory native-only 检查可以显式传入
`-AllowSurrogateExternalHeaders`。

runtime manifest 还记录 staged external headers 的 tree hash；预检和 native gate 会重新
计算该目录，避免仅凭 `surrogate=false` 这个布尔字段接受被替换的头文件。

当提供 `RuntimeSource` 时，预检还会读取项目的
`ProjectSettings/ProjectVersion.txt`，要求 `m_EditorVersion` 与 runtime manifest 的
`engine.unityVersion` 完全一致；Tuanjie 项目还必须匹配 `m_TuanjieEditorVersion` 与
`engine.version`。只检查引擎家族名称不足以证明生成 C++ ABI 兼容。

`PackageLockPath` 和 `RequireEmbeddedPackage` 只在项目把 HybridCLR 作为 embedded
package 管理时启用。使用 registry/package manager 的项目可以省略它们，通用预检
仍会执行程序集范围、runtime 和输出目录安全检查。
Embedded package locks use `pathBase=project-root-v1`; `packagePath` must be a
safe relative path such as `Packages/com.code-philosophy.hybridclr`. The lock
file itself may live outside the project because its directory is never used as
an alternate path base.
Release adapter 应把项目仓库传给 clean-checkout gate 的 GitRoot。门禁会分别建立
projectGit 和 toolGit 身份：两者都必须 clean，source boundary 必须被跟踪，并记录
HEAD、HEAD tree 与 boundary SHA-256。项目仓库与工具仓库可以是两个独立仓库；ignored
Unity 缓存仍可保留，但任一身份范围内的 tracked 或 untracked 源文件都会阻断发布。

## Stage A: artifact preflight

Use the project's own `HybridCLRSettings.asset` as the source of the DHE AOT
assembly list. When the optional `dheAotAssemblies` field is present, it is
authoritative; a project that wants every hot-update assembly on the DHE path
must list the complete hot-update set there as well. The reusable preflight
entry point works for any project:

```powershell
./scripts/run-dhe-project-preflight.ps1 `
  -SettingsFile C:/path/to/project/ProjectSettings/HybridCLRSettings.asset `
  -BaselineRoot C:/path/to/project/stripped-aot `
  -CurrentRoot C:/path/to/project/current-hot-update `
  -OutputRoot ./artifacts/project-dhe-preflight `
  -RequireCompleteCoverage -ForceOutput
```

It resolves every configured assembly, runs strict MV/binary generation, and
invokes `validate-dhe-artifacts.ps1` for each result. This offline stage does
not claim native ABI coverage; that is evaluated only after Unity/IL2CPP code
generation.

When the field is absent or an empty array, the patched Unity package applies
the same rule and treats the complete `hotUpdateAssemblies` plus assembly
definition set as DHE AOT input. A non-empty `dheAotAssemblies` list remains an
explicit subset. This keeps the project plan and the Unity build's filtered
assembly set identical.

The output also contains `dhe-project-plan.json`, a canonical per-assembly
handoff with the baseline/current paths, MV JSON/binary paths, hashes, and
changed-method counts. The demo Player adapter consumes this plan-derived
runtime mapping to load every compatible assembly without re-parsing project
settings. The plan also records `hotUpdateAssemblies`, `dheAotAssemblies`, and
`requireDheEqualsHotUpdate`; a release plan must prove that those sets are
identical. Its `settingsFile`, `baselineRoot`, `currentRoot`, and `batchReport`
references are authoritative material inputs: the project-plan validator checks
that they exist, have the expected report format, and agree with every batch
assembly record. Relative references are resolved from the file that contains
them; generated preflight plans currently use absolute paths for local
traceability.

Re-validate a copied plan independently before handing it to another build
stage:

```powershell
./scripts/validate-dhe-project-plan.ps1 `
  -Plan ./artifacts/project-dhe-preflight/dhe-project-plan.json `
  -RequireCompleteCoverage `
  -Output ./artifacts/project-dhe-preflight/project-plan-validation.json
```

The final project release gate consumes both the validated plan and a Player
workflow report. It requires complete native coverage and an exact match between
the plan assembly set and the assemblies loaded as DHE:

```powershell
./scripts/run-dhe-release-gate.ps1 `
  -ProjectPlanValidation ./artifacts/project-dhe-preflight/project-plan-validation.json `
  -WorkflowReport ./artifacts/dhe-project-workflow/workflow-report.json `
  -Target StandaloneWindows64 `
  -Output ./artifacts/dhe-project-workflow/release-gate.json
```

The demo adapter now emits a four-assembly plan and loads every declared DHE
image. A project-level release gate still requires its own plan and Player
evidence; demo success is not inferred as production success.

## Project adapter contract

`run-dhe-project-workflow.ps1` is the reusable Stage 0-D orchestrator. A
project-specific adapter supplies only the Unity/IL2CPP integration details
through two actions, both implemented as named PowerShell parameters:

```text
Prepare -ProjectPath -SettingsFile -RuntimeSource -OutputRoot -Target -Mode
        -ToolchainContractVersion
Player  -ProjectPath -SettingsFile -RuntimeSource -OutputRoot -Target -Mode
        -ToolchainContractVersion
        -ProjectPlan -ProjectPlanValidation -BatchReport
        -SourcePreflight -CleanCheckoutGate [-RequireCompleteCoverage]
```

`Prepare` must build the exact stripped-AOT baseline and current hot-update DLL
roots that will be compared, then write
`<OutputRoot>/adapter/prepare.json` conforming to
`schemas/dhe-project-adapter-prepare.schema.json`. The report must contain the
same project/settings paths, the exact target supplied to the orchestrator,
`pathSemantics=workspace-absolute-v1`, and `passed=true`; all four path fields
must be absolute paths. The target is an opaque safe identifier to the core
workflow; platform support is enforced by the selected adapter. The two roots
are authoritative inputs for the shared project preflight and are resolved
independently of the caller's current working directory.
The checked-in implementation is
`scripts/adapters/dhe-demo-project-adapter.ps1`; it is the first complete
contract example and is exercised by the manual self-hosted CI lane.

Before calling the adapter, the orchestrator verifies the installed toolchain
identity when applicable, then runs clean-checkout and source/runtime preflight.
After `Prepare`, it repeats both gates so adapter or Unity generation cannot
silently modify project/tool sources, runtime inputs, package contents, or
settings. It then runs strict MV/artifact generation with
`-RequireDheEqualsHotUpdate`, so every configured hot-update assembly is covered
by DHE. `Player` receives the validated project plan and must write the standard
`<OutputRoot>/workflow-report.json`; it owns the Unity build, generated C++ guard
injection, runtime-plan staging, and Player assertions, but it must use the paths
and assembly set from that plan rather than rediscovering them.
When complete coverage is requested, the core also passes
`-RequireCompleteCoverage` to the adapter so project-specific transforms and
artifact checks use the same policy as preflight and release gates.
The workflow runner independently executes the archive gate and, in `Release`
mode, the release gate. Its summary is
`<OutputRoot>/project-workflow-report.json` and conforms to
`schemas/dhe-project-workflow.schema.json`.

If the adapter publishes `yooAssetBuild` evidence, its `requiredAssets` must
include the five DHE control assets, every runtime-plan `current`/`mv`/`snapshot`
payload, and every AOT metadata asset listed by the project's AOT list. The
validator matches these records back to the runtime plan and, for workspace
evidence, verifies the referenced bundle file, size, SHA-256, and YooAsset hash.

For adapter development, `-StopAfterPreflight` runs the real source and project
preflight with a `Prepare` implementation but intentionally skips Player,
archive, and release stages. It is contract evidence only; a release result
still requires a real target Player report and complete native coverage.
The reusable runner and demo runner serialize complete workflow executions with
a worktree-scoped cross-process mutex. The default timeout is zero, so a second
writer fails immediately; use `-WorkflowLockTimeoutSeconds` only when a bounded
queue is intentional. After an output root is safely initialized, failures are
recorded in versioned top-level JSON reports. Project preflight writes
`project-preflight-report.json` even when source preflight fails, while retaining
the detailed nested source report.

The production-shaped invocation is:

```powershell
./scripts/run-dhe-project-workflow.ps1 `
  -AdapterScript C:/path/to/project/build/dhe-adapter.ps1 `
  -ProjectPath C:/path/to/project `
  -SettingsFile C:/path/to/project/ProjectSettings/HybridCLRSettings.asset `
  -RuntimeSource C:/path/to/staging/runtime/DHE-Tuanjie2022/libil2cpp `
  -OutputRoot ./artifacts/project-dhe-workflow `
  -BaselineAotRoot C:/path/to/releases/previous/stripped-aot `
  -PackageLockPath C:/path/to/project/manifests/dhe-package-lock.json `
  -IdentityTemplatePath C:/path/to/project/Assets/Runtime/HybridCLRDheBuildIdentity.cs `
  -GitRoot C:/path/to/project `
  -SourceBoundaryPath C:/path/to/project/manifests/dhe-source-boundary.json `
  -RequireEmbeddedPackage -RequireIdentityTemplate -ForceOutput
```

This example runs from a clean source checkout. When invoking an installed
toolchain, use its `dhe.ps1 workflow` entry point and supply the externally
pinned `-ExpectedToolchainPackageId` described in
`docs/HybridCLR-DHE-Toolchain.md`.

`Release` is the default and therefore requires complete native coverage and
matching (non-surrogate) engine external headers;
`-Mode Exploratory` is the explicit non-release development lane; it may have
complete native coverage but never produces a passing release identity.

Release also runs the clean-checkout gate before source preflight. `GitRoot`
defaults to `ProjectPath` and can be set explicitly when the project is nested
inside a larger repository. Use `-ProjectVcs Svn` for an SVN project; an explicit
`Git` or `Svn` selection also enables the project identity check in Exploratory
mode, even when `GitRoot` is omitted. The resolved
project Git top-level (or SVN working-copy root) must contain both the project and
its tracked source-boundary manifest; an unrelated clean repository cannot supply
project identity. Independently, the runner resolves the tool Git root and
`manifests/dhe-source-boundary.json`. Both `projectGit` and
`toolGit` must be clean and tracked, and the report binds Git identities to their
HEAD commit/HEAD tree and SVN identities to their concrete URL, numeric revision,
and `svnversion` spec, together with the source-boundary SHA-256. The gate also binds the
runtime manifest to current workflow/repository locks, actual source commits,
dirty state, locked external-header tree hash, and editor ProductVersion. Use `-RequireGitClean`,
`-RequireTrackedSources`, or `-RequireCleanRuntimeSources` to opt into these
checks in Exploratory mode. An embedded package in Release must have a matching
package lock.
Release always requires tracked-source coverage. For a repository outside this
lab, pass `-SourceBoundaryPath` to that repository's boundary manifest; if it is
omitted, the runner looks for `manifests/dhe-source-boundary.json` inside the
project and otherwise fails closed. Exploratory `-RequireTrackedSources` uses
the same resolution rules.

Use `scripts/archive-dhe-artifacts.ps1` (or the archive gate) to hand the
evidence to another machine. The archive copies the settings file, managed
assemblies, MV files, generated C++, and reports, then rewrites the project plan
to archive-relative paths and independently revalidates that rewritten plan.
If the workflow report carries a `packageLock` reference, that lock is copied
under `provenance/dhe-package-lock.json`; registry-managed projects may omit
that optional provenance file.
The copied runtime manifest and build identity declare `archive-relative-v1`:
workspace-only runtime/editor/source paths are removed, while commit, tree-hash,
patch-hash, engine, runtime-lock, and project/tool identity hashes remain
available as provenance. The archive gate recursively rejects Windows drive and
UNC paths in every JSON document, so no machine-local absolute path can survive
handoff. The archive does not contain a runnable `libil2cpp` tree; a new build
must assemble the locked runtime in its own workspace.
Inside `batch/dhe-batch-summary.json`, payload references use `../payload/...`
because batch paths are relative to the batch report directory. An archive gate
without `-RequireCompleteCoverage` is exploratory when native ABI shapes remain
unsupported; it must not be promoted to a release result.

For a capability inventory before selecting Player cases, use
`scripts/analyze-dhe-capabilities.ps1` on the same assembly set. Its counts are
planning evidence only and do not loosen the strict compatibility gate.

Without `-RequireCompleteCoverage`, the command is an exploration preflight:
`generationPassed`/`validationPassed` mean the requested reports were generated
and validated without tool or artifact errors, while `coverageComplete` and
`artifactReady` expose missing or rejected assemblies. The offline report always
keeps `releaseReady=false` because native ABI and Player evidence are not
evaluated here. The release command must use `-RequireCompleteCoverage` so an
incomplete assembly set returns a non-zero exit code, then consume the final
Player report through `run-dhe-release-gate.ps1`.

When the project does not use the demo's embedded package, pass its own dnlib
assembly explicitly with `-DnlibPath C:/path/to/project/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll`.
The preflight records the resolved path in `dnlibPath` and prefers the project's
embedded package when one is present.

The lower-level batch primitive remains available when a caller needs direct
control over its arguments:

```powershell
./scripts/generate-dhe-batch.ps1 `
  -BaselineRoot C:/path/to/project/HybridCLRData/HotUpdateDlls/Android `
  -CurrentRoot C:/path/to/project/Library/ScriptAssemblies `
  -SettingsFile C:/path/to/project/ProjectSettings/HybridCLRSettings.asset `
  -OutputRoot ./reports/cat-dhe-preflight
```

The command only reads the project DLLs and writes an isolated report. A
release candidate must use `-StrictCompatibility -FailOnIncompatible`; in
that mode a binary `*.mv.bytes` is emitted only when the method-body-only
compatibility gate passes.

When `-RequireDheEqualsHotUpdate` is used, a mismatch between the project's
`hotUpdateAssemblies` and `dheAotAssemblies` is recorded in the batch report as
`configurationPassed=false` with `configurationErrors[]`. The project preflight
propagates that result and fails without discarding the machine-readable report.
Multi-assembly artifact validation matches inputs by assembly basename and
rejects duplicate or unmatched names; it does not fall back to array position.

## Stage B: AOT baseline

The baseline root must be the stripped AOT assemblies produced by the exact
player build that will ship. `Library/ScriptAssemblies` and a previous
hot-update compile are useful for preflight, but are not an AOT snapshot.
Build identity, platform, Unity version, HybridCLR fork revision and the
baseline assembly hash must be recorded together.

## Stage C: generated-code gate

After the custom native runtime is installed, build the player with every
listed hotfix assembly present in the AOT input. The generated C++ gate must
resolve a guard for every changed method whose concrete IL2CPP ABI is supported
by the injector, including the tested virtual and state-machine entries.
Unsupported ABI shapes are failures, not silent interpreter fallbacks.

## Stage D: player gate

Before loading any gameplay prefab, load every current DLL with its matching
`mv.bytes` and verify:

- current DLL hash and baseline snapshot hash;
- changed methods execute through the interpreter;
- unchanged methods execute through the generated AOT entry;
- calls from changed to unchanged methods remain valid;
- a failed changed-method registration retires its homologous image and a
  subsequent valid MV can retry in the same process;
- the result matches the normal hotfix player.

Only after all four stages pass should the existing BattleAOT special branch be
removed. Until then the production project continues using its current
HybridCLR path.

## Demo gate

The reusable workflow is accepted only after the standalone Tuanjie 2022 demo
passes the same gates. The demo uses four DHE AOT assemblies. The
`HybridCLR.ManagedCasesAot` image carries the controlled method-body change;
`ManagedCases`, `MetadataStress`, and `CrossAssemblyDerived` exercise unchanged
AOT dispatch and cross-assembly loading:

```powershell
$prepare = Get-Content -Raw ./artifacts/dhe-project-workflow/adapter/prepare.json | ConvertFrom-Json
./scripts/generate-dhe-batch.ps1 `
  -BaselineRoot $prepare.baselineRoot `
  -CurrentRoot $prepare.currentRoot `
  -SettingsFile ./unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset `
  -ProjectRoot ./unity2021-dhe-demo `
  -OutputRoot ./artifacts/dhe-demo-batch `
  -StrictCompatibility -FailOnIncompatible -RequireDheEqualsHotUpdate
```

The generated binary is then embedded in the demo Player and the Player is
run with `-labMode dhe`. A complete release result must report `loadError=OK`,
changed methods in `interpreter`, unchanged methods in `aot`, valid current,
baseline and snapshot hashes, and zero unsupported changed methods. The
workflow can be run without the complete-coverage switch for ABI exploration;
that result is evidence only and must not be treated as a release pass.

### Extended demo capability gate

The demo now exercises the categories visible in the reference project's
capability profile: interface and virtual dispatch, generic and constrained
calls, generic containers, value-type/ref-out/nullable values, boxing-related
value flow, async and iterator state machines, closed/open receiver delegates,
multicast delegates, exceptions/finally, reflection invocation, and changed
to unchanged AOT call chains. The raw assembly gate reports:

```text
methodCount=115
changedMethodCount=24
typeChangeCount=0
compatibility=compatible (method-body-only)
```

The standalone Player result is emitted under the workflow output directory
(for example, `artifacts/dhe-project-workflow/dhe-player-result.json`).
It also records the planned/loaded assembly sets and cross-assembly probes:

```text
passed=true
loadError=OK
capabilityPassed=true
multiAssemblyValidated=true
changedMethod=interpreter
unchangedMethod=aot
interpreterEntryCount=10
aotBridgeCallCount=0
aotEntryCount=10
capabilityDirectPassed=true; capabilityDirectInterpreterEntryCount=26
boxValueChanged=true; boxNumber=103; boxWide=4
delegateOpenInstanceChanged=true; delegateOpenInstanceResult=108
secondaryAssemblyChangedValidated=true; secondaryAssemblyDirectValidated=true
```

The deterministic build is reproducible from a clean generated-code cache:

```powershell
./scripts/run-dhe-deterministic-player-build.ps1 `
  -UnityExe "C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t12/Editor/Tuanjie.exe" `
  -ProjectPath ./unity2021-dhe-demo `
  -BuildPath ./unity2021-dhe-demo/Builds/DHE-Capability-Extended/HybridCLRLab.exe `
  -MvJson ./artifacts/dhe-capability-gate-extended3/HybridCLR.ManagedCasesAot.mv.json `
  -TransformerScript ./scripts/apply-dhe-generated-cpp.ps1 `
  -DheAotAssemblies ./artifacts/dhe-capability-gate-extended3/baseline/HybridCLR.ManagedCasesAot.dll `
  -BuildIdentity ./artifacts/dhe-capability-gate-extended3/build-identity.json
```

For the demo, the complete sequence runs through the same reusable project
orchestrator used by external adapters. It builds both stripped images, runs the
shared preflight, consumes that exact plan in the Player action, and then runs
the independent archive/release stages:

```powershell
./scripts/run-dhe-project-workflow.ps1 `
  -AdapterScript ./scripts/adapters/dhe-demo-project-adapter.ps1 `
  -ProjectPath ./unity2021-dhe-demo `
  -SettingsFile ./unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset `
  -RuntimeSource ./staging/runtime/DHE-Tuanjie2022/libil2cpp `
  -OutputRoot ./artifacts/dhe-project-workflow `
  -ArchiveRoot ./artifacts/dhe-project-workflow-archive `
  -BaselineAotRoot ./releases/previous/stripped-aot `
  -DnlibPath ./unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll `
  -PackageLockPath ./manifests/dhe-package-lock.json `
  -IdentityTemplatePath ./unity2021-dhe-demo/Assets/Runtime/HybridCLRDheBuildIdentity.cs `
  -GitRoot . -SourceBoundaryPath ./manifests/dhe-source-boundary.json `
  -Mode Release -RequireEmbeddedPackage -RequireIdentityTemplate -ForceOutput
```

The resulting `artifacts/dhe-project-workflow/workflow-report.json` and
`dhe-player-result.json` report the exact source/runtime identity, changed
method count, native guard coverage, and Player result. The output also contains
`runtime-plan/dhe-runtime-plan.json` with all current/baseline/MV/snapshot payloads;
it is the self-contained handoff for later packaging or independent validation.
The reusable orchestrator creates the sibling archive and runs its portable
copy gate; the workspace and archived workflow reports bind the same project
plan, validation, batch, and runtime payloads using their respective path semantics.
The two-argument loader
is retained only for ABI compatibility and always rejects loading; a release
build must use the three-argument loader with the actual platform AOT snapshot
hash. The workflow report
separates `validationPassed` (the Player gate), `coverageGatePassed` (the
complete-coverage requirement), and `releaseReady` (complete compatible Player
evidence plus Release mode, clean tracked sources, clean runtime provenance, and
matching engine headers). The `-RuntimeSource` argument is mandatory: the workflow installs that merged runtime into the
project before any Unity generation step and verifies the complete staged runtime tree and patch lock. The final Player is
compiled from the baseline AOT DLL;
the current DLL is loaded only as the differential image.

The demo report declares `assemblyScope.strategy=multi-assembly-dhe` and records
the exact four-assembly set (`ManagedCasesAot`, `ManagedCases`, `MetadataStress`,
and `CrossAssemblyDerived`) in both the project plan and Player result. The
runtime asset plan is staged beside the per-assembly current/MV/snapshot assets;
the archive handoff plan carries the corresponding current/baseline/MV/snapshot
files, so a missing or extra image fails before gameplay execution.
The demo's `Assets/Plugins/HybridCLRLab` directory may also contain the
compile-only `HybridCLR.BoundaryContracts` reference used by the editor/player
scripts; it is refreshed separately and is intentionally excluded from the
DHE/hot-update assembly plan.

The workflow stages the supplied AOT DLL into `Assets/Plugins` before Unity
Linker/IL2CPP runs. Both baseline and current metadata files used by the
Player are the corresponding stripped outputs; using a raw current DLL makes
the runtime reject the package with `DHE_MV_CURRENT_HASH_MISMATCH`.

At the current capability level the primary capability fixture reports 24
changed managed methods. The complete four-assembly workflow reports 27/27
supported managed tokens and 34 generated native entries; one managed generic
token may legitimately map to several concrete/gshared IL2CPP entries.
The Player also reports `secondaryAssemblyChangedValidated=true` and
`secondaryAssemblyDirectValidated=true`, proving that the secondary current bodies
were reached through direct generated AOT entries rather than reflection alone.
The verified value-type async state-machine receiver uses the dedicated
`void()` helper. Direct/reflection generic calls, null references, and generic
virtual dispatch are also covered. `-Mode Exploratory` is retained for dirty
tool development, but it can never produce `releaseReady=true` or a passing
release gate even when native coverage is complete.

The runtime patch set is pinned in `manifests/dhe-runtime-lock.json`, and the
embedded Unity package is pinned by `manifests/dhe-package-lock.json` (including
its full tree hash and applied patch IDs). A clean checkout can be assembled with
`scripts/assemble-runtime.ps1`; no dirty sibling worktree is required. The demo also verifies the embedded Player snapshot hash
through `HybridCLRDheBuildIdentity`, so a replaceable `StreamingAssets` snapshot
cannot satisfy the gate by itself.
Use the isolated `DHE-Tuanjie2022` runtime profile for this lane; the ordinary
`Baseline-Clean` profile intentionally contains no DHE patch and is only a
control build. identity v2 also records patched generated-C++ and native-manifest
hashes, while the final Player gate remains the authoritative dispatch check.

The archived outputs are versioned by the contracts in `schemas/dhe-*.schema.json`.
`scripts/run-dhe-schema-gate.ps1` performs strict duplicate-property-aware JSON
parsing and draft 2020-12 validation for every recognized DHE document. It runs
natively under PowerShell 7 and delegates there when invoked from Windows
PowerShell 5.1. CI scans the complete outputs of the static, capability, and
compatibility-negative gates; the resulting `schema-gate-report.json` is the
validation evidence, not the presence of schema files alone.
Run `scripts/validate-dhe-artifacts.ps1` after copying an output directory or
before publishing it; pass the actual baseline/current DLL paths when
available. The validator then re-computes both SHA-256 values in addition to
checking the binary token set and the exact supported+unsupported native coverage
set. Every changed MV token must appear exactly once in one of those two lists,
and all report cross-references are checked independently of Unity.
The native manifest also carries resolver v2 and the versioned
`il2cpp-generated-cpp-signature-v2` adapter contract; an unknown resolver or ABI
contract is rejected rather than interpreted optimistically.

For a portable handoff, create a self-contained archive instead of copying the
workflow directory manually. The archive includes the exact generated C++ files,
all MV/DLL/runtime-plan payloads, rewritten relative paths, and a file inventory:

```powershell
./scripts/run-dhe-archive-gate.ps1 `
  -InputRoot ./artifacts/dhe-demo-workflow `
  -ArchiveRoot ./artifacts/dhe-demo-workflow-archive `
  -LabRoot . `
  -ForceOutput
```

The gate validates every archive file and then copies the archive to a fresh
system temporary directory. It runs the independent artifact validator from a
different current directory, so an archive pass does not depend on the original
`Library/Bee` path. `archive-manifest.json` and the companion `*.gate.json`
report are the handoff records. The archive gate remains an artifact check only;
it does not turn an incomplete native ABI set into a release pass.

### Compatibility negative gate

`scripts/run-dhe-compatibility-negative-gate.ps1` builds isolated variants and
requires strict MV generation to reject each one. The current report is
`artifacts/dhe-compatibility-negative-gate-final/compatibility-negative-gate-report.json`:

```text
token drift:       rejected, 2 reasons
method addition:   rejected, 69 reasons (including method/type addition)
type layout change: rejected, 1 reason
```

This is an intentional safety boundary. Signature mapping, stable token
mapping, and a full metadata-version format are still required before method
addition/removal, token drift, or type layout changes can be enabled.

### Reference project profile

The inspected `cat` project has 16 configured hotfix assemblies and the
following aggregate capability counts:

```text
methods=63,223; virtual=15,515; generic=588; async=1,343
delegate types=104; value-type methods=3,628; interfaces=206; P/Invoke=0
```

The extended demo covers the corresponding execution categories that can be
validated without the production project's full asset graph. A production
cutover still needs per-platform stripped artifacts, generic-declaring-type
ABI coverage, reverse P/Invoke coverage where used, and a full all-assembly
Player build. Until those gates pass, the existing BattleAOT branch remains
the production fallback.

## Example project preflight

For one reference project, using an available Android compile output as a
preflight baseline and `Library/ScriptAssemblies` as current output, all 16
configured hotfix DLLs were present. Seven passed the current strict
method-body-only gate and nine required token/signature or type-layout
handling. This is only an artifact compatibility result, not a project-specific
design rule or a player performance conclusion.
