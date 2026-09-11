# Generic owner parent evolution: Unity 2022 Windows

## Result and scope

The producer now admits the existing generic owner `GenericParent<T>` changing
from ProcessorRoot to a new `GenericMiddle<T> : ProcessorRoot`, while retaining
its type parameters and constraints. Base104 already AOT-compiles that owner;
Base103 does not contain it. Both immutable Players execute the identical Current
resource successfully. No runtime/package change or new Base build was needed.

| Gate | Base104 | Base103 |
|---|---:|---:|
| Business reference sequence | 46/46 | 46/46 |
| Virtual signatures | 25/25 | 25/25 |
| Framework callbacks | 18/18 | 18/18 |
| Existing generic-parent checks | 29/29 | 29/29 |
| Generic owner checks | 31/31 | 31/31 |
| Standard resource and immutable Player verification | pass | pass |

The owner suite covers open parent metadata, closed value/long/reference
instances, constructor order/count, fields at three inheritance levels, arrays,
generic and virtual methods, field/property/method reflection, root dispatch,
exceptions, static isolation and GC through the existing physical Processor.
The same case sequence passes on CLR. Independent audit passes two Bases,
46 business cases, four successful runs and 125 files. Base104 preparation
failure, fresh-process recovery and Component lifecycle probes also pass.

This is conditional correctness qualification on Unity 2022.3.62f3 Windows.
It does not establish arbitrary parameter/constraint changes, all nested generic
inheritance graphs, generic Scene/Prefab parents, retained old-object migration,
native publication stress, performance/memory or mobile behavior. Existing
no-op routing evidence is recorded in the preceding report; it was not repeated
or converted into a new performance claim. Older runtime identities are not
qualified merely because they export a similarly named capability.

## Change and review

The old producer rejects the exact valid Current with
``HybridCLR.ValueLayoutOther:existing-type-layout-or-vtable-change:HybridCLR.Lab.GenericPhysicalParents.GenericParent`1``.
The original failure is retained under `admission-control-01`; `policy-control-01`
also records the five expected positive cases rejected before implementation.

Parent traversal now starts with symbolic owner parameters and substitutes them
through each parent definition. It compares the final immutable boundary for
all instantiations, rather than choosing one concrete argument. The existing
parent-independent layout hash still requires unchanged owner declarations,
including arity and constraints. Generic parameter numbering is validated for
owners, parents and internal argument definitions. Out-of-range variables,
method variables, bare-variable parents, cycles, changed constraints, changed
arity, swapped external arguments and external root changes remain rejected.

The new policy gate passes 26 checks, preceding generic policy 32, cross-assembly
policy 29 and public execution-plan tests 126. MV bytes compare identically with
the preceding tool for the original/current workload assemblies. The changes
are admission-only metadata facts and traversal; the binary MV format, runtime
ABI, locking and native publication code remain unchanged. The prior real-header
native gate remains separate evidence in
[the snapshot-package report](dhe-snapshot-package-native-windows.md), with
`mergeReady=true`, `surrogateExternalHeadersUsed=false`; it was not rerun here.

## Source and artifact identity

The isolated branch is `research/dhe-generic-owners-v8.13.0`. Final resource,
replay and regression tests bind clean source commit
`db4d17e768dde0d09be2c869fc349834ba5c68cb`. The host was compiled from the same
source immediately before that implementation commit; final policy runs were
repeated after the commit. Current generation and the CLR reference use the
earlier fixture host at `0f003b1`; their Current bytes did not change afterwards.
This later documentation commit does not replace those identities.

| Component | Identity |
|---|---|
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Package in both Bases | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Final host (`host-03`) SHA-256 | `35A28A53A959F26B68603A54EF497AAF4FCD9647F158E4BFC18AC5D319120DEF` |
| Tool (`tool-01`) SHA-256 | `409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4` |
| Current set SHA-256 | `00f5d4154c1c627a7f50abf5239d67d239f251060ab5d90264d387cd56887c47` |
| Shared resource manifest SHA-256 | `6D56824372B5FA7BED044DB915FC876EB8DEE98119190BD3DFC914A5638B185A` |

Evidence root: `C:/hybridclr_optimize/artifacts/dhe-generic-owners`.

| Result | SHA-256 |
|---|---|
| `policy-02/result.json` | `769068B8746B057223A9DDA30B3D3DEFB7749DD5787F42FCD851345EBBCABECC` |
| `generic-policy-regression-02/result.json` | `41CD1544E320A12EA30F8461AB940AB2078CC956410CFB0A5491B177A06777E1` |
| `cross-policy-regression-01/result.json` | `9E956949496322DFBA57A5E600DDF42B8F7F81BB62DBBA506DF78B61B8C5B1D1` |
| `execution-plan-01.json` | `1BD155C37239F098BC4B0E0D57473FE4725DF7800018EF3AC7E9A2D2D8069220` |
| `reference-01/result.json` | `D0C4FFA26909F8997747DC7BC60812ED602D56144F4D67222EF1D650087E3E38` |
| `shared-two-base-01/result.json` | `9B03D5E8D449BAF1A38D5A8C896BEE6C334C7BE1E65DF1465AAAB5A87035FA1A` |
| `shared-audit-01.json` | `BBDA306BB91D4AB8A8A71F4D67D89422FEB5AF944A2F2E36CFC0C76080D4DA36` |
| `replay-base-104-01/result.json` | `3ACFECD66ADC5303E75FB196B2D3352167AF4845BE2067D3477B90002F907EB5` |
| `replay-base-103-01/result.json` | `90358B030067C24C532A583DF025ED90C9F066D2DD8110DCF884C017D9FF30E5` |
| `public-probes-base104-01/result.json` | `20A51ED70526F0CD6DA327EE30D03B7FEDFE33BA615968890D1AF9C86A768F4C` |

The additional failure/lifecycle evidence is
`public-probes-base104-01/result.json`; its input identities and process records
are included in that report. All tests completed; no build or Player remains
pending. The lab, HybridCLR, IL2CPP and package candidate trees were checked clean
before documentation. No stash, deletion, formal branch/tag, Installer or CAT
change was made.

## Reproduce and continue

Use the C# host `generic-owner-current` with Base104 and archived
`dhe-generic-physical-parents/current-02/current`, then `generic-owner-reference`
with Base104's authenticated Native DLL. Run the final tool through
`frozen-resource-workflow` with ordered Base104/103 and this exact Current;
follow with `generic-owner-replay` for each Base, `frozen-resource-audit` and
Base104 `unity-public-probes` using `:current:`. All output directories must be
new. The admission controls and final producer are separate preserved artifacts.

Rollback the producer change by reverting `db4d17e` or selecting `897fa5a` and
its compatible resource, then restart the Player. Do not use the old producer
to publish the newly unsupported resource or reset native metadata in place.
Next cover richer generic substitutions and deleted generic lookup semantics,
then address the outstanding Scene/Prefab, old-object and unified qualification
work rather than treating this passing subset as the full goal.
