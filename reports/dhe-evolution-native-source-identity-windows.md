# DHE runtime source and Base identity: Windows checkpoint

## Result

Clean replay `cc48992f0ff40fa95123a45015caac5f596ab57f` passes 18 actual Windows
processes with 18 unique PIDs across six Bases. Three archived original v11
Bases and three new evolved v12 Bases consume the same two current payloads,
including independent skipped updates. Every process executes 220 cases with
zero differences, without pre-touch. There are 792 successful outer assertions
and 72 explicitly inapplicable legacy probes. All 114 distinct immutable files
were independently rehashed after replay and are unchanged.

This is the established capability regression, not a generic-field-address pass,
unrestricted evolution, performance qualification or a formal release.

## Defects and repair

Previous generic-field workflows accepted a v12 runtime manifest while compiling
old installed native sources. All nine generic-field Players failed registration.
The host now checks the staged runtime hash and exact installed source file set
before and after every Unity stage, including Exploratory mode. Thirteen binding
regressions pass on each actual stale project. The actual Unity 2021 workflow
rejects nine stale/missing files before launching Unity. Only three specific
package-generated runtime files may differ from the staged source.

Installing the correct runtime and rebuilding reveals a second defect: different
GameAssembly.dll binaries share BaseId and native-manifest hash. Package `93f436e`
adds the actual source digest/count and generated-file digests to that manifest,
which is already hashed into BaseId. The package checks for source drift after
Bee finalization; the host independently checks the final manifest. Thirteen
identity regressions pass for each engine's actual old and new runtime trees.

All three actual Editor workflows compile the package, build final Players and
pass no-op/schema/final-source checks. On each engine, managed assembly and guard
hashes stay unchanged while BaseId differs from the previous unbound identity.

## Source identity

| Component | Candidate commit |
|---|---|
| HybridCLR | `1dd57b46bd86b4470ef8d2195536519fac667af2` |
| Unity 2021 IL2CPP | `3a0ce4d178033a9edd32b62988ff8a1bf0432150` |
| Unity 2022 IL2CPP | `2cd3264524cf566a315f83222bebf6fdff31cc7a` |
| Tuanjie IL2CPP | `00c3a97144111d8aca6029a6613c3192a9276f46` |
| Unity package | `93f436e07ac10d38642a795dfca4cb47f81c42ac` |
| Base workflow tool/source lock | `7acebef` |
| Cold six-Base replay | `cc48992` |

Package tree SHA-256 is
`DE476C434258BF38C6777FB5AE1B3F4106434CA90CA177CE81CE835C5C3ADF73`.
Native contract remains v12 and MV stays DHEMETA1/schema 1. This repair changes
package/tooling identity handling, not native runtime semantics.

The three assembled libil2cpp and external-header hashes are identical to the
previous `native-generic-fields-verified` matrix inputs. Its real-header
compile/CTest results remain evidence for those unchanged native bytes; no new
native matrix was run or claimed in this package-only repair.

| Engine | New evolved BaseId |
|---|---|
| Unity 2021 | `f514481f100662aa87023a3f171c3cbb6c31ad7aec9723ab5062eabc9bdda11b` |
| Unity 2022 | `21b7aa251655ce67ed36f6e0b54a306756a046cccfcd920107f9d2f5cbe6d6e2` |
| Tuanjie | `f791d5eb39e893807982fb6f0e7d624917b034837ef2cac2f31737b5d9f2fd87` |

## Evidence

Paths are relative to `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `runtime-binding-tests`, `runtime-binding-tests-u22`, `runtime-binding-tests-tuanjie`:
  source binding checks, plus actual `workflow-runtime-binding-rejected`.
- `native-source-identity-tests-u21-finalize`, `native-source-identity-tests-u22`,
  `native-source-identity-tests-tuanjie`: package capture/drift checks.
- `base-native-source-identity-<engine>/project-workflow-report.json`: three passing
  source-bound workflows; package migration committed before building.
- `registry-native-source-generations.json`: three original and three evolved
  Bases, each Unity 2021 Base with its own supplemental metadata set.
- `resource-native-source-generations-aot` and `resource-native-source-generations-interpreted`:
  common resources from unchanged full-suite-cold inputs and references.
- `replay-native-source-generations-cold/report.json`: 18-process pass, SHA-256
  `DB402CDBEFF4AC12FE9236FCDD59A94FDE5BF6F3DDB448D6BF45B63DE09F44F4`.

## Open regression

Before the identity repair, correct installed runtime bytes reached actual
generic-field execution. The `303d755` report `replay-generic-fields-bound-current`
records nine registrations returning OK, then nine failures at the original
`generic-fields-nullable` assertion: `ldflda is not supported for DHE supplemental
instance fields.` Report SHA-256 is
`878B827EFDA8BA6E2FA24F49B094E8B790C4A0C86136CCD88062807C2D53A896`.

Those diagnostic Bases predate the source-bound manifest. The passing resources
above are not evidence for that failed fixture. Its C# expression and exact DLLs
remain unchanged. Field addresses, closed-generic address analysis and token/name
collision coverage are next. Interface/vtable and value-type layout evolution,
Unity behavior, AOT API availability, concurrency/GC/ABI and performance/memory
remain required before the full Windows handoff.

## Preservation

All old Players/resources/failures remain. Old installed sources are preserved
in `stale-installed-runtime-<engine>.zip`. Generated Demo inputs were stashed:
Unity 2021 `27e5f29` and `eccba39`; Unity 2022 `676da61` and `775b34d`; Tuanjie
`4a64f9a` and `cc1bf98`. Older stashes remain. Demo projects retain four ordinary
generated input DLL changes, not uncommitted package patches.

No formal branch, runtime tag, Installer default, CAT project or protected channel
changed. Toolchain is Exploratory, not release-ready. Rollback uses earlier
matched package/tool/native locks and resources, never rewritten Base identities.
Windows does not qualify macOS, Android, iOS or production gains.
