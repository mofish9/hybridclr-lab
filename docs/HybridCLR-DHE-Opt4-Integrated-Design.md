# HybridCLR DHE opt4 Integrated Design

This document defines the formal opt4 integration of the community DHE
workflow on top of the opt3 maintenance sources.

## Runtime boundary

The DHE runtime is an additive capability on top of the opt3 metadata and FGS
implementation:

- `hybridclr@9e4fc7219724a3c63ff845e0b2acaa7d10c2430b` and annotated tag
  `v8.13.0-opt4` are the immutable native replay base, not the final
  DHE release identity.
- `hybridclr_unity@623073baafd5a1d12ea46df8145de9fddc899fac` is the immutable
  package replay base. The package does not receive an opt tag.
- the three opt3 `il2cpp_plus` engine commits are replay bases for distinct
  Unity 2021, Unity 2022, and Tuanjie reflection overlays.

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

The current MetaVersion candidate uses overlay mode until its runtime and
package commits pass all release gates. Runtime assembly and engine-test project
preparation apply the authenticated overlays automatically; the package lock
binds the resulting embedded package tree. A formal release switches back to
integrated mode after the candidate commits and immutable runtime tags exist.

## C# runtime assembly

Runtime and native proof are driven by the same C# host as the project
workflow. Runtime assembly is target-bound, starts from clean locked commits,
and applies authenticated engine-specific and common native overlays only in
temporary staging; no shell script or source-checkout mutation is involved:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime \
  -LabRoot . -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs \
  -Il2CppPlusSource ../worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests \
  -LabRoot . -Profile DHE-Tuanjie2022
```

`DHE-Unity2021` and `DHE-Unity2022` use the same commands with their matching
engine workflow and `-Il2CppPlusSource`. The host selects exactly one
engine-specific overlay from `dhe-runtime-lock.json` and records only applied
patches in the runtime manifest. A machine without the matching Editor must
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

## Rollback

Rollback is a source identity change, not a patch toggle. Restore the package
commit/version selection and the runtime/repo locks to the previous published
identities, reassemble the runtime, run `Generate/All`, and rebuild the Player.
Do not leave an opt4 package next to an opt3 runtime or reuse a generated
`Library`, `LocalIl2CppData`, or Player build from the other identity.
