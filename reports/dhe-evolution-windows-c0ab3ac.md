# Windows DHE structural evolution replay

## Evidence identity

- Test source commit: `c0ab3ac8870da5793463bbd22b95023c9b65f55c`.
- Test source tree: `decb0f0f02fb641573c66024d8f2fa1ce07aeb70`.
- Working tree at replay: clean.
- Report: `C:/hybridclr_optimize/artifacts/dhe-evolution-20260907/replay-frozen/report.json`.
- Report SHA-256: `88b10c7fc24cc8d29af22e4856f8b0109966c2ffdd7e6cc454c4a8c67b880f05`.
- Runner SHA-256: `ab98ba89946ced744ddc0627fb680e8d3598211aa94613c6a6afe59b71105408`.
- Tool SHA-256: `6ebfc23648cb18289a5daca3127332d8a28c0c0f54b66ea5a2e2ee5c617e3234`.
- Configuration SHA-256: `a709f59a3330acb95c1b5017b14c5c267bbdd6ad4e61d55970e6988b984213a9`.

The native Players are preserved, previously built Base artifacts. Their runtime
was not rebuilt or changed in this iteration. Current DLLs were freshly compiled
and Unity-prepared during this iteration; their payload and manifest hashes are
recorded per run. The frozen test runner actually launched each Player below;
it did not revalidate old Player result files. This is exploratory correctness
evidence, not a new formal runtime release.

## Results

All 13 runs passed all 48 required assertions, for 624 assertion evaluations and
13 unique process IDs. These are repeated evaluations of a 48-assertion fixture,
not 624 distinct managed test cases or a performance sample.

| Base | First resource changed methods | Second resource changed methods | Result |
|---|---:|---:|---|
| Unity 2021 default | 71 | 72 | passed |
| Unity 2022 default | 71 | 72 | passed |
| Tuanjie 2022 default | 71 | 72 | passed |
| Unity 2021 newer r6 | 53 | 53 | passed |
| Unity 2022 newer r6 | 53 | 53 | passed |
| Tuanjie 2022 newer r6 | 53 | 53 | passed |
| Independent Unity 2021 default copy | skipped | 72 | passed |

Both resource releases contain one current DLL/MV set for all six distinct Base
IDs. The second retains the first's structural changes and advances two method
bodies. Every run checked its selected Base, current payload, engine, both AOT
and interpreter execution, structural/reflection behavior, and ten immutable
files including native binaries and embedded Base MV. No Base was rebuilt.

The original full replay report remained byte-identical after the runner rejected
an attempted reuse of its nonempty output directory. Original archives were not
modified; the runner operated only on independent Player copies.

## Open capability work

The `evolution` fixture adds four methods to an existing type: ordinary and
generic attributed methods, an async entry, and an iterator entry. It also uses
LINQ, introducing a System.Core reference. Its CLR reference assertions pass.
The Unity-prepared payload is rejected for every selected Base by the current
compatibility policy. The rejection report is:

`C:/hybridclr_optimize/artifacts/dhe-evolution-20260907/resource-evolution-six-base-rejected/dhe-resource-update-validation.json`.

No compatibility check was bypassed. Remaining implementation includes correct
current-image attribute lookup for supplemental methods and generic instances,
and review of safe assembly-reference evolution. Later work must also test a
new Base that already incorporates structural changes, further field/type/ABI
evolution, the independent managed differential suite, and performance/memory.
Android validation remains a user handoff after Windows work is ready.

No formal maintenance branch, runtime tag, package migration, or production
channel was changed by this candidate. The Demo-only missing directory helper
was repaired, but its embedded package still differs from the locked formal
package. Reconcile that source discrepancy before final distribution.
