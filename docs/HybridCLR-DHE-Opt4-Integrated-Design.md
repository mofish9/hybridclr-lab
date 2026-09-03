# HybridCLR DHE opt4 Integrated Design

This document defines the formal opt4 integration of the community DHE
workflow on top of the cumulative opt3 maintenance sources.

## Runtime boundary

The DHE runtime is an additive capability on top of the opt3 metadata and FGS
implementation. The formal opt4.1 source identities are:

- `hybridclr@f777ed77eafd3eaa7262115c0c135f7418f83b74`, annotated tag
  `v8.13.0-opt4.1`;
- `il2cpp_plus@b3fdf1ef70b63758dc6598c674ffb38f3534c4e6`, annotated tag
  `v2021-8.1.0-opt4.1` for Unity 2021;
- `il2cpp_plus@60322744721410e79203155fc455be4232c3df4b`, annotated tag
  `v2022-8.11.0-opt4.1` for Unity 2022;
- `il2cpp_plus@52968ad6c88416f212d09d919b9a1b6afdc8a53b`, annotated tag
  `v2022-tuanjie-8.13.0-opt4.1` for Tuanjie 2022;
- `hybridclr_unity@71a2e7b11e46119c649437d5afec02f2f2b82197` on
  `optimize/v8.13.0`. The package does not receive an opt tag.

The old base commits and patches remain in the locks as replay and migration
evidence. They are not the source identities used to build the integrated
runtime.

The native runtime keeps every hot-update assembly's baseline image in the
player. At runtime, the strict MV loader validates the current DLL hash, the
baseline AOT snapshot hash, assembly identity, and the changed method token set.
Only methods listed by MV are prepared for the interpreter. Unchanged methods
remain on their generated AOT entry and use the bridge only when an interpreter
call graph needs to cross the boundary.

## Source lock modes

`manifests/dhe-runtime-lock.json` supports two mutually exclusive modes:

- `overlay` verifies and applies the locked patch files to temporary runtime or
  package staging copied from clean base commits. The C# host invokes Git
  directly and never mutates the source checkout.
- `integrated` verifies the repository's exact integrated commit and the
  content tree hash, then refuses to apply any patch. The lock still retains
  patch paths and hashes as migration/audit references.

The formal opt4.1 lock uses integrated mode. Runtime assembly and engine-test
project preparation verify each repository's integrated commit, selected
engine record, content-tree SHA-256, and retained audit-patch SHA-256. They do
not apply a patch. Release preflight rejects an overlay package or runtime even
if its resulting files happen to match.

## C# runtime assembly

Runtime and native proof are driven by the same C# host as the project
workflow. Runtime assembly is target-bound, starts from clean locked integrated
commits, and verifies the engine-specific and common source trees before native
compilation. No shell script or source-checkout mutation is involved:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime \
  -LabRoot . -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs \
  -HybridClrSource ../repos/hybridclr \
  -HybridClrUnitySource ../repos/hybridclr_unity \
  -Il2CppPlusSource ../worktrees/dhe-opt4-il2cpp-tuanjie
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests \
  -LabRoot . -Profile DHE-Tuanjie2022
```

`DHE-Unity2021` and `DHE-Unity2022` use the same commands with their matching
engine workflow and `-Il2CppPlusSource`. The host selects exactly one
engine-specific integrated record from `dhe-runtime-lock.json` and records the
verified audit records in the runtime manifest. A machine without the matching Editor must
pass `-AllowSurrogateExternalHeaders`; that native report is exploratory
evidence only.

## C# project workflow

After a target-bound previous-release stripped-AOT root is available, generate
the compilable, fail-closed adapter template with `new-adapter`, then implement
its resource evidence and customize signing and smoke callbacks. The adapter exposes `Prepare`, `StageRuntimePlan`,
`BuildDheYooAsset`, `BuildScriptsOnly`, and `BuildFinalPlayer`. Package APIs own
the stripped-AOT preparation, runtime-plan staging, generated-C++ discovery,
guard injection, and Bee rebuild. Run the cross-platform host directly:

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

The release contract requires separate real-header CMake/CTest evidence for
Tuanjie 2022, Unity 2022, and Unity 2021. The `release-evidence` command binds
all three native reports to their live staged trees and rejects a surrogate or
missing engine lane. Native evidence does not imply Android, iOS, Unity 2022
Player, or production-project correctness; those remain target Player gates.
The changed managed role is generated by `resource-player-evidence` from a real
resource-only Player smoke and its immutable Base workflow. A changed Player
rebuild is not a substitute and is rejected when it would lose universal guard
coverage.

The current release conclusion is conditional: Windows dual-Base Player
correctness and all three real-header native lanes are the completed evidence
surfaces. Android/iOS Player, device smoke, performance, memory, temperature,
and weak-core gates are not complete and must not be inferred from them.

## Rollback

Rollback is a source identity change, not a patch toggle. Restore the package
commit/version selection and the runtime/repo locks to the previous published
identities, reassemble the runtime, run `Generate/All`, and rebuild the Player.
Do not leave an opt4 package next to an opt3 runtime or reuse a generated
`Library`, `LocalIl2CppData`, or Player build from the other identity.
