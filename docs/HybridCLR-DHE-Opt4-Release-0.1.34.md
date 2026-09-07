# HybridCLR DHE opt4 toolchain 0.1.34 release report

## Status

Toolchain 0.1.34 is the current conditionally accepted DHE Release line. The
immutable C# package is
`C:/hybridclr_optimize/releases/HybridCLRDhe-0.1.34-opt4.26`. Its Package ID is
`80b3d477927eb7018268f0e9951086ae1b59a4b2fbea0ffb5c006ad2ed481eb5`.

The package records `mode=Release`, `releaseReady=true`, 110 authenticated
files, and no PowerShell, batch, command, or shell launcher. Its exact identity
is:

- source commit: `00ef0b4d765f1f45801f98d35c20fa2a9757e308`;
- source tree: `9de93f9d3b9d6bf36c17a88e31ac78c731449d9e`;
- package manifest SHA-256:
  `dbd0186c1b29ca89f983e64a99fbac844af6ed2b4dab6af15ba90ce8c435316d`.

External package-source compilation completed with zero warnings and zero
errors. Source-host and package-host verification, doctor, and schema gates all
passed.

## Locked sources

| Repository/lane | Branch | Commit | Runtime tag |
|---|---|---|---|
| HybridCLR | `optimize/v8.13.0` | `fe3b1edb222511a1d3227f7e76e8b83b618c4d27` | `v8.13.0-opt4.2` |
| il2cpp_plus Unity 2021 | `optimize/unity2021-v8.1.0` | `b3fdf1ef70b63758dc6598c674ffb38f3534c4e6` | `v2021-8.1.0-opt4.1` |
| il2cpp_plus Unity 2022 | `optimize/unity2022-v8.11.0` | `60322744721410e79203155fc455be4232c3df4b` | `v2022-8.11.0-opt4.1` |
| il2cpp_plus Tuanjie 2022 | `optimize/tuanjie-1.10-v8.13.0` | `52968ad6c88416f212d09d919b9a1b6afdc8a53b` | `v2022-tuanjie-8.13.0-opt4.1` |
| hybridclr_unity | `optimize/v8.13.0` | `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10` | none by policy |
| DHE tool source | `optimize/dhe-android-device-v8.13.0` | `00ef0b4d765f1f45801f98d35c20fa2a9757e308` | none |

No runtime or Unity package source changed in this release. Existing runtime
tags remain immutable, and `hybridclr_unity` remains a maintenance branch
without a package opt tag.

## Multi-Base qualification command

`resource-release-qualify` turns the per-Base staging, Player execution,
resource Player evidence, and aggregate gate sequence into one config-driven C#
operation. It checks that configured Base IDs exactly equal the active compatible
Base set before any Player starts.

The `process` runner uses direct .NET process execution. It requires exact
standalone `{result}` and `{log}` argument tokens, immutable Player file hashes,
an executable, a matching Base build identity, and a Base workflow report. It
does not invoke a shell. The `prequalified` runner accepts a complete resource
Player workflow report from an external platform job and subjects it to the same
aggregate package, ledger, Base, payload, and channel checks.

A failed run may retain diagnostics under its per-Base output, but removes the
top-level passing summary and promotion gate. Qualification never advances the
protected channel; `channel-state promote` remains a separate compare-and-swap
operation.

## Formal evidence

The clean source regression passed 157/157 checks. Four new checks cover a full
prequalified qualification, missing active-Base rejection, process runner token
and immutable-file rejection, and stale snapshot rejection. The regression also
revalidated the existing release ledger, exact 13-Base Player set, three-engine
resolver/native matrix, authority set, protected release build, concurrent CAS,
and post-commit snapshot recovery.

- regression report SHA-256:
  `f0b80f71fdaa842bd9b79e9f46ffde11848a429392c1d235b757f27f7c129ab3`;
- release evidence SHA-256:
  `debe76f1fcdd42e38b1a9256dd7f0a45d22ef9bdd0982ec8f38509e3ce98a630`.

Release evidence contains eight fixed reports plus all 13 changed Player
reports. The historical authority manifest exactly covers both 0.1.20 Release
identities and every Release package from 0.1.21 through 0.1.33.

## Revision 8 qualification and promotion

One revision 8 resource candidate retained registry revision 4 and all 13 active
Bases. It carried the existing `windows` and `android` managed payload variants.
Its important identities are:

- release channel: `dhe-builder-six-base-e2e`;
- resource manifest SHA-256:
  `4e64a90b483994b4fc165458920a668c2610b07feddeb415382ea742d8424f01`;
- resource validation SHA-256:
  `4863ebaf705e422620909268b1e37b45e3a0e0e0e2617037fc7ec80185c272ee`;
- release ledger SHA-256:
  `6bc1c863eecd275dca8b391882845ff4d613d4f3cf2d8e697956d311b8f505c7`;
- parent ledger SHA-256:
  `b97ca6b9f1437cb45c9b119a6c2e0ad53e615e22e2810a0c3901ff95b9ca782d`;
- Base registry SHA-256:
  `a5179eab94d83abbbe6e3c2c64d615604615cd0834e3a46d901882213203fe58`;
- payload variant-set SHA-256:
  `51358fb9761eee819c3433b044d7f650e1b1bd794a242c011764782ae896097e`.

The formal 0.1.34 package directly started 13 distinct Windows Player processes.
The set contains four Unity 2021 Standard, five Unity 2022 FGS, and four Tuanjie
2022 FGS Bases. Every process exited successfully, every selected Base reported
its expected changed-method count, and unchanged execution remained AOT. The
qualification records `activeBaseCount=13`, `processRunnerCount=13`, 13 unique
PIDs, `exactActiveBaseCoverage=true`, and `engineMatrixCovered=true`.

- qualification summary SHA-256:
  `b7a5e226195bde1a6f8208489c2831ed50f85a2aef3d94de6982fadda500964b`;
- aggregate gate SHA-256:
  `e0c219f098fa78bb1ccbbbfb20e89597cbd9b9b47bb3a3e14453509511282a72`.

CAS promotion advanced the protected channel from revision 7 head
`56b694ea8f39f623dfba10a4a248423bb73b579a35bf3d5983354e84cd40fbb4`
to revision 8 head
`06955d6a36be61ca1d54a317417b8f5c959df1a2423fd385dc816cd03e311bd6`.
The promoted snapshot SHA-256 is
`51ae2fc75eba40e8ebf98bb08df29b655a2826a19724213efdede88b6007adb6`.
The next legal release revision is 9.

## Remaining gates

- The `android` payload selections are cross-target managed payload validation
  in Windows Players. Android ARM64 still lacks device correctness, PSS/RSS,
  thermal, weak-core, and tail-latency evidence.
- iOS/macOS still lacks Xcode generation/link/signing, IPA, device, and
  performance evidence. The existing iOS gate covers export structure only.
- CAT still requires its own assembly registry, Base archives, resource catalog
  staging, one Player/device result per active Base, and production performance
  and memory qualification.
- Unsupported ABI, native layout, vtable, GC, P/Invoke, and declaration changes
  continue to fail closed and require a new Base Player.

This release is therefore conditionally accepted for the proven three-engine
Windows workflow. It is not Android, iOS, CAT, or production performance
evidence.

## Rollback

For tool rollback, pin `HybridCLRDhe-0.1.33-opt4.25`, Package ID
`1293dc857990633be5ac13d735d0e9012cc06c10923ddaf9cd8b247b6f63d3b7`.
Do not rewrite the promoted revision 8 channel head. Application rollback is a
new forward resource revision derived from the actual protected head. Revision 7
and revision 8 heads and their content-addressed artifacts remain immutable.
