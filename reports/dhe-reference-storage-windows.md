# Physical reference storage: Unity 2022 Windows checkpoint

## Result and release boundary

The current candidate passes cold-selection business, public reference identity,
generic field coherence, serialization and lifecycle checks on two immutable
Bases using identical Current DLLs. It remains an incomplete research candidate:
pre-selection component creation breaks subsequent native lookup by the evolved
type on the old-layout Base. Do not publish, enable Installer defaults, or claim
general scene/Prefab, live-object, Tuanjie, mobile or performance qualification.
Unity 2022 Windows is the active target; Tuanjie follows after correctness is
stable. No new Unity 2021 work is required by the current user scope.

## Exact source and artifact identities

All artifact paths in this report are relative to `D:/hybridclr_artifacts`.

| Repository | Candidate branch | Tested commit |
| --- | --- | --- |
| HybridCLR | research/dhe-reference-storage-v8.13.0 | 8eb835acec8cf1aacca1b02730fe1c571905a147 |
| IL2CPP Unity 2022 | research/dhe-reference-storage-unity2022-v8.13.0 | 4b02c37c262b2e52396b52ad74259280b0621d7b |
| Package | research/dhe-evolution-v8.13.0 | 2d7355fb191b0ddee957234e54a5e3feb01be16f |
| Lab build/replay source | research/dhe-reference-storage-v8.13.0 | d69e11e66b1424666ceaa7c8d27f3cabb99c9894 |

Host-08 was compiled at lab `99e5b53`; subsequent lab commits change workload
sources, design and runtime locks, not host code. Host SHA-256:
`A51B47323A21D9BDB4D9482791CDCCC6DE16F7D1F78AC9EC2F1F3BF3A396883D`.
Tool SHA-256: `E28E69072C31B2EA32E9BEE4F52EC7E66955BB5D699E4B5ACA2C1F14FA3C5564`.

`dhe-reference-storage-runtime-06/DHE-Unity2022/runtime-manifest.json` SHA-256:
`F2D0282EB7D0217AAFDDD7577D6E3625FAAB84607CA989F78E3E63B384E1DFD2`.
Native06 passes real Editor headers, compile and CTest with `mergeReady=true`,
`surrogateExternalHeadersUsed=false`. This native gate is not whole-feature
release approval. The experimental runtime contract is still v32; reference
storage capability admission remains unqualified. MV remains DHEMETA1/schema 1.

| Immutable Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| dhe-reference-storage-base-58, original layout | f73c9a984c1b3eac580765a822c9f215cbabc91af41c242d91abe9472138571e | 4AC2775E1B35E3282F3C938F1F85B249C110096D4F2F6F547765CCA7E8279C72 |
| dhe-reference-storage-base-59, evolved layout | 4f013f973e4415390a330ec39e122135371d3ae8811c001e5e8f24882dd4e043 | 9A39EAB87E92B40B731C3EB1B956530586C3E7F241328F762399056818F4C06C |

Both pass initial startup and generated no-op resources. Shared resource06 uses
the four DLLs in `dhe-unity-reference-generic-current-04/current`, unchanged from
the stronger failure reproduction on Base-56/57. Current-set identity:
`35cfd7a4e3ade5516244858c7ee025c4417e4e0350302ecd60e86b704a10071f`.
Resource06 manifest SHA-256:
`6D849BEE2BC4ECC1FFBCF87DA54B746CD4EABC7CC9F343B1839EED5A92F97031`.
Each Base selects its own differential and frozen Base dependency plan. Ordinary
AOT DLLs are retained Base sources, not mutable hotfix input.

## Actual tests

| Gate | Base-58 | Base-59 | Evidence |
| --- | --- | --- | --- |
| Shared resource business, rejection, restoration | pass | pass | dhe-reference-storage-resource-06 |
| Public reference identity | 14/14 | 14/14 | dhe-reference-public-base-58/59-01 |
| Generic, arrays, nested containers, concurrent identity | 18/18 | 18/18 | dhe-reference-generic-base-58/59-01 |
| Serialization and lifecycle | 11/11 and 17/17 | 11/11 and 17/17 | dhe-reference-full-base-58/59-01 |
| Pre-selection cache safety plus generic suite | 11/11 and 18/18 | 11/11 and 18/18 | dhe-reference-generic-cache-base-58/59-01 |
| Cache safety plus native type query/clone suite | 11/11, then 12/14 | 11/11, then 14/14 | dhe-reference-cache-base-58/59-01 |
| Cached full serialization/lifecycle | fails at clone lookup assertion | pass | dhe-reference-full-cache-base-58/59-01 |
| Base controls, native preparation fault, fresh-process recovery | pass | pass | dhe-reference-public-probes-06 |

Every replay checks the full 46-case business sequence and unchanged Player and
staged-resource hashes. The independent `dhe-reference-storage-resource-audit-06.json`
checks two Bases, four successful/restored business runs, three rejected runs and
126 files. Its business audit does not override the separately failed native
query cases. Public probes preserve the actual native exception/preparation phase,
restart-required state and no-publication behavior, then recover in fresh processes
with all 46 business cases and 17 lifecycle checks.

## Corrected generic storage defect

At runtime `02d333e`/`4b02c37`, the stronger identical Current04 passes 16/18
generic checks on Base-56 and 18/18 on Base-57. Evidence:
`dhe-reference-coherence-before-resource-01` and
`dhe-reference-coherence-before-base-56/57-01`.
Reflection construction reports publicType=true, currentCast=true,
reflectedValue=true, directValue=false. Both write directions fail coherence.

GetSupplementalFields registered selected reference generic fields as sidecars,
despite those definitions owning physical Current storage. The correction returns
actual selected physical fields, maps their closed execution arguments and avoids
sidecar registration or Base-context field aliases for selected definitions.
Unselected generic definitions retain their sidecar behavior. Both Bases now pass
all 18 assertions, including reciprocal reflected/direct writes and neighbor
preservation, with the exact failure-reproduction Current bytes.

## Remaining blockers and evidence limits

On Base-58 after pre-selection component creation, GetComponent(publicType)
returns null for the new component and clone, while GetComponent(MonoBehaviour)
finds both. The clone has Value=29 and Extra=91000000031, matching its source.
This narrows the observed failure to native type lookup; the exact engine cache
or API path is not established. The original query remains mandatory; ancestor
lookup is diagnostic only. Same-layout Base-59 passes the cached suites.

Next trace the native type lookup/ancestry APIs on a separately instrumented
candidate, comparing cold and pre-selected paths. Preserve the current physical
receiver checks: Current references may satisfy public Base queries, but old
allocations must not pass as physical Current receivers or accept Current offsets.
Do not infer old-object migration, serialized scene/Prefab evolution, inherited
or generic value identity support from the passing cache-safety checks.

The preceding runtime05/Base-56 diagnostic reports the selected reflected reader
as interpreter=1; the same-layout Base-57 reader and unaffected sentinel report
interpreter=0. AOT totals include reflection wrappers, and the interpreter counter
counts changed DHE entries. These are dispatch diagnostics, not per-method timing
or a general proof that no unrelated interpreter work occurs. Full lifecycle
selection/execution assertions also pass on the latest Base-58/59. Performance,
memory, broader native boundaries and precise release capability admission remain
separate gates. Earlier 106 managed package checks retain their historical source
identity; they were not rerun or rebound to this native correction.

## Isolation and rollback

Only candidate source/test/lock commits were made. No formal branch, tag, remote,
Installer default, package source or CAT project was changed in this checkpoint.
Documentation consolidation after d69e11e does not imply a Player rebuild.
The four active worktrees were clean before source-bound verification. No stash
was created and no files were deleted; at completion C had about 11.2 GiB free
and D about 55.1 GiB. New large artifacts are on D.

Research rollback restores the matched runtime05, lab 99e5b53, Base-56/57 and
resource05 set, retaining its documented generic/native-query failures. Never
replace binaries inside an existing Base or label that failed research set a
production fallback. Existing formal defaults remain the deployment fallback.
