# DHE interface slots: six-Base Windows checkpoint

## Result

Clean replay source `4eef487d0bfe74fc03d04095633a8aa3e3228fd1` passes 18 independent
Windows Player processes with 18 unique PIDs across six immutable Bases. Each
process executes all 42 evolution groups and 220 differential cases with zero
differences. There is no diagnostic pre-touch or inline padding. The first case
payload stays AOT on every Base (zero entry receipts); latest and skipped-latest
runs each record 220 interpreted case entries. These are correctness fixtures,
not production-equivalent performance samples.

The interface fixture inserts Added before Apply in Current metadata while
existing AOT callers retain the old Apply slot. It covers explicit class and
implicit boxed-struct implementations, constrained calls, delegates, reflection
invocation, GetInterfaceMap and canonical type identity. The explicit class is
new to original Bases and already native in evolved Bases. All previous 35
evolution groups remain enabled. The evolved Base fingerprints of InterfaceCall,
DelegateCall and GenericConstrained are unchanged; original-generation callers
can differ and are not mislabeled as retained AOT evidence.

There are 792 passing outer structural assertions and 72 explicitly inapplicable
legacy probes. All 114 distinct protected files were independently rehashed and
remain unchanged. All 24 Base DLL/MV pairs reproduce byte-for-byte. Each resource
contains one common current DLL/MV payload for all six Bases. Unity 2021 uses its
own per-Base AOT metadata set; Unity 2022 and Tuanjie use FGS without that payload.

## Exact sources

| Component | Commit |
|---|---|
| HybridCLR | `8cbc13a8e48004eb5cb75734cf03d52236538448` |
| Unity 2021 IL2CPP | `2266aca69e29bfc1f8dbd98df1a2187cb7475ddb` |
| Unity 2022 IL2CPP | `5679c717713755312fa8a2a8bb8399c5f855c130` |
| Tuanjie IL2CPP | `a840b0cf4a19e6d9d7372d00094006110636a785` |
| Unity package | `a22db3db8ad300e218fa6564f3ba224e7a9f8c57` |
| Base/resource host and toolchain snapshot | `98d35f3` |
| Schema repair and full replay | `4eef487d0bfe74fc03d04095633a8aa3e3228fd1` |

All sources are isolated research candidates. The native contract is
`dhe-runtime-v16`, capability `existing-interface-method-slots-v1`; MV remains
`DHEMETA1`, schema 1. All three gates in `native-interface-token/<profile>` have
`passed=true`, `mergeReady=true`, `surrogateExternalHeadersUsed=false`. The native
unit executable tests slot binding; actual interface execution is established by
the separate Player matrix, not by compile-only coverage.

## Preserved failures and schema repair

The v14 replay `replay-interface-slots-u21-cold` failed atomic registration because
MethodImpl's Current declaration owner did not match the canonical interface list.
`fd3b112` normalizes only that declaration owner. The v15 replay
`replay-interface-owner-u21-cold` then registered and passed 38/42 groups, but four
IL dispatch groups failed with MethodAccessException. `8cbc13a` uses the existing
published logical-method map before caching IL method-token results. Reflection
and direct dispatch now share the public interface identity and logical slot.
No object layout, new publication field or new lock was introduced by these repairs.

All six new no-op Player reports pass. Evolved Base workflows pass end-to-end.
The three original Base workflows initially fail only the structural-entry schema:
their older code has no structural test entry, so it emits a complete empty record.
The unchanged failures remain in each Base archive. At `4eef487`, 26 schema checks
pass and `schema-interface-original-<engine>.json` separately revalidates each
archive. An absent record cannot claim execution, an identity, a native change,
nonzero entries, or either structural expectation. This is report-shape repair,
not a bypass of a failed Player or a reason to rebuild the immutable Base.

## Evidence

Paths are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

| Artifact | SHA-256 |
|---|---|
| `registry-interface-generations.json` | `BC7D9CC8A34BAFB598700BB02C3D067188A7C7FF7A3B4636A45E1AB24F1D9E86` |
| `resource-interface-generations-first/dhe-resource-update.json` | `70B987F6C454B9D8D9F3D4A3C9F52CA0C382D92DE76447D2C8DD14C44F94A3DD` |
| `resource-interface-generations-latest/dhe-resource-update.json` | `086FA009372A3C11DFE86667045BFAB67A259BE1A044A78EC8071239181FA881` |
| `replay-interface-generations-cold/report.json` | `2CABA90DF77DBF96A1368EB2BDE51CB5E40C537672FA0E9F040B876CD428560F` |
| `interface-generations-mv-archive/report.json` | `93317CB8C880ECA7311817B4C66499F7D19E83860947612DBDAE6BE1E675F02C` |
| `interface-original-schema-regression/report.json` | `84796FCDD0010EF50D700CA3D0B7E00C5E956385312B835DFE208281E3048E19` |
| `interface-token-caller-compatibility/report.json` | `00367E5905182914442808356A20E175EC7526C116EB42E9F78B5E2A7E2F0522` |

`manifests/dhe-interface-generations-windows.json` freezes all six Player paths,
identities, first/latest CLR references, 42 required groups, and the full golden
suite. Use the existing C# evolution runner and a new replay output directory.
Never overwrite the successful or failed archived run directories.

## Remaining work and rollback

This does not prove generic/inherited interface evolution, inherited implementations,
cross-assembly slot evolution, general class virtual-method changes, value-type
layout changes, Unity components/serialization, broader field addresses, independent
sidecar expansion with a live alias, external AOT API availability, GC/concurrency/
ABI stress, performance/memory or Android/iOS. These remain part of the full goal.
No production performance or BattleAOT replacement claim follows from this result.

No formal maintenance branch, runtime tag, Installer default or CAT source changed.
All six source worktrees are committed; the three scratch Demo projects only retain
their ordinary generated input DLL changes. New input stashes are preserved:
Unity 2021 `047e98e`, `1f731f7`, `445ebd8`; Unity 2022 `c233a9f`, `40a973d`;
Tuanjie `bfe4c88`, `3750075`. Earlier stashes and all old Players remain intact.

Rollback selects a matched previous native/package/tool snapshot and resources
within each Base's proven capabilities. Old v11/v13 Bases cannot acquire the new
native capability from a resource manifest; v14/v15 failed candidates are not
qualified release Bases. The candidate remains Exploratory, not release-ready.
