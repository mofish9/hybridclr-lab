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
