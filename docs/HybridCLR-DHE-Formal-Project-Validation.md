# DHE Formal Project Validation

The supported project workflow is the cross-platform C# host in
`tool/HybridCLR.DheTool.csproj`, the HybridCLR package-owned workflow runner,
and a small Unity C# project adapter. No PowerShell or platform shell is part
of the contract.

## Required project inputs

- `ProjectSettings/HybridCLRSettings.asset` with non-empty
  `hotUpdateAssemblies` and an exactly equal `dheAotAssemblies` set.
- An embedded HybridCLR package whose path, including an optional `@8.13.0`
  suffix, commit, and tree are recorded by the project package lock.
- A runtime manifest bound to the exact engine, runtime patches, package tree,
  target, and real external headers.
- A C# adapter exposing `Prepare`, `StageRuntimePlan`, `BuildDheYooAsset`,
  `BuildScriptsOnly`, and `BuildFinalPlayer`. The package owns phase ordering,
  generated-C++ discovery, guard injection, identity state, and restoration;
  the adapter owns resource catalog, signing, Player output, and device smoke.
- A release-ready authenticated DHE toolchain package and exact package ID for
  Release mode.

There are two separate production flows. Bootstrap creates a new immutable
Base Player. Resource update compiles and publishes one resource package without
building a Player; the package may contain multiple target-specific current
payload variants when managed metadata shapes differ.

## Base bootstrap

Run the project workflow once for each platform/Player release:

```text
dotnet HybridCLR.DheTool.dll workflow \
  -ProjectPath C:/project \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/base-100 \
  -RuntimeManifestPath C:/runtime/runtime-manifest.json \
  -PackageLockPath C:/project/ProjectSettings/DHE/dhe-package-lock.json \
  -ToolchainRoot C:/project/Tools/HybridCLRDhe \
  -ExpectedToolchainPackageId <64-hex-id> \
  -Target Android \
  -AdapterMethod MyGame.Editor.DheWorkflowBuild.Prepare \
  -Unity /path/to/Unity \
  -Mode Release -Bootstrap -RunPlayer
```

Bootstrap is the initial trust root and therefore does not require a previous
baseline manifest. It still requires the release toolchain, clean/tracked
project and tool sources, package/runtime identities, real target headers,
universal native guard coverage, final Player identity, resource evidence,
Player/device smoke, release gate, and archive gate.

The scripts-only pass generates clean IL2CPP C++, injects universal guards,
embeds one Base MetaVersion for every DHE assembly, and stages a
`state=staged-for-final-player` BuildIdentity. The final pass must compile that
identity without guard drift and restore the project identity source to its
zero template in both success and failure paths.
The identity and composite `baseId` bind the engine workflow and actual IL2CPP
code-generation mode. The locked mappings are `Unity2021Standard/OptimizeSpeed`,
`Unity2022Fgs/OptimizeSize`, and `Tuanjie2022Fgs/OptimizeSize`; a missing or
mismatched pair fails before the Player or registry can be published.
The host also owns AOT metadata selection: Unity 2021 uses the configured
supplemental set, while both FGS workflows pass an explicit empty set. Project
`UnityArguments` cannot override workflow, code generation, or metadata mode.

Every external/precompiled hot-update DLL used to create the Base must come
from the active target's current-generation compilation. In particular, an
Android or iOS Base must not freeze a DLL produced under Windows conditional
compilation. A target-only P/Invoke or metadata member that is missing from the
Base cannot be repaired by weakening the later compatibility gate.

Archive the Base output's `baseline/`, `build-identity.json`,
`native/dhe-native-manifest.json`, runtime/package locks, and Player evidence.
They are required to validate later updates, but they are never sent as part of
the remote hot-update payload.

## Resource-only update

After code changes, use the project's C# build pipeline to compile exactly one
current hot-update DLL set. Then validate it against every Base Player that is
still supported:

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot C:/build/current-hotfix \
  -BaseRoots C:/release/base-100/baseline,C:/release/base-110/baseline \
  -BaseNativeManifests C:/release/base-100/native/dhe-native-manifest.json,C:/release/base-110/native/dhe-native-manifest.json \
  -BaseBuildIdentities C:/release/base-100/build-identity.json,C:/release/base-110/build-identity.json \
  -AotMetadataRoots C:/release/base-100/aot-metadata,C:/release/base-110/aot-metadata \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-205
```

The command emits one current DLL and one current MetaVersion per assembly. It does not
emit per-Base MV/delta files. Each supported Base record binds the managed
assembly set, target, generated AOT snapshot, embedded Base MetaVersion set, native
guard source, and native manifest. Any unsupported Base causes the whole command
to fail and removes publish manifests.
`-AotMetadataRoots` additionally packages one complete `patchAOTAssemblies` set per
Base when the project uses supplemental AOT metadata; in registry mode each Base entry
may instead set `aotMetadataRoot` to `null` to select the empty set. The empty-set hash
must already be present in that Player's BuildIdentity. Use the singular
`-AotMetadataRoot` only when every Base intentionally shares one root. Omit both in the
legacy parallel-argument form only for a separately validated no-metadata workflow.

The default current DLL bytes are shared across the registry. Platform-specific conditional
members, P/Invoke declarations, or other managed metadata differences are not translated;
the affected Base must bind a separate `payloadVariantId` and variant root. Use
`-CurrentVariantRoots {"android":"C:/build/current-android","windows":"C:/build/current-windows"}`
with a registry whose entries select `android` or `windows`. The manifest/runtime plan
contains all variants, while each Player stages and loads only its selected variant.
Android/iOS and desktop still require independent target-specific Player gates.

`stage-resource-update` copies this one payload into the project's resource
catalog staging root. Pass the exact archived `build-identity.json` for the
Player being staged with `-BaseBuildIdentity`. The command verifies the identity
schema, composite `baseId`, and file SHA before selecting that exact
`supportedBases` record, then checks every embedded Base MetaVersion against both
the identity and selected record. It can also assert that Player and GameAssembly
hashes remain unchanged. The resource manifest binds the runtime plan by SHA-256,
and staging verifies every current DLL, MetaVersion, and optional AOT metadata payload
before copying. YooAsset/Addressables catalog building,
signing, upload, rollback pointer, and device smoke remain project callbacks.

After the staged Player/device smoke writes its result, bind the resource-only
changed lane for review and toolchain release evidence:

```text
dotnet HybridCLR.DheTool.dll resource-player-evidence \
  -ResourceUpdateRoot C:/build/resource-205 \
  -StageReport C:/build/resource-205/stage-base-100.json \
  -PlayerResult C:/build/resource-205/player-base-100.json \
  -BaseWorkflowReport C:/release/base-100/player-workflow-report.json \
  -Output C:/build/resource-205/evidence/resource-player-workflow-report.json
```

This command revalidates the selected Base, live staged manifest/validation/plan
bytes, build identity, native manifest, immutable Player files, assembly scope,
changed dispatch, remaining AOT entries, structural/reflection probes, and
transaction rollback. It replaces rebuilding a changed Player merely to obtain
the `player-changed` release role; Base Players must retain universal guards.
Formal toolchain release evidence runs this smoke against at least three distinct
Base identities, requires Unity 2021, Unity 2022, and Tuanjie 2022 coverage, and
requires every report to reference one manifest/validation and its selected current
payload variant. The input
is extensible, so additional representative Base reports can be bound as the
matrix grows. Every online Base remains a mandatory input to `resource-update`.

For consecutive hotfix releases, keep the same archived Base registry and run
`resource-update` again with the new current DLL set. Stage the resulting
manifest/payload over the same Player resource root after the previous smoke;
the Player's embedded Base MetaVersion and immutable native files must remain
byte-identical across both stages. Do not use the previous current payload as
the new baseline unless a new Base Player is intentionally being shipped and
archived as a new registry entry.

## Runtime proof

Every Player downloads the selected current payload variant and compares current
MetaVersion with its own embedded Base MetaVersion. The Player result must prove:

1. Its complete Player identity uniquely matches a validated `supportedBases`
   record.
2. Every planned current DLL/MetaVersion hash and embedded Base MetaVersion hash matches.
3. Changed existing methods dispatch to the interpreter and unchanged methods
   retain their native AOT entry.
4. Supported new top-level types/methods execute through direct and reflection
   probes.
5. Multi-assembly load is complete and ordered.
6. A forced registration failure does not publish partial state, and retrying
   the same DLL succeeds in the same process.

The current compatibility policy permits existing method-body changes, new
top-level and nested ordinary/generic types and their members, non-virtual methods
added to existing ordinary types, constrained instance/static field evolution,
type/method/reference-field removal, method signature replacement, logical
property/event evolution, and existing-member custom attributes. Existing value-type
instance layout, inheritance/interface/class-layout/vtable changes, unsupported
method declaration changes, new virtual/abstract/PInvoke methods, and fields whose
storage or address semantics cannot be preserved still fail closed. The complete
matrix is maintained in `HybridCLR-DHE-Resource-Only-Design.md`.

## Platform matrix

The C# host and package APIs are shared by Windows, Android, and iOS. Android
still requires the Unity Android module, SDK/NDK, signing, and device runner.
iOS still requires macOS, the Unity iOS module, Xcode, signing, and a device
runner. The demo DHE reader handles Android APK StreamingAssets through its ZIP
container; a production YooAsset/Addressables provider must provide the same
logical asset-path behavior on Android and iOS. Windows evidence cannot be
reported as Android/iOS evidence, and native compilation against one engine's
headers cannot stand in for another engine.

After Android Bee native finalization, the package uses the current Player
DAG's `DestinationPath` and the Editor-owned Java/Gradle tools to rebuild the
requested APK/AAB. The final workflow is allowed to pass only after both the
package and host prove that every archive `libil2cpp.so` equals the corresponding
Bee JNI staging file. `adapter/native-finalize.json` is therefore a required
live gate, not an informational log. iOS follows the same C# Bee state machine,
but its Xcode link/sign/device result must be produced and checked on macOS.

## Release boundary

Base bootstrap is publishable only when source, clean checkout, package/runtime,
native, Player, resource, schema, release, and archive gates agree on the same
identity. A resource-only update is publishable only when its single payload is
compatible with all declared online Bases and the resource system preserves the
immutable Base files. Keep generated `artifacts/`, `Library/`, `bin/`, and
`obj/` output out of source commits.
