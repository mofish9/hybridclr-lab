# HybridCLR DHE Toolchain

## Distribution contract

The formal distribution is a versioned source toolchain, not a copy of the lab
workspace. Version `0.1.1` uses toolchain and adapter contract version `1`.

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

- Windows with PowerShell 7 (`pwsh`) on `PATH`;
- Git on `PATH` for publish, install provenance, and Release workflows;
- a Unity project with HybridCLR settings;
- `dnlib.dll` from the project's embedded HybridCLR package, or an explicit
  `-DnlibPath` for registry/external packages;
- an already assembled DHE runtime whose manifest binds engine, repository,
  patch, and external-header identities.

Windows PowerShell 5.1 can launch validation in CI when `pwsh` is also installed,
but it is not the runtime implementation host for the formal CLI.

## Publish

Publish only from a clean Git checkout in which every layout input is tracked:

```powershell
./scripts/publish-dhe-toolchain.ps1 `
  -OutputRoot ./artifacts/dhe-toolchain-0.1.1 `
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
$candidate = "C:/releases/dhe-toolchain-0.1.1"

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
  -PackageRoot C:/releases/dhe-toolchain-0.1.1 `
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

Commit the installed directory before a Release workflow. The tool identity gate
then binds the installed boundary to the consumer repository's clean HEAD/tree.
Installed `doctor -RequireRelease` and `workflow -Mode Release` commands reject
missing external package-ID pins.
Generated command reports default to the system temporary report directory and
cannot be written inside either source or installed package roots.

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

- `ProjectPlan`
- `ProjectPlanValidation`
- `BatchReport`
- `SourcePreflight`
- `CleanCheckoutGate`

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
  -OutputRoot C:/build-artifacts/dhe-workflow `
  -ExpectedToolchainPackageId $packageId `
  -Mode Release -ForceOutput
```

Other entry-point commands are:

```text
archive  doctor  install  new-adapter  preflight
release  schema  validate  verify-package  version  workflow
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
