# Unity 2022 project trial preparation

Scope: make the existing structural DHE implementation installable and reproducible
for a real Unity 2022 project. Unity 2021 must use official HybridCLR 8.13.0
(IL2CPP v2021-8.1.0). Tuanjie is deferred and retains its existing runtime selection.

The observable workflow is Base AOT construction, startup selection of Current,
one Current delivery across archived Bases, and verified code/asset activation.
Only assemblies originally configured for hot update participate in DHE; ordinary
AOT assemblies are not made hot-updatable. Their boundary guards still need validation.

Acceptance before handoff:

1. Synchronize the applicable upstream package, integrate the tested candidate and
   preserve the Unity 2021 official selection. Use maintenance branches and new,
   immutable annotated runtime tags; never move published opt tags.
2. Provide the complete C# toolchain, package and runtime identities. A fresh
   consumer must not depend on research worktree paths or an old partial zip.
3. Compile and run native CTest with real Unity 2022.3.62f3 headers. Run managed
   execution-plan, delivery and asset-provenance regression suites against the
   actual package. Build a new Windows Base at the final identity, then validate
   no-op, changed Current and delivery rejection cases.
4. Archive source locks, commands, test results, Base identity and artifact hashes.
   Document project adapter, install/reinstall, build, resource update and rollback.
5. Treat earlier performance samples as historical exploration. No method-level
   AOT speedup, mobile PSS/RSS or P99 claim is required or implied by project-trial
   readiness. Android/ARM64 and iOS correctness remain project/device gates.

Correctness must not regress; no shortened scenario may substitute for a failed
capability. New runtime fixes require a new Base. Existing capable Bases roll back
by selecting an archived compatible delivery and restarting, never by relabeling
their identity. Keep current sources and prior evidence available for rollback.

The previous review found integration gaps but did not constitute a complete
source audit. Freeze/installation checks and actual Player runs are required;
branch membership alone is not proof of correctness.
