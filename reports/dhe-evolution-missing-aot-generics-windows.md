# DHE missing generic AOT fallback: Windows checkpoint

## Result and limits

The reproduced Unity 2021 `reflection_make_generic_method` and
`reflection_make_generic_type` failures are repaired. The unchanged methods now
return `hello:42` and `Int32:17`; their MV change flags remain false. A generic
definition being unchanged no longer prevents interpreter fallback when its
requested instantiation has no native code. Existing native implementations are
retained.

All three engines compile and pass CTest with real headers. The cold Player
matrix has nine unique processes on three fresh Base Players. Six interpreted
runs pass 220/220, zero differences and 220 entry receipts each. All three
retained-AOT runs still fail the same two exception cases. The overall gate is
**failed**, not full DHE acceptance or a formal release. This checkpoint still
has one managed Base generation per engine; mixed managed generations, wider
evolution, performance/memory and mobile qualification remain unfinished.

## Repair and review

GenericMethod now passes the original `methodPointers.methodPointer == nullptr`
result into interpreter eligibility on each engine. This decision must use the
original lookup rather than a pointer later replaced by an unresolved-call stub.
The metadata predicate accepts explicit missing-AOT evidence and methods already
implemented by the interpreter while retaining native unchanged implementations.
Ordinary supplemental-metadata behavior is unchanged.

The existing slow call-pointer initialization can select IL for a null native
entry, and the public managed-to-native preparation path now reaches it instead
of rejecting unchanged DHE methods early. FGS preparation explicitly permits
fallback when it has no valid native invoker. These paths continue to use the
existing metadata lock, method-pointer lock, epoch recheck and pointer publication
order. No new lock, object/MethodInfo layout, cache or publication field was added.
This is not a new live-reload or metadata-after-failed-invocation retry protocol.

Native regressions cover missing/available AOT, absent metadata, generic
inflation, the public call-preparation path, retained MV identity, missing FGS
invokers, and preservation of valid native FGS invokers. Existing first-touch,
concurrency and publication tests remain enabled. The initial Unity 2022 build
found two test-only assignments to Unity 2021's `indirect_call_via_invokers` field.
Removing these unused fixture assignments aligns the tests with the existing
cross-engine FGS setup. The failed `native-missing-aot-generics` logs are retained;
the final gates use `native-missing-aot-generics-verified`.

## Source identities

| Component | Clean candidate commit |
|---|---|
| HybridCLR | `a57999edeb67f20ef48832737a32662065226ca5` |
| Unity 2021 hooks | `3a0ce4d178033a9edd32b62988ff8a1bf0432150` |
| Unity 2022 hooks | `2cd3264524cf566a315f83222bebf6fdff31cc7a` |
| Tuanjie hooks | `00c3a97144111d8aca6029a6613c3192a9276f46` |
| Unity package | `57c4c07ca0c3fe953ab495f3b179d05f0d5aaf35` |
| Compiled C# host | `344afc6675541c277a766bf7463905aec451f46f` |
| Corrected native tests, locks and exploratory package | `3e34616b7e653fb10fda8118161c69146f13d75d` |
| Frozen Player replay | `33c22780aa2d84779e18755143c02c0882eac418` |

Runtime identity is `dhe-runtime-v11`; MV is still `DHEMETA1`, schema 1. The
temporary toolchain is Exploratory with `releaseReady=false` and Package ID
`4b7c1c557af7c6e78476e403439667125d919195acb7f8d60b1275150c920695`.
Formal branches, runtime tags, Installer selection, published toolchains,
protected resource channels and CAT were not modified.

## Player evidence

Paths below are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

| Engine | Retained AOT | Interpreted consecutive | Interpreted skipped |
|---|---|---|---|
| Unity 2021 | 218/220; two differences | 220/220; zero differences | 220/220; zero differences |
| Unity 2022 | 218/220; two differences | 220/220; zero differences | 220/220; zero differences |
| Tuanjie 2022 | 218/220; two differences | 220/220; zero differences | 220/220; zero differences |

- `native-missing-aot-generics-verified/<profile>/native-gate.json`: all three
  pass, `mergeReady=true`, `surrogateExternalHeadersUsed=false`.
- `base-missing-aot-generics-{u21,u22,tuanjie}/project-workflow-report.json`:
  three bootstrap/no-op AOT workflows and schema gates pass.
- `replay-missing-aot-generics-u21-cold/report.json`: focused actual Player
  confirmation of the reflection repair, with the two other failures retained.
- `replay-missing-aot-generics-cold/report.json`: nine unique PIDs, six passes,
  three failures, no immutable-file changes and `diagnosticPreTouch=false`.
- `differential-evidence-tests-missing-aot-generics/report.json`: 20/20 reader
  and golden-verifier positive/negative checks pass.

All 220 retained-AOT entry flags are unchanged. All interpreted entries are
marked changed and have receipts. No golden, test body, case count or assertion
was relaxed. Both resources' four DLL/MV pairs are byte-identical to the original
`resource-full-suite-cold-{aot,interpreted}` resources, and the same CLR references
are reused. No pre-touch or anti-inlining padding is enabled.

The fresh registry contains:

- Unity 2021: `35fa3bcb09f8b58f27429739642da5bf599da6f798fe798e67a77ea6cedca546`
- Unity 2022: `be0c0033cb06ddb266b020f6ef23ffa08deecdd8cf6aefe471ad202d4e2dc647`
- Tuanjie: `214d19c172d99869647faf88cb07a9e3f75640e5bf53fef555ddbb44d9f48b94`

The Base input remains the lab's `artifacts/evolution-repeated-evidenced-raw`.
Unity 2021 uses OptimizeSpeed with supplemental metadata; Unity 2022/Tuanjie use
FGS/OptimizeSize without supplemental metadata. These Windows configurations do
not establish Android ARM64 or iOS correctness.

## Remaining exception investigation

`divide_by_zero_catch` and `invalid_cast_catch` still return `none` on retained
AOT instead of `divide` and `cast`. They pass on the interpreted resource.
Current generated C++ omits the discarded division and scalar unboxing work.

Read-only dnlib inspection of the installed Unity 2021 compiler establishes that
`Unity.IL2CPP.MethodBodyWriter.ProcessInstruction` (token 100663863) handles
`Unity.IL2CPP.DataModel.Code.Pop` (ordinal 37) by popping its expression stack and
returning without writing that expression. The normal scalar Unbox path returns
an expression containing `UnBox(...)`. This is a compiler-side causal hypothesis,
not a completed plain-AOT reproduction or an implemented repair.

The inspected `Unity.IL2CPP.dll` SHA-256 is
`B322A871A65485642F95872748EDF91E0E054F3481F541A3D5A81A8961809A9D`.
The original Editor DLL, the project's installed LocalIl2CppData compiler DLL,
and its StrippedAOTDllsTempProj copy all match this hash. The compiler also exposes
`--enable-divide-by-zero-check`; the effect of this option on these exact inputs
is not yet verified. No compiler DLL or generated C++ source was manually edited.
Next, run an unregistered/plain-AOT control with the same IL and determine which
checks or code-generation repair are necessary without changing the golden.

## Reproduction and workspace

Use the existing C# `assemble-runtime`, Demo InstallRuntime and `workflow
-Bootstrap -RunPlayer` lifecycle with the locked sources and fresh output roots.
`base-registry` records the three outputs; `resource-update` consumes the existing
`artifacts/full-suite-cold-first` and `artifacts/full-suite-cold-interpreted` roots.
The replay configuration is `manifests/dhe-missing-aot-generics-windows.json`;
choose a fresh output directory before rerunning it.

The six core research worktrees are clean at replay freeze. Demo package-input
commits are `33b100f` (Unity 2021), `3a9bb9f` (Unity 2022), and `7cdb3f3` (Tuanjie).
Their package trees match the lock. Generated DLL changes remain after bootstrap;
Unity 2021 also normalizes the YAML header in HybridCLRSettings.asset.
Previous input changes were preserved in these project-local stashes:

| Demo project under the artifact root | Preserved stash commit |
|---|---|
| `generation-projects/Unity2021Standard` | `1f034a164b56f649d36d67d4e0a04c662e30012a` |
| `windows-projects/Unity2022Fgs` | `b918937a056b21c44a993946fdc7502dc8015be6` |
| `windows-projects/Tuanjie2022Fgs` | `a3fe4099e110de886dc8143248fe03691c457183` |

All 90 immutable files across the nine pre-repair Base archives were rehashed
against the retained cold reports and are unchanged. Earlier stashes, failures,
resources and archives remain intact. A native rollback requires a different
Base build/source lock, not rewriting or relabeling an archived Player.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Three-engine cold replay | `E02EF068B4653D75B297F4FBD9CA4FB593CFA3D9938E577E7A279500C18C1C1C` |
| Three-Base registry | `87ACDADE9E7995D102536849DFA0CD707D39BE26E0B1110A3D10DDA7353AB730` |
| Retained-AOT manifest | `C45337A142A528535E17D84D263ECEC037893287EC0AFB2B484212EAA4049E8E` |
| Interpreted manifest | `981FAFEA05A4B1F0147633351329D70050F72196AB72A96E6CE0A50FD83F5C3A` |
| Result-verifier tests | `943EEB231FA20571622A657137A54836F2221A9FC0C4FE27400FD1E402F94F1E` |
| Unity 2021 native gate | `060762C0E9E81CE4EB77B983E1D53A1AE0E1CF473E1A1EF946C19EFBBEB10368` |
| Unity 2022 native gate | `CFB57DF474D15C1154C49BBC17FF9FE50B55BE86E4D8527DFF732EFA78BC0FDC` |
| Tuanjie native gate | `8FC5D9278AE6B6B1F2388B1D2908AD23B783FC99B391178C7FD76C08D0941CF2` |

Beyond the exception differences, retain the full goal: mixed managed Base
generations, remaining field/interface/layout and Unity-facing evolution,
first-touch/GC/ABI/concurrency coverage, production-equivalent performance and
memory, workflow integration and the Windows-to-Android project handoff.
