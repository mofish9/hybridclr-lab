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

## Verified resource binding result (2026-09-10)

Lab `471ee47`, package `f1111dc4a72454568563f40e44a1f8c405e59d67`:
`artifacts/dhe-frozen-resource-binding-20260910/managed-01.json` passes 74 host
checks. Native calls are recorded, not executed by this host fixture.

`real-snapshot-02/result.json` in the same artifact root passes 29 checks using
proof-12's immutable Base snapshot. This includes stale Current hashes,
relabelled stale selections, all selection arrays, source DLL/MV substitution,
the real resource-update CLI, frozen resource payload resolution, wrong Base
and immutable-root rejection, no-op staging and stage-report schema validation.
The original `real-snapshot-01` process was interrupted with no result; it is not
passing evidence. The second run was started only after confirming no live
process remained and used a new output directory.

The valid evolved plan still produces a failed resource compatibility report
because ordinary layout/native ABI admission is unresolved, and no publishable
resource manifest is emitted. No-op resource staging preserves the Base MV tree
and Player binary hashes. This does not qualify a frozen-source resource Player.
