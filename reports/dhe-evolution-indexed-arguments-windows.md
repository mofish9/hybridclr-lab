# DHE indexed interpreter arguments: Windows checkpoint

## Result and limits

The reproduced `generic_nested_combo` corruption is repaired on all three Windows
engine profiles. Six cold interpreted runs complete 220 cases each, with zero
differences and 220 execution receipts each. These cover a consecutive resource
update and a skipped-first-update process on each of three fresh experimental
Bases. No constructor pre-touch or anti-inlining padding is enabled.

The full gate remains **failed** because all three retained-AOT runs still have
the independent differences listed below. This is not full DHE qualification,
performance evidence, mobile acceptance, or a formal opt4 release. This checkpoint
has one managed Base generation per engine; the earlier six-Base mixed-generation
matrix must be repeated after the remaining native repairs, not replaced by this
smaller repair matrix.

## Repair and review

`Managed2NativeCallByReflectionInvoke` used the first argument index as the start
of a contiguous argument block when a method became interpreted. That assumption
is false for `NewValueTypeVar`, whose receiver follows the explicit arguments and
value buffer. A native-form call site can survive the constructor's first call,
then enter the interpreter shortcut on its second invocation with the wrong slots.

The bridge now obtains the published InterpMethodInfo, allocates separate argument
storage, and gathers each argument using its index and `stackObjectSize`. Byrefs
remain pointer values. Multi-slot structs, repeated/nonmonotonic indices, and a
return buffer overlapping the original inputs do not clobber later arguments.
The original caller frame remains a dynamic GC root until the callee has copied
its arguments. The temporary vector is released on return or exception. The
zero-argument case has a non-null dummy slot and never dereferences an index table.

There are no object-layout, ABI, publication-state or global-cache changes. The
additional argument allocation/copy is not yet performance-qualified. Native
regressions cover constructor ordering, multi-slot/repeated sources, ref aliasing,
input overwrite after packing, bulk copying and zero arguments. The compile gate
now includes InterpreterModule.cpp itself. Historical baseline sources without
the helper still compile through the existing feature-detection convention.

## Source identities

| Component | Clean candidate commit |
|---|---|
| HybridCLR | `25b4d9f3011d0c82ca2e9eceb383d15d76f1b117` |
| Unity 2021 hooks, unchanged | `7cf7f8f9db216edd60adc8cb5b3ef99af39b997d` |
| Unity 2022 hooks, unchanged | `cf8b191aed51c7de3de9de7499734eea591c00e5` |
| Tuanjie hooks, unchanged | `60c588118779f0bf76c6a593e3087ccd40b313ea` |
| Unity package | `b96988ed90441ae7f3e2e8a7b78ad9a3c695b2c3` |
| C# host, native tests, source locks | `94bf79bc6745b465bb44e6f6bf11838a1e4f1d6b` |
| Frozen replay configuration and runner | `ce2f2e87de87756e1573b35366e7bbedca5a8955` |

The package and host identify this native repair as `dhe-runtime-v10`. MV stays
`DHEMETA1`, schema 1. The temporary toolchain is Exploratory, `releaseReady=false`,
Package ID `1734092855a402a6d6526c99dbe8607294d420d23af033696b0d96b32cf5bc96`.
No formal maintenance branch, runtime tag, Installer version selection, published
toolchain, protected channel or CAT project was changed.

## Evidence

Paths below are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `native-indexed-arguments/<profile>/native-gate.json`: three real-header
  compile/CTest passes, `mergeReady=true`, `surrogateExternalHeadersUsed=false`.
- `base-indexed-arguments-{u21,u22,tuanjie}/project-workflow-report.json`: three
  bootstrap/no-op AOT workflows and schema gates pass.
- `replay-indexed-arguments-u21-cold/report.json`: focused three-process replay;
  both interpreted runs pass 220/220, retained AOT fails as expected.
- `replay-indexed-arguments-cold/report.json`: nine distinct process IDs, six
  interpreted passes, three retained-AOT failures, no immutable-file changes.
- `differential-evidence-tests-indexed-arguments/report.json`: 20/20 positive
  and negative result-reader/golden-verifier checks pass.

| Engine | Retained AOT | Interpreted consecutive | Interpreted skipped |
|---|---|---|---|
| Unity 2021 | 216/220; four differences | 220/220; zero differences | 220/220; zero differences |
| Unity 2022 | 218/220; two differences | 220/220; zero differences | 220/220; zero differences |
| Tuanjie 2022 | 218/220; two differences | 220/220; zero differences | 220/220; zero differences |

Retained AOT still disagrees on `divide_by_zero_catch` and `invalid_cast_catch`
on every engine. Unity 2021 also fails `reflection_make_generic_method` and
`reflection_make_generic_type` for missing AOT generic implementations. All 220
retained-AOT case entries report unchanged; all 220 interpreted entries report
changed and have their own receipts. Neither failing assertions nor golden values
were removed or altered.

Both new common resources contain four DLL/MV pairs byte-identical to the matching
`resource-full-suite-cold-{aot,interpreted}` resources. The CLR references are the
same `differential-reference-cold-{aot,interpreted}.bin` files. Only Base selection,
the native repair and its identity have advanced. The new registry has three IDs:

- Unity 2021: `0c2789a916a311f167afd5d15fdd6a34c39b1eba35d69e770467c93ea2e30e5d`
- Unity 2022: `74176ba216e44f6ea22e07766868460d4ca1682828268a1011e738ae0f4183a4`
- Tuanjie: `c3f9ada92b5ffcdf2137a72d7e9c3fb8850871cfcb4fa8fbdcda3aa163b5b97d`

The raw Base input is the existing `artifacts/evolution-repeated-evidenced-raw`
under the lab. Unity 2021 uses OptimizeSpeed with supplemental AOT metadata;
Unity 2022 and Tuanjie use FGS/OptimizeSize without supplemental AOT metadata.

## Reproduction and retained state

The C# workflow is unchanged. Assemble each profile from the locked sources,
install it through the Demo's existing InstallRuntime entry, and bootstrap to a
fresh output using the Base input above. `base-registry` records those new outputs.
`resource-update` consumes the existing `artifacts/full-suite-cold-first` and
`artifacts/full-suite-cold-interpreted` roots against that registry. Replay uses
`manifests/dhe-indexed-arguments-windows.json` with a new output directory:

```text
dotnet <evolution-runner>/HybridCLR.DheEvolutionRunner.dll <lab>/manifests/dhe-indexed-arguments-windows.json
```

The six implementation/lab research worktrees are clean at replay freeze. Demo
package-input commits are `98c9928` (Unity 2021), `59450f3` (Unity 2022), and
`ee3a5de` (Tuanjie). Their package trees match the new package lock. Bootstrap
subsequently writes the four generated input DLLs; those changes remain visible.
Previous generated DLL changes were preserved in project-local stashes, not lost:

| Demo project under the artifact root | Preserved stash commit |
|---|---|
| `generation-projects/Unity2021Standard` | `6e266f59b16c5393e0119a59c500187248be6e4b` |
| `windows-projects/Unity2022Fgs` | `693964ef4c2a123c2cbc63522d87baaeb6b762cd` |
| `windows-projects/Tuanjie2022Fgs` | `abcf70bb9aa19c2738e8ade69ce25115100d33df` |

The six prior original/evolved Base archives were separately checked against the
immutable hashes in `replay-full-differential-cold/report.json`: all 60 files match.
No failed inputs, reports, archived Players or old identities were overwritten.
Native rollback means selecting the previous source locks and building another
Base, not relabeling or patching the old Player through a resource manifest.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Three-engine cold replay | `6A3D05EBA68136E0BE01E08054727172443107A567C05A1D5D5B63A03385DA99` |
| New three-Base registry | `D085A162907F008595356F4D6FB414E00CF3AEB7458CACAB57391B82CCBA16F1` |
| Retained-AOT resource manifest | `7C41C420C8BC21765AA217CAEA1A122CA19BFAAA9DB933C7584C04A75452DDB7` |
| Interpreted resource manifest | `572A7EA9D4325EC24A914B5568A0A1954BFD3F7870417BA543BDFD3BBA65EB18` |
| Result-verifier tests | `943EEB231FA20571622A657137A54836F2221A9FC0C4FE27400FD1E402F94F1E` |
| Unity 2021 native gate | `AF889472045937E1E9918B453D792B15BDC46B0521B375085B0881B3FCEC36B1` |
| Unity 2022 native gate | `6549D66B50771DB354D61FC7C8B3B515B79084A21B7BD040926807EEC7C051FA` |
| Tuanjie native gate | `9D9F959E56140486A01F35DC9410DB5579755776799A5332D776125D9E453B25` |

## Next work

Repair unchanged generic fallback by distinguishing available native code from
method-change status, especially before GenericMethod installs unresolved stubs.
Diagnose the two discarded throwing operations against a production-equivalent
plain-AOT reference without changing the test contract. Then continue mixed Base
generations, remaining field/interface/layout evolution, concurrency and ABI
review, performance/memory and the final Windows-to-Android project handoff.
Existing unconditional dispatch counters and the new bridge-copy cost must be
accounted for in the future production-equivalent performance work.
