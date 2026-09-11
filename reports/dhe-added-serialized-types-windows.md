# New serialized types on two immutable Bases

Base117 (old initial asset layout) and Base118 (evolved initial layout) both load
the identical newer Current and AssetBundles successfully. The new inline class,
its array, and a new SerializeReference implementation are absent from both Base
input assemblies. The Player binaries were not rebuilt for this Current update.

## Identities

Unity 2022.3.62f3 Windows x64, FGS enabled. Artifacts:
`F:/hybridclr_artifacts/dhe-asset-evolution`.

- Both Bases: HybridCLR `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`,
  IL2CPP `ecad8a09d1eb9b91a57c59fcdc69b268377bad59`,
  package `ed4b7b52a49373069d1a1336e3f8784278a03b39`.
- Runtime manifest SHA-256:
  `67A5DBCE7267AC48FA13C5F934D6CFFC1D15D8A736DAD3F210BFD2C9810E5A7E`.
- Base117 build lab `6981233`; its identity is in the public-image report.
- Base118 build lab `f55f78f7b81f26feaba3bb93d600f4cf4bd44ed5`;
  GameAssembly SHA-256 `F776D6F4CFD87C14ACB263DFA0E44E246A90C94C48D07F273D097D3E6CA77448`.
- Final Current, authoring and replay lab `913098f28893b8d6c253dabf308a1528f5930a63`.
- Host SHA-256 `C506B51AB84D04E8217353D5EAC25533DDA244D06F9CEC6ABBED2578408CBCEB`.
- Producer remains `db4d17e`, binary SHA-256
  `409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4`.
- Bundle evidence SHA-256:
  `CBD9AE8585C3DC863072C8BFBD0E8E8DA71FC106A2D383A1A0DC1615E7E94CA1`.

## Checked behavior

Current introduces AssetExtension in an inline field and a two-element array,
and AssetAddedNode as a new implementation of IAssetNode in a SerializeReference
field. Callbacks check the authored values and the new implementation's method
result. The same checks run in Awake after the immutable Player explicitly runs
full GC. The gate requires callback records from both saved prefab and scene;
absent all new fields cannot count as success.

| Artifact | Result |
|---|---|
| `noop-base118-01` | Evolved Base's own assets pass 42/42 |
| `added-current-02/input-evidence.json` | New Current compiled with original seed DLLs preserved |
| `added-bundles-02/authoring-result.json` | Source-only project copy; typed bundles built without building a Player; template and Current input hashes unchanged |
| `shared-added-types-02/result.json` | One exact Current, two different Base identities, 46 business cases each |
| `added-base117-gc-01` | 42/42 ordinary asset checks; 3 prefab and 2 scene validated callbacks; both post-GC checks pass |
| `added-base118-gc-01` | Same 42/42 and new-data/GC checks pass |
| `shared-added-types-audit-02.json` | Two immutable Bases, exact original Current, 117 files verified |
| `regression-base118-01` | Business/virtual/framework/generic-parent/generic-owner regression passes: 46/25/18/29/31 |
| `public-probes-02` | Both Bases pass the eight public lifecycle/failure recovery runs with this Current |

Earlier `added-base117-01` passes before the post-GC checks were added. It is
historical diagnostic evidence, not a substitute for the final runs above.
The older uncorrected Base112 fails the new-type asset case at the deserialize
callback. `added-missing-assets-control-01` also fails with the capable Base117
when new code is paired with old assets: new-data validation throws, and the
additional gate has zero successful callbacks. Both failures are preserved.

## Remaining workflow boundary

The stock non-DHE Player also fails old-bundle field renames; see the separate
stock-control report. Latest/rebuilt assets work. A production workflow must bind
Current code and the appropriate assets before activation, rather than relying on
FormerlySerializedAs to repair old Player bundles. The current fixture still
supplies its bundle root separately; atomic single-package delivery is not yet
implemented or qualified.

This establishes the specified new serializable class/array/reference cases, not
all Unity serialization features or automatic save-data migration. Native runtime
source and package are unchanged from the public-image candidate; no new native
compile sweep was needed. No mobile, performance, memory or formal release claim.
