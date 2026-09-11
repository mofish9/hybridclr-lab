# Archived Unity Scene and Prefab evolution

Create real authored inactive Scene/Prefab objects whose hotfix Component and
nested serializable types are AOT in the Base. After selecting a Current, load
those unchanged assets through Resources and additive SceneManager APIs. Cover
existing fields, renamed nested fields with FormerlySerializedAs, added fields,
lists, SerializeReference nodes, Unity object references, serialization callbacks,
Awake and cloning. Existing JSON/dynamic-component evidence does not prove this.

Build separate old-layout and evolved-layout Bases, then serve identical Current
DLLs to both. Check each Base's own original asset values; do not rewrite old
scene/Prefab bytes. An unchanged resource must first pass the asset probes. New
resource bundles and assets instantiated before DHE selection remain later gates.

Source work is isolated under research/dhe-unity-assets-v8.13.0, starting from
lab 1764faf, runtime 6180597/819f74c and package ed4b7b5. Changes to generated
Unity projects are produced only by committed C# fixture workflows. Large new
artifacts use F:/hybridclr_artifacts/dhe-unity-assets because C: is nearly full;
existing proofs are not moved or overwritten. No formal release, Installer
default, CAT or mobile change. Correctness and identities are hard gates;
no performance claim. Runtime fixes, if required, need isolated commits and
new native gates/Bases. Rollback selects compatible archived resources and
restarts the Player, without rewriting existing Base identities.
