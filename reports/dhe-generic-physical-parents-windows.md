# Generic physical parents: Unity 2022 Windows candidate

## Result and boundary

An existing non-generic AOT owner can now change from `ProcessorRoot` to the
new cross-assembly `GenericParent<Packet> : ProcessorRoot` through resources
alone. `Packet` is a value type containing a managed reference. One unchanged
Current payload passes on archived Base103, Base102 and Base101 without
rebuilding or replacing their Player/GameAssembly binaries.

| Gate | Base103 | Base102 | Base101 |
|---|---:|---:|---:|
| Business reference sequence | 46/46 | 46/46 | 46/46 |
| Virtual signatures | 25/25 | 25/25 | 25/25 |
| Framework callbacks | 18/18 | 18/18 | 18/18 |
| Generic physical parent checks | 29/29 | 29/29 | 29/29 |
| Standard resource workflow and immutable Player checks | pass | pass | pass |

The 29 generic checks cover closed parent identity, generic fields and methods,
virtual/root/interface dispatch, property/event reflection, static isolation,
reference-containing value-field GC, independent objects and constructor
exceptions. Observed names/order agree with the CLR reference. Independent
resource audit passes three Bases, 46 cases, six successful runs and 179 files.
Base103 additionally passes native preparation failure, fresh-process recovery
and Component lifecycle probes (10 aggregate checks).

This is conditional correctness evidence for this subset on Unity 2022.3.62f3
Windows, not unrestricted hotfix or production qualification. Generic owners
remain rejected. A Base already containing the generic parent in AOT, subsequent
generic argument replacement/removal, generic Scene/Prefab parents, old cached
objects across parent changes, native publication stress, performance/memory
and mobile platforms remain unqualified. Previous no-op AOT routing evidence
belongs to the preceding workload; this round does not establish no-op routing
for a newly built generic-parent Base.

## Implementation and review

Only the lab producer and fixtures changed; package/runtime binaries did not.
Parent analysis uses structured signatures with generic argument substitution
and cached child hashes. Each Base and Current is resolved against its own
assembly graph. External immutable boundary identity must agree. Admission
retains rejection of cycles, missing/duplicate peers, invalid parent kinds,
incorrect arity and malformed generic signatures. Bare type parameters cannot
be parents, non-generic definitions cannot be wrapped as generic instances, and
nested instantiation arity is checked. Cached structural hashes avoid
exponential string growth during repeated substitutions.

The old producer rejects this exact valid Current with
`existing-type-layout-or-vtable-change` for `HybridCLR.Lab.VirtualSignatures.Processor`.
That control is retained under `admission-control-01`. The MV binary format and
identity are unchanged; the previous-tool byte comparison remains historical
supporting evidence. Final producer policy passes 32/32, preceding cross-parent
policy 29/29 and execution-plan tests 126/126. Native ABI/publication code was
not changed; its separate real-header gate is recorded in
[the snapshot-package report](dhe-snapshot-package-native-windows.md), not rerun
or relabelled as new native evidence here.

## Exact identity

Final host/tool and resource/replay gates bind clean lab commit
`695fa72d132e7fccc209c3f03f989cb960c03e66` on
`research/dhe-generic-physical-parents-v8.13.0`. This report's later documentation
commit does not replace that tested source identity.

| Component | Commit or SHA-256 |
|---|---|
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Package in Base103 | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Package in Base102/Base101 | `841abfd46e122343717fe4115186b97a215b58df` |
| Host (`host-07`) | `F1A99EA53C63C5246EBBDE42DF913B3425B10FFA5466C3C2FDAF825B149B2C50` |
| Tool (`tool-03`) | `49FF5AD2D488D7ADD43CE9F2BC0127EC348919FE48C6374798A457EBEAE7A599` |
| Current assembly set | `e77b0153c9c737e85b74f127fb052ee569aea282b322b1b538f96cf99b9217b0` |
| Shared resource manifest | `DA6D32FFDBAB75BFD5F1E503236268A60FDBE768DCABE633161B4C131245E759` |

`current-02/current` was compiled from workload commit `30e78f0`; its bytes have
not changed. `reference-01` belongs to the earlier host and identical Current.
Intermediate `eb34a3d` runs remain historical; the following final runs rebind
the tightened producer at `695fa72` without rebuilding Base or Current.

Evidence root: `C:/hybridclr_optimize/artifacts/dhe-generic-physical-parents`.

| Result | SHA-256 |
|---|---|
| `policy-04/result.json` | `A14EEF8C33F5B9DE23B06BA6E2A6717B41B3AB83119CC81B9708D3106E33F80C` |
| `cross-policy-regression-02/result.json` | `2175AF93620CC946AA7F030A1C33CC900C9C04C00CACFDA17164E84D422F00F5` |
| `execution-plan-02/result.json` | `22F0100273459B37AD343B71907B223B38A8081E2D9501691D587E095F59C198` |
| `reference-01/result.json` | `852E2993154129EA7EF024073FACFB6445DF0DBBC4F13E643CED16F39147E424` |
| `shared-three-base-02/result.json` | `A9B311C4A164EB28ADE0732D93CAE45E2BCCA3CDD328318E1B1E1ACE08C0F031` |
| `shared-audit-02.json` | `DDF0A5D54B39282F7C94961CBD07B110B860369C9974AE836D9D26A99FA5CCBC` |
| `replay-base-103-shared-02/result.json` | `8AFD6038E88C452D427F6A246C4472C34E42216B95FF94A66125BE2ED4996DD5` |
| `replay-base-102-shared-02/result.json` | `DD97E45505B05FAF4E7E1958A87C80032A1EEEC3DD8A17BC3B89111111129B3A` |
| `replay-base-101-shared-02/result.json` | `B6334E9AD7405484A9265F6ECC5A54CE078026157E09B3BB756F7C67C3CF9E2E` |
| `public-probes-base103-02/result.json` | `82A9A57E4A6A22C772A20236273B4ADA4FD634D0D5B3DFDD20ABEAE02F4EC058` |

## Reproduce and roll back

Use the C# host `frozen-resource-workflow` with ordered archived proofs Base103,
Base102, Base101, the explicit final tool, a fresh output and `current-02/current`.
Run `generic-physical-parent-replay` for each proof against that shared output,
then `frozen-resource-audit` against the original Current. Run
`unity-public-probes` with Base103, the shared output, `:current:` and a fresh
output. All command implementations are C#; no shell business workflow was added.

Rollback selects the preceding producer at `c5a2541` and its compatible archived
resource, then restarts the Player. It must not reset already published native
metadata in place. No formal maintenance branch, runtime tag, Installer default
or CAT project was changed. Existing Players, failed attempts and reports remain
preserved. The next targeted step is a generic-parent AOT Base plus no-op and
subsequent argument/parent evolution, followed by the other stated boundaries.
