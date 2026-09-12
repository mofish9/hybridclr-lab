# Unity 2022 DHE project-trial status

The sources are integrated into the three maintenance repositories. Runtime tags `v8.13.0-opt5` and `v2022-8.14.0-opt5` are published; the package has no opt tag. This is not an Android/iOS or production-release qualification. Historical Player evidence and current maintenance integration checks are distinguished below.

## Source identity

- Package: `optimize/v8.14.1`, commit `936592974000e53846e36800fa1fd363ffbcc3f6`.
- HybridCLR: `optimize/v8.13.0`, commit `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`.
- Unity 2022 IL2CPP: `optimize/unity2022-v8.14.0`, commit `658aa64923e568a497e11640b340f316704d02f9`.
- Lab: `optimize/dhe-unity2022-project-trial-v8.13.0`.
- Unity: `2022.3.62f3`, Windows x64, `Unity2022Fgs`, FGS enabled, `OptimizeSize`, no supplemental metadata.

Unity 2021 remains explicitly on official HybridCLR `v8.13.0` and IL2CPP `v2021-8.1.0`. Tuanjie is not part of this trial.

## Passing evidence

The records in this section are historical candidate evidence. In particular,
the complete Base workflow used package `f99bfa4`, not the maintenance package.
Maintenance-package c6a7aff source assembly, native CTest, managed regression and
Installer checks are recorded in `dhe-maintenance-integration.md`. Opt5 publication
checks and current identity are recorded in `dhe-opt5-release.md`.

- Native gate: `F:/hybridclr_artifacts/dhe-project-trial-20260912/native/DHE-Unity2022/native-gate.json` reports `passed=true`, `mergeReady=true`, and `surrogateExternalHeadersUsed=false`.
- New Base workflow: `F:/hybridclr_artifacts/dhe-project-trial-20260912/base-01/result.json` reports `passed=true`, 40 ordinary AOT assemblies, and runtime contract `dhe-runtime-v33`.
- Managed execution-plan regression passed; the host and package compile with zero errors.
- Asset provenance passed 16/16 checks.
- Toolchain source coverage passed 5/5 checks; the published toolchain independently compiles from its output directory.

## Project trial procedure

1. Install the candidate package with the matching Unity 2022 IL2CPP and HybridCLR sources. Do not mix it with an older DHE Base.
2. Set `hotUpdateAssemblies` to the project's hotfix set and `dheAotAssemblies` to the same set for the first trial. Keep ordinary AOT assemblies outside both lists.
3. Use `DheProjectWorkflowRunner` for Prepare, StageRuntimePlan, BuildScriptsOnly and BuildFinalPlayer. Archive identity, native manifest, AOT snapshot, stripped inventory and Player hashes.
4. Build Current DLL/MV from that Base. For serialized assets, run `DheAssetBuild.Build` and pass its provenance to `DheDeliveryBuilder.Build`.
5. Publish the immutable delivery and call `DheRuntime.TryPrepareDelivery` at the existing hot-update load point. Missing/changed files and wrong manifest hashes must fail before native effects.
6. Repeat with a second Windows Base and verify the same Current delivery. Then build Android ARM64 with the same identities and record correctness, PSS/RSS, tail latency, temperature and weak-core data.

## Boundaries

This evidence does not prove Android ARM64, iOS, save-data migration, all Unity serialization forms, or a method-level AOT speedup. Runtime contract changes require a new Base. Existing capable Bases roll back by selecting an archived compatible delivery and restarting.
