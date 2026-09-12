# Package-owned DHE build tool

Delivery hardening acceptance: both source and binary publishers use the same
Release evidence policy (clean exact source, complete authenticated evidence),
and invalid/missing evidence produces no binary distribution. Package outputs
must not overlap the executing tool, including directory ancestors and resolved
links. Workflow precedence is explicit CLI, then JSON config, then package
defaults. Tests must reproduce template overwrite, directory output overlap,
config precedence and rejected Release publication without weakening any gate.

Projects receive a portable .NET 6 DLL in the Unity package's `Tools~/DHE`,
invoked by an Editor C# API using Unity's bundled runtime. No SDK, Lab checkout,
PowerShell, test sources, historical patches, PDB or platform apphost is required
on the build machine. This changes tool delivery only, not runtime opt5 or 8.13.0.

The bundle contains the executable DLL, dnlib, runtime configuration, JSON
schemas, adapter/config templates, workflow policy and third-party notices.
The existing shared CLI implementation is compiled intact; a compile-time
package entry profile exposes only project operations. Lab-only commands and
source publication are not project entry points. This avoids a risky rewrite of
the already-tested MV/plan/resource algorithms simply to reduce source files.

Publish from a clean committed Lab tree with `publish-unity-tool -LabRoot ...
-OutputRoot ...`. Output must be a new external directory. The publisher builds
the DLL itself, records source HEAD/tree and build inputs, and authenticates the
exact distribution through the existing canonical toolchain manifest. The
Editor verifies file hashes before launching it; the DLL reuses package
verification. Build outputs and project evidence remain outside the bundle.

The bundle does not embed package/repository qualification locks that would
self-reference its containing package commit. Release validation still needs
the precise build's evidence/locks via `ValidationSourceRoot`, and fails closed
when they are missing. This exploratory opt5 distribution does not become
release-qualified by compiling with `-c Release`.

Acceptance: portable bundle verification, missing/tampered/extra-file rejection,
Release rejection, real MV and resource-update parity, managed plan regression,
Unity 2022 invocation from a path containing spaces without system SDK, and a
worker3 refresh. macOS host path handling is implemented but cannot be claimed
tested here. No performance or mobile runtime conclusion follows from this
packaging change. Roll back the package and its bundle together; runtime tags
are unchanged.
