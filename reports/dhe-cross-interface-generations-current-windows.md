# DHE cross-interface six-Base Windows review

The complete replay at clean source `d19b64944875a58d3c4f00644fff1167a6f7fa02`
is **failed**. All six Base construction/no-op/schema workflows pass, but all
nine resource runs on the three original-generation Bases fail the existing
`generic-inheritance` group. The three evolved Bases pass all nine runs, each
with 52 evolution groups and 220 differential cases, zero differences.
This remains an exploratory candidate, not unrestricted DHE or a release.

## Confirmed regression and next investigation

Current introduces `DheEvolutionGenericDerived : GenericVirtualOperation<IntOperationStruct>`
on original Bases. Its ordinary inherited virtual Apply must return 106.
The separate direct generic virtual probe does return 106, but the new derived
receiver fails: Unity 2021 reports `DHE inherited virtual method has no callable
entry`; Unity 2022 and Tuanjie reach the missing-AOT stub for Apply. Evolved Bases
already contain that derived type and pass the same frozen resources.

Registration and offline compatibility both accept these original Bases. Their
capability declarations therefore do not prove this case works. Do not publish
these Bases or relabel the nine failures as a successful six-Base qualification.
No fixture, golden, resource DLL or archived Base was changed to avoid the error.

Review points to the recently changed Current-parent vtable path. In particular,
published Current generic tables retain Current declaring types, while InflateVts
matches the cached Base generic definition when binding the closed receiver.
An open declaring type surviving that mapping is a hypothesis to verify, not a
completed root-cause diagnosis or a native repair. Next work should inspect the
selected method/declaring type and generic context, retain this original-generation
regression, and repair the mapping/eligibility before rebuilding affected Bases.
The existing 52-group payloads remain the acceptance workload.

## Changes retained and repaired

All earlier HybridCLR implementation commits remain in use; HybridCLR itself is
still `6ed809b4e372fe91f0283c9ed3ad80c81cd11eef`. This review fixed three workflow
problems without replacing its field, metadata, generic or interface implementation:

- `1c5b35f`: shared Unity log reads. The held-writer regression fails with the
  old read semantics and passes all six checks after the repair, including on
  the clean commit. The authenticated C# tool builds with no warnings/errors.
- IL2CPP codegen now includes its DHE declarations directly. A public-header
  compile unit reproduces the old Tuanjie Player's C2653/C3861 errors. All three
  real-header compile/CTest gates then pass, with `mergeReady=true` and
  `surrogateExternalHeadersUsed=false`. The first attempt at this test directly
  included an internal header and lacked required typedefs; that failed output
  remains separate from the valid public-header reproducer.
- `8f50199`: the Demo uses the existing package NativeFinalizeOptions and callback
  during both build phases, sharing one compiler scope for generation/finalization.
  This closes the reproduced second-session compiler replacement failure on
  Tuanjie. Both original/evolved Tuanjie workflows pass afterward. The package
  compiler patch and its transaction implementation are unchanged.

## Exact source and Base identities

Current research sources:

| Component | Commit |
|---|---|
| HybridCLR | `6ed809b4e372fe91f0283c9ed3ad80c81cd11eef` |
| Unity 2021 IL2CPP | `566a4b270d597bfc5e0a72b83b678fecf7ca3a6e` |
| Unity 2022 IL2CPP | `27dd0ee877bc94a75584378ca8f590ca88753e21` |
| Tuanjie IL2CPP | `6159455972722c94dfc4b4adbedceb62c6905618` |
| Unity package | `ba2da96cec9512d83c88cbc5a2b1247b0bf93fbd` |
| Tool snapshot / native compile configuration | `fddc5be` |
| Updated Demo adapter | `8f50199` |
| Frozen replay | `d19b64944875a58d3c4f00644fff1167a6f7fa02` |

MV remains DHEMETA1 schema 1. The current package declares dhe-runtime-v21 with
the same dispatch capabilities as v19/v20; this only distinguishes actual source
identities. Four previously successful Base builds are reused without rewriting
their older source identities. The successful Unity 2021/2022 Players do not
constitute Player evidence for the later header-only IL2CPP commits.

| Base | Contract | BaseId | Resource replay |
|---|---|---|---|
| U21 original | v20 | `248da9490127c7c81a34cc29297d651157c9049f0c9892a786a8cdae6968a016` | 3 failed |
| U22 original | v20 | `0d39fefb5a00b99b692aad67cc4184b06f0a1c75d9b5d5ffdd0aadb9c82e10fc` | 3 failed |
| Tuanjie original | v21 | `3f9c8bf0b1b3b99d0c651d9ebde044a4b7755e9d2d15fe4cd67e07996e2027db` | 3 failed |
| U21 evolved | v19 | `1ee0b432ce1bbc2edc2830930c583c0c0be5f529bbc3999a188e009d9f6b6b1d` | 3 passed |
| U22 evolved | v20 | `eafdadb605a583d01b706309e9f9d69b8443ba29de4e2586eb7c2446cbc993f1` | 3 passed |
| Tuanjie evolved | v21 | `07975b54862d992e76b05598a71f64f0f2f536c5813056bf11c106a0d3bbf51e` | 3 passed |

The earlier U21 Base uses package 92e27c2; the other reused U21/U22 Bases use
5d17256. Their IL2CPP commits remain 01c1725 (U21) and 4e3637d (U22).
Both new Tuanjie Bases use 6159455 and ba2da96. All use HybridCLR 6ed809b.

## Evidence and limits

All paths below are under `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

| Artifact | SHA-256 |
|---|---|
| `registry-cross-virtual-generations.json` | `B4EED8F82AD5AF6130C41D0BBC8353E607314748ED33B5DFBC6811EA0C1FCA87` |
| `resource-cross-virtual-generations-first/dhe-resource-update.json` | `D178C0A396198AC13CA9411EE16ED83941B95FE28A58C94020AF72E2A6A7F548` |
| `resource-cross-virtual-generations-latest/dhe-resource-update.json` | `6569F5109D8C308EF2F31D3D093BB4524C2B880352EB2CE0A6B788001E5A4E5C` |
| `replay-cross-virtual-generations-cold/report.json` | `F63554D6584512CC5FCC4ABCD3BDDE7E9D891F5D277804E1306445626F9138CD` |

All 114 distinct protected files from successful and failed runs were independently
rehashed and remain unchanged; see `cross-virtual-generations-immutable-check.json`.
Nine passing runs record nine unique PIDs. The runner records process=null when
RunProcess throws for a nonzero exit, so this report does not claim authenticated
unique PIDs for all 18 invocations. Failed run artifacts include the actual Player
errors and immutable-file hashes; failure PID retention is another harness gap.

The first payload retains 208 AOT case entries and conservatively invalidates 12
on the evolved Bases. The uninstrumented first payload's zero entry receipts are
not an all-AOT claim. Latest/skipped-latest passing runs record 220 interpreted
entry receipts. There is no pre-touch or inline padding. Each resource contains
one common Current DLL/MV set; U21 selects its own archived AOT metadata, while
U22/Tuanjie use FGS with an empty supplemental metadata set.

Other evidence: `unity-log-sharing-before`, `unity-log-sharing-clean`,
`native-codegen-public-header-before`, `native-cross-codegen/<profile>`,
`cross-codegen-package-verification.json`, and both
`base-cross-*-tuanjie-integrated/project-workflow-report.json` reports.
Earlier log-sharing, codegen and compiler-session failures remain intact.

No CAT files, formal maintenance branches, runtime tags or Installer defaults were
changed. All six source worktrees and three scratch Demo Git worktrees are clean
at this checkpoint. New input stashes remain: U22 cfb7e8f; Tuanjie c2ee912,
abae4a4, 732b0ef and faa093a. Older stashes remain as well.

Rollback uses a matched previous runtime/package/tool snapshot and resources
proven on that Base. Already-built native code cannot be repaired by a resource
package. Full generic/virtual/layout evolution, Unity serialization, broader
GC/ABI coverage, RVA-stable fingerprints and performance/memory qualification
remain open. Android and iOS were not tested; the immediate next native work is
the confirmed original-generation generic inheritance regression.
