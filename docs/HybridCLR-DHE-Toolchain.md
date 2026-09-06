# HybridCLR DHE Toolchain

The DHE workflow is a cross-platform .NET host plus a Unity C# project adapter.
The same host runs on Windows, macOS, and Linux; Unity-specific compilation
remains in the embedded HybridCLR package and the project adapter.

## Layout

`tool/HybridCLR.DheTool.csproj` is the entry point. `tool/Program.cs` owns the
CLI and project orchestration; `tool/ProductionGates.cs` owns source, checkout,
artifact, release, archive, and regression validation. `tool/dnlib.dll` is the
pinned assembly inspection dependency. JSON schemas are the report contract.

The current DHE lab commands include `assemble-runtime`, `native-tests`, `build-managed-cases`,
`generate-test-manifest`, `generate-metadata-stress-source`, `reference`,
`compare-results`, `check-environment`, `prepare-engine-test-project`,
`bootstrap-repos`, `clear-unity-project-locks`, and `wait-editor`. For example:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- assemble-runtime \
  -LabRoot . -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs \
  -Il2CppPlusSource C:/repos/il2cpp_plus
dotnet run --project tool/HybridCLR.DheTool.csproj -- native-tests \
  -LabRoot . -Profile DHE-Tuanjie2022 \
  -RuntimeRoot C:/build/runtime -OutputRoot C:/build/native-tests
```

These commands use direct .NET process execution and fail closed when an
external prerequisite (Unity, CMake, compiler, git, or dotnet) is missing.

The multi-Base lab fixture can derive a second Base-generation current set from
an already authenticated `DHE_CURRENT` assembly set without recompiling unchanged
assemblies:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- build-managed-cases \
  -LabRoot . -Target StandaloneWindows64 -Variant current-base2 \
  -SeedCurrentRoot C:/evidence/current-authenticated \
  -OutputRoot C:/build/current-base2
```

This lab-only variant copies the three secondary assemblies byte-for-byte and
changes only `DheMultiBaseProbe.CurrentValue` and the `DheDemoCalculator`
constructor bodies in `HybridCLR.ManagedCasesAot.dll`. The command preserves
assembly/module identity and rejects overlapping seed/output trees. The release
regression requires this transformation to remain compatible and to produce
exactly two method changes without metadata drift; pass the authenticated seed DLL through
`-ManagedCurrentSeed`. Project hot-update DLLs continue to come from the project's
normal managed compilation; this fixture is not a production IL rewriter.

Prepare every locked engine-specific `il2cpp_plus` checkout without manually
creating worktrees:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- bootstrap-repos \
  -LabRoot . -ReposRoot C:/repos -AllEngineWorkflows
```

This keeps the shared `hybridclr` and `hybridclr_unity` checkouts at their
locked commits and creates `il2cpp_plus-<engine-workflow>` worktrees for Unity
2021, Unity 2022, and Tuanjie 2022. `assemble-runtime` automatically selects
the matching worktree. Use `-EngineWorkflow <id>` to prepare only one lane.
Integrated source identity hashes normalize Git's CRLF checkout conversion,
so an equivalent clean checkout does not depend on `core.autocrlf`; binary
bytes remain exact.

## Project Configuration

The host is project-independent and does not select a Demo adapter implicitly.
Generate the Unity C# adapter and configuration, then customize only the
project-owned resource, signing, and smoke policies:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-adapter \
  -Root . -Namespace MyGame.Editor \
  -Output C:/project/Assets/Scripts/Editor/DHE/DheWorkflowBuild.cs
dotnet run --project tool/HybridCLR.DheTool.csproj -- new-config \
  -Output C:/project/ProjectSettings/DHE/dhe-workflow-config.json
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -Config C:/project/ProjectSettings/DHE/dhe-workflow-config.json
```

The generated adapter is a compilable, fail-closed scaffold. It calls every
package-owned DHE phase, but `BuildDheYooAsset` deliberately throws until the
project implements its resource/catalog build and writes verified structured
evidence. A YooAsset/Addressables project also replaces its asset root/resolver;
a production project supplies signing and runtime smoke logic. Paths in the
config are resolved relative to the config file; explicit
command line values override config values. `unityArguments` is a scalar map for
project-owned Unity adapter options (for example `dhePreview`,
`dheStandalone`, or a target-specific fallback metadata root). The host still
owns the reserved DHE paths and stage ordering. The config contract is
`schemas/dhe-workflow-config.schema.json`.

## Build and run

Build once on the build host:

```text
dotnet build tool/HybridCLR.DheTool.csproj --configuration Release
```

Run commands with the produced project (or use `dotnet run` during development):

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- version -Root C:/tools/HybridCLRDhe
dotnet run --project tool/HybridCLR.DheTool.csproj -- mv \
  -BaselineAssembly C:/release/Assembly-CSharp.dll \
  -CurrentAssembly C:/build/Assembly-CSharp.dll \
  -Output C:/build/Assembly-CSharp.mv.json \
  -BinaryOutput C:/build/Assembly-CSharp.mv.bytes \
  -StrictCompatibility
dotnet run --project tool/HybridCLR.DheTool.csproj -- batch \
  -BaselineRoot C:/release/stripped-aot \
  -CurrentRoot C:/build/stripped-aot \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/dhe/batch
```

All paths are explicit. Output paths are checked against the input roots and
are never allowed to overwrite an assembly or settings file.

## Fixed Base and resource-only updates

Build a Base Player once with the project workflow's `-Bootstrap -RunPlayer`
mode. The Player embeds one Base MetaVersion per configured hot-update assembly and a
build identity; the scripts-only stage emits universal native guards before
the final Player is compiled. Archive the Base DLL root,
`build-identity.json`, and `dhe-native-manifest.json` for every Player version
that remains online.

The build identity also records the normalized, complete DLL-name inventory
from `AssembliesPostIl2CppStrip/<target>` and its SHA-256. The configured DHE
set must be a subset of that inventory. The package re-enumerates the stripped
output before and after the final build, compares it with the staged identity,
and verifies that every supplemental AOT metadata input still matches the
stripped DLL bytes. A stale or partial inventory aborts the Base build.

For later releases, compile one current hot-update DLL set and validate it
against all supported Base Players in one command:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRoots C:/release/base-100/dlls,C:/release/base-110/dlls \
  -BaseNativeManifests C:/release/base-100/dhe-native-manifest.json,C:/release/base-110/dhe-native-manifest.json \
  -BaseBuildIdentities C:/release/base-100/build-identity.json,C:/release/base-110/build-identity.json \
  -AotMetadataRoots C:/release/base-100/aot-metadata,C:/release/base-110/aot-metadata \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-update
```

For an online game, keep the supported Base set in one authenticated
`hybridclr.dhe-base-registry.json` file instead of maintaining parallel command
line lists. Registry paths can be relative to the registry file, and each entry
declares one Base ID, engine workflow, baseline snapshot, native manifest,
BuildIdentity, and (when needed) its supplemental AOT metadata root:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRegistry C:/release/base-registry/supported-bases.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-update
```

The host rejects duplicate Base IDs, unsupported engine workflows, mixed registry
and parallel Base arguments, missing registry files, and an identity whose
`baseId` differs from the registry entry. The resulting resource manifest records
the registry SHA-256 and entry count and copies the exact registry bytes to
`audit/dhe-base-registry.json`; staging verifies that copy before touching the
project asset root. The audit can therefore prove which online Base set was
validated even after the build host is gone. Add a newly shipped Base by adding
its archived entry before the next resource build; the current DLL/MV payload
remains a single shared payload.

Use the C# `base-registry` command to create or extend that registry. It validates
each archived identity, the universal native manifest hash, every baseline DLL hash,
and the managed assembly-set hash before writing a portable registry. Existing
entries are retained with `-ExistingRegistry`; new entries are supplied as equal-length
comma-separated lists:

```text
dotnet HybridCLR.DheTool.dll base-registry \
  -ExistingRegistry C:/release/base-registry/supported-bases.json \
  -BaseIdentities C:/release/base-120/build-identity.json \
  -BaselineRoots C:/release/base-120/dlls \
  -BaseNativeManifests C:/release/base-120/dhe-native-manifest.json \
  -EngineWorkflows Unity2022Fgs \
  -PayloadVariantIds windows \
  -AotMetadataRoots C:/release/base-120/aot-metadata \
  -Labels "Unity 2022 Base 120" \
  -Output C:/release/base-registry/supported-bases.next.json
```

The command never copies Base artifacts and refuses to overwrite an input. The
output is re-read through the same resolver used by `resource-update`; publish the
new registry atomically together with the next resource build. A registry is only
considered complete when the `regression` gate has exercised both normalization and
duplicate-Base rejection.

Every generated registry has a stable `registryId`, a monotonically increasing
`revision`, and the SHA-256 of its direct parent. `-ExistingRegistry` retains every
active Base by default. Removing an online Base requires both
`-RetireBaseIds <baseId,...>` and `-RetirementReason <reason>`; the generated
registry carries the cumulative retirement audit and rejects later reactivation of
the same Base ID. `resource-update` requires `-PreviousBaseRegistry` for revision 2
or later, verifies the direct transition, and archives both current and parent
registry bytes. This prevents an accidentally incomplete registry from producing a
green hotfix package merely because the omitted Player was never checked.
Historical Base Player evidence remains valid across tool releases when the
currently pinned Release package explicitly authorizes its immutable package ID in
`manifests/dhe-toolchain-evidence-authorities.json`. Each record binds the historical
toolchain version, Package ID, source commit, and source tree; the manifest itself is
part of the current Package ID. The release system keeps those historical Release
packages by content identity and can relocate them with `-EvidenceToolchainRoots`,
so it does not need the DHE Git repository. A legacy package without the authority
manifest may use verified Git ancestry during migration qualification. Once an
explicit manifest exists, an unknown or removed ID cannot use Git fallback.

Registry lineage alone cannot identify the head used by the last published hotfix:
without an external head, another revision 1 could omit old Bases. Release mode
therefore also maintains `dhe-release-ledger.json`. Initialize a channel exactly
once (including migration from 0.1.21), only after auditing that the selected
registry contains every live Base:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -Mode Release \
  -InitializeReleaseLedger \
  -ReleaseChannelId production \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRegistry C:/release/base-registry/supported-bases.json \
  -PreviousBaseRegistry C:/release/base-registry/parent.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-update
```

For every later hotfix, keep the complete previous release directory and pin its
ledger SHA-256 in the release service or CI protected state:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -Mode Release \
  -PreviousReleaseLedger C:/published/update-N/dhe-release-ledger.json \
  -ExpectedPreviousReleaseLedgerSha256 <published-ledger-sha256> \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRegistry C:/release/base-registry/supported-bases.json \
  -PreviousBaseRegistry C:/release/base-registry/parent.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/update-N-plus-1
```

The current registry may be byte-identical to the ledger head (ordinary code-only
hotfix) or exactly one direct successor (new/retired Base). The command derives the
channel and next release revision from the previous ledger; it never accepts them
from candidate output. Publish the manifest, validation, runtime plan, ledger, and
payload as one signed/catalogued directory, then atomically replace the protected
head hash only after all Player gates pass. Once that protected head exists, CI
must reject any later use of `-InitializeReleaseLedger`; a local CLI cannot infer
global publication history.

The recommended Release path uses `channel-state` as that protected state instead
of copying ledger values into CI variables. For a new channel, create a snapshot
before building the first resource:

```text
dotnet HybridCLR.DheTool.dll channel-state \
  -Operation snapshot \
  -StateRoot C:/release-state \
  -ChannelId production \
  -ToolchainRoot C:/tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <pinned-release-package-id> \
  -Output C:/build/snapshots/production.json
```

For a channel that already published ledger-backed updates before adopting this
store, perform the privileged migration exactly once:

```text
dotnet HybridCLR.DheTool.dll channel-state \
  -Operation adopt-existing \
  -StateRoot C:/release-state \
  -ChannelId production \
  -ResourceUpdateRoot C:/published/update-N \
  -ExpectedReleaseLedgerSha256 <published-ledger-sha256> \
  -AcknowledgeExistingPublishedHead \
  -ToolchainRoot C:/tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <pinned-release-package-id> \
  -Output C:/build/snapshots/production.json
```

Pin the snapshot bytes by SHA-256 and pass both values to the next resource build:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -Mode Release \
  -ChannelSnapshot C:/build/snapshots/production.json \
  -ExpectedChannelSnapshotSha256 <snapshot-sha256> \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRegistry C:/release/base-registry/supported-bases.json \
  -PreviousBaseRegistry C:/release/base-registry/parent.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/update-N-plus-1
```

Add `-InitializeReleaseLedger` only when the snapshot says
`initializationRequired=true`. An initialized snapshot supplies the previous
ledger path/hash, channel ID, and next revision; it cannot be combined with the
legacy explicit head arguments. A stale snapshot is rejected by both qualification
and promotion.

Release qualification must execute that exact continuation on every active Base.
`regression -ResourceUpdateRoot <previous> -ResourceUpdateRoot2 <candidate>` binds
the complete `-WorkflowChangedRoots` Player set to the candidate manifest and
ledger. Every active `supportedBases` ID must appear exactly once; three-engine
coverage alone cannot hide an untested old or newly added Base. The regression
records this head identity and `release-evidence` revalidates it against the copied
Player reports. Consecutive releases may use the same registry; a changed Base set
must use its authenticated direct successor. The qualification gate has no fixed
four-to-five-Base assumption.

The output contains one copy of each current DLL and current MetaVersion. Base inputs
are compatibility evidence only and are never copied into `payload/`. At
runtime each Player compares the remote current MetaVersion with its own embedded Base
MetaVersion, so different Base versions may produce different changed-method sets from
the same payload. If any declared Base is incompatible, the command removes
the publish manifests and fails instead of producing a partial release.
When supplemental AOT metadata is required, `-AotMetadataRoots` supplies one complete
`patchAOTAssemblies` root per BaseRoot, in the same order. The command content-addresses
and deduplicates equal blobs across sets. On disk, each blob uses a 128-bit SHA-256 prefix
as a short lookup name so long Windows project/worktree paths remain readable by Unity;
the full SHA-256 is still recorded and verified. In registry mode each entry owns this
choice independently: `aotMetadataRoot: null` selects the empty metadata set for that
Base, while a directory supplies its complete set. The empty-set hash must match the
Base identity, so `null` cannot be used to bypass metadata embedded in an existing Player.
The legacy parallel-argument form still requires an explicit root for every Base whenever
the project `patchAOTAssemblies` list is non-empty. `-AotMetadataRoot` remains available
as a shorthand only when every Base intentionally shares one root.

The default current DLL set remains shared byte-for-byte for legacy and single-target
releases. When target-specific compilation produces different managed metadata shapes,
the same release can carry multiple payload variants instead of transforming one
platform's assembly into another. For example:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot C:/build/current-windows \
  -CurrentVariantId windows \
  -CurrentVariantRoots {"android":"C:/build/current-android"} \
  -BaseRegistry C:/release/base-registry/supported-bases.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-update
```

Set `payloadVariantId` to `android` or `windows` in each registry entry. The manifest
and runtime plan retain top-level records for the `-CurrentRoot` payload for compatibility
and add `payloadVariants[]`; each Base is bound to one variant hash and the runtime loads
only that variant's DLL/MV files. `-CurrentVariantId` defaults to `default`, so existing
single-target commands and payload paths do not change. Give it the real target/channel
name for a multi-variant release so the package does not contain an unused duplicate
`default` payload. This is still one resource package. Every variant must independently
pass its target's guard and Player gates; if all targets share a compatible metadata shape,
keep using one default variant.

Assembly execution mode is classified against both Base sets. A name in the
Base DHE set is `dhe-differential`; a name absent from the complete Base AOT
inventory is `interpreter-only`. A name already present in the Base AOT
inventory but outside the DHE set is rejected with
`assembly-present-in-base-aot-outside-dhe:<name>` because its registered AOT
image cannot be treated as a newly loaded interpreter assembly.

Use `stage-resource-update` from the resource/catalog build to copy this one
payload. Supply the exact archived Base identity with `-BaseBuildIdentity`; the
command uses its composite `baseId` to select one supported Base and proves that
the identity file, embedded Base MetaVersion, and optional Player binaries were
not changed. Two Players may therefore share an identical Base MetaVersion set
while retaining different runtime/native identities. It verifies the manifest-bound
runtime plan and every current DLL, MetaVersion, and supplemental AOT metadata hash
before copying. The complete lifecycle and current compatibility subset are in
`docs/HybridCLR-DHE-Resource-Only-Design.md`.

Run `resource-player-evidence` after the real Player/device smoke. It binds the
resource manifest, stage report, Player result, and immutable Base workflow into
`resource-player-workflow-report.json`. This is the only supported
`player-changed` input for toolchain release evidence; a non-bootstrap changed
Player build is intentionally rejected because online Base Players require
universal guards.

The distributed tool also ships `dhe-resource-player-workflow.schema.json` and
validates this report as part of the schema gate. A legacy Player result may omit
both payload-selection fields only for a `single-current-payload` release whose
authenticated manifest and validation contain one implicit `default` payload. A
partial pair or any variant release remains fail-closed; generated evidence
always records the inferred or selected variant and current assembly-set hash.

After every active Base has produced that report, run the project-facing aggregate
gate once for the resource release:

```text
dotnet HybridCLR.DheTool.dll resource-release-gate \
  -ToolchainRoot C:/tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <pinned-release-package-id> \
  -EvidenceToolchainRoots C:/tools/history/dhe-0.1.20-a,C:/tools/history/dhe-0.1.20-b \
  -ResourceUpdateRoot C:/build/resource-205 \
  -ChangedPlayers C:/evidence/base-100.json,C:/evidence/base-101.json \
  -ChannelSnapshot C:/build/snapshots/production.json \
  -ExpectedChannelSnapshotSha256 <snapshot-sha256> \
  -ExpectedReleaseLedgerSha256 <candidate-ledger-sha256> \
  -Output C:/release-gates/resource-205.json
```

The command revalidates every referenced artifact instead of trusting report
booleans. The Player Base IDs must exactly equal the candidate manifest's active
Base set, and every report must resolve to that manifest, validation, ledger, and
selected payload variant. Revision 1 replaces `ExpectedPreviousReleaseLedgerSha256`
with `-InitializeReleaseLedger`; revision 2 and later reject that flag and require
the exact protected parent hash. `-RequireEngineMatrix` is an optional lab or
cross-engine-channel policy. A normal project channel needs reports for all of its
active Bases, not artificial Bases from engines it has never shipped.

`EvidenceToolchainRoots` is the comma-separated set of historical Release package
locations actually referenced by active Base reports. A root may authenticate the
toolchain that built a Base or the exact runtime lock used by that Base when those
two package generations differ. The gate resolves every root by recomputed Package
ID, requires an exact runtime-lock SHA-256 match before using a runtime contract,
then verifies the locked repository/workflow commits and trees. It rejects duplicate
IDs, wrong packages, current-package substitution, and roots used by neither role.
It records all resolved package identities and each Player's `current-package` or
`authorized-historical-package` mode. During promotion,
`channel-state promote` passes those roots back through a complete gate regeneration
and compares every field before CAS publication.

`ValidationSourceRoot` is only a bootstrap compatibility input for a legacy current
package that has no authority manifest. Normal project releases omit it. An explicit
authority set always fails closed on unknown or revoked IDs even if a Git checkout is
also supplied. The successful output uses
`hybridclr.dhe-resource-release-gate.json`; it is the project release system's
approval input for publishing the already-built single resource directory. Put
the output in a separate release-gate directory; the command rejects output
inside the resource candidate, authenticated toolchain, validation/schema source,
or any Player evidence directory so qualification cannot mutate its own inputs.

Promote only that state-bound gate:

```text
dotnet HybridCLR.DheTool.dll channel-state \
  -Operation promote \
  -StateRoot C:/release-state \
  -ChannelId production \
  -ResourceReleaseGate C:/release-gates/resource-205.json \
  -ExpectedChannelHeadSha256 <snapshot-channel-head-sha256> \
  -ToolchainRoot C:/tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <pinned-release-package-id> \
  -Output C:/build/snapshots/production-promoted.json
```

For genesis, replace `-ExpectedChannelHeadSha256` with `-InitializeChannel`.
Promotion fully regenerates the aggregate gate, requires every non-time field to
match, publishes the resource tree and gate into content-addressed directories,
then replaces `channels/<channel>/head.json` while holding the exclusive lock.
Exactly one contender can advance a head. If the head replacement succeeds but
writing the external output snapshot fails, do not retry promotion; run
`channel-state -Operation snapshot` and use the recovered current head.

`filesystem-cas-v1` is valid only on a protected filesystem whose exclusive file
handles coordinate every publisher and whose same-directory rename/replace is
atomic. Orphan `.staging-*` directories are unreferenced and harmless. A network
filesystem with weaker semantics, or S3/OSS/CDN object storage, requires a C#
adapter that preserves the same immutable-object and conditional-head-write
contract. Do not advance a local file head and later treat a separate CDN pointer
as the same atomic transaction.

## JSON contract gates

Validate one document or every registered DHE report below an output root with
the same host distributed to the project:

```text
dotnet HybridCLR.DheTool.dll schema-validate \
  -Schema C:/tools/HybridCLRDhe/schemas/dhe-workflow-config.schema.json \
  -Document C:/project/ProjectSettings/DHE/dhe-workflow-config.json \
  -Output C:/build/dhe/config-schema-validation.json
dotnet HybridCLR.DheTool.dll schema-gate \
  -SchemasRoot C:/tools/HybridCLRDhe/schemas \
  -InputRoot C:/build/dhe \
  -Output C:/build/dhe-schema-gate.json \
  -RequireKnownFormats
```

The gate builds its format registry from the distributed schemas, rejects an
unsupported assertion keyword instead of silently ignoring it, and validates
its own evidence document before returning success. `workflow` invokes this
gate over its complete output root before returning success, including when it
stops after preflight.

## Project workflow

The project provides a C# class containing these Unity execute-methods:

- `Prepare`: call `DheBuildPipeline.PrepareProjectArtifacts` to regenerate the
  current stripped-AOT image and stage complete baseline/current assembly sets,
  then write `adapter/prepare.json`.
  Any project-owned precompiled DLL supplied through `dheCurrentInputRoot` or
  an equivalent callback must be compiled for the active target with
  `HYBRIDCLR_DHE_CURRENT_GENERATION`. A Windows-compiled external DLL is not a
  valid Android/iOS Base input even when its assembly name is identical;
  target-specific P/Invoke and conditional metadata would otherwise be absent
  from the immutable Base snapshot.
- `StageRuntimePlan`: call `DheBuildPipeline.StageRuntimePlan` with an explicit
  `Target`, complete hotfix load list, AOT metadata roots, and project resource
  callbacks. `RuntimeAssetPathResolver` maps staged files to a YooAsset,
  Addressables, or other catalog locator; its default is StreamingAssets.
- The demo adapter accepts the optional Unity argument
  `dheAotMetadataAssemblies`. Leave it unset to use
  `HybridCLRSettings.patchAOTAssemblies`; pass `none` when intentionally
  building a Base with no supplemental AOT metadata. The resulting empty-set
  hash is part of BuildIdentity and must be archived before a registry entry
  can use `aotMetadataRoot: null`.
- `BuildScriptsOnly` and `BuildFinalPlayer`: call
  `DheBuildPipeline.BuildPlayer` with the project build callback.
- `BuildScriptsOnly`: call `DheBuildPipeline.FinalizeProjectNativeCode` with
  `RebuildPlayer=false` after the clean generated-C++ pass and record its guard
  evidence.
- `BuildFinalPlayer`: set `DhePlayerBuildOptions.NativeFinalizeOptions`. The
  package resolves the final generated-C++ root, injects all MV guards, rebuilds
  the editor-owned Bee graph, and invokes `NativeFinalizeResultCallback` before
  temporary baseline assembly inputs are restored. Bee exit code 4 triggers the
  target Editor's `*PlayerBuildProgram.exe`, graph stabilization, guard
  reapplication, and one final native build. This state machine is limited to
  eight backend attempts and fails when no reapply callback is available.
  The Player DAG must uniquely reference the finalized generated-C++ root; it
  is never selected only by directory timestamp. For Android the package then
  resolves the Gradle destination from that exact DAG input, invokes the
  Editor-owned Java/Gradle distribution, rebuilds
  the APK/AAB, and compares every packaged `libil2cpp.so` with its Bee JNI
  staging file. The host then independently repeats the path, artifact, and
  SHA-256 checks from `adapter/native-finalize.json`.

After `StageRuntimePlan`, the C# host materializes every authenticated
`aotMetadata` record into `<OutputRoot>/aot-metadata-root/<AssemblyName>.dll`.
The Player and project workflow reports expose this directory and its
content-addressed `aotMetadataSetId`; use that directory directly as the new
Base registry entry's `AotMetadataRoot`. An empty metadata set reports a null
root and retains the SHA-256 empty-set identity. Payload hash or path traversal
failures stop the workflow before the Player build.

The host starts Unity directly with `ProcessStartInfo`.
The final-player phase binds `HYBRIDCLR_DHE_AOT_BASELINE_ROOT`, while
current-generation clears that variable and always regenerates the current
stripped image. This is the key correctness boundary for baseline/current
token identity.

The host's `workflow` command performs Prepare, C# MV batch, and project
preflight. Pass `-RunPlayer` to continue in the same C#-orchestrated flow with
the adapter's `StageRuntimePlan`, `BuildDheYooAsset`, `BuildScriptsOnly`, and
`BuildFinalPlayer` methods. Each Unity invocation is started directly by
`ProcessStartInfo`, reads stdout/stderr concurrently, has a bounded
`-UnityTimeoutSeconds` (default 600), and writes logs below the output root.
A project contract test can pass `-StopAfterPreflight` after Prepare and strict
MV validation; this exits before runtime-plan/resource/Player stages while
still leaving all preflight reports on disk.
A project adapter owns resource catalog integration, platform signing, and
device smoke callbacks. Generated-C++ discovery, native guard injection, and
Bee graph evaluation are package C# APIs and are mandatory for a DHE Player.
Those stages must
produce the existing JSON evidence before a build is called Release-ready; a
compiled Player without changed/unchanged dispatch evidence is intentionally
rejected.

Native manifest resolver version 3 uses `guard-block-set-v1`. Every method owns
one begin/end delimited guard block. The package rejects missing, duplicate, or
non-canonical blocks, while the independent release gate hashes the same blocks
in path/function/token order. Unrelated generated C++ bytes are provenance but
are deliberately outside this identity, so compiling the embedded build
identity cannot create a self-referential hash.

For a project adapter named `MyGame.Editor.DheWorkflowBuild`, invoke the same
host on Windows or macOS with an explicit method name:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow \
  -ProjectPath C:/project \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/dhe \
  -BaselineAotRoot C:/release/stripped-aot \
  -BaselineManifestPath C:/release/stripped-aot/dhe-baseline-manifest.json \
  -RuntimeManifestPath C:/release/runtime/runtime-manifest.json \
  -PackageLockPath C:/project/ProjectSettings/DHE/dhe-package-lock.json \
  -SourceBoundaryPath C:/project/ProjectSettings/DHE/dhe-source-boundary.json \
  -ToolchainRoot C:/project/Tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <64-hex-package-id> \
  -ArchiveRoot C:/build/dhe-archive \
  -Target Android \
  -AdapterMethod MyGame.Editor.DheWorkflowBuild.Prepare \
  -Unity /path/to/Unity -Mode Release -RunPlayer
```

Use `-Mode Exploratory -StopAfterPreflight` while first integrating an adapter.
Exploratory runs may omit runtime/baseline manifests and may use a dirty project,
but they never produce `releaseReady=true`. A Release run requires every input
shown above and rechecks source/package identity both before and after Unity.

The final reports are:

- `project-workflow-report.json`: complete orchestrator stages and paths.
- `player-workflow-report.json`: Player, MV, native guard, identity, and runtime
  evidence consumed by `release-gate`.
- `release-gate.json` and `release-gate.artifact-validation.json`: independent
  live revalidation of DLLs, MV JSON/binary, runtime plan, native manifest,
  resource evidence, and Player results.
- `archive-gate.json` plus `dhe-archive-manifest.json`: hash-verified portable
  evidence. The archive excludes the large Player backup directory and includes
  only referenced generated C++ guard sources.

## Android and iOS

The host is platform-neutral. Unity Editor is the platform-specific build
dependency: Android requires the Android module/SDK, and iOS requires macOS,
the iOS module, Xcode, signing, and a project-owned runner. The package locates
the editor-owned `bee_backend` executable directly on Windows or macOS and
invokes it through .NET; no shell executable is involved. The same C# host and
adapter code is used on both platforms; only the explicit Unity executable,
target, output path, and runner configuration differ. Windows cannot provide
evidence for an iOS Xcode/device build.

Android finalization is not complete when Bee has only rebuilt a staging
`libil2cpp.so`. The package must re-run the Editor-owned Gradle launcher against
the Gradle root named by the current Player DAG, copy the single resulting
APK/AAB to the requested Player path, and prove all ABI entries match staging.
`native-finalize.json` records the DAG, graph-regeneration counts, Gradle root,
artifact hash, native source paths, archive entries, and hashes. The C# host
rejects stale Gradle roots, more than eight attempts, graph regeneration without
guard reapplication, array misalignment, and source/archive hash drift.

iOS uses the same C# Bee graph regeneration and guard reapplication path, with
no PowerShell dependency. This repository has no macOS/Xcode environment, so
the generated Xcode project, final linked binary/IPA, signing, device
correctness, memory, and tail-latency gates remain conditional rather than
passed.

## Baseline manifest

Create a target-bound previous-release manifest with the C# command:

```text
dotnet run --project tool/HybridCLR.DheTool.csproj -- baseline-manifest \
  -BaselineRoot C:/release/stripped-aot/Android \
  -RuntimeManifestPath C:/runtime/runtime-manifest.json \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -Target Android \
  -Output C:/release/stripped-aot/Android/dhe-baseline-manifest.json
```

The manifest binds target, engine/runtime identity, package lock, and every
baseline assembly SHA-256. A Release update must supply this manifest; an
exploratory bootstrap may use current artifacts for both sides but cannot be
published.

## Package and project locks

The package lock must point to the actual embedded directory, including Unity
version suffixes such as `Packages/com.code-philosophy.hybridclr@8.13.0`.
`dhe-toolchain-layout.json` and the generated toolchain manifest authenticate
the C# host source, schemas, patches, and pinned dnlib bytes. The installed
tool directory should be outside an SVN working copy or explicitly ignored.

## Release boundary

Project Release readiness is derived only from passing source, clean checkout,
project preflight, Player, independent artifact, release, and archive gates.
It requires equal hot-update/DHE assembly sets, the MetaVersion proven-safe
compatibility subset, complete native guard coverage, target-bound Base
identities, clean source/package/runtime provenance, and a real Player smoke
result. The accepted subset includes method-body changes, supported additions
and removals of types, methods and fields, method signature replacement,
logical property/event evolution, custom attributes, and constrained reference
type sidecar fields. Existing value-type layout, inheritance/interface/vtable,
unsupported field shapes, and unsupported declaration changes fail closed. A no-op
release is valid when all changed/interpreter/native counts are zero and the
transaction status is `notApplicable`. The Player must also set
`noOpAotBehaviorValidated=true` after checking generation-local AOT results
against equivalent reflection calls, the complete multi-assembly scope, a
positive AOT entry count, and the absence of interpreter dispatch. The no-op
gate must not encode business-result constants from the first Base generation.
Changed-player validation follows the same rule: the project selects and invokes
an actually changed probe, keeps an unchanged AOT control, and validates its own
business oracle. Core DHE evidence checks dispatch, direct/reflection consistency,
multi-assembly execution, structural capability, and transaction rollback without
assuming that every later payload has one fixed set of changed methods or results.

Publishing the toolchain itself in `-Mode Release` requires
`-ReleaseEvidence <report>`. Generate it from a clean source identity with the
same host:

Before the final regression, run
`HybridCLR.Lab.Editor.HybridCLRDheCppResolverRegression.Run` once with each
locked Editor and pass `-dheResolverEngineWorkflow Unity2021Standard`,
`Unity2022Fgs`, or `Tuanjie2022Fgs`. Each report binds the exact Editor version
and the SHA-256 of the package's `DheBuildPipeline.cs`. The production
`regression` command must receive all three reports through
`-ResolverUnity2021`, `-ResolverUnity2022`, and `-ResolverTuanjie2022`; a
partial matrix or a report from a different package source is rejected. Its
`-WorkflowChangedRoots` argument is the comma-separated list of changed Player
evidence directories; `-WorkflowNoopRoot` supplies the no-op directory.
It must also receive every historical Release package named by the candidate's
authority manifest through `-EvidenceToolchainRoots`. This is an exact-set check:
missing, extra, duplicate-ID, non-Release, or version/head/tree-mismatched packages
fail the toolchain release regression. Once a channel has advanced beyond revision
2, pass its immutable genesis resource and matching registry through
`-ReleaseGenesisRoot` and `-ReleaseGenesisBaseRegistry`. This keeps the genesis
test independent from the current `-ResourceUpdateRoot` to
`-ResourceUpdateRoot2` consecutive-release pair.

```text
dotnet HybridCLR.DheTool.dll release-evidence \
  -LabRoot C:/src/hybridclr-lab \
  -OutputRoot C:/build/dhe-release-evidence \
  -Regression C:/build/regression.json \
  -ChangedPlayers C:/build/unity2021/resource-player-workflow-report.json,C:/build/unity2022/resource-player-workflow-report.json,C:/build/tuanjie2022/resource-player-workflow-report.json \
  -DemoNoop C:/build/demo-noop/player-workflow-report.json \
  -NativeTuanjie2022 C:/build/native-tuanjie/native-gate.json \
  -NativeUnity2022 C:/build/native-unity2022/native-gate.json \
  -NativeUnity2021 C:/build/native-unity2021/native-gate.json \
  -ResolverTuanjie2022 C:/build/resolver-tuanjie/dhe-cpp-resolver-regression.json \
  -ResolverUnity2022 C:/build/resolver-unity2022/dhe-cpp-resolver-regression.json \
  -ResolverUnity2021 C:/build/resolver-unity2021/dhe-cpp-resolver-regression.json
```

The generated report must match the exact source HEAD/tree and binds eight fixed
reports plus every `player-changed` report by SHA-256. `-ChangedPlayers` accepts
a comma-separated list, requires at least three distinct Base identities, and
must cover `Unity2021Standard`, `Unity2022Fgs`, and `Tuanjie2022Fgs`. Additional
Base reports may be supplied. Every changed result must be backed by the same
resource manifest and validation, must select the current assembly set bound to
its own payload variant, and must match its Base record's target. Each managed role is rebound to its integrated
runtime manifest, clean tracked sources, real Editor headers, and the authenticated
release toolchain that executed it. This toolchain may be the preceding release;
the clean current host independently revalidates the complete evidence to avoid a
circular self-publication dependency. Each resolver and native role is revalidated against its
own locked engine workflow, package/runtime source commits, live runtime tree, and real Editor header
tree. A command-line readiness flag cannot promote a package.

When qualifying a new toolchain release, pass the previous Release package as
`-ToolchainRoot`/`-ExpectedToolchainPackageId` and the clean candidate Git root
as `-ValidationSourceRoot`. Runtime locks, schemas, and clean tool identity are
then taken from the candidate while the previous immutable package remains the
release authority. Normal game-project builds omit `-ValidationSourceRoot`; in
that case the installed Release package is both authority and validation source.
Native compilation and iOS/Xcode execution remain target environment gates;
a Windows result is not iOS evidence.
