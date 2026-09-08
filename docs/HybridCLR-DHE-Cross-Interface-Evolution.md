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
