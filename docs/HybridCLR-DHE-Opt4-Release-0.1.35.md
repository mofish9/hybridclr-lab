# HybridCLR DHE opt4 toolchain 0.1.35 release report

## Status

Toolchain 0.1.35 is the current conditionally accepted DHE Release line. The
immutable C# package is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.35-opt4.27`. Its Package ID is
`fa7c50ea46b39856ab77e1eed49b55639ce652299d971651b50b0f8c9664becb`.

The package records `mode=Release`, `releaseReady=true`, 117 authenticated
files, and no PowerShell, batch, command, or shell launcher. Its exact identity
is:

- source commit: `87f3c8b6cec9922e2a18ea58d0dd3fb81d4a6416`;
- source tree: `7d2f002a7c310e3af4fc7657c3d7dd12fac3d9bf`;
- package manifest SHA-256:
  `52d2b777cd981203e4c37d2edd87c3f436c6d4ea13e02115e7a4e09068a4823d`.

The same clean source produced exploratory Package ID
`2cc00ced13a7a82cb94e4310ce0a24e39f753a10bf9236d5724c69b1be6f7207`
on both C: and D:. External package-source compilation completed with zero
warnings and zero errors. Source-host and package-host verification, doctor, and
schema gates all passed without writing `bin` or `obj` into the Release package.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE tool source | `optimize/dhe-android-device-v8.13.0` | `87f3c8b6cec9922e2a18ea58d0dd3fb81d4a6416` | none |

No runtime, il2cpp_plus, or hybridclr_unity source changed in this release.
Existing runtime tags remain immutable, and hybridclr_unity remains a maintenance
branch without a package opt tag.

## Resource release planner

`resource-release-plan` makes the Base-to-runner mapping an authenticated release
input. It consumes one channel snapshot, one Release resource update, and one
runner catalog bound to the exact registry ID, revision, and SHA-256. The catalog
must cover every active compatible Base exactly once.

The command emits:

- `dhe-resource-release-plan.json`, recording every Base, runner, payload variant,
  immutable Player input, and evidence-authority decision;
- `dhe-resource-release-qualification-config.json`, ready for the existing C#
  `resource-release-qualify` command;
- audit copies and a passing schema gate.

Planning does not start a Player or promote a channel. A missing or duplicate Base,
registry identity drift, stale channel snapshot, invalid external report template,
or unknown template token fails before output publication. Qualification and
`channel-state promote` remain separate operations, preserving the existing
exact-coverage and compare-and-swap boundaries.

## Formal evidence

The clean C: source regression passed 163/163 checks. The six new checks cover:

- a generated plan followed by its generated qualification;
- missing active-Base rejection;
- duplicate Base rejection;
- registry identity rejection;
- external report-template token and identity rejection;
- stale protected-channel snapshot rejection.

The same run revalidated the existing 13 changed Windows Base Player reports, one
no-op Base report, all three native roles, all three generated-C++ resolver roles,
the complete historical Release authority set, consecutive revision 7 to 8
resource staging, protected-channel CAS, and post-commit recovery. No new Player
or Editor build was performed for 0.1.35; the immutable 0.1.34 evidence was
revalidated by the clean current source.

- regression report SHA-256:
  `0d906484bcf8558f1c18c178e1b7125bf310ca11a6bed5c9083ae5ae854f887d`;
- release evidence SHA-256:
  `0de5880d93f53003b3649da4132256f765d94db4a761008dc1412c087b49eb9d`;
- Release package verification SHA-256:
  `b1809030d7296570adae9db0053327649a2db2ba61625b0454b4a3d63bf67499`;
- package doctor SHA-256:
  `22fab1e5aae910518263f0a7ab5a9d326ad60e013cc2145ed35b3cc491c2f895`;
- package schema gate SHA-256:
  `f2371148a341d768a965ba181ca0e0b508a65126e9c5b2497b7cf12af5afba69`.

The protected lab channel remains at revision 8. This toolchain release does not
create or promote a new game resource revision; the next legal resource release
revision remains 9.

## Remaining gates

- Android ARM64 still lacks device correctness, PSS/RSS, thermal, weak-core, and
  tail-latency evidence for this source identity.
- iOS/macOS still lacks Xcode generation/link/signing, IPA, device, and performance
  evidence. The existing iOS gate covers export structure only.
- CAT still requires its own active Base registry, runner catalog, resource build,
  one Player or device result per active Base, and production performance and
  memory qualification.
- Unsupported ABI, native layout, vtable, GC, P/Invoke, and declaration changes
  continue to fail closed and require a new Base Player.

This release is therefore conditionally accepted for the proven three-engine
Windows workflow. It is not Android, iOS, CAT, or production performance evidence.

## Rollback

For tool rollback, pin `HybridCLRDhe-0.1.34-opt4.26`, Package ID
`80b3d477927eb7018268f0e9951086ae1b59a4b2fbea0ffb5c006ad2ed481eb5`.
Do not rewrite the promoted revision 8 channel head. Application rollback is a new
forward resource revision derived from the actual protected head.
