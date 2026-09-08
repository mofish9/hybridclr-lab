# FGS AOT field-address null semantics

## Supported scope

The user's 2026-09-08 clarification sets the active DHE targets to Unity 2022
for the app and Tuanjie 2022 for mini games. Unity 2021 is no longer a required
implementation or validation target for this DHE goal. Its existing source,
reports and Player archives remain historical evidence. This explicit scope
supersedes the workspace's default three-engine DHE matrix for this task.

New release qualification requires the Unity 2022 and Tuanjie 2022 native,
resolver and changed-Player lanes. Registry readers retain recognized legacy
engine IDs so archived records remain inspectable; recognizing an old identity
does not qualify that engine against the current runtime. Existing Unity 2021
workflow descriptions and source locks are historical, not a new build request.

Only the originally configured hot-update assemblies become DHE AOT assemblies.
Ordinary AOT dependencies remain outside the hot-update set; their inventory
is recorded for build identity and compatibility, not to make them hot-updatable.

## Reproducer and failure

The v25 generic-interface Base/no-op builds on both active engines compile,
then crash with 0xC0000005 in field-address-null-owner before any resource update.
The same frozen Base DLLs pass on Unity 2021. The failed roots are
base-mr-generic-u22 and base-mr-generic-tuanjie under artifacts/dhe-evolution-20260908.
Both retain project-workflow-failure.json, Player, symbols, log and crash.dmp.

The unchanged Reference<T>(DheAddedGenericType<T> item) returns ref item.AddedValue.
Its FGS C++ body calls il2cpp_codegen_get_instance_field_data_pointer directly.
The helper performs no null check before Field::GetInstanceFieldDataPointer adds
the offset to the receiver. Reference<int>(null) therefore returns a low invalid
address and the following assignment crashes instead of throwing the expected
managed NullReferenceException. Both symbolized stacks point to the same store.
No changed-method guard or supplemental-field path is required to reproduce it.

## Repair and acceptance

Check the receiver in the codegen field-address helper before calculating the
address, using the existing IL2CPP NullCheck path. Keep Field's low-level offset
calculation unchanged; it is also used with unboxed value storage and GC helpers.
This preserves non-null addresses and byref aliasing without a new cache,
publication field, object layout or ABI. Compile the affected codegen translation
unit in the real-header gate. Keep the exact managed test and DLL/MV inputs.

The primary gate is a fresh exact-source Base/no-op Player on both FGS engines,
then the unchanged 61-group/220-case resource replay for original, evolved and
generic-interface Base generations. The resource analyzer must reject affected
older Base implementations via a declared aot-fgs-field-address-null-check-v1
capability. Merely passing the analyzer is not native execution evidence.

Correctness and unchanged AOT routing are required. Performance, memory, ARM64
and Unity serialization remain unqualified. No new Unity 2021 build is required.
Rollback selects matching prior native/package/tool identities and resources
already proven for the selected Base. A resource cannot repair this native
helper in an installed Player, so the failed Base archives remain ineligible.
