# Current virtual signatures on immutable Windows Bases

## Result and scope

Unity 2022.3.62f3 Windows now passes the 25 compiler-produced virtual-signature
checks on old-layout Base-88, grown-layout Base-87 and preserved Base-81. The same
Current DLL bytes run on each Player, with 46 business checks per Player. The
old-layout Player additionally passes all six pre-load receiver/cache checks.
The shared resource passes independent identity auditing and public native
preparation failure/recovery. This is a correctness checkpoint, not completion
of full DHE, project handoff, performance qualification or formal publication.

Base-81 has none of the new signature workload types in its AOT input. It is a
new-type interpreter control, not evidence that its older runtime supports
evolution of these existing AOT declarations. Base-88 and Base-87 supply that
evidence with different immutable old/grown layouts.

## Source and build identity

| Component | Tested source |
| --- | --- |
| HybridCLR | fdf1299b44cf5b4d5a6afe41dc449fb89cb888c9 |
| Unity 2022 IL2CPP | 816778af772b67db5080d1569a27e41fbbea7452 |
| Package used by Bases 87/88 | 74b068ca734cbe55d35ecf6c669203ee7393a158 |
| Lab used by Base-87 | f7f222436a22d2bbd95711e6f1037866071f247e |
| Lab used by Base-88 and final replays/audits | 37e5fede75f8ef41683b600161aee7909e5daad7 |

The runtime/lab branch is research/dhe-virtual-signatures-v8.13.0. Package source
is research/dhe-declaration-transitions-v8.13.0. These are candidate sources;
formal maintenance branches, runtime tags, remotes and Installer defaults were
not updated. MV remains DHEMETA1/schema 1 and the contract remains dhe-runtime-v32.

All paths below are relative to
C:/hybridclr_optimize/artifacts/dhe-virtual-signatures unless explicitly stated.
The runtime is runtime-06/DHE-Unity2022/runtime-manifest.json, SHA-256
5863D2E44C33F179DEC3E1C407651EB92CF7D0A51F9B465A1F94E5872975742E.
native-06/DHE-Unity2022/native-gate.json passes compile/CTest with real Editor
headers, mergeReady=true and surrogateExternalHeadersUsed=false.

Canonical native source hashes are HybridCLR
A8EF2032E6E56B7D75AF5253856EDD331B2B2B7D56A5B631F04215DEEC03B9F0
and IL2CPP 4C9DAC6DC4D0F06C7DC1ECAF976A766B5DC237FF8957A90E8630F29340CAB1B4.

| Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 88, old layouts | 3bd63ff236251a6b0ebb7d445f204f4f54484651fba8a5ba5d0b72a65c12fe40 | 2364B61DF6A6FA9CD6A0E64376BDDAD1C24D6B3D46A581E15C631D117D28BA2C |
| 87, grown layouts | 7a140d0e737b9677ebc7bad2d2b7ce95f3e1eaddd873e760cf2660519325bc6d | E5D878A4C113E9AA0E5A7BA9481D67025BDAA32C82D3DF46290A5D9E2D4659FB |

Base-81 is ../dhe-declaration-transitions/base-81, at the earlier native
ec5684e/b1e3952 checkpoint documented in dhe-declaration-transitions-windows.md.
No archived Player or Current DLL was patched.

## Qualified behavior

The exact current-01/current input grows Processor and ReferenceValue fields and
Packet value storage. Its 25 checks cover interface/class virtual dispatch with
reference, value and byref arguments/returns; generic methods and a closed generic
interface; delegates; reflection and interface maps; nulls and exceptions;
repeated calls, four worker callbacks and preservation of input data.

noop-base88-01 and noop-base87-01 each pass all 25 unchanged checks, six unchanged
virtual implementations, positive AOT entry counts and zero DHE interpreter
entries. These guard against obtaining update correctness by interpreting the
whole unchanged workload. They do not establish production throughput gains.

shared-three-base-02 passes 46 business checks on each of Bases 88/87/81. Its
Current-set hash is c3d2911f1e4228560e62433736b1a7f4ee8a8a7701bfc4e99e2b8338d5570906.
Its resource manifest hash is
7E69FD9BD8BE7F5036EB82FC2AA1518E1136B179065B9B177E1927E97BCE2D5A.
shared-base88-probe-02, shared-base87-probe-02 and shared-base81-probe-02 each
pass the complete 25-signature and 46-business sequence, preserving input,
resource and Player hashes. Base-88's cached mode additionally passes:

- stable logical MethodInfo identity;
- allocation of Current receiver storage;
- cached and fresh reflection rejection of the old receiver;
- execution of the new body on a Current receiver;
- preserved old and Current object data after rejection.

Both rejection paths in this run are TargetException. This proves safe receiver
rejection, not automatic object migration. Old data is inspected through a
FieldInfo captured before Current selection. The fresh Current field view is
not allowed to access incompatible old storage.

shared-three-base-audit-02.json passes with 167 rehashed files, three distinct
Bases and three complete business runs. SHA-256:
921E5E2C1764B7A1BDCB2CDE57F4E73016C423A8934A8DD58482F4961CEE748B.
shared-public-probes-02/result.json passes 22 checks and binds 138 files. It
preserves the native preparation exception, verifies no business effects in
failed processes and restores the valid resource in fresh processes. SHA-256:
316103943F14117F795434F006144C25CF7894C8662EC7073239B8985679C96F.

Base-85, using the same native sources, also passes adjacent cached suites:
11 receiver + 11 serialization + 17 lifecycle checks; 11 receiver + 18 generic
checks; and 11 receiver + 9 callback checks. These use the respective
adjacent-base85-full-unity-cached-01, reference-generic-cached-01 and
reference-callbacks-cached-01 outputs. They are not additional runs of Base-88.

## Corrections and preserved failures

Base-82-control reproduces generic MethodImpl startup failure before Current
loads. Selecting the Current declaration owner's generic container fixes it.
Base-83 then passes startup but only 6/25 signature checks. Selecting physical
Current call signatures separately from logical virtual slots raises Base-84 to
24/25. Reflection selects the Current receiver after virtual lookup and before
argument unboxing. Cached dispatch retains logical identity and revalidates the
physical target and frame.

The remaining worker callback has a scalar frame and retains its Base receiver
storage, but its body uses grown locals. The instance scalar allowance requires
exact staged Current owner identity and retained, nongeneric physical parents.
native-05 caught 52 existing negative assertions when the owner proof was too
weak; native-06 passes the original assertions unchanged. Base-85 passes 25/25.
native-02 and native-04 retain earlier compile/link failures.

probe-base85-cached-01 exposed a harness expecting only a wrapped old-frame
exception, while reflection correctly rejected the receiver directly.
shared-base86-cached-probe-01 then passed 5/6 but tried reading old storage through
a fresh Current FieldInfo. These failed results remain unchanged. Base-88 uses
the corrected harness and supplies the complete six-check pass. The mismatched
reference-callbacks-control-cached Base-85 invocation is also retained; the
correct existing-interface mode passes without any workload or runtime change.

## Remaining work and rollback

The package used by these Players predates capability admission for these native
fixes. The separate research/dhe-virtual-admission-v8.13.0 work adds those checks;
until its own newly built Players pass, these results must not be presented as
an end-to-end package admission qualification. Archived Base capabilities are
immutable and must not be rewritten to accept newer resources.

Full DHE still needs broader native callback frames, declaration/parent changes,
Scene/Prefab and old-object semantics, simultaneous publication stress, and
production-equivalent performance/memory measurements. Four worker calls do not
prove concurrent publication or ARM64 memory ordering. There is no Android/iOS,
Unity 2021 or Tuanjie qualification in this checkpoint.

Rollback uses the matching sources and compatible resources from
dhe-declaration-transitions-windows.md. Each Player retains its embedded native
runtime; restart with a resource compatible with that Base. No live unload of a
committed Current assembly set is claimed. Old Players, inputs and failures are
retained. Disk maintenance uses lossless archive compression; no stash was
created or removed and no CAT source was modified.
