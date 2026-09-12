# Unity 2022 DHE opt5 project-trial release

This report preserves the 044d553 package/Player validation identity. The newer
package-owned binary tool distribution uses package 02dc136; current tool-only
evidence and project usage are in [dhe-package-tool.md](dhe-package-tool.md).
Do not attribute the Player results below to that later package commit.

Status: conditionally qualified for Windows project trials. Existing runtime tags
are unchanged. Use the corrected package commit below; 65581c1 was an incomplete
rollback and is not a usable DHE package. This is not mobile production approval.

## Source identity validated in this report

| Repository | Maintenance branch | Commit | Runtime tag |
|---|---|---|---|
| hybridclr | optimize/v8.13.0 | b0fe826f071332d109d2bde87c0aa2cc18b9f3c7 | v8.13.0-opt5 |
| il2cpp_plus | optimize/unity2022-v8.14.0 | 658aa64923e568a497e11640b340f316704d02f9 | v2022-8.14.0-opt5 |
| hybridclr_unity | optimize/v8.13.0 | 044d55337ae5b4ad6226fc73f2087becd6b7a48d | none; migrate by commit |

Package upstream baseline is v8.13.0 (ca7f87b6a72f3739f99a5ad0c957c7aae0cbd922).
Upstream v8.14.0/v8.14.1 commits are not ancestors of the corrected package.
The Installer retains the upstream 8.13 API/version selection and the customized
package-path, fork URL and commit checks. JSONSerialize is declared as a dependency.
The existing IL2CPP tag retains its 8.14.0 name and published source; it is not a
package upgrade. No new runtime tag or package tag was created.

Canonical package source SHA-256, excluding Git and the existing named importer
meta exclusion: F9CE95D9C44A0644862CD4A9732A2696D8A02C15CE0A0D2A63A8621A3760627F.
Lab tools and synchronized locks are on
optimize/dhe-unity2022-project-trial-v8.13.0. The toolchain manifest records its
actual Lab build commit and package ID; older bundles must not be reused.

## Review findings and corrections

- P1: the rollback to 65581c1 removed the later DHE execution plan, Delivery, asset
  provenance, compiler integration and loading protocol while locks claimed v33.
  Reproduced by the execution-plan fixture failing compilation on missing package
  files. Merged the existing pre-upgrade DHE history and restored contract v33.
- P1: the 8.14.1 Installer was copied wholesale into the 8.13 package.
  Removed upstream-only changes while retaining custom immutable runtime pinning.
- P2: JSONSerialize was used without a declared package dependency. Added it.
- P2: previous release text offered the removed 8.14.1 combination as rollback.
  Replaced those instructions and corrected stale current evidence in the trial lock.
- P2: remote optimize/dhe-project-trial-v8.14.1 remained after earlier cleanup.
  Deleted it; optimize/v8.14.1 is also absent. Official upstream tags are unrelated
  to those erroneous custom branches and were not removed.

Runtime source and tags were not modified. All restored DHE Runtime, Commands,
Il2CppDef, Link, BuildProcessors and Settings files match the complete pre-rollback
implementation at 9365929; that comparison is a completeness audit, not a release
reference or a claim that its older tests ran on the new package.

## Current verification

Artifact root: F:/hybridclr_artifacts/dhe-opt5-review.
[Artifact hashes](dhe-opt5-review-artifacts.json) bind the records below.

- Clean-source runtime assembly verifies all three current commit/tree locks.
  Its staged runtime hash is BF1E41CE8997CE4BE74F293C7A77F225DAF7ADF4F56C5F392FC0AFEFBAF2F9CC,
  identical to the existing opt5 native gate. That gate used real 2022.3.62f3
  headers, FGS, mergeReady=true and surrogateExternalHeadersUsed=false.
- CTest rerun passed 1/1 on the existing binary for those exact runtime sources.
  This does not relabel the original compile as a new package build.
- Real Unity 2022.3.62f3 default Installer passed and installed 930 runtime files
  matching the independently assembled runtime byte-for-byte. installer.log records
  this new run using the existing runtime tags.
- Managed execution-plan checks: 126/126. Linker preservation: 11/11.
  Runtime source binding controls: 12/12. Asset provenance: 16/16.
  Managed tests validate package logic with recorded native calls, not native execution.
- New Windows Base completed Prepare, StageRuntimePlan, BuildScriptsOnly and
  BuildFinalPlayer, including native finalization and snapshot identity validation.
  Base ID: 756448eaa30e3b7fe9ca00a8bb1aab7d624e496575e86e5d4ffd527559cedb15.
  Four DHE assemblies loaded; Base revision 59 and unchanged sentinel 5 passed.
- trial-method-current verifies the original IL body returns 59 before generating
  Current revision 73. Standard resource-update and stage-resource-update commands
  produced a single Current payload. The same Player then passed at revision 73
  with the same Base ID and unchanged sentinel. Stage evidence verifies unchanged
  Player/GameAssembly bytes and embedded Base metadata.

The initial fixture call supplied an incorrect expected Base revision of 41;
base-02/player-result.json preserves that failure (actual revision 59, no load
error). base-59-result.json and current-73-result.json are the corrected assertions.
No Player or Base identity was edited to pass. The wrapper did not finish after
that assertion; remaining resource steps were run with its standard C# commands.
The fixture lacking the ordinary Native DLL was rejected before a project build.

The old v32 Delivery fixture was rejected at preparation; it is not a passing v33
Delivery run. The source-binding optional stale-runtime test was also initially
given two matching roots; its stale assertion failed as expected. The dedicated
12-case source-binding controls and real Installer comparison are separate passes.

## Remaining qualification and use

Follow [the project checklist](../docs/DHE-Unity2022-Project-Team-Checklist.md).
Install this package commit and both existing runtime tags, regenerate through
the DHE workflow, and build a new Base. Old v32 Bases cannot be relabeled v33.
Keep the project's ordinary AOT set outside hotUpdateAssemblies/dheAotAssemblies.
Use the matching C# Lab toolchain and archive each Base identity and source lock.

The standalone compiler crash/recovery fixture is incomplete on this host: Windows
denied creation of a symbolic link. Its build passed; the full test did not.
The successful real Base build exercised the compiler integration, but does not
replace that missing crash/recovery coverage.
Full structural changes, multiple current-package Bases, asset Delivery Player
checks, Android/iOS, ARM64, performance and memory qualification remain pending.
Earlier structural/multi-Base records retain their historical identities.
No claim of all hotfix changes or measurable AOT speedup follows from this trial.

## Rollback

Do not roll back to 65581c1 or the removed 8.14.1 package line.
For a new project abandoning this DHE trial, restore its archived pre-DHE
package/runtime/build configuration as a set and rebuild the Player.
Existing Players may only select a compatible archived delivery and restart.
Native fixes require a new Base and a new immutable runtime release; never move
an existing opt5 tag. User projects and unrelated worktrees were not modified.
