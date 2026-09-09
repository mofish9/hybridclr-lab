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

Rollback boundaries: separate candidate runtime API, package API/loader,
compiler/planner and fixture commits. Do not update runtime tags, Installer
defaults or formal branches until the complete public workflow is qualified.
