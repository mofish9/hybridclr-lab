# DHE Formal Project Validation

The supported project workflow is the C# host in
`tool/HybridCLR.DheTool.csproj` plus a Unity C# adapter. This document is the
validation contract for a new project; it does not require PowerShell.

## Required inputs

- `ProjectSettings/HybridCLRSettings.asset` with explicit
  `hotUpdateAssemblies` and `dheAotAssemblies`.
- An embedded HybridCLR package whose path and commit are recorded in the
  project package lock. Versioned paths such as
  `Packages/com.code-philosophy.hybridclr@8.13.0` are valid.
- A target-bound previous-release stripped-AOT root and
  `dhe-baseline-manifest.json` for Release. Bootstrap/current-as-both-sides is
  exploratory evidence only.
- A staged runtime manifest bound to the same engine, runtime patch, package
  tree, target, and external headers.
- A C# adapter exposing `Prepare`, `StageRuntimePlan`, `BuildDheYooAsset`,
  `BuildScriptsOnly`, and `BuildFinalPlayer` Unity execute-methods. A project
  without YooAsset still implements `BuildDheYooAsset` and writes structured
  `policy=skip` resource evidence. Signing and device smoke remain project
  callbacks.

## Offline validation

Build the host and run the C# preflight against baseline/current assemblies:

```text
dotnet build HybridCLR.Lab.sln --configuration Release
dotnet run --project tool/HybridCLR.DheTool.csproj -- preflight \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -BaselineRoot C:/release/stripped-aot/StandaloneWindows64 \
  -CurrentRoot C:/build/stripped-aot/StandaloneWindows64 \
  -ProjectRoot C:/project \
  -OutputRoot C:/build/dhe/project-preflight \
  -RequireDheEqualsHotUpdate -RequireCompleteCoverage
```

The command emits `dhe-batch-summary.json`, one strict MV JSON and binary per
assembly, `dhe-project-plan.json`,
`project-plan-validation.json`, and `project-preflight-report.json`. A
preflight pass proves assembly scope, token stability, method-body-only
compatibility, and artifact identity. It does not prove native ABI coverage
or Player runtime behavior.

## Unity phases

The host calls the adapter's `Prepare` with `DHE_BASELINE_ROOT` set to the
previous release. The adapter must call
`DheBuildPipeline.GenerateCurrentArtifacts(target)`, which clears inherited
baseline state and regenerates the current stripped image. The final Player
must call `DheBuildPipeline.BuildPlayer` with the same target and baseline
root; the package binds `HYBRIDCLR_DHE_AOT_BASELINE_ROOT` only for that phase.
Every Unity invocation must pass an explicit target. Do not rely on the active
Editor target when staging runtime plans or fallback AOT metadata.

## Runtime and resource evidence

The runtime plan must contain each DHE assembly's current DLL, MV binary,
baseline snapshot, and AOT metadata records. A resource policy of `required`
must produce structured YooAsset evidence. A policy of `skip` must produce a
structured alternate resource-evidence document with target and strategy;
absence of that document is a Release failure.

The Player result must demonstrate all of the following for the real target:

1. Every planned assembly loads successfully with its snapshot hash.
2. At least one changed method executes through the interpreter.
3. An unchanged method executes through its native AOT entry.
4. Transaction rollback and retry return the expected registration failure and
   then succeed.
5. Native guard coverage and managed MV counts agree.

## Platform matrix

Android requires the Android Unity module, SDK/NDK, and a device or emulator
runner. iOS requires macOS, the Unity iOS module, Xcode, signing, and a
project-owned device runner. The C# host is shared; only the Unity executable,
target, output, signing, and smoke runner are platform inputs. A Windows run
cannot be reported as iOS evidence.

## Release gate

Release is publishable only when the C# host, package lock, runtime manifest,
baseline manifest, project plan, Player identity, native manifest, resource
evidence, and smoke result all refer to the same target and source identity.
The host fails closed on missing or mismatched evidence. Keep generated
`artifacts/`, `Library/`, and `bin/obj` output out of source commits.
