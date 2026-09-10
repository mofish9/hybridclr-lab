# Added native serialization callbacks

The native query trace shows Unity retains a Base EvolvingBehaviour descriptor
after DHE selection, including calls to class-is-subclass-of-interfaces for
ISerializationCallbackReceiver. The current fixture never implements that interface,
so a correct false result does not prove native discovery of an added interface.

Append a Current-only probe, then add the interface and its two compiler-generated
method bodies to the existing EvolvingBehaviour definition. Keep donor and final
merged DLLs for audit. The callbacks only record invocation counts and the existing
Value field; they do not change business state. Preserve original Base DLLs,
all 46 cases and the same latest Current on original/evolved immutable Bases.

Check Current interface identity, direct interface invocation, native ToJson and
FromJsonOverwrite callbacks, cloning callbacks and copied fields, native type
lookup and untouched lifecycle counters. Run both cold and cached modes with the
existing 11 physical-receiver assertions. Native invocation must not be replaced
with a manual managed callback. A discovery or invocation failure remains a failure.
No production/interface/inheritance capability is inferred before these tests.
This extends Unity 2022 Windows correctness, with no package, formal release,
Tuanjie, Unity 2021 or performance changes.
