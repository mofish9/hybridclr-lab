# DHE evolution implementation and validation

## Latest checkpoint: same-runtime original/evolved cold differential

The clean `7e35687` replay passes 18 cold Windows processes on six Bases: original
and structurally evolved managed generations for all three engines, using the
same frozen native/package implementation. Every process executes the complete
220-case suite with zero differences. Both common resources and skipped updates
pass; 792 structural assertions pass, 72 legacy probes are explicitly inapplicable,
and all immutable-file checks hold. See
`reports/dhe-evolution-compiler-generations-windows.md` for exact identities.

This closes the current mixed-generation cold-suite gate. It does not implement
the still-rejected generic-field, field-address, interface/vtable or layout
changes, establish performance/memory benefits, or qualify Android/iOS. The next
capability work targets fields added to an existing generic reference type.

## Previous checkpoint: package compiler integration and cold differential

The package-owned C# compiler transaction is integrated at `2c04c24`. Its
33 transaction tests and nine compiler-patch tests pass separately on all three
real Editor compilers. Fresh Unity 2021, Unity 2022 and Tuanjie Base workflows
pass, including no-op Players and all three real-header native gates.

The clean `51f71e1` replay passes nine cold Windows processes on these three
Bases. Every run executes 220 cases with zero differences; all six interpreted
runs have 220 entry receipts and all three retained-AOT runs have zero receipts.
There is no diagnostic pre-touch or immutable Base-file change. See
`reports/dhe-evolution-compiler-integration-windows.md` for exact identities.

This closes the reproduced cold-suite failures, not the complete evolution
objective. These Bases contain only one managed generation per engine. The next
gate rebuilds original-generation Bases on the same frozen implementation and
tests both generations against shared current resources, including skipped
updates. Generic fields, field addresses, interface/layout and Unity-facing
evolution, external AOT API availability, concurrency/GC/ABI and performance
remain required. No formal branch/tag, Installer selection or CAT source changed.

## Previous checkpoint: AOT compiler exception controls

Unregistered, unguarded Unity 2021 AOT reproduces the two remaining exception
differences with unchanged method IL. The supported divide-check option repairs
division; a lab-only C# compiler patch restores evaluation of discarded scalar
unboxing expressions. The repaired control passes four exception cases on each
of Unity 2021, Unity 2022 and Tuanjie Windows. All three compiler patch suites
pass nine positive/negative checks. Original Editor compilers remain untouched;
project-local compiler copies were restored and rehashed after each build.
See `reports/dhe-evolution-aot-exceptions-windows.md` for exact identities.

This is not a full DHE pass. The v11 cold 220-case replay remains failed and
unchanged. Package-owned compiler/workflow integration, broader code-generation
regressions and fresh DHE/multi-generation replay are the next gates; all wider
evolution and performance/memory requirements still apply. No formal source,
runtime tag, Installer selection, resource channel or CAT project changed.

## Previous checkpoint: unchanged generic methods without AOT code

The `a57999e` runtime and three updated GenericMethod hooks repair the reproduced
Unity 2021 missing-AOT reflection failures. The clean `33c2278` replay executes
nine Windows processes on three fresh v11 Bases. All six interpreted runs pass
220/220 with zero differences and 220 entry receipts each. Every retained-AOT run
now has only the same two remaining exception differences: `divide_by_zero_catch`
and `invalid_cast_catch`. The overall gate remains failed. All three real-header
compile/CTest gates pass. See `reports/dhe-evolution-missing-aot-generics-windows.md`.

The indexed-argument repair left two Unity 2021 reflection failures because
an unchanged generic definition can be instantiated without generated native
code. Method-change status is not proof of AOT implementation availability.

This candidate passes explicit missing-AOT information from GenericMethod's
original method-pointer lookup into interpreter eligibility. It also allows the
existing slow call-pointer initialization and missing-invoker FGS preparation to
select IL when no usable AOT implementation exists. Already-interpreted methods
remain interpreted; unchanged methods with usable native code remain AOT.
Ordinary supplemental metadata behavior must not change.

Native regressions cover available/missing AOT, absent metadata, generic
inflation, cached call-pointer initialization and FGS invoker fallback. All three
engine hooks use their real headers and existing metadata/publication locks;
no MethodInfo layout, new cache or new publication field is introduced. The
primary Player gate is the unchanged cold 220-case suite, with explicit generic
reflection and native-retention checks. No pre-touch, golden edits or performance
claims are allowed. Native fixes get fresh Base identities; archived v10 and
earlier Players remain unchanged. Full managed evolution, concurrency/ABI and
performance/memory qualification remain the broader objective.

The first native attempt passes Unity 2021 but finds two Unity 2021-only field
assignments in the new FGS fixture on Unity 2022. Those unused assignments are
removed, matching the existing FGS tests' cross-engine shape. Production review
also identifies the public managed-to-native preparation path's early rejection
of a null unchanged AOT pointer; it must fall through to ordinary interpreter
preparation. The regression now calls that public path. Failed logs remain under
`native-missing-aot-generics`; the corrected gates use a fresh output directory.

## Previous checkpoint: indexed interpreter bridge arguments

HybridCLR `25b4d9f` fixes the reproduced indexed-argument corruption. The clean
`ce2f2e8` replay runs nine cold Windows processes on three fresh v10 Bases. All
six interpreted runs pass 220 cases, zero differences and 220 entry receipts
each, including skipped updates. The three retained-AOT runs still fail: four
differences on Unity 2021 and two each on Unity 2022 and Tuanjie. The overall
gate remains failed. See `reports/dhe-evolution-indexed-arguments-windows.md`.

All three real-header compile/CTest gates pass, now including InterpreterModule.cpp.
The resource DLL/MV bytes and golden are unchanged from the previous cold matrix;
there is no constructor pre-touch or inline padding. The six historical Bases'
60 immutable archive files were rehashed against the retained failed report and
are unchanged. Runtime identity is `dhe-runtime-v10`; MV remains schema 1.

The cold `381a51d` full matrix completes 220 observations in each of 18 processes
on six archived Bases, but remains failed. Type-only and field-only pre-touch
also fail on all six Bases; constructor-only pre-touch passes all six interpreted
runs. Those passing diagnostic runs are not cold qualification.

`Managed2NativeCallByReflectionInvoke` previously passed the first indexed slot
directly to `Interpreter::Execute` after a method becomes interpreted. This
incorrectly assuming contiguous invocation arguments. `NewValueTypeVar` places
its receiver after the explicit arguments and value buffer. A first constructor
call can change the method implementation flag after its caller was transformed,
so the next call takes that shortcut with noncontiguous arguments.

The repair gathers each argument using its index and transformed slot count into
separate storage, preserving receiver/byref values and multi-slot structs. The
caller's interpreter frame remains live and GC-rooted until the callee has copied
the gathered arguments. No method publication, object layout, or ABI changes are
required. Native tests cover this argument contract; the compile matrix must also
compile InterpreterModule.cpp, which was previously absent from its object gates.
The existing unmodified cold case and golden remain the Player regression.

Acceptance requires all three real-header compile/CTest profiles and fresh-Base
cold Player runs, with no constructor pre-touch or inline padding. The other
generic-fallback and AOT exception differences remain independent open failures.
This repair requires a new native Base identity; archived Bases and historical
reports are retained unchanged. It is not full DHE or mobile qualification.

## In progress: full managed differential

The complete first retained-AOT run now records 220 cases and four differences:
`divide_by_zero_catch`, `invalid_cast_catch`, `reflection_make_generic_method`,
and `reflection_make_generic_type`. All four report unchanged native methods.
The generated Unity 2021 C++ currently available omits the unused division and
unboxing operations, but it is not yet proven to identify the cause in the
archived Base. Generic reflection still needs its inner exception diagnosed.
The original golden and failed result remain unchanged.

The next harness records full exception chains in a separate diagnostic file
after sampling the case counters. Replay continues after individual failures
to exercise both paths and every configured Base, retaining failed artifacts
and keeping the entire gate failed. Any immutable Base-file change stops replay.
This is diagnostic coverage, not a relaxation of the 220-case acceptance gate.

The clean `dfb67d8` complete replay executes all 18 processes on six archived
Bases, with 220 observations per process and no immutable-file changes. All runs
fail qualification: both unused throwing operations disagree on all three
engines' retained-AOT paths; generic reflection additionally fails on Unity 2021.
Every interpreted run has one different failure, `generic_nested_combo`, returning
an unstable integer instead of the expected swapped value. All 220 callback
methods report changed in these interpreted runs. This is a newly exposed
correctness defect, not passing full-suite evidence. Raw observations and failed
run hashes are retained in `replay-full-differential-complete`.

The diagnostic resource confirms `ExecutionEngineException` for missing AOT
instantiations of `ReflectionMethods.Echo<int>` and `ReflectionBox<int>..ctor` on
Unity 2021. DHE currently suppresses unchanged-method interpreter eligibility,
even when that generic instantiation has no native implementation.

To isolate the nested generic/ref failure, the instrumenter can additionally
prefix 64 NOP bytes to every case-assembly method. This exceeds the archived
test Bases' default 32-byte inline budget without changing declarations or
golden values. It is a diagnostic variant only, not a production workaround.

The `4129fe1` field/construction diagnostic changes the result: interpreted
cases pass after that pre-touch, while the cold and no-inline runs still fail.
This is initialization-order evidence, not a repair. Runtime field offsets
are correct when queried before the case (inner 0/4, outer 0/8), and reflected
construction yields 2/3/5/7. The next fixture defaults to diagnostics AFTER the
220 observations so qualification cannot silently rely on prewarming. Explicit
types-only, fields-only, construction-only and combined pre-touch modes are
recorded in the configuration/report and are diagnostic experiments only.

The clean `8d04137` no-inline replay still fails the same nested generic case
on all six Bases. No inlining fix is justified by that experiment. The next
resource adds a separate layout diagnostic for `Pair<int>` and `Pair<Pair<int>>`,
including size, field offsets/types, and reflected construction. Golden values
and the original failing case body remain unchanged.

The next gate executes the complete existing 220-case manifest after DHE
registration, through a resource-only fixture entry on the archived Windows
Bases. No new Player entry or native runtime change is required for the harness.
The primary metric is exact case/metadata/return/side-effect/exception agreement
with both the same current DLL on CLR and the tracked golden contract.

Two resource variants distinguish retained AOT case bodies from interpreted case
bodies. The latter gives each managed case-assembly body a semantic no-op marker,
and its delegate entry methods also emit stable-ID execution receipts. This is
test instrumentation, never production-equivalent performance evidence. Native
IsDifferentialMethodChanged observations must agree with the actual Base/current
MV, and every interpreted case entry must have its own execution receipt.

The fixture emits a bounded, versioned BinaryWriter record stream, avoiding a
new Unity or JSON serialization dependency in the hot-update assembly. A missing,
partial, stale, duplicate or mismatched result must fail the external C# gate.
Each actual Player process has new output paths and an overall timeout; a partial
record identifies the case reached before a crash or hang. Existing Base archives
and prior reports remain unchanged. Three-engine Windows results stay separate
from Android/iOS and performance/memory qualification.

Initial harness checks exposed its ordering mismatch with the existing manifest
generator (layer, category, ID), and constant-data RVA relocation after IL
instrumentation. Ordering now matches the original generator. The instrumentation
verifier checks unchanged compiler-data bytes and field declarations while leaving
the output's actual RVA and MV intact. An exploratory CLR run now matches all 220
golden cases, and the interpreted variant changes 554 managed method bodies.

The first actual Unity 2021 replay reaches `checked_multiply_overflow`, then
stops because the driver only caught TargetInvocationException while Unity
propagated the case's OverflowException directly. The driver now records either
the direct exception or one invocation wrapper's inner exception, matching the
existing Player runner's case-observation boundary. Golden exception comparisons
remain mandatory. The partial binary, failed Player and logs remain under
`replay-full-differential`; they are not passing 220-case evidence.

## Previous checkpoint: logical attribute members

The clean `8de07cb` replay passes 18 Windows processes across three original v8
Bases and three repaired, already-evolved v9 Bases. Both common resource releases
and skipped updates pass; the latest release requires all four evolution receipts.
All three real-header native gates and new Base/no-op workflows also pass. See
`reports/dhe-evolution-logical-attributes-windows.md` for exact identities and
remaining work. This closes the reproduced Attribute failure, not the full goal.

The clean `3017f7a` repeated-structure replay passes 18 unique Windows processes
across six original/evolved Bases. The latest resource adds fields, a property,
and a method to an already-native carrier. Twelve latest/skipped runs require
the `repeated-structural-evolution` execution receipt. This is selected capability
evidence, not the full 220-case differential or performance qualification.

The subsequent clean `9e6d5d5` Attribute fixture adds a named field, named property,
and constructor overload to an existing Attribute type. CLR assertions pass,
but the Unity 2021 evolved Base crashes with access violation `0xc0000005`
after the named-field receipt. The archived failure is
`artifacts/dhe-evolution-20260908/replay-attribute-members/report.json` relative
to the workspace. Original-generation runs pass; the newer Unity 2022 and
Tuanjie Bases were not reached. Preserve all failed inputs and archived binaries.

Logical PropertyInfo objects are not elements of the physical Base property
array. The candidate preserves physical slots and encodes logical slots in a
suffix beginning at `property_count`; all three engine attribute readers decode
that suffix. Tuanjie's physical lazy-property setup remains unchanged. Added
Attribute constructors are canonicalized through the homologous logical method
map. These maps are prepared before the existing DHE publication boundary; no
new mutable cache, object layout change, or post-publication write is introduced.

The runtime contract is `dhe-runtime-v9` with `logical-attribute-members-v1`.
MV stays `DHEMETA1`, schema 1. The C# compatibility scan considers the complete
current assembly set, including inherited/cross-assembly property declarations.
Eleven exploratory tests pass, and resource generation rejects the three
affected evolved v8 Bases while retaining the three compatible original Bases.
Rejecting a resource is a safety gate, not implementation of the missing ability.

The initial native and fixture failures below were resolved before that replay.
The first Unity 2021 native build exposed a nonexistent image-classification
macro in the new constructor resolver. HybridCLR `82e6829` uses the existing
`IsInterpreterImage` API; the failed `native-logical-attributes` log is retained.
The next build compiled the runtime but rejected aggregate initialization of
Il2CppClass's zero-length vtable in the test fixture. The fixture now uses the
existing calloc allocation pattern with scoped ownership; both failed logs remain.
CTest then reached a preexisting incomplete resolver image: Image construction
enumerates registered assemblies, whose fixture image omitted nameNoExt. Both
resolver images now carry real image names. The failed CTest log is preserved;
no assertions are removed or bypassed.
Unity 2021 and Unity 2022 then pass compile/CTest. Tuanjie's fixture requires a
const PropertyInfo pointer array, matching its real header; that test declaration
is corrected before the final three-profile run. No production layout changes.
The final gates use clean locked commits and real headers on all three engines;
resource replays require named-field, named-property and constructor-overload
receipts. Native repairs require a new experimental Base;
old Base identities must not be relabeled or overwritten. Formal branches,
Installer tags, released packages, and CAT remain unchanged.

## Previous checkpoint: managed generations

Runtime contract v8 now has six real Windows Bases: original and structurally
evolved managed generations on each of the three engines. The clean `03ac677`
replay passes 18 consecutive/skipped resource runs, with exact-current-DLL CLR
observations and immutable Base hashes. See
`reports/dhe-evolution-generations-windows.md` for identities, the corrected
no-op report-schema failure, and remaining scope. Earlier sections below are
the historical investigation, not claims about the latest qualification.

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
- [x] Verify original/evolved managed Bases on all three engines with common consecutive/skipped updates and independent CLR observations.
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

The corrected replay runner at clean source `2c17392` passes all seven original
Base runs; see `replay-engine-capabilities-frozen/report.json`. The initial
runner revision confused the unrelated AOT control with Stable; its failed
report is retained under `replay-unresolved-stubs-frozen`. New reports expose
the two Stable classifications explicitly. Archived reports must prove the
static Stable classification through their changed-caller record.

## Evolved Base attribute identity regression

The generation-aware Demo builds an evolved Base on all three Windows engines,
but each actual no-op Player fails the custom attribute assertion inside
`DheEvolutionAssertions.ValidateMarker`. `changedMethodCount=0`,
`noOpAotBehaviorValidated=true`, and `structuralPassed=false`; the overall
workflow correctly fails. The three preserved roots are `base-evolved-u21`,
`base-evolved-u22`, and `base-evolved-tuanjie`. They are negative regression
inputs, not qualified Base generations.

Custom attribute data conversion resolved a local constructor from hidden
Current MethodDefs, even when the attribute class already existed in Base.
The candidate resolves constructor references through the homologous image
before serializing their method indexes. This leaves ordinary interpreter and
non-DHE supplemental images unchanged, adds no post-publication mutation, and
retains the existing cache lock/publication sequence. The MV wire format does
not change. Native compile/CTest and both legacy/evolved Player shapes must be
rerun before qualification; named attribute members and added constructor
overloads also need dedicated coverage.

Runtime contract `dhe-runtime-v8` declares `homologous-attribute-constructors-v1`.
Offline analysis reads actual CustomAttribute rows and requires that capability
when a local constructor's type already belongs to Base. This includes
compiler-generated Nullable/Embedded attributes, not just business marker types.
Consequently earlier candidate Bases can be missing this capability even when
their narrower declaration smoke passed. They cannot be relabeled as fixed.

The v8 evolved no-op Players subsequently passed on all three engines. New v8
original-generation Players also passed, but their enclosing workflows failed
because the report schema required eight deletion probes even when no structural
fixture existed. The fix permits an empty probe array for that no-op shape while
still requiring exactly eight records for a structural report. Fifteen schema
regressions pass; original failed workflow reports are retained, with corrected
schema gates written separately. No Base was rebuilt to repair this schema.

Both generations then consumed two resource-only updates and independently
skipped the first update on every engine. An initial 18-process pass used only
the existing internal generation marker. Review found that its value was not
exported in the Player report, so a second fixture advances an observable Add
result. The CLR executes the exact shipped DLL; the replay checks its hash and
three observed values against every Player. All 18 runs pass at clean source
`03ac677`, with Add returning 102 then 103. Thirty-three generation/observation
tests include stale, missing, and malformed result rejection.

This closes the selected mixed-generation lifecycle checkpoint, not the full
goal. The evolved Base receives three method-body changes in these updates;
additional structural changes after that evolved Base, named attribute members,
constructor overloads, currently rejected layouts/interfaces/generic fields,
member-level AOT availability, and the full 220-case DHE differential remain
unfinished. Native/mobile/performance qualifications are not inferred from this
Windows correctness replay.

## Next structural generation

The next fixture extends DheEvolutionCarrier, which is absent from original
Bases but already native in evolved Bases. It adds fields, a property and a
method, with direct/reflection/delegate calls and a reference-field GC check
whose allocating helper does not retain the payload. Every prior declaration
assertion remains enabled. Existing six Base archives and native capabilities
are fixed inputs; only current resources change. Correctness and immutable-file
hashes are the acceptance gate, with separate Unity 2021 supplemental-metadata
and Unity 2022/Tuanjie FGS runs. Attribute named members and new constructor
overloads are the following separate regression surface. No native fix or
performance result is presumed by this fixture change.
