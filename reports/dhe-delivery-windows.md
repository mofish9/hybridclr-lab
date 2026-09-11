# Verified code and asset delivery on Windows

One delivery directory now activates Current code and its assets on two immutable
Unity 2022 Bases. The package builder and runtime handle are project-independent
C# APIs. Both Players pass 46 business and 42 asset checks, including newly added
serialized data after GC, using the exact same delivery manifest/files. This is
candidate correctness evidence, not a complete DHE release qualification.

## Identities

Unity 2022.3.62f3 Windows x64, FGS enabled. Artifact root:
`F:/hybridclr_artifacts/dhe-delivery`.

| Component | Source |
|---|---|
| Package | `research/dhe-delivery-v8.13.0`, `cd6ed999f0cf294bf590db87b7e8375cb14115ed` |
| Lab build/tests | `research/dhe-delivery-v8.13.0`, `3eedd7e53ca10fca12a17f67df21c18034ebc713` |
| HybridCLR | `research/dhe-public-image-v8.13.0`, `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7` |
| IL2CPP Unity 2022 | `research/dhe-public-image-v8.13.0`, `ecad8a09d1eb9b91a57c59fcdc69b268377bad59` |

Producer remains `db4d17e`, binary hash
`409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4`.
Native sources are unchanged from the public-image gate; no new native performance
or concurrency result is inferred. Runtime assembly with the new package is
`runtime-01/DHE-Unity2022/runtime-manifest.json`, SHA-256
`6F71B136D05AA56B82391877F198A2BBF0F47E9AC59102EB075EA10E7460B5C2`.

Base119 has the old initial asset layout; Base120 has the evolved initial layout.
Both embed the new package bootstrap API and are built at the exact lab identity
above. GameAssembly hashes:

- Base119: `938AB4766563FE05CDAEDAB74A8AF1CD23733BAF673DF2E67575D8CD9B6B64B8`.
- Base120: `384F1E8325F16F7A29B2169200287895BB236BA32C7B0951069524270BB4FE73`.

Delivery manifest SHA-256:
`0FC1A59A3EAE7454BF18A2D9958589C54B8CC6EB2D0029B23E83674CF8E078CD`.
Player workflow host:
`51DAF8A280E4438AFF25F8D284CA3ACC32E3BF181E2A709E4D4E32821C7F0843`.
Builder/managed-test host:
`15BC82A8B1764D1D0A42C3C95EACDD5C8D068E37B7DC352B2A11DDD5E95B950C`.

## Checks and scope

| Evidence | Result |
|---|---|
| `managed-03/result.json` | 49 delivery checks using the actual package sources and mocked native calls |
| `existing-package-regression-01.json` | 126/126 pre-existing package validation/load-state checks; clean package/lab identities |
| `base-119/result.json`, `base-120/result.json` | Real Unity Editor compile, Base build, immutable snapshot and no-op resource phases pass |
| `noop-base119-01`, `noop-base120-01` | Own assets pass 36/42 |
| `shared-two-base-01` | One unchanged Current on both new Bases; 46 cases each |
| `player-delivery-01/result.json` | Six real Player runs, 21 gate assertions: two valid and four negative runs |
| `shared-audit-01.json` | Original Current, two immutable Bases and 117 code-workflow files verified |
| `regression-base120-01` | 46 business / 25 virtual / 18 framework / 29 generic-parent / 31 generic-owner checks pass |
| `public-probes-01` | Eight existing public lifecycle/failure recovery runs pass on the two new-package Bases |

The same package builder creates a deterministic directory with code/, assets/ and
dhe-delivery.json. An expected manifest hash binds file paths, lengths, hashes,
target/workflow and Current-set identity. Only embedded Base MV may fall back to
the Base provider. No Current files fall back to an old Base copy. Streaming
verification is supported; code metadata reads are snapshotted for consistency.

Managed checks exercise missing/corrupt data, wrong target/Current/digest, unsafe
and duplicate paths, attempted Base MV override, stale/superseded handles, code or
asset mutation before load, asset mutation after load, reentrant/concurrent load
and configuration, retry after restoring an asset, and post-publication native
failure. They record native calls rather than execute IL2CPP.

Real Player negative runs use a missing asset and wrong expected manifest hash.
Both are rejected in prepare, before metadata commit or business entry, without
requiring restart. Fresh-process valid runs then load all code and assets from
the same delivery directory and digest. Asset access goes through the verified
handle and AssetBundle.LoadFromMemory, with no separate bundle-root argument.
All original Player and delivery file hashes are checked before/after each run.
These are fresh-process controls; same-process recovery is currently covered by
the managed suite and the separate existing public lifecycle fixture.

## Remaining requirements

The builder binds the files supplied together; it does not yet verify that an
arbitrary supplied AssetBundle was authored from that exact Current code. A trusted
asset-build provenance record and its checked handoff to the builder remain next.
Native public-image capability admission also needs an explicit build/selection
gate before project distribution; these tested Bases use the locked corrected
native runtime. Earlier Bases without the new managed bootstrap cannot gain that
API through a resource-only update.

No automatic old-bundle field rename or saved-data migration is claimed. Downloads,
release authentication/distribution and asset-framework dependency resolution are
project integrations; the provider must select immutable content and the caller
must obtain the expected manifest hash from its chosen release. Current explicit
asset reads allocate the entire asset bytes; memory/latency benchmarks and final
native concurrency/Windows total regression remain outstanding. ARM64/iOS and
Tuanjie are untested here.

Source is committed on candidate branches, not published maintenance branches or
runtime tags. No Installer defaults changed. Roll back future builds to package
ed4b7b5 and its code-only bootstrap; retain the matching runtime locks. A capable
existing Base selects an archived compatible delivery after restarting the process.
