# Public pre-commit retry and Unity component evolution

Unity 2022 Windows correctness conditionally passes on two immutable Bases with
one identical four-DLL Current. Both complete all 46 reference cases, 12 public
prepared-registration rejection/retry checks and 17 real Unity component checks.
Both also pass 11 actual native preparation-exception checks and recover in fresh
processes using the same valid resource. This is a candidate correctness milestone,
not a performance qualification or formal release. Tuanjie follows Unity 2022;
no new Unity 2021 work or Android/iOS execution is claimed.

## Scope and failure semantics

The public
loader must reject a native MV registration error after metadata preparation,
retain old dispatch and an unpublished Current graph, then accept the corrected
same graph without resetting native state. The fixture alters only the
in-memory test copy of an authenticated Base MV after public initialization,
restore it in finally, and use public loading for both attempts. No archived Base,
DLL, snapshot or resource is modified. This fault injection tests runtime rejection
and retry; it is not a claim that resource integrity validation accepts a bad MV.
Native metadata-preparation exceptions are a separate case, tested with malformed
metadata that normal C# compilation rejects. The exception must retain native
phase 1, report RestartRequired without claiming MetadataCommitted, prevent reset
and corrected retry, and preserve zero Current initializer/business effects.

Second, add real hotfix MonoBehaviour types to Base AOT inputs and evolve the
same Current DLLs. Exercise Awake/OnEnable/Start/Update/LateUpdate/OnDisable/
OnDestroy, IEnumerator coroutine scheduling, additional instance fields and their
GC roots, and a newly added component type. Create components only after DHE
selection, matching the required before-business-entry workflow. A method without
changed layout dependencies must retain AOT while changed callbacks use Current. Compare two Bases
with original and evolved component definitions, using one unchanged Current set.
Retain the existing 46-case resource reference and initializer invariants.

Use dedicated C# fixture/compiler/Player code, committed before source-bound
builds and immutable replays. Production runtime/package changes require their
own commits and repeated real-header native tests. Report full checks and exact
source/build identities; do not extrapolate Windows to ARM64 or Unity to Tuanjie.
These are correctness gates, not performance results. All outputs go to new D:
directories and all failure evidence is retained. No CAT, formal branch/tag,
Installer default or remote change is authorized by this work.

## Committed source and immutable evidence

All final builds and replays below bind to lab `69b700c81db03e42b4b1aec7a2b0a0ef5c5738f4`.
The later report-only commit does not relabel these source-bound results.

| Repository | Candidate branch | Commit |
|---|---|---|
| HybridCLR | `research/dhe-value-layout-v8.13.0` | `988a7aa7dcaca19010ec89c94df5658fe39657a9` |
| IL2CPP Unity 2022 | `research/dhe-evolution-unity2022-v8.13.0` | `4d5052e28b6be289f21cc3b79b349675483f2b38` |
| Package | `research/dhe-evolution-v8.13.0` | `2d7355fb191b0ddee957234e54a5e3feb01be16f` |
| Lab | `research/dhe-value-layout-v8.13.0` | `69b700c81db03e42b4b1aec7a2b0a0ef5c5738f4` |

Runtime/package code is unchanged from `dhe-public-load-recovery-windows.md`.
Its 106 managed checks and real Unity 2022 header native compile/CTest retain
their exact identities: `dhe-public-recovery-native-01/DHE-Unity2022/native-gate.json`
has passed/mergeReady true, surrogateExternalHeadersUsed false. No redundant
native or managed rerun is claimed. Runtime contract remains `dhe-runtime-v32`;
MV stays DHEMETA1/schema 1. No runtime or package tags were created.

All following artifact paths are relative to `D:/hybridclr_artifacts/`.
Editor is Unity `2022.3.62f3`, Windows x64, OptimizeSize/FGS.

| Base | Source input | DHE assemblies | Base ID |
|---|---|---|---|
| `dhe-unity-base-45` | `dhe-unity-input-base-03/current`, old component layout | 3 | `0f84bf49d11304c4eeec6ea9b8ca9184f01169f030015e7a1ac0af9905c251a2` |
| `dhe-unity-base-46` | `dhe-unity-input-base-04`, evolved component layout | 4 | `75b7091e04fb3e5e2b385562cbc9d89caf13475fe284339be3e4962f7d9ac4c8` |

Each proof's `result.json` binds the original Player, GameAssembly, build identity,
AOT snapshot, runtime manifest and source commits. Both have 40 ordinary AOT
assemblies captured, successful startup and a separately staged no-op resource.

- Host `dhe-unity-host-08/AotSnapshotTests.dll` SHA-256:
  `C3A70E737F2794253E5A7630B83EFF6CB538E9CF524ADD789E1C2B7451B60B62`.
- Resource compiler `dhe-public-recovery-tool-01/HybridCLR.DheTool.dll` SHA-256:
  `80BC2BF93119CC5EB3CF14FAB5845AA5234239E3916A28CFC43916F651171319`.
- Shared Current set, copied byte-for-byte from `dhe-unity-input-current-03/current`:
  `27d437220f4dc04d2ef90d357406ca3431339b653bef6662ec2aa90b30ffdaa0`.
- Runtime manifest `dhe-public-recovery-runtime-01/DHE-Unity2022/runtime-manifest.json`:
  `AC8C085C47F0A0985755498DEF2EE63BB154FE1FD5C675FC02AC42B3BB2B56F8`.

`dhe-unity-shared-resource-02/result.json` passes all 16 workflow checks. Its
four successful/restored runs each execute the exact 46-case reference sequence,
12 public rejection/retry assertions and 17 Unity assertions. Three damaged-input
runs reject a missing DLL, corrupt DLL or wrong frozen snapshot before business
or Unity entry. These faults affect only disposable staging and are restored.
The Base binaries remain byte-identical to their construction proofs.

`dhe-unity-shared-resource-audit-02.json` independently passes 66 checks and
re-hashes 126 files, including original Base proof identities and Current bytes.
It checks exact probe sequences and ordering in JSON and Player logs. Its hash is
`3AB8C3E5EDCB351C790455825AB3F53D5607FC385917AD877E65C25BA963C495`;
the workflow result hash is
`4A07A5AB05973EA0DF63F639BAEF36A88ED856BB3ABEEA1EDB3970C523F591D8`.

`dhe-unity-public-probes-01/result.json` passes 15 workflow checks and binds 93
files. Six distinct Player processes run the two embedded-Base Unity controls
(14/17 assertions), two real native preparation faults (11 assertions each),
and two fresh-process recoveries (all 46 business cases and 17 Unity assertions
each). The result hash is
`369D1219F39F736C6D54AA557BC27EFA8D97E1D0E27AA283B3EFBD3E6E843624`.

The malformed DLL `dhe-unity-preparation-cycle-01.dll` has SHA-256
`6300F7FBD0AA096BD532FEC39458BECE9B7D323E1112F56136E4EDC09DD29548`.
It adds a self-parenting type to a copy of valid Model. The public fixture changes
only in-memory hash/MV records after normal resource initialization and restores
them in finally. This deliberately bypasses artifact admission to reach the real
native exception; it is not an accepted production resource. The equivalent C#
control `dhe-unity-preparation-cycle-compiler-01.cs` is rejected by the real Unity
compiler with CS0146, and no control DLL is emitted.

Reproduction uses the committed C# host commands `unity-workflow`,
`frozen-resource-workflow ... precommit-unity`, `frozen-resource-audit` and
`unity-public-probes`; arguments and source identities are recorded above and
in the result manifests. Always choose new output directories. No `.ps1` workflow
or edits to archived generated C++, Players or Base assets are needed.

## Preserved fixture failures and diagnostic runs

Base attempts 41 and 42 failed during Unity Linker preparation, before any Player
was accepted. Their real coroutine's IteratorStateMachineAttribute referred to the
temporary compiler assembly. Lab `22acf6b` rebinds embedded attribute type references
before merging. The old input fails the ownership verifier; both input-base-03 and
input-current-03 pass. Failed directories remain unchanged on D:.

Base-43/44 subsequently pass construction, startup and no-op resource loading at
lab `e45eed9` (host-05), with the same runtime/package identities above. Shared
resource attempt `dhe-unity-shared-resource-01` on Base-43 passes all 46 business
cases, all 12 public prepared-MV rejection/retry assertions, then the first ten
Unity checks. It fails the fixture assertion `unchanged-reader-not-selected`.
This assertion incorrectly ignores dependency changes: `ReadUnchanged` reads an
instance field on the type that gains fields, and `StableMethodDependencyShape`
includes the owner layout version. It must be interpreted on the old Base, while
the same-layout new Base may use its AOT version. The corrected fixture measures
both modes and separately measures `Factory.UnchangedRevision`, whose dependencies
are unchanged, requiring AOT entries and zero interpreter entries. It retains
the real coroutine and added-field/GC/lifecycle assertions. No runtime policy is
weakened or changed. The failed immutable Players/resource/results are preserved.

The independent Base-43 preparation probe passes all 11 assertions with the real
native `BadImageFormatException: type parent hierarchy contains a cycle` at phase
1, no publication/initializer effects, rejected corrected retry and rejected reset.
The normal Unity 2022 compiler separately rejects the equivalent self-parent C#
control with CS0146 (`dhe-unity-preparation-cycle-compiler-01.cs`). Base-44 later
also passes its 11 preparation assertions and fresh-process recovery with the
original 16-assertion Unity fixture. These are intermediate diagnostics, superseded
by the complete Base-45/46 verification above.

The standalone `dhe-unity-precommit-resource-01` and its audit preserve additional
public retry evidence on Base-43/44: 16 workflow checks, 55 audit checks, 126 files,
four successes/restorations and three pre-entry rejections. That diagnostic runner
used host-07 built at `e552fd6` while the worktree was `69b700c`; its recorded host
hash must be retained separately. Final Base-45/46 evidence uses host-08 built at
the exact recorded `69b700c` source. Do not relabel earlier Players or host binaries.

## Remaining qualification and rollback

This closes the selected public loading failure matrix and real Unity component
suite. It does not cover serialized scene/Prefab evolution, components created
before DHE selection, every Unity/native API boundary, or performance and memory.
Ordinary AOT code remains immutable: the remaining ThreadStatic/RVA and native ABI
questions concern its dependencies on evolved hotfix layouts, not permission to
hot-update ordinary AOT source. Complete those gates and production-equivalent
Windows performance/memory before porting the candidate to Tuanjie. Tuanjie must
then have its own real-header/native/Player evidence; Unity results cannot certify
its ABI. Android validation remains with the user, and iOS is untested.

Rollback selects an archived accepted Current for the matching immutable Base on
the next process start. Reset cannot undo native preparation or publication. This
round changes only lab verification code and reports; the previous runtime/package
commits and all failed artifacts remain intact. Candidate worktrees are committed
and clean. No formal branches/tags, remotes, Installer defaults or CAT files were
changed. New large artifacts reside on D:; C: still has about 11 GiB free, so no
cleanup was needed in this round. The overall DHE goal remains incomplete.
