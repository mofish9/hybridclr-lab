# Current virtual signatures on fixed AOT Bases

Continue the qualified declaration checkpoint with ordinary managed reference,
value, byref and generic arguments/returns through interfaces and class virtual
methods. The previous scalar-only reference invocation selector must not be
mistaken for a general interpreter call-frame adapter. Full DHE remains the goal.

Compile one C# workload with old and expanded layouts for a receiver, payload
class and payload struct. Both versions contain the same interface and virtual
method declarations before entering AOT; this is essential to reproduce dispatch
through retained logical Base methods. A third, preserved Base with none of these
types supplies the ordinary new-type interpreter control. Current DLLs are shared
without edits between all supported Base proofs.

Check interface and class calls, typed reference identity, value copies and ref
mutation, generic method/interface dispatch, delegates, reflection, null receivers,
exceptions and repeated/concurrent calls. Keep the original 46 business checks;
run the additional pure-managed suite both under CLR and in the real Windows
Player. Failure must retain the exact Current inputs and result, including failed
partial sequences. Use guards and physical receiver/signature evidence to choose
Current execution; do not disable old ABI checks or force unaffected code to
interpret globally.

First test the new-type control on a preserved capable Base. Then build a Base
with the old declarations using the currently qualified runtime, reproduce actual
failures, and fix them in isolated runtime worktrees. Commit sources before native
compile/CTest and Player builds. Qualify old/grown Base layouts against the same
Current resources, adjacent declaration/Unity behavior and recovery checks, with
independent source/input/binary audits. This checkpoint makes no throughput,
P95/P99 or memory claim. Release/acquire, caches and object lifetimes need review;
Windows x64 does not establish ARM64 concurrency correctness.

Use Unity 2022.3.62f3 Windows only. Keep DHEMETA1/schema 1 and C# workflows; do not
modify CAT, formal branches/tags, remotes or Installer defaults. Ordinary AOT
source bytes remain immutable, with existing frozen-IL adaptation where needed.
Scene/Prefab, pre-existing-object evolution, broader declarations/native boundaries
and performance remain required follow-up work, not exclusions from the goal.

Rollback selects the matched sources/locks from
reports/dhe-declaration-transitions-windows.md and a compatible resource for each
immutable Player. Preserve old Players, failures and input DLLs; archive space can
be conserved through lossless compression. No in-process Current unload is claimed.

## Reproduced startup failure and first correction

The preserved `artifacts/dhe-virtual-signatures/base-82-control` builds with the
preceding declaration runtime but fails native metadata preparation before any
update executes. `IPacketOperation<Packet>.Copy`, an explicit generic interface
implementation present in the original AOT inputs, cannot resolve. Its
`player-result.json` records zero loaded assemblies and `load-current-batch`.
Do not count the successful native build as a passed Base or rewrite this proof.

`ResolveMethodDefinition` selects a staged Current declaration table while
retaining the Base generic container for signature matching. VAR ordinals must
resolve in the selected declaration owner's container; the downstream logical
interface type and closed instantiation remain unchanged. The first candidate
corrects only that owner selection, without weakening generic identity or ABI
checks. Rebuild into a fresh output with the exact `base-input-old-01` inputs;
startup/no-op must pass before testing the unchanged `current-01/current` update.
The existing new-type control passing all 25 checks does not establish this fix
or the subsequent old-layout virtual call-frame behavior.

Base-83 at HybridCLR `60b78c3` passes startup/no-op. Its four processed Base
assemblies must remain byte-identical to Base-82's. The fixed Current resource
passes all 46 business cases and a 67-file independent audit. The explicit
virtual-signature replay passes six checks and fails nineteen, all invocation
paths reaching the old AOT frame guard (the concurrent check aggregates the same
worker exceptions). Preserve `probe-base83-01` and its exact Current payload.

The next correction separates call-site signature selection from logical
virtual slot lookup. Abstract declarations also need Current parameter/return
metadata before the interpreter sizes its stack. Dispatch must continue to use
the logical method/slot, then select an implementation compatible with the actual
physical receiver. Interpreter calls validate the selected frame signature;
reflection selects the implementation after virtual lookup and before unboxing
arguments. Raw native invocation retains its existing conservative ABI selector.
Keep native virtual fallback available for abstract declarations; selecting a
Current declaration does not imply every derived implementation interprets.

The next Base also includes an opt-in no-op probe invoking the unchanged compiler
suite directly after loading its generated no-op resource. Its Base revision
remains 59, so this does not claim the separate 46-case Current business suite.
Require all 25 named checks, six unchanged virtual implementations, positive AOT
entries and zero DHE interpreter entries; verify the original Player and staged
resource hashes. This guards against gaining update correctness by interpreting
the entire unchanged virtual workload.

Base-84 at HybridCLR `9208965` / IL2CPP `816778a` passes native03,
startup/no-op, all 25 unchanged checks (4,136 AOT entries, zero DHE interpreter
entries), the 46 Current business cases and a 67-file independent audit. Its
updated signature probe passes 24/25. All four ThreadStart callbacks fail at the
entry guard before entering their try blocks; this is not evidence of a dispatch
cache race. Preserve `probe-base84-01` as the remaining failure.

The worker closure retains its Base object storage and has a void/no-argument
signature, but needs an interpreter body because its locals use grown types.
The guard classifier currently admits only static scalar frames. Extend it to
non-generic reference instance scalar frames only when the declaring class and
every physical parent retain Base storage. Value/byref/custom-type arguments and
changed receiver storage remain guarded. Re-run the unchanged and full update
suites, including old-receiver rejection controls, before qualifying this rule.

`virtual-signatures-cached` additionally captures an original AOT Processor and
its MethodInfo before Current loading. It requires cached/fresh reflection to
reject this old receiver, a newly allocated Current receiver to invoke the body,
stable logical identity and unchanged data after rejection. This is separate
from the previous Unity component cache suite and only applies to the old-layout
Base. Both modes still require all 25 signature checks and all 46 business cases.

Native05 caught 52 failures in the existing receiver/ABI rejection matrix: an
image without a matching Current declaration was insufficient evidence for the
new scalar allowance. The refined rule requires the exact staged Current owner
definition of the selected method, rejects generic parents, and checks physical
storage selection along the parent chain. Keep those original assertions intact.
Native02 (exception-helper namespace) and native04 (test-link dependency) are
preserved failed compile attempts; neither has a passing Player claim.
