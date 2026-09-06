# HybridCLR DHE opt4 toolchain 0.1.32 release report

## Status

Toolchain 0.1.32 closes two defects found while extending one protected resource
channel from seven to ten immutable Base Players. The released C# package is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.32-opt4.17`. Its Package ID is
`5cee1f8aeda8e23576572a63ec0a1fd2a68104f327b2296ec3b4c8a9c518c964`.

The package records `mode=Release`, `releaseReady=true`, 102 authenticated files,
41 commands, source commit `e5208f38dae4ee625da9ce0a0990d26af5d6f6f0`, and
tree `f8e5cf2b0e8276f2b15351b8c2fcbdbd55dcace1`.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE tool source | `optimize/dhe-consecutive-current-v8.13.0` | `e5208f38dae4ee625da9ce0a0990d26af5d6f6f0` | none |

No runtime or Unity package source changed. No runtime or package tag was created.
The 0.1.31 package identity was added to the authenticated historical authority
set so its Base archives remain usable after upgrading the release host.

## Consecutive current generation

The lab-only `current-next` variant can be applied repeatedly to the previous
authenticated current assembly set. It increments generation constants while
preserving assembly/module identity and metadata shape. The old `current-base2`
name remains an alias for compatibility.

The constructor transformation no longer removes a `ret` instruction that may
already be a branch target. On later generations it locates and updates the
existing sentinel. Regression requires each consecutive derivation to remain
compatible, change exactly two method bodies, introduce no metadata drift, and
reject overflow.

## Portable Base archives

New archives point their workflow directly at the immutable original native
manifest. Existing 0.1.31 archives are accepted only after their archive manifest
is revalidated and its immutable native record matches the Base registry hash.

`resource-player-evidence` now resolves archive-relative source preflight, clean
checkout, toolchain gate, runtime manifest, and native manifest references before
writing evidence outside the archive. It records the archive manifest and its
SHA-256. Qualification recomputes the complete archive file set and then checks
the recorded runtime lock, engine workflow, source commit/tree identities,
non-surrogate header identity, and exact Release package. Missing, extra,
relocated-without-authority, or tampered archive content fails closed.

## Formal qualification

The clean regression passed 143/143 checks with `sourceClean=true`, six historical
changed Base reports, one no-op report, all 13 historical authority packages, and
three real Editor resolver reports. The two new mandatory checks cover consecutive
current derivation and archive-bound native/provenance resolution with tamper
rejection.

- Regression SHA-256:
  `6e5d0e7a8aa2e7b3023b80382c7f486908de2f70f8bcc3f282555a439aeb80b0`.
- Release evidence SHA-256:
  `58c5c6acc82397552e5d9733beff37ff7f9f5daa8eaedd632e31c12d76de25cd`.
- Package manifest SHA-256:
  `18216a5118adb606ed38e8d09c8b75fb6d6b6ccee8f5a4da173436735f8219c5`.
- Verify, doctor, schema gate, and post-build verify SHA-256:
  `8f2a64de8fff6ad1277a95b6316afe5dcea2409d678a3c4c0f9d75a675457256`,
  `2b3b1a3dbd119541b8e8cafda7a22db16b59fbc57889aa2d2f282369110fc24d`,
  `868babb7947ad25bf6246e26ac141247b6a11ea126384b16ae7820080d8c981b`,
  and `be5c696d675b18d8ff03662d3f2f05466115d07a81282e092bed2c11b1ab0c1c`.

The distributed source was compiled with output and intermediate directories
outside the immutable package. Compilation had zero warnings and zero errors,
and package verification passed afterward.

The runtime/native repositories did not change. Their locked three-engine native
reports remain historical evidence for the same commits; they were not rerun as
new runtime results. They report real Editor headers, no surrogate headers,
`passed=true`, `mergeReady=true`, and CTest 1/1.

## Remaining gates

- Windows Editor/Player evidence is not Android ARM64 or iOS device evidence.
- Android/iOS correctness, PSS/RSS, tail latency, temperature, and weak-core gates
  remain incomplete.
- macOS filesystem behavior and Xcode generation, link, signing, and device
  execution remain untested.
- CAT still requires its own Base registry, all-hotfix build, catalog staging,
  one Player/device run per active Base, and performance/memory gates.
- Existing value-type layout, inheritance/interface/vtable, ABI, GC, and other
  unsupported shape changes remain fail closed and require a new Base Player.

## Rollback

For tool operations, pin the previous immutable package
`HybridCLRDhe-0.1.31-opt4.16` and its Package ID. Do not delete or rewrite an
already published channel head; application rollback is a new forward resource
revision from the actual protected head. The pre-release package that exposed the
archive-relative defect is retained only as
`dhe-toolchain-0.1.32-opt4.17-failed-archive-relative-paths` and is not an
authorized release.
