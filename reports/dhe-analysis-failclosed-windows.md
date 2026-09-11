# DHE compatibility analysis fail-closed gate: Unity 2022 Windows

## Result

The Unity 2022 Windows resource workflow now converts non-fatal analysis
exceptions into an explicit incompatible result. Batch analysis and resource
generation therefore retain a validation record with an `analysis-failed:*`
reason and still return a failing exit status; malformed input cannot produce a
partially accepted resource or terminate before writing its rejection evidence.

The strict `ResourceUpdateCompatibility.Analyze` API remains unchanged for
policy and diagnostic callers. Production-facing paths use
`AnalyzeFailClosed`, which preserves the fail-closed behavior while retaining a
short, deterministic exception type/message in `UnsupportedChanges`.

## Source identity

| Component | Commit |
|---|---|
| Lab implementation used to build the host | `9faac9167bfbc0182fd0d242c1a79c03ee250378` |
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Package | `841abfd46e122343717fe4115186b97a215b58df` |

The source remains on the research branch
`research/dhe-cross-assembly-parents-v8.13.0`; no formal branch, runtime tag,
Installer default, or project was changed.

The report itself is recorded by the follow-up documentation commit
`dd20dd6`; it does not change the tested implementation.

## Evidence

The host was rebuilt with the current lab source and the Unity 2022 package
source. `aot-module-policy` passed every existing module policy check plus the
new malformed-analysis check. The result is archived at
`C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents/policy-failclosed-01/result.json`.

| Artifact | SHA-256 |
|---|---|
| `HybridCLR.DheTool.dll` | `1BB61A43FD849FBC9A0526FCAA241B5CE4A55425972F47FF848A271D31183157` |
| `policy-failclosed-01/result.json` | `01E567B1E553862B6B9FF641336EF7FDB341456B9A971DD90EEA3AE669F2E49E` |

The new check deliberately supplies a null Current snapshot to the strict
analyzer through the fail-closed wrapper. It verifies that the wrapper returns
exactly one incompatible `analysis-failed:*` reason instead of throwing. The
normal module mutation checks continue to pass, so this gate does not broaden
the accepted DHE change set.

## Boundary

This closes reporting and producer robustness for analysis failures. It does
not make unsupported layout, generic-parent, Unity serialization, live-object,
concurrency, performance, memory, or mobile scenarios supported. Such inputs
remain incompatible and must be handled by the existing resource release gate.
