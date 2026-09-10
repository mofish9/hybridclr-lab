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

## Initial evidence and admission candidate

Lab `bf3ddfe` builds Base-27 with runtime `9e7b601`, package `b51473f` and IL2CPP
`8a13baf`. Base startup (PID 15888) and no-op resource (20256) pass. Base ID is
`d9b3be71ef860ea44a273de0b7d65e718e9ba1c2222e8674a73ddeb2129285b2`; snapshot
`df9a450ed2480aafdf8b30417ea0ef3e2f613b093c4670b6fdbd5852582db4a9`; GameAssembly
`90FF4BB7BE64D8FC6C5B01BDF01E8EDAC43CF501D2DD15A268DC05F456A3712C`.
The new Current Model hash is
`BBD7C413F923926E131B2639AD3F01BE5A6278A1F40ADDE21123DC1E6AE8D31E`.
The three other Current DLLs retain the prior 31-case payload bytes.

Artifacts are under `artifacts/dhe-frozen-static-resources-20260910`. Host-02 at
lab `cdea2c2` compiles Current against the real captured Native source and passes
all 45 CLR reference cases in `resource-01/reference.json`. Resource generation
then rejects exactly six ordinary static fields, before any Player update.
`admission-01/result.json` confirms all six are detected and selected, and the
complete 40-assembly ordinary guard inventory has no holes. The other native
copy/owner obligations are discharged by the existing frozen-source proof.

The candidate admission requires original frozen source identity, a selected
static field, selected/covered concrete affected methods and every owner cctor.
Unselected readers, writers, ref access, clear operations or cctors must retain
the rejection. Ordinary ThreadStatic and RVA fields stay outside this proof.
Resource generation must require current-static-value-storage-v1 and shared-
type-initialization-v1 for ordinary fields as well as hotfix fields. This is
only an admission candidate until the same 45-case bytes execute in a real
immutable Player. No native source has changed at this step.

Lab `29f3d9d` passes all ten admission proof checks in `admission-02`, including
rejections after removing each initializer/read/write/ref/clear selection.
`resource-02` then passes all 45 cases on the original Base-27 (PID 18648), using
the exact Current above. All 12 workflow checks pass: missing/corrupt Added
rejections (8492/16704), full resource restoration (18944), snapshot substitution
rejection (17708), restoration (13240), and immutable Player hashes. Required
runtime capabilities include both Current static storage and shared type
initialization. Current set is
`272ca2a518ebb15ae99b77353b1cfe27d84ec9af022c12071400300f0f952e2f`; resource manifest
`91B5F55DC2CFFE91E6FBFF464500BC7520F729EC96C45A2D50BB3540BA664E5D`.

Add one further Current-only case to measure dispatch counters around the
unaffected NativeStaticOwner neighbor/count readers after adaptation. The CLR
host skips IL2CPP internal calls; the real Player must show two AOT entries and
zero interpreted entries in that interval. This creates a separate 46-case
Current without replacing the original 45-case evidence. The next Base has the
grown Payload layout but the same ordinary Native definitions; both immutable
Bases must consume the identical new Current. Runtime sources remain unchanged.
