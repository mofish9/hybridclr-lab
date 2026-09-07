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
