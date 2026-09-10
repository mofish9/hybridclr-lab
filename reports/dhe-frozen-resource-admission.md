# Frozen AOT admission through the standard resource workflow

Target Unity 2022 Windows first. The update runs at startup before hotfix business
entry. Original hotfix assemblies may evolve; ordinary AOT assemblies may only
use their captured Base IL to adapt those dependencies. Unity 2021 is out of the
current user scope; Tuanjie follows Windows qualification.

Admission requires a Base-authenticated complete ordinary guard inventory and
exact, freshly compiled frozen selections. Discharge individual layout/method
obligations only after matching their source, token and stable identity. Keep
native-only ABI, excluded identity, ordinary static storage, ThreadStatic and
RVA obligations closed until separately implemented and tested.

The resource includes the original analysis manifest at
`payload/frozen-aot/<baseId>/snapshot.json`. The loader derives its path and
authenticates the bytes against the analysis hash embedded in the Player. Each
frozen source must be an ordinary assembly in that manifest, with the exact Base
DLL hash; its MV must bind the same DLL. Manifest/validation/plan agreement alone
does not authenticate a source. A new capability prevents older Bases from
accepting resources which rely on this loader guarantee. MV schema stays 1.

Required tests: valid standard resource loading, rejected missing/substituted
snapshots, a consistently substituted DLL/MV/record, wrong source role, source
hash mismatch inside the MV, missing native coverage, and unresolved ABI cases.
Reject before any native registration or business entry. Preserve retry behavior.
Then build a new immutable Windows Base and load resources through the public
resource API. Existing proof-15 remains historical native-transaction evidence.

Correctness and evidence identity are the primary gates. No performance claim
is made here; P50/P95/P99, startup cost and memory qualification remain separate
work with production-equivalent sources. Package loader/source binding and lab
admission are independently revertible candidate commits. No CAT, formal branch,
tag, remote or Installer-default changes belong to this step.

## Verified Windows resource checkpoint (2026-09-10)

The standard resource route now passes against two immutable Players. Source
combination: HybridCLR `b3e72d815f935fad25e3f255bf9a611264901622`, Unity 2022
IL2CPP `8a13baf1ec45068fbb9535beea03425b717f501b`, package
`187af4f182f29daa60bdcb9a72f3131d52d710ca`. Resource tool source is lab
`a5ab3ad`; resource fixture source is lab `8c234bf`. Later `7181f97` changes
help text/comments only and is not the identity of the tested tool binary.

Evidence root: `artifacts/dhe-frozen-resource-admission-20260910`.

* `managed-01.json`: 81 host checks pass on lab `e81e71c` and package
  `187af4f`; native calls are recorded, not executed. Includes missing/other-Base
  snapshot, snapshot role rewrite, MV source hash mismatch, consistently replaced
  source DLL/MV/records, required capability and retry checks.
* `admission-01/result.json`: 9 policy checks pass on lab `2949505`, using
  proof-15's immutable snapshot/native evidence. The exact eight concrete
  obligations are discharged only with complete method coverage. Missing an
  unaffected guard, missing physical storage, unsupported native entries and
  unresolved storage/native obligations remain rejected. Host SHA-256:
  `CB0B80BBF60DA60623990EA5A168045FC868DFEA071D914348728F57D9CB2711`.
* `resource-02-multibase/result.json`: 8 workflow checks pass. Resource generation
  automatically creates the frozen selections; no imported diagnostic plan is
  used. Both Players use normal staging, `InitializeFromResourceUpdate`, and
  `LoadAssemblyImages`, followed by the actual hotfix business entry.

| Immutable Base | Original revision/layout | Ordinary guard coverage | Current resource result |
| --- | --- | --- | --- |
| `dhe-frozen-entry-proof-16` | 41; Payload lacks Extra/Reference | 40 assemblies / 49,089 executable methods / zero missing | 73; two frozen sources, eight exact obligations discharged |
| `dhe-frozen-resource-base-17` | 59; Payload already has Extra/Reference | 40 assemblies / 49,061 executable methods / zero missing | 73; zero frozen sources needed |

Base IDs respectively:
`252600b1186b4ab86f62998084b21d38538dd8659ae1b9c1ab1cb2d9ac35a5c8`,
`b1cfafb7ca5d06240b01c214035e1294205846c94c8179bfce3fd03a95b127e3`.
The common Current DLL set SHA-256 is
`944155ae19b2ac11bd2bcd92fe0cbf1ea06ec79b15fcd04ce2f3ca45c74dd444`;
the shared resource manifest SHA is
`B73A59EBF191C63A401CF64DE1F33873BB6287C2FE6F876C553C996C9D049587`.
There are no per-Base Current DLL variants. The single resource directory carries
Base-specific frozen sources and metadata, selected by the embedded Base ID.
The original hotfix Base MVs remain embedded in each Player.

The Current business entry calls new code which asserts Count=17,
Extra=90000000001 and reference identity after Echo, boxed copy and inline-owner
copy through the ordinary Native assembly. It also requires the unchanged
ordinary sentinel to return 137. Only then does it return revision 73. The exact
same Current code passes the CLR reference. Both Players retain sentinel 5 and
require the original hotfix UnchangedRevision method to remain unchanged.
These grouped assertions are not additional independent workflow case counts.

Player PIDs: first Base 1380, second Base 6980. Snapshot substitution in the first
Base is rejected (PID 20088) with loadedAssemblies=0 and revision=0; restoring
the exact original snapshot passes (PID 5080). Native binaries remain unchanged:
GameAssembly hashes `CDD599ECB5476CCDF183C7F6755703CB1D309FA2F66B6A3106A9CDCEFA0A8D37`
and `C1C7B9EADBE8CB7F8AC2A60D8B0E80B5BF52E8389B1A464BCEE2E3699EEF7736`.
The generic Unity launcher executable hash is identical for both builds; the
different GameAssembly, snapshots and embedded Base IDs establish distinct Bases.

Proof-16 also passes its 35-check native-source core replay (PID 13072). It was
built from fixture lab `5bafde8`; proof-17 from `70afe58` and passes Base startup
and a no-op resource load. Broader proof-15 collection/nullable/generic results
remain evidence of their earlier Player/package identity and are not relabelled.

The native source tree remains
`2442F1F93771123F79D928C31587299901991C3D4E48E862A2593BE0747EAEBC`, exactly matching
the previously passing real-header native compile/CTest in
`dhe-complete-ordinary-guards-20260910/native-03/DHE-Unity2022`. No new native
source change or fresh native CTest run is claimed for this package-only step.

## Review, failures and remaining gates

`binding-01` timed out in its old five-minute subprocess limit and produced no
passing result. Its artifacts are preserved. The fixture timeout is now twenty
minutes; the full legacy binding fixture has not been rerun on that change.
`resource-01` generated/staged/loaded its resource and completed its field-copy
probe, but failed afterwards when an old optional demo callback looked up a
method absent from this fixture. That attempt is not a passing workflow result.
The corrected fixture executes its assertions through Factory.GetRevision;
proof-16 was not rebuilt or patched to make `resource-02-multibase` pass.

Admission review keeps ordinary AOT static-value storage, ThreadStatic/RVA and
concrete native-only ABI obligations closed. Original ordinary source DLLs remain
Base-authenticated; adaptation does not grant them independent hot-update rights.
The guard coverage option remains a research Base-build option, and the new
snapshot-binding capability prevents older Bases from silently accepting these
resources. This is a conditional Windows correctness checkpoint, not a claim
that every possible hotfix edit is supported.

Remaining work: broader standard-resource generics/collections, reflection and
initialization/cyclic/concurrent cases; newly introduced interpreter-only
assembly references with frozen sources; unresolved storage/ABI capabilities;
production-equivalent startup/performance/memory measurements. Tuanjie follows
Windows qualification; Android/iOS remain untested. No performance, mobile or
production-release claim is made. CAT, official maintenance lines, tags, remotes
and Installer defaults are unchanged.

Rollback: retain immutable Players/evidence, omit these candidate package/lab
commits, and restore the previous research locks (lab `f24c206`, package `3c9558c`).
Never patch an existing Player or move a tag. Use original/no-op resources only
when the Base capability set cannot admit the evolved resource. All four source
worktrees were clean at this checkpoint; no stash or disk cleanup was needed.
