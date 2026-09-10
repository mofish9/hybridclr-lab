# AOT hotfix module initialization after version selection

Current checkpoint: the v31 candidate passes all four preserved multi-Base
resource variants on immutable Unity 2022 Windows Base-36/37. The unchanged AOT
caller/changed inline callee probe also passes on Base-37. See the v31 evidence
section at the end; earlier failures below retain their original identities.
Actual addition to Base-38 without a module initializer also passes: both preserved
Current variants now serve Base-36/37/38 in one resource. Public post-commit
initialization-failure state and recovery remain the next implementation gate.

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

## Constant evolution follow-up

Base-31 startup (PID 9984) and no-op resource (PID 18500) now pass with
`moduleRunsBeforeLoad=0`, followed by exactly one version-101 initializer.
Both `resource-changed-01` and `resource-removed-01` fail resource validation:
changing the existing `ExpectedVersion` literal from 101 to 202 changes the
field metadata and static-field fingerprints. Preserve both outputs and the
exact `current-changed-01/current` and `current-removed-01/current` inputs.

The follow-up must admit only constant-value changes on otherwise identical
static literal fields. Keep every existing MV fingerprint unchanged; add an
analysis-only constant-independent fingerprint. Visibility, storage flags,
layout, marshal and runtime-semantic attributes remain checked. IL2CPP must
resolve existing literal defaults through the published Current token map,
including reflection handles created before load, while ordinary AOT remains
unchanged. Capability-gate this behavior because Base-31 has the old default
reader. Build a new immutable Base from the exact Base-30 inputs, then replay
the preserved resources plus explicit GetRawConstantValue/GetValue probes.
No performance claim is made. Rollback is the preceding tool/package/IL2CPP
combination, which rejects these updates before executing business code.

Lab `89d061a`, IL2CPP `4d5052e`, package `3c68563` (runtime contract v29)
admit literal-value changes with `current-literal-field-values-v1`; the native
reader uses the published Current metadata token map for existing literals.
`policy-before-constant` fails three assertions on the prior tool; `policy-02`
passes all 31. `native-02` passes real-header compile/CTest on runtime tree
`412D7EE768CF880A585ADADB847BDD1FC9C773413A8787D220D273AF1617BF73`, manifest
`80D7D133CB8FC4BA178A561DBD5CB1DDE2C5E24682B517048975409FE1F1432D`.
`managed-02.json` passes the 81 package validation/argument-selection checks.
`resource-old-reader-rejected-01` rejects Base-31 for the missing literal-value
capability, before running the Player. The MV format/fingerprints are unchanged.

Base-32 builds from the exact Base-30 input DLLs. Startup PID 16912 and no-op
PID 5364 pass; retained/fresh raw and boxed constant reads all return 101.
Its Base ID is `da9b905c7d0fac29e7972fd3f6b4e0463e9d4ee563c1076d59a6c67f55c134a6`,
snapshot `5e840aa21e28376ec5a9bbbe456fbcb436516a412a4290d7c711947d87791759`,
GameAssembly `36AC5C02E811DA113D13758FB2344D94DCAB0CCDD1AEDEF04E26A8E73594B752`.
The exact prior Current bytes now pass resource compatibility, but real replays
`resource-changed-02` (PID 7972) and `resource-removed-02` (PID 5168) both return
DHE_MV_REGISTRATION_FAILED before publication or business entry. This is not a
passing constant/reflection execution result. Preserve both complete outputs.

The native Base method resolver enumerates `Image::GetTypes`, which deliberately
hides `<Module>`. Changed/removed cctors therefore cannot resolve their old
MethodDef. The old standalone stub incorrectly included this hidden type. Lab
`032ae95` corrects that simulation and adds changed/removed module registration
checks; `native-before-module-lookup` fails seven assertions on `9d778f0`.
HybridCLR `421bb18` instead enumerates physical Base definitions, excluding
supplemental Current reflection views. Package `831f2f6` adds capability
`aot-module-token-resolution-v1`, contract v30; IL2CPP remains `4d5052e`.
New source-bound native and immutable Player verification is required.

Lab `2418ab5` adds 74 CLR-verified literal reflection assertions for numeric,
string/null/Unicode, enum and generic-owner literals (`literal-base-01` and
`literal-current-01`), alongside all 46 preceding cases. `2c2f84e` adds independent
module/literal replay audits. `2169c5e` adds a real C# second module initializer,
changing the compiler-generated cctor body (`current-chained-01`). Base-33 is
being built from the newer module/literal inputs on the prior v29 runtime; it
must retain that identity and cannot qualify the v30 resolver fix.

The seven native lookup failures pass on `421bb18` in `native-03`, including
real-header compile/CTest. `policy-03` passes all 43 checks (including rejection
of the old module resolver). Base-33 finishes building but its final ordinary
guard coverage check rejects before startup: package `DheOrdinaryGuardInventory`
also omitted `<Module>`. `ordinary-guards-before` reproduces two failed coverage
assertions against Base-33's exact captured Native DLL. Package `eaf1ec5` includes
module methods in ordinary guard inventories without changing primary-only
initializer deferral. Its canonical tree is
`E0560DD1615040D477BAC0BF02E7B52E920FEF41D0B0E10288362F4FACF565D7`.
Keep Base-33 and its `.inputs`; it is not an accepted Base proof. Verify the
combined source on new immutable Players using exact Base-30 and Base-33 inputs.

## Immutable v30 Players and remaining replay failures

Lab `7735660`, HybridCLR `421bb18`, IL2CPP `4d5052e`, package `eaf1ec5` pass
`ordinary-guards-01` (four checks), `native-04` real-header compile/CTest and
`managed-03.json` (81 package checks). Runtime tree:
`79824B4AFC63EFEA7338F963FBA14D85AA2CE540C14C4B069D9D10F6E634EF04`; manifest:
`AFA88F48A466CCE731E57C8319771615D8DE0DFEA090A5FF06FD99A1E4DEF0CE`.
`resource-old-resolver-rejected-01` rejects Base-32 for its missing module-token
capability before Player execution.

Both fresh Players pass startup and no-op on this precise identity:

| Base | Input | Startup / no-op PIDs | Base ID | GameAssembly SHA-256 |
| --- | --- | --- | --- | --- |
| 34 | Exact Base-30 inputs, old layout, module 101 | 23340 / 21404 | `ccef9f455e6ba1b449c4e55c4ed724af1a01f5d10a9918bdef03c9660285721e` | `97212B25FF4C6945C159D1CBE246E0A75269662925E2FA34AB5E1247D0F689C2` |
| 35 | Exact Base-33 inputs, grown layout, module 202, literal suite, ordinary module | 16664 / 21268 | `5e5be504ea1aa3f4c855910a43cea755797c787afd61475e1dcff053b4bd3a90` | `4ED7E83E98CC69C7DBED4D0447D45965F07140A60F398EDE15DF15CD70955C57` |

Base-35 shows the ordinary initializer at the start of its log, counter 1
before and after loading. Hotfix initialization is deferred, counter 0 before
load and one version-202 invocation after it. All 74 baseline literal reads
pass. The final native manifest includes both module cctors, deferral true for
Model and false for Native.

`resource-changed-03-multibase`, `resource-removed-03-multibase` and
`resource-chained-01-multibase` pass their initial Base-34 executions (PIDs 19972,
16692, 22940) and all 46 cases; changed constants read 202 through retained and
fresh handles. Restored Base-34 runs also succeed. On Base-35 they reach the last
case, then fail `unchanged-readers-stay-aot` (PIDs 24300, 20460, 24012). Thus no
one of these multi-Base workflows has a passing final result/audit yet.

The exact archived generated C++ shows the cause: the AOT
`FrozenStaticCases.VerifyNativeDispatch` calls `_inline` copies of
`NativeStaticOwner.ReadNeighbor/ReadRuns`; these copies lack guards although the
standalone functions appear in the universal guard manifest. Do not reduce the
assertion threshold or force this test into interpretation. Cover indexed
native symbols' ABI-identical inline copies in every caller, and additionally
test an unchanged AOT caller invoking a changed inline hotfix getter.

`resource-literals-01-multibase` fails first on Base-34 (PID 23676): the existing
constant converter treats UTF-16 length as a maximum and truncates at embedded
NUL. Preserve the exact `literal-current-01/current` bytes. HybridCLR `8417ea0`
adds an exact-span UTF-16 constant writer, shared by field and parameter default
conversion. Package `6d59a27` expands only indexed symbols to validated inline
copies, preserving signature/owner checks. Contract v31 advertises
`length-preserved-constant-strings-v1` and `aot-inline-entry-guards-v1`; the tool
must reject incapable Bases, including NUL defaults in new assemblies and
frozen ordinary sources. These newest changes still need source-bound native,
inline-definition and new immutable Player verification. No previous passing
v30 numbers qualify v31; no release or platform extrapolation is authorized.

## Immutable v31 resource replay and independent audits

The committed candidate is HybridCLR `8417ea0cd0cc768f659fc539533038b3fae6280a`,
IL2CPP Unity 2022 `4d5052e28b6be289f21cc3b79b349675483f2b38`, package
`6d59a278cd9daab60a4d0f8df1f71e729a7e95f9`. Package canonical tree:
`9D70528FD83F34375700929511F3678C7CC9B42A333605B5D04540C973D986E8`.
These are research worktree commits, not a new formal branch/tag release.
No CAT, Installer default, formal branch or remote was changed.

Under `C:/hybridclr_optimize/artifacts/dhe-aot-module-evolution-20260910`,
`runtime-05/DHE-Unity2022` binds runtime tree
`1E3A6EEFDC31D12465C9457177BBD084BA1B1B17385C1879969E0E1900050F65`, manifest
`D387B5C0B0221E294D705DA1102A4A027F21A7132D77B734B4BA915F10DC69C1`.
`native-06` passes real Unity 2022.3.62f3 headers, compile/CTest, with
`mergeReady=true`, `surrogateExternalHeadersUsed=false`. Preserve `native-05`'s
earlier test-host linker failure; lab `a39064f` supplied its missing `Memory::Calloc`
stub. `managed-04.json` passes 81/81 package validation/native-argument-selection
checks; it records native arguments without executing native calls. `policy-05`
passes 52/52 checks on lab `807e63a`, including scalar constant-kind rejection.

The following Players are archived under `D:/hybridclr_artifacts`. Both were
built on clean lab `a39064f`, host-15 SHA
`ACB579D61E907FA3377F0F2F0650E03FE15B48448AD915E174577212B686B299` and build tool SHA
`BED46F7C977C9FF8ACFE79E88C753014C655B3E19B3BC7C033FCAEBBDFED1276`.
Do not relabel their build tool as the newer resource-generation tool below.

| Base directory | Input | Startup / no-op PIDs | Base ID | GameAssembly SHA-256 |
| --- | --- | --- | --- | --- |
| `dhe-aot-module-base-36` | Exact Base-30 inputs; old layout, module 101, three hotfix DLLs | 20944 / 23040 | `d934240254f353947833eec69935be56cda940c0a8b05de2f5e7c15b55fd9cb0` | `933212378409DBB8C1EFCFDEC6188C2FDCB8C5EE291033BF8A4CCF827B7AE790` |
| `dhe-aot-module-base-37` | Grown layout, module 202, baseline literals and inline probe, ordinary initializer, four hotfix DLLs | 24552 / 23892 | `797d156fe84be7bce27874396fa2755e85daba6db19232165bd27710159ccda0` | `5550452542532C8CEB512705A17EF435EFA17ADDB551080DF3E00920EBD46385` |

`inline-base-36.json` and `inline-base-37.json` audit 4,946 and 4,970 actual
indexed inline copies respectively, with zero missing guards. ABI/context
mismatch and foreign-symbol rejection checks also pass. Base-37 generated C++
confirms its non-inlined `InlineHotfixCaller.Invoke` calls the guarded
`InlineHotfixCallee.Read_inline` copy. Startup/no-op logs report `17:2:0`.

Resource workflows ran on clean lab `807e63afa3967ade138f2a1b8e3020c4594b2f97`,
host-16 SHA `4D565FB03AD63BAF560122136E43388AF9F4A5388A5E3E9B86706FDFFE43257C`,
tool-07 SHA `75597987BAE9B0F11E34D0DBC5C0C06A60EDC810F5A3A297C884F1E1894D12B1`.
Every workflow preserves the exact pre-existing Current DLL bytes. Independent
auditor lab `7f32432`, `D:/hybridclr_artifacts/dhe-v31-host-17`, SHA
`8424DF4F598B5204F31EAD19F4DB0B1FACB551E433E5F6F4968867A90EFB3E9F`, rehashes
original Base identities, Players, snapshots, original Current DLLs and staged
resources, and verifies complete successful/restored traces and pre-entry failures.

All outputs below are under `D:/hybridclr_artifacts`; each audit is the sibling
`dhe-v31-<variant>-audit-01.json`. Every successful execution matches the full
46-case CLR reference sequence, differential zero.

| Resource output | Bases | Initial Player PIDs | Workflow checks | Successful / rejected runs | Audit checks / files |
| --- | --- | --- | --- | --- | --- |
| `dhe-v31-changed-01` | 36, 37 | 19752 / 18948 | 16/16 | 4 / 3 | 48 / 126 |
| `dhe-v31-removed-01` | 36, 37 | 13612 / 18704 | 16/16 | 4 / 3 | 48 / 126 |
| `dhe-v31-chained-01` | 36, 37 | 17620 / 5536 | 16/16 | 4 / 3 | 52 / 126 |
| `dhe-v31-literals-01` | 36, 37 | 16216 / 20588 | 16/16 | 4 / 3 | 52 / 126 |
| `dhe-v31-inline-01` | 37 | 19268 | 7/7 | 1 / 0 | 19 / 67 |

For each two-Base resource, both Bases select one identical Current DLL set.
Base-36 additionally exercises the new-DLL missing/corrupt/restored path and
frozen-snapshot substitution/restoration; Base-37 already contains that DLL and
needs no frozen projection for this layout. The single-Base inline row has no
new-DLL or frozen-source corruption runs; do not infer those from its seven checks.

The updated module runs once at version 202; removal runs zero times. The chained
variant also runs its real second C# initializer exactly once. All hotfix counters
are zero before load. Base-37's ordinary initializer remains eager, counter one
before and after load. Retained and fresh reflection handles read Current constants.
The literal variant passes all 74 additional numeric/string/null/enum/generic
reflection assertions on both Bases, retaining the exact `Current 常量\0尾` string.
Inline resource PID 19268 reports `DHE inline hotfix pass: 18:2:1`: unchanged
caller remains AOT, only the changed callee enters the interpreter. No threshold
was reduced and the caller was not forced into interpretation.

The following continuation closes the actual addition gate using Base-38 without
a module cctor. Public post-commit initialization-failure reporting and recovery,
ordinary ThreadStatic/RVA, native-only ABI obligations, broader
Unity behavior and production-equivalent performance/memory remain open. This
milestone conditionally passes Windows correctness only; it is not complete DHE
or production qualification. Tuanjie follows Unity 2022; no new Unity 2021 work.

Rollback keeps the archived Players and selects the preceding runtime/package/tool
combination when building a new Base. Already shipped Players cannot acquire new
native capabilities from DLL resources; capability admission must continue to
reject them. For an existing capable Base, select a previously accepted resource
at process startup. Do not claim in-process rollback after metadata commit or
initializer side effects. Preserve all v28-v30 failures and their original inputs.

C: had about 11 GiB free. Automatic approval review rejected the attempted removal
of Base-30..35 Bee `.obj`/`.pch` caches (`blocked by policy`); nothing was deleted.
New large builds and replay outputs use D:. No stash or destructive cleanup occurred.

## Actual initializer addition across three immutable Bases

`D:/hybridclr_artifacts/dhe-aot-module-base-38` passes startup PID 22040 and no-op
resource PID 17324. Its inputs derive from the exact removed-initializer Current,
with the existing ModuleState type and no module cctor, four hotfix DLLs and an
ordinary initializer. Bootstrap and entry prove hotfix runs/version `0:0`; ordinary
initialization stays eager once. The existing `aot-module-next-base` C# workflow
creates this input and Player; no runtime or package change was needed.

Build identity: lab `7f3243284722dd7a9a0cfa18bad7c3b8e7423fd6`, host-17 SHA above,
build tool SHA `981F134218C02D6003D8A3656E481512C9EF3B2C75398B26FCCE958EA96B416E`,
and the same v31 package/runtime-05 combination.

- Base ID: `31cb6340788ec6998b4532ded7dac79052e2bcb82ba2416329089867c84ee436`.
- Build identity SHA: `382A403CE62C54D74FFC35444807C06D5539854E6F214A8512A88CFC423F35EB`.
- Snapshot SHA: `7b267fd0b6855c49ca51190e7b9b6510561cf37994a089af2d2545d49b9c7505`.
- GameAssembly SHA: `14E0D5960E0994D1EEBBE857A12D98A52ADE26386DC8E3366727D03C0F2691B6`.
- Inline audit `dhe-v31-inline-base-38.json`: 4,969 indexed copies, zero missing;
  all five signature/coverage checks pass.

Runner/auditor lab `7c44bd960b3c0ff60ba1220b1e2ef03dc7a4b2d5`,
`D:/hybridclr_artifacts/dhe-v31-host-18`, SHA
`325911D9ACD35D6C6BD9FAE99758E220411FE9C3D373097E1CB5F6882ECFA08E`, uses unchanged
tool-07 to generate two new resources, each supporting Base-36, Base-37 and
Base-38. Current DLL set hashes equal the earlier changed/chained resources;
neither the previously failing inputs nor the existing Players were rebuilt.

| Output under `D:/hybridclr_artifacts` | Base-36 / 37 / 38 initial PIDs | Workflow checks | Successful / rejected | Audit checks / files |
| --- | --- | --- | --- | --- |
| `dhe-v31-added-01` | 24320 / 17880 / 4092 | 20/20 | 5 / 3 | 59 / 176 |
| `dhe-v31-added-chained-01` | 18712 / 22220 / 22948 | 20/20 | 5 / 3 | 64 / 176 |

Each sibling `dhe-v31-<variant>-audit-01.json` verifies all 46 reference cases
per successful/restored run, all three original Base identities, and pre-entry
rejections without initializer effects. The auditor reads each authenticated
Base snapshot DLL directly: both resources record module-cctor transitions
`true -> true`, `true -> true`, `false -> true`. Base-38 reports version 202,
runs one; the chained resource additionally reports second-initializer runs one.
Ordinary initializer counters remain one before and after load on Base-37/38.

The shared resource manifests have SHA-256
`855F35D065A826C8F6E13800C5E84EA0E08188198B009862770E4BF4F51A11AD` (added) and
`5306A48FA03B1072ADE40C6115545350A7011855F41BD4A7B6D5AE02767A3727` (added/chained).
Their Current set hashes remain `ed1d0b59e136874634c7f3ead29e93319cfb1d8df90894b563c7965808f89ce6`
and `72c4a1f296248b50e0d01db1be3c68c2967938792cb1be546b40838273555f8c` respectively.
The extended auditor also passes the preceding removed/chained workflows in
`dhe-v31-removed-audit-02.json` (49 checks) and `dhe-v31-chained-audit-02.json`
(53 checks), explicitly confirming `true -> false` and `true -> true` transitions.
The first audits remain valid at their original auditor identity.

Remaining P1 for release: `DheRuntime.LoadCurrentAssemblyImages` and
`LoadAssemblyImages` can report a module exception as registration failure even
though native metadata is committed. Their success-only bookkeeping has not yet
recorded loaded assemblies, and `Reset` only clears managed state. Existing native
exception tests prove committed visibility, but do not qualify public recovery.
The next change must distinguish pre-commit rejection from post-commit failure,
expose a stable restart-required state through the public API, and prevent managed
reset/retry from implying native rollback. Verify failures and retries through
the public resource loader on a new immutable Player, including mixed new-assembly
and existing-only payloads. Recovery after commit means a fresh process choosing
an accepted resource; never reset executed initializer side effects in place.

All four relevant candidate worktrees are committed; no stash was created.
This is still conditional Unity 2022 Windows correctness, with the broader goal,
performance/memory qualification and Tuanjie follow-up incomplete.
