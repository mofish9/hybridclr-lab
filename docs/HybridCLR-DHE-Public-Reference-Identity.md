# Public reference identity and physical receivers

## Current checkpoint

Runtime b69128f/eaf006d, native08 and immutable Base-63/64 pass the cached
query/lifecycle correction described below. With identical Current04, both pass
46 business cases, 11 cache + 14 reference checks, 11 cache + 18 generic checks,
and cold/cached 11 serialization + 17 lifecycle checks. Public preparation/recovery
and the independent resource audit pass. Full identities are in
`../reports/dhe-reference-storage-windows.md`. Wider native parameter/receiver
coverage and existing-type interface additions remain required.

## Preserved Base-61/62 diagnosis

At runtime 8eb835a/23f2f54, Base-61/62 pass the original cached native query
and clone assertions (14/14), all 11 physical receiver checks and 18 generic
checks. Cached serialization now passes all 11 checks on Base-61, then lifecycle
fails Awake with the old-AOT-frame guard; OnDisable and OnDestroy are also
rejected. Cold lifecycle still passes, and Base-62 passes the cached full suite.

Native invocation currently forwards cached Base MethodInfo directly into
Runtime::Invoke, unlike managed reflection's prior Current selection. Add a
receiver-aware Current selection there for an incompatible Base instance frame
only when its concrete non-byref scalar/string/object ABI matches Current and
the actual receiver has the Current physical parent. Keep open generics, value
receivers, changed value buffers, byrefs and old physical receivers on their
existing guarded paths. Do not change generated AOT guards or mark all Base
instance frames compatible. Reuse the preserved cached lifecycle failure and
full regression; explicit broader native parameter/receiver gates remain required.

The isolated native trace (IL2CPP e20b8d7, lab 2db7b49, Base-60) reproduces
the cached failure at exported il2cpp_class_has_parent(Base descriptor, Current
descriptor), while class-from-system-type already selects Current correctly.
Cold lookup has no such mismatched descriptor comparison and succeeds. Resolve
both reference descriptors at this exported native hierarchy-query boundary.
Keep internal Class::HasParent, casts and field checks physical; this change
must not let old object memory satisfy a Current code receiver. Validate the
unchanged cached reference/serialization failures and all 11 safety assertions
on new uninstrumented Bases, then full previous business/generic/recovery gates.

See `../reports/dhe-reference-storage-windows.md`: runtime 8eb835a/4b02c37,
lab d69e11e and Base-58/59 pass all eighteen strengthened generic assertions,
including reciprocal field writes, with identical Current04 from the 16/18
failure reproduction on Base-56. Full cold-selection reference/serialization/
lifecycle, eleven cached receiver checks and public failure recovery also pass.
Pre-selection component creation still breaks native lookup on the old-layout
Base; the physical-field correction does not resolve or waive that blocker.
The following sections preserve the measurements and design leading here.

## Latest measured correction and remaining reproductions

At runtime 02d333e/4b02c37 and lab 99e5b53, Base-56/57 pass startup,
no-op, identical four-DLL resources with all 46 business cases, 11 serialization
checks and 17 lifecycle checks. Native05 uses real Unity 2022 headers and passes.
Both pass 14 public reference checks and all 11 cached physical-receiver checks.
Generic checks pass 17/18 on Base-56 and 18/18 on Base-57; the remaining failure
is owner-reflection-construction. List allocation/add, array construction and
nested generic public assignability now pass. The reflected selected reader
records interpreter=1 on Base-56; the same-layout control and unaffected sentinel
record interpreter=0. Full lifecycle execution assertions pass without weakening.

Pre-selection component creation still changes native behavior on Base-56:
the following GetComponent(publicType) and clone checks fail. The full cached
serialization replay reads/writes JSON correctly but stops at clone-copies-existing-field.
Keep this separate from the passing cold-selection path and cached field/cast
safety checks; no pre-existing object or native cache compatibility is claimed.
Public preparation-failure/fresh-process recovery also passes on both Bases.

Add diagnostic logging to the existing failed assertions without changing their
pass conditions. Distinguish generic construction's physical Current cast from
its reflected/direct field value, and compare native lookup by the evolved type
against lookup by the unchanged MonoBehaviour ancestor. Read clone fields through
the ancestor solely for diagnosis; do not use this fallback to pass the original
query/clone assertion. These Current-only probes reuse immutable Base-56/57.

The diagnostic resource confirms publicType=true, currentCast=true and
reflectedValue=true but directValue=false for generic construction on Base-56.
GetSupplementalFields currently registers every reference generic owner in the
sidecar map, including definitions explicitly selected for physical Current
storage. This mixes detached cells with direct Current field offsets. For a
selected definition, remap its closed owner to the execution arguments and expose
the real physical fields directly, without registering sidecars or cloning the
field type from a Base generic context. Unselected generic definitions retain
their existing sidecar behavior. Verify reflected/direct writes in both directions
and the adjacent field on the same object, then all prior value/reference cases.

The native query diagnostic finds the source and clone through MonoBehaviour,
with clone Value=29 and Extra=91000000031 intact, while GetComponent(publicType)
returns null after pre-selection creation. This isolates the observed failure to
native type lookup; it does not yet identify the engine cache or qualify migration.

## Observed first correction and next correction

At d52de3b/6886fee, Base-54/55 both pass all fourteen public reference checks.
All ten cached-handle/physical-receiver assertions pass, but the subsequent
old-layout component GetComponent/clone checks fail after pre-selection creation.
Generic/array identity improves from 4/18 to 10/18 on the old-layout Base; the
same-layout control passes 18/18. Field handles with generic Current arguments,
ordinary List<T> allocation, arrays and nested generic assignability remain.

The real reflected dispatch diagnostic on Base-54 returns 38 as expected with
selected=true, aot=23 and interpreter=0. Base-55 returns 38 with selected=false,
aot=24 and interpreter=0. The unaffected sentinel returns 5, aot=20,
interpreter=0 on both. These aggregate AOT counts include reflection wrappers;
they do not identify the reader's execution mode. Source review confirms the
DHE counter is in PrepareDheInterpreterMethod and misses direct interpreter
invoker entry. Move it to actual Interpreter::Execute entry for changed DHE
methods and remove preparation counting; preserve the full execution gate.

Resolve physical generic arguments recursively through each owning published
assembly, including reference wrappers. Permit a physical Current reference
instance to satisfy its public Base class query, while retaining strict physical
ancestry for field offsets and refusing the reverse old-to-Current cast. Add an
explicit Current-code cast assertion to the cached receiver suite. Normalize
generic declaring handles when constructing FieldInfo. Reflection array creation
must select the physical reference element, without reinterpreting old value
buffers or migrating existing arrays. Keep generic invariance and old-frame
guards. These are candidate changes, not a completed capability statement.

The preceding e403775/6d0f642 candidate passes native compile and actual Unity
serialization, but old-layout Base-52 fails Type equality/assignability and the
managed MethodInfo.Invoke receiver check. Same-layout Base-53 passes. The full
goal still includes different immutable Bases using identical Current DLLs,
changed dependencies interpreting and unaffected methods remaining AOT.

Keep one stable public reference Type identity using the Base type when that
declaration exists. Do not mutate a cached RuntimeType, its hash or native Base
layout. Physical Current metadata remains available for allocation, fields and
execution. Normalize published selected reference representations when creating
reflection Type objects. Reference generic definitions/arguments and wrappers
must be considered, but existing value-copy ABI rules remain separate.

Public IsInstanceOfType/IsAssignableFrom may compare the selected reference
representations; native Object::IsInst, generated old-frame guards and physical
receiver validation retain their stricter meaning. In particular:

- a cached Base FieldInfo with an unchanged field type can resolve its Base
  token to the corresponding physical Current field on a Current receiver;
- a raw Current field cannot address an old allocation; reject before its
  offset is applied, unless a separately validated adaptation owns the storage;
- old fields on old objects retain their valid physical offsets;
- static fields, sidecars and value-copy rules retain their existing paths.

Before implementing, add a Player fixture that retains Type, its hash, a Type-keyed
dictionary, FieldInfo and an inactive component before version selection. After
selection verify stable identity, allocation through the cached Type, cached
field reads/writes on new storage, old-field access on the old object, and safe
rejection of Current offsets on old storage. This guard does not claim live
object migration; native serialization of pre-existing objects, generic/value
argument identity, inheritance and serialized assets remain required work.

Primary gates are exact business differential=0, all fourteen public reference
checks, eleven serialization checks, seventeen lifecycle checks and the cached
handle/receiver suite on original/evolved Unity 2022 Windows Bases with identical
Current. Include RuntimeTypeHandle.cpp in the real-header native compile gate.
Preserve failed probes and bind commits before building. Recheck publication
acquire and class/metadata locking. No throughput/memory improvement is claimed;
unaffected AOT dispatch must still be measured. Public preparation/recovery and
broader reference/generic boundaries follow this semantic correction, then
performance and Tuanjie. No new Unity 2021 or Android/iOS claim is in scope.

Rollback uses the complete preceding matched candidate/runtime/resource set.
Keep the prototype out of formal branches, tags and Installer defaults. Update
the runtime capability contract before qualification; experimental v32 is not a
promise that older candidates support new reference storage.
