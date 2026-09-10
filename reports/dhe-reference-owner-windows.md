# Physical reference owners: Unity 2022 Windows

## Current result

Base-70 passes the complete adjacent Unity regression with the exact callback
Current02 DLLs that failed on Base-69. The runtime allocation fix repairs custom
generic-owner identity/reflection. A separate planner fix selects the coroutine
state machine whose concrete field refers to the selected component. Changing
only the resource plan on immutable Base-70 repairs the coroutine; native frame
guards remain unchanged. All 46 business cases, 18 generic reference checks,
11 cached receiver checks, 14 reference checks, 9 interface callbacks,
11 serialization checks, 17 lifecycle checks and public failure/recovery pass.

The full shared-resource rerun at 0636641 also passes on original-layout Base-71
and evolved-layout Base-70, both using the same native runtime. Three independent
shared resources cover the preserved callback payload, a changed generic body,
and unchanged generic AOT dispatch. Detailed identities are below.

This is an incomplete research candidate.
Broader interface removal/inheritance, old-object migration, scene/Prefab
evolution, byref/native boundaries, performance and memory remain required work.
Unity 2022 Windows is the qualification target; Tuanjie follows Windows stability.
No new Unity 2021 work or mobile/production claim is included.

## Source and evidence identities

Artifact paths below are relative to `D:/hybridclr_artifacts`.

| Component | Candidate source |
| --- | --- |
| HybridCLR, research/dhe-generic-context-v8.13.0 | e3d660482d7a671152ef5625f77212d1ef3fa2a6 |
| IL2CPP, research/dhe-reference-storage-unity2022-v8.13.0 | 6951d3052190ce3b62853434b4788d4b4780ccb4 |
| Package, research/dhe-generic-context-v8.13.0 | fc1ba89770479dbb47f23af9c9baa20fffebf7e6 |
| Lab, lifecycle oracle and native03 | 9c71e01 |
| Lab, Base-70 build and pre-closure resource | f9b2e45326d5866a851f71b41dcc741740f25b15 |
| Lab, first owner closure | cc68c3a |
| Lab, initial complete owner closure, hosts/tools and resource02 | b0dbf75c67230fd93a621416e8329167752aee9e |
| Lab, parent/cross-assembly closure and final shared resources | 0636641d49da4f981d90895c44c57dd9c696f6e0 |

HybridCLR canonical source tree:
`D25C6843DA04EF6F65F7AF983A3ABD8CDD443BCF2B20A99F01A3B5DEC5D3D724`.
IL2CPP/package source trees remain those in `dhe-hotfix-generic-windows.md`.
Runtime03 manifest SHA:
`9D3020BB712EDCECDD3853CF3DD0A9A7262FB4DFB38768A303197D7D9E1717AB`.
The experimental runtime contract remains dhe-runtime-v32; MV is DHEMETA1/schema 1.

Base-70 lives at `dhe-hotfix-generic-base-70`. Base ID:
`b68b9aea9bc70e6c8c155ea43b366ed821d01a9ee81e12d25c7a9797bc4aa9c7`.
GameAssembly SHA:
`8DC03BB65F2D52B27061A95A079BCADBB725C8467A3196AC0E1F0690CE13CB5F`.
Build/startup/generated no-op resource all pass. Base-70 was built with generic
host-06 and generic tool-01 from the preceding report; later tool changes never
replace its binaries or embedded identity.

| Final tool/host artifact | SHA-256 |
| --- | --- |
| dhe-reference-owner-tool-02/HybridCLR.DheTool.dll | D75E67774483B5481416210BF4A95265DD4A93E23318A2B460C73625A9EF0692 |
| dhe-reference-owner-host-02/AotSnapshotTests.dll | 220C3A17DA73C480E8884D9E610AAC323D4280BD7053D3D6D7F29489C0515970 |
| dhe-reference-owner-plan-host-04/ExecutionPlanTests.dll | 6E5847091AC2D76F60F9CA8E9A053F98AD0CB816526620D3F4B8FC4F7363460F |

## Implementation and preserved failures

Current constructor metadata can name an unselected raw Current owner. Allocation
now reapplies that image's published storage selection, including a generic
definition and each concrete argument. This produces the same physical type as
type operands and reflected construction. Selected Current definitions and new
interpreter-only types retain their representations. Existing objects are not
migrated. Mapping acquires published DHE state and interns metadata under the
metadata lock.

`dhe-hotfix-generic-owner-resource-01` uses the exact Current02 DLLs on Base-70.
Cold/cached `dhe-hotfix-generic-base-70-reference-generic[-cached]-01` pass 18/18,
with 11/11 cached receiver checks. The same runtime still fails
`dhe-hotfix-generic-base-70-full-unity[-cached]-01`. The corrected oracle passes
the first five checks, but MoveNext reaches the Base ABI guard and lifecycle
times out. Preserve these failed Player logs and reports.

The state machine's own layout and constructor MV are unchanged; its MoveNext MV
is changed. Its typed component reference requires a Current owner representation
even though the field remains pointer-sized. The planner now computes a fixed
point through concrete instance-field types, including chains, cycles, arrays
and closed generic signatures. Open T alone does not select a generic definition.
Actual root changes remain separate from dependent owners, preserving existing
impact-report semantics and capability/admission checks.

`dhe-reference-owner-plan-before-01` preserves an additional test-oracle mistake:
it incorrectly expected the captured MoveNext MV to be unchanged. Corrected
before02 retains 14 intended missing-owner/constructor failures. After02 passes
29 checks, including the real coroutine, reference cycles, original root
identities, package binding and unselected scalar/open-generic controls.
Generic-plan02 passes the 17 preceding conditional generic regressions.

The first implementation cc68c3a incorrectly counted dependent owners as root
changes. Managed01 preserves that single `compiler-finds-layouts` failure.
b0dbf75 keeps original roots and propagates their dependencies; managed02 passes
all 113 package, schema, native-argument and recovery checks without changing the
root-count assertion. These are managed tests with recorded native calls.

## Player and native gates

`dhe-hotfix-generic-native-03` passes real Unity 2022 headers, compile and CTest at
the allocation runtime, with mergeReady=true and
surrogateExternalHeadersUsed=false. The later planner changes do not change
native source or relabel this native evidence.

`dhe-reference-owner-resource-01` and lifecycle-diagnostic01 establish the first
passing coroutine at cc68c3a. Final evidence uses resource02 and tool/host02:

- Current-set SHA: `e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24`.
- Model DLL SHA: `4FE3BE882007F3601DCD9133A85B555CE331A281E8A81EFCE468A78137DA99B3`.
- Resource02 manifest SHA: `1122B427B7E48A255BEC3C4F4077FABF686D55FD263A1E39F4BA8527EA312513`.
- Resource02 workflow: seven checks, full 46-case sequence, immutable Player.
- Resource-audit02: one Base, one successful run, 46 cases and 67 files.

Replays use `dhe-reference-owner-base-70-<mode>-02`:

| Mode | Result |
| --- | --- |
| reference-callbacks | 9/9 callbacks |
| reference-callbacks-cached | 11/11 receiver checks and 9/9 callbacks |
| reference-cached | 11/11 receiver checks and 14/14 reference checks |
| reference-generic-cached | 11/11 receiver checks and 18/18 generic checks |
| full-unity | 11/11 serialization and 17/17 lifecycle |
| full-unity-cached | 11/11 receivers, 11/11 serialization and 17/17 lifecycle |

`dhe-reference-owner-public-probes-02` passes all nine checks, binds 47 files and
verifies original Base behavior, the full deliberate preparation-failure
sequence, and valid Current recovery in a fresh process. No failed-process
business entry is allowed. These are one-Base results until the shared-resource
rerun is recorded separately.

## Final two-Base rerun

Base-71 (`dhe-reference-owner-base-71`) was built at lab b0dbf75 with owner
host/tool02. It uses the exact runtime03 manifest and runtime/package commits
listed above. Base ID:
`db3bd6f8bbbd7403319ed78921b031b89c7b4a10b08cd6890d911dd44bc7fabc`.
GameAssembly SHA:
`44B6EBB31B2A22E29002B2DFE2CABFA5E18B57C773287ED37B466DCCF035EB0E`.
Both Base-71 and Base-70 retain their original build/startup/no-op proofs.

Review found another dependency edge: an instance field can refer to a derived
class whose parent moved to Current, or a class can inherit a closed generic
with an affected argument. Parent-before01 preserves three missing-selection
failures. Parent-after01 passes all 38 checks, including unchanged cross-assembly
owners and order-independent closure. This verifies propagation through an
existing hierarchy, not arbitrary edits to inheritance declarations. Generic
plan03 passes 17 checks and managed03 passes 113 checks at 0636641.

The final binaries at that lab commit are:

| Tool/host artifact | SHA-256 |
| --- | --- |
| dhe-reference-owner-tool-03/HybridCLR.DheTool.dll | B7FA12AAC0CF0188129DDAB6F6F03E8F0E7C30390F2E71C573A94311838DF790 |
| dhe-reference-owner-host-03/AotSnapshotTests.dll | D5725F2956DD37B714E599DB7A605C8D321ED5417218CD0FB9D186C4FB4797AC |
| dhe-reference-owner-plan-host-06/ExecutionPlanTests.dll | E2968DD9DB3D9D01EBC5E23FF22FF6C91FBB46426BE604CC3DA39583ED948545 |

All three workflows below pass 16 checks and the complete 46-case reference
sequence on both immutable Bases. The Current DLL sets are identical across
the two Bases for each resource; Base-specific plans remain separate.

| Resource output | Current-set SHA-256 | Manifest SHA-256 |
| --- | --- | --- |
| dhe-reference-owner-shared-resource-03 | e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24 | A873ED517B691A0E4DA597719C654D6EF68E83C6FA59A6F57D32B2256ABD58D6 |
| dhe-reference-owner-body-resource-03 | 1e58232fb8f0e723071e8b2cb959935148c7e416712f706c85ea80bd90047cb0 | C73B53D7D162FB122B559BA4B145A04D4EFBDF426B17C9A6F26BE153AD650B26 |
| dhe-reference-owner-dispatch-resource-03 | 339bec559c47d90bd2293f44169d07ed32a0bb65098feb84378aef869a24e17c | 014E9FB88706F3D04A60B778DBE79AAA3370D64161052A896F214E5FB9233295 |

The matching `*-resource-audit-03.json` reports independently rehash 126 files,
verify two Bases, 46 cases and four successful/restored runs each.

All twelve `dhe-reference-owner-base-<71|70>-<mode>-03` replays pass. Each Base
passes the six modes and exact counts from the table above: cold/cached native
interface callbacks, cached reference and generic identity, and cold/cached full
serialization/lifecycle. Current lifecycle selection is True,True for both;
the selection derives from their bound plans rather than their numerical Delta.

`dhe-reference-owner-public-probes-03` passes all 15 checks, binds 93 files and
verifies both original Base controls, full preparation failures without business
effects, and complete fresh-process Current recovery. The changed-body resource
requires exactly four Current generic calls on each Base, including direct,
reflection and delegate invocation. `dhe-reference-owner-dispatch-replay-03`
passes 13 checks on Base-70: its existing unchanged Payload/Envelope instances
report selected=False and interpreter=0, with AOT totals 30/37. Those totals
include reflection wrappers and are not a performance measurement. Base-71
introduces that helper as new Current code and is not counted as retained-AOT
evidence for it.

## Isolation and remaining work

All implementation work is on candidate worktrees. No formal branch, runtime tag,
remote, Installer default or CAT source changed. No cleanup or deletion occurred;
new large artifacts are on D. Preserve original failures and each immutable Base.
Rollback source, locks and resources as matched candidate sets; existing formal
defaults remain the deployment fallback. Native source changes require a new
Player; a resource-only rollback cannot replace the native runtime in an already
built Base. The preceding failed resources remain diagnostic history.
