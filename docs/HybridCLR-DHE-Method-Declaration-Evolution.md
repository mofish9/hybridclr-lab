# Method declaration evolution after implicit interface removal

The full DHE goal includes ordinary C# edits to originally hotfix assemblies.
Removing an implemented interface while retaining its public methods normally
changes the compiler-emitted methods from virtual/final/newslot to nonvirtual.
The earlier interface-removal fixture deliberately retained those valid IL flags;
it cannot qualify this common compiler transition.

First compile a non-interface donor with the Unity compiler and transplant its
unchanged callback behavior onto the existing Current component. Preserve the
46 business cases and distinguish this variant with its own exact replay marker.
The retained methods must be callable and reflect nonvirtual declarations, while
the removed interface and native JSON/clone callbacks remain absent. Use an AOT
Base which originally implements the interface plus another Base without it,
sharing the same Current DLLs and keeping both Players immutable.

Reproduce admission before changing it. Public method attributes, declaring type,
inherited enumeration and cached MethodInfo objects must use Current declaration
semantics without weakening internal physical receiver validation. Audit existing
native entry guards and reflection invocation separately. If older native source
cannot provide the semantics, declare a distinct capability and build a new Base;
never edit an older Base's capabilities or bypass its resource checks.

Keep DHEMETA1/schema 1. New admission-only facts must not change archived binary
MV hashes. Commit each source/lock change before real Unity 2022 headers/CTest,
managed checks and Player verification. Preserve earlier failures and qualification
under their exact commits; do not transfer them to this candidate. No CAT,
Installer defaults, formal branches/tags or remote publication changes belong here.

This is one required step, not a reduced completion definition. Broader method
declarations, reference parents, generic/value hierarchies, serialized objects,
native boundaries and performance/memory remain part of the full goal.

## Reproduced entry point

Base-74 now contains the original callback interface in AOT and passes the
explicit interface-removal/replacement variants; see
`reports/dhe-reference-interface-evolution-windows.md` for immutable identities.
At test source 0bd9195, `D:/hybridclr_artifacts/dhe-method-declarations-current-01`
compiles with ordinary nonvirtual callback methods and passes the 46-case CLR
reference. `dhe-method-declarations-admission-before-01` rejects exactly the two
existing callback method declarations before Player execution. Preserve it.

Public MonoMethodInfo resolves Current return types but still returns Base flags
in get_method_info/get_method_attributes. MetadataModule already has
GetDheCurrentMethodMetadata, which supplies the Current declaration without using
the physical receiver as a public identity. Audit get_base_method and method
enumeration alongside those attributes; internal Class receiver validation must
remain strict. New admission facts should mask only the explicitly supported
declaration differences, preserve unrelated metadata gates, and declare any
required native capability before a new immutable Base is built.
