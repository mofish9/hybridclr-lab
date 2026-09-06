# HybridCLR DHE opt4 r5 cross-target candidate

## Scope

This record resumes the post-release cross-target experiment for one protected
DHE resource channel. The candidate appends three Android ARM64 Base archives to
the six Windows Bases already covered by revision 4, while keeping one resource
revision and two target-specific payload variants:

- `default`: StandaloneWindows64;
- `android`: Android ARM64.

The candidate does not modify CAT or rebuild the six existing Windows Bases. It
is not promoted because Android device execution evidence is still missing.

## Release and registry identity

The candidate uses the immutable revision 4 channel snapshot and derives a direct
successor Base registry:

- channel: `dhe-demo-base-growth`;
- parent ledger SHA-256: `a44a71865ce69a66862caf2b6e45023a96a1d7fd95a7f69ad868d54cd53dbea2`;
- candidate release revision: `5`;
- Base registry revision: `6`;
- Base registry SHA-256: `7d54ad7e21994ab807bcbb31bbe1d3d2402ca02eb41241cc76ed3928629f809a`;
- release ledger SHA-256: `0deba9447ec72e96b01fad69b2382e26fa2ba3d0bd94151209bc917414fa7592`;
- current assembly-set SHA-256: `c3f2d09c049e027e4073a90e51d381c0b3b3136f1a68623ab104af191f27bc15`;
- payload variant-set SHA-256: `7fb32f9f4dff6d0ae5da3267a1090a2bf669614a374da20dc8dd7ca1edb5befb`.

The release build and all nine selected Base stages report `passed=true` and
`baseMetaVersionUnchanged=true`. The complete staging tree passed the distributed
schema gate with 51 validated documents and zero errors.

## Evidence available

The six Windows Base Players ran the candidate payload successfully:

| Base group | Workflow | Variant | Changed methods |
|---|---|---|---:|
| Existing Windows Bases (6) | Unity 2021, Unity 2022, Tuanjie 2022 | `default` | 2, 27, or 29 |

The three new Android archives passed APK-aware staging. Each stage verified the
immutable APK bytes, embedded BuildIdentity, embedded Base MetaVersion set,
selected `android` payload variant, runtime plan, manifest, validation, and
release ledger. The APK identities are:

- Unity 2021: `b2a71a697bce9c0d129469940a2540f064b117d38ff94b3c04437b4526da467d`;
- Unity 2022: `940d98a5071d65b72a4ed4154673bfdda7bd4a448d14bfafdebec90c2ad870e6`;
- Tuanjie 2022: `d188daae5a90330c2780fdb99fbc58c0cf62cd6e2ee4d51288944b94ac87e3b1`.

These are build and transfer identities only. APK staging is not Android Player
correctness, and no `dhe-device-player-run.json` was produced.

## Gate result

The project-facing aggregate gate correctly rejects the candidate until all nine
active Base Player reports are present. A run with only the six Windows reports
fails with `Changed Player evidence must cover every active Base in the shared
resource release.` The failed gate writes no passing output.

The C# host and package remain clean and reproducible:

- tool source: commit `a051cb3209b11b5de861a74feeb305ccb44b95f1`, tree
  `39930346c3c0303e2690a2539bb40cadcf8b941a`;
- package: `HybridCLRDhe-0.1.32-opt4.18`;
- package ID: `a1c4347c533080b4c8bc2d5344fe8bf5e6b0c0cb5b9ccf0d10f1ef6bb68a9284`;
- formal regression: 146/146 checks passed;
- package verification and package schema gate: passed.

## Remaining qualification

Run `android-device-smoke` once for each Android APK and stage, then generate
one `resource-player-evidence` report per Android Base. Re-run the aggregate
`resource-release-gate` with all nine reports and promote only the resulting
content-addressed snapshot. Android PSS/RSS, temperature, weak-core and tail
latency remain separate gates. iOS still requires a macOS/Xcode build, signing,
resource staging, and device evidence; Windows or APK staging cannot substitute
for those checks.
