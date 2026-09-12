# Unity 2022 DHE project-team checklist

Use this checklist for the first real project trial. It targets Unity 2022.3.62f3
on Windows and the `Unity2022Fgs` workflow. Tuanjie and Unity 2021 are outside this
trial; Unity 2021 remains on the official `v8.13.0` / `v2021-8.1.0` combination.

## Source checkout

Use these maintenance commits (runtime installation selects the opt5 tags below):

| Repository | Branch | Commit |
|---|---|---|
| `hybridclr_unity` | `optimize/v8.13.0` | `65581c17c7e67ea8e972fccfa3b84152262fd65c` |
| `hybridclr` | `optimize/v8.13.0` | `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7` |
| `il2cpp_plus` | `optimize/unity2022-v8.14.0` | `658aa64923e568a497e11640b340f316704d02f9` |

The C# tooling remains on hybridclr-lab branch
`optimize/dhe-unity2022-project-trial-v8.13.0`. Use the synchronized locks on that
branch; old final-01 through final-07 zip bundles are superseded. Runtime tags are
`v8.13.0-opt5` and `v2022-8.14.0-opt5`; the package has no opt tag.

Do not mix these with the official runtime or an older DHE Base. The runtime
contract is `dhe-runtime-v33`; changing package or native runtime source requires
building a new Base and invalidates the previous Base identity.

## Toolchain check

From the extracted C# toolchain directory, run `verify-package` and require:

```text
passed=true
packageTreeSafe=true
hashesValid=true
actualFileCount=expectedFileCount
```

Build a working copy of the toolchain, or set both `BaseIntermediateOutputPath`
and the output directory outside the authenticated package. Setting only `-o`
does not redirect `obj`. Verify the original package before and after building.

## Project configuration

1. Copy the package into the project without renaming its `@8.13.0` suffix if the
   project already uses that directory convention.
2. Set `hotUpdateAssemblies` to the project's existing hotfix assemblies.
3. For the first DHE trial set `dheAotAssemblies` equal to that complete hotfix
   set. Ordinary AOT assemblies stay outside both lists.
4. Preserve the generated `link.xml`, AOT generic references, stripped assembly
   inventory and supplemental metadata outputs. Do not edit `Library` or generated
   IL2CPP source to make the workflow pass.
5. Implement the project adapter callbacks for scenes, Player output, resource
   staging and runtime asset access. The package owns identity, guards, plan
   validation and dispatch.

## Base and resource sequence

Run the package lifecycle in order: `Prepare`, `StageRuntimePlan`,
`BuildScriptsOnly`, then `BuildFinalPlayer`. Archive `build-identity.json`,
`native/dhe-native-manifest.json`, the AOT snapshot, complete stripped AOT
inventory, Player SHA-256 and GameAssembly SHA-256.

Build Current DLL/MV from that Base. If serialized assets contain the changed
types, run `DheAssetBuild.Build` in the authoring Editor and pass its
`asset-build.json` to `DheDeliveryBuilder.Build`. Publish the immutable delivery
and its manifest hash. At runtime call `DheRuntime.TryPrepareDelivery` with the
selected manifest hash and load through the returned handle.

The first Windows trial must include valid, missing-asset, changed-asset and wrong
manifest-hash cases. Rejected cases must fail before native effects. Build a second
Windows Base and prove that the same Current delivery is accepted by both Bases.

## Android handoff

After Windows succeeds, use the same source commits and project adapter to build
Android ARM64. Record correctness/differential, PSS/RSS, P50/P95/P99, temperature,
weak-core behavior, package size and download behavior. Windows evidence cannot be
used as Android release evidence.
