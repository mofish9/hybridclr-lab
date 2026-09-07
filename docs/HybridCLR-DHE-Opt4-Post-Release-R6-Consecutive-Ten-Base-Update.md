# HybridCLR DHE opt4 post-release revision 6

## Scope

This run derives a new current payload from the authenticated revision 5
payload and applies it to every active Base in the protected lab channel. No
Base Player is rebuilt. The resource update contains `windows` and `android`
variants and is validated against Unity 2021, Unity 2022, and Tuanjie 2022
workflow identities.

## Bound release

- toolchain: `HybridCLRDhe-0.1.32-opt4.23`;
- Package ID:
  `d22b37ed90d5e25d164ed046dfff4d63c4ef74120b6012b05dbe0c8982db8528`;
- channel: `dhe-builder-six-base-e2e`;
- parent ledger SHA-256:
  `3281696217b977644c423402d52c6d4b151f939315c9716f6deefddf7fbd926d`;
- current revision: `6`;
- revision ledger SHA-256:
  `24f6f3a81af3437f53478a8452f77663ca6f54621c3970b37c24255c52f3318f`.

The candidate update and all evidence are under
`artifacts/resume-release6-resource-20260907` and
`artifacts/resume-release6-players-20260907`. The aggregate gate is
`release-gates/resume-release6-gate-20260907.json`.

## Active Base coverage

| Engine workflow | Base ID | Variant | Changed methods |
|---|---|---|---:|
| Unity2021Standard | `d4e111598fd018f5bfd36049fde62c893d81e7efa0212f4917ab4ef6dae1eddf` | windows | 29 |
| Unity2021Standard | `723f38655642600358d75cd0a7bc4fc21767310d97e1fd87f13dd63d6f109f8b` | android | 3 |
| Unity2021Standard | `83d933943225fb94bccdbc75be946224a99e826f34a1f02fb73a5e0f7a3b42c2` | windows | 29 |
| Unity2022Fgs | `a7b13681e18526ad87cd8052b5f7e359220a5624e61fe65a1ef1d0f36dc81a32` | android | 29 |
| Unity2022Fgs | `e0d1682388719a48c8840d0f5e7b0513b45ddf7cf8b2898af46d1c4093024e34` | windows | 29 |
| Unity2022Fgs | `adfc4294ad4912dda9379b1556921dcd0a827f8b39e33ab2ddebea82ba3097ae` | android | 29 |
| Unity2022Fgs | `c6cfacbdb86035b047dad24e3a9e418f3f1de167f968e8acffb85098e29a7d60` | windows | 29 |
| Tuanjie2022Fgs | `74706b344b0055b0d6b09c04f18611a300207d35cebfd960d2221a75645ed6f1` | windows | 29 |
| Tuanjie2022Fgs | `5f202dd58c999bfc35a7b1c7a6b383fff507aa268572d1e2c26a10a53ffe839d` | windows | 29 |
| Tuanjie2022Fgs | `902de93df99cd1fac094456ca659e1354041d237cd233c99bc009087c79b8005` | windows | 29 |

Each Base generated a passing `resource-player-workflow-report.json`, retained
its own embedded MetaVersion, and validated transaction rollback and same-process
retry. Changed methods use differential interpreter dispatch while unchanged
methods continue through AOT dispatch.

## Promotion

The resource-release gate passed with exact ten-Base coverage:

- gate SHA-256:
  `e49293f7263a075d158bcf7fa1df77fc94dbb477709110f5a11f15d290d47998`;
- current assembly-set SHA-256:
  `6f4cae92afa11386dd118e10cdca43d4e93e0442c3882c202212434a42413ea7`;
- payload variant-set SHA-256:
  `e3fd59f17d7b00b8c33c2191dc2491a93d1db9108a9246f5e1f6132582122d7c`;
- promoted snapshot SHA-256:
  `2b4ebfb2d2be6f016c1e9bc20c9680e5ea3cc9251d173c379917ad1a84821dc5`;
- channel-head SHA-256:
  `353fcfbb7b173e1da6460cf1d141e5615ea786f71beaa9c2b5618db7609562ef`.

Promotion was a CAS update from revision 5 to revision 6. The old Base Players
remain immutable and the next resource update must use revision 6 as its parent.

## Qualification boundary

This is a Windows execution and protected-channel lifecycle result. It does not
provide Android ARM64 device correctness, PSS/RSS, thermal, weak-core, or tail
latency evidence. It also does not provide macOS/Xcode build, signing, IPA,
device, or iOS performance evidence. Those gates remain required before a
mobile production release.
