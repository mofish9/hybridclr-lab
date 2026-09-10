# Existing reference hierarchy evolution

## Goal and invariant

Extend DHE beyond additive interfaces on existing hotfix reference classes.
Remove/replace interfaces and change managed parent relationships while keeping
each Base Player immutable and sharing one latest Current DLL set. Ordinary AOT
source remains immutable. Unaffected methods/instances retain AOT. This is one
step toward the full hotfix goal; generic/value hierarchy cases, old objects and
native boundaries are not excluded from the final objective.

Use isolated hierarchy worktrees derived from the passing owner checkpoint:
HybridCLR e3d6604, IL2CPP 6951d30, package fc1ba89, lab 2de495c. Unity 2022 Windows
comes first; no new Unity 2021 work, Tuanjie afterward. No formal publication,
Installer default or CAT modification belongs to this candidate.

## Implementation order

1. Add failing admission tests for removal, removal of the old implementation
   methods and replacement with a valid new implementation. Keep additive
   interface admission compatible with its existing capability. Require a new
   explicit capability for removal/replacement; immutable older Base manifests
   cannot be edited to advertise it.
2. Verify reflection and native type queries against the selected Current
   declaration. Public GetInterfaces/BaseType/subclass/assignability queries must
   agree. Preserve strict physical ancestry and receiver validation inside the
   runtime; a public alias must never grant Current layout to an old allocation.
3. Build an AOT Base which already contains the callback interface and the
   hierarchy fixtures, then send a Current resource removing/replacing it. Check
   cold/cached type queries, ordinary managed interface dispatch, reflection,
   native JSON/clone callback presence or absence, field data, GC and lifecycle.
   An older Base can introduce the same Current fixture as new interpreted code;
   it still consumes exactly the same Current DLLs.
4. Add parent-change admission/fixtures using existing alternate managed parents,
   inherited fields and virtual overrides. Reuse typed field/parent dependency
   closure. Audit retained AOT callers, constructor selection, public BaseType,
   cached native descriptors and actual physical buffers before allowing each
   new shape. Native Unity ancestry must remain a separately verified boundary.
5. Extend generic/value hierarchy coverage and serialized scene/Prefab cases,
   then performance/memory. The full goal stays open while any required boundary
   remains unimplemented or unverified.

## Gates and rollback

Preserve every failed artifact. Commit candidates before real-header native
compile/CTest and Player verification. Keep DHEMETA1/schema 1; admission-only
facts must not alter existing binary MV hashes. Every new Base/resource/report
records exact source/tool/package hashes. Keep the original 46 business cases,
reference/generic/cache/serialization/lifecycle suites and public failure/recovery
as adjacent regressions. Report individual Base/capability results honestly;
candidate capability declarations are not release qualification.

Runtime changes require a new Base. Resource-only changes can be retried on an
unchanged Base with a new resource artifact; never overwrite a prior Base or
failed payload. Large artifacts go to D. Reclaim space only from verified,
reproducible temporary targets when needed, preserving Players, inputs,
snapshots, generated C++, logs and reports.
