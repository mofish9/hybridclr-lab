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
