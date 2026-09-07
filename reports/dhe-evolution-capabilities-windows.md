# DHE evolution capabilities: Windows checkpoint

## Status

The new capability fixture now executes successfully on a real Unity 2021
Windows Base Player. Consecutive and skipped resource updates also pass without
changing its native binaries or embedded Base MV. This is exploratory evidence
for one Base identity, not complete DHE qualification or a formal release.

## Exact sources

All source changes remain on isolated research worktrees. No formal branch, tag,
production channel, or CAT package was changed in this iteration.

| Component | Commit |
|---|---|
| HybridCLR | `32d23ae784eb9e089a2b72885507538431009e4c` |
| Unity 2021 hooks | `4d76aca57497d48e17d4befeec7c4c9ca40a836c` |
| Unity 2022 hooks | `3e30e0ea6306e03ec347f3a98f5d9f3728283076` |
| Tuanjie hooks | `240e76804658189d38777608b13b583e4c950920` |
| Unity package | `80c00e785979aba730029dc1315de6a723fcef31` |
| Native tests and Base workflow source | `c6798bb627a4b7646e2f62c29edd0e837c24d68e` |
| Frozen replay source | `686e0cbfc325fb844a97f2b5718830be792fa0ac` |

Runtime contract is `dhe-runtime-v5`; the MV wire format and stable identity
algorithm are unchanged. Package Installer version selection still references
the previously released runtime tags. Do not migrate this candidate through the
normal Installer and assume it will obtain the candidate native runtime.

## Failures resolved

1. Future reflection and awaiter APIs were stripped from the initial Base.
   The package now owns a UnityLinker processor that preserves all DHE roots,
   resolves external type forwarders using actual target linker inputs, and
   supports `dhePreserveAotAssemblies`. Its default retains mscorlib, System,
   and System.Core in full. Reference-only preservation was independently tested
   and proved insufficient when a future type was absent from Base hotfix code.
2. Bodies on entirely new types resolved existing types to a hidden current
   class, causing ordinary reflection invocation to reject a Base instance.
   They now use the merged Base/current body-resolution view.
3. Inflated supplemental method aliases have a Base declaring class but no Base
   method token. The runtime now recognizes their supplemental image when
   selecting an interpreter implementation for generic reflection invocation.
4. The standalone native metadata test stub returned address 1 instead of an
   image object. It now uses a valid minimal image, the real engine lock, and
   explicit supplemental/inflated/unchanged-method assertions.

Each runtime capability addition is gated offline. Old Base capabilities cannot
be rewritten to acquire a native fix through a resource package.

## Current evidence

All paths below are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908/`.

- `native-generic-verified/<profile>/native-gate.json`: Unity 2021, Unity 2022,
  and Tuanjie compile/CTest pass, `mergeReady=true`, real headers,
  `surrogateExternalHeadersUsed=false`. FGS tests are enabled for the latter two.
- `compatibility-frozen/report.json`: 12 positive/negative compatibility checks
  pass against the prepared capability fixture.
- `linker-frozen-6e23fe0/report.json`: nine linker-preservation checks pass.
  The tested preservation helper is unchanged in the final package candidate.
- `base-generic-u21/project-workflow-report.json`: fresh bootstrap workflow and
  no-op Player passed with Unity 2021.3.45f2, OptimizeSpeed, supplemental metadata.
- `player-generic-u21.json`: first complete capability update passed with 96
  changed methods. The main dispatch probes observed 22 interpreter entries and
  56 AOT entries; these are probe counts, not totals for the entire process.
- `replay-u21-capabilities/report.json`: clean frozen replay passed, three unique
  PIDs, one distinct Base, two resource releases, 48 required outer assertions
  per run, ten immutable files checked per run. The fixture additionally invokes
  the method/parameter attribute, generic reflection, new-type identity, real
  async completion, iterator, and current assembly-reference assertions.

Replay report SHA-256:
`cbbee04733a0735e1bf7eb3fb39f2b16094f2cad7d9565d672385f176a594399`.

| Sequence | PID | Changed methods | Result |
|---|---:|---:|---|
| Base -> first update | 33664 | 96 | 48/48 |
| First -> second update | 25132 | 97 | 48/48 |
| Independent Base -> second update | 9212 | 97 | 48/48 |

Base ID:
`3aff50dabece38d3dba481cc4eda83ace4b100ca1048c9aadfcdcc951267c286`.

The initial capability payload was prepared by Unity from the raw DLL whose
SHA-256 is `dff124a486883db99ae268dfe634f774087bfd02623c877bc9ab2580552c4d47`.
Its assertions also ran successfully on the CLR. The separate 220-case CLR
reference report is `reference-current.json`; there is no corresponding complete
220-case DHE Player differential result for this candidate yet.

## Cost and remaining work

On this Windows Demo, preserving the three framework assemblies increased
GameAssembly.dll from 15.17 MiB to 34.57 MiB and the selected supplemental metadata
from 2.21 MiB to 7.32 MiB (`base-u21` versus `base-future-u21`). These are file
sizes from the same fixture/native runtime with a changed preservation policy,
not resident memory, performance, or mobile measurements.

The original six-Base structural replay belongs to older native capabilities.
It must not be presented as cross-Base proof for this new runtime. Still required:

- Audit and test new-type declaration references as well as method-body token
  resolution: fields, method signatures, generic arguments, and inheritance.
- Run this fixture on Unity 2022 and Tuanjie Windows Players.
- Build a newer Base that already includes structural evolution, then update old
  and new capable Bases using one current release, including skipped updates.
- Add member-level availability validation for archived AOT APIs; assembly-name
  presence alone did not detect the initial stripped API failures.
- Expand remaining structural/ABI capability and full managed differential,
  then measure steady-state, startup, tail latency, and memory costs.
- Complete source review and exact-identity qualification before formal branch
  integration and an Android project handoff. iOS/macOS remain untested.

Archived failed Bases and reports are retained as regression inputs. No native
binary rollback can be shipped as resources: select another verified resource
release within the Base's capabilities, or ship a new Base for native changes.
