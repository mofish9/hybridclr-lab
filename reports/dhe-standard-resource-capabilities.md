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
