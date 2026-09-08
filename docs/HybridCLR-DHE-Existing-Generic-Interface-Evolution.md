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
