# Existing parent removal/replacement: Unity 2022 Windows

The complete DHE objective remains open. This report records the current
correction and preserves failed immutable Players; it is not a release or a
project handoff. Only Unity 2022.3.62f3 Windows is under qualification.

## Latest two-existing-Base checkpoint

The latest runtime now also builds immutable root-only, smaller-layout Base-100.
Both Base-99 and Base-100 pass the exact original removal/replacement Current
sets, and the preserved extended insertion Current. No runtime/package/IL2CPP
source changed: 6180597 / 841abfd / 819f74c remain qualified by native03 with
real Unity 2022 headers. This adds the missing second existing-type Base and
insertion regression; it does not qualify full DHE or production deployment.

Base-100 was built from preserved base-input-97 using clean lab
c90f6153e32c6ba434ff7c2513eadd2e2d605ad3, host03/tool02 from lab85949a2 and
runtime02. All following transition workflows, replays, audits and recovery use
that same lab checkout and those binaries. The input directory name refers to
earlier preparation, not the failed Base-97 build. Base-100 ID:
6fe4431c2a3ff785c933e0f892a796eb4be4ff8f1e9c0ba71f6bf9823fefb707.
GameAssembly SHA-256:
05450C2235E7A20FAED0C04D24D7BD7B71DC68337E78DA82B5FBE36A33C41AAF.
Snapshot SHA-256:
d62d7d655fe945797a3b3e30af97fe75d6fef52d129e77a1ec7a6ecbb5157968.
Build/startup/generated no-op pass. noop-base100-01 passes all 25 checks with
six unchanged implementations, 4,136 AOT entries and zero DHE interpreter entries.
These counts establish routing, not performance. Base-99's earlier no-op evidence
remains bound to its original identity.

| Resource | Base-99 | Base-100 | Old objects / caches | Audit / recovery |
| --- | --- | --- | --- | --- |
| shared-removal-04 | 21 checks | 21 checks | Base99: 29 checks | 121 files; 16 recovery checks / 96 files |
| shared-replacement-04 | 31 checks | 31 checks | Base99: 29 checks | 125 files; 16 recovery checks / 99 files |
| shared-insertion-01 | 28 checks | 28 checks | Base100: 12 checks | 121 files; 16 recovery checks / 96 files |

Every explicit replay also passes 18 framework, 25 virtual-signature and 46
business checks. Base99 originally has Middle; Base100 originally has Processor
directly inheriting Root, smaller Packet fields, and the existing virtual methods.
Thus Base99 proves actual parent removal, Base100 proves insertion, and their
replacement plans begin from different original parent/storage graphs. Base100
is not labelled as a second original-Middle removal proof. Its twelve insertion
cache checks reject incompatible old physical receivers and preserve data;
no automatic migration is claimed.

Explicit outputs are probe-removal-base99-04, probe-removal-base100-04,
probe-removal-base99-cached-04, the corresponding replacement paths,
probe-insertion-base99-01, probe-insertion-base100-01 and
probe-insertion-base100-cached-01. Paths are relative to
C:/hybridclr_optimize/artifacts/dhe-parent-transitions. Each resource uses the
same original DLL bytes across both Bases. Removal/replacement hashes remain
the original values below; insertion uses current-02/current from parent-evolution
with set hash 80cc5c6e51265fbd72706ea152c052c38f5a72c85102cbef92151b3909c37d72.

Independent audit hashes (removal / replacement / insertion):
869BE3FE027D84556CDC3A07AEA6A33F29D5A2224E0958B59D9C0F43EFCA3928,
7B36BD0D4CDCFAC903B1B26CFDBC14447C8286C49F4B924B606A255D927B7D47,
BDCE9EE16FF11FD99C068A51086FC0E5C86C973E514BB6A6C1CE14D8EA34F78B.
The three shared-public-probes-* result hashes in the same order are
5E666BAB1E784B973D3101B9EDA9FF66CE8AFDE4B6643E19652C7CD3C940FF79,
B90CFBEBF0623F1D82EA0B00F3A637E67BD22BCD50328FA955735389F56061ED,
F198DE82F4CEBE618B66AAC072AE3C1CC05BE7EAEFCBE3E560E1FF180821DC72.
Recovery means deliberate native preparation failure followed by recovery in a
fresh process. The full file/Player/resource identities are verified independently.

Twenty-four additional inactive archived Player directories were losslessly
NTFS-compressed before/during the build. All complete file-tree hashes match;
receipts are archive-compression-02.json. No files were removed. C: has about
12 GiB free after qualification; earlier space figures below are historical.

All transition sources remain committed and isolated, with no formal publication,
Installer default or CAT changes. A separate following candidate tests actual
type deletion. Cross-assembly/generic parent evolution, broader Unity assets and
startup state, publication stress, performance/memory and final source-bound
regression remain open. The preceding failures and checkpoints below are retained
under their own identities; later documentation does not rebind those results.

## Previous single-existing-Base checkpoint

Runtime6180597 / IL2CPP819f74c / package841abfd now pass removal and replacement
on immutable Base-99, including the original cached-object failure. Both exact
Current sets that failed on Bases95/96 are reused without changing their bytes.
Base-99 was built at labcea876870974f56f41992d9eefc67b5e5ecd0de1 using host03
and tool02 from lab85949a2. All final replays/audits/recovery also use that clean
lab checkpoint and those binaries. Later report commits do not rebind them.

Base-99 ID: abffce206450aca233e9f72098b3c337cda25b424c7290310df49adaba15465f.
GameAssembly SHA-256:
461C3DF3B45C563D501D75A680DA632C29D7C0205AFEBE5CDC3BC7781DBDB7BF.
Snapshot: d4a28c534a0d2a7755e8743d7b8d11ccbe7d45d125668a4b379343e3114da580.

| Gate | Removal | Replacement |
| --- | --- | --- |
| Base-99 cold parent checks | 21 pass | 31 pass |
| Base-99 original objects/member caches and field validation | 29 pass | 29 pass |
| Framework / virtual signatures / business, every explicit replay | 18 / 25 / 46 pass | 18 / 25 / 46 pass |
| Same Current on Base-81 new-type control | complete sequences pass | complete sequences pass |
| Independent two-Base resource audit | 121 files, 46 cases | 121 files, 46 cases |
| Native preparation failure / fresh-process recovery | 16 checks, 96 files | 16 checks, 96 files |

Base-81 has no original Processor/Middle declarations. Its success is a new-type
control and shared-resource proof, not a second existing-parent evolution proof.
Base-99 provides the actual removal/replacement and old-object evidence. The
29 checks preserve the original fourteen and add old-field writes, unrelated/null
receiver rejection, invalid values, custom Binder behavior without changing
global logical assignability, static/literal/open generic fields and data safety.
No automatic object migration is claimed.

Artifacts are relative to `C:/hybridclr_optimize/artifacts/dhe-parent-transitions`:

- `shared-removal-03`, `shared-replacement-03`: both complete standard workflows.
- `probe-removal-base99-01`, `probe-replacement-base99-01`: cold existing-type runs.
- `probe-removal-base99-cached-01`, `probe-replacement-base99-cached-01`: 29 cache
  checks together with the full parent/framework/virtual/business sequences.
- `probe-removal-base81-02`, `probe-replacement-base81-02`: explicit control runs.
  The attempted `*-base81-01` output names were rejected because those historical
  directories already existed; no old output or Player was overwritten.
- `noop-base99-01`: all 25 checks, six unchanged implementations, 4,151 AOT
  entries and zero DHE interpreter entries. This is not performance evidence.

The Current-set hashes remain
fac561fce0528e05759337bd6ad46f218477b2fc04504229c0a474499dd9dcc9 (removal) and
22422ea2ea897c942bc53c3582ebd6857062bde05da3932b7d94a5c56a1d6c3b (replacement).
Cached Player log hashes are respectively
4DAD0F3302881B1B790BD1AA96B4239D967CE95E93578A4F1995D9C6739B958C and
B3892D3A35919923DE85DE9F3209AD4A8A9847E0901480E75FAC67619E255550.
Audit03 hashes are
1101DDC1DEB99B8F17AF2D18DFA6C0AB906A91068183E8A113A51A49C5F04EE1 and
26F0B2E403E88F5F63B9C2A4D81D38F414BB6ED94E773508694642FAB5DF45B7.
Public recovery result hashes are
55E162F38C5962B44676C2BE008C660951FD71097A66FC9ABBC9CFF2BE617448 and
F380B3CF88A7BEF19F0B201D4F848BF1AC591E77D33809D5065468249FF589B1.

These scenarios are now conditionally qualified on Windows. A second existing
Base with root-only ancestry and different fields, insertion regression on the
latest runtime, type deletion, cross-assembly/generic parents, Unity assets and
startup state, publication stress and performance/memory remain open. Inputs
for the root-only Base are preserved at `base-input-97`, derived from Base-94;
they have not yet been built with the latest runtime. Full DHE is not complete.

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

At this earlier native checkpoint, Player qualification remained outstanding.
Base-97 stops in
Installer CopyFileOrDirectory with only about 0.5 GiB C: space remaining, before
any Player is generated. Its install/process logs and partial project remain.
The intended Player fixture retains the original fourteen cache assertions and
adds fifteen checks for old-field writes, unrelated/null targets, invalid values,
custom Binder execution and logical ancestry, static/literal/open generic fields
and data preservation. These subsequently pass on Base-99 as recorded above.

Base-98 proceeds through Installer and compilation but fails copying
GameAssembly.pdb during BuildScriptsOnly: the build log explicitly reports disk
space exhaustion. Its partial output and log are preserved, with no completed
Base identity or successful Player proof. This does not qualify runtime02.

## Workspace, space and next gates

All changes are isolated candidate commits. No CAT, formal branch/tag/push or
Installer-default changes occurred. Original Current sets, Players and failures
are retained. Projects90/95/96 were losslessly NTFS-compressed, saving about
1.25 GiB each. Attempted removal of Base-97's failed generated project was
automatically policy-blocked; nothing was deleted. Additional archived Player
compression checks full file-tree hashes before/after each directory.
Forty-five archived Player directories subsequently passed those full-tree
checks. The final twenty-five receipts are retained in
`artifacts/dhe-parent-transitions/archive-compression-01.json`; the earlier twenty
are in command receipts. C: has approximately 7.9 GiB available after this work.
No files were deleted and no executable-compression/reparse mechanism was used.

The Base-99 replays/audits/recovery above close the next gate for these two
Current sets. Continue with the second existing Base and the remaining
evolution/Unity boundaries. Keep all failures visible.
Broader deletion/parent/generic/Unity asset and startup-object semantics,
publication stress, performance and memory still require qualification. Windows
results do not establish ARM64 correctness or mobile performance.

Source rollback requires a matched runtime/package/tool combination. Resource
rollback selects a compatible archived release/no-op and restarts. Do not replace
embedded runtimes, modify original ordinary AOT source, or relabel older evidence.
