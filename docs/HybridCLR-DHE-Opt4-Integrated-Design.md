# HybridCLR DHE opt4 Integrated Design

This document defines the first formal opt4 integration of the community DHE
workflow. The earlier `dhe-toolchain-0.1.1` package remains a historical workflow proof;
this opt4 line binds the same behavior to the opt3 maintenance sources.

## Runtime boundary

The DHE runtime is an additive capability on top of the opt3 metadata and FGS
implementation:

- `hybridclr@9e4fc7219724a3c63ff845e0b2acaa7d10c2430b` is the formal
  `optimize/v8.13.0` commit and annotated tag `v8.13.0-opt4`.
- `hybridclr_unity@a383efee93c984a9700a08b6b4272b973b62c41f` is the formal
  `optimize/v8.13.0` package commit. The package does not receive an opt tag.
- `il2cpp_plus` has no DHE source change in this round and continues to use the
  three opt3 engine tags.

The native runtime keeps every hot-update assembly's baseline image in the
player. At runtime, the strict MV loader validates the current DLL hash, the
baseline AOT snapshot hash, assembly identity, and the changed method token set.
Only methods listed by MV are prepared for the interpreter. Unchanged methods
remain on their generated AOT entry and use the bridge only when an interpreter
call graph needs to cross the boundary.

## Source lock modes

`manifests/dhe-runtime-lock.json` supports two mutually exclusive modes:

- `overlay` applies the historical patch files to a clean opt3 base commit. It
  is retained for reproducing the original proof package.
- `integrated` verifies the repository's exact integrated commit and the
  content tree hash, then refuses to apply any patch. The lock still retains
  patch paths and hashes as migration/audit references.

The distributed DHE package does not apply source patches implicitly. Runtime
source assembly and native CTest remain explicit gates driven by the C# host;
the cross-platform package entry point is the C# host described below. The
package lock still binds the embedded package tree to the integrated
`hybridclr_unity` commit.

## C# runtime assembly

Runtime and native proof are driven by the same C# host as the project
workflow. Runtime assembly is target-bound and uses an integrated runtime
checkout; no shell patcher is involved:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime \
  -LabRoot . -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs \
  -Il2CppPlusSource ../worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests \
  -LabRoot . -Profile DHE-Tuanjie2022
```

`DHE-Unity2021` and `DHE-Unity2022` use the same commands with their matching
engine workflow and `-Il2CppPlusSource`. A machine without the matching Editor
must pass `-AllowSurrogateExternalHeaders`; that native report is exploratory
evidence only. Overlay patch application is intentionally not implicit: use a
repository checkout at the locked integrated commit.

## C# project workflow

After a target-bound previous-release stripped-AOT root is available, a project
provides a C# adapter with `Prepare`, `StageRuntimePlan`,
`BuildDheYooAsset`, `BuildScriptsOnly`, and `BuildFinalPlayer`. Run the
cross-platform host directly:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj --configuration Release --no-restore -- workflow \
  -ProjectPath C:/project \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/dhe \
  -BaselineAotRoot C:/release/stripped-aot \
  -Target Android \
  -AdapterMethod MyGame.Editor.DheWorkflowBuild.Prepare \
  -Unity /path/to/Unity -RunPlayer
```

The host starts Unity with .NET `ProcessStartInfo`, writes separate Unity and
process logs, and performs MV/batch/preflight before any Player stage. Resource
packaging, signing, device smoke, and generated-native evidence remain explicit
C# adapter responsibilities.

## Required lab gates

The supported DHE entry point is the C# host shown above. It performs strict
MV/batch/preflight validation, invokes the Unity C# adapter, and emits the
machine-readable workflow evidence. Runtime assembly, native CMake/CTest,
signing, and device smoke remain explicit platform-owned prerequisites; they
must be invoked by a project adapter or CI job that writes the corresponding
JSON evidence before a Release package is published. No DHE gate requires a
PowerShell script.

The current verification is conditional: Tuanjie 2022 and Unity 2021 native
compilation/CTest pass, and Unity 2022 native compilation passes with surrogate
headers because Unity 2022.3.63f1 is not installed on this machine. No Android,
Unity 2022 Player, or production-project result is implied by this source
integration.

## Rollback

Rollback is a source identity change, not a patch toggle. Restore the package
commit/version selection and the runtime/repo locks to the previous opt3
identities, reassemble the runtime, run `Generate/All`, and rebuild the Player.
Do not leave an opt4 package next to an opt3 runtime or reuse a generated
`Library`, `LocalIl2CppData`, or Player build from the other identity.
