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
  -LabRoot . -Profile DHE-Tuanjie2022
```

These commands use direct .NET process execution and fail closed when an
external prerequisite (Unity, CMake, compiler, git, or dotnet) is missing.

## Project Configuration

The host is project-independent and does not select a Demo adapter implicitly.
Generate the Unity C# adapter and configuration, then customize only the
project-owned resource, signing, and smoke policies:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-adapter \
  -Root . -Namespace MyGame.Editor \
  -Output C:/project/Assets/Editor/DHE/DheWorkflowBuild.cs
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-config \
  -Output C:/project/Assets/Editor/DHE/dhe-workflow-config.json
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -Config C:/project/Assets/Editor/DHE/dhe-workflow-config.json
```

The generated adapter is a compilable StreamingAssets implementation, not a
stub. It calls every package-owned DHE phase and writes the required structured
evidence. A YooAsset/Addressables project replaces its asset root/resolver and
resource evidence; a production project also supplies signing and runtime smoke
logic. Paths in the config are resolved relative to the config file; explicit
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

## JSON contract gates

Validate one document or every registered DHE report below an output root with
the same host distributed to the project:

```text
dotnet HybridCLR.DheTool.dll schema-validate \
  -Schema C:/tools/HybridCLRDhe/schemas/dhe-workflow-config.schema.json \
  -Document C:/project/Assets/Editor/DHE/dhe-workflow-config.json \
  -Output C:/build/dhe/config-schema-validation.json
dotnet HybridCLR.DheTool.dll schema-gate \
  -SchemasRoot C:/tools/HybridCLRDhe/schemas \
  -InputRoot C:/build/dhe \
  -Output C:/build/dhe-schema-gate.json \
  -RequireKnownFormats
```

The gate builds its format registry from the distributed schemas, rejects an
unsupported assertion keyword instead of silently ignoring it, and validates
its own evidence document before returning success.

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
  -PackageLockPath C:/project/Assets/Editor/DHE/dhe-package-lock.json \
  -SourceBoundaryPath C:/project/Assets/Editor/DHE/dhe-source-boundary.json \
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
It requires equal hot-update/DHE assembly sets, strict method-body-only diffs,
complete native guard coverage, a target-bound previous baseline, clean
source/package/runtime provenance, and a real Player smoke result. A no-op
release is valid when all changed/interpreter/native counts are zero and the
transaction status is `notApplicable`.

Publishing the toolchain itself in `-Mode Release` requires
`-ReleaseEvidence <report>`. That report must match the exact source HEAD/tree
and bind passing `regression`, `demo-changed`, `demo-noop`, and `native` JSON
reports by SHA-256. A command-line readiness flag cannot promote a package.
Native compilation and iOS/Xcode execution remain target environment gates;
a Windows result is not iOS evidence.
