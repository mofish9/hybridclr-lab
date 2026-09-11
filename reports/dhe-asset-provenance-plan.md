# Asset build provenance

The existing delivery hashes bind supplied files but cannot detect that a caller
paired assets authored under another code schema. Add package-owned C# capture
around the actual Unity asset build. Before invoking the callback, snapshot the
Current assembly bytes and compare their serialization-relevant declarations to
the Editor's loaded assembly files. Verify the loaded MVID matches disk. Compare
schema rather than whole Editor/Player DLL hashes: source assemblies legitimately
have different method bodies and compilation symbols in Editor builds.

The schema gate conservatively includes public/SerializeField/SerializeReference
instance fields, their order/types and former names, Serializable declarations,
enum values, and their declaring type/base identity. Reject mismatches; never
silently stamp arbitrary assets with a new Current hash. After the build, recheck
Current and Editor inputs and capture only files inside the fresh output directory.
The record binds Current-set bytes, target/engine, schema hashes and asset hashes.
DeliveryBuilder must require and validate this record and embed its hash-bound
bytes. Mismatched code, changed assets or incomplete asset inventory must fail
before creating a delivery output.

Tests: method-only Editor differences allowed; field/type/order/attribute changes
rejected; stale loaded image rejected; wrong Current or changed bundle rejected;
real Unity asset build produces provenance, then a delivery using the existing
capable immutable Players passes all 46/42/new-data checks. No native runtime
change is needed for provenance; old unproven delivery artifacts remain historical.
Keep scope explicit: this is trusted build-pipeline provenance, not a signature,
and does not prove arbitrary callback internals. Native capability admission is
the next separate requirement. No CAT/mobile/production release in this step.
