# DHE existing interface evolution

## Completed six-Base checkpoint

Clean `4eef487` passes the original/evolved v16 matrix on Unity 2021, Unity 2022
and Tuanjie: 18 cold processes, 42 evolution groups and 220 differential cases
per process, zero differences. All 114 protected files independently rehash
unchanged and all 24 Base MV snapshots reproduce byte-for-byte. The first
resource retains the case methods in AOT; latest/skipped runs have 220 interpreted
entry receipts each. Evolved Base InterfaceCall, DelegateCall and GenericConstrained
fingerprints remain unchanged; 23 fixture/compatibility checks pass.

Original-generation no-op Players also pass, but the initial enclosing workflows
fail the report schema because their structural entry does not exist. The schema
now accepts only the complete empty/unexecuted entry when both structural
expectations are false. Twenty-six positive/negative checks and separate schema
gates pass. No Base or failed workflow report is rewritten. Full evidence and
remaining capability work are in `reports/dhe-interface-generations-windows.md`.
The sections below preserve the investigation history, not current failures.

## Completed Unity 2021 checkpoint

Clean `c68b8c9` passes all three cold processes on the new evolved v16 Base.
Each executes all 42 evolution groups and 220 differential cases, with zero
differences. The first case payload stays AOT (zero entry receipts); both latest
runs record 220 interpreted entries. All 19 distinct protected files independently
rehash unchanged. Report `replay-interface-token-u21-cold/report.json` has SHA-256
`791C0260FC910432EA6D0469414F4B2928993E939790147BABF3956F177B0CD0`.

This closes both preserved Unity 2021 failures for the unchanged interface payloads.
All three native gates under `native-interface-token` pass with real headers.
The evolved Unity 2022/Tuanjie Base/no-op workflows also pass; their resource
execution and the same-runtime original/evolved six-Base replay are next. The
new config is `manifests/dhe-interface-generations-windows.json`. Existing v11/v13
Bases are not relabeled; original-generation code is compiled into fresh v16
Bases. General interface/generic/layout and Unity-facing behavior remain open.

## Token dispatch repair

Clean replay `d086ef2` on the fresh v15 Base closes the registration failure.
All three processes report `loadError=OK` and execute the previous 35 groups plus
the new reflection-invoke, interface-map and type-identity groups. Direct class,
boxed-struct, constrained and delegate calls still fail with MethodAccessException.
The failed output is preserved in `replay-interface-owner-u21-cold`.

IL MethodDef resolution returns the hidden Current interface MethodInfo and caches
its raw slot. Reflection already returns the canonical Base alias and logical slot.
Candidate `8cbc13a` reuses the existing published logical-method resolver before
an IL method token enters its cache. The custom-attribute entry remains a wrapper
over that shared resolver. Normal images and unavailable/unpublished DHE maps retain
their original behavior. Package `a22db3d` identifies the new native build as v16;
capabilities and MV bytes do not change. Fresh Base and unchanged-payload replay
are required before claiming this dispatch repair works.

## Reproduced explicit declaration failure

Clean replay `2c2f6bd` runs three processes on the new v14 Unity 2021 Base.
All fail atomic registration with `VTableSetUp fail` for the explicit
`IIntOperation.Added` implementation, before any evolution or differential case.
The failed replay remains in `replay-interface-slots-u21-cold`; neither accepted
resource nor successful Base/no-op evidence proves changed-interface execution.

MethodImpl MethodDef references keep their hidden Current declaring type, whereas
the interface list has already resolved to canonical Base identity. HybridCLR
`fd3b112` normalizes only the declaration owner through the existing homologous
type resolver. It retains the Current method definition and physical implementation
owner, including their generic context. Ordinary interpreter images are unchanged;
no layout, publication or locking changes are introduced. Package `2ec4821`
identifies this separate native candidate as v15, with the same capabilities and
unchanged MV format. A fresh Base and the unchanged payloads must reproduce or
close the failure; three-engine and multi-generation execution remain required.

## Player validation input

The v14 Unity 2021 Base/no-op workflow passes at
`base-interface-slots-u21/project-workflow-report.json` under the artifact root
below. Its immutable BaseId is
`41583e0fc8e32172464733ea5be1202d8de38e225cfd50468f594ab6126be732`.
The clean `da01f3b` C# host builds a separate one-Base registry and two accepted
resource packages. Exact first/latest CLR references both pass 220 differential
cases; the latest case assembly has entry-receipt instrumentation without inline
padding. The main interface fixture DLLs and their 42-group references are
unchanged. `manifests/dhe-interface-slots-u21.json` freezes the next cold replay,
including consecutive and skipped updates. This preparation is not yet proof of
changed-interface execution and does not replace the previous six-Base pass.

## Candidate implementation

HybridCLR `e58946f` keeps Base interface slots and appends logical slots for
Current-only methods. Its homologous VTableSetUp context uses Current interface
declarations with canonical Base type identities. Dispatch resolves the current
receiver vtable and maps the implementation back to its logical method. Slot
maps are built before DHE registration publication; receiver entries are created
under g_MetadataLock and remain stable after insertion. Failed registrations do
not expose this map through the published DHE assembly set.

Unity 2021 `2266aca`, Unity 2022 `5679c71` and Tuanjie `a840b0c` route their real
interface lookup paths through that map. Reflection resolves each logical slot
independently instead of indexing a contiguous old vtable slice. Tuanjie's lazy
lookup implementation remains separate. All three actual-header compile/CTest
gates pass at lab `4f5c064`, including the newly compiled ClassInlines.cpp and
RuntimeType.cpp, and native slot collision/removal/overflow checks.

Package `1e86958` and the candidate C# analyzer declare runtime v14 and
`existing-interface-method-slots-v1`. Nineteen declaration/compatibility checks
pass, including rejecting old runtimes and unrelated final/non-final virtual
methods. All 24 archived MV snapshots still reproduce exactly. This capability
declaration is exploratory for new validation Bases, not a release or a Player
qualification. Actual interface execution and the full mixed-Base replay remain
pending. MV wire format remains DHEMETA1/schema 1.

## Reproduced checkpoint

Clean fixture source `16107af` compiles with zero warnings/errors and passes all
42 CLR evolution groups before and after ordinary Unity 2021 Current preparation.
The previous 35 groups are unchanged; seven new groups cover explicit class and
boxed-struct dispatch, constrained calls, delegates, reflection invocation,
GetInterfaceMap and type identity. The preparation stops after preflight and
does not build a new Base Player. The project-local compiler is restored and
its transaction journal is absent.

The prepared input passes twelve independent declaration checks. Both original
and evolved Bases expose Apply at interface slot 0. Current exposes Added at 0
and Apply at 1. The three affected types retain their physical fields and their
original Apply method fingerprints. The class implementation is explicit and
the value-type implementation is implicit. This is a genuine slot collision,
not an unrelated value-layout or old-method-body change.

Resource validation rejects all six frozen Bases with only the expected added
interface/virtual-method reasons; guard coverage still passes. Original Bases
have two reasons (IIntOperation and IntOperationStruct); evolved Bases add the
existing DheEvolutionOperation explicit implementation as a third reason.
No resource-update or runtime-plan manifest is emitted. The validator and native
runtime are unchanged. This is an unsupported-capability reproduction, not a
Player pass or an implementation of interface evolution.

Paths are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`:

| Artifact | SHA-256 |
|---|---|
| `prepare-interface-slots-u21/current/HybridCLR.ManagedCasesAot.dll` | `BDA9EE5E5364DB54952EE7A11651D85819CD04DA34A2662A111D758DC59BDDCA` |
| `interface-slots-reference-prepared.json` | `CC3DDAAFF8447DC8F5978E979824C780BBD5D9D6423B597BEE7EE01D4CA80A1C` |
| `interface-slots-prepared-identity/report.json` | `B2A2499E73A917FE74BB91855C367987A58C6383D6CB88996CD62E1F4C492AA1` |
| `resource-interface-slots-rejected/dhe-resource-update-validation.json` | `A2CE5448D39E3C736137B6D1C0CC73233456289DA24058D20B2F76D83FDF5D86` |

`interface-slots-reference-prepared-checks.ids` records all 42 executed groups.
`registry-field-address-stable-generations.json` is the unchanged six-Base input.
Raw inputs are `artifacts/evolution-interface-slots-raw` under the lab worktree.
The earlier append-only experiment remains under the `interface-evolution-*`,
`prepare-interface-evolution-u21` and `resource-interface-evolution-rejected`
paths; its evidence is not relabeled as a slot-collision test. Generated Demo
inputs from those two preceding states are preserved in stashes `e2a94af` and
`5af8f3c`; older stashes and all archived Players remain untouched.

## Objective and acceptance

An immutable Base already contains IIntOperation and its value-type implementation.
A later resource adds an interface method and updates its implementations, without
changing object or value-type fields. The same resource must work when the class
implementation is new to an original Base and already native in an evolved Base.
The ordinary Apply method and its existing AOT call sites must keep their semantics.
Added precedes Apply in Current metadata, deliberately shifting Apply from slot 0
to slot 1. Base AOT callers still use the original slot 0; the fixture must prove
both old and new dispatch instead of only appending a method after existing slots.
This is an initial fixture for the wider interface/vtable objective, not a claim
that other interface, inheritance or value-layout changes may remain unsupported.

Correctness is primary. Require direct interface dispatch, an explicit class
implementation, boxed structs, constrained generic calls, delegates, reflection
invocation and GetInterfaceMap to agree with CLR. Preserve all 35 field/evolution
groups and the 220-case golden suite. Call cost, metadata allocation and resident
memory are secondary metrics; no performance conclusion follows from correctness.
Do not depend on pre-touch, FGS or changed assertion values for passing semantics.

The first phase must reproduce the current compatibility rejection using ordinary
C# compilation followed by Unity Current preparation. Preserve the exact DLL and
rejected output. Removing the existing interface/vtable checks is not a repair.
No native capability is advertised until implementation and real Player gates
cover it. The fixture uses DHE_INTERFACE_EVOLUTION_CURRENT; disabling it leaves
the established Base and previous Current declarations unchanged.

## Runtime investigation

Existing native class storage has fixed-size vtable and interface-offset tables.
ClassInlines::GetInterfaceInvokeDataFromVTable uses an interface slot directly in
the native table. New logical MethodInfo objects cannot simply use Current slot
numbers against that Base table. Existing AOT Apply callers must still address
their original slots even if Current declarations are reordered or expanded.

Investigate an immutable logical dispatch map keyed by canonical Base/current
method identity and actual receiver type. Audit interpreter callvirt/constrained,
delegate binding, reflection invocation, interface maps, boxed adjustor thunks,
FGS and generic inflation before choosing the hook boundary. Preserve native
layout and old slots; native callers require explicitly covered lookup hooks.
Publication must prepare complete maps under existing metadata synchronization,
publish once with release/acquire ordering, and roll back failed multi-assembly
registration without leaving partial mappings. A Windows pass is not ARM64 proof.

The current Interpreter GET_OBJECT_VIRTUAL_METHOD and Class::GetVirtualMethod
both reach ClassInlines' slot-based interface lookup. RuntimeType::GetInterfaceMapData
additionally fetches the start of the old contiguous vtable slice and indexes it
using the reflected method count. Adding logical method enumeration without
repairing this consumer can read beyond the original interface slice. Audit the
homologous InterpreterImage/VTableSetUp path too: a new receiver type can implement
an interface that already belongs to Base. Slot selection must retain canonical
type identity while distinguishing native Base slots from logical Current slots.

Unity 2021 uses ordinary bridges and its own supplemental AOT metadata. Unity
2022 and Tuanjie also require FGS and their real headers. After a native repair,
build new Bases for all three engines and replay both managed generations with
common resources, consecutive/skipped updates and full cold differential.
Old v13 Players cannot acquire a new native capability from a manifest.

## Boundaries

No formal source/tag, Installer default or CAT change belongs to this research
step. Keep failures and previous successful Players immutable. Rollback selects
the previous matched source/tool/package locks and compatible resources.
Independent sidecar expansion, non-generic address coverage, general class
virtual-method evolution, value-type layout, Unity-facing behavior, external AOT
API availability and concurrency/GC/performance qualification remain required.
