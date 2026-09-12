# Unity 2022 DHE opt5 source release, 2026-09-12

Status: runtime tags published; conditionally qualified for Windows project
trials. This release freezes the existing DHE maintenance implementation without
runtime code changes. It does not establish mobile production readiness or new
performance gains. The package change only updates runtime selection and docs.

## Source identity

| Repository | Maintenance branch | Commit | Annotated runtime tag |
|---|---|---|---|
| hybridclr | optimize/v8.13.0 | b0fe826f071332d109d2bde87c0aa2cc18b9f3c7 | v8.13.0-opt5 |
| il2cpp_plus | optimize/unity2022-v8.14.0 | 658aa64923e568a497e11640b340f316704d02f9 | v2022-8.14.0-opt5 |
| hybridclr_unity | optimize/v8.13.0 | 65581c17c7e67ea8e972fccfa3b84152262fd65c | none; migrate by commit |

Lab locks and tools are maintained on
`optimize/dhe-unity2022-project-trial-v8.13.0` in `mofish9/hybridclr-lab`.
Use the Lab commit containing this report and the synchronized locks, not old
handoff zip files. Toolchain publishing records its exact Lab HEAD/tree.

Canonical source SHA-256 values (LF-normalized content):

- HybridCLR `hybridclr/`: `6E51FF1692784E3106606CE1E7C060EE6A3FB26307B954FDD1B19269B1429FCA`.
- IL2CPP `libil2cpp/`: `45DA48D66495FC3865439E9F99224447FD67384A69E84C181376EEE0F0870887`.
- Package root excluding Git and the existing explicitly ignored importer meta:
  `156734464A7911E18F76F14CF47598FF649236ACC628AE5B7C5945D3F5804853`.

Unity 2021 still uses official `v8.13.0` / `v2021-8.1.0`. Tuanjie retains
`v8.13.0-opt4.2` / `v2022-tuanjie-8.13.0-opt4.1`. Neither is qualified for opt5.
Other engine entries are unchanged.

## Publication checks and evidence scope

Both runtime annotated tags were pushed before the package. Remote tag peeling
was checked against the full commits above. Their tag object IDs are respectively
`f6ad5ea04c14392b302d0369948959213932f987` and
`5c0880c91c73f464063c792a72a1bb8ada384966`.

Fresh `git clone -b <tag> --depth 1` runs using the package's fork URLs succeeded
and returned the pinned commits. This is the clone operation used by Installer;
the cloned runtime subtree hashes match the source locks and previously tested
runtime. Checkout artifacts are in `F:/hybridclr_artifacts/dhe-opt5-20260912`.
The manifests schema gate passed (`schema-gate.json` in that directory).

The existing native gate at
`F:/hybridclr_artifacts/dhe-maintenance-20260912/native/DHE-Unity2022/native-gate.json`
covers these exact runtime commits and source hashes: passed, mergeReady=true,
real Unity 2022.3.62f3 headers, surrogateExternalHeadersUsed=false, FGS enabled.
Its SHA-256 is `B7ACC9FB0303B6C8C0E6C17D54BE9F2A042C2FF04B2CC68EDE3A65704CD24F04`.
Its runtime manifest records the earlier package c6a7aff, not the corrected
8.13.0 package commit; those results remain historical evidence.

The 126/126 managed checks and real Unity Installer's 930-file comparison in
[dhe-maintenance-integration.md](dhe-maintenance-integration.md) belong to package
c6a7aff. This tag-selection-only publication does not relabel those runs as
65581c1 results. The full Base/Player workflow remains historical package
f99bfa4 evidence. No full current-package Player build or Unity Installer UI run
was repeated for opt5; fresh Git tag clone and identity checks were performed.

## Remaining gates and project use

Follow [the project checklist](../docs/DHE-Unity2022-Project-Team-Checklist.md).
Copy the pinned package and use its default Installer. The MV/multi-Base C# CLI
still comes from Lab; generate it with the synchronized locks and verify the
resulting toolchain package before use. A newly published toolchain's manifest
is authoritative for its Lab commit, package ID and qualification mode.

Full current-package Windows Base/Current/multi-Base validation remains required.
Android ARM64 and iOS correctness, performance, memory, tail latency and device
qualification are pending. No method-level AOT speedup is asserted by this tag.
Minimal Unity projects must enable JSONSerialize; its package dependency is not
yet declared. The validated fixture also enables AndroidJNI and AssetBundle.
`formalRelease=false` in the trial lock means qualification is incomplete, even
though the opt5 runtime tags are published.

## Rollback and workspace scope

For the opt5 selection change alone, restore the package at
`c6a7aff28f509144b8654e2a1c37c57f1de34971` and Lab at
`d5f7b31947011cea21d94c85636622a53b08a717`; both runtime commits remain identical.
That older Installer uses moving branches with commit checks, so prefer its
local-source installation from the archived exact runtime commits.

For removal of DHE maintenance integration, use the archived pre-integration
combination in [the integration report](dhe-maintenance-integration.md) and build
a new Base. Existing Players can only roll back to compatible archived deliveries
with a process restart. Do not move tags or relabel Base identities.

Only the three formal repositories and the DHE Lab checkout are in this release.
No user project, unrelated worktree, stash or existing tag was changed.
