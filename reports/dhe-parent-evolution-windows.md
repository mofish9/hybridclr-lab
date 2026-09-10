# Existing hotfix parent evolution: Unity 2022 Windows

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

## Next correction, pending Player proof

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

Remaining gates include original and extended Current replays on new old/grown
Players, cached receivers, shared-resource identity and recovery; parent removal
and replacement (currently only planning evidence), cross-assembly/generic
parents, Scene/Prefab and pre-existing-object semantics, concurrent publication,
production-equivalent throughput/tails and memory. Windows never establishes
ARM64 correctness. Full DHE and project handoff are not complete.

Source rollback uses a matching previous runtime/package/lock combination.
Resource rollback selects a compatible archived resource or Base no-op and
restarts. Never replace an embedded runtime or rewrite Base identity in place.
Inactive archive projects 28 and 29 were losslessly compressed (about 1.23 GiB
saved each); no input/Player/evidence deletion or stash change occurred. Base-91
project compression is ongoing separately and does not edit Player bytes.
