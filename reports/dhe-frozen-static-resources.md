# Ordinary AOT static storage of evolved hotfix values

Current checkpoint: the same 46-case Current passes on two immutable Unity 2022
Windows Bases with different Payload layouts. All 19 shared-resource checks
pass, including unchanged ordinary static readers retaining AOT dispatch.
Independent audit `2a0a645` rehashes 131 files and checks all five successful
primary/restored executions and five rejected executions. This is conditional
correctness evidence; full DHE and production qualification remain incomplete.

Continue after the three-Base, 31-case checkpoint in
`dhe-new-assembly-resources.md`. The active runtime/package/IL2CPP candidates are
`9e7b601` / `b51473f` / `8a13baf`. Unity 2022 Windows is the first target; Tuanjie
follows after this runtime path stabilizes. No Unity 2021, CAT or formal release
changes are in scope. The previous goal turn made verified implementation and
Player progress; the full DHE goal remains active.

Build a new immutable old-layout Base with additional ordinary Native static
owners compiled against its old Payload. Capture that exact Native DLL. Later
Current adds static-storage probes to the preceding 31-case hotfix resource,
using the captured ordinary Native bytes and the evolved Payload. No new
ordinary DLL may be shipped as hotfix code. Preserve the initial resource-policy
rejection before changing admission or runtime behavior.

Cover regular and readonly static initialization, independent copies, byref
access, adjacent unchanged fields, generic fixed-value and generic-argument
owners, reflection get/set and logical identity, GC reference retention, whole
value clearing, concurrent cctor, cached cctor failure, and a holder whose
instance and static value storage both expand. Reference and Player must execute
the entire suite in order. The first Base keeps its original DLL/Player/snapshot
hashes. After correctness, extend to another Base with a different value layout
and the same Current bytes. No throughput or memory benefit is claimed by these
correctness probes.

The runtime already supports Current static-value storage for hotfix owners.
Frozen-source planning detects ordinary static fields and selects their cctors,
but admission still rejects the ordinary static-field obligation. Do not simply
remove that error: require authenticated frozen sources, covered readers/writers
and initializer methods, and correct physical storage and GC descriptors in a
real Player. Keep unrelated native-only, RVA and ThreadStatic obligations until
their own implementations and tests justify admission. Loading happens before
application entry; migrating already running application state is separate.

Rollback boundary: new fixture/admission changes remain independent of the
previous `f494726` lab checkpoint. Any runtime correction gets its own commit,
real-header native gate and new immutable Player. Historical evidence and the
same failing Current are retained rather than overwritten.

## Initial evidence and admission candidate

Lab `bf3ddfe` builds Base-27 with runtime `9e7b601`, package `b51473f` and IL2CPP
`8a13baf`. Base startup (PID 15888) and no-op resource (20256) pass. Base ID is
`d9b3be71ef860ea44a273de0b7d65e718e9ba1c2222e8674a73ddeb2129285b2`; snapshot
`df9a450ed2480aafdf8b30417ea0ef3e2f613b093c4670b6fdbd5852582db4a9`; GameAssembly
`90FF4BB7BE64D8FC6C5B01BDF01E8EDAC43CF501D2DD15A268DC05F456A3712C`.
The new Current Model hash is
`BBD7C413F923926E131B2639AD3F01BE5A6278A1F40ADDE21123DC1E6AE8D31E`.
The three other Current DLLs retain the prior 31-case payload bytes.

Artifacts are under `artifacts/dhe-frozen-static-resources-20260910`. Host-02 at
lab `cdea2c2` compiles Current against the real captured Native source and passes
all 45 CLR reference cases in `resource-01/reference.json`. Resource generation
then rejects exactly six ordinary static fields, before any Player update.
`admission-01/result.json` confirms all six are detected and selected, and the
complete 40-assembly ordinary guard inventory has no holes. The other native
copy/owner obligations are discharged by the existing frozen-source proof.

The candidate admission requires original frozen source identity, a selected
static field, selected/covered concrete affected methods and every owner cctor.
Unselected readers, writers, ref access, clear operations or cctors must retain
the rejection. Ordinary ThreadStatic and RVA fields stay outside this proof.
Resource generation must require current-static-value-storage-v1 and shared-
type-initialization-v1 for ordinary fields as well as hotfix fields. This is
only an admission candidate until the same 45-case bytes execute in a real
immutable Player. No native source has changed at this step.

Lab `29f3d9d` passes all ten admission proof checks in `admission-02`, including
rejections after removing each initializer/read/write/ref/clear selection.
`resource-02` then passes all 45 cases on the original Base-27 (PID 18648), using
the exact Current above. All 12 workflow checks pass: missing/corrupt Added
rejections (8492/16704), full resource restoration (18944), snapshot substitution
rejection (17708), restoration (13240), and immutable Player hashes. Required
runtime capabilities include both Current static storage and shared type
initialization. Current set is
`272ca2a518ebb15ae99b77353b1cfe27d84ec9af022c12071400300f0f952e2f`; resource manifest
`91B5F55DC2CFFE91E6FBFF464500BC7520F729EC96C45A2D50BB3540BA664E5D`.

Add one further Current-only case to measure dispatch counters around the
unaffected NativeStaticOwner neighbor/count readers after adaptation. The CLR
host skips IL2CPP internal calls; the real Player must show two AOT entries and
zero interpreted entries in that interval. This creates a separate 46-case
Current without replacing the original 45-case evidence. The next Base has the
grown Payload layout but the same ordinary Native definitions; both immutable
Bases must consume the identical new Current. Runtime sources remain unchanged.

## Two immutable Bases and the 46-case Current

`current-02/current` adds only the dispatch probe to the 45-case workload.
Workload source is lab `983415f`; its NoInlining helper keeps IL2CPP internal
calls outside the CLR reference's JIT body. The CLR skips that helper; the real
Player requires at least two AOT entries and zero interpreter entries around
the two unchanged Native readers. This is dispatch correctness, not a throughput
measurement. All 46 reference cases run once through the actual revision entry.

Current Model SHA is
`7303D21328D6B9D228BC4947F2DDBA2FABAFAF8EE70F105C654631BB96C66D20`;
the complete Current set is
`f14135a093fe4cd18d987dfcd2d51c50407b68ae5061f53b7b4c9b6e0c08709e`.
The other three DLLs retain their earlier bytes. Host-04 is compiled from
`b4c46d3` and has SHA
`20506410E025FB7BB7DB97F2FC55650A50546F4845C2F46F0E9F27FD082DF5F2`.
Workloads are excluded from that host's Compile glob and are compiled separately
from the checked-out source by Unity csc; `current-02/compiled/compiler-evidence.json`
records those identities. Resource-tool-01 remains on `29f3d9d`, SHA
`69401657F05776557045A076C5F6F7FB884D102565C57D7A8F20BF86EC6CCFEE`.

`resource-03` passes all 12 single-Base checks on Base-27. Primary/restored
Player PIDs are 16968, 1004 and 19804; missing/corrupt/snapshot rejections are
15696, 6724 and 17524. Its resource manifest is
`F42BB3BF822C6414DEA4D571D2881FD827F54B23DCE2B4C753F37366B009B137`.

Base-28 compiles the grown Payload, the original three DHE assemblies and the
captured Native definitions from Base-27 into a new immutable Player. Its
startup (5612) and generated no-op resource (16772) both pass at revision 59.
It contains 40 ordinary assemblies with complete guard coverage. Base-27's
startup revision is 41 and its Payload has the old layout. Neither archived
Base is rebuilt or patched during resource testing.

| Identity | Base-27 | Base-28 |
|---|---|---|
| Base ID | `d9b3be71ef860ea44a273de0b7d65e718e9ba1c2222e8674a73ddeb2129285b2` | `fb86714d7fc71828a415351379a5cb922a1ac8b65b66fb5e66f47365e39b7cb9` |
| Snapshot | `df9a450ed2480aafdf8b30417ea0ef3e2f613b093c4670b6fdbd5852582db4a9` | `81d4dcf1d570d09e4dd5b661d1cf37dbb6d7aac3f139f61f3f04995db92ac6b2` |
| GameAssembly | `90FF4BB7BE64D8FC6C5B01BDF01E8EDAC43CF501D2DD15A268DC05F456A3712C` | `EF6D53F715465AFC6930C4303FBF5F76AA4BF86330858568334EC9808764E6A0` |

Both use HybridCLR `9e7b601`, IL2CPP `8a13baf` and package `b51473f`.
Base-28's build records lab `29f3d9d`, host-03 and the previously built default
tool (`739F4B2FC604259C249D24EB4F653B80BB0930DE261D603D6B69BADB7515208E`),
distinct from resource-tool-01. The build's no-op does not need the new static
admission. Its source-bound result remains unchanged.

`resource-04-multibase/result.json` records clean lab `983415f` and passes all
19 workflow checks with one shared resource manifest:
`027CBACD7092AAF1040A97F4F90233C775B721042FD2E1D1F1645188A4EB432E`.
Both Bases run the same four Current DLLs, with Added interpreter-only and the
original three differential. Base-27 selects authenticated frozen Native and
corlib IL to adapt the grown value storage. Base-28 already has the current
layout and needs no frozen source payload. Ordinary source code remains fixed.

| Execution | Base-27 PID | Base-28 PID |
|---|---|---|
| Primary, all 46 cases | 16284 | 12504 |
| Missing Added rejected before entry | 11296 | 19196 |
| Corrupt Added rejected before entry | 8828 | 5388 |
| Added restored, all 46 cases | 6500 | 20164 |
| Frozen snapshot substitution rejected | 20300 | Not applicable; no frozen payload |
| Snapshot restored, all 46 cases | 17056 | Not applicable |

## Review, audit and remaining work

Lab `2a0a645` adds `frozen-resource-audit`, a C# read-only verifier of original
Base build results, Player binaries, captured snapshot manifests/DLLs, original
Current DLLs, resource DLL/MV hashes, complete Base selection, and full case
sequences in every successful/restored Player log. It also checks that rejected
runs never enter business code. Host-05 produces `audit-01-multibase.json`:
131 files rehashed, two Bases, five full successful runs and five rejected runs.
`audit-02-historical-45.json` separately verifies 76 files and three successful
runs of the retained 45-case evidence; it is not relabeled as 46-case evidence.

The static admission proof retains all ten checks in `admission-02`, including
omitted storage/cctor/reader/writer/ref/clear selection rejection. The same
runtime's real-header `native-04/DHE-Unity2022/native-gate.json` remains the
applicable native compile/CTest evidence (`mergeReady=true`, no surrogate
headers); no runtime, package or IL2CPP code changed for this milestone.

Remaining boundaries are ordinary ThreadStatic/RVA storage of evolved values,
actual mixed-batch MV registration failure/retry and module initializers,
native-only ABI obligations, broader Unity behavior, and production-equivalent
performance/memory qualification. Existing hotfix-side ThreadStatic coverage
does not discharge ordinary ThreadStatic storage. GC tests here are retention
smokes, not complete memory qualification. Startup loading remains required;
arbitrary live migration after business execution is not qualified.

Continue Unity 2022 Windows first, then implement and verify Tuanjie; do not
infer Tuanjie or ARM64 success from this result. New Unity 2021 work is outside
the user scope. No CAT, formal branch/tag, remote or Installer-default change
was made. C: retained about 24.6 GiB free after validation, so no cleanup or stash
was needed. Rollback of this static admission selects lab `f494726` and keeps
the same runtime/package/IL2CPP identities; ordinary static evolution will
again be rejected. Preserve all newer Base and Current evidence.
