# Cross-assembly hotfix parent evolution: Unity 2022 Windows

## Current result

The current candidate accepts and executes a changed parent whose definition is
in another hotfix assembly. The same Current DLL set is loaded by immutable
Base-101 (the original parent is present) and Base-100 (root-only, smaller layout).
Both real Unity 2022.3.62f3 Windows Players pass 33 cross-parent checks, plus 18
framework, 25 virtual-signature and 46 business checks. A shared resource audit
and deliberate native-preparation failure/fresh-process recovery also pass.

This is conditional correctness evidence for a cross-assembly parent addition on
the tested Base layouts. It is not a release, Android/iOS, performance, memory,
Scene/Prefab, startup-object or full DHE qualification. Type deletion and cached
old-object behavior are qualified separately in the type-deletion report. A Base
whose original parent was already cross-assembly is being built as a follow-up;
its success is not inferred from this new-parent result.

## Source identity and changes

Runtime and package sources are unchanged from the preceding qualified checkpoint:

| Component | Commit |
|---|---|
| HybridCLR | 6180597d2c0e455ab09fe0920d34d6dea5ad00fc |
| Unity 2022 IL2CPP | 819f74c08e466a0d2a8fe5b1afaad5b1d784e482 |
| Package | 841abfd46e122343717fe4115186b97a215b58df |
| Native gate | parent-transitions/native-03, real Unity 2022 headers, mergeReady=true |
| Lab candidate | research/dhe-cross-assembly-parents-v8.13.0 |

The lab source evolution is isolated and committed:

| Commit | Role |
|---|---|
| 24bd980 | cross-parent workload, compiler wiring and workflow commands |
| b85ec9e | detached metadata reads before writing merged Current |
| 3f71f05 | policy and graph-control fixtures |
| 5c92149 | duplicate peer handling in closed-parent scan |
| f76685e | duplicate peer handling in logical metadata scan |
| b2b81e2 | complete Base/Current graph binding, invalid-peer rejection and full layout-plan selection |

Final cross-parent tool SHA-256 is
8FF8F90564CAFB043A80ED01EAA23F30803986730E43B0265055C58B8EF24A0E.
Final Player host SHA-256 is
AB705F5E8AF2B3F270797C9ADF345A4E85A6CE06275D36F8C8A1339662FAC5AA.
The tool change is currently in the lab candidate; no formal package branch,
runtime tag, Installer default or CAT project was changed.

## Reproduction and evidence

All paths below are relative to C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents.
`current-01/current` is compiled from the unchanged type-deletion Current. The
Other hotfix assembly receives `CrossParent : ProcessorRoot`; Model.Processor is
retargeted to that parent and its constructor call. A second Model probe is
compiled against the updated Other assembly. Both the compiler output and final
entry wiring are retained; original Current bytes are hash-checked unchanged.

CLR reference-03 passes all 33 checks. They cover assembly-qualified parent lookup,
absence in the owner assembly, constructor/fields/properties/events, declaring and
reflected type identity, root/interface/generic dispatch, reflection construction,
GC and independent instances.

`shared-two-base-01` uses one Current payload on Base-101 and Base-100. Both
Players pass the full sequence. Result SHA-256:
02896F2165BD858D08082CC043C11D6064C28FCD0030731AFB2998C2802D45DB.
Base-101 and Base-100 Player result SHAs are respectively
6A0562492C6A3547A4B875B83C339418839B9BBCA4F83ECD36697191435B7786 and
55D986300BDE2321F0BAD9EA6CEB280615EC32CFE293EB57789BADF208A60726.
The shared Current-set SHA-256 is
c3b993355d53a1694e21daa66520fe3858556083346b57e8fae0e04ba76fb8e1.

`probe-base101-01`, `probe-base100-01` and `probe-base100-cached-01` each pass
33 cross-parent checks and the complete framework/virtual/business sequence.
The cached control uses the existing virtual receiver cache; it does not claim
that the new cross-parent fields are available on an old physical object.

`shared-two-base-audit-01.json` passes 43 checks and verifies 125 files, the
original Player/GameAssembly identities and four successful resource executions.
Its SHA-256 is
491883A21F4CD3EB9335EA30B3F05F2423F6F9311697764987F48971861E1A1C.
`shared-public-probes-01/result.json` passes 16 preparation-failure/recovery
checks and binds 99 files. Its SHA-256 is
6AF8C008ECE8B89FC4CC488DDF68DE901ECB2D43BD6EF5F702D171FA5097FF99.

The initial unmodified producer attempt is retained at
`admission-control-01`: both Bases are rejected with
`existing-type-layout-or-vtable-change:...Processor`. The rejection is the
expected pre-fix reproduction. The corrected producer accepts the same Current
after it receives complete original and Current peer snapshots.

`policy-05/result.json` passes 29 checks. It verifies the accepted cross-assembly
boundary, physical selection requirement, removal symmetry, no-op AOT retention,
missing/duplicate/wrong peer rejection, and every required runtime capability's
missing-capability rejection. Its SHA-256 is
E91E320B63B236F0421579F785338C709B1AAF4C13EBD1A51B1ACC50B31DDF5D.
The existing parent-evolution policy regression passes 48 checks using the
previous local-parent fixtures.

## Implementation boundary

`ResourceUpdateCompatibility.Analyze` now accepts an optional immutable
`baselineAssemblySet`. The resource producer passes the full archived Base set
and the full Current set. `PhysicalParentGraphs` indexes `(assembly, type)` keys,
walks each side's own parent graph, preserves the immutable external boundary,
and rejects missing, duplicate, ambiguous, cyclic, sealed, generic or wrong
owner snapshots. Current declarations never substitute for a missing Base peer.
Duplicate peer lists are rejected before any capability scan, preventing a throw
or accidental selection. Existing full execution-plan type/method selections are
still passed for every Base, including the smaller-layout Base-100.

No runtime ABI guard was weakened. Required physical parent, frame, reference,
reflection and snapshot capabilities remain in each selected plan. Unchanged
methods keep their AOT path; no-op proofs are separate from performance claims.

## Remaining gates

The reverse case—an immutable Base that already contains a cross-assembly parent—
is still in progress. After that, test cross-assembly parent removal/replacement
and cached old objects. Generic parents remain explicitly outside the accepted
physical-parent subset. Unity Scene/Prefab and startup/live-object behavior,
concurrent publication stress, production-equivalent throughput/tails/memory,
final source-bound regression, formal branch/tag publication and Android testing
remain open. Windows Player correctness cannot establish ARM64 correctness or
mobile performance.

Rollback selects the previous matched runtime/package/tool combination or an
archived compatible resource and restarts. All relevant candidate worktrees are
clean; no files were deleted. C: has roughly 6–8 GiB free after these builds.
