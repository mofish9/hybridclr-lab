# Public DHE load outcome and process recovery

Current result: source-bound Windows Players pass post-commit failure state,
rejected reset/reinitialize/retry, real worker reentry, and fresh-process recovery.
One unchanged business Current also passes across two v31 and two v32 Bases.
This is a conditional Windows correctness milestone, not full DHE qualification.

Continue after the v31 three-Base module/literal/inline checkpoint. Target Unity
2022 Windows first, then Tuanjie; no new Unity 2021 work. Starting identities:
HybridCLR `8417ea0`, IL2CPP `4d5052e`, package `6d59a27`, lab `b1e3da8`.

The public loader catches a module exception after native graph publication and
reports registration failure without completing its managed bookkeeping. Reset
clears managed state but cannot undo native registration or initializer effects.
This is a release blocker. Reproduce using a real compiler-generated module cctor
whose initializer records its version and run count, then throws. Use the same
Current on Base-36 (mixed/frozen sources) and Base-37 (existing differential only).
Preserve all failed resources, Players and original source identities.

The new tracked native batch entry reports a per-call phase through an out int:
not started, preparing metadata, metadata prepared, committed, initialized.
Write committed immediately after successful atomic graph registration, before
running user code; exceptions must not erase it. The out field belongs to the
managed load attempt and is inspected only when the synchronous call returns or
throws. It is not a new shared native cache or a substitute for the existing
metadata publication barriers. Legacy native entry points retain their behavior.

Public loading serializes attempts with an atomic busy flag, releasing it before
return. No managed monitor stays locked while native code runs initializers.
Callbacks and other threads receive an explicit in-progress result. Expose stable
load state, confirmed metadata commit and the original error. A rejected prepared
graph can retry the same validated bytes. Exceptions during metadata preparation
or after commit require a new process. Reset/reinitialize must never erase the
fact that native metadata has been touched. A successful load is not reloadable
with another Current graph in the same process.

Correctness gates: preserve the existing resource argument-selection tests;
exercise public pre-validation rejection/corrected retry, native MV rejection/
corrected retry, post-commit module failure, rejected reentry/reset/reinitialize,
stable failure details, and fresh-process recovery to a valid resource. Test mixed
new-assembly/frozen sources and existing-only resources on immutable Windows
Players. Keep all 46 reference cases and unaffected AOT dispatch assertions.
Run real-header native compile/CTest and independently bind artifacts to commits.
There is no performance claim. The new API needs a matching Base runtime/package
capability; an old shipped Player cannot acquire it through Current DLLs.

Rollback selects the preceding source combination when building a new Base and
retains the v31 module checkpoint. A loaded process is recovered by restarting and
selecting a known accepted resource, never by resetting initializer side effects.
No formal branches/tags, Installer defaults, CAT files or remotes are in scope.

## Reproductions and committed candidate

All new artifacts are under `D:/hybridclr_artifacts`. `dhe-public-failure-current-01`
was compiled by lab `7aad62a` from the preceding 46-case Current with
`MODULE_CURRENT,MODULE_FAILURE`. Its DLL set SHA is
`57e09e84a5b0883cf2303dbb7c51395563cb78825af2ff4867e77a7e24733887`.
`dhe-public-failure-legacy-01` reproduces incorrect public classification on the
unchanged Base-36 (PID 12348) and Base-37 (PID 15568). Both enter initializer
version 202 once, return DHE_MV_REGISTRATION_FAILED, report zero loaded Current
assemblies and never enter business cases. This is an expected failing behavior,
not recovery qualification. The resource manifest SHA is
`27052511137DA15C4A8652213C73BB588A685688F1B56C49F5D31D88724878DC`.
Runner lab `370c7af`, host-02 SHA
`EB0E511EB0510F8CE33006BB05D536D11A0194788FF4CD53874D6636D141A641`, uses prior
tool-07 SHA `75597987BAE9B0F11E34D0DBC5C0C06A60EDC810F5A3A297C884F1E1894D12B1`.

Lab `e03bc5b` adds 25 public state/retry/reentry checks. The same test source built
against detached package `6d59a27` at `package-before-public-recovery` preserves
all prior 81 passes and fails 24 new checks in `dhe-public-recovery-managed-before.json`.
The one additional pass only observes the published set; it does not imply correct
failure handling. This is a host adapter which records/simulates native calls.
Its explicit test-only process reset never exists in the package or Player.

The committed implementation is HybridCLR `988a7aa7dcaca19010ec89c94df5658fe39657a9`,
package `2d7355fb191b0ddee957234e54a5e3feb01be16f`, unchanged IL2CPP Unity 2022
`4d5052e28b6be289f21cc3b79b349675483f2b38`. Runtime contract is `dhe-runtime-v32`,
capability `tracked-native-load-phase-v1`; MV remains DHEMETA1/schema 1.
Package guard generation requires the corresponding native feature macro.
Canonical HybridCLR tree:
`1FC75CA88DB05749C8AB5DA58FE2583A8949B46C98246B1845B7134602BE047A`; package tree:
`3AC43D56FB30882F57A4A15269F995568119EE2321751F5615D7EBDF9D098F4D`.

`dhe-public-recovery-managed-03.json` passes all 106 host checks on lab `5db0bc0`,
host SHA `8AE7DB71627585479B4FAA16AA594DC061153544D9675BA03D30728192A71F74`.
It exercises metadata-preparation exception, post-commit exception, retained error
and published set, reset/configuration/retry rejection, prepared-MV corrected retry,
and a worker thread reentering the package during native initialization. This is
managed behavior evidence; actual out-argument preservation through native exception
unwinding and actual mixed/existing-only resource recovery still need Player evidence.

Lab `01921b5` locks the runtime/package and builds tool-01, SHA
`80BC2BF93119CC5EB3CF14FAB5845AA5234239E3916A28CFC43916F651171319` and host-03, SHA
`4894D958929AAF1412356514DDA75F6AE97ABCFAA8435DABF7286CC6802A0A8F`.
`dhe-public-recovery-runtime-01/DHE-Unity2022` has runtime tree
`5BF75DB2EBA82D69E04CE5B332658A74008C8147F2E1CC34EAD0AB038D943213`, manifest
`AC8C085C47F0A0985755498DEF2EE63BB154FE1FD5C675FC02AC42B3BB2B56F8`.
`dhe-public-recovery-native-01` passes real Unity 2022 headers, compile/CTest,
`mergeReady=true`, `surrogateExternalHeadersUsed=false`. The compilation includes
RuntimeApi.cpp; the new phase transitions require the separate Player gate.

Base-39/40 build from the exact earlier old-layout and grown-layout/inline
inputs using this identity. `dhe-public-failure-current-02` adds a separately recorded
worker-reentry variant; keep Current-01 unchanged for exact old/new replay comparison.
All original artifacts and the baseline source worktree are retained. No C: cleanup
was attempted this turn; new large builds are on D:.

## Real Player outcome and recovery evidence

Both immutable Players pass startup and generated no-op resources on lab
`01921b56352685e82683904dbfd39bea365e8d3b`, host-03, tool-01 and runtime-01 above.

| Base under `D:/hybridclr_artifacts` | Startup / no-op PIDs | Base ID | GameAssembly SHA-256 |
| --- | --- | --- | --- |
| `dhe-public-recovery-base-39` | 23220 / 10828 | `87db6b360a9a39b43b3c140cf275295a7759a7bd0e096dd15f1303bace71204b` | `AE55E175A94B1ACE2ED88679ED1FB7DCE57033FDD8AA3FAF0795AB8CBBE08641` |
| `dhe-public-recovery-base-40` | 12644 / 14884 | `af3c0a8fd555d7a8b25dd09e28b4bc0a9b3f56a7c00fca134d7bdde1ca7fd208` | `B5B785D2EBFF4D86A8BE195D8A476BF8F3B81E9225DF3FD9FC41C7DAC971192F` |

Snapshots: Base-39 `a40014ec57d2c27adc854dbf4a87fcff5235af3008b1bced4fe9b6e051d4c67a`,
Base-40 `317b2f5190104d6217f337e87a4317df8258450cfcccf6f7430836adb598bb5a`.
The source/input difference remains deliberate: Base-39 exercises new interpreter
assemblies and frozen ordinary dependencies; Base-40 exercises existing-only DHE.

Resource runs use clean lab `a841ddd34ff6570e57392d7bfa51613bbd424664`, tool-01 and
auditor/runner host-04 built at `78d00fc`, SHA
`E0ABCB5A09F7B9B6ACB85AADD6D71E868006D1F02D2C6BF948234DBC732158E8`.

| Workflow output | Base-39 / 40 PIDs | Player checks | Workflow checks | Independent audit |
| --- | --- | --- | --- | --- |
| `dhe-public-failure-exact-01` | 18788 / 14944 | 14 / 15 | 8/8 | 119 checks, 108 rehashed files |
| `dhe-public-failure-reentry-01` | 18492 / 21008 | 14 / 15 | 8/8 | 121 checks, 108 rehashed files |

The exact variant retains Current-01's DLL set from the old-Player reproduction.
Its new resource manifest SHA is
`06149B06E288236B77A038B8BAF53885121D59EB4865CA15BBBDE817F6D98EFF`.
Reentry variant Current set:
`6f173ebe469fc6eedff01f2b9192914ecc1e359a7c0e8fb551e546f747b70669`; resource manifest:
`E2DE0AD22B03F612744BFC966AD337B81BEE98F818A2260BAAD2BB25958657D4`.
The sibling `dhe-public-failure-<variant>-audit-01.json` independently checks
original Base/snapshot/Current identities and complete failure assertion traces.

Every failure Player confirms all planned assemblies are visible natively and in
managed bookkeeping, and the changed Factory method is registered. The native out
phase therefore survives actual exception unwinding. First load returns
DHE_INITIALIZATION_FAILED with the deliberate inner exception retained;
MetadataCommitted and RestartRequired are true. Reset and reinitialization are
rejected. Current, legacy batch, single-image and interpreter-image retry entry
points all reject with DHE_RESTART_REQUIRED and the same error. The initializer
count remains one, the selected plan remains intact, and no business case starts.
Base-40 also checks its ordinary initializer remains at one. The reentry resource
starts a real thread inside module initialization; loading, reset and configuration
return promptly with rejection and emit `DHE public reentry pass` exactly once.

After the failed processes exit, `dhe-public-recovery-valid-01` loads the preserved
normal changed Current in fresh processes on the same two Players (initial PIDs
17692 / 24188). It passes 16/16 workflow checks. Its 49-check independent audit
rehashes 126 files and verifies four complete 46-case successful/restored executions
and three pre-entry corruption/missing/snapshot rejections, with zero differential.
Manifest SHA: `3F24F34E73C4E5949EAFACCF770AE3D375E79CADD3CCCC5D8D30983B1CFF8CAE`.
No Player binary or failed payload was modified to recover.

`dhe-public-recovery-inline-01` additionally passes on Base-40 (PID 12572): all 46
cases, 74 literal assertions and `DHE inline hotfix pass: 18:2:1`. Its 20-check
audit rehashes 67 files. The unchanged caller stays AOT and only its changed callee
interprets. These are current-identity regression results, not reused v31 numbers.

## One business Current across old and new runtime Bases

`dhe-public-recovery-mixed-runtime-01` serves exactly the same changed Current DLL
set, SHA `ed1d0b59e136874634c7f3ead29e93319cfb1d8df90894b563c7965808f89ce6`, to
Base-36/37 (v31) and Base-39/40 (v32). Initial PIDs are 22820 / 3188 / 23280 / 14268.
All 29 workflow checks pass. Independent audit
`dhe-public-recovery-mixed-runtime-audit-01.json` passes 91 checks, rehashes 235
files, and verifies eight complete 46-case successful/restored executions and six
pre-entry rejections. Resource manifest SHA:
`D826A79D59D042C0D9D9A7BE4FFB510D40E0E91BEF669C5F5CAD6B36A079E6A7`.
The old Players retain their v31 behavior; this does not retrofit the new failure
state API into them. The common business resource does not depend on that new API.

## Remaining gates and API use

Applications call LoadCurrentAssemblyImages before business entry. On failure,
inspect its code/error and RestartRequired. Preserve diagnostics, prevent entry,
and let the application's existing resource/version system choose a known accepted
resource on the next process start. Reset is only for plans which have not touched
native metadata. The package does not automatically download or select a fallback.
The existing IDheRuntimeAssetProvider boundary continues to own resource access.

Real post-commit exceptions, worker reentry and fresh-process recovery now pass on
both native loading paths. Native preparation exceptions and corrected prepared-MV
retry through the new public phase API are verified in the managed host; actual
native retry evidence remains the preceding low-level mixed-transaction Player at
its original runtime identity. Add a Player probe for those public pre-commit paths
before claiming the entire recovery matrix complete. Ordinary ThreadStatic/RVA,
native-only ABI boundaries, broader Unity behavior, production-equivalent performance
and memory, and Tuanjie remain open. Android/iOS have not been run locally.

All changes are committed on candidate worktrees. No formal branch/tag, remote,
Installer-default or CAT change was made. Keep the detached preceding package
worktree and all failure/build evidence. C: has about 11 GiB free; new artifacts
remain on D:. This milestone does not complete the overall DHE goal.
