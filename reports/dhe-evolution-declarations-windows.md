# DHE declaration identity: Windows checkpoint

## Scope and source identity

This checkpoint fixes a real supplemental declaration identity failure. It is
exploratory Windows evidence, not a formal release, complete C# evolution support,
or multi-generation Base qualification. No CAT project, formal branch, or tag
was changed.

| Component | Commit |
|---|---|
| HybridCLR | `66454a4572f6e0e68ed13082abefccd665ae0a6b` |
| Unity 2021 hooks | `4d76aca57497d48e17d4befeec7c4c9ca40a836c` |
| Unity 2022 hooks | `3e30e0ea6306e03ec347f3a98f5d9f3728283076` |
| Tuanjie hooks | `240e76804658189d38777608b13b583e4c950920` |
| Base package | `f04bffc5ffcc1b49c86e63a83b2fdf2c6f1d65eb` |
| Base workflow and compatibility tests | `24505b8` |
| Expanded fixture and frozen replay | `d42ec47a692fe68fa5caa98bb2c89dc857077881` |

Runtime contract is `dhe-runtime-v6`, with
`supplemental-type-declarations-v1`. MV serialization and stable identities are
unchanged. The package Installer still selects older formal native tags.

## Failure and repair

The original v5 Base loaded the update but failed all five declaration groups:
method parameters/returns, field types, generic field arguments, inheritance,
and attribute Type arguments. Hidden Current type definitions leaked into public
declarations. Homologous type references are now bound before initializing
signatures, constraints, parents and layouts; hidden storage definitions remain
separate. Type-valued attributes also canonicalize generic and array arguments.

All paths below are under `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `player-declarations-u21.json`: reproduced failure on the unmodified v5 Base.
- `resource-declarations-oldbase-rejected/dhe-resource-update-validation.json`:
  the corrected full host command rejects that Base for its missing capability.
- `compatibility-declarations-frozen/report.json`: 14 positive/negative checks pass.
- `native-declarations-verified/<profile>/native-gate.json`: all three profiles
  pass compile/CTest, `mergeReady=true`, no surrogate headers. FGS is enabled for
  Unity 2022 and Tuanjie. These tests bind to the package commit above.
- `base-declarations-u21/project-workflow-report.json`: full new Base workflow
  and no-op Player pass with Unity 2021.3.45f2, OptimizeSpeed, supplemental metadata.
- `player-declarations-fixed-u21.json`: five declaration groups pass, 109 changed
  methods; the main probes still record 22 interpreter and 56 AOT entries.
- `player-declarations-expanded-u21.json`: ten declaration groups pass, 129
  changed methods. Added coverage includes arrays, ref/out invocation and
  delegates, Base interface implementation, generic constraints, generic
  inheritance, and instantiated/raw generic and array Type attribute arguments.
- `reference-declarations-expanded.json`: the same raw fixture passes a C# CLR
  reference runner. Input DLL SHA-256 is
  `0D25332F2D2349433DD47EF82227A561CB0D1A9D774BC895721161C73693AAD9`.

## Frozen resource replay

`replay-u21-declarations/report.json` records three unique processes on **one**
Base identity, 48 outer assertions per run and ten immutable files per run.
The expanded payload's nested assertions contain all ten declaration groups.

| Sequence | PID | Changed methods | Outer assertions |
|---|---:|---:|---:|
| Base to initial declarations | 45200 | 109 | 48/48 |
| Initial to expanded declarations | 36888 | 129 | 48/48 |
| Independent Base to expanded declarations | 38016 | 129 | 48/48 |

Base ID: `92d1d85025741a27e529b596de6f56957fdfb52111d810057de6ff23dbb528cd`.
Replay SHA-256: `E7EFC1CE5CB8A5D99FA5158EB4F04351CE07C27AA77DDD305F7A60F37357D670`.

## Remaining work

Fresh Unity 2022 and Tuanjie Demo imports/installations succeed, but their first
Base preparations exposed a linker lifecycle bug: `data.inputDirectory` is not
populated when Bee constructs the linker graph. The package's explicit build-input
capture repair is being tested separately; it is not included in the frozen
package evidence above. A previously cached project's success is insufficient.

Still required: real Player runs on these two engines, a newer Base already
containing structural evolution, shared releases across old/new capable Bases,
member-level AOT API availability checks, full managed differential, remaining
structural/ABI capabilities, and performance/memory gates. Windows does not
qualify Android ARM64 or iOS/macOS. Native fixes require a new Base; they cannot
be delivered by rewriting an archived Base's capability declarations.
