# Base AOT analysis snapshot

Goal: bind the complete AOT input needed for layout impact analysis to the Base,
without making ordinary AOT assemblies hot-updateable. Validate Unity 2022
Windows first. A Current resource must not claim layout compatibility using an
unbound or incomplete ordinary-AOT directory.

Capture all stripped AOT DLLs after the scripts build, including the unchanged
ordinary assemblies. Store immutable content-addressed snapshots beneath the
Base output and include their manifest SHA-256 in the embedded Base identity.
An archive keeps that manifest and its DLLs with portable relative paths. The
resource compiler validates the manifest/DLL hashes and exact AOT inventory,
then analyzes the ordinary subset without registering it as hotfix.

The generated BuildIdentity changes its containing DLL during the final build.
Its hash cannot depend on its own final bytes. Keep the captured original DLL
bytes, and compare them with final stripped inputs after canonicalization:
normalize module build IDs/timestamps and only the generated identity type's
constant values/initialization bodies. Retain that type's definition and member
signatures. Reject an unexpected identity type shape, duplicate identity owner,
an identity in the DHE set, or changes outside the permitted normalization.
The existing staged-source hash check still binds the actual generated C#.

Use one dnlib rewrite algorithm in the package for capture/final comparison;
do not construct an incomplete ad-hoc type fingerprint. It produces analysis
bytes in memory only and never replaces an input DLL. Test determinism with
real Unity stripped inputs as well as explicit method, layout, reference and
identity-shape changes. Tests must also reject missing/extra/tampered snapshot
files. Final Player validation must happen before recording successful native
evidence. A failure must preserve the previous snapshot for diagnosis.

UnityLinker can change the stripped facade inventory between scripts-only and
final builds (observed: a UnityEngine.TextAsset exported type disappears). Keep
this difference visible to normalization. If final inputs or native guards
change, validate the original capture first, capture the actual final inputs,
then rebuild with their identity once. The last build must match both the staged
native identity and full AOT snapshot. Never allow an unbounded retry or accept
the first mismatching Player as the completed Base.

Primary metrics: complete identity/inventory coverage and zero differences
outside the documented normalization. No throughput, memory, ARM64 or release
claim is implied. The snapshot alone does not implement native ABI bridges or
static Current storage; those remain separate required runtime work.

Rollback boundaries: package snapshot/capture and identity changes, lab archive
and resource reader, and dedicated regressions. Keep the layout publication
gate until the snapshot is bound and the public Windows resource workflow is
verified. Do not update formal branches/tags or CAT while this is a candidate.
