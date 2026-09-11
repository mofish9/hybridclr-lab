# DHE public status snapshots: Windows host gate

## Result and scope

The package no longer exposes a partially initialized assembly list to callers
reading DHE status during plan configuration. It publishes sorted array snapshots
when configuration, loading or reset finishes. Each getter returns a copy, so
callers cannot modify subsequent observations. Successful loads and committed
initializer failures publish the loaded set before the operation returns.

The committed candidate passes 126/126 execution-plan host checks. The preceding
package passes 122/126 with exactly four failures in the new concurrent plan
checks. Both were rebuilt and run with clean source trees and the same committed
fixture, input DLLs/MVs and tool executable. This is conditional managed-host
correctness evidence, not a Unity Player or native concurrency qualification.

## Implementation review

All writers already acquire `loadBusy` with `Interlocked.CompareExchange`.
`PublishAndReleaseLoad` now runs at the three writer exit points: configure,
execute-load and reset. It builds every array while it still owns the writer
gate, then release-publishes the references with `Volatile.Write`. Readers use
`Volatile.Read` and clone the selected array. Published arrays are never mutated.
The final release of `loadBusy` follows publication and executes even if snapshot
construction throws.

Each property is independently consistent. Separate property reads can straddle
a completed transaction; this change does not provide a single atomic view of
all properties. Other public metadata APIs and native metadata publication are
outside this proof. No new native capability, resource schema, package version,
runtime tag or supported structural-change category is introduced.

The interrupted draft refreshed snapshots during reset and configuration but
omitted the load exit point. That omission could leave the loaded list stale.
The final implementation centralizes publication at transaction boundaries and
tests loaded visibility before any subsequent reset attempt can mask it.

## Exact identities

| Component | Commit / SHA-256 |
|---|---|
| Candidate package, `research/dhe-parent-transitions-v8.13.0` | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Preceding package, detached control | `841abfd46e122343717fe4115186b97a215b58df` |
| Fixture and tool, `research/dhe-cross-assembly-parents-v8.13.0` | `46372939d728e13eb9f7c104d3b1c3c89a0d5522` |
| Candidate `ExecutionPlanTests.dll` | `D1416B63F68D27C87A02899BAAEF8F779DC3615D1B71C07D909820A1DE95F208` |
| Control `ExecutionPlanTests.dll` | `DDD2E445950EB2F7360A0F8229C740A93B0374FE6FD8BDFE4A659D7584A55749` |
| Shared `HybridCLR.DheTool.dll` | `76CF458E6591AC6432AE2D519B94438D59032F3B996D6EB643CB6518D608129E` |
| Candidate result | `09B2143CC607B59CD15B998E02B6D8DD60EFD50F4719A1523FA5E90F0B705363` |
| Control result | `0F9DEF329A9758C5EE16236053FC7C80ABC4EB60EDF6F26DC8926842691DFDC0` |

Both final reports record `labDirty=false`, `packageDirty=false` and hashes of
the actual package runtime source inputs. This documentation is a subsequent
commit and does not replace the tested source identity.

All outputs are under `C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents`:

- `execution-plan-concurrency-current-01/result.json`: 126/126, exit 0.
- `execution-plan-concurrency-baseline-02/result.json`: 122/126, expected exit 1.
- `execution-plan-concurrency-tool-01/HybridCLR.DheTool.dll`: shared tool.
- `execution-plan-concurrency-01` and `execution-plan-concurrency-baseline-01`:
  retained exploratory runs from uncommitted source; not the final gate.

Input root is `C:/hybridclr_optimize/artifacts/dhe-public-reflection-20260909`.
The host uses its two Base MV sets and identical Current DLL bytes. It adapts
Unity JSON/resource APIs and records native-call arguments without executing
IL2CPP. Earlier Player evidence retains its original package identity.

## Checks and reproduction

The concurrency fixture pauses the provider before adding the second assembly,
after the first private plan entry exists. Eight reader threads inspect the four
public status properties. Each array must be empty or complete. One run completes
successfully; another fails at that exact provider read and must publish empty
status. This schedule deterministically exposes partial plans in the old package
without depending on a lucky collection-enumeration exception.

The four expected control failures are `snapshot-valid-plan:partial-plan-hidden`,
`snapshot-valid-plan:concurrent-readers`, `snapshot-rejected-plan:partial-plan-hidden`
and `snapshot-rejected-plan:concurrent-readers`. Remaining checks cover caller
mutation, retained snapshots after reset, load success, preparation rejection,
committed initializer failure, reentry, retry and existing execution-plan policy.

From the lab commit above, build with the desired package root and a fresh output:

```text
dotnet build tool/HybridCLR.DheTool.csproj -c Release -t:Rebuild -o <new-tool-output>
dotnet build tool/fixtures/execution-plan/ExecutionPlanTests.csproj -c Release -t:Rebuild -p:DhePackageRoot=<absolute-package-worktree> -o <new-host-output>
dotnet <new-host-output>/ExecutionPlanTests.dll C:/hybridclr_optimize/artifacts/dhe-public-reflection-20260909 <new-result.json> <new-tool-output>/HybridCLR.DheTool.dll <absolute-lab-worktree>
```

Both builds completed with zero errors; the SDK emitted the existing net6.0
end-of-support warning. Outputs must be new: the fixture refuses to overwrite
an existing report. No throughput, tail-latency or memory claim is made.

## Remaining gates and rollback

HybridCLR remains `6180597d2c0e455ab09fe0920d34d6dea5ad00fc`; Unity 2022 IL2CPP
remains `819f74c08e466a0d2a8fe5b1afaad5b1d784e482`. Their preceding real-header
native gate and old-package Windows Player evidence are described in
[parent transitions](dhe-parent-transitions-windows.md) and
[cross-parent replay](dhe-current-host-cross-parent-replay-windows.md).
They were not rebuilt here and do not qualify the new package in a Player.

The new package still needs source-bound Unity 2022 Player regression. Native
publication stress, generic physical parent evolution, Scene/Prefab and
startup/live-object semantics, performance, memory and Android/iOS gates remain
open. Unity 2021 and Tuanjie work remains deferred by the user's current scope.

Rollback this package change with a reviewed revert of `ed4b7b5` when building a
new Base, or use the preceding exact package commit with its matching candidate
inputs. Runtime and resource formats are unchanged by this fix. A package source
change does not retrofit already-built Players; those retain their embedded
loader. No formal branch, tag, Installer default or CAT project was changed.

The four relevant candidate source trees and the detached control worktree were
clean after verification. The control is retained at
`C:/hybridclr_optimize/worktrees/hybridclr-unity-dhe-public-snapshot-baseline`.
No stash or deletion was performed; C: has approximately 3.84 GiB free.
