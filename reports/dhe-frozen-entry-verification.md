# Frozen AOT Entry Verification

This candidate tests Unity 2022 Windows only. It is not release admission,
Android/iOS qualification, or a performance result.

## Current Verified State

The latest standard-resource checkpoint is documented in
[frozen resource admission](dhe-frozen-resource-admission.md). Package `187af4f`
now authenticates frozen sources with the Base's embedded snapshot hash. Two
immutable Windows Bases (original revisions 41 and 59, different Payload layouts)
load one identical Current resource set through normal generation/staging/public
loading and both execute revision 73 after field-copy assertions. Complete
ordinary coverage and a real Player snapshot-rejection test pass. Remaining
storage/ABI, broader resource behavior, performance and Tuanjie gates stay open.
The following proof-15 results belong to the earlier native diagnostic checkpoint.

As of 2026-09-10, runtime `b3e72d815f935fad25e3f255bf9a611264901622`,
Unity 2022 IL2CPP `8a13baf1ec45068fbb9535beea03425b717f501b`, package
`3c9558cab91d383b9825e4dbcd349a43b7cabaa6` and fixture lab `c034236` passed
the real-header Unity 2022 native gate and proof-15 Windows core (35 checks).
All 40 ordinary Base assemblies and 49,084 executable method requests have
complete final-native coverage. The same immutable Player passes old-values
(45), nullable (39), generics (37), arrays-byref (38), and dynamic collections
(41). Swapped collections (41) and reversed old-values (45) also pass. All counts
include the core checks; they are not independent case counts to add together.

The previously failing unaffected Nullable<long> now retains AOT. Old boxed
Payload, nested and generic values can be copied into independent Current
storage while preserving retained fields and defaulting added fields. See
[generic dispatch](dhe-generic-context-dispatch.md) and
[boxed value copy](dhe-boxed-value-copy.md) for earlier evidence;
[complete ordinary guards](dhe-complete-ordinary-guards.md) records current
identities, the corrected final-strip/slow-startup failures, and exact results.
List/Dictionary operations preserve Current fields while unchanged long
collections retain AOT. Guard lookup no longer enumerates metadata for each
unchanged native call. This correctness result is not a performance claim.

Proof-15 is a diagnostic native-source workflow, not formal resource release
admission. Source-plan and resource-path binding passed 29 real-snapshot/CLI
checks and 74 managed host checks; see [resource binding](dhe-frozen-resource-binding.md).
At proof-15, frozen-source admission/resource loading and the multi-Base route
were still unverified; the linked newer checkpoint closes those representative
cases. Broader startup reflection, initialization and cyclic/concurrent
publication remain subsequent gates. Windows correctness precedes the Tuanjie port;
Unity 2021 is outside the current user scope. No performance or mobile claim.

## Earlier checkpoints (historical identities)

Runtime `d6aab95d8aa12f467abcfdc9105cd87a29309580`, Unity 2022 IL2CPP
`8a13baf1ec45068fbb9535beea03425b717f501b`, package
`2d4c3845d8716087c49ec9d50176abd4e1253548`, fixture lab
`60d2765c2815990163943fea8aa55f4a100f48d8` passed the native gate and the
34-check Windows frozen-entry probe. Native evidence is
`artifacts/dhe-batch-initialization-20260909/native-04/DHE-Unity2022/native-gate.json`
with real Unity 2022 headers and no surrogates. This flag is not release admission.

`artifacts/dhe-frozen-entry-proof-08` (PID 4896) rejects an invalid Base MV
after metadata preparation, keeps Base dispatch (AOT count 1, interpreter count
0), hides the added field and retains the original Count. Corrected MV retry
then passes direct and inline-owner added-field preservation, reflected value
return, exception virtual behavior and unchanged AOT sentinels.

The same immutable Player, DLL and MV bytes pass all 34 checks in each replay:

| Directory suffix after `dhe-frozen-entry-proof-08-` | Order | PID |
| --- | --- | --- |
| order-original | Native, mscorlib, Consumer, Model, Other | 41556 |
| order-swap | Model, mscorlib, Consumer, Native, Other | 56868 |
| order-reverse | Other, Model, Consumer, mscorlib, Native | 53856 |

All runs retain GameAssembly SHA-256
`F8C3B92F3C62D6DD50EDCCC98CE67AE90CEA640273E9725CC89FDAE28CDD6BA3`.
This resolves the previously reproduced order differential for this graph; it
does not establish arbitrary cyclic graphs or concurrent publication correctness.

The next fixture adds independently selectable nullable, generic, array/byref
and pre-existing boxed-value probes. They reuse one Player through
`replay-frozen-entry` with an optional capability argument and retain every core
assertion. The runner checks the reported capability, so an older Player cannot
silently pass a capability it does not implement. The missing-capability negative
check correctly rejected proof-08 (PID 58220) despite its process returning zero.

`dhe-frozen-entry-proof-09`, fixture lab `8e7f235`, uses the same runtime/package
commits as proof-08 and passes the 34 core checks. GameAssembly SHA-256 is
`F322C80FC0933841F6DFB2D44EA5D9E3BBB55092A1BC67E514F08F90E28BC477`.
Its immutable replays produce these results:

| Suffix after `dhe-frozen-entry-proof-09-` | PID | Outcome |
| --- | --- | --- |
| generics | 54160 | 36 checks pass, including generic value container and open generic method copies |
| arrays-byref | 16344 | 37 checks pass, including clone, array element and reflected byref copies |
| nullable | 49020 | managed reflection rejects Payload as an argument to Nullable<Payload> |
| old-values | 55968 | old boxed Payload cannot be unboxed as its Current representation |

The next planner candidate distinguishes an affected generic instantiation from
an affected generic definition. A frozen `Nullable<T>` definition is unchanged;
its Current argument can produce the required new closed layout while retaining
the core-library identity required by IL2CPP nullable handling. All affected
owner methods still require frozen IL and native guards. Owners with concrete
affected fields retain physical definition selection. `recompile-frozen-entry` reauthenticates an existing Player,
native manifest and prior proof, then regenerates only the diagnostic resources
and records the source evidence hash. No Base rebuild or binary mutation occurs.

### Resource-Only Generic Definition Result

Planner lab commit `565e5e5` produced
`artifacts/dhe-frozen-entry-generic-definition-01` against the immutable proof-09
Player. Every DLL, Base MV, Current MV and method selection matches proof-09.
The only selection difference is removal of corlib type token `0x02000161`
(`Nullable<T>`) from physical definition selection. The ordinary inline owner
and mutable storage selections are unchanged.

| Suffix after `dhe-frozen-entry-generic-definition-01` | PID | Outcome |
| --- | --- | --- |
| (none) | 40056 | all 34 core checks pass |
| -generics | 7320 | all 36 checks pass |
| -arrays-byref | 51100 | all 37 checks pass |
| -order-swap | 53000 | core and generic checks pass in swapped order |
| -order-reverse | 42308 | core and array/byref checks pass in reversed order |
| -nullable | 56328 | affected nullable copy and null pass; unaffected Nullable<long> fails the native ABI guard |

This is a partial correction. Frozen methods are still selected at definition
granularity, so the selected Nullable constructor also marks Nullable<long> as
requiring interpretation. The open-definition ABI guard rejects its native
entry even though this closed instance is unaffected. Do not weaken the ABI
guard or claim Nullable support from the first two checks alone.

The next implementation needs an authenticated distinction between unconditional
method dependencies and methods selected only because their generic arguments
change. The runtime must retain AOT for unaffected closed instances and remap
affected instances before creating their Current call frames. Native selections,
retry identity, resource-plan bindings and managed loading must carry the same
distinction. The negative guard for genuinely changed value frames remains
mandatory; matching names or sizes is not sufficient to enter an old native ABI.

Old boxed-value adaptation remains a separate failing gate. Universal guard
admission, managed resource authentication/staging, multi-Base qualification and
performance also remain open.

This iteration changes the lab candidate only. Runtime/package candidate commits
remain those listed above. No formal maintenance branch, tag, remote, Installer
default or CAT checkout was changed. C: retains about 73 GiB free after the two
new Player builds, so no cleanup was needed.

## Workload and Historical Findings

The workload keeps the configured hotfix set unchanged. Ordinary AOT DLL bytes
remain frozen. It builds guards from actual Unity-stripped method tokens, then
loads selected frozen sources together with evolved hotfix images through the
public source-role transaction. Direct `object -> object` native calls exercise
unbox/copy/box and an ordinary inline value owner. Reflection separately exercises
the value-signature Echo method. AOT and interpreter counters distinguish routes.
Only the diagnostic Nullable method inventory is guarded in mscorlib; universal
ordinary-AOT coverage for arbitrary future updates is still an implementation gate.

Required checks: old-layout AOT copy before registration, authenticated Base and
DLL/MV identity, complete selected method guard coverage, atomic source load,
Current field preservation on direct and reflected copies, unaffected ordinary
method staying AOT, unchanged hotfix sentinel, immutable Player/GameAssembly hashes.
No performance sampling is meaningful until these correctness checks pass.

Earlier exploratory failures do not prove ABI truncation: the Echo assertion was
inside a missing optional ResourceEvolutionProbe branch, so it was never executed.
The :evolve: Base revision mismatch was a test argument error, not an unstable
unchanged-method predicate. Both checks are restored, without relaxing runtime
semantics. Resource admission no longer suppresses ABI obligations merely because
a source assembly name is present in a plan.

The independent command is `frozen-entry-workflow` in AotSnapshotTests. Native
failure reports must be retained. A passed probe will still require resource
schema/guard authentication, old-object migration, multi-Base, generic/native ABI
and platform qualification before formal opt4 release. No native code or project
generation output is manually patched by this fixture.

## Generic Signature Initialization Regression

The `dhe-frozen-entry-proof-02` Windows Player passed Base construction and
pre-load AOT checks, then exited with 0xC0000005 in the source batch. Its native
stack connects `InitTypeDefs_1`, `Image::ReadGenericClass`, `GenericClass::CreateClass`
and `InterpreterImage::GetTypeInfoFromTypeDefinitionRawIndex`. Signature decoding
was materializing a hidden Current class before `InitClass` initialized its table.
This is a failed native integration regression, not a completed execution proof.

Candidate runtime `684672a5d5078d1233143eb8d5da7aa57f73ac38` keeps generic and
array remapping at the metadata-description level, preserves signature flags,
and never materializes a Current generic definition from `ReadGenericClass`.
The same five-source frozen-entry workload must pass unchanged on a freshly built
Player. Native compile/CTest is a separate check; its stubs do not execute image
initialization and cannot replace this Player regression. No latency or memory
claim is made. Unity 2022 Windows is the current target; Tuanjie follows after
correctness is stable, and Unity 2021 is outside the user's current scope.

### Verified Partial Result

`artifacts/dhe-frozen-entry-proof-03` used runtime `684672a`, IL2CPP `bda33548`,
package `2d4c3845`, lab `7d7c1fd`. The Unity 2022 native gate passed (9 test groups,
real headers). The five-source Player transaction returned OK; Current fields
were visible and the direct frozen copy preserved the added long and object
reference. Player/GameAssembly hashes remained unchanged. The complete probe
failed at `ordinary-inline-owner` with 0xC00000FD (stack overflow).

The dump is retained as `dhe-frozen-entry-proof-03/crash.dmp`. Raw stack address
sampling, not a full unwind, identified repeated `ResolveDheVirtualEntry`,
`TryGetDheVirtualInvokeData` and `Exception.get_StackTrace` entries against this
Player's PDB. Code inspection found frozen source registration was enabling
mutable virtual hierarchy handling for every mscorlib type. This must be fixed
before the original inline-owner error can be reliably reported. The next
candidate distinguishes mutable assemblies in virtual/interface eligibility,
retains ordinary frozen native overrides, and adds an exception virtual-call
regression. Inline-owner and reflected value-copy assertions remain mandatory.

### Source Order Differential (Current Candidate)

Runtime `c065ce6a7be923703584a29e1fabb01faae32c6f`, IL2CPP
`bda33548e37ec79f11996b2e26cd2f2ed044fead`, package
`2d4c3845d8716087c49ec9d50176abd4e1253548`; Player fixture lab `458a8d7`,
replay host lab `59c4570ad0d422d5193fd1d8caa982004346b96e`.
No formal branch, tag, Installer default or consuming project was changed.

The native compile/CTest gate is
`artifacts/dhe-generic-signature-20260909/native-03/DHE-Unity2022/native-gate.json`:
passed, `mergeReady=true`, `surrogateExternalHeadersUsed=false`. These flags
apply to this native gate only. The restored managed execution-plan suite passed
50 checks in `artifacts/dhe-frozen-entry-static-check-02.json`; it records native
arguments on the .NET host and is not native Player coverage.

`artifacts/dhe-frozen-entry-proof-04` built and validated its Base successfully.
The five-source transaction and exception virtual-call regression passed. The
remaining inline-owner failure is now reported normally as an assertion failure,
not a native crash. Original order is Native, mscorlib, Consumer, Model, Other.

`replay-frozen-entry` reuses that exact Player and all original DLL/MV bytes. It
authenticates the prior plan/evidence and binary hashes, accepts only a complete
permutation, writes a new plan, and records new process/result evidence.

| Evidence directory under artifacts | Order | Result |
| --- | --- | --- |
| dhe-frozen-entry-order-original | Native, mscorlib, Consumer, Model, Other | PID 47148: inline added-field preservation fails |
| dhe-frozen-entry-order-swap-owner | Model, mscorlib, Consumer, Native, Other | PID 53540: all 29 checks pass |

Both replays use host SHA-256
`F41A35FEF106D3FBDE325C8AB0922FE69C3D4D311A6681C9C25DBBDFD068DA20`
and GameAssembly SHA-256
`14E917C987AA5FBD842FAF38247A3FD5AAAA6F8E1B6F139DE083128BF91B7E3E`.
Player/GameAssembly are unchanged before and after each run. The passing order
proves direct frozen copy, inline owner, reflected value return, ordinary AOT
sentinel and unchanged hotfix predicate for this workload. It does not prove
order independence, arbitrary updates, multiple Bases, or the managed resource
release workflow.

The confirmed blocker is order-dependent cross-assembly storage binding. The
current loader fully initializes each image before the next: `InitMethods` and
`InitFields` can materialize an ordinary owner's physical layout while its
selected hotfix value definition is still unavailable. Swapping only Model and
Native changes the result. Manual source ordering is a diagnostic, not a fix.

Next implementation boundary: prepare every batch image and its type/generic
definitions, bind Current references across the entire batch, then initialize
layouts/method metadata and publish dispatch. Pending lookup must stay private
to the metadata-locked preparation; failures must retain referenced allocations
without publishing a partial graph or allowing an incompatible retry. Both
orders above must pass unchanged, followed by cyclic cross-assembly references,
failed registration/retry and concurrent access tests. This phased initialization
was not yet implemented at that evidence point. Universal guard admission, frozen managed-loader
authentication/staging, old-object/static state and multi-Base qualification remain
separate open gates. Overall status: research candidate, not opt4 release-ready.

### Batch Initialization Candidate

The next runtime splits interpreter metadata into definitions, details and
finalization. All batch type/generic definitions exist before any peer signature
is decoded; all details exist before classes/layouts are materialized. A thread
local preparation scope supplies type definitions under g_MetadataLock without
exposing incomplete images to other threads. It is removed on exception. Images
are registered only after the complete graph initializes. Finalizer inheritance
is resolved across peer images after their method/parent information is ready.

Pending images retain the full batch membership and exact source/selection
identity. MV registration failure can reuse that graph with corrected MV data.
Metadata-initialization failure retains allocations and fails later attempts for
that graph; restarting the Player is required because cached references cannot
be safely freed or rebound. No partial dispatch is intentionally published.
Validation must exercise original and permuted orders, MV failure/retry and
cross-assembly cycles; no passing result is implied by this design note.

The first phased candidate `fa416e3` passed native compile/CTest and Base Player
checks, but `dhe-frozen-entry-proof-05` failed during the initial invalid-MV probe
with `MissingMethodException: IEnumerable<char>::GetEnumerator`. It did not reach
the expected MV rejection or the layout assertions. Review found the new private
registry also made frozen corlib visible to Current virtual declaration mapping.
Candidate `1bc8547` restricts frozen `GetDheCurrentType` to explicit physical
selections; unaffected frozen interface/Object declarations keep their Base
identity during preparation as well as after dispatch publication.

Candidate `d6aab95` additionally keeps generic signatures in their logical
TypeDef/TypeRef domain while matching Base fields and methods. The previous
candidate changed `Nullable<long>` to an execution Current generic during
signature decode, causing `ParallelLoopResult::_lowestBreakIteration` to be
reported as an unsupported supplemental field before MV rejection. Runtime
execution sites still remap selected physical owners; this change only removes
the premature remap from signature matching.

`dhe-frozen-entry-proof-07` with runtime `d6aab95` reached the expected MV
registration rejection. Its combined post-rejection assertion then failed.
Review found `RuntimeType.GetFields_native` selected Current fields from a cached
homologous image without checking successful dispatch publication. IL2CPP
candidate `8a13baf` adds the acquire-state gate. The Player now records AOT and
interpreter counters and checks dispatch, field visibility and retained Count
separately before retrying the correct MV, so these outcomes cannot be conflated.
