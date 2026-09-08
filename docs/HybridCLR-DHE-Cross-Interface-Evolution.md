# DHE cross-assembly and inherited interface evolution

## Objective and fixed inputs

Extend the completed 42-group interface checkpoint without modifying its six
archived v16 Bases. All already contain ICrossAssemblyLazyVTableContract and
CrossAssemblyLazyVTableBase in ManagedCases, plus CrossAssemblyLazyVTableDerived
in a different DHE assembly. Current adds ordinary and generic interface methods
before Compute; existing native Compute callers retain their original slot.
The existing parent provides both new implementations, inherited by the existing
derived class. Neither existing class gains physical fields or changes old bodies.

The same Current resource must support original/evolved managed generations on
Unity 2021, Unity 2022 and Tuanjie, including consecutive and skipped updates.
Unity 2021 keeps its own AOT metadata; the other engines use FGS. Existing Base
archives, DLL/MV inputs, previous successes and failures remain immutable.

## Fixture and acceptance

DHE_CROSS_INTERFACE_CURRENT enables ten additional groups: base and inherited
dispatch, a new generic interface method, delegates, reflection, interface maps,
a new child interface, a new generic interface inheriting the old contract,
cross-assembly explicit implementation, and type/assembly identity. The existing
42 groups and 220-case golden suite remain unchanged. CLR must execute the exact
shipped DLLs. Native old-caller method fingerprints must remain unchanged.

This fixture tests generic method evolution on an existing non-generic interface
and new generic interfaces inheriting it. It does not yet test adding methods to
an already-native generic interface definition; that still requires an appropriate
Base fixture. Do not represent these two capabilities as equivalent.

Correctness, per-case execution evidence and immutable Base-file hashes are hard
gates. No pre-touch or inline padding is permitted. Dispatch cost, allocations and
resident memory are secondary and remain unqualified; no performance claim follows
from correctness. The first phase uses the existing C# compatibility gate and
records any rejection without disabling it. Actual Player failures determine the
next runtime repair. Cross-image declaration preparation, inherited vtables,
generic inflation and metadata publication must be audited if a repair is needed.

No runtime change, formal branch/tag, Installer default or CAT change is part of
fixture preparation. If a native repair is required, compile all three actual
header profiles and build fresh Base identities; do not overwrite old Players.
Rollback selects the last matched native/package/tool snapshot and its proven
resources. General virtual/layout evolution, Unity behavior, sidecar expansion,
external AOT availability, GC/ABI/concurrency stress and performance remain open.

## First payload fingerprint diagnosis

The initial `cross-interface-prepared-identity/report.json` is preserved with its
18/19 failure: all 220 case entry versions were expected to remain unchanged.
`cross-interface-rva-diagnostic/report.json` retains that failure and proves the
cause. All 220 bodies and method metadata match; 12 dependency fingerprints differ.
Compiler-generated array constants have identical bytes at different RVA addresses.
Recomputing only in memory with the old RVAs restores all 220 method versions.
No DLL, embedded MV, scanner algorithm or archived result is rewritten.

The fixture now explicitly checks unchanged bodies/metadata, the existing cross-image
AOT callers, and the absence of unexplained dependency changes. First-payload case
routing is mixed: 208 retained-AOT candidates and 12 conservatively invalidated
entries on the diagnosed evolved Base. Every actual Player method state must still
match its own exact Base/current MV; latest requires all 220 interpreted receipts.
This does not resolve the scanner's RVA sensitivity or qualify AOT retention as
optimal. Removing that sensitivity while preserving immutable Base contracts is
separate unfinished work; the old all-AOT assertion did not pass.

## Reproduced native registration failure

Clean `a4f1b2e` ran all 18 processes against the six unchanged v16 Bases.
Every process failed registration with `MethodNotFind
HybridCLR.Lab.ManagedCases.ICrossAssemblyLazyVTableContract::Added`.
The complete failed replay is retained at `replay-cross-interface-generations-cold`.
CLR references still pass all 52 groups and 220 golden cases.

The candidate repair resolves MethodImpl MemberRef declarations from the staged
Current definition table while preserving the public Base container identity.
It does not enumerate unpublished logical MethodInfo aliases or change a class
layout. The existing global metadata lock and atomic registration own this table;
no additional cache, publication field or lock is introduced. The new v17 contract
declares `cross-assembly-interface-declarations-v1`. Resource compatibility requires
it when an existing interface gains methods and another Current assembly references
that interface; missing assembly-set context is conservative. v16 capability sets
are rejected by the focused test. Actual repaired Player execution is still pending.
