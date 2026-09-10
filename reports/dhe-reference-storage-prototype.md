# Reference layout Current storage prototype

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
