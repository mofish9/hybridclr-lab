# Frozen AOT Entry Verification

This candidate tests Unity 2022 Windows only. It is not release admission,
Android/iOS qualification, or a performance result.

The workload keeps the configured hotfix set unchanged. Ordinary AOT DLL bytes
remain frozen. It builds guards from actual Unity-stripped method tokens, then
loads selected frozen sources together with evolved hotfix images through the
public source-role transaction. Direct `object -> object` native calls exercise
unbox/copy/box and an ordinary inline value owner. Reflection separately exercises
the value-signature Echo method. AOT and interpreter counters distinguish routes.
Only the diagnostic Nullable method inventory is guarded in mscorlib; universal
ordinary-AOT coverage for arbitrary future updates is still an implementation gate.

Required checks: old-layout AOT copy before registration, authenticated Base and
DLL/MV identity, complete selected method guard coverage, atomic source load,
Current field preservation on direct and reflected copies, unaffected ordinary
method staying AOT, unchanged hotfix sentinel, immutable Player/GameAssembly hashes.
No performance sampling is meaningful until these correctness checks pass.

Earlier exploratory failures do not prove ABI truncation: the Echo assertion was
inside a missing optional ResourceEvolutionProbe branch, so it was never executed.
The :evolve: Base revision mismatch was a test argument error, not an unstable
unchanged-method predicate. Both checks are restored, without relaxing runtime
semantics. Resource admission no longer suppresses ABI obligations merely because
a source assembly name is present in a plan.

The independent command is `frozen-entry-workflow` in AotSnapshotTests. Native
failure reports must be retained. A passed probe will still require resource
schema/guard authentication, old-object migration, multi-Base, generic/native ABI
and platform qualification before formal opt4 release. No native code or project
generation output is manually patched by this fixture.
