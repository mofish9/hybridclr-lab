# Unity serialization and cloning with evolved hotfix layouts

## Latest Windows checkpoint

See `dhe-reference-storage-windows.md` for runtime b69128f/eaf006d, native08
and immutable Base-63/64. Both pass eleven serialization and seventeen lifecycle
checks with identical Current DLLs, plus public identity, generic coherence and
public preparation/recovery. Cached native queries and lifecycle now pass, together
with eleven physical receiver checks. Existing-type serialization-interface
addition is still rejected before Player execution. This is not complete
serialization/scene/Prefab qualification. Earlier evidence below
is preserved under its original commits and must not be treated as current.

## Historical Base-52/53 Windows results

The latest native correction is HybridCLR
`e4037758ad8476e87e005b6857f6b4db9bbeca48`, IL2CPP Unity 2022
`6d0f642bc99367ff07e9e0f2f248ee136d037114`, package `2d7355f`, lab
`4b2cc49ba4dd9d8ae3a66ff4a9d1e64392a35f5e`. It maps native component class
resolution before metadata caching and exposes exact physical field parents.
The allocation wrapper is defined with MetadataModule's implementation.
`dhe-reference-storage-native-03` passes real-header native compile/CTest,
including il2cpp-api.cpp; mergeReady=true and surrogateExternalHeadersUsed=false.
The preceding native01/02 failures are preserved, not relabelled as passes.

`dhe-reference-storage-base-52` and `dhe-reference-storage-base-53` both pass
construction, original startup and no-op resource checks. Their Base IDs are
`f3a84bb3ba9cc54d627e3b11f260a625775ba778570c2fc89f4b87130cad2ecb` and
`302ade15e97d03679fd93fff24489142dda34e5bce4fae6f5e00f1b498b2c40f`.
The unchanged full Current04 is packaged by
`dhe-reference-storage-resource-03`. Base-52 passes the 46 business cases,
eleven serialization checks, and eleven lifecycle checks through
layout-dependent-reader-selection. MethodInfo.Invoke then throws TargetException
because the object does not match the reflected type. Earlier constructor/Awake/
OnDisable frame exceptions and reflected field GetValue failures no longer occur
on this path. No old call-frame or general assignability guard was loosened.

Independent full-unity replay at lab `6077928` confirms the failure in
`dhe-reference-storage-base-52-replay-01`, and confirms all eleven serialization
plus seventeen lifecycle checks on Base-53 in
`dhe-reference-storage-base-53-replay-01`. Both replays validate the complete
46-case sequence and unchanged Player/staged-resource hashes. No full two-Base
serialization/lifecycle pass is claimed.

The unchanged fourteen-check reference Current02 is packaged separately in
`dhe-reference-identity-resource-03`; all business/rejection/restoration workflow
checks pass on both Bases with native probes disabled. Explicit reference replay
in `dhe-reference-identity-base-52-01` passes six assertions and fails eight;
`dhe-reference-identity-base-53-01` passes all fourteen. Current physical field
reads/writes now work. Type equality, assignability, declaring type identity and
clone type identity still fail on Base-52. Public identity and reflection
receiver semantics are the next mandatory gate, including cached Base handles
and safe rejection/adaptation when a field/object still has an old layout.
This candidate is not release-qualified and retains the experimental v32
contract; capability admission must be updated before any supported release.

## Preceding candidate and diagnostic evidence

The public reference identity probe now has real Player evidence. At lab
`088f967`, `dhe-unity-reference-current-02/current` passes CLR reference with
46 cases. The resource compiler at lab `4b2cc49` produces
`dhe-reference-identity-resource-02`; its ordinary business workflow succeeds
on both Base-50/51 with the reference probe disabled. Explicit native replay
in `dhe-reference-identity-base-50-01` fails ten of fourteen identity assertions;
`dhe-reference-identity-base-51-01` passes all fourteen with the same Current.
Base-50 uses the allocation-only `623e453`/`e9ba2a8` candidate, not the later
native API correction. The failures cover Type equality, assignability, field
GetValue/SetValue and clone type identity. Both replay reports rehash and verify
unchanged Player and staged resource bytes; all 46 business cases precede the
native probe. This is a confirmed remaining type identity gap, not a fixture
failure. No Base-52/53 result is inferred from this preceding candidate.

The full suite exposes a real runtime gap. Base-47 (old component layout) passes
all 46 business cases, then fails `json-reads-added-field`: JsonUtility emits
`Extra=0` after Current wrote `91000000019`. Base-48 (evolved layout) consumes the
identical Current and passes all eleven JSON/overwrite/clone assertions and the
46 business cases. These Bases were built at lab `ff9c627`, HybridCLR `988a7aa`,
Unity 2022 IL2CPP `4d5052e`, and package `2d7355f`, with explicit serialization
API preservation. The replay host is bound to lab `1c51414`.

All paths below are under `D:/hybridclr_artifacts`:

- `dhe-unity-serialization-current-04`: final compiler/entry-wiring evidence;
  the immutable merged DLL and final entry-wired DLL are recorded separately.
  Model SHA-256 is
  `5A076E42EC90D7F4146DBC94F6672F3E9CC558D2465340D948CBDF89CBFA3C54`;
  Current-set identity is
  `c6daf542bf579c7f1bdd5ad70d80fffc7f072d1a9abf7251faadb49649b67fba`.
- `dhe-unity-serialization-full-resource-01`: failed Base-47 run.
- `dhe-unity-serialization-full-base-48-01`: successful same-layout replay.
- `dhe-unity-serialization-read-resource-01` and
  `dhe-unity-serialization-read-base-46-01`: preceding two-assertion diagnostic
  fails on Base-45 and passes on Base-46; it does not replace the full suite.

The separately instrumented trace at IL2CPP `16c7a61`, lab `4030ba0` preserves
`dhe-serialization-trace-native-01`, `dhe-unity-serialization-trace-base-49` and
`dhe-serialization-trace-resource-01`. Native compile/CTest passes. The failed
Player records nine GetOffset calls for Extra (offset 56, supplemental=1), no
GetValue/GetValueObject/SetValue calls, then JSON Extra=0. This is diagnostic
evidence only; Unity bypasses the sidecar-aware managed field value APIs.

An isolated resource-compiler prototype at lab `4f3134f` selects physical Current
storage for changed reference layouts, retaining existing dependency closure.
`dhe-reference-storage-resource-01` passes all eleven serialization assertions
and 46 business cases on Base-47 with the original Current bytes. Its subsequent
native component lifecycle test fails `awake-current-value`; Base constructor and
callback entry raise the Current-call-frame ABI guard. Thus the prototype is
incomplete. A separate native allocation candidate is being tested; none of these
changes are a formal release or an Installer default.

Public Base type identity, reflection/native allocation, instances existing
before selection, inheritance/generics and archived scene/Prefab evolution still
need tests. Preserve the ABI rejection gates and all failed inputs/Players. The
independent resource audit must also explicitly validate serialization checks
before this capability can be qualified. There is no new performance, ARM64,
Tuanjie or general Unity serialization claim.

## Original design and compiler diagnostics

The next Current-only fixture adds fourteen independent public reference identity
and allocation assertions: Assembly.GetType versus typeof/Object.GetType, equality,
assignability, reflected declaring type, field reads/writes, native GetComponent
and cloning. Each failure is logged without hiding the remaining assertions.
`unity-reference-current` appends its optional entry to preserved Current DLLs;
`unity-serialization-replay ... reference` runs it under `-unityReferenceProbe`
and checks all fourteen exact records. It does not run in the CLR reference.
Use a fresh resource with identical Current across original/evolved Bases, and
preserve failures. This fixture does not claim to test pre-existing objects or
Type handles cached before DHE selection; those require a separate Base fixture.

The initial reference fixture at `b52ede0` compiled, but CLR reference fails
before resource generation: JIT resolves Unity locals in RunIfRequested even
without its command-line flag. Preserve `dhe-unity-reference-current-01` and
`dhe-reference-identity-resource-01`. Move the native body into a separate
non-inlined method, as in the serialization probe; do not simulate Unity APIs
or remove the 46-case CLR reference gate.

The independently audited serialization control succeeds at lab `b52ede0`:
`dhe-unity-serialization-control-resource-01` uses unchanged Current04 on
same-layout Base-48, and `dhe-unity-serialization-control-audit-01.json` passes
17 checks and binds 67 files. It checks all 46 business cases and the complete
eleven-assertion native serialization sequence. This is the unchanged-layout
control, not evidence of the new native allocation candidate.

Continue from the completed public pre-commit/Unity component checkpoint on
HybridCLR `988a7aa`, IL2CPP Unity 2022 `4d5052e`, package `2d7355f` and lab
`0aef01a`. The prior goal turn made concrete progress: two immutable Bases passed
one Current, real lifecycle/coroutine/GC checks and native failure recovery.

The next gate is native Unity access to changed hotfix fields. Managed reflection
and interpreter field access already support supplemental storage; Unity native
serialization may instead consume raw field offsets. First use existing immutable
Base-45/46 and a new Current-only fixture to test JsonUtility serialization,
FromJsonOverwrite using both old and Current fields, and Object.Instantiate
cloning of an inactive existing MonoBehaviour. Require old/new fields to retain
their exact values and prevent fixture callbacks from contaminating the separate
lifecycle suite. CLR reference continues checking all 46 business cases; Unity
checks run only under an explicit Player flag and are never simulated in CLR.

Then extend to actual archived scene/Prefab assets and nested serializable types
as evidence warrants. No generalized Unity serialization support is claimed by
the initial JSON/clone subset. Changes go through committed source before tests;
preserve all failing DLLs, resources, Players and logs. Unity 2022 Windows first,
Tuanjie later, no new U21, CAT, formal tag/branch, Installer or remote change.
Large outputs stay on D:. This is correctness work, not performance evidence.

The first fixture compilation at lab `f0d16c5` fails against the old Base's stripped
Unity references: SetActive, FromJsonOverwrite, Instantiate, GetComponent and
DestroyImmediate are absent, and only ToJson(object, bool) remains. Preserve
`dhe-unity-serialization-current-01/compiled/compile.log`; no Player ran. Compile
valid Current code against complete Unity reference assemblies, and build dedicated
next Bases with explicit serialization API linker roots. These are ordinary AOT
API preservation requirements, not restored native code from a hotfix DLL.

In parallel, a labelled read-only diagnostic uses the old Base's existing
ToJson(object, bool) entry and checks its old/added field values. Its two assertions
do not replace the complete eleven-assertion JSON/overwrite/clone suite. New Base
roots and diagnostic selection are fixture code only; runtime/package stay frozen.
