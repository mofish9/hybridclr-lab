# Existing generic interface evolution

## Scenario and fixed boundaries

The preceding six-Base checkpoint only adds generic interface types. Build a
further Base generation containing ICrossRevisionValue<T>, its existing native
implementation and explicit native callers of RoundTrip for int, string and a
reference-containing struct. Current adds two interface methods before RoundTrip
and implements them on the already-native generic class. A native child of the
closed generic class inherits the new implementations. New boxed/constrained
value-type implementations and a new child also exercise interpreted receivers.

Keep the preceding six v22 Bases, all original DLL/MV inputs and reports intact.
Use the same v22 native/package sources until an actual failure requires a fix.
The new Base is necessary to put the generic interface into native metadata;
later resources must run on this Base and on the older generations. It is not a
Player rebuilt to accommodate an already-released update.

## Acceptance

Preserve 52 prior evolution groups and the 220-case golden. Add nine groups for
unchanged native callers, new interface dispatch, generic interface methods,
delegates, reflection, maps, inheritance, boxed/constrained calls and identity.
First/latest resources must have observable different main results. Validate the
exact shipped DLLs in CLR, use independent Player processes for consecutive and
skipped updates, and rehash all protected Base files. No pre-touch or padding.
Unchanged caller fingerprints must remain identical and actual method routing
must establish that those calls retain AOT. A changed caller cannot prove native
slot translation. Int/reference/struct instantiations and generic method calls
must return distinct expected values so an old slot selecting a newly inserted
method fails visibly.

Offline analysis must recognize closed generic implementing parents by their
metadata definition names. A Base lacking inherited-interface-dispatch-v1 or
base-virtual-slots-on-current-descendants-v1 must not pass simply because its
parent reference includes type arguments. First reproduce this analyzer gap
with the actual generated fixture, then fix it without changing MV serialization.

Run native compile/CTest against real headers for any native change and then
new exact-identity Players on all engines. Begin with Windows U21; extend to U22
and Tuanjie after that replay determines whether the existing runtime works.
Do not infer native correctness from the analyzer. Existing pools/metadata locks
and generic inflation are the first audit area if execution fails.

Correctness is the primary metric. Dispatch cost, allocation and resident memory
are secondary and remain unqualified; no performance claim follows from these
tests. ARM64, Unity serialization, general virtual/value-layout evolution, live
field-cell expansion, external AOT APIs and broader GC/ABI stress remain open.
No CAT, Installer-default, formal branch or tag change is part of this fixture.
Rollback selects a matched older runtime/package/tool and its proven resources.

## Initial offline reproducer

The actual raw fixture at generic-interface-capabilities-before passes identity,
unchanged-caller and slot-change checks, but fails both inherited capability
requirements and their missing-capability rejection checks (13/17 pass). Resolving
the parent through DefinitionName fixes all 17 checks. The caller evidence gate
adds nine positive/negative tests: it requires a complete unique method set,
MV-matching routing, nonnegative counters and actual entries on the expected path.
The complete 26-check report is generic-interface-capabilities-routing.

The Player fixture records routing after exercising each cold caller; the runner
independently derives expected routing from archived Base MV and actual Current
DLL metadata. Where the three callers already existed, all three must be unchanged;
new interpreted callers on older Bases are recorded as such. Missing routing
evidence fails the run. CLR references do not pretend to provide native evidence.

Reproducible source builds use the C# fixture executable:

```text
dotnet run --project tool/fixtures/generic-interface/GenericInterfaceTests.csproj -c Release -- build base <lab> <immutable-seed> <new-output>
dotnet run --project tool/fixtures/generic-interface/GenericInterfaceTests.csproj -c Release -- build current <lab> <immutable-seed> <new-output>
dotnet run --project tool/fixtures/generic-interface/GenericInterfaceTests.csproj -c Release -- analyze <Base-DLLs> <Current-DLLs> <new-output>
```

The builder records symbols, source commit/tree/status and assembly hashes, and
preserves the metadata-stress seed. Native, package and installed-source locks
remain those of v22; no runtime change is justified by this offline gate alone.

## Reproduced native no-op failure

The clean 2217e78 workflow builds the v22 U21 Player successfully, then its
unchanged Base/Current startup fails seven existing cross-interface groups.
Registration is OK, changedMethodCount=0, and noOpAotBehaviorValidated=true.
The failure is DHE interface implementation has no callable entry for the
already-native generic interface method EchoAdded<T>. The failed BaseId is
d04df52b4b9bdc70ca043af70f85b05a9db8c6bb5f023ea143378f3966ccd3d0.
Its dhe-player-result.json SHA-256 is
277B3DEE4839ACDF7DD8599D94DFFE148730205C417964D68AACD313BFA921C4.
Preserve base-existing-generic-interface-u21 and both frozen raw input sets.

IL2CPP's generic virtual/interface callers first request the target method
definition from the vtable, then apply the caller's method arguments through
GetGenericVirtualMethod. An open native generic method definition legitimately
has no callable pointer. The DHE resolver currently requires a pointer before
returning this definition, so it fails before ordinary generic inflation runs.
The same requirement exists in the inherited virtual-slot resolver.

For an is_generic target, return a method-only dispatch record and defer callable
selection to existing IL2CPP generic inflation. Continue requiring a callable
entry for every nongeneric target; retain missing/abstract method rejection.
Audit every consumer: codegen generic calls, Object/Class virtual resolution,
ldvirtftn and interface maps read the method before inflating it. Do not insert a
callable stub for an open definition or force unchanged methods into IL.

Cache identity/lifetime and metadata lock ownership stay unchanged; completed
method-only records are still inserted last. No object layout or new publication
field is needed. Require open-generic-dispatch-definitions-v1 when Current retains
an already-native generic virtual/interface method. Previously proven Bases that
do not contain those methods remain eligible. New package/source identities must
be bound before three-engine compilation and rebuilding this exact Base fixture.
Actual no-op plus resource replay must verify the repair; it is not yet qualified.

## Resource replay exposes three remaining generic metadata gaps

HybridCLR ece8419 / package b30b472 (v23) pass all three real-header compile/CTest
gates and the unchanged U21 Base/no-op workflow. The repaired BaseId is
ba753d9042e6f8a3aabc34008d2faf4dca17f5ef12cb3dbc17ab13c3fa3632d1.
The packaged command rejects the failed v22 no-op Base with exactly the new
open-definition capability error and emits no final manifest/plan.

Clean 74e9714 then fails all nine resource runs across three U21 generations.
Original/evolved v22 Bases execute 58/61 groups; the v23 generic-interface Base
executes 53/61. The prior native generic caller group succeeds on the latter,
recording Integer/Reference/Value as unchanged with 3/3/2 AOT entries respectively.
The entire replay is failed. All nine process IDs are unique and all 57 protected
files independently rehash unchanged. Report SHA-256:
36701FCE3875678BD420EE89BA07C29960F6D96806F3CFF0A07972DB08F00610.

1. GetFirst/Next/CountSupplementalMethod look up only an exact Base class pointer.
   Closed generic classes therefore omit methods registered on their definitions,
   causing AddedValue/EchoGeneric lookup failure and incomplete reflection maps.
   Inflate the immutable supplemental method list per closed class, cache a complete
   vector under g_MetadataLock, and use it consistently for iteration and counts.
   Vector/node addresses must remain stable; never publish a partly inflated list.
2. ComputeVTable's opt3 parent-slab shortcut takes a generic parent's uninflated
   definition table for a nongeneric child. Its inherited generic interface offsets
   retain open arguments and cannot match the requested closed interface. Exclude
   generic-instance parents from that shortcut and use existing full vtable setup.
   Retain the shortcut for nongeneric parents; no new cache or publication is needed.
3. All three engine RuntimeMethodInfo implementations leave class_inst handling in
   GetGenericMethodDefinition_impl unimplemented. For DHE methods, strip only method
   arguments and retain class arguments through GenericMetadata::Inflate. Reuse the
   existing reflection cache and preserve the reflected type. Leave external ordinary
   AOT behavior outside this DHE hook unchanged.

These fixes need separate capabilities for closed supplemental methods, interpreted
generic-parent tables, and closed generic method definitions. Require them from actual
method/type declarations, including wholly new DHE types where relevant. Keep the
61-group DLL payloads and failed Bases unchanged. Rebuild supported Base generations
with matched sources, run the same payloads, and then extend the Windows engine matrix.
None of these source diagnoses constitutes passing execution evidence.

The first v24 focused replay at 8463730 never reaches runtime registration:
its copied AOT metadata paths are 261 characters, beyond this Windows Unity
Player's file API limit. The files exist and the .NET stage hashes validate.
Keep that failed report (CC8F46F1F15CAE4AD4FD35F290C8FE659A068DF1968FA86CB902829925728777).
Use the shorter replay-gm-u21 output and reject staged runtime asset paths of
260 or more characters before launching Windows Players. Base/resources and
all required assertions remain unchanged; this is a harness path correction.

## Remaining MemberRef signature mismatch

The shorter 0f00124 replay reaches registration and executes 56/61 groups in
all three processes. Reflection, interface maps, native callers and type identity
now pass. Five direct/delegate/constrained groups still fail to resolve AddedValue
or EchoGeneric on ICrossRevisionValue<int>. Preserve replay-gm-u21 (report SHA-256
DE6AB6E7165FBDD6B7540FDFC22CEE1F6645ECEE8FD2127AA4995FDD041FC01B).

Image::ResolveMethodInfo compares a raw MemberRef signature containing VAR/MVAR
ordinals against the now-inflated supplemental MethodInfo, whose class arguments
are already int/string/etc. It also supplies the Base generic container, although
the supplemental definition's parameter handles belong to Current. Match the
uninflated method definition and its actual metadata declaring-type container,
then return/inflate the already-selected logical method with the caller context.
This preserves the distinction between overloads taking T and concrete types;
matching two fully closed signatures would lose that distinction.

No new runtime cache, publication field or object layout is needed. Declare
supplemental-generic-memberref-signatures-v1 for added members on existing generic
types and retain the exact DLL payloads for the next replay. Passing reflection
alone is insufficient to declare those members callable from IL.

The first signature candidate dc1d1fd fails all three native compile gates with
C2440: GetGenericContainerFromIndex returns an opaque handle. The U21 workflow
also fails native compilation and exits normally, preserving its compiler
transaction cleanup and failed artifacts. Commit 0787aad adds the explicit cast;
the runtime remains v25 with the same capability. These failed roots are
native-generic-signatures and base-generic-signatures-interface-u21. New staged
sources, compile gates and Players must use the corrected commit.

## Corrected signatures and a retained-entry harness failure

The corrected 0787aad native candidate passes all three real-header gates and
the U21 Base/no-op workflow at clean tool source 19b0ef3. The frozen payload
replay at clean f3785c1 passes latest and skipped-latest: 61 groups, 220 cases,
zero differences and 220 interpreted case receipts in each process. First
executes all 61 groups, but the Player exits 1 because dispatchProbeValidated
still requires one of three fixed entry methods to have changed. The complete
replay remains failed at replay-mr-u21 and must remain unchanged.

Its actual Base/Current MV and runtime receipt agree: ExerciseCurrentMembers
is unchanged, executes once, and its call contains 39 interpreter entries.
The same fixed-entry rejection is also present in the old v24 report. Keeping
this wrapper in AOT while its callees change is intended differential behavior.
The mixed-dispatch fixture reproduces the erroneous rejection using the real
DLLs and failed Player result (13/14 checks before repair).

The Demo now accepts the existing MV-bound structural-call receipt as execution
evidence, while preserving changedProbeChanged=false for an unchanged probe.
The host independently validates MV, native routing, execution, and counters.
Negative tests reject zero/negative counters, missing execution, wrong native
or MV routing, wrong Base membership and wrong method identity. No managed
workload, golden, native source or archived Player is changed. A new Demo Base
and replay are required; the broader Release qualification commands still use
the old changedProbeChanged requirement and must be aligned before promotion.
