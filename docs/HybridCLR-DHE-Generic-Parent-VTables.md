# Closed Current parent vtables

## Reproducer and acceptance

The frozen cross-interface replay at d19b649 fails all nine runs on original
Bases, while the same resources pass nine runs on evolved Bases. Current adds
DheEvolutionGenericDerived : GenericVirtualOperation<IntOperationStruct> to the
original generation. Direct Apply returns 106, but inherited Apply on the new
receiver reports no callable entry (U21) or a missing AOT implementation (U22 and
Tuanjie). Keep both resource DLL sets, 52 required evolution groups, 220-case
golden, and all old Base files unchanged.

VTableSetUp caches an existing parent under its Base type identity but imports
the Current definition's published method records. Those records can name the
Current open generic declaring type. InflateVts currently recognizes only the
Base definition as the self type, leaving a Current self declaration open.
Bind that declaration to a pooled Current generic instance with the receiver's
actual class arguments. Preserve the Base cache key, method identity mapping,
override/slot selection, ordinary generic behavior and AOT/IL eligibility.

The fix uses the existing generic metadata/type pools under the existing metadata
lock; it adds no class layout, managed object, publication field or global cache.
Closed type descriptors outlive the vtable records that retain them. Do not solve
this by forcing every virtual call into the interpreter or masking a null entry.
Actual Player replay must verify this diagnosis; compile success alone cannot.

## Compatibility and verification

Declare closed-current-parent-vtables-v1 for repaired Bases. Resource analysis
must require it when a newly interpreted descendant reaches an existing DHE
generic parent, including through an intermediate type. Normalize generic argument
lists while preserving nested type names when following the declaration graph.
Keep already-native descendants eligible on previously proven Bases. Missing
cross-assembly context must be conservative.

Test the original and evolved immutable inputs against the capability analyzer,
including negative negotiation and an intermediate descendant. Run all three
real-header compile/CTest profiles on clean source identities, then first test a
fresh original U21 Base with both frozen resources and skipped updates. Expand
to original/evolved Bases on all engines only after that case passes. Rehash old
and new protected files independently. Preserve every failure and retain failed
Player process receipts even when exit code is nonzero.

This is one runtime regression within the full DHE evolution objective. Generic
interface definitions, broader virtual/layout evolution, Unity serialization,
GC/ABI stress, stable fingerprints and performance/memory remain open. Windows
is the current test platform. No formal branch/tag, Installer default or CAT
change is authorized by this research checkpoint. Rollback selects a matched
previous source/package/tool with resources proven for that immutable Base.
