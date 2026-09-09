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
