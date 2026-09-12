# Constrained Current frame correction

Reproduction: package-delivery structural-05, immutable Base revisions 59/61,
Payload field insertion and a new TLS owner, List<Payload>.Enumerator.Dispose.
The Current assembly executes constrained.callvirt. Its concrete class is
resolved with Current generic arguments, but FindImplMethod returns a logical
vtable MethodInfo which the transform forwards directly to LabelCall. Ordinary
call tokens already pass through ResolveCurrentExecutionMethod; constrained
implementation lookup does not.

Hypothesis: resolve the selected value implementation's Current execution
descriptor before generating its direct call. Preserve the existing concrete
receiver check, boxing fallback, public token/vtable identity, and ABI rejection.
No global relaxation of CanEnterWithBaseAbi is allowed.

Primary metric: real Unity 2022 Windows Player correctness and full 46-case
sequence, zero differential; no throughput or tail-latency claim. Check ordinary
unchanged generic instances, exceptional/finally paths, interface disposal and
the existing two-Base resource suite. Use actual Editor headers for native compile
and CTest. This candidate makes no Unity 2021/Tuanjie/mobile support claim.

Create an isolated runtime worktree from b0fe826, commit before assembly/build,
and bind a separate Lab candidate lock. Do not edit installed or staged runtime
sources. For runtime diagnosis use the already released package 4fb36af and its
local Installer path; this is a runtime experiment, not evidence that the new
ordinary remote Installer supports unpublished native sources.

Existing opt5 tags stay fixed during diagnosis. If a runtime change is required,
publication identity must be resolved before any package can advertise the new
native source as installed opt5. Source candidates and results can be completed
without changing published refs. Rollback is selecting the original clean native
commit and reinstalling/rebuilding the matching Player, not retagging a binary.
