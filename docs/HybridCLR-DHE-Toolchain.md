# HybridCLR DHE Toolchain

The DHE workflow is a cross-platform .NET host plus a Unity C# project adapter.
The same host runs on Windows, macOS, and Linux; Unity-specific compilation
remains in the embedded HybridCLR package and the project adapter.

## Layout

`tool/HybridCLR.DheTool.csproj` is the entry point. `tool/Program.cs` owns the
CLI and project orchestration; `tool/ProductionGates.cs` owns source, checkout,
artifact, release, archive, and regression validation. `tool/dnlib.dll` is the
pinned assembly inspection dependency. JSON schemas are the report contract.

The current DHE lab commands include `assemble-runtime`, `native-tests`, `build-managed-cases`,
`generate-test-manifest`, `generate-metadata-stress-source`, `reference`,
`compare-results`, `check-environment`, `prepare-engine-test-project`,
`bootstrap-repos`, `clear-unity-project-locks`, and `wait-editor`. For example:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime \
  -LabRoot . -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs \
  -Il2CppPlusSource C:/repos/il2cpp_plus
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests \
  -LabRoot . -Profile DHE-Tuanjie2022 \
  -RuntimeRoot C:/build/runtime -OutputRoot C:/build/native-tests
```

These commands use direct .NET process execution and fail closed when an
external prerequisite (Unity, CMake, compiler, git, or dotnet) is missing.

Prepare every locked engine-specific `il2cpp_plus` checkout without manually
creating worktrees:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- bootstrap-repos \
  -LabRoot . -ReposRoot C:/repos -AllEngineWorkflows
```

This keeps the shared `hybridclr` and `hybridclr_unity` checkouts at their
locked commits and creates `il2cpp_plus-<engine-workflow>` worktrees for Unity
2021, Unity 2022, and Tuanjie 2022. `assemble-runtime` automatically selects
the matching worktree. Use `-EngineWorkflow <id>` to prepare only one lane.
Integrated source identity hashes normalize Git's CRLF checkout conversion,
so an equivalent clean checkout does not depend on `core.autocrlf`; binary
bytes remain exact.

## Project Configuration

The host is project-independent and does not select a Demo adapter implicitly.
Generate the Unity C# adapter and configuration, then customize only the
project-owned resource, signing, and smoke policies:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-adapter \
  -Root . -Namespace MyGame.Editor \
  -Output C:/project/Assets/Scripts/Editor/DHE/DheWorkflowBuild.cs
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-config \
  -Output C:/project/ProjectSettings/DHE/dhe-workflow-config.json
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -Config C:/project/ProjectSettings/DHE/dhe-workflow-config.json
```

The generated adapter is a compilable, fail-closed scaffold. It calls every
package-owned DHE phase, but `BuildDheYooAsset` deliberately throws until the
project implements its resource/catalog build and writes verified structured
evidence. A YooAsset/Addressables project also replaces its asset root/resolver;
a production project supplies signing and runtime smoke logic. Paths in the
config are resolved relative to the config file; explicit
command line values override config values. `unityArguments` is a scalar map for
project-owned Unity adapter options (for example `dhePreview`,
`dheStandalone`, or a target-specific fallback metadata root). The host still
owns the reserved DHE paths and stage ordering. The config contract is
`schemas/dhe-workflow-config.schema.json`.

## Build and run

Build once on the build host:

```text
dotnet build tool/HybridCLR.DheTool.csproj --configuration Release
```

Run commands with the produced project (or use `dotnet run` during development):

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- version -Root C:/tools/HybridCLRDhe
dotnet run --project tool/HybridCLR.DheTool.csproj -- mv \
  -BaselineAssembly C:/release/Assembly-CSharp.dll \
  -CurrentAssembly C:/build/Assembly-CSharp.dll \
  -Output C:/build/Assembly-CSharp.mv.json \
  -BinaryOutput C:/build/Assembly-CSharp.mv.bytes \
  -StrictCompatibility
dotnet run --project tool/HybridCLR.DheTool.csproj -- batch \
  -BaselineRoot C:/release/stripped-aot \
  -CurrentRoot C:/build/stripped-aot \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/dhe/batch
```

All paths are explicit. Output paths are checked against the input roots and
are never allowed to overwrite an assembly or settings file.

## Fixed Base and resource-only updates

Build a Base Player once with the project workflow's `-Bootstrap -RunPlayer`
mode. The Player embeds one Base MetaVersion per configured hot-update assembly and a
build identity; the scripts-only stage emits universal native guards before
the final Player is compiled. Archive the Base DLL root,
`build-identity.json`, and `dhe-native-manifest.json` for every Player version
that remains online.

For later releases, compile one current hot-update DLL set and validate it
against all supported Base Players in one command:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRoots C:/release/base-100/dlls,C:/release/base-110/dlls \
  -BaseNativeManifests C:/release/base-100/dhe-native-manifest.json,C:/release/base-110/dhe-native-manifest.json \
  -BaseBuildIdentities C:/release/base-100/build-identity.json,C:/release/base-110/build-identity.json \
  -AotMetadataRoot C:/build/stripped-aot \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-update
```

The output contains one copy of each current DLL and current MetaVersion. Base inputs
are compatibility evidence only and are never copied into `payload/`. At
runtime each Player compares the remote current MetaVersion with its own embedded Base
MetaVersion, so different Base versions may produce different changed-method sets from
the same payload. If any declared Base is incompatible, the command removes
the publish manifests and fails instead of producing a partial release.
When supplemental AOT metadata is required, `-AotMetadataRoot` adds the complete
`patchAOTAssemblies` set to the same payload. Projects that have separately proved a
no-supplemental-metadata workflow may omit it.

Use `stage-resource-update` from the resource/catalog build to copy this one
payload. Supply the exact archived Base identity with `-BaseBuildIdentity`; the
command uses its composite `baseId` to select one supported Base and proves that
the identity file, embedded Base MetaVersion, and optional Player binaries were
not changed. Two Players may therefore share an identical Base MetaVersion set
while retaining different runtime/native identities. It verifies the manifest-bound
runtime plan and every current DLL, MetaVersion, and supplemental AOT metadata hash
before copying. The complete lifecycle and current compatibility subset are in
`docs/HybridCLR-DHE-Resource-Only-Design.md`.

Run `resource-player-evidence` after the real Player/device smoke. It binds the
resource manifest, stage report, Player result, and immutable Base workflow into
`resource-player-workflow-report.json`. This is the only supported
`demo-changed` input for toolchain release evidence; a non-bootstrap changed
Player build is intentionally rejected because online Base Players require
universal guards.

## JSON contract gates

Validate one document or every registered DHE report below an output root with
the same host distributed to the project:

```text
dotnet HybridCLR.DheTool.dll schema-validate \
  -Schema C:/tools/HybridCLRDhe/schemas/dhe-workflow-config.schema.json \
  -Document C:/project/ProjectSettings/DHE/dhe-workflow-config.json \
  -Output C:/build/dhe/config-schema-validation.json
dotnet HybridCLR.DheTool.dll schema-gate \
  -SchemasRoot C:/tools/HybridCLRDhe/schemas \
  -InputRoot C:/build/dhe \
  -Output C:/build/dhe-schema-gate.json \
  -RequireKnownFormats
```

The gate builds its format registry from the distributed schemas, rejects an
unsupported assertion keyword instead of silently ignoring it, and validates
its own evidence document before returning success. `workflow` invokes this
gate over its complete output root before returning success, including when it
stops after preflight.

## Project workflow

The project provides a C# class containing these Unity execute-methods:

- `Prepare`: call `DheBuildPipeline.PrepareProjectArtifacts` to regenerate the
  current stripped-AOT image and stage complete baseline/current assembly sets,
  then write `adapter/prepare.json`.
- `StageRuntimePlan`: call `DheBuildPipeline.StageRuntimePlan` with an explicit
  `Target`, complete hotfix load list, AOT metadata roots, and project resource
  callbacks. `RuntimeAssetPathResolver` maps staged files to a YooAsset,
  Addressables, or other catalog locator; its default is StreamingAssets.
- `BuildScriptsOnly` and `BuildFinalPlayer`: call
  `DheBuildPipeline.BuildPlayer` with the project build callback.
- `BuildScriptsOnly`: call `DheBuildPipeline.FinalizeProjectNativeCode` with
  `RebuildPlayer=false` after the clean generated-C++ pass and record its guard
  evidence.
- `BuildFinalPlayer`: set `DhePlayerBuildOptions.NativeFinalizeOptions`. The
  package resolves the final generated-C++ root, injects all MV guards, rebuilds
  the editor-owned Bee graph, and invokes `NativeFinalizeResultCallback` before
  temporary baseline assembly inputs are restored.

The host starts Unity directly with `ProcessStartInfo`.
The final-player phase binds `HYBRIDCLR_DHE_AOT_BASELINE_ROOT`, while
current-generation clears that variable and always regenerates the current
stripped image. This is the key correctness boundary for baseline/current
token identity.

The host's `workflow` command performs Prepare, C# MV batch, and project
preflight. Pass `-RunPlayer` to continue in the same C#-orchestrated flow with
the adapter's `StageRuntimePlan`, `BuildDheYooAsset`, `BuildScriptsOnly`, and
`BuildFinalPlayer` methods. Each Unity invocation is started directly by
`ProcessStartInfo`, reads stdout/stderr concurrently, has a bounded
`-UnityTimeoutSeconds` (default 600), and writes logs below the output root.
A project contract test can pass `-StopAfterPreflight` after Prepare and strict
MV validation; this exits before runtime-plan/resource/Player stages while
still leaving all preflight reports on disk.
A project adapter owns resource catalog integration, platform signing, and
device smoke callbacks. Generated-C++ discovery, native guard injection, and
Bee graph evaluation are package C# APIs and are mandatory for a DHE Player.
Those stages must
produce the existing JSON evidence before a build is called Release-ready; a
compiled Player without changed/unchanged dispatch evidence is intentionally
rejected.

Native manifest resolver version 3 uses `guard-block-set-v1`. Every method owns
one begin/end delimited guard block. The package rejects missing, duplicate, or
non-canonical blocks, while the independent release gate hashes the same blocks
in path/function/token order. Unrelated generated C++ bytes are provenance but
are deliberately outside this identity, so compiling the embedded build
identity cannot create a self-referential hash.

For a project adapter named `MyGame.Editor.DheWorkflowBuild`, invoke the same
host on Windows or macOS with an explicit method name:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -ProjectPath C:/project \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/dhe \
  -BaselineAotRoot C:/release/stripped-aot \
  -BaselineManifestPath C:/release/stripped-aot/dhe-baseline-manifest.json \
  -RuntimeManifestPath C:/release/runtime/runtime-manifest.json \
  -PackageLockPath C:/project/ProjectSettings/DHE/dhe-package-lock.json \
  -SourceBoundaryPath C:/project/ProjectSettings/DHE/dhe-source-boundary.json \
  -ToolchainRoot C:/project/Tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <64-hex-package-id> \
  -ArchiveRoot C:/build/dhe-archive \
  -Target Android \
  -AdapterMethod MyGame.Editor.DheWorkflowBuild.Prepare \
  -Unity /path/to/Unity -Mode Release -RunPlayer
```

Use `-Mode Exploratory -StopAfterPreflight` while first integrating an adapter.
Exploratory runs may omit runtime/baseline manifests and may use a dirty project,
but they never produce `releaseReady=true`. A Release run requires every input
shown above and rechecks source/package identity both before and after Unity.

The final reports are:

- `project-workflow-report.json`: complete orchestrator stages and paths.
- `player-workflow-report.json`: Player, MV, native guard, identity, and runtime
  evidence consumed by `release-gate`.
- `release-gate.json` and `release-gate.artifact-validation.json`: independent
  live revalidation of DLLs, MV JSON/binary, runtime plan, native manifest,
  resource evidence, and Player results.
- `archive-gate.json` plus `dhe-archive-manifest.json`: hash-verified portable
  evidence. The archive excludes the large Player backup directory and includes
  only referenced generated C++ guard sources.

## Android and iOS

The host is platform-neutral. Unity Editor is the platform-specific build
dependency: Android requires the Android module/SDK, and iOS requires macOS,
the iOS module, Xcode, signing, and a project-owned runner. The package locates
the editor-owned `bee_backend` executable directly on Windows or macOS and
invokes it through .NET; no shell executable is involved. The same C# host and
adapter code is used on both platforms; only the explicit Unity executable,
target, output path, and runner configuration differ. Windows cannot provide
evidence for an iOS Xcode/device build.

## Baseline manifest

Create a target-bound previous-release manifest with the C# command:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- baseline-manifest \
  -BaselineRoot C:/release/stripped-aot/Android \
  -RuntimeManifestPath C:/runtime/runtime-manifest.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -Target Android \
  -Output C:/release/stripped-aot/Android/dhe-baseline-manifest.json
```

The manifest binds target, engine/runtime identity, package lock, and every
baseline assembly SHA-256. A Release update must supply this manifest; an
exploratory bootstrap may use current artifacts for both sides but cannot be
published.

## Package and project locks

The package lock must point to the actual embedded directory, including Unity
version suffixes such as `Packages/com.code-philosophy.hybridclr@8.13.0`.
`dhe-toolchain-layout.json` and the generated toolchain manifest authenticate
the C# host source, schemas, patches, and pinned dnlib bytes. The installed
tool directory should be outside an SVN working copy or explicitly ignored.

## Release boundary

Project Release readiness is derived only from passing source, clean checkout,
project preflight, Player, independent artifact, release, and archive gates.
It requires equal hot-update/DHE assembly sets, the MetaVersion proven-safe
compatibility subset, complete native guard coverage, target-bound Base
identities, clean source/package/runtime provenance, and a real Player smoke
result. The accepted subset includes method-body changes, supported additions
and removals of types, methods and fields, method signature replacement,
logical property/event evolution, custom attributes, and constrained reference
type sidecar fields. Existing value-type layout, inheritance/interface/vtable,
unsupported field shapes, and unsupported declaration changes fail closed. A no-op
release is valid when all changed/interpreter/native counts are zero and the
transaction status is `notApplicable`. The Player must also set
`noOpAotBehaviorValidated=true` after checking baseline results, the complete
multi-assembly scope, direct/reflection capability probes, and the absence of
interpreter dispatch.

Publishing the toolchain itself in `-Mode Release` requires
`-ReleaseEvidence <report>`. Generate it from a clean source identity with the
same host:

```text
dotnet HybridCLR.DheTool.dll release-evidence \
  -LabRoot C:/src/hybridclr-lab \
  -OutputRoot C:/build/dhe-release-evidence \
  -Regression C:/build/regression.json \
  -DemoChanged C:/build/demo-changed/resource-player-workflow-report.json \
  -DemoChangedBase2 C:/build/demo-changed-base2/resource-player-workflow-report.json \
  -DemoNoop C:/build/demo-noop/player-workflow-report.json \
  -NativeTuanjie2022 C:/build/native-tuanjie/native-gate.json \
  -NativeUnity2022 C:/build/native-unity2022/native-gate.json \
  -NativeUnity2021 C:/build/native-unity2021/native-gate.json
```

The generated report must match the exact source HEAD/tree and binds all seven
reports by SHA-256. Both changed roles must be Release-ready Player results for
distinct Base identities, backed by the same resource manifest, validation,
current assembly set, and target. Each managed role is rebound to its integrated
runtime manifest, clean tracked sources, real Editor headers, and the authenticated
release toolchain that executed it. This toolchain may be the preceding release;
the clean current host independently revalidates the complete evidence to avoid a
circular self-publication dependency. Each native role is revalidated against its own locked
runtime workflow, source commits, live runtime tree, and real Editor header
tree. A command-line readiness flag cannot promote a package.
Native compilation and iOS/Xcode execution remain target environment gates;
a Windows result is not iOS evidence.
