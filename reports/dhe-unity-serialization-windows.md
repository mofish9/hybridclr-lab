# Unity serialization and cloning with evolved hotfix layouts

## Actual Windows results

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
