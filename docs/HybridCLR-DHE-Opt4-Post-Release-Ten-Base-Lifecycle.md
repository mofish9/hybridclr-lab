# HybridCLR DHE opt4 ten-Base lifecycle

## Scope

This isolated lab run proves the intended long-lived DHE workflow:

1. build one real Base Player with each locked Unity 2021, Unity 2022, and
   Tuanjie 2022 Editor;
2. archive and append all three Bases to the existing seven-Base registry in one
   direct-successor resource build;
3. execute one revision 4 hot-update payload on all ten Base Players; and
4. promote the resource only after exact ten-Base evidence coverage.

CAT was not modified. All configured hot-update assemblies remained in the DHE
set; no BattleAOT exception was introduced.

## Fresh Bases

| Workflow | Base ID |
|---|---|
| `Unity2021Standard` | `83d933943225fb94bccdbc75be946224a99e826f34a1f02fb73a5e0f7a3b42c2` |
| `Unity2022Fgs` | `c6cfacbdb86035b047dad24e3a9e418f3f1de167f968e8acffb85098e29a7d60` |
| `Tuanjie2022Fgs` | `902de93df99cd1fac094456ca659e1354041d237cd233c99bc009087c79b8005` |

Each real Editor build, Player execution, archive, release gate, and schema gate
passed. The three source worktrees were clean after construction.

## Revision 4 identity

The protected snapshot SHA-256 was
`3dff18e0c38278fe4266adaa240598f16902237cb603848865d6916700c661ef`.
It bound channel head
`7220e3198e747a1c8432e247568fed025cea8b3374773b3bf7572e19f2a8efbb`,
revision 3, and parent ledger
`c350a8abc252ab2724e8dbbd67b8a9dc4f040eb6d5d072f162c1cf91a50b10f9`.

One atomic `resource-release-build` changed the registry from revision 2 to 3,
kept all seven old Bases, added all three new Bases, and produced:

- Base registry SHA-256
  `4dd2a9b969609f85c94c2faa480c4afebf4f4194b862a46e6817423cc7df24d4`;
- revision 4 ledger SHA-256
  `9799b299f9d866ca13d176cf1b786d32dbc225d02062f97a4e4dbf5d08c420f5`;
- resource manifest SHA-256
  `4f1511c39aeb7332ab72363de4b6b4d375ef24d87220f1fdb8b21dc16afb28b2`;
- validation SHA-256
  `374df406128c8dc6dd5516574f31655a40f7dbfeefb8c0bcc91beace00ab191f`;
- current assembly-set SHA-256
  `e49bc1122982934c9800c18162eaaaaa8f74cfafc83c1b5e2b3570f175358020`;
  and
- payload variant-set SHA-256
  `e9e98fe28755be8f70a090b42a351682a0210f0119981e24b6a3e0248d0df80c`.

`unsupportedChangeCount=0` and active Base coverage was exactly 10.

## Player results

| Player | Workflow | Changed | Interpreter | AOT |
|---|---|---:|---:|---:|
| `u21` | Unity 2021 | 29 | 10 | 37 |
| `u22` | Unity 2022 | 29 | 10 | 37 |
| `tuanjie` | Tuanjie 2022 | 29 | 10 | 37 |
| `u22-legacy` | Unity 2022 | 29 | 10 | 37 |
| `u22-base2` | Unity 2022 | 29 | 10 | 37 |
| `u21-new` | Unity 2021 | 3 | 3 | 60 |
| `tuanjie-new` | Tuanjie 2022 | 29 | 10 | 37 |
| `fresh-u21` | Unity 2021 | 29 | 20 | 60 |
| `fresh-u22` | Unity 2022 | 29 | 20 | 60 |
| `fresh-tuanjie` | Tuanjie 2022 | 29 | 20 | 60 |

All ten reports passed structural behavior, direct/reflection and secondary
assembly execution, transaction rollback, and same-process retry. The fresh Base
reports were generated from their portable archives and revalidated the complete
archive file sets.

## Qualification and promotion

Toolchain 0.1.32 regenerated the three fresh reports and schema-validated all ten.
The schema gate SHA-256 is
`2614f1ca786ed5f674654412cbfc603f5e839cda00dc86e0f5d221e629e14c5b`.

The aggregate gate reports `releaseReady=true`, exact Base coverage, and all
three engine workflows. Its SHA-256 is
`b634503cebf2ed301a227e1669bdf3813ab146183427933b993f3f84e75a1678`.

CAS promotion revalidated that gate and advanced the isolated channel to
revision 4. The promoted snapshot SHA-256 is
`a66884c39fc2b04c248ec390bd1f5b96e0ca7b6cd0a60a513168e97a1892357b`;
the new channel-head SHA-256 is
`d6fce4a2727d2a4b7f8413965306c17c36cfc110c347f41a0e4e5b5685d23a25`.
The published artifact tree is
`a4ec1e4514726f91f2feaa4cc052727127666baaf5015e998f92e175bee1471a`.

This is a protected lab channel, not CAT production content. It proves the
multi-generation Base/resource workflow on Windows; mobile device and production
performance conclusions remain pending.
