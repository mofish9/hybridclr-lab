# AOT hotfix module initialization after version selection

The preceding goal turn made verified progress in mixed retry and newly added
interpreter module initialization (`dhe-mixed-transaction-windows.md`). Continue
the full DHE goal on Unity 2022 Windows first, then Tuanjie. The initial runtime
is HybridCLR `9e7b601`, IL2CPP `8a13baf`, package `b51473f`, lab `905ee81`.

Source inspection identifies two concrete gaps for modules already in Base:
`BuildCurrentImagePlan` rejects metadata TypeDef row 1, and Unity's
`MetadataCache::ExecuteModuleInitializers` directly runs every AOT module cctor
before the managed DHE resource choice. Merely accepting a new MV cannot undo
those early Base side effects. Ordinary AOT modules must keep their normal
startup behavior; only configured DHE/hotfix module initializers may be deferred.

Reproduce with a real C# module initializer merged into the existing Model DLL
before building a new Base. The bootstrap records its counters before DHE load
and requires zero, then business entry checks exactly one selected initializer
with the expected version. Preserve the failing Base, Player logs and compiler
inputs. No archived project/Player may be edited. Create Current variants which
change, add or remove module initialization and repeat across different Bases.

Candidate design: put deferral in the package-generated DHE module-cctor guard,
selected from primary hotfix MVs (never ordinary guard inventories). Execute
mutable Current module initialization after the complete batch has committed
and all load locks have been released. Unchanged bodies retain AOT; changed
bodies interpret; removed module initialization does not run Base code. Support
module ownership in MV/execution mapping without treating the global type as
ordinary object storage. New runtime/package capability evidence must prevent
old Players from silently accepting unsupported module evolution.

Primary requirements: correct version and exactly-once behavior, zero early
hotfix side effects, unchanged ordinary initialization and AOT dispatch, complete
Current case sequences, failed-registration retry and module-exception behavior.
Use actual C# compilation, real-header native compile/CTest and immutable Windows
Players on committed identities. No performance, ARM64 or production qualification
is implied. Audit locks and publication order; never reset completed initializer
state to simulate a rollback. Source changes must have independent runtime,
package and lab commits. Rollback to the preceding identities preserves the
mixed interpreter-module milestone but does not support AOT module evolution.

## Reproduction and candidate gates

Lab `a41b4ca` adds the real compiler workload and before-load counter check.
`artifacts/dhe-aot-module-base-30` completes its native Player build on the old
runtime/package, then fails startup (PID 12980) before Current selection:
`moduleRunsBeforeLoad=1`, revision 0, no loaded Current assemblies. Its log begins
with `DHE selected module: 101:1`, before Unity's ordinary initialization logs.
This is the expected timing defect, not a successful Base archive. Preserve the
whole directory and `dhe-aot-module-base-30.inputs`. The latter's Model SHA is
`1E99A003235C64909CF82C2A11FCFB3BC25A6F23701DBC67AB79872E15371E09`.

Lab `647ae91` adds native global-owner cases. `native-before/DHE-Unity2022` under
`artifacts/dhe-aot-module-evolution-20260910` compiles the prior runtime with real
headers, then fails four CTest assertions for module method mapping and module
addition/removal. Global module instance-storage selection remains rejected.

Runtime candidate `9d778f0` retains module owners in explicit plans and executes
mutable module initialization after releasing load locks. It checks Current
before initializing the module class, so removed cctors do not fall back to
Base code. Package `0f0dd21` adds the deferral guard only to primary hotfix MVs,
not additional ordinary MV inventories, and verifies the matching native
feature macro. IL2CPP remains `8a13baf`. Capability
`deferred-aot-module-initialization-v1` and runtime contract `dhe-runtime-v28`
describe this new Base behavior; MV remains DHEMETA1/schema 1.

Lab `1c57aee` locks the candidate source identities. `runtime-01` has runtime
tree `EE32B4737EA4360694429494952B23921FBE9FC0C50F39AC87013B56ED2D6402` and
manifest `799EA8450FD45B364B92284DF8D84F6181066A2128CF9855CBCAAA598384102E`.
`native-01/DHE-Unity2022/native-gate.json` passes compile/CTest with real headers,
`mergeReady=true`, `surrogateExternalHeadersUsed=false`. `managed-01.json`
passes the 81 existing package validation/argument-selection checks; native
calls in that host are recorded, not executed.

Lab `55066b6` adds 20 passing policy checks (`policy-01`) for unchanged, helper
body changed, module cctor body changed, added and removed initializers. Every
case requires the new capability and rejects the old capability set. Frozen
source records containing module members also require it for TypeDef row 1
mapping, without deferring ordinary initialization.

`current-changed-01` and `current-removed-01` both pass all 46 CLR reference
cases. Their business entry additionally verifies module version/count as
202/1 and 0/0 respectively. These are Current-only variants and do not modify
any Base input. Their actual Player execution is the next gate.

Base-31 is being built from the exact Base-30 input DLLs on the new runtime and
package, using host-02 at `1c57aee`. Its generated native manifest identifies the
Model module cctor and records `deferModuleInitializer=true`. Do not qualify the
timing fix until its startup/no-op and Current resource replays pass.
