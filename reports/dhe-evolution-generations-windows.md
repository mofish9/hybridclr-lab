# DHE mixed managed generations: Windows checkpoint

## Status

Six Windows Bases on runtime contract `dhe-runtime-v8` consume the same two
current DLL/MV resource releases. Every engine has an original managed Base and
a Base already containing the expanded structural fixture. Each Base executes
both consecutive updates and a separate skipped-first-update run. No Base is
rebuilt between resource releases. This is exploratory correctness evidence,
not full DHE capability, performance, mobile, or formal release qualification.

## Frozen identities

| Component | Commit |
|---|---|
| HybridCLR | `8060e4145f92d4a2b2a1d0720a188c7f1cba9284` |
| Unity 2021 hooks | `4d76aca57497d48e17d4befeec7c4c9ca40a836c` |
| Unity 2022 hooks | `fb1b46d26b959df43f614f033c105a195d5ed56d` |
| Tuanjie hooks | `ea788c5259566a23ccd6769e8c09d2918e3d4e4d` |
| Unity package | `3d23847995b417a9a834de369335c69e83cb9a5f` |
| Bootstrap host/toolchain | `6a31dd2847ddd18d5823e8bb334d926f6a5d88de` |
| Corrected report schema | `628b3a50367efa970772e805f4ddac61ce066d16` |
| Frozen observation-qualified replay | `03ac67718570e20b3782145fdeff31f9251ec3f5` |

All implementation commits remain on isolated research branches. No formal
runtime branch, tag, package Installer manifest, protected channel, or CAT
project changed. MV stays `DHEMETA1`, schema 1; no MV format migration is involved.
The normal package Installer still references older formal runtime tags.

The three reused local Demo projects were clean at their original-generation
input freezes: Unity 2021 `aab7afca986f2d70acc97296ac002432e9219a70`, Unity 2022
`1e27d2dec73f3f9113a9cf884f5c7bfa560dc739`, and Tuanjie
`e4e86118a9039c35cede300c53a8aa86f9da3533`. Existing evolved Base outputs are
unchanged. Actual Base IDs and archive paths are in the registry below.

## Results

Paths in this report are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908/`.

- `native-attribute-constructors/<profile>/native-gate.json`: all three native
  compile/CTest profiles pass with real headers, `mergeReady=true`, and
  `surrogateExternalHeadersUsed=false`. Native source is unchanged by this
  iteration's schema and lab-test work.
- `base-attribute-evolved-{u21,u22,tuanjie}/`: all three bootstrap workflows,
  no-op AOT behavior, and the expanded structural assertions pass.
- `base-attribute-original-{u21,u22,tuanjie}/`: all three actual Players pass
  with zero changed methods and no interpreter entries in the main probes.
  The original workflow/schema reports remain failed because of the schema
  regression described below; they have not been rewritten as passing.
- `schema-revalidated-base-attribute-<generation>-<engine>.json`: all six
  archived output directories pass against the corrected report schemas.
- `registry-structural-generations.json`: six distinct Base identities, one
  payload variant. Unity 2021 uses supplemental metadata and OptimizeSpeed;
  Unity 2022/Tuanjie use FGS/OptimizeSize without supplemental metadata.
- `resource-observed-next/` and `resource-observed-latest/`: each manifest
  supports all six Bases and ships one common current DLL/MV set.
- `replay-structural-generations-observed/report.json`: 18 runs, 18 unique PIDs,
  six Base IDs, both managed generations, no dirty source, and zero failures.
  Executable, native DLL, embedded Base MV, and identity hashes stay unchanged.

| Base generation | Runs | Changed methods per run | Named checks per run | Inapplicable checks |
|---|---:|---:|---:|---:|
| Original | 9 | 130 | 48 | 0 |
| Already evolved | 9 | 3 | 40 | 8 |

There are 792 successful named checks and 72 explicitly unexecuted,
inapplicable checks. The latter are never counted as passes. Each run also
compares three numeric results against the CLR, for 54 scalar comparisons.

The CLR runs the exact shipped `HybridCLR.ManagedCasesAot.dll.bytes`, verified
by SHA-256. Its observations are Add(1) = 102 for the first release and 103 for
the second, Stable(2) = 4, and AddViaStable(2) = 104. Both consecutive and skipped
Players return the same respective values. Original Bases interpret structurally
invalidated Stable methods; evolved Bases retain their native implementations.
The unrelated unchanged AOT control remains checked separately.

The earlier `replay-structural-generations/report.json` also passed 18 runs,
but did not expose an independently checked generation value. It is retained
as the earlier smoke checkpoint, not the strongest lifecycle evidence.

## Regressions and reproduction

The first original-generation workflows exposed an unconditional `minItems=8`
constraint on deletion probes. No structural fixture means no deletion probes
are executed. The schema now permits zero or eight records globally and still
requires eight when structural assertions are expected. Empty or partial
structural evidence, false skip/pass claims, and failing structural results
remain rejected. `generation-schema-before/report.json` reproduces the failure
(14/15); `generation-schema-frozen/report.json` passes 15/15 after the fix.

`observable-generation-tests-frozen/report.json` passes 33 checks. These cover
Base/MV probe applicability, real CLR execution of two incremented generations,
unchanged declaration identities, unchanged default generator behavior, and
stale/missing/non-numeric observation rejection.

The tracked replay entry is `manifests/dhe-evolution-generations-windows.json`.
Its output must be new or empty. Reproduction uses the C# host's existing
`base-registry`, `build-managed-cases -Variant current-next -AdvanceObservableResult`,
and `resource-update` commands. Seed the first observable variant from
`prepare-declarations-expanded/current`; seed the second from the first output.
The generation command must use a new, explicit lab artifact output path.
Then run `runners/dhe-capability-reference` on each shipped DLL with
`--main-only` before running `runners/dhe-evolution` against the replay config.
The reference scope is three main observations, not the full 220-case suite.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Observable replay report | `b954dcc0edabc86ae1463d84cce739aee4f83ab48e8dae8e415606cedc379010` |
| Six-Base registry | `a33f0976aa6856b2f41fe33d2b11178bd548da3d2aa1d927cf76742cc4032114` |
| First resource manifest | `f2e58e0f6bf558553fbf92f4982df7280e965fec001196b7cbba804e6a07c363` |
| Second resource manifest | `cfd0fa41529cfa93ed5c05b3552d15932b05a6065f866865dbc9eda092bd5c64` |
| Frozen schema tests | `f0a38f4b3a3a8c071ed46c4257f159816934a696cdfa056080976c6a480270b3` |
| Frozen generation tests | `438d450ae84631c7daf86a5451c75c5529a53b7c35716e52eda893fff57474e9` |

## Remaining scope and rollback

This checkpoint proves the selected original-to-structural evolution and the
two managed generations sharing later releases. The already-evolved Base only
receives three method-body changes here. Repeated structural additions after
that Base, named attribute arguments/constructor overloads, rejected generic
fields/interfaces/layouts, and archived AOT member availability still require
implementation or dedicated validation. Full 220-case DHE differential,
performance/memory, final source review, and Android handoff remain incomplete.

No Android/iOS execution or performance claim follows from these Windows tests.
Keep prior native failures and all Base archives intact. Lab test changes can be
reverted independently; no existing Player runtime was modified this iteration.
Native capability rollback or repair still requires a different Base build.
Resources must stay within each archived Base's real capabilities.
