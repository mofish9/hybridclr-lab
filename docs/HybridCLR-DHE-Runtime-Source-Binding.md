# DHE installed runtime source binding

## Reproduced failure and scope

The generic-field replay at clean lab `aea9d17` fails all nine Windows runs during
registration, before its field assertions. Its three Base manifests advertise
`dhe-runtime-v12`, but the Unity 2021 project-local libil2cpp still contains the
earlier generic-field rejection. Nine implementation/header files differ from
the selected assembled runtime (one is absent). The workflow validates the
supplied runtime manifest, not the source actually used by the project compiler.
Consequently the Base/no-op passes are not v12 runtime qualification. The same
stale-source mismatch is reproduced on the Unity 2022 and Tuanjie projects.

The primary gate is exact installed native-source agreement before and after
each Unity workflow stage, including Exploratory runs. Rejection must occur
before preparing or compiling a mismatched Base. Windows, macOS and Linux use
the same C# check and the package's host-specific LocalIl2CppData convention.
Only Windows execution is currently available. No performance claim is made.

## Binding and installation

The manifest's staged libil2cpp tree hash is checked in both workflow modes.
Every installed source file must match that tree byte-for-byte, with an exact
file set. The three package-generated files AssemblyManifest.cpp, MethodBridge.cpp
and UnityVersion.h must remain present; their project-specific hashes are
recorded separately. The Installer's libil2cpp-version.txt is a receipt, not an
executable source file. No other generated-directory exemption is allowed.

The workflow does not silently replace a project's native sources. Install a
selected assembled tree explicitly using the existing package-owned C# command
`HybridCLR.Editor.Commands.DheRuntimeCommand.InstallRuntime` and Unity argument
`-dheRuntimeSource <assembled libil2cpp>`, then run the normal workflow against
that tree's runtime manifest. This uses Installer instead of patching generated
source or build caches. Runtime fixes always produce new Base identities.

The standalone tests pass 13 checks for each of the three actual stale projects:
matching/stale/missing/extra files, exact generator exceptions, host paths and
actual stale-runtime rejection. The actual Unity 2021 workflow also rejects all
nine mismatching/missing source files before launching Unity. No unity-prepare.log
is emitted. Evidence is under `artifacts/dhe-evolution-20260908` in the workspace:
`runtime-binding-tests`, `runtime-binding-tests-u22`, `runtime-binding-tests-tuanjie`
and `workflow-runtime-binding-rejected`. Host and fixture compile with no warnings
or errors. The next gate installs the selected runtime through the package, then
builds fresh Bases and replays actual generic-field resources. Existing failed
archives must not be rewritten or relabeled.

This check does not by itself audit generated bridge semantics or prove all
historical Base identities. Direct package APIs and custom build callbacks still
need their own complete native-source identity binding before final handoff.

## Source-bound build and Base identity collision

The package Installer installs the selected v12 tree on all three projects.
Their old installed trees are preserved as `stale-installed-runtime-<engine>.zip`.
Clean lab `8313cc6` builds `base-generic-fields-bound-<engine>` on each engine;
all three no-op Players and schema gates pass, with actual source binding checks
before and after every Unity stage. These are exploratory diagnostics, not
completed generic-field qualification.

The Unity 2021 rebuilt Base has the same BaseId and native-manifest hash as its
previous incorrectly labeled Base, despite different GameAssembly.dll bytes.
The BaseId is `98bfcbf6394faa2c4e4b258bdcbbeac889da80ad02cdb3ae58245814ea362c52`.
Old DLL SHA-256 is `86B4D85F222A24A9930620A1B1DA67F35118DB93A4E71310A271952F590F23C7`;
new DLL SHA-256 is `4994D90313CE3D6C07309ADC480F52FD67C545DE16742C427E7F8BBAA0665870`.
The native manifest lacks the installed runtime source digest. Its own hash is
already included in BaseId, so the package must bind the actual runtime sources
there. Merely fixing host preflight cannot distinguish these existing archives.

The first `35e0746` replay tries the original resource manifests on the new
archives. All nine attempts are rejected at staging, before a Player is launched:
the raw build-identity file hashes differ even though the BaseId is unchanged.
That secondary check remains effective; this is not native execution evidence.
The failed `replay-generic-fields-bound` report is retained.

The bound replay configuration now uses resource manifests regenerated against
`registry-generic-fields-bound.json`, with unchanged current DLL/MV inputs and CLR
references. Its output is `replay-generic-fields-bound-current`. This diagnostic
still cannot qualify distinct native Base generations until the package binds
actual native sources into its manifest. No old resources, Player binaries or
build identities are modified.

The `303d755` bound-current replay then executes all nine real processes. Every
registration returns OK, but every run fails `generic-fields-nullable` at `ldflda`
on a supplemental field. The exact report SHA-256 is
`878B827EFDA8BA6E2FA24F49B094E8B790C4A0C86136CCD88062807C2D53A896`.
This is a field-address regression reached by the original current DLL, not a
successful generic-field or complete-suite gate. The assertions must not be
rewritten to bypass the unsupported C# operation.

## Native identity implementation

Package `93f436e` captures the actual installed source file set and SHA-256 values
in `DheNativeSourceIdentity`. Its independent source digest agrees with the C#
host binding checker. The three generated runtime files have separate digest
entries, so changes there also change the native manifest. No local absolute path
or Installer receipt contributes to the digest. The existing native-manifest hash
already contributes to BaseId; its wire algorithm and MV schema do not change.

The package checks for drift after Bee finalization. The host independently
checks the final native manifest against the installed sources for every new
workflow, including Exploratory mode. Archived native manifests remain readable,
but a newly built Base cannot omit this field. This is identity enforcement, not
a claim that arbitrary runtime source advertises trustworthy capabilities.

Thirteen standalone tests pass against each engine's real archived old and
installed new runtime: `native-source-identity-tests-u21-finalize`,
`native-source-identity-tests-u22`, and `native-source-identity-tests-tuanjie`.
They check native/source/manifest changes, deterministic capture, independent
hashes, generated-source changes, ignored receipts, missing output and mid-build
drift. The next actual Base gate must prove package compilation in each Editor,
new embedded Base identity, and final source identity validation.
