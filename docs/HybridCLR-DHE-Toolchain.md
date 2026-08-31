# HybridCLR DHE Toolchain

The DHE workflow is implemented in C# and runs as a normal cross-platform
.NET process. The distributed tool has no PowerShell dependency and can run on
Windows, macOS, or Linux. Unity-specific compilation remains in the embedded
HybridCLR package and the project's C# adapter.

## Layout

`tool/HybridCLR.DheTool.csproj` is the entry point. `tool/Program.cs` owns
settings parsing, dnlib assembly inspection, MV generation, batch/project-plan
creation, baseline manifests, artifact checks, archive copying, and Unity
process orchestration. `tool/dnlib.dll` is the pinned dependency used by both
the host and the Unity package. JSON schemas remain the compatibility contract.

The package does not contain a shell script or a PowerShell script. Other lab
experiments used to have platform-specific helpers; those helpers have been
removed from this branch as well. The repository's lab-only operations are
available as C# host commands, so a macOS/iOS build machine does not need
PowerShell.

The current DHE lab commands are intentionally explicit and replace the
helpers that participate in this workflow: `assemble-runtime`, `native-tests`, `build-managed-cases`,
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
Create a configuration with `new-config`, then set the project's adapter method
and target-specific paths:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-config \
  -Output C:/project/Assets/Editor/DHE/dhe-workflow-config.json
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -Config C:/project/Assets/Editor/DHE/dhe-workflow-config.json
```

Paths in the config are resolved relative to the config file; explicit command
line values override config values. `unityArguments` is a scalar map for
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

## Project workflow

The project provides a C# class containing these Unity execute-methods:

- `Prepare`: call `DheBuildPipeline.GenerateCurrentArtifacts(target)` and
  write `adapter/prepare.json`.
- `StageRuntimePlan`: call the package API with an explicit `Target`, complete
  hotfix load list, AOT metadata roots, and project resource callbacks.
- `BuildScriptsOnly` and `BuildFinalPlayer`: call
  `DheBuildPipeline.BuildPlayer` with the project build callback.
- `BuildFinalPlayer`: supply `GeneratedCppFinalizeCallback`; inside that
  callback resolve the final generated-C++ root, call
  `DheBuildPipeline.InjectGeneratedGuards`, then call
  `DheBuildPipeline.RebuildPlayerFromGeneratedCpp`. The callback runs before
  the package restores temporary baseline assembly inputs, preventing Bee from
  invalidating UnityLinker/IL2CPP and overwriting the guards.

The host starts Unity directly with `ProcessStartInfo`; no shell is involved.
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
A project adapter owns MV-to-generated-C++ root discovery, platform signing,
and device smoke callbacks. Native guard injection and Bee graph evaluation
are package C# APIs and are mandatory for a DHE Player. Those stages must
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
  -Target Android \
  -AdapterMethod MyGame.Editor.DheWorkflowBuild.Prepare \
  -Unity /path/to/Unity -RunPlayer
```

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

Release publication additionally requires an explicit `-ReleaseReady` switch
after the native, Player, archive, and source gates have passed; the host never
infers release readiness from a successful copy alone. Release requires equal hot-update/DHE assembly sets, compatible method-body
diffs, complete native guard coverage, a target-bound previous baseline, clean
source/package/runtime provenance, portable archive evidence, and a real
Player smoke result. The C# host fails closed on missing evidence. Native
compilation and iOS/Xcode execution remain environment gates, not claims made
by a Windows-only run.
