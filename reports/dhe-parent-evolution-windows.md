# Existing hotfix parent evolution: Unity 2022 Windows

## Latest verified checkpoint

Parent insertion now passes both the original sixteen-check Current and the
extended twenty-eight-check Current on immutable old-layout Base-94, grown-
layout Base-93 and new-type-control Base-81. Every explicit replay also passes
18 framework, 25 virtual-signature and 46 business checks. This completes these
insertion/member scenarios; removal/replacement and the broader goal below
remain open. Historical failures in the following sections stay preserved.

| Component | Qualified source |
| --- | --- |
| HybridCLR | 155a47d8b2c8de3222b3983f30b4dcbb67e121f5 |
| Unity 2022 IL2CPP | 819f74c08e466a0d2a8fe5b1afaad5b1d784e482 |
| Package | 125e6086b1c857689900d8a770094b795f45fdb5 |
| Lab, native02 / Base93 / policy / managed loader | e715904ba51e31d7cd0494e04032638bd7c5a6ba |
| Lab, Base94 / final shared resources and probes | e62f18c0c52d7ba8f68cbd045b977b6a9b04e68f |

native-02 passes real-header compile/CTest and FGS, including property/event
icall compilation and all physical-frame/old-receiver negative controls. Its
report SHA is 00A4800547661D6FABA24513E3278762A9AFF7B57682E54DE96A1085CACCEE6C;
runtime02 manifest SHA is AD660ADAF4120568FD955F81D459FBF848074EB20F25FD4DC67F2547BEC52E98.
Both new Bases pass build/startup/generated no-op and their explicit 25-check
no-op runs, with six unchanged implementations, AOT entries >0 and DHE
interpreter entries 0. No performance benefit is inferred from routing counts.

| Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 93, grown | 903f1d2e2715411af0c7a5180eb98c830c7bad8728d83b10f0dd6db80af19ee1 | 3E705802B1DB2197D302D9E2C6B6222FFCE68414DD81001BF857CBC4A1AEC4C4 |
| 94, old | 966ab53e595ad702e9154f21404e47103f94b5019abeb8b8fae2ab959c8b78fd | E8BEB14C2268AF5E24B1F2903347A40C02BF37CBE6FCE07F5112B7A37786F1F2 |

shared-three-base-07 uses the original current-01/current bytes; probes
probe-base94-08, probe-base93-08 and probe-base81-08 pass 16/18/25/46. Its
Current-set hash is 77050349b6acd1fe1f7735ed44d11412825657ef7f119adbda37b40a06217f2e.
shared-three-base-06 uses unchanged extended current-02/current bytes; probes
probe-base94-07, probe-base93-07 and probe-base81-07 pass 28/18/25/46. Its
Current-set hash is 80cc5c6e51265fbd72706ea152c052c38f5a72c85102cbef92151b3909c37d72.

probe-base94-cached-07 passes twelve old-object/cache assertions alongside all
28/18/25/46 checks. Member handles resolve successfully before checking that
inherited fields, methods, property get/set and event subscription reject the
old physical receiver; old/new data remains unchanged after rejection. This is
not object migration. Log SHA:
0769F2D7D99975ED4F07F1486281DA9E6012C46B9DB5FA4AA4671615D39C144C.

Both final resource audits rehash 167 files and verify Current/Player identities.
Audit06 SHA: 3D0034BD9D16F831FC3456FE377EA56F0094D3B86D539CD71C4F0287314B5524.
Audit07 SHA: 035F0B978CC52D47F296347F00D595D714AB6532237FE30C0AB79DFE6D0399CC.
shared-public-probes-06 passes 22 three-Base deliberate native-preparation
failure/recovery checks and binds 138 files. Result SHA:
9779342DC3CBA643DC9DD4793D05C483445A73F2EBF25861FB7088A0AE770AC2.
policy-03 passes 44 parent/capability checks with the extended Current and
unchanged archived MV; managed-plan-02 passes all 114 package-loader checks
with package125e608. Native calls in the latter remain recorded stubs; real
Player evidence above supplies actual runtime execution.

All four candidate worktrees are clean after commits; no formal publication or
project handoff is claimed. Archive projects28/29 and Base91/92/93/94 projects
were losslessly compressed, with all Players, Current DLLs and failures retained.

The full DHE objective remains open. This checkpoint preserves successful and
failed immutable Player evidence, not a release qualification. No CAT changes,
formal branches/tags/pushes, Installer defaults, Android or Tuanjie qualification.

## First native correction

Sources: HybridCLR 379068ce400e9d81da1e4eb1e41364f1e13b9540,
Unity 2022 IL2CPP 6b74b106997215480cc4f4b037a5488f75983cc7, package
3fcab7ed8367b9def98feb393f256de3cfb6add7. Both Base builds use lab 6c2dd61;
extended fixture/replays use 072fc5c. All paths below are relative to
C:/hybridclr_optimize/artifacts/dhe-parent-evolution.

native-frame-control-01 at lab 3d54300 reproduces fifteen identical-physical-frame
rejections against unchanged fdf1299. native-01 passes real Unity 2022.3.62f3
headers, compile/CTest and FGS checks, mergeReady=true and
surrogateExternalHeadersUsed=false. This is not whole-workflow merge readiness.
Native manifest SHA-256:
69DEAE99E10AE2312ED48DCB8D18122B1792ED3EE4489C1F28676078E68F8682.

Base-91 (old layout) and Base-92 (grown layout) both pass construction, startup
and generated no-op resources. noop-base91-01/noop-base92-01 each pass 25 checks,
six unchanged implementations, positive AOT entries and zero DHE interpreter
entries. Their GameAssembly SHA-256 values are respectively
1020E588FD3D8FA4F7E68319D1BBF6739F7C0E363C9B85A87B86C1C0842EC516 and
5FD9245A3972FEBFED7D30025FD70510250E332336A1EBEB691D8B077E3BA97E.

The exact original current-01/current from the earlier failed Base-89/90 runs is
preserved. shared-two-base-02 and shared-three-base-02 consume those same bytes;
all ordinary resource runs pass 46 business checks. Explicit probes supply the
parent/callback evidence, which must not be inferred from resource success:

- probe-base91-02/03 and probe-base81-02/03 pass 16 parent, 18 framework,
  25 virtual-signature and 46 business checks. Base-81 is a new-type control.
- probe-base91-cached-02 passes six old-receiver checks and those complete log
  sequences; incompatible old receivers remain rejected and data preserved.
- probe-base92-03 passes ten virtual-signature checks but fails fifteen AOT
  virtual/interface invocations before reaching the parent/framework suites.
  The previous top-level array-return guard no longer blocks Cases.Run; native
  instance entries still cannot select a physically compatible Current receiver.
- shared-two-base-audit-02.json verifies 117 files; the three-Base audit verifies
  167. shared-public-probes-02 passes sixteen recovery checks, binding 93 files.
  Recovery is in fresh processes, not in-process Current replacement.

The extended current-02/current is compiled by the actual Unity compiler from
the preceding framework Current. reference-02 passes 28 parent/member plus
18 framework, 25 virtual-signature and 46 business checks under CLR.
shared-three-base-03 ordinary resource/business runs pass. Its explicit probes
show Base-91 passing 22/28 parent checks, with six inherited property/event handle
failures; Base-92 again fails fifteen earlier virtual invocations; Base-81 passes
all 28/18/25/46. Preserve both complete Current sets and all failures.

policy-02 passes forty parent planning/admission checks, virtual-policy-01 passes
45 and declaration-policy-02 passes 32. managed-plan-01 passes 114 loader checks
at 6c2dd61 with the same package; its native calls are recorded, not executed.
old-capability-rejection-01 verifies actual Bases 89/90 are both rejected for
the two missing parent/frame capabilities before any Player runs. Inventories
are never rewritten retroactively.

## Second correction and preserved control

native-receiver-control-01 at lab a2e47d0 reproduces twenty positive Current-
receiver/descendant failures against 379068c; old, unrelated, null, unresolved
and mismatched-layout receivers/frames retain their negative expectations.

The next source combination is HybridCLR 155a47d, Unity 2022 IL2CPP 819f74c,
package 125e608 and matching lab locks. Native reference invocation compares
physical signatures; generated reference-instance entry guards pass the actual
receiver through that proof before interpreter dispatch. Value-type receivers
and incompatible arguments/buffers retain their guards. Field/property/event
handles share a published-ancestry check while retaining logical reflected
identity and separate physical object validation. New capabilities distinguish
these corrections; archived Bases do not acquire them by updating the tool.
These source changes do not retroactively fix Players 91/92.

The replays, cached receivers, shared-resource identity and recovery for this
correction are qualified in the latest checkpoint above. Remaining gates include
parent removal and replacement (currently only planning evidence), cross-assembly/generic
parents, Scene/Prefab and pre-existing-object semantics, concurrent publication,
production-equivalent throughput/tails and memory. Windows never establishes
ARM64 correctness. Full DHE and project handoff are not complete.

Source rollback uses a matching previous runtime/package/lock combination.
Resource rollback selects a compatible archived resource or Base no-op and
restarts. Never replace an embedded runtime or rewrite Base identity in place.
Inactive archive projects 28 and 29 were losslessly compressed (about 1.23 GiB
saved each); no input/Player/evidence deletion or stash change occurred. Base-91
project compression subsequently completed without editing Player bytes.
