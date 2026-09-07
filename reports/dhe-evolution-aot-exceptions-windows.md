# DHE AOT exceptions: compiler control checkpoint

## Result and limits

The two remaining v11 retained-AOT failures are reproduced without registering
DHE or injecting native guards. On Unity 2021, enabling the supported divide
check option repairs `divide_by_zero_catch`, but leaves `invalid_cast_catch`
failing. A C# compiler-control patch that evaluates discarded expressions then
passes all four focused exception cases on Unity 2021, Unity 2022 and Tuanjie.
The test IL and golden were not edited.

This is a verified compiler repair candidate, not a passing full DHE gate.
The original `replay-missing-aot-generics-cold/report.json` remains failed and
unchanged. Package integration, compiler identity in the normal workflow,
full cold 220-case DHE replay, multi-generation evolution, performance/memory
and Android handoff remain required. This does not establish an official-runtime
baseline: the controls use the existing v11 HybridCLR native runtime, without
DHE registration, supplemental metadata loading or method guards.

## Source identity

| Component | Candidate source |
|---|---|
| Lab plain-AOT control | `8327a10` |
| Lab compiler patch and scoped build experiment | `f63fbc2` |
| Lab positive/negative patch tests | `f8a4980` |
| HybridCLR, unchanged | `a57999edeb67f20ef48832737a32662065226ca5` |
| Unity 2021 hooks, unchanged | `3a0ce4d178033a9edd32b62988ff8a1bf0432150` |
| Unity 2022 hooks, unchanged | `2cd3264524cf566a315f83222bebf6fdff31cc7a` |
| Tuanjie hooks, unchanged | `00c3a97144111d8aca6029a6613c3192a9276f46` |
| Unity package, unchanged | `57c4c07ca0c3fe953ab495f3b179d05f0d5aaf35` |

The frozen patch host SHA-256 is
`e740cc3bb5f3441270bce69e6f70970475f3f0b7f6c4baa67cd6411954ade93b`.
MV remains `DHEMETA1`, schema 1. Native contract stays `dhe-runtime-v11`;
these independent diagnostic Players are not registered or published as new
production Bases. No formal branch/tag, Installer selection, resource release,
CAT source or published toolchain changed.

## Actual Windows Players

All paths below are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

| Control output root | PID | Invalid cast | Divide by zero | Overall |
|---|---:|---|---|---|
| `aot-exception-control-u21-default` | 17508 | none | none | 2/4, exit 1 |
| `aot-exception-control-u21-divide-checks` | 49060 | none | divide | 3/4, exit 1 |
| `aot-exception-control-u21-evaluate-pop` | 35888 | cast | divide | 4/4, exit 0 |
| `aot-exception-control-u22-evaluate-pop` | 47244 | cast | divide | 4/4, exit 0 |
| `aot-exception-control-tuanjie-evaluate-pop` | 15348 | cast | divide | 4/4, exit 0 |

NullReferenceCatch and IndexOutOfRangeCatch pass in every run. All five
compiled `HybridCLR.ManagedCases.dll` files are byte-identical:
`06e00fd428986b21055e446befd70feae5436f8ef98a06e14154b46bf8cb102b`.
The selected method instructions and locals match their archived Base inputs.
Unity 2021's generated Base/control MV documents also confirm identical method
versions. Re-linking changes the whole DLL identity relative to the archived
Base, so the control DLL is not described as byte-identical to that Base.

Each output retains `result.json`, `player.log`, the compiled case DLL,
`HybridCLR.ManagedCases.cpp`, and the complete new Player. Editor logs are the
sibling `<control-output-root>-build.log` files. The archived generated C++ has
no DHE guard marker. The fixed output explicitly evaluates `UnBox(...)` in the
try block and emits `DivideByZeroCheck`; default code omitted both operations.
Unity 2021 uses OptimizeSpeed; Unity 2022 and Tuanjie use OptimizeSize/FGS.

## Compiler mechanism and safety checks

The installed compiler handles Code.Pop by removing its StackInfo and returning
without emitting its pending expression. Scalar unboxing constructs an expression
containing UnBox; dropping that expression also drops the InvalidCastException.
The division check is a separate emitted statement, enabled through
`--enable-divide-by-zero-check`; the option alone cannot restore scalar unboxing.

`runners/dhe-aot-codegen` uses dnlib, not byte offsets or text replacement. It
checks the compiler input hash, the unique stack-pop branch and the real Code.Pop
enum/switch mapping, then emits `(void)(expression);` through ICodeWriter.
All other method instruction sequences and assembly references are checked
after writing. The original compiler is never overwritten by this tool.
The initial prototype checks incorrectly expected a concrete generic return
signature and an in-module Code enum; both attempts failed before output.
The corrected tool resolves Stack<StackInfo>'s generic return and the adjacent
DataModel assembly. The frozen three-engine runs use that corrected source.

| Engine | Original compiler SHA-256 | Patched compiler SHA-256 | Unchanged methods |
|---|---|---|---:|
| Unity 2021 | `b322a871a65485642f95872748edf91e0e054f3481f541a3d5a81a8961809a9d` | `381e327482f4eec026006a53de1d727d6f44f6e730558d6949c8bed0bbb07b59` | 7154 |
| Unity 2022 | `7c8f2ddcec69c13f78cb8892c59f6c0d69d9732037a7800ae8e2705fe2719825` | `a3a7f9e5cc09788cb49be4b003d080843a763b501e7f630f527ef4b3864f4360` | 7313 |
| Tuanjie | `b5bd4c446f46e467ffc579f5be7a01832488eea50a3f04d4ce5380ea4b5f2bcc` | `9a975d7694ae917a16ddac09fade524c4d74ed4074b9d74b5fc76f3a8334470b` | 7321 |

The `aot-codegen-<engine>-frozen/Unity.IL2CPP.dll.patch.json` reports also bind
the DataModel DLL and patch host. Three `aot-codegen-tests-<engine>/report.json`
reports each pass nine checks: positive generation; rejection of wrong hash,
existing output, repeated patch, unrelated assembly and same input/output; and
three input/output immutability checks. All tests use new output roots.

The Editor diagnostic entry backs up the project-local compiler before use,
verifies compiler/DataModel identities, restores the original compiler and
additional options in finally, and checks the restored hash. All three local
compiler hashes equal their original Editor hashes after the builds. Original
Editor installations were never modified; recovery backups remain with the
experiment outputs. No generated C++ or archived Player was manually edited.

## Evidence hashes

| Artifact | SHA-256 |
|---|---|
| Default Unity 2021 result | `151E88C204BB6B916B7AF6F946EFEA8ED55EC9BF6C75F44D4FDBFA2DB7C1FEDD` |
| Divide-check Unity 2021 result | `05BDB73A6807C664159F92745C28194F0ABA0223AF253C947565E2410C3CFB05` |
| Fixed Unity 2021 result | `6BBD52DF9F8AAA84019B1198A99A53FF5B43AFA1A810D23FA82FC994F2A859B0` |
| Fixed Unity 2022 result | `E4D6BDDE9FE6D3502D069BCA6539E4E9E38C1F921519CD954273D9D0C5883B5E` |
| Fixed Tuanjie result | `DFB462D06694EA32C3A26067719B6C34DE4DAF6BDE5321BA497CDF43EF52B071` |
| Unity 2021 patch tests | `0F5E28DC99D2840D4CF82F02A184FFF6BAE212479C46160A0F100030F084301B` |
| Unity 2022 patch tests | `6D455B8B0D83D0E101CD2EA5B26525BA7C3CE14F9D379591AEC97EF41670D4E8` |
| Tuanjie patch tests | `BA3FD17C5E5A0D96EC256A21FC065A37EE984C4ED66CA5591900755DA4F4EF25` |

The fixed GameAssembly hashes are Unity 2021
`7a15192fafcdb980d21fcdd474ba98c94252977cc322a72cc2342fbd7c67fb10`,
Unity 2022 `aeda4212fd29a46cab3c648931821affc83353083175b365c0d7f647665a35e2`,
and Tuanjie `697fa1ad972b2f62af21ac83e77310664c9f13fa218ab8bef990dd23b086ce86`.

## Remaining work and rollback

First integrate compiler repair and divide checks into the package-owned C#
workflow, with immutable compiler input/output identity, failure recovery and
no Editor-installation mutation. Expand discard-expression regressions before
freezing; four exception cases do not prove all compiler semantics. Rebuild
fresh candidate Bases and rerun the original cold 220-case DHE differential,
then the shared-resource multi-generation matrix on the same frozen identity.
Continue generic-field, field-address, interface/layout, Unity-facing evolution,
external AOT API availability, concurrency/GC/ABI and performance/memory work.

The six core research worktrees are clean at the source checkpoints. Demo input
commits are `5a3039a`, `1f031f1`, and `2cc0d45`; their prior generated plugin DLL
changes remain, as does Unity 2021's prior settings normalization. Existing
stashes remain unchanged. No test process remains live at evidence freeze.
Rollback is lab-only: stop using the explicit compiler-control option. It is
not enabled in the normal workflow. Previously archived Bases and failed/full
replay reports have not been rewritten or relabeled as fixed.
