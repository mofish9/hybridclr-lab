# Conditional generic execution for mutable hotfix sources

## Reproduction and contract

Base-66 with callback Current02 fails open-generic-value-copy although Payload
and FrozenResourceCases.Identity<T> are unchanged for that Base. The reference
interface change creates an execution plan; every inspect-generic-context method
then receives an unconditional Current selection. An unchanged AOT caller enters
the generic guard with a valid Base frame and is rejected. Base-67 verifies the
separate native interface query fix. Preserve both identities and all failures.

For every immutable Base, unchanged closed generic instances must retain AOT when
their concrete arguments and declaring storage are unaffected. Changed method
bodies/metadata and concrete dependencies must still interpret. A changed generic
method reached from AOT may use the Current interpreter only when its concrete
closed parameter/return ABI is physically compatible; never reinterpret a changed
value buffer or grant an old object a Current receiver layout.

## Execution plan and runtime

Add optional currentGenericContextMethodTokens and its count to the existing plan
schema. Keep DHEMETA1 and existing binary MV hashes. Empty/absent conditional lists
retain the prior canonical binding. Nonempty lists participate in manifest,
validation, runtime plan and staged-resource comparisons. Require sorted unique
Current method tokens, subset-of-execution selection, unchanged MV version,
generic declaration and an unselected declaring storage type. The compiler must
also prove inspect-generic-context with no concrete dependencies; equal MV hashes
alone do not establish cross-assembly safety.

Require hotfix-generic-context-dispatch-v1 for mutable execution plans selecting
generic methods. Old experimental Bases must reject these resources; do not alter
their embedded manifests. Existing raw native batch arrays already carry source
kind and conditional tokens. Forward mutable conditional tokens through mixed,
frozen/mutable and differential-only package paths. BuildCurrentImagePlan validates
Current tokens and maps conditional identities to immutable Base tokens before
registration, including reordering/collision cases.

Generalize the existing frozen generic decision to explicitly selected mutable
conditional entries. Validate the unchanged method version again in native
preparation, preserve physical Current execution handles, and publish the complete
set through the existing release/acquire transaction. Frozen ordinary AOT remains
an immutable captured source.

For unconditional Current generic entries, check a closed Base frame against the
resolved closed Current signature. Unknown/open contexts remain rejected. Scalar
types retain their ABI rules; compound types require the same physical class and
matching byref form. A changed physical receiver remains rejected. The guard must
choose interpreter execution for changed bodies even with unchanged arguments.
Do not infer general native marshalling or live-object migration from this check.

## Gates and rollout

Before implementation, add native failing tests for mutable conditional dispatch,
changed-body rejection of conditional admission, Current/Base token reorder and
invalid selections. Add planner/package tests for the preserved Base-66 fixture,
cross-assembly concrete dependencies, changed-body unconditional selection,
conditional binding tamper and all three load paths. Keep ordinary frozen tests.

Run real Unity 2022 header compile/CTest and the managed package regression after
candidate commits. Build new original/evolved Windows Bases, then run the exact
callback Current02, all 46 business cases, cold/cached native interface callbacks,
reference/generic/serialization/lifecycle, public recovery and independent audit.
Explicitly test changed generic bodies with stable scalar/value/byref arguments,
and unchanged generic bodies with affected versus unaffected arguments. Update
cache expectations using actual reference selection, not Base revision alone.

Report exact source/build identities and retain every failure. No performance,
Tuanjie, mobile or production claim follows from Windows correctness. Tuanjie
comes after Windows stability; no new Unity 2021 work. Keep formal branches, tags,
Installer defaults and CAT unchanged; rollback as a complete candidate set.

## Player conditional dispatch evidence

Append an optional Current-only probe without changing Identity<T> or rebuilding
Base-68/69. Close the existing method over Payload and Envelope<Payload> on the
evolved Base-69, where the method and its AOT instances already exist. Base-68
introduces this helper as Current code, so it cannot prove retained generic AOT.
Validate the unchanged Base method identity before measuring. Derive affected/unaffected from
each Base's bound storage plan, never its revision number. Warm reflection first,
then isolate one invocation between dispatch counter resets and reads. Require
the expected selection, exact value/reference preservation and one changed DHE
entry for affected instances versus zero for unaffected instances. AOT counters
include reflection wrappers and cannot supply per-method timing. Bind all source,
plan, input and binary hashes; retain failed probes without changing the oracle.

## Unselected owner allocation follow-up

Base-69 passes the conditional Identity<T> probe but fails four GenericOwner<
EvolvingBehaviour> reflection/type assertions after the component gains an
interface. The owner definition is not selected for storage. The interpreted
constructor's MethodInfo uses the raw Current owner; newobj allocates that class,
while typeof/MakeGenericType use the retained Base definition with Current
arguments. The allocation mapper currently returns raw interpreter definitions
unchanged. Reapply the owning published image's storage selection to those
definitions before allocating, including each part of a closed generic class.
Preserve type modifiers and the argument context. New interpreter-only types
and selected Current definitions must remain themselves. Existing objects are
never migrated by this operation. Publication still uses the image's completed
DHE state and metadata interning still holds the metadata lock.

Preserve the cold/cached Base-69 failures and rerun their exact Current DLLs on
a newly built evolved Base after real-header native compilation. Retain Base-68
as the passing selected-owner control. Separately replace the lifecycle oracle
based on Delta with the bound resource plan; do not hide the observed coroutine
Base-frame exception by weakening that test. Generic dispatch, native callbacks,
reflection writes and all 46 business cases remain required regressions.
