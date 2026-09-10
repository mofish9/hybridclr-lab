# Bidirectional compiler declarations and optional arguments

Continue the completed compiler-removal checkpoint without changing its source
identities or evidence. The full DHE goal still includes ordinary hotfix code
evolution and one Current payload for multiple immutable Bases.

Compile three variants with the Unity 2022 compiler. An existing component has
ordinary callback methods and Measure(int value = 3), then implements the callback
and a local operation interface with Measure(int value = 7), then removes both
interfaces and keeps its methods with default 11. Transfer compiler-produced
declarations onto the component; never manufacture virtual flags to obtain a pass.
Keep the same 46 business cases and add explicit reflection, GetInterfaceMap,
direct/interface calls, optional arguments and native callback checks.

Build one Base with nonvirtual/default-3 declarations and another with
virtual/default-7 declarations. Both must consume the same virtual/default-7 and
nonvirtual/default-11 Current DLLs in separate processes. Capture method and
parameter metadata before selection, then verify Current queries and invocation
through cached method handles. Distinguish fresh metadata from previously returned
ParameterInfo objects in diagnostics rather than hiding a stale result.

First exercise Current-only probes on a preserved capable Player to find missing
runtime behavior before another Base build. Missing stripped native API roots are
reported separately and require a new Base fixture; they are not fixed by editing
an archived Player. Preserve every failure and resolve actual runtime faults in
isolated worktrees, with capability admission when older runtimes differ.

Commit sources before tests. Keep DHEMETA1/schema 1 and all historical hashes.
Unity 2022 Windows correctness comes first; no CAT, formal publication, Installer
default, Android/iOS or new Unity 2021/Tuanjie work belongs to this checkpoint.
Existing Bases and failed artifacts remain intact. NTFS-compressed artifact
directories conserve storage for correctness tests only; these runs make no
production-equivalent performance claim.

Broader method declarations, virtual hierarchies, parents, generic/value layouts,
serialized objects, native boundaries and performance/memory remain required.

## Reproduced failures and correction boundary

Base-78-control retains the previous tested runtime and passes build/startup/no-op.
The unchanged virtual-7 Current02 reproduces four warmed parameter failures:
cached and fresh method queries, interface query, and omitted-argument invocation
all retain default 3 instead of 7. The cold replay separately fails both interface
calls through old AOT entries; its interface map succeeds because the interface
type exists in this Base. Preserve both replays and the preceding Base-75 failure
where the new interface map contains a null target. Nonvirtual-11 also reproduces
missing rejection for an unimplemented interface on Base-75.

Parameter cache entries must include original method, selected metadata method,
execution method and reflected class. Acquire both selected handles and retry if
either changes during the lookup; publication is immutable and one-way per Base
assembly. Construct an immutable new parameter array outside cache locks, then
publish via the existing GC-aware append-only map. Already returned parameter
objects remain snapshots; requery through stable MethodInfo obtains Current data.
Do not mutate a shared old parameter object or claim it refreshes in place.

Interface maps use actual selected dispatch storage while retaining logical
reflection identity, and reject nonimplemented interfaces. Interpreter virtual
lookup, including warmed cache hits, uses the existing native-reference invocation
selector for compatible scalar/reference signatures and actual Current receivers.
It must not relax old-object or changed-value-ABI guards. Broader virtual argument
shapes still require explicit implementation and testing.

The package and resource admission record separate capabilities for selected
parameter caching, physical interface maps and reference virtual invocation.
An older Base cannot acquire these native corrections from resource DLLs. Qualify
new fixed immutable Bases against the exact failing Current bytes, and reject
unsupported old Bases rather than editing their capabilities or binaries.
