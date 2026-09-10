# Public pre-commit retry and Unity component evolution

Continue the full DHE goal after `dhe-public-load-recovery-windows.md` on committed
HybridCLR `988a7aa`, IL2CPP Unity 2022 `4d5052e`, package `2d7355f`, lab `13791a7`.
Unity 2022 Windows remains first; Tuanjie follows and no new Unity 2021 work is planned.

Two remaining correctness areas need real Player evidence. First, the public
loader must reject a native MV registration error after metadata preparation,
retain old dispatch and an unpublished Current graph, then accept the corrected
same graph without resetting native state. The fixture will alter only the
in-memory test copy of an authenticated Base MV after public initialization,
restore it in finally, and use public loading for both attempts. No archived Base,
DLL, snapshot or resource is modified. This fault injection tests runtime rejection
and retry; it is not a claim that resource integrity validation accepts a bad MV.
Native metadata-preparation exceptions remain a separate case: use a reproducible
malformed metadata fixture and explicitly record which normal compiler checks
reject it, rather than treating a simulated phase as actual native execution.

Second, add real hotfix MonoBehaviour types to Base AOT inputs and evolve the
same Current DLLs. Exercise Awake/OnEnable/Start/Update/LateUpdate/OnDisable/
OnDestroy, IEnumerator coroutine scheduling, additional instance fields and their
GC roots, and a newly added component type. Create components only after DHE
selection, matching the required before-business-entry workflow. An unchanged
reader must retain AOT while changed callbacks use Current. Compare two Bases
with original and evolved component definitions, using one unchanged Current set.
Retain the existing 46-case resource reference and initializer invariants.

Use dedicated C# fixture/compiler/Player code, committed before source-bound
builds and immutable replays. Production runtime/package changes require their
own commits and repeated real-header native tests. Report full checks and exact
source/build identities; do not extrapolate Windows to ARM64 or Unity to Tuanjie.
These are correctness gates, not performance results. All outputs go to new D:
directories and all failure evidence is retained. No CAT, formal branch/tag,
Installer default or remote change is authorized by this work.
