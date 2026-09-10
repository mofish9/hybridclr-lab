# Reference layout Current storage prototype

## Current checkpoint

The latest source-bound result is in `dhe-reference-storage-windows.md`.
HybridCLR b69128f / IL2CPP eaf006d and immutable Base-63/64 pass reference/generic
identity, cold and cached serialization/lifecycle and public failure recovery.
The cached native query and callback failures are fixed. Existing-type interface
addition remains rejected; broader native ABI, scene/Prefab and old-object
semantics are still open. This prototype remains unqualified.
The results below preserve earlier candidates and are historical evidence.

## Historical Base-52/53 result

See `dhe-unity-serialization-windows.md` for all source-bound evidence. The
current locked runtime is HybridCLR `e403775`, IL2CPP Unity 2022 `6d0f642`,
package `2d7355f`. Native03 passes compile/CTest with real headers, including
il2cpp-api.cpp. At lab `4b2cc49`, immutable Base-52/53 pass startup and no-op
resources. Full Current04 passes all eleven serialization assertions on both.
Old-layout Base-52 passes eleven lifecycle checks before reflection instance
invocation fails; same-layout Base-53 passes all seventeen. The fourteen-check
public reference fixture passes six on Base-52 and all fourteen on Base-53.

Native type resolution now selects the physical class before Unity caches
callbacks. Current physical field reflection works; public Type identity and
reflection receiver compatibility are not solved. Keep the allocation/API/field
changes together as an isolated prototype and do not merge into formal branches,
change Installer defaults, or claim full DHE support. No performance/ARM64/Tuanjie
qualification is present, and the experimental v32 capability contract has not
yet qualified reference storage.

Next define one public reference type identity without conflating physical
layouts. Test Assembly.GetType/typeof/Object.GetType, reflection method calls,
generic and inherited owners, cached Type/FieldInfo handles and objects created
before selection. In particular, broadening logical IsInstanceOfType must not
let Current field offsets access an old object. Preserve native ABI checks and
add explicit physical receiver validation/adaptation before enabling such paths.
Then rerun one identical Current on both Bases, full lifecycle/serialization,
public preparation/recovery and unchanged AOT dispatch gates. Port to Tuanjie
only after the Unity 2022 Windows candidate is stable.

The source/report consolidation at lab `3fce4e5` builds host-04 successfully.
`dhe-reference-identity-resource-audit-03.json` independently validates two Bases,
46 business cases, four successful/restored runs and 126 files. Those runs have
the optional reference probe disabled; the separately preserved eight failing
identity assertions are not converted to a passing capability claim by this
business/resource audit. Native03 remains bound to its original exact runtime
commits; no native or Player rebuild is claimed for documentation/host changes.

## Allocation-only candidate result

At lab `bb6f743`, HybridCLR `623e453` and IL2CPP `e9ba2a8`, both immutable
`dhe-reference-storage-base-50` and `dhe-reference-storage-base-51` pass Base
construction, startup and no-op resources. All artifacts are under
`D:/hybridclr_artifacts`. The separate native gate in
`dhe-reference-storage-native-01` fails because including MetadataModule.h from
Object.cpp exposes the Windows max macro to the metadata parser's std::max.
These Player builds do not override that failed gate.

`dhe-reference-storage-resource-02` uses unchanged serialization Current04 on
both supported Bases. Base-50 passes all 46 business cases and eleven native
serialization assertions, then fails the lifecycle suite. Awake/OnDisable/
OnDestroy still enter the old Base ABI guard, and RuntimeFieldInfo.GetValue
rejects the object's same-named but different physical type. The constructor
exception from the preceding prototype is absent, but allocation routing alone
does not align Unity's cached callbacks and reflected field identities. This
run stops on Base-50; it is not a two-Base pass.

The preceding intermediate native source was HybridCLR `ca15268`, IL2CPP
`b0cf8e3`. Its lightweight wrapper addressed the include dependency only and
failed the native02 unit compilation described below. It is superseded by the
current source above; its failed evidence is retained.

## Original design and preceding compiler prototype

The lightweight-wrapper gate also fails in native02: the isolated DheRuntime
unit target does not include MetadataModule's implementation dependency. Move
the wrapper definition beside the existing virtual/interface wrappers in
MetadataModule.cpp; keep only its declaration in DheRuntime.h. Do not add a
test-only stub or bypass the gate.

The next behavioral candidate maps native `il2cpp_class_from_system_type`
requests to selected reference execution storage before Unity caches class
metadata. Allocation-only mapping occurs too late for those caches. For a
reflected field physically declared by exactly that selected Current class,
expose that physical declaring class for the managed receiver check. This does
not make all Base/Current objects assignable, change cached field offsets, or
relax any old call-frame guard. Extend real-header native compilation to include
il2cpp-api.cpp. The preserved eleven serialization assertions and seventeen
lifecycle assertions are the failing-before acceptance tests; public Type
identity and pre-existing objects remain separate required gates.

The uninstrumented runtime accepts a resource whose old-layout MonoBehaviour has
an added long field in sidecar storage. Current reads/writes the field correctly,
but Unity ToJson reports zero. The identical resource succeeds on a same-layout
Base, including all eleven JSON/overwrite/clone assertions. The original Players,
Current DLLs and failures remain immutable.

Existing Current storage supports physical reference owners containing changed
value fields. Test whether the same execution-plan mechanism can represent a
changed reference layout directly, instead of using supplemental field sidecars.
Include reference layout roots in the existing dependency analysis, preserving
field/reference distinctions, derived-layout closure, native boundary checks and
the same Current DLLs. Reference fields themselves still have pointer storage.

This is a resource-compiler prototype only. It does not change runtime/package
or weaken rejection gates. Run the real failure and its same-layout control using
one regenerated standard resource on Base-47/48. A pass does not establish complete
support: public reflection allocation, native-created components, cached Base
types/objects, inheritance, generic instances, serialized assets and mixed AOT
callers still require validation. Unchanged methods outside affected dependencies
must remain AOT. Keep this isolated from the separately instrumented trace branch.

The compiler-only prototype at `4f3134f` passes all eleven serialization checks
and 46 business cases on old-layout Base-47 with the original Current bytes.
Its subsequent lifecycle probe fails at Awake: native entry calls to the Base
constructor and callbacks are rejected by the existing Current-call-frame guard.
Preserve `dhe-reference-storage-resource-01`, including its failed Player result.

The isolated native trace independently confirms nine Field::GetOffset calls for
Extra (offset 56, supplemental=1) and no value-access API calls; ToJson still emits
zero. Thus sidecar-aware managed field APIs cannot fix this native consumer.

Next route new reference allocations requested with a public Base class to its
published Current execution class. Do this only after the existing acquire gate,
only for selected reference layouts, and before class initialization/allocation.
Preserve all old value ABI rejection guards. This does not migrate pre-existing
objects; that remains a separate correctness gate, not a supported claim.
Build new immutable original/evolved Bases, rerun serialization plus lifecycle
and failure recovery, and keep native/runtime source identities separate from
the diagnostic branch. Do not qualify the prototype from serialization alone.
