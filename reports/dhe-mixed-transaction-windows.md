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

## Initial reproduction

Fixture `a94c365` compiles its host and begins Base-29 on unchanged runtime
`9e7b601`. While that build runs, `preflight-base27` compiles the two real C#
module initializers and passes the unchanged 46-case CLR reference. Execution
planning then crashes in `MetaVersionSnapshot.Create`: it enumerates the module
`.cctor` but omits its `<Module>` declaring type, causing a KeyNotFoundException.
Preserve these compiler outputs. This is a tool defect before native loading.

The fix includes global types which actually contain methods or fields while
keeping empty global types omitted, so earlier MV bytes remain unchanged. Verify
real compiler initializer inventory/body fingerprints and byte-for-byte archived
MV compatibility. This enables the new interpreter-only peers' analysis; it is
not evidence for changing an already AOT module initializer. Native explicit
execution-plan handling of TypeDef row 1 remains a separate boundary to verify.
