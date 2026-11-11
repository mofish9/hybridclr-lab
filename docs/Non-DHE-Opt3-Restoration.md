# Non-DHE opt3 maintenance restoration — 2026-11-11

The user paused DHE and requested removal of post-opt3 DHE commits from the active maintenance lines. The retained runtime is byte-for-byte opt3, including cumulative opt1/opt2/opt3 optimizations. No upstream baseline upgrade is permitted.

## Scope and acceptance

- Observable behavior: ordinary HybridCLR assembly loading, interpreter execution, reflection and optional FGS; DHE build, loader, structural overlays and startup selection are removed from the active package/runtime.
- Primary gate: exact runtime commit/tree equality with opt3 for HybridCLR and all three engine hooks. Re-run the native compatibility matrix against real Editor headers. Package differences are limited to installer directory resolution and clone failure handling.
- No new performance claim: median, P95, P99, memory and managed semantics are expected to remain those of the exact opt3 runtime. Historical Player/performance results retain their original identities. This restoration does not close pending Android ARM64 or frozen-identity performance gates.
- FGS and supplemental metadata behavior stays at opt3, independently selected by each engine workflow. Prewarm APIs stay at opt3. Unity 2021 uses its own opt3 hook, not the later official-only selection; Unity 2022 stays on the approved IL2CPP 8.11.0 baseline; Tuanjie uses its opt3 hook.
- Rollback: every prior maintenance head is retained under an archive/dhe-paused-20261111/* reference. Historical DHE tags, research worktrees, artifacts and existing stashes are retained. No remote rewrite is part of this local operation.

## Generic repairs retained

Only package installer repairs are retained: version-suffixed embedded package directory discovery (including ambiguous-directory rejection), and immediate failure when git clone fails. They have no runtime ABI or execution-path effect.

Embedded-NUL conversion and generic-parent vtable repairs are deferred: applying them would create a new runtime identity requiring separate semantic gates and a new immutable runtime release, rather than the requested exact opt3 restoration. DHE-specific PInvoke regression repairs and PendingDheImage C++11 changes are unnecessary once DHE is removed.

## Verification and final identities

Results will be recorded after the source commits and validation complete. No historical result is relabeled as a new run.
