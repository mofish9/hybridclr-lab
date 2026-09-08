# DHE supplemental field addresses

## Reproduction and objective

The original generic-fields-nullable assertion reaches `ldflda` when ordinary
C# nullable comparisons read HasValue. All nine source-bound Windows runs at
`303d755` register successfully, then fail with the runtime's explicit unsupported
field-address exception. The exact DLLs and failed report remain archived.
Changing that C# expression to a local copy would bypass the requested capability;
the original expression must remain a required regression.

Support ordinary managed addresses of supplemental instance fields on existing
reference types, including closed generic reference types. Address aliasing,
direct and reflected stores, ref/out/return, struct and nullable mutation,
Interlocked calls, owner lifetime and GC must match CLR. Keep Base object layouts
unchanged. Native ABI, existing value-type layout and interface/vtable evolution
are separate required parts of the broader goal, not silently accepted here.

Correctness is primary; allocation count, resident storage and access cost are
secondary measurements. A slow correct prototype is not a performance claim.
All three native real-header profiles and actual Windows Players are required.
Unity 2021 uses its ordinary bridge/metadata path; Unity 2022 and Tuanjie also
exercise FGS. Do not infer ARM64 GC/ordering/ABI correctness from Windows.

## Storage investigation

MetadataModule currently stores a replaceable boxed value in an object[] slot.
Returning its unboxed address would become stale on the next direct or reflection
assignment. Returning the object[] element address would become stale if the
array expands. Nullable boxing additionally removes the nullable wrapper, so
neither address represents the declared field's physical value reliably.

The existing sidecars already use the engine's registered ephemeron arrays.
A candidate design is a stable per-field managed cell with an owner reference
and an inline value of the closed field type. A package-owned preserved generic
cell declaration can supply ordinary runtime layout and GC descriptors. Native
code can allocate a closed cell without invoking newly generated AOT methods.
This remains a design hypothesis until verified on the actual engines.

The ephemeron value would retain cells; each cell retains the logical owner.
A live interior managed reference must keep the cell and owner alive. An
unreachable owner/cell cycle must still be collectible through ephemeron semantics.
Cells must never be replaced after publication. Array expansion moves only cell
references, not field storage. All direct/reflection accesses must converge on
the same cell and use engine field-copy/write-barrier/nullable helpers.

Cell type inflation and storage allocation must use metadata-lock then sidecar
mutex order. Publish fully initialized owner/value storage under the sidecar lock;
concurrent first access must produce one canonical address. Do not hold the
sidecar mutex while entering an unaccounted engine metadata path. Audit GC roots
across native calls and keep value-type reflection get/set copy semantics.

## Identity and tests

The offline address scanner currently formats IField.DeclaringType.FullName.
For a closed GenericInst this differs from the open FieldDef identity, allowing
the original unsupported nullable use through compatibility checks. Resolve
definition identities with dnlib and test same/cross-assembly closed references,
including generic arrays and nested signatures. A new address capability must
be required on affected Bases. Old runtimes must not gain it via a manifest.

Image::GetFieldInfoFromFieldRef also still chooses by token alone and asserts
the name afterward. Add a Current fixture inserting fields before original
fields, require token/name agreement, and verify old storage and new aliases.

Required semantic fixtures include:

- the unchanged nullable failure, default values and ref mutation;
- repeated addresses, direct/reflection writes through existing aliases;
- sidecar-array expansion while an earlier field reference remains live;
- ref/out and ref-return calls across interpreter/AOT boundaries;
- Interlocked numeric/reference operations on supplemental storage;
- structs with references, nullable values and generic closed-type isolation;
- concurrent first access and repeated access without address replacement;
- GC with only a managed field reference live, then complete owner/cell collection;
- original and evolved Bases sharing current resources, consecutive/skipped updates;
- the full cold 220-case suite with unchanged golden and no diagnostic pre-touch.

Freeze native/package commits before new Base builds, use the new installed-source
and Base identity checks, and retain all archived Players. A native capability
repair needs newly compiled Bases; existing v12 archives cannot be relabeled.
Rollback selects the previous matched implementation and compatible resources.
