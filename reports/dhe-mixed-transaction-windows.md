# Mixed metadata registration, retry and module initialization

Continue from `dhe-frozen-static-resources.md` on HybridCLR `9e7b601`, IL2CPP
`8a13baf`, package `b51473f`. The preceding goal turn made verified progress:
46 cases on two immutable Bases and independent artifact audits. The full DHE
goal remains active. Unity 2022 Windows is first; Tuanjie follows it.

Build a new immutable old-layout Base from Base-27's captured hotfix/ordinary
DLLs and add a dedicated test entry in the lab bootstrap. Reuse the exact four
46-case Current DLLs; add two separately compiled interpreter-only DLLs with
real C# module initializers. Do not rebuild or patch an archived Player.

The native mixed API must reject an invalid Base MV after preparing the hidden
metadata graph, retain old AOT dispatch and field identity, and keep all new
assemblies invisible with no module code run. Changing a pending peer's bytes
or removing a peer must be rejected. The exact graph must retry with corrected
MV, including permuted input order, and then run the complete Current suite.

Both new module initializers must observe the fully committed graph, call an
evolved value through an ordinary frozen AOT accessor, and permit a worker
thread to call the native batch API again without deadlocking. Every initializer
must run once. A separate process deliberately throws from one initializer:
registration has already committed, so this must not be described as rollback
or safe in-process retry. Preserve the failure state and require a cold restart
for application recovery; evaluate public package behavior separately.

Primary metric is zero semantic differences and complete case/check sequences;
secondary evidence is distinct PIDs and immutable Player/source/resource hashes.
No throughput, tail-latency, ARM64 memory or production-ready claim is made.
Run on committed candidate source, preserve any failing Current, and change
native code only after reproduction. New native changes require real-header
compile/CTest and a new immutable Player before replay. Test-only rollback is
lab `b768c91`; this does not revert the preceding static admission.
