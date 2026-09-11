# Existing hotfix type deletion on Unity 2022 Windows

Continue the full resource-only DHE objective after parent transitions. Remove
the actual ProcessorMiddle type definition and its companion marker/probe from
the preserved insertion Current. Processor then directly inherits ProcessorRoot.
Use the Unity compiler for new assertions and preserve the compiler/merge output
separately from the final, explicitly recorded fixture wiring. Keep the original
Current DLLs, ordinary AOT snapshot and immutable Players unchanged.

The first qualification uses a Base that originally contains Middle and another
Base that originally inherits Root. Both must consume identical latest Current
DLLs through the standard resource producer, staging and public package loader.
Check Type.GetType and Assembly.GetType (including throwing/case-insensitive
lookups), GetTypes/ExportedTypes/DefinedTypes, retained ancestry and fields,
construction, root/interface/generic dispatch, reflection invocation and GC.
Retain all 18 framework, 25 virtual-signature and 46 business assertions.
Validate that the deleted definitions and stale references are absent from the
final Current and that the plan identifies actual Base type/method removals.

Primary gate: the exact expected assertion sequence matches CLR, with zero
differences in each real Player. Secondary gates: immutable file identities,
single Current across both Bases, explicit capability admission, malformed
resource rejection and fresh-process recovery. Reuse unchanged native gates only
for their exact sources. No performance claim follows from correctness; median,
P95/P99 and memory remain separate full-goal work.

Deletion with pre-load cached handles/objects is a separate next boundary and
must not be inferred from cold success. No automatic migration, in-process
unload/reload, ordinary AOT source edits or weakened native ABI checks. If the
existing runtime fails, retain the exact Current and failure before implementing
a scoped correction in isolated runtime/package candidates and rebuilding Bases.
Missing embedded capabilities cannot be repaired by changing an old inventory.

Only Unity 2022.3.62f3 Windows is qualified here. No CAT, Installer default,
formal branch/tag/push, Unity 2021, Tuanjie or mobile changes. Source rollback
uses the matched previous runtime/package/tool combination; resource rollback
selects a compatible archived resource and restarts. This checkpoint cannot
close broader cross-assembly/generic parent, Unity assets/startup, publication
stress, performance or full DHE delivery gates.

## Pre-load deleted-type handles and objects

Build a new immutable existing-parent Base with a dedicated pre-load capture
fixture, using the same native/package sources until a real failure demands a
correction. Reuse the exact cold-qualified deletion Current without recompiling
or rewriting it. Capture the original child object, parent/marker type handles,
fields, methods, property/event accessors, a bound delegate and marker constructor
before public DHE registration. Verify that each captured operation works in Base.

After publication, fresh assembly lookup must hide deleted definitions and the
retained child's logical ancestry must select Root. Cached type identities and
physical old-object storage remain alive: cached fields may read/write the old
object, and references in its old parent storage must survive GC. Incompatible
new receivers must be rejected without corrupting either object's fields. Old
metadata handles are not automatically migrated or destroyed.

Deleted method bodies, including cached delegate/property/event/constructor
invocations, must reach the MissingMethodException tombstone rather than execute
removed AOT code. Existing cached event effects must not advance. This explicit
runtime transition contract is tested alongside the unchanged 22-case CLR
reference and full 18/25/46 Player sequences; CLR is not claimed to implement
in-process replacement of an assembly. Collect independent assertion failures,
retain original inputs/logs, and only then change the runtime if required.
