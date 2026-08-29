# HybridCLR DHE opt4 Integrated Design

This document defines the first formal opt4 integration of the community DHE
workflow. The earlier `dhe-toolchain-0.1.1` package remains a historical workflow proof;
this opt4 line binds the same behavior to the opt3 maintenance sources.

## Runtime boundary

The DHE runtime is an additive capability on top of the opt3 metadata and FGS
implementation:

- `hybridclr@9e4fc7219724a3c63ff845e0b2acaa7d10c2430b` is the formal
  `optimize/v8.13.0` commit and annotated tag `v8.13.0-opt4`.
- `hybridclr_unity@9a9b703463a453e7cfa75957f17cbcb28acb21dc` is the formal
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

`apply-dhe-runtime-patches.ps1` and `assemble-runtime.ps1` fail closed on an
unknown mode, a mixed entry mode, a commit mismatch, a tree mismatch, or an
attempt to use an integrated lock with a stale staged tree. The package lock
uses the same mode and binds the embedded package tree to the integrated
`hybridclr_unity` commit.

## Assembly commands

The normal formal assembly is:

```powershell
./scripts/assemble-runtime.ps1 `
  -Profile DHE-Tuanjie2022 `
  -EngineWorkflow Tuanjie2022Fgs `
  -ReposRoot ../repos `
  -PackageRoot ./unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr
```

The same integrated HybridCLR source can be checked against the Unity 2021
opt3 engine line:

```powershell
./scripts/assemble-runtime.ps1 `
  -Profile DHE-Unity2021 `
  -EngineWorkflow Unity2021Standard `
  -ReposRoot ../repos `
  -Il2CppPlusSource ../worktrees/dhe-opt4-il2cpp-unity2021 `
  -PackageRoot ./unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr
```

`DHE-Unity2022` is also supported for source/native assembly. A machine without
the matching Unity 2022 Editor must pass `-AllowSurrogateExternalHeaders`; the
result is exploratory native evidence only.

## Required gates

Run the gates in this order after assembling the runtime:

1. `run-dhe-source-preflight.ps1` checks the integrated source commits/tree
   hashes, package tree, settings scope, and runtime manifest.
2. `run-dhe-native-gate.ps1` runs the native compile/CTest gate with the
   matching engine headers.
3. `run-dhe-capability-gate.ps1` and
   `run-dhe-compatibility-negative-gate.ps1` validate MV semantics.
4. `run-dhe-project-workflow.ps1` is the only gate that can produce real
   Player evidence and a release-ready report.

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
