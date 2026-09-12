# Unity 2022 DHE maintenance integration, 2026-09-12

Historical integration snapshot. Subsequent opt5 publication and its package
identity are recorded in [dhe-opt5-release.md](dhe-opt5-release.md).

The latest DHE sources are included in the three maintenance branches under
`repos`. The maintainer explicitly requested no new tags; none were created or
moved. This source integration is conditionally qualified for Windows project
testing, not a mobile production release.

| Repository | Maintenance branch | Commit |
|---|---|---|
| HybridCLR | optimize/v8.13.0 | b0fe826f071332d109d2bde87c0aa2cc18b9f3c7 |
| il2cpp_plus | optimize/unity2022-v8.14.0 | 658aa64923e568a497e11640b340f316704d02f9 |
| hybridclr_unity | optimize/v8.14.1 | c6a7aff28f509144b8654e2a1c37c57f1de34971 |

HybridCLR was fast-forwarded. The new versioned IL2CPP and package maintenance
branches include the previous maintenance heads and the complete candidate
history. The old package 8.13.0 and IL2CPP 8.11.0 branches remain historical lines.
Unity 2021 still selects official HybridCLR v8.13.0 / IL2CPP v2021-8.1.0;
Tuanjie retains its earlier runtime tags. Package Installer now selects the
explicit fork repositories and maintenance branches and verifies full commits.

## Current checks

Artifacts: `F:/hybridclr_artifacts/dhe-maintenance-20260912`.

- `runtime/DHE-Unity2022/runtime-manifest.json`: assembly from the three clean
  `repos` checkouts; verifies all three current commit/tree locks.
- `native/DHE-Unity2022/native-gate.json`: passed, mergeReady=true, FGS enabled,
  real Unity 2022.3.62f3 headers, surrogateExternalHeadersUsed=false.
  SHA-256: B7ACC9FB0303B6C8C0E6C17D54BE9F2A042C2FF04B2CC68EDE3A65704CD24F04.
- `execution-regression.json`: 126/126 checks passed against the maintenance
  package. SHA-256: 5F35FB8AC35F1D8F844CD49AB67F9138C76D603E2A35C4857E0D6AFB4B70C9D7.
- `installer-result.json`: real Unity default Installer cloned the maintenance
  runtime branches from the fork URLs, verified commits and installed 930 files
  matching the independently assembled runtime byte-for-byte.
  SHA-256: 410540A087FDE59AA400B303CA7E0EA7FAE99B2085D52669F5DA86ECCE006B95.
  `installer-02.log` records the run; the first minimal project lacked built-in
  JSONSerialize and was rejected at compilation. The successful fixture enables
  JSONSerialize, AndroidJNI and AssetBundle modules, as did the existing demo.

The Unity import appended importer metadata to one known hash-excluded .meta
file; that test-generated edit was restored. No user project was modified.

## Evidence limits and rollback

Historical full Base/Player and multi-Base records remain at their original
package/runtime identities. The complete Player workflow was not rerun for this
Installer/manifest-only integration. Native source matches the validated trial;
Android, iOS, performance and memory remain separate gates.

For pre-integration source rollback, archived maintenance heads are HybridCLR
fe3b1edb222511a1d3227f7e76e8b83b618c4d27, IL2CPP
60322744721410e79203155fc455be4232c3df4b and package
f2d4c0fb34b039e09c94eb8635ee8ecef2a0caa2. Rebuild a Base when changing native
implementation. Existing Players roll back only to compatible archived deliveries
with a process restart. No tag rewrite or force-push is needed.

MV/multi-Base CLI remains in Lab under its established responsibility. Package
consumers need that C# toolchain in addition to the three maintenance repositories.
Old handoff-final zip files contain stale locks and are superseded; obtain the
package from its maintenance branch and freshly publish the synchronized Lab CLI.
