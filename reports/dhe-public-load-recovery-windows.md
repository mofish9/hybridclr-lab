# Public DHE load outcome and process recovery

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

Base-39/40 are building from the exact earlier old-layout and grown-layout/inline
inputs using this identity. `dhe-public-failure-current-02` adds a separately recorded
worker-reentry variant; keep Current-01 unchanged for exact old/new replay comparison.
All original artifacts and the baseline source worktree are retained. No C: cleanup
was attempted this turn; new large builds are on D:.
