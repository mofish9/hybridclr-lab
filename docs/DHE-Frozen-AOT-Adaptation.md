# Fixed ordinary AOT code with evolving hotfix values

Continue the complete DHE goal. Only the original hotfix set supplies mutable
Current DLLs. Ordinary AOT code comes from each immutable Base's captured AOT
snapshot. The same latest hotfix DLL/MV set must work against different Bases,
even if those Bases contain different ordinary AOT implementations.

An ordinary method can have identical IL while its value arguments, locals,
return value, static field or containing object require a larger representation.
Calling its original typed C++ entry with a Current value is invalid. A byte-copy
adapter cannot generally restore fields lost by old native value copies.
Prepare an execution projection of the original Base IL against Current type
representations for affected methods/storage. Unaffected ordinary methods keep
their native implementation. This is an execution adaptation, not permission to
ship modified ordinary AOT code.

## Required invariants

- Mutable hotfix names, original AOT source names and guarded native names are
  separate sets. Never add ordinary AOT to HybridCLR hotUpdateAssemblies simply
  to bypass a gate. Bind original DLL bytes to the Base AOT manifest hash embedded
  in its identity; reject replacement with a newer ordinary DLL.
- A frozen projection's Base and execution MV are identical and its DLL hash is
  the bound Base source hash. The native plan and publication must retain the
  immutable-source role and reject changed source/MV before mutation.
- Generate guards for ordinary methods that may depend on hotfix layouts when
  building the Base. A runtime plan must prove coverage for every selected native
  entry. Interpreter-originated success cannot qualify an unguarded AOT caller.
- A native entry may forward only through a compatible frame. Affected callers
  using value ABIs also need adapted frames; keep the existing incompatible-ABI
  exception until there is a valid route. PInvoke/internal/native-only code needs
  separate ABI proof and remains a gate.
- Ordinary owners that embed a changed value need Current physical storage with
  logical identity preserved. Their original IL, constructors and cctors must
  use that storage. Include inline/static/nested/generic cases and inheritance.
- Prepare mutable and frozen images in one transaction. Skip prior supplemental
  loads of selected frozen images so they are not initialized twice. Cross-image
  type references must be available before field/frame layouts are cached.
  Publish only a complete set with the existing release/acquire protocol and
  rollback preparation on failure. Initialized metadata retains process lifetime.
- The snapshot's generated identity type is excluded from adaptation: its
  captured constants/init body may intentionally differ from the final embedded
  identity under the existing semantic normalization. No selected method or
  storage declaration may belong to that excluded type.

## Implementation and verification

First generate source-bound frozen plans from real Base snapshots, with concrete
type/method selections and explicit missing-guard/native-only obligations. Keep
resource admission closed until the complete runtime/build path is implemented.
Add native frozen-source validation to the same Current image plan mechanism;
default mutable plans retain their prior behavior and MV binary format.

Then extend Base guard generation/identity separately from mutable assembly
configuration, carry snapshot proof and original source payloads in per-Base
resource records, and load the projections atomically with Current hotfix images.
Complete the ordinary ABI implementation and remove only gates backed by proof.

Tests must include original AOT echo and value mutation, byrefs, arrays,
containing objects/static fields, direct AOT entry, reflection and callbacks,
plus unchanged AOT sentinels. Compare CLR execution of the same latest hotfix
DLLs with each Base's own ordinary DLL. Include source substitution, missing or
wrong guards, invalid tokens, duplicate roles and failed registration rollback.
Two Base Players and their embedded MV/native hashes must remain unchanged
across resource updates. Windows Unity 2022 first; Tuanjie and user Android later.

No performance/production claim from planning or compile tests. The current
resource rejection remains authoritative until Player evidence passes. Revert
the isolated frozen-source plan/runtime/build integration together; preserve the
49-record static-storage checkpoint and its immutable artifacts.
