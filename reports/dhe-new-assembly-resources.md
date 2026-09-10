# New assemblies through frozen-source resource updates

Continue the Unity 2022 Windows standard-resource checkpoint on HybridCLR
`cdb2a5f`, IL2CPP `8a13baf` and package `187af4f`. Build immutable old-layout and
grown-layout Bases with the three original hotfix DLLs. A later shared Current
adds an interpreter-only DLL while those original assemblies retain DHE modes.
Ordinary AOT DLLs remain the captured Base bytes. No project-specific loader or
changes to generated source/archived Players may hide missing runtime support.

The fixture must accept an explicit expected Current assembly count, report the
public loader's differential/interpreter sets and actual loaded names, and use
the existing public loading APIs. Begin with differential plus frozen sources,
then load new interpreter assemblies; reproduce any metadata dependency or
ordering failure before changing the package/runtime protocol. New resource
tests must cover cross-assembly values/generics, new declaration dependencies,
virtual/interface calls and an ordinary frozen boundary, in addition to the
existing 23 C# cases. Require a CLR reference, exact case sequences, original
Base hashes, complete mode tables and one identical Current across both Bases.

Missing/corrupt new DLLs and attempts to reclassify original ordinary AOT code
must be rejected. Keep per-Base support assets and original snapshot source
authentication. Research sources must be committed before source-bound runs.
No performance, mobile, Tuanjie or production qualification follows from these
Windows correctness cases. Native sources are unchanged until a real failure
requires a correction; the prior real-header gate remains tied to runtime-04.

## Reproduction and implementation boundary

Lab `0d20eba` compiles the four-DLL Current in `current-01` using the real Unity
compiler. All 31 CLR reference cases pass. Proof-22 (fixture lab `895543d`) starts
successfully with the original three differential DLLs. Its `resource-01` run
(PID 19200) is rejected at `load-differential`: AddedBase from
HybridCLR.ValueLayoutAdded cannot be resolved. No business entry executes.
The tool has correctly selected three differential and one interpreter-only
assembly, so this is a metadata initialization ordering failure, not an assembly
count assertion or compiler error. Preserve this exact Current for the fix.

Extend the native metadata transaction with interpreter-only peers. Parse all
inputs first; allocate hidden images, prepare all definitions, then resolve all
signatures/layouts under a thread-local preparation scope. Publish new assemblies
only after successful DHE registration, with the metadata lock held through
registration so name lookups cannot observe the intermediate state. Run new
module initializers after the complete metadata graph is available. Retain
failed metadata allocations and bind retries to the same peer names/hashes;
never silently reuse a partial graph with different DLLs. New assembly names
must be absent from the Base/runtime assembly inventory, including ordinary AOT.

New interpreter image declarations must use Current representations of evolved
value types. Member lookup compares logical type identities across Base/Current
representations without weakening physical ABI/layout checks. A package helper
will accept the complete Current set, authenticate it, and pass differential,
frozen and new interpreter images through the native batch. Existing APIs remain
available; MV schema stays 1. Add a capability so older Bases reject resources
that need this new mixed transaction rather than reaching an unresolved parent.

The initial `1d564ec` native gate (`native-01`) found an access modifier error in
the new prepared-reference helper. `236ce15` exposes that loader operation and
is the next candidate. The first managed-host run from an artifact directory
also found a fixture-only schema path assumption; the runner now accepts an
explicit lab root. Neither failed attempt is passing runtime evidence.

`native-02` compiles the candidate sources, then finds two missing standalone
test-VM definitions at link time (metadata lock and interpreter registration).
The test VM now supplies those engine contracts, using the real target lock
type, and checks that failed MV registration keeps a new assembly unpublished
while a valid retry publishes it with both differential peers. This does not
replace the real Windows Player gate. The relocated managed runner passes all
81 existing validation/argument-selection checks in `managed-03.json`; its
native calls are recorded, not executed.

## Next qualification boundary

Keep the exact 31-case Current bytes from `current-01`. First replay them on
proof-23 with the committed mixed loader. Then build a second immutable Base
whose hotfix AOT inventory already includes Added, with Base revision 59. The
same resource must select Added as interpreter-only on proof-23 and differential
on the second Base. Generalize only fixture input discovery and expected counts;
do not modify either archived Player or its project to retrofit this capability.
Also remove and corrupt a new DLL in disposable resource staging, require
rejection with zero business revision, restore the exact bytes in `finally`,
and rerun all cases. Record complete per-Base mode sets and immutable hashes.

The old proof-22 admission check in `old-base-capability-01` rejects the preserved
Current with `base-missing-runtime-capability:mixed-interpreter-source-batch-v1`.
This is expected negative evidence, not a Player pass on that old runtime.

Proof-23 on runtime `04a0502` passes Base startup (PID 5488) and all 35 core
frozen-source replay checks (PID 10192). However, `resource-02` (host lab
`5d4dfee`, PID 19452) crashes before any case executes. Its symbolized stack
points to the new RawImage parser's loop scope exit in RuntimeApi.cpp:379.
RawImageBase owns and frees the input passed to Load; the new caller incorrectly
gave it the interior of a managed byte array. Runtime `9e7b601` passes CopyBytes
instead, matching the existing assembly loader's ownership contract. Preserve
the crash log and exact Current, compile the new source identity, and build a
new immutable Base before replay. Proof-23 cannot be relabeled as a pass for
new interpreter-only assemblies.

## Parser fix: first complete 31-case Player pass

Runtime `9e7b601` passes real-header native compile/CTest in `native-04`. Proof-25
passes Base startup (PID 20152) and all 35 frozen core checks (PID 18984). Its
Base ID is `963bfbc32c20ef8716de1f4d9d1755255538a756bf72af45ce686c9314011eb1`;
snapshot `e49bdd8f3345322b09fbea76e7759a11118f3a201e90acb7c7b06e89f0f55a81`;
GameAssembly `EB1D7AE14FC87771114120F946566948E4F53EFC5A06389C5E39AA980BF9434E`.
`resource-03` passes all 12 workflow checks on that unchanged Player. PID 17264
executes all 31 cases in CLR reference order, revision 73, with three DHE DLLs,
Added interpreter-only, and original Native/mscorlib frozen sources. Missing
and corrupt Added are rejected (PIDs 19612/13384), then the restored resource
passes (1060). Snapshot substitution is rejected (20132) and restoration passes
(8368). Exact Current hash is
`6dceb3f58b95121220ad0358c20e7c2016f68a485ceb5c138df91ae792ea333b`; resource manifest
`0751178572C97A11D94C79D5AA173F78E902130FE2B52E4E2A6EF944EFC90FC7`.
Host-04 is from `5d4dfee`; the resource run records lab `b325d7f` and tool-01.

## Base DLL identity after repeated Unity linking

Base-24 includes all four hotfix DLLs as AOT and passes startup (PID 17932,
revision 59), but its no-op resource is rejected: Model's frozen baseline hash
differs from its captured final AOT source. Preserve this failure. In
`base-24-source-compare-01`, all 35 type and 83 method MV records, including their
tokens/versions, are identical. UnityLinker reordered compiler-generated
assembly attributes and their TypeRef/MemberRef rows. Sorting just those assembly
attributes, with the existing MVID/timestamp normalization, produces identical
complete bytes (SHA `C44EA277068EC0E38530A0C9EFD2342DE57FB98214928A8B86E3ADE2E2615B38`).

Introduce a conservative comparison only for DHE baseline versus captured DHE
source: keep raw-hash fast acceptance; otherwise require identical definition
tokens and complete normalized DLL bytes after ordering a fixed set of compiler
assembly attributes. Keep business attributes in place and reject duplicate
compiler attributes. Do not normalize ordinary frozen sources or alter their
authenticated payload hashes. Base MV/current hashes and native coverage remain
independent requirements. Regress method bodies, fields, compiler attribute
values and business attribute order before replaying the original Base-24.

Lab `61304b3` passes all 11 `baseline-linker-identity-01.json` checks. The original
Base-24 now accepts its generated no-op resource (`base24-noop-01/player.json`),
without rebuilding the Player or changing captured files. `resource-04-multibase`
gets past that binding, then rejects Added's assembly metadata: the Unity C#
compiler emits AnyCPU IL with PE `LargeAddressAware`, while UnityLinker emits
`Bit32Machine`. Both retain I386, ILOnly, and neither requests/prefers 32-bit CLR
execution. Ignore only those two native PE hints when forming an AnyCPU IL
assembly's compatibility shape. Preserve the CLR architecture flags, machine,
module/assembly identity, attributes and resources. Test that boundary before
retrying the identical Current on the existing Bases.
