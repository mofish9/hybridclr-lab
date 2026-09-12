# Project-owned runtime repository selection

> Historical identity. For the corrected IL2CPP 8.11 baseline, current package
> and fresh validation, see [dhe-il2cpp-811-restoration.md](dhe-il2cpp-811-restoration.md).
> Results below retain their original identity and are not current build evidence.

Package `optimize/v8.13.0` commit `5122957ee8e6647f1663205c3286ccc2e99c1e70`
restores the historical Unity 2021 optimized-ref convention: version JSON selects
branch/tag; project HybridCLRSettings selects each repository URL. Neither
repository overrides nor commit pinning are stored in the version descriptor.
Exact source identity remains in release locks and DHE build provenance. Git
clone errors remain fatal; there is no silent fallback to another repository.

Unity 2022 references still select `v8.13.0-opt5` and `v2022-8.14.0-opt5`.
Their commits remain `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7` and
`658aa64923e568a497e11640b340f316704d02f9`. Runtime branches and tags are unchanged;
Unity 2021 remains official. Package upstream remains 8.13.0; no package tag.
The bundled tool binary and its source commit/manifest ID are unchanged.

Canonical package hash (existing Git/importer-meta exclusions):
`D13FD4723E1698880E03F584A0AD9AD3E08E5D9BDA84CDB74D60F92F37B4CACC`.

Real Unity 2022.3.62f3 ran the committed repository-selection fixture from Lab
`1d08071453382387cbd7620e61faa5238bf1bdc1`, using local Git mirrors as the project
repository settings. It verified clone origin URLs, exact selected tag commits,
the branch-only version descriptor, and all 930 assembled runtime files against
the previously validated opt5 runtime. A missing configured repository was
rejected without fallback. Only the disposable fixture's clone staging was used;
project settings were restored in memory. No new Player was built.

Evidence: `F:/hybridclr_artifacts/dhe-repository-settings/result.json`, SHA-256
`3621EC60916A6439CB59631453E959F299EDDBBF22C374642B456929ADA508E5`.
Unity log: `unity.log` in the same directory. Previous tool/runtime/Player evidence
retains its original identities; this is Installer selection verification only.

worker3 received the three changed package files plus updated provenance/notes.
Its existing repository URL settings were already correct and were not modified.
The package folder retains `@8.13.0`; DHE game integration was not advanced.
Refresh/tool verification log: `worker3-refresh.log` in the evidence directory.
No SVN commit or stash. Pre-change package backup:
`F:/hybridclr_artifacts/worker3-opt5-migration/package-before-project-repository-settings`.
Restore that package and its ce1b8a8 source lock to undo this configuration change;
runtime tags/installations need not change. Android/iOS qualification is unchanged.
