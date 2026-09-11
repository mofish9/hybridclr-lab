# Current host cross-parent removal replay: Unity 2022 Windows

## Result

The current lab host was used to replay the immutable cross-assembly parent
removal resource against two different Unity 2022 Windows Base Players. Both
replays passed the exact 15 cross-parent removal checks, the 18 framework
callback checks, and the existing virtual-signature/business sequence. The
resource and Players were not rebuilt or modified.

This binds the latest host behavior to the existing resource/Player identities;
it does not expand the supported change set. The qualification remains a cold
start replay. Cached objects created before parent removal, generic physical
parent evolution, Unity Scene/Prefab behavior, performance and mobile gates
remain outside this evidence.

## Source and host identity

| Component | Identity |
|---|---|
| Lab source | `658e60a` (research/dhe-cross-assembly-parents-v8.13.0) |
| Host executable | `AotSnapshotTests.dll` |
| Host SHA-256 | `AF6FFA1375C4E13D3DB3CC2BCA7A899C6C449894C7D4EB449B09F1F143BCAFB8` |
| Main tool source | `9faac91` |
| HybridCLR runtime | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Unity package | `841abfd46e122343717fe4115186b97a215b58df` |

## Replays

Both results are under
`C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents` and were written
to new output directories.

| Base | Result | Result SHA-256 | Cross-parent checks | Framework checks |
|---|---|---|---:|---:|
| Base102 | `replay-removal-base102-currenthost-02/result.json` | `D116B0307E4C2E6C788C92338EBEC15C1190441CB0AAC22C9536362C0A6E08FB` | 15/15 | 18/18 |
| Base101 | `replay-removal-base101-currenthost-01/result.json` | `184915BD995CE1DD43233516FB1759FCF30FC155E172BFDE83D0407167483B8F` | 15/15 | 18/18 |

The nested Player logs and stage outputs are retained beside each result. The
earlier parameter-misuse run remains archived separately as a failed invocation
and was not used as evidence.

## Boundary

This is a regression replay for the current host after the fail-closed analysis
change. It confirms that accepted resources still execute on both historical
Base identities. It does not constitute release qualification or prove that an
arbitrary hotfix assembly change can be delivered without rebuilding a Base.
