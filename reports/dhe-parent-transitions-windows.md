# Existing parent removal/replacement: Unity 2022 Windows

The complete DHE objective remains open. This report records the current
correction and preserves failed immutable Players; it is not a release or a
project handoff. Only Unity 2022.3.62f3 Windows is under qualification.

## Original transition evidence

Lab d118fc4 builds Base-95 from the preceding insertion Current, so its AOT
Processor already inherits ProcessorMiddle. Native/package sources remain
155a47d / 819f74c / 125e608. The two original Current sets are preserved at
`artifacts/dhe-parent-transitions/current-removal-01/current` and
`current-replacement-01/current`. Removing the parent retains its type
definition; this does not qualify type deletion.

Removal passes 21 checks, replacement 31, under CLR and in Base-95. Each explicit
Player replay also passes 18 framework, 25 virtual-signature and 46 business
checks. Compatible controls 94/93/81 pass those same sequences. Both shared
four-Base business workflows pass with identical Current bytes per resource.
Their final independent audit/recovery has not been completed.

Both Base-95 cached-object replays fail at RuntimeFieldInfo.GetValue: the old
object physically retains Middle, but its logical Current type no longer does.
The captured mscorlib validates DeclaringType.IsAssignableFrom(obj.GetType()).
Keep the original failure in `probe-removal-base95-cached-01` and
`probe-replacement-base95-cached-01`.

## Frozen reflected-field execution correction

The producer selects the two original mscorlib field accessors when an existing
hotfix parent changes. It authenticates the Base DLL/MV and native entry coverage,
then validates the actual accessor signatures and receiver-validation prefix.
The runtime transform keeps the actual object in place of that prefix's GetType
result and calls Type.IsInstanceOfType. Shared raw IL and MethodBody are not
modified; global Type.IsAssignableFrom, null/static/literal/generic/binder and
physical field access checks remain intact. Unsupported prefixes fail closed.

Runtime1147772 / package2ec66bd / labfc1da5a pass real-header native01 and build
Base-96. Its original startup, generated no-op and explicit 25-check no-op pass;
six unchanged implementations record 4,151 AOT entries and zero DHE interpreter
entries. This is routing evidence, not a throughput measurement.

Base-96 ID: 4422d247b20364fd7576a4a6ae78e3c51cfcc0d015644d8ff0eebe319ee666e1.
GameAssembly SHA-256:
5ACFAEFFF2BE5CF7ECA6F5C4B500FD02C88EBDF201116189A05B5201ACDE62B1.

Both unchanged transition resources fail in Base-96 at the native GetValue entry
with the Current-frame ABI guard (`shared-removal-02`, `shared-replacement-02`).
They do not reach business execution. The frozen image deliberately preserves
public Base declarations; frame validation wrongly required a Current owner
declaration even for unchanged frozen receiver storage.

## Latest native candidate

| Component | Commit |
| --- | --- |
| HybridCLR | 6180597d2c0e455ab09fe0920d34d6dea5ad00fc |
| Unity 2022 IL2CPP, unchanged | 819f74c08e466a0d2a8fe5b1afaad5b1d784e482 |
| Package | 841abfd46e122343717fe4115186b97a215b58df |
| Tool02 / host03 | 85949a2, before later native-fixture-only commits |
| Complete native fixture | b5cd2c359185d070dca34dd493dd53b75d9242d9 |

Frozen instance frame admission now uses the authenticated source and its exact
Current-to-Base method mapping to establish the declaring owner. All physical
parameter/return comparisons and receiver/ancestor storage checks remain. Missing
mapping, mutable source, selected receiver or selected ancestor remain rejected.
The two new capabilities are `frozen-field-object-validation-v1` and
`frozen-base-instance-frames-v1`. Updating a producer cannot retrofit either
capability into an archived Player.

`frozen-frame-control-02` reproduces exactly two assertion failures against
unchanged runtime01; it finishes without a crash. `native-03` passes the same
complete fixture against runtime02, with real Editor headers, FGS,
mergeReady=true and surrogateExternalHeadersUsed=false. Runtime02 manifest SHA:
FD5271A5FAE7CCBDE2823A6A57CDDD08AA82FAE1A7A96EBD68D2ABD92C4C0EC0.
The earlier control01/native02/diagnostic01 retain a fixture cleanup bug which
disabled simulated metadata before later tests, causing a crash. Those runs
are not passing gates and are superseded only by the stated fresh outputs.

`field-policy-01` passes 19 checks at lab7816027. `field-policy-02` passes 20 at
lab85949a2, validating real Base-96 mscorlib, original-byte/MV preservation,
malformed-prefix/signature controls and both capability requirements.
`old-base95-rejection-01` and `old-base96-rejection-01` reject their original
resources for the respective missing capability before any Player execution.

Player qualification for runtime6180597 remains outstanding. Base-97 stops in
Installer CopyFileOrDirectory with only about 0.5 GiB C: space remaining, before
any Player is generated. Its install/process logs and partial project remain.
The intended Player fixture retains the original fourteen cache assertions and
adds fifteen checks for old-field writes, unrelated/null targets, invalid values,
custom Binder execution and logical ancestry, static/literal/open generic fields
and data preservation. None are claimed as a Player pass yet.

## Workspace, space and next gates

All changes are isolated candidate commits. No CAT, formal branch/tag/push or
Installer-default changes occurred. Original Current sets, Players and failures
are retained. Projects90/95/96 were losslessly NTFS-compressed, saving about
1.25 GiB each. Attempted removal of Base-97's failed generated project was
automatically policy-blocked; nothing was deleted. Additional archived Player
compression checks full file-tree hashes before/after each directory.

Next: build a new immutable Player with runtime02, replay the exact two Current
sets cold and cached, then finish compatible multi-Base resources, independent
identity audits and preparation failure/recovery. Keep all failures visible.
Broader deletion/parent/generic/Unity asset and startup-object semantics,
publication stress, performance and memory still require qualification. Windows
results do not establish ARM64 correctness or mobile performance.

Source rollback requires a matched runtime/package/tool combination. Resource
rollback selects a compatible archived release/no-op and restarts. Do not replace
embedded runtimes, modify original ordinary AOT source, or relabel older evidence.
