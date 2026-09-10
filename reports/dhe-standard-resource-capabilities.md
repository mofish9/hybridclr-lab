# Broader behavior through standard resources

Continue from the immutable Unity 2022 Windows proof-16 and proof-17 Bases.
Compile a Current-only C# suite with the target Unity compiler and captured Base
references, then merge its new definitions into the existing hotfix Model DLL.
The actual business entry invokes all assertions before returning revision 73.
Neither Base nor ordinary AOT source bytes are modified for these new scenarios.

The shared Current payload exercises value/generic copies, Nullable, arrays and
byref, List/Dictionary growth and enumeration, reflection/default arguments,
generic delegates, exception filters/finally, new generic/static/thread-static
storage, cyclic initializers, interface/virtual dispatch and concurrent first
static touch. Require the same Current bytes in both Bases, CLR reference success,
zero Player assertion differences, retained immutable binary hashes and the
existing snapshot rejection/recovery checks. A failing case must remain visible;
do not reduce the suite to obtain a passing result.

These are correctness fixtures, with case progress logging. They do not qualify
production-equivalent throughput, tail latency or memory. New Current-only static
storage is distinct from evolving an ordinary AOT static field that already
exists in a Base; the latter admission gate remains a separate implementation
task. Tuanjie follows Windows qualification. Candidate changes remain in the
research worktrees, with no CAT, formal branch, tag, remote or Installer changes.

## Reproduced direct Nullable failure

Lab `d7c8ee6` compiles all 23 cases with the real Unity compiler; the merged
Current DLL passes the CLR reference. In `resource-03` under
`artifacts/dhe-standard-resource-capabilities-20260910`, proof-16 (PID 10300)
loads the resource and passes ordinary Echo/box/inline/sentinel checks, then
fails the direct Nullable value case. Its corrupted diagnostic argument also
indicates a stack-layout disagreement; this is not passing Player evidence.

The transform recognizes Nullable intrinsics by namespace/name. Frozen method
execution can use an interpreter fallback copy of the generic definition, whose
Il2CppClass lacks the engine's canonical nullable flag and whose castClass is
itself. The intrinsic pushes that class as the underlying value and uses it for
copying. Reflection executes the IL body and did not expose this fast-path error.
Require the actual engine Nullable identity and a distinct underlying castClass
before any Nullable intrinsic. Other definitions execute their IL normally.
Test canonical/alias/invalid class eligibility with real engine headers and
rerun the unchanged resource suite on a newly built immutable Player. Keep the
old failure and Players; no patching of generated code or existing binaries.

Runtime `4ced83d` passes real-header native compile/CTest (`native-02`) and
builds proof-18. Replaying the exact same Current bytes in `resource-04` (PID
17872) passes direct Nullable.Value, then fails nullable boxing with an intact
diagnostic string. The first correction removes the stack-size disagreement;
it does not close the complete nullable case.

Runtime type-token/field resolution currently asks only the outer type's image
to map storage. For Nullable<Payload>, corlib owns the definition while Model
owns the grown argument. Traverse generic arguments, arrays and pointer wrappers
and ask each definition's own image for its execution type. Preserve the generic
definition and all qualifiers when no change is required. Resolve local storage
through the same mapping after generic inflation so copies and box tokens agree
on physical sizes. Signature matching remains in its original logical domain;
the mapping applies at execution use sites. Re-run the complete unchanged suite,
including unaffected generic instantiations, native guards and snapshot rejection.

Runtime `b477922` passes real-header native compile/CTest (`native-03`) and
proof-19 core replay (PID 15080). The unchanged 23-case Current in `resource-05`
(PID 17256) passes both Nullable cases, then fails open-generic-value-copy.
The new Current-only Identity<T> has no selected Base method replacement, but its
MethodSpec still supplies logical Base Payload. Local/type-token mapping alone
does not map that method's physical parameter and return types. At interpreter
call resolution, map both class and method generic arguments, re-inflate the
same definition only when arguments change, then select any Current body. Keep
the shared logical token/reflection cache unchanged. Re-run the exact Current
bytes and existing core native-dispatch assertions on a new immutable Player.

For diagnosis, the same C# suite can select one case using `-dheResourceCase`.
`frozen-resource-cases` launches a fresh Player for every reference case, checks
that exactly that case began and passed, and retains all failures with process
IDs and result/log hashes. Player binaries and all staged resources must remain
unchanged. This separates independent failures from a previous case's damaged
stack or failed initializer. It does not waive the complete default run or the
same-Current multi-Base gate, and older payloads without a selector cannot pass
the isolated-case gate.

`resource-06` compiles the selector-enabled suite from lab `d71eac7`; its Current
Model SHA is `6357038C9C8FAE10DEBF4578F169DF7A222826C22EA73C7199046BE5EFAF2284`.
Its unchanged assertion bodies pass all 23 CLR cases, and the full proof-19 run
still fails open generic copying (PID 16844). `isolated-01` uses host-07 from
lab `68255f6` and this same staged payload. All 23 independent Players finish
without timeout: 15 pass, 8 fail; binaries and all staged resources are unchanged.
Failures: open generic copy, generic value/reference owners, array resize/byref,
List and Dictionary, generic delegate, generic interface/virtual dispatch. The
interface failure is MethodAccessException for IReader<Payload>.Read; other
failures are value assertions. Nullable, both reflection cases, exception/finally,
Current static/generic static/cyclic initialization, constructor defaults,
ThreadStatic and concurrent first static touch pass individually on this identity.
This is diagnostic evidence, not a passing complete suite.

## Complete 23-case resource checkpoint

Runtime `cdb2a5f5ec757326057dae8ae89e056730fe6228`, IL2CPP 2022 `8a13baf`,
and package `187af4f` pass real-header compile/CTest in `native-04`, with
`mergeReady=true` and `surrogateExternalHeadersUsed=false`. Runtime assembly
`runtime-04` is locked by lab `937d834`; its canonical HybridCLR source subtree
hash is `93DC86EA98AD2B4F03FDFD9434991142633E8CD22C94015360FB51E5DA4F4A6E`.
The earlier `runtime-03` assembly attempt failed because the lock incorrectly
hashed the repository root instead of its runtime source subtree; it is not a
passing source identity.

Proof-20 passes Base startup (PID 17844) and the 35-check core replay (PID 19508).
Its Base ID is `b4f0f62a378645fc66729596cc2ceba157d3534f7c277d00aef37d2a15103c6f`,
snapshot `e0ab800349df8b206ccbf36ecc0306788f53b85090ec06153308a0463afe175e`, and
GameAssembly `50B9DA5C111C078A679E424D04F97A1630E7D86900BB4F5E5AA7244ABF55CE31`.
The core replay retains unchanged native/hotfix AOT dispatch and the failed-load
rollback/retry assertions.

`resource-07` reuses the exact original failing `resource-03/current` DLL bytes,
without selecting or removing cases. All 23 cases pass sequentially in the
immutable proof-20 Player (PID 14148), with the exact CLR reference case sequence.
Snapshot substitution is rejected before any load/business entry (PID 11188),
and restoring the original resource passes again (PID 20296). All 8 workflow
checks pass. Current assembly-set SHA is
`4b949bb5679363be96346ea1ef9164bbea442230503f3b81d8897fafb314cedf`, resource
manifest `229B4E2E648D36FF0227F5E0FCB69A946899F1756BD0CC86D0A29688D3117636`.
Host-07 and the full-sequence gate are built from lab `68255f6`; the resource
tool remains tool-03 from lab `a5ab3ad`. The original Current suite source is lab
`d7c8ee6` (Model SHA `790E94EDF7BEA24D7109D69F8A10D3DAA14AB34B7A0625B2591059770AC9FF02`).

This closes these 23 behaviors for one Unity 2022 Windows Base on this exact
candidate identity. The next gate is a second Base with the grown value layout,
consuming the same Current bytes. Ordinary existing static-value storage and
new interpreter-only assembly references remain separate gates. The reusable
SnapshotPlayer fixture currently requires exactly 3 loaded assemblies; a future
new-assembly Base fixture must remove that test-only fixed count before building
its immutable Player. Do not patch an existing Player to bypass that assertion.
