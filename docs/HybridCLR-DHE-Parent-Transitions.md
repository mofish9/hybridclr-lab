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
