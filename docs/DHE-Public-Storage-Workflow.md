# Public Current storage integration

Scope: finish Unity 2022 Windows before porting to Tuanjie 2022. Unity 2021 is
outside the user-requested scope. Keep ordinary AOT assemblies immutable and
unaffected hotfix methods eligible for AOT execution.

First acceptance boundary: expose the existing validated multi-image Current
storage transaction through RuntimeApi, then run the guarded storage fixture
using that public entry and ordinary MethodInfo.Invoke. The fixture must not
resolve reflected methods through a lab-only internal call. Check method-only,
no-op and layout evolution against different immutable Bases. Bad plan tokens,
missing selection arrays and duplicates must fail before image publication;
the same process must still accept the valid payload afterwards.

Second acceptance boundary: derive per-Base storage and method selections in
the C# resource builder, bind them to Base/Current MV and the selected Base
identity, preserve them during staging, and consume them in DheRuntime. Layout
impact includes unchanged cross-assembly callers and generic contexts. A method
body hash alone is insufficient. Newly introduced methods are already
interpreted and must not be mistaken for methods with Base entry guards.
Native ABI obligations must remain explicit; probe success does not authorize
removing the resource compatibility rejection for arbitrary layouts.

Primary metrics are correctness (zero differences from CLR), selected changed
entry execution, unchanged AOT sentinel execution, and rejection before native
mutation. No throughput, P99, memory or mobile claim is made. FGS and real Unity
2022 headers are used. No new cache or publication order is introduced by the
API adapter: it reuses the serialized, prepared multi-image registration.
Later public resource identity validation must finish before entering it.

The first public-API Player exposed a real failure: Factory.Create invoked via
ordinary reflection still used the Base struct return ABI and raised the
Current-frame guard. The five invalid-plan checks and revision dispatch passed.
Correct reflection at its managed invocation boundary, before receiver and
argument conversion. ParameterInfo and ReturnType must expose that same
physical signature while preserving the logical declaring/reflected owner.
Keep raw native Invoke calls under their existing ABI contract. Add boxed value,
byref, nullable, generic and foreign-assembly argument coverage.

Rollback boundaries: separate candidate runtime API, package API/loader,
compiler/planner and fixture commits. Do not update runtime tags, Installer
defaults or formal branches until the complete public workflow is qualified.

The resource transport stores an optional executionPlan under each per-Base
assemblyModes entry, never in the shared Current payload. It binds the exact
Base MV and Current MV SHA-256 and sorted Current TypeDef/MethodDef token sets.
Manifest, validation and runtime plan must carry identical selections. The
package validates the selected plan against its embedded Base MV before native
loading, and preserves the plan for both initial load and transaction retry.
An interpreter-only assembly has no Base plan. The runtime capability is
current-storage-execution-plan-v1; this candidate accepts it on Unity2022Fgs.

Host regression fixture: tool/fixtures/execution-plan. Compile with an explicit
DhePackageRoot. It includes the actual package validator/loader source and
records native arguments, testing two Bases, identical Current resources,
reordered load inputs, invalid bindings/tokens and inconsistent manifest tables.
Synthetic identity construction and the host JSON adapter make this a package
logic test only; a complete native Player through the resource builder/stager is
still required before declaring the public resource workflow qualified.
