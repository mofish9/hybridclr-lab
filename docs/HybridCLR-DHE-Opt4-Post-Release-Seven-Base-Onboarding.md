# HybridCLR DHE opt4 post-release seven-Base onboarding

## Status

On 2026-09-06, the released 0.1.31 C# toolchain completed the temporal
multi-Base lifecycle that was not covered by the initial six-Base run:

1. the protected lab channel already contained resource revision 2 and six
   active Bases;
2. a seventh real, immutable Tuanjie Windows Base archive was appended through
   one direct-successor registry;
3. `resource-release-build` produced one resource revision 3 for all seven
   Bases;
4. the same resource directory was staged and executed on every old and new
   Player; and
5. exact 7/7 aggregate qualification passed before the channel head advanced
   through compare-and-swap promotion.

The seventh Base had been built and archived by an earlier real Editor workflow
but had never been active in this registry. This run therefore proves late Base
onboarding and one-build old/new-Base convergence. It does not claim that a new
Editor Player build was performed during this run.

## Tool and input identities

- Toolchain: `HybridCLRDhe-0.1.31-opt4.16`.
- Package ID:
  `5af090eca274468623a7b4e8bab3ceeb669f378f7293bfeb3d1498c69ba7d960`.
- Starting channel revision: 2.
- Starting channel-head SHA-256:
  `edbee27aed5829640cb982a92a02a7b585bfb292671cf7115d75b50713f3480f`.
- Starting release-ledger SHA-256:
  `8442ea2f727e00551286cf323b9e7d346fe449f5630071d273daaefe20352ef2`.
- Starting registry revision/SHA-256: 1 /
  `df2fdde424cd2142f3922564344c30a4e16b84ffa0f12ed5b8d4c3582612589f`.
- New Base ID:
  `5f202dd58c999bfc35a7b1c7a6b383fff507aa268572d1e2c26a10a53ffe839d`.
- Build config SHA-256:
  `9e4bb7bd08fbcdc51c36c881c9dfce9693df9288f801c43c25274cbe4389d1a7`.

The new historical Tuanjie Base binds its own non-empty AOT metadata set even
though later FGS Bases use an empty set. An initial null metadata input was
correctly rejected because it changed the composite Base identity. Supplying
the archived metadata root matched the immutable BuildIdentity and allowed the
onboarding to proceed. No identity check was bypassed.

## Registry and release transition

The build report has `registryDisposition=created-successor`,
`addedBaseCount=1`, `activeBaseCount=7`, and `releaseRevision=3`. The successor
registry retains all six previous Base IDs exactly once and contains the new
Base exactly once.

- Successor registry revision/SHA-256: 2 /
  `e9b516b90ddcbab1c121cb8732c61ab2378a6ff83fb7d499a86ff479231ef313`.
- Registry parent SHA-256:
  `df2fdde424cd2142f3922564344c30a4e16b84ffa0f12ed5b8d4c3582612589f`.
- Resource manifest SHA-256:
  `994c73bbf92ce2fb63d6fdb2da4001fbecc82024c40a8b41d5da896c5c1f5ed4`.
- Resource validation SHA-256:
  `09d40bb38cb09d7f5d3ef3ef23d9a1f385345de03fd0a45830a9b05bbf4d59b2`.
- Runtime plan SHA-256:
  `ae2895c13a831c42a3a8e3d42bbb43f1e4916e0645b5fc7594974ff11ec7b983`.
- Release-ledger SHA-256:
  `c350a8abc252ab2724e8dbbd67b8a9dc4f040eb6d5d072f162c1cf91a50b10f9`.
- Ledger parent SHA-256:
  `8442ea2f727e00551286cf323b9e7d346fe449f5630071d273daaefe20352ef2`.
- Current assembly-set SHA-256:
  `c3f2d09c049e027e4073a90e51d381c0b3b3136f1a68623ab104af191f27bc15`.
- Payload variant-set SHA-256:
  `84e9c67e3deff6becf5e2aa038cabea8e777f56694bb3f72b13af89ee64c4a7a`.

The single output contains both `windows` and `android` named managed variants.
Every Base selected exactly one variant, and every configured variant was used.

## Player execution

All seven `StandaloneWindows64` Players consumed the exact revision 3 manifest,
validation, runtime plan, and ledger. Staging preserved each embedded Base
MetaVersion tree, Player executable, and `GameAssembly.dll` SHA-256.

| Engine workflow | Base label | Variant | Changed methods | Interpreter/AOT entries |
|---|---|---|---:|---:|
| `Unity2021Standard` | `u21` | `windows` | 29 | 10/37 |
| `Unity2021Standard` | `u21-new` | `android` | 3 | 3/60 |
| `Unity2022Fgs` | `u22` | `android` | 29 | 10/37 |
| `Unity2022Fgs` | `u22-base2` | `windows` | 27 | 10/37 |
| `Unity2022Fgs` | `u22-legacy` | `android` | 29 | 10/37 |
| `Tuanjie2022Fgs` | `tuanjie` | `windows` | 29 | 10/37 |
| `Tuanjie2022Fgs` | `tuanjie-new` | `windows` | 29 | 10/37 |

The different changed-method counts are expected because each Player compares
the one selected current payload with its own embedded Base MetaVersion. Every
result retained positive AOT entries and proved changed interpreter dispatch,
multi-assembly execution, structural behavior, and transaction retry.

## Qualification and promotion

The seven generated Player evidence documents passed the distributed schema
gate. The aggregate gate authenticated three historical package identities,
recorded every Player as `authorized-historical-package`, covered all three
engine workflows, and required exact 7/7 Base coverage.

- Evidence root:
  `artifacts/dhe-seven-base-onboarding-e2e-20260906/evidence`.
- Evidence schema gate SHA-256:
  `40a9ad82f06d3ac7ef5ade447cda713744c200f119e9d52b828e0be3aade9017`.
- State-bound aggregate gate:
  `artifacts/dhe-seven-base-onboarding-gate-20260906/resource-release-gate.json`
  (SHA-256
  `0fa67c8f647ecea8c4c06768a5f89ac90b4f5861756dd86ed6033ed9314239c1`).
- Promoted snapshot:
  `artifacts/dhe-seven-base-onboarding-promotion-20260906/promoted-snapshot.json`
  (SHA-256
  `9d2c0bbc3cac9011d3e82aaa4c7ca2fa9a99ffd70fbd1f61d6b3bd88f15086f1`).
- Promoted channel-head SHA-256:
  `7220e3198e747a1c8432e247568fed025cea8b3374773b3bf7572e19f2a8efbb`.

Promotion independently regenerated the aggregate gate, compared all stable
fields, stored the resource tree and approval by content hash, and advanced the
channel from revision 2 to 3. This is an isolated lab channel, not CAT
production content.

## Proven scope and remaining gates

This run proves on Windows that an already active six-Base channel can append a
seventh Base and use one later resource build for every old and new Base across
Unity 2021, Unity 2022, and Tuanjie 2022 workflows. It also proves per-Base AOT
metadata selection across historical workflow generations.

It does not yet prove a newly built Player from each Editor in the same run,
Android ARM64 or iOS device execution, macOS filesystem semantics, signing and
catalog upload, CAT project integration, or production memory and tail latency.
Those remain independent gates and Windows results are not substituted for
them.
