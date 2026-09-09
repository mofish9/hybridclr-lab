# Static value storage evolution

Continue the full hotfix-AOT resource goal on Unity 2022 Windows after the
two-Base initialization fix. Ordinary AOT assemblies retain their original code;
all three hotfix fixtures stay registered. No CAT/release/default Installer work.

The defect to exercise is an existing static field with unchanged signature but
an evolved inline value type. Matching the logical Base signature currently
keeps the Base offset and allocation, even though interpreted code copies the
larger Current value. That cannot be repaired by an offset change in Base memory.

During unpublished homologous image preparation, compare the matched static
value field's logical type with its physical Current field type. When the
execution plan selects a different value representation, retain the Current
field definition/storage and expose that physical field through the logical
owner's reflection view. Hide the old Base field from enumeration. Unchanged
static fields keep their original Base storage and ordinary AOT addresses.
Use Current's actual GC-described static allocation for added references; never
copy a larger struct into Base storage or keep untracked reference side tables.

Reuse the single initialization owner implemented in the previous candidate.
No cctor runs and no Base static data is modified during preparation. Metadata
maps are immutable after the existing release/acquire DHE publication; closed
generic aliases use the metadata lock. Rejected registration does not require
undoing Base field contents. Current images/storage retain process lifetime.
Removing the overlay and associated tool capability/locks is the rollback unit.

The resource planner must still reject evolved static value storage in ordinary
AOT owners and unproven ThreadStatic/RVA cases. The new capability is explicit
and required only for accepted static storage evolution. Generic argument-based
storage selection needs real tests; preserve every failed case until fixed.

Use two immutable Base generations (small and intermediate layouts), one latest
Current and exact CLR records. Include inline/nested values, independent copies,
static byrefs and initobj, reflection get/set and logical declaring type, GC
retention, neighboring unchanged state and its ordinary AOT caller, cctor counts,
closed generic owners and generic value arguments. Base/no-op paths must keep
passing. Bind the same actual Unity-stripped DLLs and final snapshot/native
identity. No performance claim, ARM64 inference, or live object migration claim.

The first full snapshot run failed before resource generation because Int32's
core-library backing field recursively resolved to its own definition. Primitive
CorLibTypeSig storage is intrinsic and must terminate layout/signature traversal.
Extend policy tests with the actual complete stripped AOT inventory. Reuse frozen
Bases for tool-only fixes through the workflow's optional existing-matrix path;
verify package/runtime/Player hashes before reuse and preserve failed outputs.
Full core-library analysis also exposed duplicate AssemblyRef rows when the
planner unnecessarily created hotfix MVs for ordinary AOT modules. Ordinary AOT
is frozen input: use its retained method/field definitions directly as the
reference inventory and reserve MV generation for actual hotfix assemblies.

After resource generation passed, the Player reached a selected generic cctor
through Runtime::ClassInit's Base MethodInfo and correctly rejected its old
call frame. Keep the canonical initialization owner, but resolve the selected
Current execution method before invoking the cctor. The cctor signature is
static void(), so this does not pass a changed Base value ABI. Rebuild new Bases
for this native change and preserve the earlier immutable pair and failure.
