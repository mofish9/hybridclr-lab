# Current package across three Unity 2022 Windows Bases

## Result

Base103 was built from the snapshot-safe package and passes the complete Base
workflow, embedded startup, generated resource no-op and 25 explicit no-op
virtual-signature checks. Six implementations remain unchanged; the measured
routing counters are 4,151 AOT entries and zero DHE interpreter entries. These
are routing checks, not a performance benchmark.

One unmodified four-DLL Current payload now passes resource generation, staging
and public loading on Base103, Base102 and Base101. All three retain their
original Player/GameAssembly bytes. The resource has three distinct Base
identities, zero unsupported changes and one Current assembly-set SHA-256:
`251f17ce544b61a553cacfb9b3f0d56e20de97f12d6ac14bbc5e10d97b9ec4a0`.

| Gate | Base103 | Base102 | Base101 |
|---|---:|---:|---:|
| Standard business reference sequence | 46/46 | 46/46 | 46/46 |
| Virtual signatures | 25/25 | 25/25 | 25/25 |
| Framework callbacks | 18/18 | 18/18 | 18/18 |
| Cross-assembly parent removal | 15/15 | 15/15 | 15/15 |
| Snapshot tampering rejected; original resource restored | pass | pass | pass |
| Native preparation failure; fresh-process recovery and component lifecycle | pass | pass | pass |

The parent-removal names and order were additionally compared with the new CLR
reference result, with no differences on any Base. The independent resource
audit passed three Bases, 46 cases, six successful runs and 179 file identities.
The public preparation/lifecycle gate passed all 22 aggregate checks; its nine
Player runs plus two Git invocations are retained as 11 process records.

This is conditional correctness evidence for the tested subset on Unity
2022.3.62f3 Windows. It does not qualify arbitrary hotfix changes, generic
physical parent evolution, Scene/Prefab evolution, old cross-parent object
migration, native publication stress, performance, memory or mobile platforms.

## Source and artifact identity

| Component | Identity |
|---|---|
| Lab workflow execution | `1b22cc558a0b93b316398f59d5717f6295543c24` |
| Package in new Base103 | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Package in archived Base102 / Base101 | `841abfd46e122343717fe4115186b97a215b58df` |
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Base103 ID | `9ee0ed08d2b1f2c718edcb074b66659dfb3603db71db57a0c44fddf06d614af9` |
| Base103 GameAssembly SHA-256 | `4E8A44B7594BA38DBD0C71292216675F51029A5CDAE22F93A3F0AD2267646FA8` |
| Base103 analysis snapshot | `f24e6dfa6411605c6a5147ce042cddefea07d38ee6eb08e767e82448a2fd8393` |
| Runtime manifest SHA-256 | `76FFE0A91201E926E6AF8E3AF4637481F14F6D10F2DAED40654DE052A9CA07DF` |
| AOT snapshot host SHA-256 | `30FB39071CD52F3BC06B1A721883BA144BFA401711C08627785221659481F31B` |
| Tool SHA-256 | `76CF458E6591AC6432AE2D519B94438D59032F3B996D6EB643CB6518D608129E` |
| Shared resource manifest SHA-256 | `2AE70C95667DFA9D2C26B5DE1DCC5EBE64C6BDB6EBCE14D6CF84AD0FF22F0E5D` |

The host was rebuilt at `e0f25a3`; its tool/template sources are unchanged at
the workflow execution commit above. The intervening commit changes only
documentation. The tool was built as described in the snapshot host report.
Source trees were clean when each workflow started. The documentation commit
containing this report does not replace those tested identities.

The real-header native compile/CTest result remains the separately recorded
[snapshot-package native gate](dhe-snapshot-package-native-windows.md):
`mergeReady=true`, `surrogateExternalHeadersUsed=false`, FGS enabled.

## Evidence

Paths are relative to
`C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents`.

| Result | SHA-256 |
|---|---|
| `base-103/result.json` | `325E74C6D1E16D767BD5BB4591E89432A49E615835FE53A94511D18F95519D0E` |
| `noop-base103-01/result.json` | `446C19F5303E07A151FFAC619115E47B56072283EAB7C464F677ECEC88883218` |
| `shared-three-base-01/result.json` | `A9A01BACD6518248F09BAD91D4DE655F2EACBF1F8A728100412856406C4D9CAB` |
| `shared-three-base-audit-01.json` | `30D88B98334CCA2DBCFA0CF74DB54994C0F2CA91FE9A54FB264384D05C81C3BB` |
| `replay-removal-base103-three-base-01/result.json` | `4F4ECC5BB447DB538EC8613A325643211C7F0B5D8895AF21A9EF1F659FEE5091` |
| `replay-removal-base102-three-base-01/result.json` | `1B35327B1424E16DB766796C69CC06DE27C2149506914E24ACDD973FC627CDAE` |
| `replay-removal-base101-three-base-01/result.json` | `B3DE2CEF5448A0CE9C5DA667B6BCB1CE9592EB5FD1A9B3ECC7E93998F67C7466` |
| `shared-three-base-public-probes-01/result.json` | `E2F31652199E803265412C2253077DAE5872DA20BA196B957396D2B60425E29F` |

The CLR oracle is `removal-reference-base103-01/result.json` and uses the Native
DLL from Base103's authenticated analysis snapshot. The unchanged Current input
is `current-removal-12/current`. The shared resource directory contains 27 files
and 44,436,835 logical bytes, including Base-specific support data. That is a
raw file-size observation, not compressed network size or a size optimization
claim. Payload size and deduplication remain relevant before project delivery.

## Reproduction and continuation

The exact Base invocation and source locks are described in the native report.
After that build, run the existing `virtual-signature-noop` command. For the
shared resource use `frozen-resource-workflow` with the ordered proofs Base103,
Base102, Base101; the explicit tool DLL; a new output; and
`current-removal-12/current`. Then run:

- `cross-parent-removal-reference` using that Current and Base103's captured Native DLL;
- `cross-parent-removal-replay` for each Base against the new shared output;
- `frozen-resource-audit` against the shared output and original Current;
- `unity-public-probes` with the same ordered proofs, shared output, `:current:` and a new output.

All commands are C# host entry points. No project integration or shell business
workflow was added. The deliberately malformed DLL is confined to the fixture
and cannot pass ordinary resource admission. It tests preparation failure and
recovery, not permission to publish corrupt resources.

New package Player verification is now complete for these no-op, structural,
business and failure/lifecycle cases. Further work should address the remaining
generic physical parent and Scene/Prefab/old-object boundaries rather than
repeating these unchanged gates. A unified final suite and performance/memory
measurements are still required before a handoff for Android testing.

## Rollback and workspace

All changes remain on the research branches listed in the current status report.
No formal branch, runtime tag, Installer default or CAT project was modified.
To undo the package snapshot change for future builds, revert `ed4b7b5` and the
lab package lock update `e0f25a3` together, or select the preceding matched
candidate. Existing Base binaries remain immutable. Resource rollback selects
a compatible archived resource/no-op and restarts the process; it does not reset
already published native metadata in place.

The four related source worktrees were clean before documentation. No files were
deleted and no stash was created. C: has approximately 5.76 GiB free after the
new build and tests. No Unity build or verification process remains pending.
