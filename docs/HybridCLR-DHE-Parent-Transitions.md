# Removal and replacement of an existing hotfix parent

Continue the qualified insertion checkpoint on Unity 2022.3.62f3 Windows.
Build a new immutable Base from the preserved extended insertion Current, so
Processor originally inherits ProcessorMiddle and its native fields/members.
Produce two fresh compiler Current variants: one removes the intermediate
parent, the other replaces it with a new parent with distinct fields, constructor,
property and event. Retain ProcessorRoot and all original ordinary AOT inputs.
Keep ProcessorMiddle declared to isolate ancestry changes from type deletion.

Run identical latest DLLs on the inserted-parent Base, old/grown root-only Bases
and a new-type control as appropriate. Each resource must retain the previous
46 business, 25 virtual-signature and 18 framework callback checks. Add direct
ancestry/casts, construction and offsets, inherited member absence/presence,
DeclaringType/ReflectedType, GC, independent objects and root virtual/generic
dispatch. Capture Base objects/member handles before registration and verify
both retained old storage and rejection of incompatible new receiver access.

The correctness target is zero differences against CLR for new instances;
old instances retain physical storage and require explicit receiver validation.
No automatic object migration, in-process unload/reload, or native ABI weakening.
An unchanged/no-op Base must still record positive AOT entries and zero DHE
interpreter entries in the established no-op probe. No performance claim follows.

Preserve the preceding Current sets and Players. Commit sources before each
source-bound run; use fresh output paths. Native/package changes require actual
failure evidence, new source identities/capabilities and new immutable Players.
Old capability inventories must remain unchanged. Verify resource hashes and
native preparation failure/recovery. Roll back by selecting a compatible prior
resource and restarting; changing embedded runtime requires a new Player.

Removal/replacement is not full DHE: cross-assembly/generic parents, Scene/Prefab,
startup objects, publication stress, performance and memory remain open. No CAT,
formal publication, Installer defaults, Unity2021, Tuanjie or mobile testing here.

## Cached reflected fields: confirmed failure and correction boundary

Base-95 passes the unchanged removal Current's 21 checks and replacement
Current's 31 checks, each with 18 framework, 25 virtual and 46 business checks.
Both pre-load cache runs fail at RuntimeFieldInfo.GetValue on the old object.
The exact captured mscorlib checks DeclaringType.IsAssignableFrom(obj.GetType()).
The selected logical ancestry no longer contains ProcessorMiddle, although the
old physical object still contains its fields. Keep these failures and both
Current sets unchanged.

Select the original frozen mscorlib RuntimeFieldInfo.GetValue and SetValue when
an existing hotfix type changes parent. Authenticate the original DLL/MV, require
complete Base entry guards and an explicit runtime capability. Verify the actual
method signatures and Unity 2022 validation IL before admitting this adaptation.
The interpreter transform skips only the validated Object.GetType instruction
and dispatches its adjacent Type.IsAssignableFrom as Type.IsInstanceOfType,
preserving the actual receiver. Do not modify shared MethodBody or original IL.
All null, static, literal, generic, binder and field-storage checks remain.
Malformed or unfamiliar validation bodies must fail admission/transform.

Keep Type.IsAssignableFrom unchanged globally. New receivers must still reject
the former parent; old receivers retain physical storage. Add negative controls
for unrelated/null receivers, wrong values, literals, open generic fields and
custom binder execution, in addition to the original fourteen cached checks.
Require committed native/package/tool sources, real-header compile/CTest and a
new immutable Base before replaying the exact failing Current. No throughput or
memory improvement is inferred from this correctness correction.

Base-96 on runtime1147772 passes build/startup and its 25-check AOT no-op, but
both original transition resources now fail at RuntimeFieldInfo.GetValue's
native entry, before business execution, with the Current-frame ABI guard.
The frozen image intentionally returns no public Current declaration for an
unchanged framework owner. Native frame admission mistakenly requires that
declaration. Use the authenticated frozen source plus its exact Current-to-Base
method mapping as the owner identity proof, while retaining physical signature
checks and rejecting any selected receiver/ancestor storage. Require a separate
frozen-base-instance-frames capability; Base-96 cannot acquire the fix by changing
its recorded inventory. Preserve both failures and build another immutable Base.
