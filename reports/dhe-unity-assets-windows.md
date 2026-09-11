# Archived Scene/Prefab checkpoint: structural update failure

## Finding

Real baked Scene/Resources assets expose a remaining release blocker. Both
immutable Players build and pass no-op asset validation. The same latest Current
loads and executes the 46 business cases on both. It passes all 39 asset checks
on the evolved-layout Base108, but fails on old-layout Base107 after four checks:
the component's nested State is null after Unity reports incompatible binary
serialization layout. This checkpoint is not an asset compatibility pass.

| Gate | Base107 (old asset layout) | Base108 (evolved layout) |
|---|---|---|
| Base build/startup/generated no-op | pass | pass |
| Real Scene/Prefab no-op | 33/33 | 39/39 |
| Shared Current business | 46/46 | 46/46 |
| Shared Current asset checks | fail after 4/39 | 39/39 |
| Virtual/framework/generic-parent/generic-owner replay | 25/18/29/31 pass | 25/18/29/31 pass |

Unity logs on Base107:

```text
The list of [SerializeReference] objects being deserialized is from a more recent version of Unity.
A scripted object (probably HybridCLR.Lab.UnityAssets.AssetBehaviour?) has a different serialization layout when loading. (Read 72 bytes but expected 236 bytes)
```

Both files actually report engine `2022.3.62f3`. Read-only serialized-file header
inspection (format 22, platform 19) confirms `typeTreeEnabled=false` in both
`resources.assets` and `level1`, on both Bases. The newer-Unity warning therefore
does not establish an engine-version mismatch; it accompanies decoding old
layout bytes through the new layout. A compatible-layout control succeeds.
The metadata inspection reports `parsed=true`, not a correctness pass.

The no-op/successful asset probes cover inactive Component identity, authored
scalar values, nested serializable state, lists, SerializeReference nodes and
their methods, Unity object references, serialization callbacks, cloning,
independent storage/GC, Awake and additive scene unloading. Current renames a
nested field with FormerlySerializedAs and adds fields to the component, nested
state and managed-reference node. The failing old-layout run never reaches
those later assertions; their success must not be inferred from Base108.

Full Player file hashes and original Scene/Prefab source hashes are preserved and
checked by replay. The independent standard-resource audit passes 117 files,
two Bases and two successful business runs. It does not certify the failing
asset replay. The CLR reference checks business/generic behavior only, not Unity
serialization.

## Source and evidence

Source branch: `research/dhe-unity-assets-v8.13.0`. Both Bases and Player replays
bind lab `046bbbd0df98eeb16799a7b61c10094f551ec065`. Host03 was built at `af5df49`;
the subsequent commit changes only the copied Editor scene preparation source.
The metadata inspection uses host04 at `770b4a2`. Later documentation does not
replace these tested source identities.

Runtime/package are unchanged: HybridCLR `6180597`, Unity 2022 IL2CPP `819f74c`,
package `ed4b7b5`. The explicit producer is the generic-owner tool at `db4d17e`,
SHA-256 `409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4`.
Host03 SHA-256 is `CD4DD22A6E16AA72DD4D0E6B165FE08FED422DDD8FBC122E25EF460CA25AF52B`.
Current set SHA-256 is `faf7e1fc89bfb619c3a060bfe6a503d7aad17301acff30da52b4939bce05b7fd`.

Artifact root: `F:/hybridclr_artifacts/dhe-unity-assets`. Existing C: proofs were
not moved. F: was selected for new large builds because it had ample free space.

| Result | SHA-256 |
|---|---|
| `base-107/result.json` | `E380CF09B31D27810A812FF0ABE679F5C8835FDF2A024B0F1A553E36F622078D` |
| `base-108/result.json` | `B7A6672DAA0555BC80457FB51615C44A381BB5B7A1C56BEC9A687E95D0CBCF34` |
| `noop-base107-01/result.json` | `75AECFB54693A3D3CB4DC365D995CDE22BC40B90414F1D3C1291450FB7065037` |
| `noop-base108-01/result.json` | `585355FC16D8C7B9970B049E0CFFFAF22ACD2BDE9CAD048F635F50A6F8FEBB91` |
| `shared-two-base-01/result.json` | `361856435EF14396496E90D2A1CF61C9BDBE26B4E2D2D497D1CCC1EFBB43B48C` |
| `current-base107-01/result.json` (failed) | `ACA7E13087AE3C16073DC2D902B09A0D48FF17093E9DB40AD99FF5A421C99095` |
| `current-base108-01/result.json` | `39A27E32A55D3F720F28F33C3A24C265C33C7D47B9795C63DA4707FB504DCCA0` |
| `shared-audit-01.json` | `4EDDF27F3FB6F3B8D146406D84512B7FE60669676C58746A7555BC169762B663` |
| `type-trees-base107-01.json` | `3827C89FF4863EDEC8E4D0217DE77A90AD48B7BDB4275AA4EC4CBC3DEE327BCF` |
| `type-trees-base108-01.json` | `7F516E8EF05853E1C0DA4B2FCFD53A3E0B2EF0902D570D07EC8B818868DF446F` |

Base105 is the retained failed Unity import: the merged fixtures referenced
their own assemblies. The helper now normalizes exact self references only in
copied fixture inputs and new merges. CLR/generic reference tests still pass.
Base106 is the retained failed asset-preparation attempt: each batch Editor can
start in an untitled scene. The source now explicitly opens the saved startup
scene before authoring the additive scene. Neither generated project was edited
to repair the failure, and no old artifact was overwritten.

## Next implementation step

Do not declare the full DHE workflow ready while serialized hotfix assets can
silently reach this failure. Next test AssetBundles retaining type trees and
latest asset content, so one hotfix delivery can contain both the Current DLLs
and compatible assets without rebuilding archived Bases. First compare an old
typed bundle against the same Current, then a latest bundle against both Bases.
Only measured results can establish which path is supported. Include AssetBundle
APIs in the next Base fixture's linker roots; the current probes use Resources
and SceneManager, and must not acquire new native APIs by changing old identities.

This is not permission to impose a new project restriction as a substitute for
the goal. If baked Resources/scenes cannot consume changed layouts directly, the
workflow needs an asset packaging/loading solution or an explicit actionable
build gate, with the compatible-asset delivery path demonstrated. Do not attempt
to hide the problem by skipping fields, changing expected values or labeling the
standard resource audit as an asset pass. Pre-selection objects and save-data
migration remain separate boundaries.

All source changes are local candidate commits. No runtime tag, maintenance
branch, Installer default, CAT or mobile change was published. No process remains
live at this checkpoint. Source is clean after documentation; no stash/deletion.
Rollback uses the original no-op/compatible asset and code set, then restarts the
Player. The broader goal remains active.
