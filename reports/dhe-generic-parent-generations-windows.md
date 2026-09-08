# DHE generic parent repair: six-Base Windows checkpoint

Clean replay source `5f27aa778e48af9a8bfe0942356ad099d0290a54` passes all
18 cold processes with 18 distinct PIDs on six newly built v22 Bases. Each run
executes 52 evolution groups and 220 differential cases, with zero differences.
Original and evolved managed generations consume the same two Current releases,
including skipping the first release. All 114 protected files independently
rehash unchanged. This closes the original-generation generic inheritance
regression; the full DHE evolution objective is still incomplete.

## Implementation retained and repair

The earlier implementation and workflow commits remain the foundation. The
previous six-Base failure is preserved in
`reports/dhe-cross-interface-generations-current-windows.md`.

HybridCLR `323dc3a` fixes Current generic declaring types during vtable inflation.
The parent tree uses a Base cache identity but imports Current method records;
those Current open self declarations now bind to a pooled closed Current type
using the actual class arguments. Actual original-generation Players now pass
the unchanged previously failing inherited Apply result (106). Base cache keys,
slot selection, normal generic behavior and AOT/IL eligibility are preserved.
The repair uses existing metadata locks/pools and adds no layout, publication
field or global cache.

Package `fb69cb6` declares `closed-current-parent-vtables-v1`. The analyzer
requires it for new descendants reaching an existing generic DHE parent, also
through intermediate classes; missing cross-assembly Base context is conservative.
TypeParents records dnlib's generic definition name without changing serialized
MV bytes. The runner now retains the process receipt before rejecting nonzero
Player exit codes. MV remains DHEMETA1 schema 1; v22 identifies runtime capability
and source, not a second MV format.

## Source identities

| Component | Commit |
|---|---|
| HybridCLR | `323dc3ad6f0f3eb36a448912a1a6f5f36bee21bb` |
| Unity 2021 IL2CPP | `566a4b270d597bfc5e0a72b83b678fecf7ca3a6e` |
| Unity 2022 IL2CPP | `27dd0ee877bc94a75584378ca8f590ca88753e21` |
| Tuanjie IL2CPP | `6159455972722c94dfc4b4adbedceb62c6905618` |
| Unity package | `fb69cb671278fdc1b400814e62b224e6c1652953` |
| Authenticated C# tool snapshot / native gate configuration | `66de7a82c05b625bc20690b3c25518caf688bb5c` |
| Focused original U21 replay | `a28e7a1ce45b4de2fc864aea84a18d2b1143dc09` |
| Complete six-Base replay | `5f27aa778e48af9a8bfe0942356ad099d0290a54` |

All three real Editor header compile/CTest gates pass with `mergeReady=true`
and `surrogateExternalHeadersUsed=false`. The authenticated C# package verifies
and builds without warnings/errors. Six complete construction/no-op/schema
workflows pass. Unity 2021 uses OptimizeSpeed and its own supplemental metadata;
Unity 2022 and Tuanjie use OptimizeSize/FGS without supplemental metadata.

| Base | BaseId |
|---|---|
| U21 original | `1b2ac8ea9968a1723ea4f46de3fe2ed059a3fcde7eca1439337a3a6403f171df` |
| U21 evolved | `eec2451598feaa9283e00a6bca09b41f78e6009a698d9fc98ad8a4c88a2af65a` |
| U22 original | `366ed98a22b8c84c16afcfb04585e53250958eba1d9e60f5b07fa1df84bc7f1a` |
| U22 evolved | `d696a42dce608517dd8e0ceda1d907d063f0b19feec2d381d9deca82e964b727` |
| Tuanjie original | `09f110443466b58a96abed6aa23573a5e4a655710fc6a40f5b1b27a2ba415135` |
| Tuanjie evolved | `49f65c87e00eeeeb735599924cf67d9243cd79387d3835c65bc968c5c7043552` |

Every Base uses the listed v22 native and package source identities. These are
fresh identities; no old Player has been relabeled or overwritten.

## Evidence

Artifact paths are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

| Artifact | SHA-256 |
|---|---|
| `registry-generic-parent-generations.json` | `8F4EC9B3B32565AA62D77D01AB9A712355013EE7D82E3A78A8A3FE2404D39437` |
| `resource-generic-parent-generations-first/dhe-resource-update.json` | `02774CA5F831F12A3114AEFC67EECE0FF0BA34A509C56A4A7D03FEE2C93AC106` |
| `resource-generic-parent-generations-latest/dhe-resource-update.json` | `B301EE3CC937D8A4ECA114D53C97DDA06531B65920992FC840F14C2912A63E19` |
| `replay-generic-parent-generations-cold/report.json` | `697FD5EED2654E7609FA5DB90A0B25E48470F4840B3F2100F61077EAB25F4BDC` |
| `replay-generic-parent-u21-cold/report.json` | `B361F4085E9F5D3883B477EAD18389506546272A427F02A1B487148CFE74DFCC` |
| `replay-cross-virtual-generations-receipts/report.json` | `634D03252431E5223004021B32C78383EAB8A4E149270FB26A87AE40440E6C16` |

The full replay's first resource retains 208 AOT case entries and interprets 12
conservative fingerprint changes on every Base. Zero entry receipts in this
uninstrumented first payload do not imply all-AOT execution. Latest and skipped
latest runs each record all 220 interpreted entries. No diagnostic pre-touch or
inline padding is used. Original Bases execute 48 outer checks; evolved Bases
execute 40 and explicitly mark the eight absent legacy APIs inapplicable.

Independent checks are `generic-parent-generations-immutable-check.json`
(114 files), `generic-parent-u21-immutable-check.json` (19), and
`cross-virtual-generations-receipts-immutable-check.json` (114).
The retained old-runtime rerun still has nine pass/nine fail, now with all
18 PIDs recorded and unique; this verifies failure receipt retention and does
not qualify those unsafe Bases. The packaged resource command separately rejects
the three unsafe original Bases with exactly three missing-capability errors
and emits no final manifest/plan at `resource-generic-parent-old-bases-rejected`.

Standalone gates: ten generic-parent capability checks, 29 cross-interface
checks and all 24 archived DLL/MV pairs reproducing exactly. Their roots are
`generic-parent-capabilities`, `generic-parent-cross-interface-regression`,
and `generic-parent-base-mv-regression`. Native artifacts are
`native-generic-parent/<profile>/native-gate.json`; the package verification is
`generic-parent-package-verification.json`.

## Remaining scope and workspace

Already-native generic interface definition changes remain untested. General
class virtual/inheritance evolution, value-type layout changes, Unity components
and serialization, additional field storage expansion with live aliases,
external AOT API availability, broader GC/concurrency/ABI stress, RVA-stable
fingerprints, and performance/memory qualification remain required. Windows
correctness does not prove ARM64 ordering or mobile performance. This is a
conditional Windows checkpoint, not a production release or the completed goal.

No CAT, formal branch/tag, or Installer-default change occurred. The six research
source worktrees and three scratch Demo worktrees are clean at the frozen replay
checkpoint. The generated evolved input DLLs remain recoverable in new stashes:
U21 `35d50f918b873c21744cf8ead9e69098f37065a9`,
U22 `dc0a36b6be07de55c17cce59458911c9a8eb9a56`, and
Tuanjie `266904abd35d99aeb32f5aff5db81355a4ec50b7`.
Earlier stashes and failed artifacts remain intact.

Rollback uses an exactly matched previous native/package/tool snapshot and
resources proven for that immutable Base. A resource update cannot repair a
native runtime defect in an already-installed Player. The compatibility gate
must continue rejecting unsupported Bases rather than claiming they were fixed.
