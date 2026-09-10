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
