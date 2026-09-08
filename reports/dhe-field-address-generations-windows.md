# DHE stable field addresses: six-Base Windows checkpoint

## Result

Clean replay source `92b7ff4bb23f0fbf1030a5b4a34c00aba6ec553e` passes 18 actual
Windows processes with 18 unique PIDs across six immutable Bases. Each process
executes all 35 required evolution groups and the complete 220-case differential
suite with zero differences. No diagnostic pre-touch or inline padding is used.
The first resource retains the case methods in AOT on all six Bases (zero case
entry receipts); the latest and skipped-latest runs each record 220 interpreted
entry receipts. Both resources contain one common current DLL/MV payload.

There are 792 passing outer structural assertions and 72 explicitly inapplicable
legacy probes. After replay, all 114 distinct protected files were independently
rehashed and match the report. All 24 DLL/MV pairs from these six Bases reproduce
byte-for-byte using the repaired snapshot tool. Archived identities were not
rewritten to accommodate the scanner regression.

This closes the reproduced nullable/field-address failure for this workload,
including generic ref returns, direct/reflection writes, AOT ref/out, reference
and struct aliases, Interlocked, concurrent first access, owner lifetime, and
unreachable owner/cell cycles. It does not establish unrestricted evolution,
sidecar-array expansion coverage, performance gains or mobile qualification.

## Source identity

| Component | Commit |
|---|---|
| HybridCLR | `b950d2947870d2a851165f0ec051891d3a6e66cd` |
| Unity 2021 IL2CPP | `3a0ce4d178033a9edd32b62988ff8a1bf0432150` |
| Unity 2022 IL2CPP | `2cd3264524cf566a315f83222bebf6fdff31cc7a` |
| Tuanjie IL2CPP | `00c3a97144111d8aca6029a6613c3192a9276f46` |
| Unity package | `a8271cd8a1941915fdd7b9487af8feb87bfced1b` |
| Stable-MV Base/resource tool | `19a3f82e4c3d119b6a7b21197d471ee370c8684e` |
| Replay configuration and runner | `92b7ff4bb23f0fbf1030a5b4a34c00aba6ec553e` |

The new evolved Bases use `dhe-runtime-v13`. The three original Bases remain
the archived v11 `base-compiler-original-*` binaries: their current added generic
type is entirely new and does not need an existing-type sidecar capability.
Their capabilities are not promoted to v13 by the resource manifest.
MV remains `DHEMETA1`, schema 1. The unchanged native bytes retain the three
`native-field-address` actual-header compile/CTest passes (`mergeReady=true`,
`surrogateExternalHeadersUsed=false`). No new native result is attributed to the
tool-only fingerprint repair.

| Engine | New evolved BaseId |
|---|---|
| Unity 2021 | `1d6476e732d72116adc220165d71ebab01aecb2e3db11353e3896a28a4db0574` |
| Unity 2022 | `98059c1160c3eb775ead2055b6dec7edb96b9437a68a5406692222b95ec23bfe` |
| Tuanjie | `60ec23504e6429eb9d7864b99f1e3cf6b3ba4db78517764063bdab1aadf81f45` |

## Evidence and reproduction

Paths below are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.
All three `base-field-address-stable-<engine>/project-workflow-report.json` reports
pass the package workflow, no-op Player, schema and native-source checks. The
first and latest payload DLLs are the previously frozen collision/reference
inputs; the repaired MV generator did not modify their bytes or CLR references.
Unity 2021 retains its own per-Base AOT metadata set; Unity 2022/Tuanjie use FGS
without supplemental AOT metadata.

| Artifact | SHA-256 |
|---|---|
| `registry-field-address-stable-generations.json` | `D14661A486E024086BDE4099D6B1672476494826E34D0C382B3F2E0E6870E15B` |
| `resource-field-address-stable-generations-first/dhe-resource-update.json` | `106C5B02856769B67DD6AE7B9591D97FBB8BD3DFBEF5B43BD764B061C0915894` |
| `resource-field-address-stable-generations-latest/dhe-resource-update.json` | `E5A7E42D88E88CD20CADC6EDB29978EA29FD5F2772321FA0AC71C5944245DCC0` |
| `field-address-stable-base-mv-verified/report.json` | `DBAAF24021F8D62ECEF49DF69686A7108E5B5FFE38556E38196153D78B11D348` |
| `replay-field-address-stable-generations-cold/report.json` | `54B51694DD5DB3A7BD59D4A5F84F7186824471951053F91DD680A38EC3CBF85B` |

The committed `manifests/dhe-field-address-stable-generations-windows.json`
selects exact inputs for the existing C# evolution runner. Reproduction needs a
new replay output directory; do not overwrite the archived successful run.
The previous `dhe-field-address-generations-windows.json` intentionally retains
the failed diagnostic inputs and must not be used as this checkpoint's config.

## Remaining work and preservation

Independent sidecar expansion with a live alias and broader non-generic field
address coverage remain open. Existing interface/vtable and value-type layout
evolution, Unity-facing behavior, external AOT API availability, concurrency/GC/
ABI stress and performance/memory qualification remain necessary for the full
Windows handoff. A normal C# resource update cannot repair native limitations in
an already shipped Base. The earlier transient compiler restoration failure is
preserved and its root cause remains unproven.

All older Players, failed replays, incompatible MV variants and stashes remain.
Only ordinary generated input DLLs changed in the three disposable Demo projects.
No formal maintenance branch, runtime tag, Installer default or CAT source was
changed. The candidate toolchain remains Exploratory, not release-ready.
Rollback selects a matched earlier native/package/tool identity and resources
within each Base's actual capabilities; it never rewrites an archived Base.
