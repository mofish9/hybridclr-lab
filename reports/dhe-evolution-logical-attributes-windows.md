# DHE logical Attribute members: Windows checkpoint

## Status and scope

The reproduced evolved-Base Attribute crash is fixed for the tested cases on
Unity 2021, Unity 2022, and Tuanjie 2022 Windows Players. Three original v8 Bases
and three repaired, structurally evolved v9 Bases consume the same two current
DLL/MV payloads, including skipped updates. This is exploratory correctness
evidence, not complete managed evolution, performance, or mobile qualification.

No formal branch, runtime tag, Installer version selection, released package,
protected channel, or CAT project changed. Runtime contract is `dhe-runtime-v9`,
capability `logical-attribute-members-v1`. MV remains `DHEMETA1`, schema 1.

## Source identities

| Component | Clean candidate commit |
|---|---|
| HybridCLR | `82e6829670411ad622ff8aece2dc3a8fb9e1913e` |
| Unity 2021 hooks | `7cf7f8f9db216edd60adc8cb5b3ef99af39b997d` |
| Unity 2022 hooks | `cf8b191aed51c7de3de9de7499734eea591c00e5` |
| Tuanjie hooks | `60c588118779f0bf76c6a593e3087ccd40b313ea` |
| Unity package | `addb4828fb882fc236be9e335b2e9ce840034ef2` |
| Compiled C# host source | `7b331df64c16681867311671a1acbeeb16920408` |
| Exploratory toolchain packaging | `838c9d262ca144d31511cc6dc26fa744a00f489d` |
| Final three-profile native tests | `a6adf573fd768f08deb93ab17e2e92ac7eae2dbe` |
| Frozen mixed-Base replay | `8de07cb027e1ddd833ff938b996942f510a1cf7c` |

Later lab commits change locks, fixtures, configurations and documentation, not
the compiled host implementation. The exploratory toolchain Package ID is
`099fc8621db49b851fb36f63d0249a8b3e24813ed588743601f79e8de5f1262b`.
It is not a new formal toolchain release.

## Repair and regression inputs

Logical properties are allocated separately from the physical Base property
array. Attribute conversion previously subtracted unrelated pointers and the
engine reader indexed the physical array with that result. Logical property
indexes now occupy a suffix after the physical property count. Engine readers
resolve that suffix through the published logical view. Tuanjie's physical
lazy-property path remains intact. Constructor decoding also resolves hidden
Current methods to their logical Base aliases.

The C# compatibility scan includes inherited and cross-assembly Attribute use.
Eleven tests pass. The old six-Base resource command now correctly rejects the
three affected evolved v8 Bases while accepting the three original v8 Bases.
Those failed Bases and their reports are unchanged. This repair is native and
cannot be retrofitted into those affected Bases through a resource manifest.

Native compilation additionally found an incorrect image-classification API,
zero-length-vtable aggregate initialization in the test, missing fixture image
names, and Tuanjie's const property-pointer layout. The API and fixtures were
corrected without removing assertions; all original failure logs are retained.

## Results

Paths below are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `native-logical-attributes-frozen/<profile>/native-gate.json`: all three pass,
  `mergeReady=true`, `surrogateExternalHeadersUsed=false`. Unity 2021 is non-FGS;
  Unity 2022 and Tuanjie exercise FGS.
- `base-logical-attributes-{u21,u22,tuanjie}/project-workflow-report.json`: all
  three new Base workflows, no-op AOT checks and schema gates pass. The Base
  fixture already contains the repeated structural carrier, but not the new
  Attribute named members/constructor overload.
- `replay-logical-attributes-u21/report.json`: the focused repaired-Base replay
  passes consecutive and skipped updates in three unique processes.
- `registry-logical-attributes.json`: six distinct Base IDs, two managed/runtime
  generations, one current payload variant per release. The known broken v8
  evolved Bases remain negative inputs, not members of this fresh test registry.
- `replay-logical-attributes-generations/report.json`: 18 runs and unique PIDs,
  792 successful named checks, 72 explicitly inapplicable checks, zero errors,
  54 comparisons against exact-current-DLL CLR scalar observations. Original
  executable, native DLL, Base MV and identity hashes remain unchanged.

| Generation | First resource changed methods | Latest changed methods | Checks/run |
|---|---:|---:|---:|
| Original v8 | 135 | 147 | 48 |
| Repaired evolved v9 | 3 | 43 | 40 plus 8 inapplicable |

Add(1) is 102 for the first resource and 105 for the latest, versus 101 in the
new Base. Stable(2) remains 4 and AddViaStable(2) remains 104. Every latest/skipped
run requires and records repeated structure, named Attribute field, named
Attribute property and constructor overload execution. The three new Bases use
supplemental metadata/OptimizeSpeed on Unity 2021 and no supplemental metadata/
FGS OptimizeSize on Unity 2022 and Tuanjie.

`reference-logical-attribute-latest-full.json` also executes all fourteen
declaration/evolution groups against the exact shipped current DLL, with
`reference-logical-attribute-full-checks.ids`. This is NOT the separate 220-case
managed suite. `generation-logical-attributes-tests/report.json` passes 36
generation/observation/receipt tests against the new Base fixture.

## Reproduction and workspace

The tracked replay configurations are
`manifests/dhe-logical-attribute-u21-windows.json` and
`manifests/dhe-logical-attribute-generations-windows.json`. Execute the C#
`runners/dhe-evolution` runner against a configuration with a new output directory.
The runtime assembly roots are under `runtime-logical-attributes-verified`;
the clean host and exploratory package are `host-logical-attributes-frozen` and
`toolchain-logical-attributes-verified`.

The new Base input is the lab's `artifacts/evolution-repeated-evidenced-raw`.
Its ManagedCasesAot DLL SHA-256 is
`F84629A6DAAB3B81C38F302A9EB17044CE50E5FEECEF4A84185CBC462F6B903B`.
Current roots are `artifacts/logical-attribute-update-first` and
`artifacts/logical-attribute-update-latest`, generated with `current-next` and
`-AdvanceObservableResult` from the prepared repeated fixture and previously
observed Attribute fixture. Each resource is generated against the entire new
registry, not against the previous resource's MV.

All six implementation/research worktrees are clean at the replay freeze.
The isolated Demo package-input commits are Unity 2021 `33dd383`, Unity 2022
`9652400`, and Tuanjie `c96517b`. Bootstrap subsequently stages the four explicit
DHE input DLLs under each Demo's Assets/Plugins/HybridCLRLab; these generated
input changes are retained for follow-on tests. They are not edits to archived
Players or formal repositories. Package trees match the candidate package lock.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Mixed-Base replay | `D75D5A6BDE4FCB694E970456C1C45491D289736D4C8C3849AA61F75CA782D93F` |
| Six-Base registry | `D89CEF3CA6983D99F25A5AF680B060465176394BFAF5A5D70FDE77E2847F659B` |
| First resource manifest | `EFA2C14312AFE545B2EF496294DFBFE69A0B5D241D9F20940F967526F0EDF9B1` |
| Latest resource manifest | `8D2EFBDBE137692DE4442281C9EE60102D0045518D66E09A3BFC01B269E92647` |
| Attribute compatibility tests | `A53036D4BB2947AF1F58C99460D8C253AF046D37C8AA7F00A596A236EFC81A0B` |
| Generation/receipt tests | `A99B01E0BA20EED31A1B47B10880339C8F46464FF33640E4658544195BC69AB7` |
| Full Attribute CLR reference | `59DE7B3DAC2EB4F27B22BFFCECDEBE06A05A40A0CFBCFD11FC5E77F301C84E7F` |

## Remaining scope

Still required: full 220-case DHE differential; member-level archived AOT API
availability; remaining generic-field, field-address, interface/vtable and
layout evolution; actual Player inheritance/cross-assembly Attribute cases beyond
the offline scan; concurrency/ABI review; performance, startup and memory costs;
final integrated workflow qualification and Android project handoff.

Rollback remains capability-bound: select a verified resource within each Base's
real capabilities, or create a new Base for native fixes. Do not relabel an old
Base, rewrite a release, or claim these Windows results qualify Android/iOS.
