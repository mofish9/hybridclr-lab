# Public resource evolution validation

Continue the complete hotfix-AOT goal on Unity 2022 Windows. The previous no-op
resource loop is the starting point, not the completion criterion. Build an old
and a newer immutable Base, compile one latest Current through Unity, and run
that same DLL/MV payload against both Bases. All three configured hotfix DLLs
remain in DHE; the ordinary AOT fixture is never registered as hotfix.

First exercise reference storage and type/member growth, changed virtual bodies,
ordinary AOT calls into hotfix objects, static field initialization and initializer
execution counts. Compare concrete records with CLR execution of the same input
generation, including a distinct revision and new-member observations. A list of
unqualified PASS labels is insufficient: accidentally executing an old case body
must not satisfy the Current reference.

Keep native value-layout and static value-storage cases as required follow-up.
Static value growth cannot be solved by replacing an offset alone: Current owns
the larger storage, unchanged fields retain coherent storage, and Base/Current
must share initializer state, exception caching and concurrency semantics. A
native implementation must define publication/rollback before changing ClassInit.

Primary gate: all expected records present and exact equality, unchanged AOT
sentinel, immutable Player/MV hashes, one shared Current set with per-Base plans.
No performance, ARM64, live replacement of existing instances, or release claim.
Preserve failed artifacts. Commit candidate sources before bound Player builds.
Rollback boundaries are fixture/runner changes and any separately committed
runtime/Unity hook fixes discovered by these cases. Do not publish tags or CAT.

## Default metadata and type initialization repair

The first two-Base run passed resource loading but failed exact CLR records:
an added optional parameter returned Missing, and a changed initializer ran
twice when it accessed both Base and added Current static storage.

Parameter reflection must separate logical Member identity, selected execution
signature, and Current metadata (name, flags and constants). Register immutable
logical-to-Current method metadata mappings during homologous image preparation;
read them only after the existing DHE acquire publication. Decode constants with
the image owning the method definition, including inflated methods. This must
also work when only a default changes and the method body remains AOT.

Choose one initialization owner: a matched Base with a cctor retains ownership;
otherwise Current owns the new cctor. Current-only types own themselves. Resolve
both Base and hidden Current entry points to that owner before completion checks.
Keep IL2CPP's existing lock, recursion/thread state, atomic completion publication
and cached exception on that single class; do not copy completion flags or run a
second initializer. Generic instantiations preserve their arguments and have
independent initialization state. Storage addresses remain unchanged.

The mapping is derived from immutable image metadata after acquire publication,
so preparation failure introduces no Base initialization-state mutations to
undo. Metadata and classes retain their existing process lifetime. Startup must
install DHE before business code touches these types; replacing already-running
initializers remains outside this validation. No x64-to-ARM64 inference is made.
New/removed cctors and ordinary AOT callers require explicit tests because native
code may omit an initialization call for a Base that originally had no cctor.
Any such uncovered path must stay a failed gate, not be described as supported.

The repair has no throughput claim. Primary acceptance remains zero differential
for defaults, repeated/reflection access, recursion, exceptions and concurrent
first touch across both Bases. Runtime mapping and Unity VM hooks are separate
candidate commits and can be rolled back together without changing old evidence.
