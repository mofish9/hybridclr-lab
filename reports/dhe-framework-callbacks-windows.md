# Framework callbacks with evolved hotfix payloads: Windows checkpoint

## Result and identity

All eighteen framework callback checks pass on immutable old-layout Base-89,
grown-layout Base-90 and new-type-control Base-81. Each run also passes the
existing 25 virtual-signature and 46 business checks. The old-layout cached
receiver run passes those sequences together with all six pre-load receiver
checks. The shared resource passes identity auditing and public failure/recovery.

This adds verified behavior without changing native runtime or package sources.
It does not complete full DHE or qualify Android/iOS, performance or publication.

| Component | Tested source |
| --- | --- |
| Lab, fourteen-check Current and runs | 05de4d7 |
| Lab, eighteen-check Current and runs | bd90e93a60f8e4e08b24f7ab98d166a00f68245f |
| Package in Bases 89/90 | a96d3b9734fdbf51ebec8106d3935e3e32e83c1a |
| HybridCLR in Bases 89/90 | fdf1299b44cf5b4d5a6afe41dc449fb89cb888c9 |
| Unity 2022 IL2CPP in Bases 89/90 | 816778af772b67db5080d1569a27e41fbbea7452 |

The lab branch is research/dhe-framework-callbacks-v8.13.0. Later documentation
commits do not change these build identities. Base-89/90 identities and native
gate provenance are in dhe-virtual-admission-windows.md. Base-81 retains the
older ec5684e/b1e3952 runtime and has none of the original signature workload
types. It is a new-type control, not an old-runtime layout-evolution proof.

Paths below are relative to C:/hybridclr_optimize/artifacts/dhe-framework-callbacks.
Current compilation uses the actual Unity 2022.3.62f3 C# compiler and captured
Base references. The original preceding Current is preserved at
../dhe-virtual-signatures/current-01/current. Compiler-produced and entry-wired
outputs are retained separately; no existing Player or ordinary AOT input was
modified.

## Covered behavior

The initial current-01/reference-01/shared-three-base-01 and probe-base<N>-01
artifacts pass fourteen checks on each Base. They cover array/list sorting,
comparison delegates, IComparer, predicates, converters, iteration, binary
search, dictionary/hash-set equality comparers, a boxed struct delegate target,
and comparison/predicate exceptions. Payload checks validate every grown field
and preserve reference identity while values cross the framework call boundary.

The initial callbacks are new Current definitions. current-02 additionally
targets Processor methods that already exist in the Base AOT input:

- Array.ConvertAll invokes CopyValue with the grown Packet argument and return;
- List.ConvertAll invokes CopyReference and preserves the exact returned object;
- Array.ConvertAll invokes the closed generic Identity<Packet> method;
- Task.Run invokes Fail on a grown Processor receiver and preserves the expected
  exception through the task's AggregateException.

Base-89's native manifest contains generated entries for CopyReference, CopyValue,
Identity (including the fully shared generic entry), and Fail. It also contains
the tested generic Array/List/Dictionary/HashSet framework method definitions.
That proves the original AOT entries exist. Their presence alone does not prove
every closed framework instantiation executes natively after Current selection;
these tests do not make a per-method native-routing or performance claim.

reference-02/result.json passes eighteen callback checks plus all 25 virtual and
46 business checks under CLR. Its SHA-256 is
E89FF6CAC353832DDD091B5059947268C05455D8A50B1624FAE284B9DC689AE1.
probe-base89-02, probe-base90-02 and probe-base81-02 then pass the same sequences
on the real Windows Players. Their wrapper results bind the inner Player result,
log, host and the original immutable Player/resource evidence.

probe-base89-cached-02 also passes all six receiver checks. Its log contains the
exact eighteen callback sequence from reference-02, followed by successful
completion. Log SHA-256:
13E3EFC33D0F6CD91DAB9AB277EBEDA5294010C97DAE5089AF30E3A157BB70C2.
Safe rejection of an incompatible old receiver remains enforced; no automatic
object migration or relaxed native ABI guard is introduced.

## Shared resource and recovery

shared-three-base-02 uses the same current-02/current DLL bytes on all three
Bases. Current-set SHA-256:
a3f982997a2f51d71eb3b5d632cbd26e4ac15ab52f27080461343b00837e9d94.
Resource manifest SHA-256:
E3A942A704A67B131F0DC2CBCFF2A2697612966D4DC5CFC28CAF154B477804AB.

shared-three-base-audit-02.json verifies 167 files and the three complete
business runs against original Current/Player identities. SHA-256:
65689516B28774AC65DBE4F821F06FA8CCC350D6358232666CEB55872C689C44.
The explicit callback replays above are separate evidence from the ordinary
business-only resource workflow; a passing resource build alone does not count
as executing the callback suite.

shared-public-probes-02/result.json passes 22 native-preparation failure/recovery
checks and binds 138 files. SHA-256:
D72AA7924BE0D3882AF551D7AC8A833753FEA099ADD21F9AEC2A77F9BF1413E3.
It preserves the native preparation failure, verifies no business effects in the
failed processes and restores the valid resource in fresh processes.

## Remaining work and rollback

No new runtime defect was reproduced by these eighteen checks. Continue into
hotfix parent/declaration evolution, broader engine/native callbacks,
Scene/Prefab and pre-existing-object semantics, concurrent publication stress,
and production-equivalent performance/memory measurements. A successful Task.Run
callback is not a publication-race proof. No Windows result is extrapolated to
Android ARM64 or iOS.

Rollback selects the compatible shared resource from the preceding admission
checkpoint, or the immutable Base's own generated no-op resource, then restarts
the Player. The embedded native runtime and original Base identity remain fixed.
There is no live unload of a committed Current set. No formal branch/tag/remote,
Installer default or CAT source was changed, and no stash was created or removed.
