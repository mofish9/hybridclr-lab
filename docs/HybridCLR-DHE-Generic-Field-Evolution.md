# DHE existing generic reference-type fields

## Objective and scope

An immutable Base containing a generic reference type must accept a resource
adding ordinary instance and static fields to that type. The same current DLL
must also work when an older Base does not contain the type at all. Direct IL,
reflection, closed-type isolation, preserved old storage, GC and concurrent
first access must agree with the exact current DLL on CLR. Correctness is the
hard gate; field-access cost, metadata growth and resident memory are secondary
measurements, not presumed benefits.

The first fixture extends the already-existing `DheAddedGenericType<T>` without
changing its parent, generic parameters or original Value property. Opting into
`DHE_GENERIC_FIELDS_CURRENT` adds T and T[] instance fields, a T static field and
an int static field. Nine recorded assertion groups cover primitive, reference,
struct, nullable, nested generic/array types, concurrent first access, open and
closed reflection identity, static isolation and reference GC retention.
The fixture is disabled in the existing default/evolution builds so frozen Base
and current resources remain reproducible. It changes no golden expectations.

Field-address evolution and value-type layout remain separate required work.
This fixture copies struct fields by value; it does not claim ref/address support.
Generic field removal/signature replacement, inheritance, custom attributes,
static initialization and broader GC/concurrency stress require follow-on tests.

## Implementation investigation

Current C# compatibility rejects existing generic-type fields. Native
SuperSetAOTHomologousImage::InitFields independently rejects generic instance
field additions. Its supplemental-field enumeration is indexed by the open Base
class only; ordinary GenericClass::SetupFields inflates only physical Base fields.
Therefore deleting the checks cannot provide correct closed-type metadata or
field storage.

The implementation must provide canonical closed supplemental fields with actual
inflated field types, correct logical declaring classes and the appropriate
current-image token/physical storage owner. Direct FieldDef and MemberRef
resolution and reflection must converge on the same storage. Instance sidecar
slots should be shared by definition across closed instantiations while retaining
the closed field type; static fields require distinct storage per closed type.
Original Base object layouts and generic ABI must remain unchanged.

Generic classes can be first touched after DHE registration, so an immutable
registration-only map is insufficient. Any lazy field cache needs fully prepared
stable entries, one publication lock and synchronized readers. Audit the existing
metadata-lock/sidecar-mutex order before adding a metadata-to-sidecar path: field
storage currently invokes engine type/array/boxing operations while holding its
mutex in several paths. A cache fix must not introduce a lock-order inversion or
publish half-inflated fields. No x64 result can establish ARM64 ordering.

MemberRef resolution is an additional independent requirement: MetadataUtil's
ResolveField currently searches only the physical TypeDefinition fields. Generic
field access normally uses a TypeSpec/MemberRef even within the same assembly.
Unlike method resolution, it has no merged-view fallback. Field token-handle and
ordinary IL resolution must both locate supplemental definitions without mixing
Base and Current token ownership.

## Gates and rollback

First execute the new assertions on CLR and reproduce the current offline
rejection against the actual archived evolved Base. Retain those inputs and
failed reports. Then add native resolver/inflation tests and implement the maps,
storage and publication behavior before allowing the capability in resource
generation. Require the exact new capability on affected Base identities.

Run all three real-header compile/CTest profiles, then fresh original/evolved
Windows Bases on the frozen repair. Unity 2021 uses ordinary generic fallback and
supplemental metadata; Unity 2022/Tuanjie use FGS. Run shared resources, consecutive
and skipped updates, the new field assertions and the complete cold 220-case
suite. Preserve the current passing six-Base archives as regression inputs.

The MV wire format does not need a new name or schema. A native repair requires
new Base identities and an explicit runtime capability; old Bases cannot receive
it through a manifest. Rollback retains the existing native/package source lock
and rejects affected resources rather than modifying old Players. No formal
branch/tag, Installer selection or CAT change is part of this capability step.

## Reproduced checkpoint

The clean fixture source is `19f439b109959ab05b394329becc43eacf6e1e67`. Build the
normal evolution input with the existing C# `build-managed-cases` command, using
new explicit OutputRoot and AotOutputRoot directories. Build the AOT case project
again into that new input root with HYBRIDCLR_AOT_BENCHMARK, DHE_CURRENT,
DHE_STRUCTURE_CURRENT, DHE_EVOLUTION_CURRENT and DHE_GENERIC_FIELDS_CURRENT.
The feature-disabled AOT output remains a separate control. Both compilations
pass with zero warnings/errors after correcting three test nullable annotations.
The first failed compilation is not runtime evidence.

Current raw inputs are at `artifacts/evolution-generic-fields-raw` in the lab
worktree. All other paths below are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`:

- `reference-generic-fields.json` and its checks file pass 23 CLR groups.
- `generic-field-batch-u21` compares raw .NET inputs against a stripped Unity
  Base and also rejects framework-scope differences. It is retained as an
  exploratory mismatched-input result, not the isolated capability reproduction.
- `prepare-generic-fields-u21`: the ordinary C# workflow with Bootstrap and
  StopAfterPreflight generates Unity-prepared current assemblies, with clean
  validation source `19f439b`. No new Player is built or archived. Its local
  compiler is restored to the original hash and its transaction journal is absent.
- `reference-generic-fields-prepared.json` and
  `reference-generic-fields-prepared-checks.ids`: all 23 groups pass on the exact
  Unity-prepared DLL, including all nine generic-field groups.
- `resource-generic-fields-rejected/dhe-resource-update-validation.json`:
  `passed=false`, six candidate Bases, three compatible original Bases and three
  incompatible evolved Bases. Each rejected Base has exactly two generic instance
  field, two generic static field, one layout and one static-layout rejection.
  There are no unrelated framework-scope rejections after normal preparation.
  No `dhe-resource-update.json` or `dhe-runtime-plan.json` exists in that output.

The original-Base decisions are offline compatibility evidence only, not actual
Player execution of this new fixture. The evolved-Base rejection remains the
feature gate to fix. The six-Base 220-case pass described in the preceding report
uses the earlier frozen current DLLs and must not be attributed to these new DLLs.

| Artifact | SHA-256 |
|---|---|
| Unity-prepared fixture DLL | `E979FF508A2DAB5EF5CFE0EB8C065C5D20B12C95FD67320633F179F0450058A4` |
| Prepared CLR reference | `7E4277E3C865414A63B2F147F6205F804B27CB752363B9DDF28B0A863CA6F72C` |
| Prepared check receipts | `A3082F56F84F10C5EAEA2F803E2F65C64F4EAD06D1BDE77AE44E7C6CD0FBFA55` |
| Six-Base rejected resource validation | `00C77070682CEC9F6AED4203B0E56F4A2F20417776D614923EFBB3C868F0B158` |

Unity 2021's disposable project retains the four normally staged generic-fixture
DLLs; Unity 2022/Tuanjie remain at their original-generation input state. Previous
stashes and all archived Bases/replays are unchanged. The production native and
package sources, their locks and formal branches remain unchanged in this step.
