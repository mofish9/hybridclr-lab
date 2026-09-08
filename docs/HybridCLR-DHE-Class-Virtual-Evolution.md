# DHE ordinary class virtual evolution

## Objective and boundary

Extend the existing hotfix-only DHE workflow so a fixed AOT Base can consume a
Current assembly that adds virtual/abstract declarations, adds or removes
overrides, or inserts a virtual before a retained method. Existing native
callers must select the Current implementation without changing the Base
player. Ordinary AOT dependencies remain outside the hot-update set.

This is a new correctness candidate based on the six-Base v26 checkpoint;
that checkpoint's evidence does not validate these changes. MV remains
DHEMETA1/schema 1. Only Unity 2022 and Tuanjie 2022 are active targets, as
explicitly requested by the user. No new Unity 2021 build is required.

This work does not complete value-type field-layout evolution, parent-type
replacement, Unity serialization, or external native API availability. These
remain part of the overall DHE goal and must not be hidden by relaxing gates.

## Dispatch design

Keep the Base class's physical vtable and native slot numbers immutable.
Generated AOT calls carry a Base slot. Resolve its root declaration in the
Base hierarchy, locate that declaration by logical identity in the Current
hierarchy, then select the Current receiver's implementation. Resolve from
the root rather than an old override so removing an override falls back to
its parent implementation. Respect new-slot declarations.

Interpreter, delegate and reflection calls retain a MethodInfo and must use
a method-aware lookup. A new alias may have a Current slot numerically equal
to a different Base method; never treat that number as a Base slot. Generic
method definitions are resolved before inflation with the call's arguments.
GetBaseDefinition must follow the same Current hierarchy.

Keep separate caches for raw Base slots and method identities. Entries own
only process-lifetime metadata and contain no managed receiver objects.
Resolve the Current view only after the DHE registration acquire read.
Construct and publish immutable cache entries while holding g_MetadataLock;
all cache reads use that same lock. Failed registration must expose neither
Current views nor dispatch cache entries. Windows x64 tests do not prove
ARM64 publication correctness; source audit remains required.

## Acceptance

First add Base/Current fixtures and obtain the existing analyzer rejection.
Then implement the runtime and both engine hooks before permitting the change.
The fixture covers inserted slots, new overrides on existing children,
inherited overrides, removed overrides, new-slot hiding, added abstract
methods, closed generic types, generic virtual methods, reference-containing
value returns, delegates, reflection/base definitions, exceptions and
concurrent first touch. Retained NoInlining callers accept receiver arguments
and have identical MV versions; record actual AOT entry evidence in Player.

Use the CLR reference for expected behavior, then real-header native
compile/CTest and fixed Windows Player/no-op/update replay for both engines.
Retain the existing 61 evolution groups and 220 differential cases. Validate
the same Current resource against older Base generations as well: a class
absent from an old Base must not require a capability for changing that
class's native vtable. Do not rebuild old Players to make an update pass.

Correctness must have zero differences. Primary performance measurements are
retained AOT virtual-call throughput and update correctness; secondary metrics
are Load + Entry/Reflection, P50/P95/P99, first-touch concurrency and resident
memory. No performance claim is made by this candidate. Formal P99 requires
100 unique processes and an identity-matched comparison. New lock/cache costs
must be measured before release; correctness alone is insufficient.

## Isolation and rollback

Use research/dhe-class-virtual-v8.13.0 worktrees, with separate commits for
lab, HybridCLR, each engine and package. Do not modify CAT, generated C++,
Installer defaults or formal branches/tags. A new capability must be declared
consistently in the native contract, package runtime and package build tool;
old Bases lacking it must reject changes to their existing virtual classes.

Roll back by selecting the frozen v26 tool/runtime/package combination and
its compatible resources. A Base without this native capability cannot gain
it through DLL/MV alone. Preserve all existing Players, payloads, reports and
stashes; keep new artifacts under a distinct class-virtual output root.
