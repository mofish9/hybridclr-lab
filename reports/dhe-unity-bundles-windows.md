# Unity 2022 typed AssetBundle results

The asset workflow is not qualified. Preserving type trees and mapping four more
native type-query APIs to the selected physical class did not fix nested
serialized reference objects on an old-layout Base. No production or performance
claim follows from the passing native gate.

## Identities

Unity 2022.3.62f3, Windows x64, FGS enabled. Artifacts are under
`F:/hybridclr_artifacts/dhe-unity-bundles`.

- Lab build/replay: `364ae76df4caba0e34d83787838c6cfe39d61444`.
- HybridCLR: `6180597d2c0e455ab09fe0920d34d6dea5ad00fc`.
- Package: `ed4b7b52a49373069d1a1336e3f8784278a03b39`.
- Base113 IL2CPP: `2ff64a8708e9b54d08962c9280d9024240515bec`.
- Base111/112 IL2CPP control: `819f74c08e466a0d2a8fe5b1afaad5b1d784e482`.
- Producer: `db4d17e`, binary SHA-256
  `409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4`.
- Host SHA-256: `BC9B9EC92E3D2256A0428F6CF4DCE3C24321665FD6A97282437D9A103B015B18`.

`native-query-gate-01/DHE-Unity2022/native-gate.json` passes real-header native
compile/CTest: `mergeReady=true`, `surrogateExternalHeadersUsed=false`.
Base113 runtime manifest SHA-256 is
`E2DE3446EC05EC3B9ACACE63AEEFABADEFB3D7D3292E2BB08D1DA693BD4CBF4A`.
Its GameAssembly SHA-256 is
`0A8AA3014F02952F85295026393BDA4F41D36F342EED6A9A4A620F2AABAD3D2B`.

## Results

All bundles preserve type trees. Scene bundles contain a separate bundle-only
scene path, avoiding accidental reuse of the baked scene. Base build results,
bundle-evidence.json, replay result.json and player.json retain complete identities.

| Replay artifact | Code / bundles | Result |
|---|---|---|
| `noop-base113-bundle113-01` | Base113 no-op / own old-layout bundle | 36/36 |
| `shared-two-base-native-query-01` | One unchanged Current on Base113 and Base112 | Standard resource workflow passes both; exact Current bytes and immutable Players checked |
| `current-base113-bundle112-native-query-01` | Base113 Current / latest Base112 bundle | Fails after 6/42 checks, nested State null |
| `current-base113-bundle113-native-query-01` | Base113 Current / own old-layout bundle | Fails after 6/42 checks, nested State null |
| `current-base112-bundle112-native-query-01` | Base112 Current / own latest bundle | 42/42 |

The two Base113 failures throw NullReferenceException in UnityAssetPlayer.Validate
after `prefab:existing-field`, before `prefab:nested-type`. Keep both failures and
the earlier Base111/112 controls. The previous old-bundle renamed-field failure
on Base112 is a separate unresolved case, not evidence that field migration works.

## Next boundary

The four C API mappings alone are insufficient. Identify the native class/field
metadata and serialization traversal Unity actually uses, with targeted diagnostic
tracing and a new isolated candidate if necessary. Do not relax receiver/layout
checks, change expected assertions or rewrite existing Players. No additional
generic/performance sweep is justified for this failed asset hypothesis.

This candidate remains experimental; no maintenance branch, tag or Installer
default was advanced. Use the prior IL2CPP source for new control builds. Existing
Base113 cannot acquire a different native runtime through a resource update.
