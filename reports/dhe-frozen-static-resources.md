# Ordinary AOT static storage of evolved hotfix values

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
