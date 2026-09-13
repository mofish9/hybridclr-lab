# Current physical receiver dispatch — Unity 2022

User retired opt5 as the delivery target on 2026-09-13. Existing refs identify
historical builds only. Fix source first; do not change upstream 8.13.0/8.11.0,
publish a replacement tag, or migrate cat in this experiment.

Reproducer: boxed-current-01 adds one IEnumerator case to the unchanged 46-case
Current workload. Old opt5 fails IEnumerable.GetEnumerator before returning the
boxed enumerator; CLR passes. The prior reference-owner blanket exemption in
a7c4cbe is unsafe: a reference-sized receiver does not prove value parameters,
returns, byrefs, fields or generic context. It is excluded from this branch.

Hypothesis: selected methods have fallback-image declaration owners, whereas
allocation resolves those owners back to the selected physical definition with
Current generic arguments. Virtual dispatch compares a real allocation against
the declaration owner, and incorrectly falls back to the Base native frame.

Change boundary: resolve the method owner's physical execution class using the
same metadata mapping used for allocation; validate exact receiver/ancestry and
the concrete caller signature before selecting Current execution. Boxed values
require exact physical class identity. Never disable CanEnterWithBaseAbi.

Primary metric is correctness: exact CLR/Player case sequence, zero differences,
safe rejection of old/unrelated receivers and incompatible call signatures.
Retain no-op AOT dispatch, full ordinary guards, frozen original ordinary IL,
atomic publication, cctor and exception behavior. No performance claim; report
costs separately before optimizing. Uses FGS/OptimizeSize, no supplemental AOT
payload, startup loading before gameplay. No live object migration.

Freeze source and locks, assemble a new runtime, run real Unity 2022 headers
native compile/CTest, then build a new Base via the full existing C# workflow
and replay the exact 47-case Current. Old modified experimental staging/project
directories cannot authenticate this result and will not be reused for building.
Unity 2021 is official/unoptimized; Tuanjie is deferred by user instruction.
Windows evidence does not certify ARM64, Android, iOS, memory or P99.

Rollback is the parent runtime commit and its matching Base, not mixed source
or relabeled tags. Failed/rejected candidates remain isolated.
