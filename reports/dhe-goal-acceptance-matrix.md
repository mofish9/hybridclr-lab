# DHE goal acceptance matrix (Unity 2022 Windows)

| Goal requirement | Authoritative evidence | Status |
|---|---|---|
| Base hotfix assemblies are AOT-capable and guarded | `new-base-05/base/build-identity.json`, native manifest, final Player build | Passed for the tested fixture set |
| Later Current can update those assemblies | `shared-three-base-01/result.json`, three standard resource Player runs | Passed |
| Unchanged paths retain the selected differential/AOT execution plan | no-op and complete assembly-mode checks in the resource workflow | Passed for tested fixture assemblies; method-level AOT cost still needs profiling |
| Different historical Bases consume one Current | `shared-three-base-01/result.json`, three distinct Base IDs and `one-current-payload=true` | Passed |
| Code and assets are one immutable delivery | `three-base-delivery-01/result.json`, manifest SHA `904F4624...` | Passed |
| Asset source matches the exact Current | `authoring-03/authored/asset-build.json`, provenance suite 16/16 | Passed |
| Invalid/missing delivery data fails before native effects | three Base missing-asset and wrong-hash runs | Passed |
| Base identity mismatch is rejected | new Base against Base119 resource, prepare-stage failure | Passed |
| Native public-image capability is admitted at build time | package header gate plus new Base capability token | Passed for new Base build |
| Windows startup/prepare/load/assets cost is measured | C# concurrent timing sample, 100/100 | Exploratory only: P50 16.574s, P95 17.026s, P99 18.769s; not an isolated AOT-vs-interpreter benchmark |
| Android ARM64/iOS behavior | No device evidence available | Not tested |
| Formal release branch/tag and production download auth | Candidate branches only | Not released |

The matrix deliberately limits claims to the assemblies, Unity version, runtime
commits and asset forms exercised by the fixtures. It does not imply automatic
saved-data migration, arbitrary Unity serialization compatibility, or mobile
performance.

## Locked handoff

- Package: `research/dhe-asset-provenance-v8.13.0`, `c3d61a9d9c2997753685b5f3cb14a235614f46cb`.
- Lab: `research/dhe-asset-provenance-v8.13.0`, `208d96cdba502e3c262222455160ab45288500a1`.
- HybridCLR: `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`.
- Unity 2022 IL2CPP: `ecad8a09d1eb9b91a57c59fcdc69b268377bad59`.
- Delivery: `F:/hybridclr_artifacts/dhe-asset-provenance/three-base-delivery-01`.
