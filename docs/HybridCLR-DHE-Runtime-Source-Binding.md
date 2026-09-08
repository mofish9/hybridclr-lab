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
