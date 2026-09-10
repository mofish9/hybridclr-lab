# Mixed metadata registration, retry and module initialization

Current checkpoint: native mixed registration/retry passes on an immutable
Unity 2022 Base, including reversed input order, changed/missing pending peers,
module callbacks and cross-thread reentry. Separately, the standard public
resource workflow passes two real module initializers plus all 46 existing
cases on Base-27 and Base-28 with one six-DLL Current. Native sources are unchanged.
The MV generator fix and exact prior-MV compatibility pass nine focused checks.
This is conditional correctness; the full DHE goal is still incomplete.

Continue from `dhe-frozen-static-resources.md` on HybridCLR `9e7b601`, IL2CPP
`8a13baf`, package `b51473f`. The preceding goal turn made verified progress:
46 cases on two immutable Bases and independent artifact audits. The full DHE
goal remains active. Unity 2022 Windows is first; Tuanjie follows it.

Build a new immutable old-layout Base from Base-27's captured hotfix/ordinary
DLLs and add a dedicated test entry in the lab bootstrap. Reuse the exact four
46-case Current DLLs; add two separately compiled interpreter-only DLLs with
real C# module initializers. Do not rebuild or patch an archived Player.

The native mixed API must reject an invalid Base MV after preparing the hidden
metadata graph, retain old AOT dispatch and field identity, and keep all new
assemblies invisible with no module code run. Changing a pending peer's bytes
or removing a peer must be rejected. The exact graph must retry with corrected
MV, including permuted input order, and then run the complete Current suite.

Both new module initializers must observe the fully committed graph, call an
evolved value through an ordinary frozen AOT accessor, and permit a worker
thread to call the native batch API again without deadlocking. Every initializer
must run once. A separate process deliberately throws from one initializer:
registration has already committed, so this must not be described as rollback
or safe in-process retry. Preserve the failure state and require a cold restart
for application recovery; evaluate public package behavior separately.

Primary metric is zero semantic differences and complete case/check sequences;
secondary evidence is distinct PIDs and immutable Player/source/resource hashes.
No throughput, tail-latency, ARM64 memory or production-ready claim is made.
Run on committed candidate source, preserve any failing Current, and change
native code only after reproduction. New native changes require real-header
compile/CTest and a new immutable Player before replay. Test-only rollback is
lab `b768c91`; this does not revert the preceding static admission.

## Initial reproduction

Fixture `a94c365` compiles its host and begins Base-29 on unchanged runtime
`9e7b601`. While that build runs, `preflight-base27` compiles the two real C#
module initializers and passes the unchanged 46-case CLR reference. Execution
planning then crashes in `MetaVersionSnapshot.Create`: it enumerates the module
`.cctor` but omits its `<Module>` declaring type, causing a KeyNotFoundException.
Preserve these compiler outputs. This is a tool defect before native loading.

The fix includes global types which actually contain methods or fields while
keeping empty global types omitted, so earlier MV bytes remain unchanged. Verify
real compiler initializer inventory/body fingerprints and byte-for-byte archived
MV compatibility. This enables the new interpreter-only peers' analysis; it is
not evidence for changing an already AOT module initializer. Native explicit
execution-plan handling of TypeDef row 1 remains a separate boundary to verify.

## Source and Base identities

All artifacts below are under `artifacts/dhe-mixed-transaction-20260910` unless
an absolute Base directory is named. Lab `c1eef47` fixes module ownership in MV;
host-02 passes all nine `module-mv-01` checks. The exact original compiler DLL
is preserved, including its module cctor. All four archived Current MVs remain
byte-identical to the preceding static resource. `preflight-base27-fixed`
then passes the 46-case CLR reference and frozen admission, covering 40 ordinary
assemblies and 49,108 executable methods with no missing guards. This is a host
preflight; Base-27 does not have the dedicated native transaction callback.

`artifacts/dhe-mixed-transaction-base-29` is built from Base-27's captured
three hotfix DLLs and ordinary Native DLL, with lab bootstrap source `a94c365`.
Its startup (PID 7204) and generated no-op resource (19128) pass at revision 41.
It records package `b51473f`, unchanged HybridCLR `9e7b601` and IL2CPP `8a13baf`.

- Base ID: `8ae01b043953a8034eac220c646fd9e1d402c4e8fda718c7257f5d0b34e9a678`
- Snapshot: `3035bc41d8ebb35918e895a8f69247271d56b7bb5d1847416d55186160524a6a`
- GameAssembly: `6FC9733065027B45AABA2737C87869DE2BA5688CB5E26179D85FCB7ACE53FA71`
- Build identity: `116E6EA4C031301C3E4F4AD75FD44653201330495BE825355992BF59CFB0A971`
- Build host-01: `170FC01B67717A8DA9BDB79E52D117DE716021D0B87C516A96339298355CECA7`

The Base build uses the retained default tool
`739F4B2FC604259C249D24EB4F653B80BB0930DE261D603D6B69BADB7515208E`.
Its inputs contain no module initializer and its no-op needs no new admission.
Later resource-tool-01 is built from `c1eef47` and has SHA
`46332FE3064B1C78D92E5732D4623D29DCED4C4896EBF63BE2C6CDF821DC63EB`.
Do not conflate these two tool identities.

`inputs-base29` is prepared on clean lab `6c3424c` with host-03
`8EEA6E145A7E9B585C9E273221B849079AB4820645F60CA46E65668A47AC8F23`.
Plan SHA is `A0B804362C914C71A78D00D28D97EB7E2B8E74F58D63426DFA8B4DB09290DF41`.
It authenticates frozen Assembly-CSharp, Native and mscorlib source projections,
three differential assemblies, and three new interpreter peers. The original
four Current DLLs remain identical to `dhe-frozen-static-resources-20260910/current-02`.
The two additional DLLs have actual compiler-generated module cctors and invoke
the Base test callback by reflection.

## Native mixed transaction results

The first run (`run-01-*`) passes every Player check, but the two successful
retry modes fail the external gate: no case lines reach the log when their
process standard handles are not redirected. This failure is retained and was
not waived. Lab `de447b7` gives the Player redirected output/error handles and
captures them, matching the established resource runner. The same Player and
same six Current DLLs/plan then pass complete log comparison. No workload,
expected case sequence, runtime or archived Player changes are involved.

Host-05 SHA is
`6ED28DA15D69459FF4BC0F2B4DBB76183A06AEC5BFF0190D90614106EDA56C39`.

| Artifact | PID | Player checks | Full Current cases | Result |
|---|---|---|---|---|
| `run-02-retry` | 15512 | 56/56 | 46/46 | Registration failure 13, corrected retry 0 |
| `run-02-retry-reversed` | 7520 | 56/56 | 46/46 | Same pending graph retried in reversed order |
| `run-02-initializer-failure` | 18988 | 50/50 | No business entry | Expected module exception after commit |

Both success modes require old AOT execution and old field view after a bad
Base MV, no published interpreter peers, no initializer side effects, and
rejection of changed or missing pending peer DLLs. Corrected retry commits the
exact graph, and both initializers see every new peer and copy the expanded
value through fixed ordinary Native source. Each initializer starts a worker
which calls the native batch API and receives duplicate-registration rejection;
the join succeeds, verifying load-lock release. Module order follows the input
order, and every module runs once. Full 46-case execution follows initialization.

The failure mode requires a TypeInitializationException containing the deliberate
module failure, committed Current metadata and visible new peers, one initializer
execution, rejection of another batch load, and zero business-case entries.
This is a post-commit initialization failure, not a failed metadata registration.
The native test verifies the distinction; public API failure-state reporting
and recovery remain a separate gate.

## Standard public resource workflow on both existing Bases

Lab `6c3424c` adds separately compiled standalone initializers, which need no
special Base callback. `public-current-01` retains all four preceding Current
DLLs and adds two new DLLs. Each initializer checks single public assembly
identity, complete peer visibility, changed fields, ordinary frozen value copy,
the unchanged entry, and a worker's assembly lookup/native call. Neither
initializer executes the 46-case suite itself; it runs once afterward.

- Initializer DLL: `E941DBD217C7176077B1DD40C30E37121193ABCA12E99204BFD845AFE7ED6E64`
- Peer DLL: `346AEADB8EA78A326E15583B0706934F3972044EBFA962BDFD97DEF9BEF11C90`
- Current set: `bd95d7b6e5a2b5f9af1ff240f7a2a999a6ff8e9779dfc43bc06b1e468eee1ca6`
- Shared resource manifest: `1AA52A050074F3D46E75E6D3A838AFCF794F97373B93AF70D03AC5C1C11B446B`

`public-resource-01/result.json` passes all 31 checks on clean lab `6c3424c`,
host-03 and resource-tool-01 above. Base-27 and Base-28 retain their earlier
Player/snapshot identities and both load six Current DLLs, of which three are
new interpreter assemblies. Missing/corrupt versions of each new DLL fail before
entry, restored resources pass, and Base-27's frozen snapshot substitution is
rejected and restored. Base-28 needs no frozen source projection.

Auditor `5a7c756` (host-04) produces `public-audit-01.json`: 163 original/artifact
files rehashed, nine successful full 46-case sequences, and 13 pre-entry
rejections. It independently requires exactly two completed module initializers
before the first business case in every successful/restored run, and no module
side effects in every rejected run. CLR reference only proves the 46 cases;
module behavior is asserted by actual Windows Players.

## Remaining boundaries and rollback

No runtime/package/IL2CPP source changed at this milestone. Their applicable
real-header compile/CTest remains `dhe-new-assembly-resources-20260910/native-04`
on runtime `9e7b601`, with `mergeReady=true` and no surrogate headers. This does
not qualify an AOT module initializer's own evolution: native explicit-plan
validation still excludes TypeDef row 1. Public post-commit module failure
reporting/recovery, ordinary ThreadStatic/RVA, native-only ABI boundaries,
broader Unity behavior and production-equivalent performance/memory remain open.
Tuanjie follows Unity 2022; no new Unity 2021 work is in scope. Android/iOS
execution remains outside the available local environment.

Rollback of the MV generator fix selects lab `a94c365` (or the preceding static
checkpoint `b768c91`); real module initializers will again fail host analysis.
Keep runtime `9e7b601`, IL2CPP `8a13baf` and package `b51473f`, and preserve all
Base, Current and failure evidence. No CAT, formal branch/tag, remote or
Installer-default change was made. C: has about 22.1 GiB free; no cleanup or
stash was needed.
