# HybridCLR DHE Toolchain

## Distribution contract

The formal distribution is a versioned source toolchain, not a copy of the lab
workspace. Version `0.1.6` uses toolchain and adapter contract version `1`.

The package contains:

- project-independent MV generation, generated-C++ transformation, validation,
  archive, and Release gates;
- public JSON schemas and runtime/patch provenance locks;
- the project adapter template and the `dhe.ps1` command entry point;
- publish, verify, install, upgrade, and doctor commands;
- third-party notices for the versioned runtime patches.

Demo sources, deterministic Demo Player construction, managed/native fixtures,
CI-only schemas, Unity caches, staged runtimes, and historical probes are not
package payloads. Target-specific Unity generation and Player assertions belong
to the project adapter.

## Requirements

- Windows or macOS with PowerShell 7 (`pwsh`) on `PATH`;
- Git on `PATH` for publishing the toolchain and tool-source provenance;
- Git or SVN on `PATH` for the project source checkout (`-ProjectVcs Svn` selects SVN explicitly);
- a Unity project with HybridCLR settings and, for Release, the reviewed
  `hybridclr_unity` package vendored under `Packages/`;
- `dnlib.dll` from that package (or an explicit `-DnlibPath` only for
  exploratory external-package workflows);
- an already assembled DHE runtime whose manifest binds engine, repository,
  patch, and external-header identities.

Windows PowerShell 5.1 can launch validation in CI when `pwsh` is also installed,
but it is not the runtime implementation host for the formal CLI.

The core CLI and project-independent gates use PowerShell 7/.NET APIs that are
available on Windows and macOS. Runtime assembly accepts explicit editor and
external-header paths for each host platform; the project adapter owns Unity executable
discovery, generated-C++ locations, and target-specific Player output. An iOS
adapter must run on macOS with the Unity iOS module and may export an Xcode
project; signing, Xcode compilation, device launch, and the runtime smoke test
remain adapter-owned assertions. Release and complete-coverage workflows must
publish `dispatchProbeValidated`, `changedProbeChanged`, and
`unchangedProbeChanged` from the real Player result; an adapter may implement
that probe in PowerShell, C#, or another platform-native runner.

The previous-release stripped-AOT baseline is a target-bound input, not merely
a directory containing files. A Release adapter must bind it to the same
target, Unity/engine version, package/runtime identity, and previous Player
release used to produce the current build. The core workflow requires the
explicit `-BaselineAotRoot` argument; an environment variable alone is not a
substitute. Projects should persist these facts in their own baseline manifest
and reject a target or engine mismatch before Unity generation starts.

The package lock is project configuration, not a lab default. It must record
the exact integrated package commit, tree hash, and project-relative directory;
versioned directories such as `Packages/com.code-philosophy.hybridclr@8.13.0`
are supported when the suffix is present in `packagePath`.

The supplied helper creates the manifest from a stripped-AOT directory and the
runtime/settings locks. Keep the manifest beside the baseline directory and
carry both as one release input:

```powershell
./scripts/new-dhe-baseline-manifest.ps1 `
  -BaselineRoot C:/releases/previous/stripped-aot/Android `
  -RuntimeManifestPath C:/runtime/DHE-Unity2021/runtime-manifest.json `
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset `
  -PackageLockPath C:/project/Assets/Editor/DHE/dhe-package-lock.json `
  -Target Android `
  -Output C:/releases/previous/stripped-aot/Android/dhe-baseline-manifest.json
```

The manifest records assembly SHA-256 values and is checked again by the
orchestrator and adapter. It is intentionally workspace-absolute; copy the
directory and manifest together and pass their paths on the build host. A
different target (for example iOS versus Android) requires a separate
stripped-AOT baseline and manifest.

## Unity Package API

The embedded `HybridCLR.Editor` package exposes the project-independent DHE
primitives through `HybridCLR.Editor.Commands.DheBuildPipeline`:

- `ValidateAssemblyScope` validates `hotUpdateAssemblies` and
  `dheAotAssemblies` without depending on a project build framework.
- `StageRuntimePlan` stages current DLLs, MV binaries, baseline snapshots and
  runtime indexes from `DheRuntimePlanOptions`; the project asset plan uses
  `hybridclr.dhe-runtime-asset-plan.json`, while its handoff copy uses
  `hybridclr.dhe-runtime-handoff-plan.json` and carries baseline/hash fields.
  Adapters can supply callbacks
  for encrypted bytes, dependency ordering and project-specific dependency
  maps.
- `BuildPlayer` binds the previous stripped-AOT directory to
  `HYBRIDCLR_DHE_AOT_BASELINE_ROOT` and builds the configured target using
  `DhePlayerBuildOptions`.

The shared PowerShell workflow helpers also expose
`Get-DheNativeManifestSourcePaths` and `Get-DheFileSetHashOrEmpty`. Both treat
an empty native-manifest method set as a valid no-op, so adapters do not need
to duplicate empty-set handling or invent a sentinel file.

YooAsset, resource encryption, load-order policy and device assertions remain
adapter callbacks/configuration. The package does not reference Cat types.

## Publish

Publish only from a clean Git checkout in which every layout input is tracked:

```powershell
./scripts/publish-dhe-toolchain.ps1 `
  -OutputRoot ./artifacts/dhe-toolchain-0.1.6 `
  -Mode Release -ForceOutput
```

The publisher uses the explicit allowlist in
`manifests/dhe-toolchain-layout.json`, removes machine-local editor/runtime
paths and workspace-only Git attributes, generates a
`manifest-directory-v1` boundary, and records:

- source HEAD and tree identities;
- the layout SHA-256;
- every payload path, size, and SHA-256;
- a canonical `packageId` derived from version, contract, source identity,
  layout, and the complete payload file set.

`packageId` is stable for the same formal package and must be recorded outside
the package by the release process. It is an integrity/provenance identifier,
not a digital signature.

## Verify and install

Read `packageId` from `dhe-toolchain-manifest.json`, pin it in the consuming
release configuration, then invoke the verifier and installer from an already
trusted tool root:

```powershell
$packageId = "<64-hex-package-id>"
$trustedTool = "C:/trusted/HybridCLRDhe"
$candidate = "C:/releases/dhe-toolchain-0.1.6"

& "$trustedTool/scripts/test-dhe-toolchain-package.ps1" `
  -PackageRoot $candidate `
  -RequireRelease -ExpectedPackageId $packageId

& "$trustedTool/scripts/install-dhe-toolchain.ps1" `
  -PackageRoot $candidate `
  -Destination C:/project/Tools/HybridCLRDhe `
  -ExpectedPackageId $packageId
```

The gate rejects missing, extra, modified, or unparseable files; duplicate JSON
properties; schema/layout drift; machine-local JSON paths; dirty/untracked
Release identities; and a mismatched external package ID.

Do not execute a candidate package's verifier or installer before establishing
trust in that candidate. Upgrades use the committed installed tool as
`$trustedTool`. The first installation must bootstrap from an externally
authenticated release archive/hash or from a verifier in a separately pinned
source commit. The package ID is still required after that bootstrap; it binds
the exact unpacked payload but does not replace archive signing or transport
authentication.

The source release workflow creates the transport records without executing the
candidate package. It republishes the exact clean commit, verifies the unpacked
package and a fresh ZIP extraction through the trusted source verifier,
creates and verifies a Git bundle for the same HEAD, and binds those hashes to
the runtime tree and passing installed-consumer gate:

```powershell
./scripts/publish-dhe-toolchain-release.ps1 `
  -PackageRoot C:/releases/dhe-toolchain-0.1.6 `
  -RuntimeManifest ./staging/runtime/DHE-Tuanjie2022/runtime-manifest.json `
  -InstalledConsumerGate ./artifacts/dhe-installed-consumer-gate/installed-consumer-gate-report.json `
  -ForceOutput
```

The sibling `*.release.json` is the transport integrity record. Distribute its
authenticated hash (or sign that record) through the trusted release channel;
SHA-256 records establish exact identity but are not authentication by
themselves.

Installation verifies source, staged, and final copies. Upgrades additionally
verify the existing installation before swapping directories and roll back an
in-process failure. The destination shares a mutex with installed workflows, so
an upgrade cannot replace scripts while a build is using them.

An installed toolchain directory is a verified package, not a Git checkout.
Installed project workflows bind the tool identity from the package manifest's
`sourceIdentity` and `dhe-source-boundary.json`; source-checkout workflows use
the live tool Git identity instead. Both forms are represented by the same
clean-checkout report contract.

Commit the installed directory before a Release workflow. The tool identity gate
then binds the installed boundary to the consumer repository's clean HEAD/tree.
Installed `doctor -RequireRelease` and `workflow -Mode Release` commands reject
missing external package-ID pins.
Generated command reports default to the system temporary report directory and
cannot be written inside either source or installed package roots.
The doctor verifies the installed package and optional project inputs; project
Git/SVN cleanliness and tracked-source coverage are owned by the workflow gate.
For an SVN project, pass `-ProjectVcs Svn` to the workflow and keep the installed
tool directory outside the SVN working copy or explicitly ignored.

## Upgrade and downgrade

Upgrade a committed installation with the incoming package ID:

```powershell
& "$trustedTool/scripts/install-dhe-toolchain.ps1" `
  -PackageRoot C:/releases/dhe-toolchain-0.2.0 `
  -Destination C:/project/Tools/HybridCLRDhe `
  -ExpectedPackageId "<incoming-package-id>" `
  -Upgrade
```

Downgrades are rejected by default. `-AllowDowngrade` is an explicit recovery
operation and still requires full source/existing/staged/final verification.
A contract-version change is not an ordinary upgrade; the installer rejects it
until an explicit adapter/project migration is performed.

## Adapter contract

Create an adapter outside the installed tool root:

```powershell
./Tools/HybridCLRDhe/dhe.ps1 new-adapter `
  -Output C:/project/Build/dhe-adapter.ps1
```

The orchestrator calls the adapter twice and always supplies
`ToolchainContractVersion=1`.

`Prepare` owns project-specific generation of the exact stripped-AOT baseline
and current hot-update DLL roots. It writes `adapter/prepare.json` using
`schemas/dhe-project-adapter-prepare.schema.json` and echoes contract version 1.
The orchestrator rejects a different contract before project preflight.

`Player` receives only orchestrator-validated paths:

- `BaselineAotRoot` and `BaselineManifestPath` when a previous stripped-AOT
  Release baseline is supplied
- `ProjectPlan`
- `ProjectPlanValidation`
- `BatchReport`
- `SourcePreflight`
- `CleanCheckoutGate`
- `AdapterOptionsPath` for project-owned options
- `PlayerSmokeRunner` for an Android/iOS (or custom Windows) smoke runner
- `RequireCompleteCoverage` when complete native/MV coverage is required

It builds and runs the target Player, loads every planned DHE assembly with its
matching MV data, performs target-specific assertions, and writes the standard
`workflow-report.json`. The core then independently validates artifacts,
portable archive, and Release readiness.

## Workflow

Run doctor first, then the project workflow with the pinned installed package:

```powershell
$tool = "C:/project/Tools/HybridCLRDhe"
$packageId = "<64-hex-package-id>"

& "$tool/dhe.ps1" doctor `
  -ProjectPath C:/project `
  -RequireRelease -ExpectedPackageId $packageId

& "$tool/dhe.ps1" workflow `
  -AdapterScript C:/project/Build/dhe-adapter.ps1 `
  -ProjectPath C:/project `
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset `
  -RuntimeSource C:/runtime/DHE/libil2cpp `
  -PackageLockPath C:/project/Build/dhe-package-lock.json `
  -OutputRoot C:/build-artifacts/dhe-workflow `
  -BaselineAotRoot C:/releases/previous/stripped-aot `
  -ExpectedToolchainPackageId $packageId `
  -Mode Release -RequireEmbeddedPackage -ForceOutput
```

Other entry-point commands are:

```text
archive  baseline-manifest  doctor  install  new-adapter  preflight
release  schema  validate  verify-package  version  workflow
```

Create a target-bound previous-release baseline manifest through the same
installed entry point:

```powershell
./Tools/HybridCLRDhe/dhe.ps1 baseline-manifest `
  -BaselineRoot C:/releases/previous/stripped-aot/Android `
  -RuntimeManifestPath C:/runtime/DHE-Unity2021/runtime-manifest.json `
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset `
  -PackageLockPath C:/project/Assets/Editor/DHE/dhe-package-lock.json `
  -Target Android `
  -Output C:/releases/previous/stripped-aot/Android/dhe-baseline-manifest.json
```

Adapters may publish additional versioned DHE report formats without copying
their schemas into the installed tool. Register the adapter-owned schema
directory explicitly while retaining fail-closed handling for every other
unknown `hybridclr.dhe-*` document:

```powershell
./Tools/HybridCLRDhe/dhe.ps1 schema `
  -InputRoot C:/build-artifacts/dhe-workflow `
  -AdditionalSchemaRoot C:/project/Build/DheSchemas `
  -OutputRoot C:/build-artifacts/dhe-schema-gate `
  -ForceOutput
```

Release requires complete hot-update/DHE assembly equality, compatible method
changes, complete native coverage, real Player evidence, clean project/tool Git
identities, clean runtime provenance, and a portable archive. Preflight-only and
Exploratory results never become Release evidence.

The source repository's self-hosted native lane additionally runs
`scripts/run-dhe-installed-consumer-gate.ps1`. It publishes the exact clean
source commit, verifies and installs that package into a fresh Git clone,
commits the installed tool boundary, and runs doctor, Player, archive, Release,
and adapter-extended schema gates only through the installed entry point. This
prevents a source-checkout pass from masking a broken package or installer.

## Runtime boundary

The source toolchain does not assemble editor-specific runtime trees as part of
project execution. Runtime assembly remains a separate build responsibility
because editor executables, external headers, and source repositories are
machine inputs. The workflow consumes the resulting provenance-bound runtime
manifest and fails closed on version, tree, patch, header, or dirty-state drift.

The Windows Demo proves the current workflow categories and is a consumer test,
not a claim that every Unity version, ABI, Android/minigame target, or production
project is already validated.
