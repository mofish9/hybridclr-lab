# Unity 2022 DHE: restore the approved IL2CPP 8.11.0 baseline

The unintended IL2CPP upstream upgrade has been removed from the selected
maintenance line. DHE changes are retained. Fresh Windows compilation, native
tests, managed tool tests, Base/resource Player execution and project installation
pass. This is **conditional approval for Unity 2022 project trials**, not mobile
or production qualification.

## Cause and correction

Merge `658aa64923e568a497e11640b340f316704d02f9` imported official IL2CPP
`v2022-8.14.0` (`11251b938d2ce7fa865165130bf257ca239db69f`) into the DHE line.
It was an actual upstream change, not a tag spelling discrepancy. The earlier
package-only rollback missed this independent repository upgrade.

The existing `optimize/unity2022-v8.11.0` line was fast-forwarded to the complete
pre-upgrade DHE parent `ecad8a09d1eb9b91a57c59fcdc69b268377bad59`. Official
`v2022-8.11.0` (`e00d1d96b1795eb744eb02cd743f702f1cf584be`) is its ancestor;
official 8.14.0 is not. Relative to the wrong release, only
`libil2cpp/vm/Type.cpp` differs: the upstream nested-type-name parser change is
removed (2 additions, 10 deletions). No DHE changes from the pre-upgrade parent
are dropped. The Burst/custom-attribute parser fix from 8.14 is deliberately not
backported as part of this baseline restoration.

The version JSON and authenticated bundled workflow policy now select the
approved 8.11 line. Repository URLs still come from project HybridCLRSettings,
not JSON. Existing Release-evidence, output-protection and configuration-priority
tool fixes are retained. Workspace AGENTS explicitly locks all three upstreams
against automatic upgrades; the tracked baseline policy is
`docs/DHE-Unity2022-Upstream-Baseline.md`.

## Published identities

| Repository | Formal maintenance branch | Commit | Runtime tag |
|---|---|---|---|
| hybridclr | optimize/v8.13.0 | b0fe826f071332d109d2bde87c0aa2cc18b9f3c7 | v8.13.0-opt5 |
| il2cpp_plus | optimize/unity2022-v8.11.0 | ecad8a09d1eb9b91a57c59fcdc69b268377bad59 | v2022-8.11.0-opt5 |
| hybridclr_unity | optimize/v8.13.0 | 4fb36af996871ff1e7ab8585f096395dad5d2bd3 | None; migrate by commit |

IL2CPP annotated tag object is `00fd37ae06bbb69447a00f8a66bb9ec26ccea3f6`;
remote peel was checked against ecad8a0 before pushing the package. The HybridCLR
tag remains unchanged. The old published `v2022-8.14.0-opt5` is superseded and
not selected by current locks; no existing tag was moved or reused.

The package tool was rebuilt from clean Lab commit
`74edbcfc6f2a968fce23c5f194cc3906a1905b75`. Its 83-file package ID is
`b00e802bf163f7029b3675b9491b0dac62a03be11444b7a76dad79e8b3bb4c09`.
Final package/runtime validation ran with Lab commit
`19929b8ee82d9221b4441ae6984cb9a98ff04aba`. Subsequent Lab changes record results
and update guidance; they do not change tested runtime or package source.

| Identity | SHA-256 |
|---|---|
| Canonical package source | A0B5F4BE83DB4731088AFF6EDDC4F475E0E0FCE563F8858143A215100DE6A227 |
| Canonical IL2CPP libil2cpp | 0171A77D4F2509A1FFD4007CD000E9320C11D9EE110507C0D319792EE7926178 |
| Assembled runtime tree | 76693E1901221112CABA207FC2FA09AF870E51DF68BAE956EBE38818F6ED15B8 |
| Runtime manifest | 6524090F8D9FE2E4E9D09374AFB93F48BA30479C674B1DAE6AE2CA71B33CBE60 |
| Actual Unity external headers | D0037168A9A28B05FD8DF11EDA27812DAEAA458DC0CDFAD7765F3BE75E83EE99 |

Canonical package hashing excludes `.git` and the previously documented generated
importer metadata at `Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta`.

## Fresh checks

Evidence root: `F:/hybridclr_artifacts/dhe-il2cpp-811-restore`.
Editor: Unity 2022.3.62f3, Windows x64, Unity2022Fgs, OptimizeSize.

- Real-header native compilation and CTest: 1/1 passed; `mergeReady=true`,
  `surrogateExternalHeadersUsed=false`, FGS tests enabled. This is a fresh binary,
  not a rerun of the 8.14 build.
- Package portable-tool checks: 26 passed. Managed execution-plan checks: 126
  passed; this host records native calls rather than executing them.
- New Base: Prepare, StageRuntimePlan, BuildScriptsOnly and BuildFinalPlayer
  passed. Base revision 59 and generated no-op resource update passed.
- Changed Current: package-owned DLL generated and staged revision 73 using the
  new Base's snapshot/native identity and freshly materialized frozen plan.
  The same Player loaded four DHE assemblies, returned 73 and unchanged sentinel
  5. Base MV, Snapshot.exe and GameAssembly.dll hashes stayed unchanged.
- Installer clone/selection: project-provided repository URLs were used, the
  new tag resolved to ecad8a0, 930 source files matched, and a missing configured
  repository was rejected without fallback. JSON descriptor selects refs only.

The Base/no-op harness uses the freshly built Lab CLI. Changed Current generation
and staging explicitly use the delivered package CLI; the separate portable-tool
suite also verifies that distribution. Neither is described as the other binary.

Base ID: `3887221165bc808e62710a394103520adb9b3551c5fc6b4f47f4bd9901643295`.
Base identity SHA: `1B0E246061239CFB65C19F8CFF9BFCE368981D8BF33EC3EEEBFB6D840BF2DB4F`.
Snapshot.exe SHA: `143DD3C6A93A45819A98E3CCA8FD1535B461CDF860C927D955CA54815FAF5D7B`.
GameAssembly.dll SHA: `B7C46CFFDBEBC4A73B806641859671B19D8FA1A7D4814CF5FB0D98DA450EBD39`.

| Evidence relative to root | SHA-256 |
|---|---|
| native/DHE-Unity2022/native-gate.json | 4423B5002151669B361B6E6AB360EA8014883C60ECBE5C20B374CC31B5376016 |
| tool-tests/result.json | 52989B6CAF8CAB2220C05BCA8F2C6BD76E44F5D6B2A1BC98C0EEC9E2B531FF64 |
| execution-result.json | DF1371877E424C0FFC9A4427859E0E07B0DA6F4F0BA3D890C4D4941F76593F7C |
| base/result.json | E88820D39E66A02F1F1A019E01E346880C8F54CCACFB225DAB1179D52A8283D8 |
| base/player-result.json | 71712723980951510306AC8FA059B17B4458900F7B2848596FC66D8BCC3F80C2 |
| base/resource-player-result.json | 3FC1598BCA83821ED42DE6EDC0C48ACFE274987F009BCE8C35FE48419159CB09 |
| current-player-result.json | 281ECF1520669326575A93AAE8F7C29B10F8758C9F7063A82D76635D216CAC12 |
| stage-73.json | 92F27BA2609F5720C1187D611E9B0A9DF7D54881746E59A5AD118C9D37F5E05A |
| installer-result.json | F6DD0932D1B70B9FD962B5F72A6BACBB7BF01F0511231F93D30001A80465979E |

## Worker3 installation and recovery

`C:/mofish_cat_worker3/cat` now contains package 4fb36af at the unchanged
`Packages/com.code-philosophy.hybridclr@8.13.0` path. Its canonical source hash
matches the formal package. C# `DheRuntimeCommand.InstallRuntime` refreshed the
project and installed the restored runtime, with Unity exit 0 and all 930 source
files equal. The sole additional file is Installer-generated
`hybridclr/generated/libil2cpp-version.txt`; the full installed tree hash is
`46A2D276D1B370A6BB5D85D673C463CC1281CEB00FC6651179B89CACA7F9C90F`.
The installed CLI's `verify-package` also passed.

Installation log: `worker3-install.log`, SHA-256
`727AA4369018C2B5ECF907C8010F1ED777DA6BADF1F5534D0F5CBA115E0FE776`.
Source lock and maintenance instructions were updated. Assets remain SVN-clean;
pre-existing package integration changes remain uncommitted. No SVN commit,
hotfix-set change, game adapter or loader integration is part of this correction.

Recovery backup: `F:/hybridclr_artifacts/dhe-il2cpp-811-restore/worker3-before`,
containing package, libil2cpp, source lock and maintenance instructions. Restore
these as a set only for local recovery; that backup is the superseded 8.14-based
installation, not an approved new-build source. For approved trials, reinstall
the exact published identities above and rebuild a new Base. Do not relabel old
8.14-built Players or overwrite a Base identity to claim 8.11 provenance.

## Limits and remaining gates

Fresh results cover this baseline correction and the described Windows paths.
Historical structural evolution, multi-Base, serialization, asset delivery and
broader Player cases are not reattributed to these new build identities. Those
production coverage gates, project adapter/loader verification, Android ARM64,
iOS, PSS/RSS and tail-latency/performance remain unqualified. No new performance
claim is made. The bundled tool remains Exploratory. Unity 2021 remains official
and is not optimized; Tuanjie work is deferred by user scope.
