# Unity 2022 DHE project-trial status

The sources are integrated into the three maintenance repositories. The approved
runtime tags are `v8.13.0-opt5` and `v2022-8.11.0-opt5`; the package has no opt tag.
The unintended IL2CPP 8.14 upstream merge has been removed from the selected line.
Fresh Windows native, Base/resource Player and Installer results are recorded in
`dhe-il2cpp-811-restoration.md`. This is conditional project-trial qualification,
not Android/iOS or production-release qualification.

## Source identity

- Package: `optimize/v8.13.0`, commit `4fb36af996871ff1e7ab8585f096395dad5d2bd3`.
- HybridCLR: `optimize/v8.13.0`, commit `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`.
- Unity 2022 IL2CPP: `optimize/unity2022-v8.11.0`, commit `ecad8a09d1eb9b91a57c59fcdc69b268377bad59`.
- Lab: `optimize/dhe-unity2022-project-trial-v8.13.0`.
- Unity: `2022.3.62f3`, Windows x64, `Unity2022Fgs`, FGS enabled, `OptimizeSize`, no supplemental metadata.

Unity 2021 remains explicitly on official HybridCLR `v8.13.0` and IL2CPP `v2021-8.1.0`. Tuanjie is not part of this trial.

Current validation and a freshly built 59 -> 73 resource-only Player check are
recorded in `dhe-il2cpp-811-restoration.md`. `dhe-opt5-release.md` and records below
are historical and retain their original identity.

## Passing evidence

The records in this section are historical candidate evidence. In particular,
the complete Base workflow used package `f99bfa4`, not the maintenance package.
Maintenance-package c6a7aff source assembly, native CTest, managed regression and
Installer checks are recorded in `dhe-maintenance-integration.md`. Opt5 publication
checks for the superseded identity are recorded in `dhe-opt5-release.md`.

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
