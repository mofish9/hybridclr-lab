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
Base Player. Resource update compiles and publishes one current payload without
building a Player.

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
  -AotMetadataRoot C:/build/stripped-aot \
  -SettingsFile C:/project/ProjectSettings/HybridCLRSettings.asset \
  -OutputRoot C:/build/resource-205
```

The command emits one current DLL and one current MetaVersion per assembly. It does not
emit per-Base MV/delta files. Each supported Base record binds the managed
assembly set, target, generated AOT snapshot, embedded Base MetaVersion set, native
guard source, and native manifest. Any unsupported Base causes the whole command
to fail and removes publish manifests.
`-AotMetadataRoot` additionally packages the complete `patchAOTAssemblies` set when
the project uses supplemental AOT metadata; omit it only for a separately validated
no-metadata workflow.

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

## Runtime proof

Every Player downloads the same current payload and compares current MetaVersion
with its own embedded Base MetaVersion. The Player result must prove:

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
runner. Windows evidence cannot be reported as Android/iOS evidence, and native
compilation against one engine's headers cannot stand in for another engine.

## Release boundary

Base bootstrap is publishable only when source, clean checkout, package/runtime,
native, Player, resource, schema, release, and archive gates agree on the same
identity. A resource-only update is publishable only when its single payload is
compatible with all declared online Bases and the resource system preserves the
immutable Base files. Keep generated `artifacts/`, `Library/`, `bin/`, and
`obj/` output out of source commits.
