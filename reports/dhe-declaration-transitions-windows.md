# Bidirectional declarations and reflection caches: Unity 2022 Windows

## Result and limits

The compiler-produced nonvirtual/interface transitions and optional-default
changes now pass on two immutable Windows Players. A third Player, whose Base
has neither IOperation nor Measure, passes the exact Current that previously
returned a null interface-map target on Base-75. All three consume identical
Current DLLs for each of two resources, with 46 business cases per Player and
independent resource/failure-recovery audits. No archived Player was patched.

This qualifies these declaration, reflection and invocation scenarios on Unity
2022.3.62f3 Windows. It does not complete the full DHE goal or qualify production,
Android/iOS, Unity 2021 or Tuanjie. Broader declarations, parent changes,
generic/value/reference virtual signatures, scene/Prefab and pre-existing-object
evolution, publication-race stress, performance and memory remain required work.
The virtual-lookup correction uses the existing scalar/string/object ABI and
physical-reference-receiver checks; it is not a general virtual ABI adapter.

## Source identity and native gate

All four worktrees use research/dhe-declaration-transitions-v8.13.0. The runtime
contract remains dhe-runtime-v32; MV remains DHEMETA1/schema 1.

| Component | Tested commit |
| --- | --- |
| HybridCLR | ec5684e43d1964ac0c3d130efbf0c9418469555a |
| Unity 2022 IL2CPP | b1e395244ea0f834caf594c46e829add53e760d7 |
| Unity package | 74b068ca734cbe55d35ecf6c669203ee7393a158 |
| Lab, all fixed Base builds and qualification runs | 62092e80322218cff0621090c52ea48f3eb61177 |

The four lab locks match these sources. No formal maintenance branch, runtime
tag, remote, Installer default or CAT file was changed. Use the matched source
combination; copying this package onto an older native runtime does not install
these fixes. The subsequent report commit does not change build identities.

Canonical trees are HybridCLR
926D390BA43A94B7681D2B8E8961DA23AA09AF4D15F33C3C6AE7F393A3564C01,
IL2CPP 570A03F43F1268CE8670FF958C9C2187BEB313B5567B492D3A664237D1C89E58,
and package AA474414B77AF8E71D887EDBF965838042B25871FA9CB2F709FD58962FD623FB.
Package hashing excludes .git and the existing ignored
Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta.

Runtime manifest:
C:/hybridclr_optimize/artifacts/dhe-declaration-transitions/runtime-01/DHE-Unity2022/runtime-manifest.json.
SHA-256: B3F5C7F2B8067AB22F67E4CA49F7181AF1E9692FCCC7BE0E15F30AE5EE5B2F8C.
The sibling native-01/DHE-Unity2022/native-gate.json passes real-header compile
and CTest, with mergeReady=true and surrogateExternalHeadersUsed=false.
Reflection.cpp, RuntimeType.cpp and Interpreter_Execute.cpp are included. This
native result is not an overall release or ARM64 concurrency qualification.

Under D:/hybridclr_artifacts/dhe-declaration-transitions, host-04/AotSnapshotTests.dll
has SHA 734BC357767E6B73A7527FD5A88CFCF37FE639182402180F6DB4E860820038B5;
tool-01/HybridCLR.DheTool.dll has SHA
60CBF81FA7318CDAFE233655A3A25BF412F44A1BAFE8889BF2B1EF72E0314DAD.

## Reproduction and implementation

Base-78-control retains the previously tested I4314160/P8e076ff runtime/package.
Its build/startup/no-op pass. With unchanged current-virtual-7-02/current DLLs:

- probe-base78-declaration-virtual-7-cached-01 fails cached and fresh parameter
  defaults, the interface parameter default and omitted-argument invocation.
  Queries return 3 after selection of default 7. Stable method identity and
  explicit-argument invocation already pass.
- probe-base78-declaration-virtual-7-01 passes the interface map, but both direct
  interface calls reach a Base AOT guard and raise the Current-frame exception.
- Preserved probe-virtual-base75-02 fails interface-map-or-rejection with a null
  target entry. Its interface type was absent from the Base. The nonvirtual-11
  Base-75 replay separately fails to reject an unimplemented interface map.

Parameter cache keys now include the original method, selected metadata method,
execution method and reflected class. Both selected handles are acquired and
rechecked before lookup, so registration between the two reads causes a retry.
The one-way, immutable per-assembly publication has no production ABA reset.
New parameter arrays are constructed before the existing GC-aware append-only
cache publishes them. Old ParameterInfo objects remain immutable snapshots;
requery through the same MethodInfo returns Current information. This behavior
is logged explicitly and is not claimed as in-place refresh of old objects.

Interface maps use the selected physical dispatch class and retain logical
reflection identity. An unimplemented interface is rejected. Interpreter virtual
lookup uses ResolveNativeReferenceInvokeMethod after slot/generic resolution and
on cache hits. The selector validates the concrete argument/return ABI and walks
the actual receiver's physical parent chain. Old-object and changed-value-frame
guards remain enforced. The VM cache remains local to its MachineState; broader
pre-publication virtual-cache and concurrent-publication tests are not inferred
from the method/parameter cache tests below.

Admission and the package inventory add physical-current-interface-map-v1,
current-parameter-cache-selection-v1 and
physical-current-reference-virtual-invocation-v1. old-base-rejection-01 rejects
Base-78 before Player execution for these three missing capabilities. No old
capability inventory was rewritten. A resource cannot deliver a native runtime
fix to a previously built Player.

## Fixed Bases and shared resources

The fixed proofs are C:/hybridclr_optimize/artifacts/dhe-declaration-transitions/base-79,
base-80 and base-81. Each passes Base build/startup/generated no-op, schema and
complete ordinary AOT guard coverage (40 ordinary AOT assemblies).

| Base | Original declarations | Base ID | GameAssembly SHA-256 |
| --- | --- | --- | --- |
| 79 | Nonvirtual methods, default 3, IOperation exists but is not implemented | 1ec7da06cb21cf0f3c085310f6d967614db32ac20cc17ef8c5aa439676928b9c | E5B4F1B4263F6DADF0F1A532BE47E6D4E35C6423C5BEA9F276EBC67677B575B0 |
| 80 | Interface implementation, default 7 | b5cdde7f14691979a6a3489a2ec5aa6d08b0ce5000157d126ee72495a501bc38 | A976D6C338940373A6689941AF3F48FE4E871E32B7645728B0409696F8AE9B64 |
| 81 | Original callback interface/methods, no IOperation or Measure | 7bdf8d37cc1d518705aa4e2bf2ce4e0af3e19cc04085b00d3b13cca7ab8cdb20 | 084AF7E8F66A3D3A01CB571255C5803CF44673FCD70DA38C84A7B4A10B787648 |

Paths in the remainder of this section are under
D:/hybridclr_artifacts/dhe-declaration-transitions.

| Final three-Base resource | Current-set SHA-256 | Manifest SHA-256 |
| --- | --- | --- |
| shared-virtual-02 | 25a59db8431df23a3d3104e6e111777f6d329e659d8e1ac41819aec33fdf83e9 | AD6FDA3FAC01C54B4C93F2D42149D7E4620F7EE8BE84A451229753C0B78A35A1 |
| shared-nonvirtual11-02 | 044f4805ab5198c503351320d25fc8dc94e90eb465ae32a7176534d9cbe62d72 | 7AFCB636774B8904ACBC58900996F5EB20AAA5AC9CDE73E33BC896783D818844 |

Each resource passes all 46 reference business cases on all three immutable
Players. shared-virtual-audit-02.json and shared-nonvirtual11-audit-02.json each
rehash 167 files and verify the original Current bytes. Current sources are
current-virtual-7-02/current and current-nonvirtual-11-01/current respectively.

The earlier shared-virtual-01/shared-nonvirtual11-01 resources contain the same
Current bytes for Bases 79/80. They each pass a 117-file audit. The eight explicit
declaration replays use these two-Base resources, in
shared-base<N>-declaration-<variant>[-cached]-01. Each cold replay passes 17 checks;
each cached replay additionally passes 11 receiver and 12 declaration/parameter
checks. Bound expectations and selected object storage are:

| Base to Current | Flags before/after | Default before/after | Physical Current reference storage |
| --- | --- | --- | --- |
| 79 to virtual-7 | 0 to 352 | 3 to 7 | Selected |
| 80 to virtual-7 | 352 to 352 | 7 to 7 | Not selected |
| 79 to nonvirtual-11 | 0 to 0 | 3 to 11 | Not selected |
| 80 to nonvirtual-11 | 352 to 0 | 7 to 11 | Selected |

Flags 352 denote the compiler's virtual/final/newslot combination. Thus the
parameter correction is exercised both with and without physical storage
selection, and the declaration change is tested in both directions.

Base-81 cold replays use the final three-Base resources and each pass 17 checks,
at shared-virtual-base81-declaration-virtual-7-02 and
shared-nonvirtual11-base81-declaration-nonvirtual-11-02. The virtual map has one
non-null target, the same method identity as normal reflection, and invocation
returns 68. This is the correction of the original missing-interface Base-75
failure. Base-81 has no original Measure or IOperation handle to warm; it is not
counted as a cached-parameter transition proof.

Adjacent generic and full-Unity cached replays pass for both resources on all
three Bases: Bases 79/80 use shared-<variant>-base<N>-<mode>-01 and Base-81 uses
the corresponding -02 paths. Generic mode passes 11 receiver plus 18 generic
checks; full-Unity mode passes 11 receiver, 11 serialization and 17 lifecycle
checks. Positive cached native/direct callback controls pass on Base-79's
preliminary resource and on Bases 80/81 with shared-virtual-02 (nine callback
checks plus eleven receiver checks). No global callback disabling hides removal.

Both shared-<variant>-public-probes-02 workflows pass 22 recovery checks and
bind 138 files across three Bases. They preserve the native preparation exception,
verify no business effects in failed processes, and restore valid input in fresh
processes. Result hashes are
932161DEC2F81E6487BF23E8AA0BD2B5E13E6F705191ACF6AA5F96FFC86583B6 (virtual)
and E0A71749692FEB906580F0B3D52E00D424F9908B3897D601A171179079C75E71 (nonvirtual).
The preceding two-Base recovery runs each pass 16 checks/93 files. These are
distinct evidence sets, not extra runs of the final three-Base result.

policy-01 passes 32 declaration/capability checks, interface-policy-01 passes
26 existing interface checks, and managed-plan-01.json passes 114 managed loader,
plan, source-binding and failure-state checks. Managed native calls are recorded,
not executed; the Windows Player and native gates above supply actual execution.

## Review, archive maintenance and rollback

All reproduced failures described here are resolved in the tested scope. The
ordinary AOT source set is unchanged by resource updates. Cache publication uses
immutable entries and existing GC barriers; physical ABI validation is retained.
There is no performance/P99/memory qualification or simultaneous registration-race
proof, and no inference from Windows x64 to Android ARM64.

Base-77's earlier disk-full failure and Base-78's runtime failures remain archived.
Storage maintenance used lossless NTFS compression of retired project trees and
large files, including C proof-13 through proof-16 and D Base-71/72. Old binaries,
inputs and failure evidence were retained. Compression of C proof-07 through
proof-12 continues separately; this is storage maintenance, not an unfinished
correctness test. No stash was created or removed.

Rollback uses the matched previous source/lock/package combination recorded in
dhe-method-declarations-windows.md. Archived Players keep their embedded native
runtime. Restore only a resource compatible with its actual Base and restart;
there is no live unload/replacement of an already committed Current assembly set.
Do not change immutable tags or rewrite old Base capabilities. The full DHE goal
remains open: next prioritize ordinary virtual signatures and declaration changes,
then the broader Unity/object and performance gates before project handoff.
