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

The first Current-only interface addition compiles and passes the 46-case CLR
reference, but resource generation rejects EvolvingBehaviour with
existing-type-layout-or-vtable-change on the original Base. Preserve
dhe-unity-serialization-callback-current-01 and the failed
dhe-native-callback-resource-before-01 validation. No native callback result
has been obtained for this evolution; the admission check has not been bypassed.

Add a separately labelled callbacks-control variant that creates a new interpreter
MonoBehaviour implementing the interface. Keep identical native JSON/clone assertions
to verify the test's engine expectations without changing an existing type's
interfaces. That control cannot qualify the rejected existing-type evolution.

## Selected-storage interface-addition candidate

Admit an interface-list addition only for an existing non-generic reference class
selected for physical Current storage. Require identical parent identity (including
assembly), attributes, packing, class size and generic declaration. Every original
interface identity must remain, additions must be non-generic direct declarations,
and duplicate entries are rejected. Existing field checks and execution-plan
dependency propagation still apply. Interface removals/replacements, changed
inheritance and generic interface evolution remain separate required work.

The analysis facts must not change the MV binary format or existing hashes.
Require a new physical-current-interface-additions-v1 capability on every target
Base; do not rewrite immutable older Base manifests to add it. Candidate package
capability declaration is experimental until the same shared Current succeeds
on two newly built Windows Bases, cold and cached, including direct interface,
native JSON/clone callbacks, receiver safety and the original business suite.
No formal package/Installer defaults or remote references are changed.

First add source-bound policy tests using the compiler-produced callback DLL:
selected addition, missing selection/capability, removal/replacement, duplicate,
parent/layout/attribute changes and generic declarations. Retain emitted mutation
DLLs and report identities. Then test the actual native path. If Unity still uses
a cached Base descriptor for interface discovery, locate and correct the exported
query boundary while keeping physical object checks strict. A new-type control
must never satisfy this existing-type qualification.

## Base-65/66 reproduction and next boundaries

At lab e4183c9, runtime b69128f/eaf006d and candidate package 102cfd7, both new
Bases build and pass startup/no-op. Current02 adds the compiler-produced interface
to EvolvingBehaviour. Resource02 admission passes; Base-65 passes 46 business
cases, missing/corrupt new-DLL and snapshot rejection/restoration. The combined
workflow fails on Base-66 at open-generic-value-copy before callback execution.
The failed workflow therefore has no successful aggregate result or audit.

On Base-65, the explicit existing-interface cold replay passes all nine callback
assertions. Cached replay passes eleven receiver checks and seven callback checks:
interface/direct calls and native JSON before/after pass, but cloning reports
zero before/after callbacks. Preserve both failures and their original inputs.

The prior native trace observes exported class-is-subclass-of-interfaces on a
cached Base descriptor for ISerializationCallbackReceiver. Resolve both reference
descriptors at this exported query boundary, just as for the proven native parent
query; retain internal Class::IsSubclassOf, casts and physical receivers. Test the
same Current02 and cached clone assertions on a new uninstrumented Base. Cold
and previous reference/generic/lifecycle tests must remain intact.

Separately, the Base-66 plan selects FrozenResourceCases.Identity<T> as
inspect-generic-context although Payload has not changed. An unchanged AOT caller
reaches that selected Base generic guard, which cannot use a Current frame.
Frozen ordinary AOT already supports per-instantiation conditional selection;
mutable hotfix execution plans currently select these definitions unconditionally.
Do not drop generic selections or waive the ABI guard. A future conditional plan
must explicitly distinguish inspected generic contexts from actual body/metadata/
concrete dependency changes, including cross-assembly dependencies. Equality of
method MV hashes alone cannot establish that distinction. This is a separate
required implementation and test boundary, not fixed by native interface queries.
