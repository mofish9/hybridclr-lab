# DHE evolution implementation and validation

## Objective

Compile all configured hot-update assemblies into each Base Player, then update
their behavior through resources without rebuilding that Player. A newer Base
must coexist with older supported Bases and consume the same current release.
The implementation must support practical managed-code evolution, not only
method-body edits. Rejected evolution remains unfinished capability work, not
evidence that the full objective has been achieved.

## Starting identity and isolation

- Lab source: `6dbf6b1b8cbbfc4b82f17e7e01725bf038abd81a`.
- Existing tool implementation: `87f3c8b6cec9922e2a18ea58d0dd3fb81d4a6416`.
- HybridCLR runtime: `fe3b1edb222511a1d3227f7e76e8b83b618c4d27`.
- Unity package: `18abd01ca9847f06a64bde3cc9fc9e24a1b63d10`.
- Engine hooks: the exact commits in `manifests/dhe-runtime-lock.json`.
- Candidate branch: `research/dhe-evolution-v8.13.0`.

Existing releases, archived Bases, and the protected lab channel are immutable
inputs. Experiments use separate output roots and disposable Player copies.
Runtime changes, if required, use separate repository worktrees. No project
references or runtime tags are changed during exploratory validation.

## Scenarios and acceptance

1. Replay the structural fixture against archived Windows Bases under Unity
   2021 Standard, Unity 2022 FGS, and Tuanjie 2022 FGS. Record actual enabled
   assertions, not merely the top-level passed flag.
2. Cover added and removed types/methods/fields, reflection, GC retention of
   supplemental fields, delegates, generic calls, exceptions, and cross-assembly
   dispatch. Add regression tests before correcting discovered failures.
3. Exercise independent old/new Base generations with one current DLL/MV set,
   consecutive resource releases, and a client that skips an intermediate
   release. Hash executable, GameAssembly, and embedded Base metadata before
   and after staging. Their bytes must not change.
4. Classify remaining rejected business evolution by runtime/ABI cause, and
   implement missing capabilities rather than hiding them in fixture selection.
5. Freeze candidate commits and repeat the applicable native, managed, and real
   Player gates before integration. Historical evidence is labeled historical.

Correctness is the primary gate: every expected case must execute, with equal
reference/Player case counts and zero differences. Native matrix profiles use
their own real Editor headers. Unity 2021's non-FGS path is independent of the
two FGS paths. Supplemental AOT metadata is recorded per Base; no result is
silently extrapolated between metadata configurations or engine profiles.

Performance follows correctness: compare ordinary AOT, ordinary interpretation,
and DHE using identical workloads and inputs. Record steady-state time, load plus
first entry/reflection, P50/P95/P99, and memory. Guard overhead, interpreter
fallback growth, and supplemental-field memory are not excluded from totals.
Do not claim P99 acceptance with fewer than 100 independent processes. Do not
use instrumented correctness Players for production performance claims.

Windows is the available execution environment. Android device validation is a
user handoff after Windows verification, not a prerequisite to making progress
here. Windows results do not prove ARM64 memory ordering, Android performance,
or iOS compatibility. Existing Base runtime capabilities cannot be upgraded by
changing a resource manifest.

## Rollback and current progress

The candidate is independently reversible by returning to the starting commits
and existing released toolchain. Project DHE disable/fallback requires a Base
build when it changes the embedded native runtime; it is not a resource-only
rollback of native code. Resource rollback must be a forward resource release.

- [x] Inspect source, release identity, and current evidence limitations.
- [x] Create an isolated candidate worktree without changing formal releases.
- [x] Reproduce structural updates on existing Windows Base Players.
- [ ] Fix uncovered failures and expand realistic evolution coverage.
- [x] Verify the six selected old/new Bases and consecutive/skipped structural updates with one payload.
- [ ] Freeze, review, and prepare the verified Android project handoff.

## First replay and reproducibility repairs

The current `structural` fixture was recompiled and passed through the actual
Unity 2021 package `Prepare` phase. One resulting payload ran successfully on
Unity 2021, Unity 2022, and Tuanjie 2022 Windows Base Players. Three newer Base
generations also accepted and executed the same structural payload. The first
three Bases reported 71 changed methods, with structural assertions enabled.
These are fresh exploratory runs, not a reuse of the previous release's reports.

Two preparation issues were reproduced before reaching the Player:

- Raw dotnet DLLs are not the stripped Unity DLLs expected by this workflow.
  Unity rewrites framework references, security attributes, and PE flags. The
  raw/stripped incompatibility was kept intact, and the proper Unity preparation
  phase produced compatible current DLLs. Do not normalize these differences by
  copying metadata from a Base or suppressing compatibility errors.
- The tracked Demo package contains later iOS export helpers absent from the
  locked formal package, but omitted `RequireDirectory`, so a clean Windows
  Editor import failed with CS0103. The candidate supplies that missing helper;
  the next real Editor import and current generation passed. This does not
  qualify iOS export or resolve the package-source divergence. That divergence
  must be reconciled before a new formal handoff.

## Windows replay runner

`runners/dhe-evolution/HybridCLR.DheEvolutionRunner.csproj` is a lab-only C#
correctness runner, independent of production channel promotion. It copies
archived Players into a new output directory, stages each update through the
real tool, starts each Player, and requires all 48 named structural/dispatch
checks. It records actual process IDs, output hashes, source identity, and
immutable-file hashes. It also checks exact supported-Base coverage and preserves
the original executable, engine DLLs, Base metadata, and identity throughout the
sequence. The configured seventh replay skips update 1 on an independent copy
of the oldest Unity 2021 Base.

```text
dotnet run --project runners/dhe-evolution/HybridCLR.DheEvolutionRunner.csproj \
  --configuration Release -- manifests/dhe-evolution-windows.json
```

The checked-in configuration references this workspace's preserved Base
archives and two prepared structural resource roots. Regenerating the inputs
requires `build-managed-cases -Variant structural`, the Demo adapter's Unity
`Prepare` phase, then `resource-update` against the six-Base registry. The second
payload uses `build-managed-cases -Variant current-next -SeedCurrentRoot` on that
prepared structural set. It retains every structural change while advancing
two method bodies. Output directories must be new or empty; the runner never
deletes archives, releases, or previous replay evidence.

These 48 assertions are not the separate 220-case managed reference suite, and
the report is not a production release qualification or a performance result.
The frozen `c0ab3ac` replay completed 13 actual Windows processes with 13 unique
PIDs and 624 successful assertion evaluations. See
`reports/dhe-evolution-windows-c0ab3ac.md` for the exact evidence identity.

## Next capability failure: added attributed methods

`build-managed-cases -Variant evolution` extends the structural fixture with
ordinary and generic attributed methods, an asynchronous method that really
awaits, and an iterator on the existing calculator type. The current managed
entry invokes `DheEvolutionAssertions.Validate`, so an old Player's existing
structural smoke will exercise the new assertions without rebuilding its test
runner. Checks include direct/reflection invocation, instantiated and raw custom
attributes, inflated generic method attributes, and state-machine identity.

The unmodified host rejects the Unity-prepared evolution payload for all six
Bases. The four new entries trigger
`added-method-custom-attributes-on-existing-type`; using LINQ in the new fixture
also adds a System.Core reference and triggers
`assembly-or-module-metadata-change`. The same assertions passed on the CLR
reference. No rejection was disabled and no unsafe resource manifest was emitted.

The runtime investigation must account for supplemental method aliases: their
declaring class is the Base class, but their tokens and custom attributes belong
to the current interpreter image. Reflection's image/token selection currently
uses the declaring class image. Generic definitions/inflations and parameter
attributes must be considered together. A plain removal of the offline check is
not the implementation. New assembly references require a separate identity and
dependency review rather than treating the entire AssemblyRef table as immutable.
