# Existing reference interfaces: Unity 2022 Windows

## Result and limits

Two immutable Players consume the same Current DLLs for each of three interface
updates: remove the interface but retain callable methods, remove both interface
and methods, or replace the interface with IDisposable. Both pass the 46 business
cases, explicit cold/cached interface checks, generic/receiver checks, full Unity
serialization/lifecycle and preparation-failure/fresh-process recovery.

Base-74 originally contains ISerializationCallbackReceiver in AOT; Base-73 does
not. The existing-interface removal proof is therefore on Base-74. A separate
positive resource retains the callback interface on Base-74 and adds it on
Base-73; native JSON and clone callbacks still execute on both, cold and cached.

This is an incomplete research candidate. The retained-method removal fixture
keeps valid virtual/final/newslot IL flags. A new compiler-produced variant,
which changes those methods to nonvirtual, is still rejected by admission. It is
part of the required goal, not an excluded case. Parent evolution, broader
declarations and generic/value hierarchies, serialized/pre-existing objects,
native boundaries and performance/memory remain open.

## Source and immutable Bases

The native/package combination is unchanged from the query checkpoint:

| Component | Commit |
| --- | --- |
| HybridCLR | c16abc97f4eecc96dfab2ef0eb8d1c812281cb26 |
| Unity 2022 IL2CPP | 12b0e900bbf0d28246664e6bcb7ead06a76d7826 |
| Package | 39ef40fdd5451a72d1775d246c64b1ae7b93c882 |
| Lab, Base-74 build and initial resource/replays | 888bb5a934a5b15fe60d138c2a12918e30882872 |
| Lab, automatic matching fault source and final recovery | 2b56720 |

The exact source hashes and real-header native compile/CTest are recorded in
`dhe-reference-hierarchy-windows.md`. Native02 has mergeReady=true and
surrogateExternalHeadersUsed=false. No new native source was substituted or
earlier evidence rebound during this validation. The runtime contract remains
dhe-runtime-v32; MV remains DHEMETA1/schema 1. No performance claim is made.

Runtime manifest:
`D:/hybridclr_artifacts/dhe-reference-hierarchy-runtime-02/DHE-Unity2022/runtime-manifest.json`.
SHA: `EFFCD62BC1CB32C6733F7EC68AB5780DC2641E01DCA34D55A4E23E0CC911406E`.

| Base | Location | Base ID | GameAssembly SHA-256 |
| --- | --- | --- | --- |
| 74, callback interface already in AOT | C:/hybridclr_optimize/artifacts/dhe-reference-hierarchy-base-74 | 192fe5bce4188bfcdbae665cb72ace30e9debb8443c2a97d1f6b07d9239abc57 | E1902379D8FD421CFA82E44B414B0751D635C3326115FBB0CE59237BDFD5A0BF |
| 73, no callback interface in Base | D:/hybridclr_artifacts/dhe-reference-hierarchy-base-73 | 012d6f2ea8b0da0e2325b24c886b19bd8747c859e1040e006ddb5496cf6331b3 | 1BB59EBD30A89170475C2B38FCAE74872B66C79AF72A7CAE84B6B92471128BE7 |

Base-74 uses `dhe-reference-hierarchy-interface-base-input-01` on D, revision59,
and all ordinary AOT guards. Its build/startup/generated no-op resource pass.
The actual compiled host is interface-evolution-host01 from a5b3b7b; its SHA is
`0986A7478A90BC346BEBA7D7BE696AB7D0379C9BCC77E59921137142DBE36730`.
Tool hierarchy-tool02 SHA remains
`20A66DBD6904C216B97936304C01EED87465669EAD2ACB389FADF40A74D38250`.

The source-bound replays and resource compiler verify each Base binary's original
hashes. No Player or generated C++ was edited to make a resource pass.

## Shared resource and Player evidence

Unless otherwise stated, paths in this section are relative to
`D:/hybridclr_artifacts`. Resource roots use
`dhe-reference-<name>-shared-resource-01`, with Base-74 before Base-73.

| Name | Current-set SHA-256 | Resource manifest SHA-256 |
| --- | --- | --- |
| interface-remove | 24054b7605e36b5453a04b0cb51640997896a7b9936893222ebff3a68d8ba582 | FAAF98E475F1A500A6156839A1A0B1E53218FECB08B974B75DEAD20E78517C53 |
| interface-remove-methods | 929c63eafcd2b4ce835db1a0aebbf7999916f1de50b5059635822c4caf6bf09e | 6B79CF4ED2FA39D942DCC0E8F3E3004CD22719A7AB7F9F753318098A7EA6A1D0 |
| interface-replace | 5410e1a2863caf5081a75b8d441a033ffac2852e2a2fe8ad2f0c8d5e372920f0 | BBAB0BBA0D51EA9B069717977ED6D7629F090EDB2A50CC4250BE1BF33090B4FE |
| interface-retained | e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24 | 2D9287714AF162FF6AE9842EA362085F272628BA9609008E38210CE78989512C |

Each workflow passes 11 checks and all 46 business cases on both Bases. Each
matching `dhe-reference-<name>-shared-audit-01.json` independently passes two
Bases, two successful runs and 117 bound files. Both Bases already contain all
four hotfix assemblies, so these audits do not include the earlier new-assembly
missing/corrupt/restoration cases; do not count them as four successful runs.

The three evolution resources each have five successful replays per Base:

| Replay | Exact successful checks |
| --- | --- |
| interface variant | 16 interface/dispatch/callback/field/GC/state |
| interface variant with cached Base handles | 11 receivers + 16 interface checks |
| reference-generic-cached | 11 receivers + 18 generic/reference checks |
| full-unity | 11 serialization + 17 lifecycle |
| full-unity-cached | 11 receivers + 11 serialization + 17 lifecycle |

These are 30 replays in `dhe-reference-base<74|73>-<variant>[-<mode>]-01`.
The retained resource has four additional passing
`dhe-reference-base<74|73>-interface-retained-reference-callbacks[-cached]-01`
replays: nine positive callback checks and, for cached runs, eleven receiver
checks. They verify native JSON/clone callbacks are absent after removal and
present when the interface is retained. Initial Base-73-only `*-control-01`
resources/replays remain separate preliminary evidence.

## Recovery fixture correction

The first three `dhe-reference-<variant>-public-probes-01` reports fail the
expected-exception check on Base-74. They reused `dhe-unity-preparation-cycle-01.dll`
from an older Current payload. Native preparation failed in phase1 with no business
entry, but did not reach the expected self-parent diagnostic. Preserve these
failures; they are not successful recovery evidence.

Generating a faulty DLL from the matching remove Current repairs the unchanged
strict oracle on both Players in `dhe-reference-interface-remove-public-probes-02`.
Lab 2b56720 then adds `:current:` to the public-probe host. It generates the
self-parent fault from the bound Current Model DLL, validates unchanged tokens
and versions for all existing types, methods and fields, checks assembly metadata,
and permits only the extra empty self-parent type. Caller-supplied fault files
must satisfy the same checks.

`dhe-reference-interface-fault-mismatch-01` rejects the old mismatched DLL before
starting any Player. This is an intended negative result. All three final
`dhe-reference-<variant>-public-probes-03` pass 16 checks and bind 93 files each:
matching fault input, original Base controls, the full native failure sequence,
no failed-process business effects, original input restoration and complete
fresh-process Current recovery. No exception assertion was relaxed.

Final host: `dhe-reference-interface-evolution-host-02/AotSnapshotTests.dll`,
SHA `49BABA5200C08094A4BB104C9E47BC664713E3296DEF7EB63ACCF6E1E7671EE3`.
It binds 2b56720. Resource replacement occurs between processes; a live process
does not unload and replace an already committed Current assembly set.

## Next reproduced compiler transition

The independent `research/dhe-method-declarations-v8.13.0` lab lane is at 0bd9195.
It compiles ordinary non-interface callback methods with the Unity compiler,
transplants those nonvirtual declarations onto the existing Current component,
and keeps the same behavior and 46 business cases. Its optional 16-check replay
also requires the retained methods to reflect nonvirtual/nonfinal flags.

`dhe-method-declarations-current-01` compiles and passes CLR reference. Model SHA:
`3940F5C211EF4730B85D60DE3E7AD3BABCFB4676A4380278A1208537B0D05CEE`.
Host02 SHA:
`24E26BF1ECA01AB406858AD68BEF1A51DEECB804CC4EDE70FB9889A0B07E649A`.
`dhe-method-declarations-admission-before-01` is rejected before Player execution
for exactly two `existing-method-metadata-change` entries: OnBeforeSerialize and
OnAfterDeserialize. Current-set SHA:
`137a34b0c489ea92c26654c4ff3b21b72dfa8646cbbcaefa8bd45577f2dd740c`.

Admission and Current method-attribute reflection remain to be implemented and
qualified. MonoMethodInfo currently resolves Current return types but reads
flags from the Base descriptor. MetadataModule already exposes
GetDheCurrentMethodMetadata; public attributes and cached method queries need
review while preserving physical receiver/ABI checks. Do not disable the
compatibility gate or advertise this compiler transition as passing.

## Storage, isolation and rollback

Automatic policy review rejected deletion of the newly generated Base-73
WinPlayerBuildProgram `.obj/.pch` cache, giving only `blocked by policy`.
No cache files were deleted. The action was not retried through another tool.
Base-74 was built on C using its available space; earlier Bases, failures,
generated C++, snapshots, inputs and reports remain intact.

All changes remain on research worktrees. No formal branch/tag, remote,
Installer default or CAT source changed. Keep matched source/lock/package sets
for rollback, and Base-specific resource plans. A resource cannot replace the
native runtime of an immutable Player. Windows correctness does not qualify
Android/iOS or the full DHE goal.
