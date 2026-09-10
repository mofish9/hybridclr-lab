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
