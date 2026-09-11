# Asset provenance resume checkpoint (2026-09-11)

Scope remains Unity 2022.3.62f3 Windows. No CAT integration, engine port,
runtime release, or new Player correctness claim is included in this checkpoint.

## Candidate identity

- Package branch: `research/dhe-asset-provenance-v8.13.0`.
- Package commit: `c88982ca4e0f256f0f5ed648cd6e12150b9fad72`.
- Canonical package tree SHA-256 (using the manifest ignore list):
  `77848162C6E6A6B87EA5C3F8EA8A05EBF2CD6B2C57D552AD92BA70C130AD8AC7`.
- Lab: `worktrees/hybridclr-lab-dhe-asset-provenance-v8.13.0`.

The previous Unity authoring attempt in
`F:/hybridclr_artifacts/dhe-asset-provenance/authoring-01` failed because
`DheAssetBuildProvenance.cs.meta` contained a 33-character GUID. The package
commit above corrects it to 32 characters. All package meta GUID formats
were checked successfully. The C# lab tool builds successfully with zero
errors (the SDK reports the existing net6.0 support warning).

These checks do not prove Unity import or the asset provenance workflow passes.
The three lab locks now identify the corrected package; older Player and
delivery results retain their original identities.

## Next verification

1. Rebuild ExecutionPlanTests and AotSnapshotTests against this package into F:.
2. Run Unity asset authoring with a fresh output, such as `authoring-02`;
   do not overwrite the failed `authoring-01` evidence.
3. Run the asset-provenance managed cases, including serialization schema
   differences, then build a delivery using the generated `asset-build.json`.
4. Replay one delivery against both archived Base119 and Base120 and update
   reports with the exact tested identities. Do not rebuild those Base players
   just because an Editor meta GUID changed.
5. Keep native capability admission as a separate candidate; the public-image
   capability check is still pending.

The existing 126/126 regression and two-Base delivery results are historical
evidence, not results for this new package commit.
