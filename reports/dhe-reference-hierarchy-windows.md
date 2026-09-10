# Reference hierarchy queries: Unity 2022 Windows

The subsequent existing-interface removal/replacement checkpoint and its
compiler-method-flag limitation are recorded in `dhe-reference-interface-evolution-windows.md`.
Its evidence does not change the source identities or failures preserved below.

This continues the two-Base owner checkpoint in `dhe-reference-owner-windows.md`.
It remains an incomplete research candidate. Interface removal/replacement and
parent changes require actual Player qualification; policy admission alone is
not sufficient. No formal branch, runtime tag, Installer default or CAT changed.
Paths below are relative to `D:/hybridclr_artifacts`.

## Latest qualified query checkpoint

Immutable Base-73 passes all ten hierarchy queries, both cold and with cached
Base type/receiver handles, using the exact query-current02 DLLs that fail three
queries on Base-70. The selected sources are HybridCLR c16abc9, Unity 2022 IL2CPP
12b0e90 and package 39ef40f. No old Player was patched. This qualifies the tested
query correction, not arbitrary hierarchy changes or performance.

Base-73 and original-layout Base-71 also pass three shared resources with one
identical Current DLL set per resource, all 46 business cases and 16 workflow
checks each. Base-71 retains its older native runtime from the owner report;
the new hierarchy query itself is qualified on Base-73 only. Do not infer the
new query fix is installed in Base-71 by sending it these resources.

## Preserved reproduction

Lab f8db9f6 compiles the optional hierarchy probe into callback Current02 without
changing its 46 business cases. `dhe-reference-hierarchy-query-current-02` and
`dhe-reference-hierarchy-query-resource-02` pass CLR reference and the ordinary
resource workflow on immutable Base-70. Host03 SHA is
`0010CE495EDC0F152A66AEA25DBF157B38071AF86288F50E8A717567E62BD51F`;
the tool is owner-tool03 from the preceding report.

`dhe-reference-hierarchy-query-before-01` fails precisely three checks:
public-type-interface-list, instance-type-interface-list, public-interface-by-name.
The seven ancestry/generic-parameter/field/lifecycle-state controls pass. Player
and resource hashes are unchanged. Its bound Player-result SHA is
`F418E6C85F51B1D0825CAF9B5326152329736ECA517BA9DF56414A41A96136C2`.
The replay's result.json SHA is
`72165AAA339C38869A3D5F3DDBAF796EA2278C6D4642E89256680C9E7A922377`.
Current interfaces execute but the public reflection queries still enumerate Base.

The earlier query-current01/resource01 failure belongs to the fixture: CLR JIT
resolved Unity locals even with the optional flag absent. f8db9f6 separates a
NoInlining Unity body from the flag-only entry. Keep those original artifacts.

## Candidate sources and native gate

- HybridCLR c16abc97f4eecc96dfab2ef0eb8d1c812281cb26 selects the owner of a native
  interface cursor and excludes generic-parameter/pointer metadata from reference
  allocation TypeDef lookup. Canonical source SHA:
  `B21D998EACDFC4AEAF81D05A070B9E5140481B05A5965D622637F5D6589D3841`.
- Unity 2022 IL2CPP 12b0e900bbf0d28246664e6bcb7ead06a76d7826 resolves selected
  reference declarations in public interface, parent and assignability queries.
  Canonical source SHA:
  `A0E98B48C6D7B379B7AD72DA66E6B52AA03F3B9D492C0C5C0EA2ECFF24F23851`.
- Package 11feea7 adds the candidate interface-evolution capability, but has an
  Editor/runtime inventory mismatch; it does not produce a usable Player.
- Package 39ef40fdd5451a72d1775d246c64b1ae7b93c882 fixes that mismatch by deriving
  the Editor inventory from `DheRuntime.GetSupportedRuntimeCapabilities()`.
  It returns a copy, preserving runtime validation against caller mutation.
  Canonical package SHA, with the existing ignored meta path:
  `02E8F9921EAF5EF37DD52025BE80180006E9E1FB499887E2353244029E8E944D`.

Internal Class ancestry and receiver/offset guards remain unchanged. Initial
native interface enumeration selects Current; a non-null cursor inside the
immutable requested table finishes that same table, including repeated end calls.
The existing registration acquire and metadata lock govern selection; no new
cache or object migration is introduced. Windows x64 does not qualify ARM64.

At lab 9aefc23, runtime01/native01 pass real Unity 2022 headers, compile and CTest:
mergeReady=true, surrogateExternalHeadersUsed=false. Runtime01 manifest SHA:
`50468764E4E06203AA49A641A3E7447E262AD63800C3876393C68E7411906880`.
This manifest binds the earlier package 11feea7. Managed01 passes 113 checks;
owner-plan01 passes 38, generic-plan01 passes 17, policy-after02 passes 26.

## Preserved Base build failure

`dhe-reference-hierarchy-base-72` was built at lab 9aefc23 with hierarchy-host04,
hierarchy-tool01 and runtime01, using the same evolved input DLLs as Base-70.
Editor build, native finalization, ordinary guard coverage and identity schema
finish, but original Player startup rejects the embedded identity. The Editor
inventory omitted `physical-current-interface-evolution-v1` while the runtime
required exact inventory equality. No DHE assembly loads and no business runs.
Keep Base-72, its inputs, generated C++, logs and failure report unchanged.

Its player-result.json SHA is
`71A8FC1A9FD1F020D7DC5F1FF8C89FE083D990FE673BB1F2468EC832CDF5B038`.
An independent comparison of the frozen build identity and runtime inventory
confirms that only the new interface-evolution capability was missing.

## Shared inventory correction and Base-73

At lab d0c546a, hierarchy-runtime02/native02 pass real-header compile/CTest with
mergeReady=true and surrogateExternalHeadersUsed=false. Runtime02 manifest SHA:
`EFFCD62BC1CB32C6733F7EC68AB5780DC2641E01DCA34D55A4E23E0CC911406E`.
The native commits are unchanged from native01; this manifest binds the corrected
package 39ef40f. Hierarchy-managed02 passes 114 checks, including mutation of the
exported capability array without changing runtime identity validation. As in
the earlier managed suites, native calls in this host are recorded, not executed.

Base-73 was built at d0c546a using `dhe-unity-input-base-04`, revision 59 and all
ordinary AOT guards. Build/startup/schema/generated no-op all pass. Base ID:
`012d6f2ea8b0da0e2325b24c886b19bd8747c859e1040e006ddb5496cf6331b3`.
GameAssembly SHA:
`1BB59EBD30A89170475C2B38FCAE74872B66C79AF72A7CAE84B6B92471128BE7`.
The original Base-72 failure remains preserved under its own identity.

| Artifact | SHA-256 |
| --- | --- |
| dhe-reference-hierarchy-tool-02/HybridCLR.DheTool.dll | 20A66DBD6904C216B97936304C01EED87465669EAD2ACB389FADF40A74D38250 |
| dhe-reference-hierarchy-host-05/AotSnapshotTests.dll | C52641CE8E72438E951E359AF141A78EC56D65D405CF52B1FE5A76B0C4504AA5 |
| dhe-reference-hierarchy-plan-host-02/ExecutionPlanTests.dll | 0556E9970FDB4A8AFB303F756F605FC9D97FD4DC9290AAE9ABA9284BAF5F8759 |
| dhe-reference-interface-evolution-host-01/AotSnapshotTests.dll | 0986A7478A90BC346BEBA7D7BE696AB7D0379C9BCC77E59921137142DBE36730 |

The first three binaries bind lab d0c546a. The final host binds a5b3b7b in the
separate `research/dhe-reference-interface-removal-v8.13.0` lab worktree, prepared
while Base-73 built against the unchanged d0c546a hierarchy worktree. It adds
cached query replay and the next interface-removal fixtures. It does not rebind
Base-73's build evidence to a later source commit.

## Current shared resources and Player regressions

All three resource workflows use Base-73 followed by Base-71. Base-71's ID and
GameAssembly remain those in `dhe-reference-owner-windows.md`.

| Resource | Current-set SHA-256 | Resource manifest SHA-256 |
| --- | --- | --- |
| dhe-reference-hierarchy-query-resource-03 | a21773c1e64293a66138fabdd26ccf067b9f94738157b76b39251d7567df15c5 | FA278E8473F300C513475E8A1B4DDCB6D9B3B0E7624472C598135E953857D103 |
| dhe-reference-hierarchy-body-resource-01 | 1e58232fb8f0e723071e8b2cb959935148c7e416712f706c85ea80bd90047cb0 | 56E5613EBDC15F0022315B9633A4E588B26DEE8B813736BD6ECCA103960BE56C |
| dhe-reference-hierarchy-dispatch-resource-01 | 339bec559c47d90bd2293f44169d07ed32a0bb65098feb84378aef869a24e17c | BEBEAC443B0BA0CCE52D160123B2A562B645F3A4C95DAEEDD1D9E9B6BCA48DBC |

Each `dhe-reference-hierarchy-<query|body|dispatch>-resource-audit-01.json`
independently passes two Bases, 46 cases, four successful/restored runs and 126
bound files. DLL payloads and immutable Player hashes are verified independently
of the resource runner.

The query resource has fourteen passing explicit replays:

- Base-73: hierarchy-query (10), hierarchy-query-cached (11 receiver + 10 query),
  plus all six adjacent modes listed below.
- Base-73 and Base-71: reference-cached (11 + 14), reference-generic-cached
  (11 + 18), reference-callbacks (9), reference-callbacks-cached (11 + 9),
  full-unity (11 serialization + 17 lifecycle), full-unity-cached (11 + 11 + 17).
- Directories: `dhe-reference-hierarchy-base-<73|71>-<mode>-01`.

`dhe-reference-hierarchy-public-probes-01` passes all 15 checks, including both
original Base controls, deliberate preparation failure without business effects,
and full fresh-process Current recovery. The runtime remains single-Current per
process; this is resource replacement between processes, not assembly unloading.

`dhe-reference-hierarchy-dispatch-replay-01` passes 13 checks on Base-73. Its
existing Identity<Payload> and Identity<Envelope<Payload>> report selected=False,
interpreter=0 and AOT totals 30/37. Totals include reflection wrappers and are not
performance measurements. Base-71 introduces that helper as Current and supplies
no retained-AOT claim for it. The body resource has exactly
`DHE changed generic body pass: 4` on each successful Player run on both Bases.

## Next interface-evolution fixture

At a5b3b7b, `dhe-reference-interface-<remove|remove-methods|replace>-current-01`
compile with the Unity compiler and each passes the unchanged 46-case CLR
reference. The optional 16-check Unity suite distinguishes removed interfaces
from retained callable methods, removed implementations, replacement IDisposable
dispatch, and absence of JSON/clone callbacks. These optional checks have not yet
run on a Player. Retained callback methods keep their valid virtual IL flags;
this does not qualify arbitrary compiler changes to method modifiers.

`dhe-reference-hierarchy-interface-base-input-01` is prepared from callback
Current02 plus the captured ordinary Native DLL, with Base entry revision 59.
Next build a new immutable Base containing that callback interface in AOT, then
send all three resources to it and Base-73. The earlier Base-73 has no callback
interface in Base and cannot alone prove removal of an existing interface.

Actual parent changes, broader generic/value hierarchies, pre-existing objects,
scene/Prefab, native boundaries and performance/memory remain open. The full DHE
goal is not complete. All source changes are candidates with no publication.
No files were deleted; all new large artifacts are on D. Rollback requires the
matched source/lock/package set; changing native source requires a new Player,
and a resource-only rollback cannot install the query fix into older Bases.
