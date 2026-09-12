# DHE package tool delivery fixes

The three package-tool review findings are implemented and validated for Windows
project trials. Package maintenance branch `optimize/v8.13.0` is locked to
`ce1b8a89a4e80616dc7343c3fe3f8e405cc2e889`. Its canonical source hash is
`8BF30EF337CBBF48F0FF3C6C1392E1064D4AA9D3FBF08B037E7DFC845F14B852`,
with the existing Git/importer-meta exclusions. This remains upstream 8.13.0.

The DLL was built from clean Lab source commit
`4d29155e6e3467a68a27d642fc71ec65509547b0`; its SHA-256 is
`FE3A1205AFC441B31E5A428E71DA5C879B870BEBC85955382294C7D26EFFDA1C`.
The 83-file bundle ID is
`2ba6dd4cb4083e72ee8722f15975539895f2bf6154e806a2d767a1347caf8da6`.

## Findings resolved

1. **Release publication path.** Source and binary publishers now share
   `ResolveToolPublicationPolicy`: validate mode, clean source HEAD/tree,
   passing evidence header, and the existing complete evidence-file/role/identity
   validator. Binary publication validates before compilation and revalidates
   after compilation, then uses the resulting mode/readiness in the manifest
   and package ID. Evidence hash is recorded in build provenance. No flag or
   validation-source path bypasses this admission. Real production evidence is
   still required; the delivered bundle remains Exploratory.
2. **Tool output protection.** CLI outputs are checked after configuration is
   resolved and before command work. Shared report/output boundaries, state
   writes and resource staging also protect internal/config-derived calls.
   Destinations cannot overlap the actual executing bundle, including its
   ancestors, normalized parent traversal and resolved symlinks/junctions.
   Changing Root does not change the protected bundle. The guard prevents
   configuration mistakes; it is not an OS sandbox against concurrent hostile
   filesystem mutation.
3. **Configuration precedence.** Workflow config is merged before package
   defaults. Explicit CLI still wins over JSON, JSON wins over defaults. The
   repeated workflow merge remains idempotent. Both precedence directions were
   exercised through the actual bundled CLI without starting another Editor.

The package's raw-byte `.gitattributes` rule now explicitly allows CRLF line
endings during whitespace checks. JSON bytes are not reformatted after hashing.
No native runtime, Installer, managed runtime API or game behavior changed.

## Evidence

Evidence root: `F:/hybridclr_artifacts/dhe-package-fixes`.

| Check | Result | Artifact |
|---|---|---|
| New overwrite regression against the previous binary | failed as expected: overwrite returned success | `before` diagnostic copy; production source was not changed |
| Existing and added package-tool regressions | 26/26 | `committed/result.json` |
| Invalid mode, missing/failed/foreign/incomplete Release evidence, evidence ignored in Exploratory | 6/6 rejected before output creation | `publication-result/result.json` |
| Windows directory-junction destination targeting the bundle | rejected; subsequent package verification passed | isolated `bundle-alias` junction |
| Managed execution-plan/public-loading regression | 126/126 | `execution-committed.json`, clean package ce1b8a8 and Lab 4d29155 identities |
| Unity 2022.3.62f3 package invocation | exit 0 | `unity-verify.log` |
| worker3 refresh and bundled tool invocation | exit 0 | `worker3-refresh.log` |
| Current-73 resource generation | passed | `resource-73/dhe-resource-update-validation.json` |
| Previous/current payload comparison | 11 files, zero SHA-256 mismatches | against `dhe-opt5-review/resource-73/payload` |
| Runtime plan comparison | identical hash | `3484C993B7E5E01181257E3FA916EC08297923210202E9B554C8C216573A5CC3` |
| Resource staging and immutable Base/Player checks | passed | `stage-result.json` |
| Tool manifest schema | passed | `manifest-schema.json` |

Recorded evidence hashes:

- 26 tool checks: `52989B6CAF8CAB2220C05BCA8F2C6BD76E44F5D6B2A1BC98C0EEC9E2B531FF64`
- 6 publication rejections: `741BF568102D9DDECD78E2A827E5E84AAD609B191F3E369A3E519E5309E688C5`
- 126 managed checks: `B6D0D0B0C06C739B7AAC066C3621EAD8071ED1AEA2E9792EF2F2B292E1357705`
- worker3 log: `A6752657B5F1FAE2CBC73F2AEB467EC3EBED9A90370F7202560BA395107765B7`

The Release publisher's rejection paths are tested. A successfully qualified
Release bundle has **not** been produced: the full real evidence gate is still
outstanding. This limitation is not converted into a fabricated success fixture
or a weakened evidence policy. macOS/iOS, Android devices and new native Player
execution are not qualified in this change. Prior native Player results retain
their original package/Base identities. No new performance/memory claims apply.

## Delivery and recovery

Runtime maintenance identities are unchanged:

- HybridCLR `optimize/v8.13.0`, commit `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`,
  existing annotated tag `v8.13.0-opt5`.
- IL2CPP `optimize/unity2022-v8.14.0`, commit `658aa64923e568a497e11640b340f316704d02f9`,
  existing annotated tag `v2022-8.14.0-opt5`.

No runtime tags are created or moved, and no package tag is created. Unity 2021
and Tuanjie maintenance are unchanged. Lab locks record package ce1b8a8 while
its binary provenance independently retains its exact source build commit.

worker3 retains `com.code-philosophy.hybridclr@8.13.0`; the five changed package
files and project provenance/maintenance notes were updated. Package source
hash matches the maintenance checkout. Assets remain clean, the removed
`tools/hybridclr-dhe` directory stays absent, and SVN is not committed.
Game DHE adapter/provider/config activation remains outside this tooling fix.

To roll back just these fixes, restore package 02dc136 and its source lock;
worker3's complete pre-fix package is backed up at
`F:/hybridclr_artifacts/worker3-opt5-migration/package-before-tool-fixes`.
Runtime opt5 need not change. The removed source distribution remains recoverable
at `retired-source-toolchain` in the same parent. No stash was created and no
unrelated user edits were discarded.
