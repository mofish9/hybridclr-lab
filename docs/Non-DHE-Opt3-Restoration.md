# Non-DHE opt3 maintenance restoration — 2026-11-11

The user paused DHE and requested removal of post-opt3 DHE commits from the active maintenance lines. The retained runtime is byte-for-byte opt3, including cumulative opt1/opt2/opt3 optimizations. No upstream baseline upgrade is permitted.

## Scope and acceptance

- Observable behavior: ordinary HybridCLR assembly loading, interpreter execution, reflection and optional FGS; DHE build, loader, structural overlays and startup selection are removed from the active package/runtime.
- Primary gate: exact runtime commit/tree equality with opt3 for HybridCLR and all three engine hooks. Re-run the native compatibility matrix against real Editor headers. Package differences are limited to installer directory resolution and clone failure handling.
- No new performance claim: median, P95, P99, memory and managed semantics are expected to remain those of the exact opt3 runtime. Historical Player/performance results retain their original identities. This restoration does not close pending Android ARM64 or frozen-identity performance gates.
- FGS and supplemental metadata behavior stays at opt3, independently selected by each engine workflow. Prewarm APIs stay at opt3. Unity 2021 uses its own opt3 hook, not the later official-only selection; Unity 2022 stays on the approved IL2CPP 8.11.0 baseline; Tuanjie uses its opt3 hook.
- Rollback: every prior maintenance head is retained under an archive/dhe-paused-20261111/* reference. Historical DHE tags, research worktrees, artifacts and existing stashes are retained. The local restoration was followed by the separately authorized remote publication recorded below.

## Generic repairs retained

Only package installer repairs are retained: version-suffixed embedded package directory discovery (including ambiguous-directory rejection), and immediate failure when git clone fails. They have no runtime ABI or execution-path effect.

Embedded-NUL conversion and generic-parent vtable repairs are deferred: applying them would create a new runtime identity requiring separate semantic gates and a new immutable runtime release, rather than the requested exact opt3 restoration. DHE-specific PInvoke regression repairs and PendingDheImage C++11 changes are unnecessary once DHE is removed.

## Verification and final identities

Both Workflow and Metadata native matrices passed on all three engines (six profile builds and CTest runs), with mergeReady=true and surrogateExternalHeadersUsed=false. Unity 2022.3.62f3 compiled the package Editor and Runtime assemblies in a fresh isolated project using the version-suffixed embedded directory. The package runtime execution sources are unchanged from opt3.

Evidence: [verification](../reports/non-dhe-opt3-20261111-verification.json), [Workflow matrix](../reports/non-dhe-opt3-20261111-native-workflow.json), [Metadata matrix](../reports/non-dhe-opt3-20261111-native-metadata.json). The checked test harness is lab commit 999f325; subsequent lab changes only record these results and local status. No Player correctness/differential, performance, PSS or Android device run was performed in this restoration. Historical opt3 measurements are not new results.

| Repository / engine | Active maintenance branch | Commit | Runtime tag |
|---|---|---|---|
| repos/hybridclr | optimize/v8.13.0 | f40c6f08ccd0391ad9285276b4cc21ada3a180ab | v8.13.0-opt3 |
| repos/il2cpp_plus | optimize/unity2021-v8.1.0 | 511df62f4aa7cba2bdb505deead48a8d080d582c | v2021-8.1.0-opt3 |
| repos/il2cpp_plus | optimize/unity2022-v8.11.0 | bf15337e189ae7da5876aa51c9b896a36c52a155 | v2022-8.11.0-opt3 |
| repos/il2cpp_plus | optimize/tuanjie-1.10-v8.13.0 | 82162cb681f1564807d2ab5e7f3a9312271d0a73 | v2022-tuanjie-8.13.0-opt3 |
| repos/hybridclr_unity | optimize/v8.13.0 | 681976b74e965f948dbf93d90d8c2900a141bb7d | None; distribute by commit |

Package tree: a96b263fcb980dd9fae33d936f0287b84eafaae0. Its only delta from ac0fdc5 is two installer files (+35/-3). All three engine entries in Data~/hybridclr_version.json select opt3. No opt4–opt9 runtime commits are ancestors of the active runtime heads. All existing runtime tags are unchanged, and no new tag was created.

## Local workspace state

The three primary source checkouts and lab are clean after the report commit. There are 159 registered worktrees: 9 historical dirty trees and 2 stale registrations whose directories were already missing. The 3 existing FGS/metadata stashes are unchanged. No new stash was needed. The package/lab restoration candidate worktrees were removed after integration; the clean Unity 2021 opt3 checkout remains as the validation manifest's engine input.

The dirty trees and missing paths are listed with their unchanged statuses in [workspace inventory](../manifests/non-dhe-workspace-status-20261111.json). Historical research worktrees, ignored build outputs and releases are retained. They are not active delivery inputs. The unapproved optimize/unity2022-v8.14.0 branch name was moved under archive/dhe-paused-20261111/former-unity2022-v8.14.0; its commit remains recoverable.

The user subsequently authorized remote publication on 2026-11-11. All runtime maintenance branch heads were verified remotely to equal their opt3 tag commits exactly. Package optimize/v8.13.0 was verified at 681976b74e965f948dbf93d90d8c2900a141bb7d, exactly one installer-only commit after ac0fdc5. Its Runtime directory and Data~/hybridclr_version.json are byte-for-byte opt3. The lab lock and this report are published last. See [remote verification](../manifests/non-dhe-remote-publication-20261111.json).

All pre-restoration heads were first pushed and verified under archive/dhe-paused-20261111/*. Maintenance branch rewrites used explicit force-with-lease values from a fresh remote read, with atomic multi-ref updates within each repository. The obsolete IL2CPP optimize/unity2022-v8.14.0 remote branch was removed only after its exact head was archived. All existing tags, including opt4–opt9, remain unchanged. Repository default main branches remain unchanged: “current non-DHE version” means the named optimize/* maintenance branches and the opt3 tags selected by the package.

The workspace inventory and test verification JSON files are snapshots made before publication; their remotePublication=false fields describe that earlier stage. No new runtime, Player, performance or device qualification is claimed by this publication. HTTPS transport was unavailable; Git SSH on port 443 used strict host verification against the official GitHub /meta keys.

## Exact recovery

[Rollback refs](../manifests/non-dhe-restoration-rollback.json) records every previous maintenance branch head and its archive ref. To recover a previous source combination, first preserve any subsequent edits, then reset each corresponding clean local branch to its recorded before commit (or archive ref). Restore the corresponding lab archive as a unit. Original DHE opt4–opt9 annotated tags remain available. Local recovery can use the saved refs directly after preserving subsequent edits. A future remote rollback must also use fresh explicit leases; this record does not authorize another history rewrite.

The root workspace itself is not a Git repository. Its README now points to this active opt3 state; the previous README is saved at tmp/non-dhe-restoration-20261111/README.before.md. No game-project package or installed Unity runtime was modified.
