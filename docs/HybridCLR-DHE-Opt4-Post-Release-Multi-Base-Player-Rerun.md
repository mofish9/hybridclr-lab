# HybridCLR DHE opt4 post-release multi-Base Player rerun

## Status

On 2026-09-06, the released 0.1.30 C# toolchain requalified one already
published resource payload against six immutable Windows Base Players. The
rerun passed for Unity 2021, Unity 2022, and Tuanjie 2022 with exact active-Base
coverage.

This is additive evidence for release 0.1.30. It does not modify or replace the
released package, runtime tags, Unity package commit, or the original 140-check
release evidence.

## Bound identities

- Toolchain Package ID:
  `aa7aef3ec649ad0cca34122bc357a7779d40d6262b45920609ebb222392d8e16`.
- Resource manifest SHA-256:
  `50b58c5617fffa6765815ccb763f83ce5e3dc93b977500d22c9a91146b019577`.
- Runtime plan SHA-256:
  `0f72f4884a81b2c1cb0613c78f97c2d7d8e562f1fc21d597af0ec3bb153d981b`.
- Current assembly-set SHA-256:
  `c3f2d09c049e027e4073a90e51d381c0b3b3136f1a68623ab104af191f27bc15`.
- Release ledger SHA-256:
  `57c0e3fd01fad152501fce3b57338d3d522f28571da32be9f41efb43851fec37`.

Every Player contained byte-identical resource manifest and runtime-plan
files. The selected Base identities and observed differential method counts
were:

| Engine workflow | Base ID | Changed methods | Interpreter/AOT entries |
|---|---|---:|---:|
| `Tuanjie2022Fgs` | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | 29 | 10/37 |
| `Unity2021Standard` | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | 29 | 10/37 |
| `Unity2021Standard` | `723f38655642600358d75cd0a7bc4fc21767310d97e1fd87f13dd63d6f109f8b` | 2 | 3/60 |
| `Unity2022Fgs` | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | 29 | 10/37 |
| `Unity2022Fgs` | `e0d1682388719a48c8840d0f5e7b0513b45ddf7cf8b2898af46d1c4093024e34` | 27 | 10/37 |
| `Unity2022Fgs` | `adfc4294ad4912dda9379b1556921dcd0a827f8b39e33ab2ddebea82ba3097ae` | 29 | 10/37 |

The different changed-method counts are expected: each Player compares the
same current payload with its own embedded Base MetaVersion. Unchanged methods
continued to enter AOT code while changed methods entered the interpreter.

## Qualification evidence

The evidence root is
`artifacts/dhe-six-base-player-rerun-20260906`. It contains the six raw Player
results, six 0.1.30-generated resource Player reports, an isolated revision-2
channel replay, and the aggregate gate.

- Aggregate protected resource gate:
  `artifacts/dhe-six-base-player-rerun-20260906/resource-release-gate.json`
  (SHA-256
  `c9eee9c13d93f04b154dda24f3890b7dea0f31213af54dac6f1822bdf6ca8131`).
- Schema gate:
  `artifacts/dhe-six-base-player-rerun-20260906/schema-gate.json`
  (SHA-256
  `e00f00666c0934848b58437648d7030c21ff9843341278b331ca216196633b38`).
- Replay channel snapshot SHA-256:
  `976dd386dd8191a4ab43d67793755751d02b6ff5bc779e94ac54872fbfde4726`.

The production channel had already advanced to revision 3, so its original
revision-2 snapshot correctly failed the live-head check. Requalification used
an isolated copy whose head was restored from the immutable, content-addressed
revision-2 state object. The production channel and its promoted revision-3
head were not changed.

## Scope limits

This rerun proves the executable Windows path for six Base identities and one
shared payload. It does not provide Android ARM64 or iOS device correctness,
memory, tail-latency, thermal, signing, Xcode, or macOS filesystem evidence.
Those remain production gates rather than inferred results.
