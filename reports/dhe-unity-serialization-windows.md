# Unity serialization and cloning with evolved hotfix layouts

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
