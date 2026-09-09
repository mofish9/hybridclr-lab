# Frozen source resource binding

Target: resource generation and loading for Unity 2022 Windows, using frozen
ordinary AOT IL from the authenticated Base snapshot. This does not make
ordinary AOT assemblies mutable. No performance benefit is claimed here.

Two correctness gaps remain in the current resource adapter. An external frozen
plan is bound to a Base snapshot but is not recompiled against the selected
latest Current assembly set. A stale or edited plan can therefore carry an
incorrect method/type/generic selection. Also, generated frozen MVs are routed
under the immutable embedded Base MV root. Staging would modify that tree and
the Player provider would try to read those files from its original installation.

Require the full Current set hash and independently recompile the expected
frozen plan from actual Base and Current files at resource generation. Compare
the complete source set, DLL/MV hashes, source roles and every selection array.
Validate required runtime capabilities after frozen records are included.
Store frozen DLLs and their derived MVs together in Base-specific resource paths;
the original hotfix Base MVs remain embedded and immutable. Staged file records
must distinguish logical asset paths from destination-relative filesystem paths.

Verification must reject stale Current bindings, edited selections, missing or
duplicate sources, substituted DLL/MV bytes and immutable-root destinations.
Use real snapshots for compiler checks and host tests for managed loading and
staging. These checks do not replace a real Player resource-load gate. Universal
ordinary-AOT guards and ABI obligations remain enforced during this work.

Rollback boundary: lab generator/stager and package resource path validation.
Runtime native code and existing Base Players are unchanged by these fixes.
Bind every reported result to committed sources and preserve previous evidence.
