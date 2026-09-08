# DHE existing interface evolution

## Objective and acceptance

An immutable Base already contains IIntOperation and its value-type implementation.
A later resource adds an interface method and updates its implementations, without
changing object or value-type fields. The same resource must work when the class
implementation is new to an original Base and already native in an evolved Base.
The ordinary Apply method and its existing AOT call sites must keep their semantics.
Added precedes Apply in Current metadata, deliberately shifting Apply from slot 0
to slot 1. Base AOT callers still use the original slot 0; the fixture must prove
both old and new dispatch instead of only appending a method after existing slots.
This is an initial fixture for the wider interface/vtable objective, not a claim
that other interface, inheritance or value-layout changes may remain unsupported.

Correctness is primary. Require direct interface dispatch, an explicit class
implementation, boxed structs, constrained generic calls, delegates, reflection
invocation and GetInterfaceMap to agree with CLR. Preserve all 35 field/evolution
groups and the 220-case golden suite. Call cost, metadata allocation and resident
memory are secondary metrics; no performance conclusion follows from correctness.
Do not depend on pre-touch, FGS or changed assertion values for passing semantics.

The first phase must reproduce the current compatibility rejection using ordinary
C# compilation followed by Unity Current preparation. Preserve the exact DLL and
rejected output. Removing the existing interface/vtable checks is not a repair.
No native capability is advertised until implementation and real Player gates
cover it. The fixture uses DHE_INTERFACE_EVOLUTION_CURRENT; disabling it leaves
the established Base and previous Current declarations unchanged.

## Runtime investigation

Existing native class storage has fixed-size vtable and interface-offset tables.
ClassInlines::GetInterfaceInvokeDataFromVTable uses an interface slot directly in
the native table. New logical MethodInfo objects cannot simply use Current slot
numbers against that Base table. Existing AOT Apply callers must still address
their original slots even if Current declarations are reordered or expanded.

Investigate an immutable logical dispatch map keyed by canonical Base/current
method identity and actual receiver type. Audit interpreter callvirt/constrained,
delegate binding, reflection invocation, interface maps, boxed adjustor thunks,
FGS and generic inflation before choosing the hook boundary. Preserve native
layout and old slots; native callers require explicitly covered lookup hooks.
Publication must prepare complete maps under existing metadata synchronization,
publish once with release/acquire ordering, and roll back failed multi-assembly
registration without leaving partial mappings. A Windows pass is not ARM64 proof.

Unity 2021 uses ordinary bridges and its own supplemental AOT metadata. Unity
2022 and Tuanjie also require FGS and their real headers. After a native repair,
build new Bases for all three engines and replay both managed generations with
common resources, consecutive/skipped updates and full cold differential.
Old v13 Players cannot acquire a new native capability from a manifest.

## Boundaries

No formal source/tag, Installer default or CAT change belongs to this research
step. Keep failures and previous successful Players immutable. Rollback selects
the previous matched source/tool/package locks and compatible resources.
Independent sidecar expansion, non-generic address coverage, general class
virtual-method evolution, value-type layout, Unity-facing behavior, external AOT
API availability and concurrency/GC/performance qualification remain required.
