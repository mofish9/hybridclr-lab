# Public reference identity and physical receivers

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
