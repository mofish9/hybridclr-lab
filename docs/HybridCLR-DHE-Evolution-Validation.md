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

## Supplemental method metadata implementation

The next candidate keeps object layout, virtual slots, and calling conventions
unchanged. It resolves the metadata image for supplemental method aliases and
inflated definitions without replacing their logical declaring class. Engine
reflection uses that image for instantiated attributes, raw attribute data,
IsDefined, and parameter attributes. Parameter metadata lookup follows the
method definition rather than the declaring class. The same tests run on each
engine; Unity 2021 cache APIs and Unity 2022/Tuanjie reader APIs are separate hooks.

Reference-list evolution is distinct from assembly identity or declaration
changes. Added/removed references may be exposed through the current image only
after verifying dependency availability; changing the identity of a retained
reference remains rejected. Base MV and current MV wire format are unchanged.
New runtime capabilities are required for these features so old runtimes cannot
be relabeled as compatible. Both changes remain under the existing registration
publication/lifetime rules and require no new mutable cache or object sidecar.

## First v3 Player failure and linker repair

The first real Unity 2021 evolution update loaded successfully and identified 95
changed methods, but failed while resolving
`System.Reflection.Assembly.GetReferencedAssemblies`. The archived Base's
`mscorlib.dll` contains neither that Assembly method nor the RuntimeAssembly
override. This is not a passing evolution Player result:
`artifacts/dhe-evolution-20260908/player-evolution-u21.json` (workspace root).
The offline assembly-reference check did not detect the missing member.

The generated linker descriptor names referenced framework types under the
`netstandard` facade rather than their actual implementation assemblies. A package-owned
UnityLinker callback now resolves references against the target's real pre-link
inputs, preserves those external types in their defining assemblies, and preserves
all DHE root assemblies. The first rebuilt Base still failed: its original hotfix
code does not reference Assembly at all, so reference-only preservation cannot
cover the future API. The package therefore exposes `dhePreserveAotAssemblies`,
defaulting to complete preservation of mscorlib, System, and System.Core. Other
future AOT API libraries can be selected before freezing a Base. Nine standalone
tests cover forwarding, root retention, future API libraries, determinism,
deduplication, and missing inputs. A new Base must be built and tested; both failed
Bases are retained unchanged. Base size/build-time costs must be measured.
An archived Base cannot gain stripped native APIs through a resource update.

The latest parameter-attribute evolution DLL also passed the eight offline
compatibility tests. Remaining work includes member-level AOT availability checks,
actual evolution Player execution, and broader future external-API preservation.

## New type bodies referencing existing Base types

The future-API-preserving Base built and passed its no-op Player gate. The same
evolution update then advanced beyond missing APIs, but failed reflection invocation
of `AddedAttributedMethod`: the method's declaring type did not match the Base
instance. A diagnostic fixture confirms the failing call is ordinary, not generic.
Its caller is a newly added type. That type's body used the hidden current image,
so `typeof(DheDemoCalculator)` resolved to a second hidden class rather than the
public Base class. The previous body resolver explicitly excluded wholly new types.

The runtime candidate routes all bodies from its own supplemental image through
the merged view. It leaves unrelated interpreter images and ordinary AOT metadata
unchanged. The fixture now checks exact typeof/object identity and generic method
definition identity. Offline local type references determine whether an added type
requires `supplemental-type-base-references-v1`; existing Bases lacking this fix
cannot be relabeled as capable. Runtime contract advances to `dhe-runtime-v4`, but
the MV wire format and stable identities are unchanged. Native and Player reruns
on this exact candidate remain required.

That candidate passed all three native profiles with real Editor headers. A fresh
Unity 2021 Base and resource update then passed new-type identity, ordinary added
method invocation, method/parameter attributes, and generic definition/attribute
identity. Execution next failed at reflection invocation of
`AddedAttributedGenericMethod<string>` with a missing AOT implementation. Its
public alias has a Base class but is absent from the Base method-token map, so
`IsImplementedByInterpreter` incorrectly returned false. The candidate now also
recognizes supplemental aliases through their method image, including inflations.
It declares `supplemental-method-generic-invocation-v1` under runtime contract v5.
The exact updated candidate must repeat native and real Player gates.

The native metadata fixture previously returned address 1 for an available
homologous image. The supplemental-image virtual query exposed that invalid
stand-in. It now returns an actual minimal derived image and uses the real engine
reader/writer lock implementation. Unused metadata operations abort. Additional
assertions cover supplemental definitions/inflations, absent supplemental mapping,
and unchanged methods remaining native. This fixes the fixture without bypassing
the production dispatch check.

## Verified capability checkpoint

HybridCLR `32d23ae` and package `80c00e7` passed the fresh three-engine native
matrix. A new Unity 2021 Windows Base then executed the complete capability
fixture successfully. Frozen replay source `686e0cb` passed two consecutive
updates plus an independent skipped-first-update run: three unique processes,
48 outer checks per run, and ten unchanged immutable files. This proves one
current capable Base, not the older six-Base matrix for these new features.
See `reports/dhe-evolution-capabilities-windows.md` for exact source/artifact
identity, file-size costs, failure history, and remaining gates.

## Declaration identity regression

The extended CLR fixture passes, but the unchanged v5 Unity 2021 Base rejects all
five declaration assertions at runtime: method parameters/return types, fields,
generic field arguments, inheritance, and custom attribute Type arguments.
The update passed offline compatibility and loaded successfully with 109 changed
methods; see `artifacts/dhe-evolution-20260908/player-declarations-u21.json` at
the workspace root. This is a reproduced correctness failure, not a passing gate.

The candidate splits homologous type mapping from member/class construction.
DHE fallback images bind local TypeDef references after allocating their raw
definitions, before initializing signatures, parents, generic constraints or
layouts. Raw Current definitions remain available for supplemental storage and
logical aliases; their physical classes are not replaced with Base classes.
Attribute Type arguments are canonicalized including arrays and generic types.
Ordinary interpreter images and non-DHE supplemental loads keep their reference
behavior. The mapping is immutable before existing metadata publication; no new
post-publication mutation or independent lock is introduced.

The offline declaration-reference scan requires
`supplemental-type-declarations-v1` under runtime contract `dhe-runtime-v6`.
It does not change the MV wire format or stable identities. A v5 Base remains a
negative regression input and cannot receive this native fix through resources.
The candidate must pass real-header native tests and new Windows Base/Current
runs before it supplies positive evidence.

The five original declaration groups and five expanded groups now pass on the
v6 Unity 2021 Windows Base. Expanded coverage includes arrays, ref/out reflection
and delegates, Base interface implementation, type/method generic constraints,
generic inheritance, and generic/array Type arguments in instantiated and raw
attributes. The frozen `d42ec47` replay uses the original and expanded payloads
on the same immutable Base, including an independent skipped-update process.
This remains one Base identity, not completed multi-generation qualification.

For other Windows engines, `prepare-engine-test-project -DheDemo` selects the
same DHE Demo source instead of the ordinary interpreter test project. It keeps
the existing isolated output, target-specific package and engine selection,
and managed input staging. No Unity Library or native build cache is copied.

Fresh Unity 2022 and Tuanjie preparation exposed a missing linker-input directory
dependency. Bee calls the linker descriptor callback before populating staging.
The assembly filter's list was also insufficient because it omitted framework
dependencies. The package now uses the same current BuildReport file roles and
filename precedence as PlayerBuildConfig: ManagedLibrary, DependentManagedLibrary,
and ManagedEngineAPI. Unity 2021 still uses its supplied input directory because
its BuildReport file inventory is incomplete at that callback. Newer Editors use
GetFiles(). Both paths are checked in their actual Editors.
Explicit input lists are tested with assemblies in different directories and
without a staging directory. No static input cache or framework-directory guess
is used. This changes Editor preparation only, not the v6 native runtime contract.

## Missing generic implementation behind an unresolved call stub

The fresh Unity 2022 and Tuanjie Base workflows now pass, including their no-op
AOT Players. Their paths are `base-linker-u22-verified` and
`base-linker-tuanjie-verified` under the existing evolution artifact root.
The common declaration resource set passes offline checks for these Bases and
the existing Unity 2021 Base. Actual execution on Unity 2022 nevertheless fails
at reflection invocation of the added generic method: `loadError=OK`, followed
by an ExecutionEngineException for missing AOT code. The failed replay and
Tuanjie probe remain under `replay-three-engine-declarations` and
`player-unresolved-stubs-tuanjie.json`. Neither archive is changed.

Unity 2022/Tuanjie fill a missing generic implementation with unresolved call
stubs before selecting interpreter fallback. A non-null stub hid the original
absence of AOT code. The candidate checks the original method-pointer record,
as the Unity 2021 path already does. It does not downgrade valid native FGS
invokers or change allocation, metadata locks, or publication order. Correct
reflection and direct invocation, with unchanged methods remaining AOT, are
the primary acceptance criteria; performance claims remain pending.

Runtime contract `dhe-runtime-v7` advertises
`supplemental-generic-unresolved-stubs-v1`. The resource compatibility check
requires it for added generic methods on existing types for Unity 2022 and
Tuanjie, but does not unnecessarily reject the already-capable Unity 2021 Base.
Unknown engine contexts require it conservatively. Seventeen standalone
compatibility checks pass. Native matrix, real Player execution on newly built
Bases, and exact-identity replay are required before positive qualification.

All three real-header native profiles pass on the frozen runtime source above.
New Unity 2022 and Tuanjie Bases also pass the original and expanded declaration
updates. `replay-unresolved-stubs/report.json` records seven successful Windows
processes with 48 original assertions each and unchanged Base files. It includes
the unchanged capable Unity 2021 Base and its skipped-update process. The replay
ran while the separate generation-aware Demo work was dirty; it is exploratory,
not the final clean-source replay. Those Bases still share the original managed
fixture generation.

## Generation-aware Demo validation

The Demo previously assumed every Base contains the removed legacy APIs and
every type change invalidates Stable. Its current fixture selection also skipped
the structural assertions when those types already existed in a newer Base.
The candidate derives fixture presence and Stable dispatch from the actual MV,
and compiles each direct legacy AOT probe only if the exact Base DLL contains
its method identity. It never compiles obsolete calls into an evolved Base.

Each legacy probe reports applicability, execution, outcome, reason, and stable
method identity. The replay runner independently checks those records against
the archived Base MV and the selected Current MV. Inapplicable probes are not
passed assertions. Old archived Players remain usable only when all their
legacy checks are actually applicable and pass. The runner can require both an
original Base and a Base already containing structural evolution, and checks
unique process IDs. Twenty-one standalone positive/negative tests cover these
decisions. Actual evolved Base builds and old/new shared-release replay remain
required; these tests alone do not prove runtime evolution.
