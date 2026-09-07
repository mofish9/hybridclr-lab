# DHE same-runtime mixed generations: cold Windows checkpoint

## Result and identity

The frozen `7e356873f925e251237f0890e690e089807caac5` replay passes 18 actual
processes with 18 unique PIDs on six Windows Bases. The source worktree is clean;
`diagnosticPreTouch=false` and `requiredStructuralBaseGenerations=true`.
Every process executes 220 cases with zero differences. This is current candidate
correctness evidence, not complete managed evolution or production qualification.

Native and package sources are exactly those in
`reports/dhe-evolution-compiler-integration-windows.md`: HybridCLR `a57999e`,
Unity 2021 `3a0ce4d`, Unity 2022 `2cd3264`, Tuanjie `00c3a97`, package `2c04c24`.
No native code or compiler patch changed. Native contract remains
`dhe-runtime-v11`; MV stays `DHEMETA1`, schema 1. The same exploratory host,
toolchain and assembled runtime manifests built the additional original Bases.

The managed generations really differ: original Bases do not contain the
structural fixture, while evolved Bases do. All four configured hot-update
assemblies enter AOT in every Base. The original input root is
`artifacts/managed-cases/StandaloneWindows64` under the lab worktree; the evolved
input root is `artifacts/evolution-repeated-evidenced-raw`. Neither input was
rebuilt or modified for this replay. The two current resource input roots and
exact-DLL CLR/golden references are unchanged from the preceding cold replay.

## Windows results

Artifact paths below are relative to
`C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

All three `base-compiler-original-<engine>` workflows pass Base construction,
no-op AOT Player and schema gates. They are new outputs; no archived Player is
rewritten. They join the three `base-compiler-integration-<engine>` evolved Bases
in `registry-compiler-generations.json`.

`resource-compiler-generations-aot` and `resource-compiler-generations-interpreted`
each contain one common current DLL/MV payload for all six Bases. Every configured
Base passes resource compatibility; the replay covers the exact supported set.
Each Base runs both resources consecutively and independently skips the first.

| Managed generation | Engines | Processes | Cases/process | Differences |
|---|---:|---:|---:|---:|
| Original | 3 | 9 | 220 | 0 |
| Evolved | 3 | 9 | 220 | 0 |

The six retained-AOT runs each record zero case-entry receipts. All twelve
interpreted/latest/skipped runs each record 220 receipts. Every case's native
changed status matches Base/current MV. Original Bases pass 48 outer structural
assertions per run; evolved Bases pass 40 and mark eight absent legacy probes
inapplicable. Totals are 792 successful checks and 72 inapplicable checks. The
four required repeated-structure/Attribute receipts and CLR scalar observations
are checked on every run. Ten immutable files per run retain their original hashes.

| Engine | Original Base ID |
|---|---|
| Unity 2021 | `021e29c6fdc7ee90601cf95b42ced933e6196cce57204221b876dd0f4e64b1f5` |
| Unity 2022 | `7649bf59f829be747baf853678074e111ac33a1e1771c032d971851846ac42e4` |
| Tuanjie | `072fecb850bfefe205e5e51aea6922caacf999018e778298131587ae9eb53e3c` |

Evolved Base IDs are unchanged and recorded in the preceding integration report.

| Artifact | SHA-256 |
|---|---|
| Six-Base registry | `001F396828F574EBADE01315768CAD8E17E781A1BB6884DC595E8296A0C1CDC8` |
| Retained-AOT resource manifest | `B99C1041F9F26D75317CACE70697C63788561DB09CC5BB32EB1182A9428E5B64` |
| Interpreted resource manifest | `2F6B7E34BAC6C6F5C82E5B90314BB6BBBCC1DE1F0697FC8F0624A46CD517B806` |
| `replay-compiler-generations-cold/report.json` | `242E6AB06DCF69D517A0FA3D016BFB5CA16F2047BF16D612C1ECED754CF39F44` |

## Preservation and remaining work

Existing disposable Demo projects were reused to limit disk growth. Their prior
generated evolved inputs were preserved in stashes: Unity 2021 `167613b`,
Unity 2022 `1f49f29`, Tuanjie `28711e4`. All older stashes remain. Bootstrap stages
original input DLLs through the existing package adapter, not manual generated
C++ edits. After all three builds, project-local compiler hashes equal the
original hashes recorded by their native manifests, and no transaction journal
remains. The original Editor compilers are unchanged.

This gate covers one pair of managed generations per engine and two current
resource releases. It does not prove unrestricted future code changes. Generic
fields, field-address access, existing interfaces/vtables and value-type layout
still need implementation and real Player evidence. External AOT API availability,
Unity-facing semantics, concurrency/GC/ABI, build/startup cost and performance/
memory measurements remain required. The field storage and generic metadata
paths require dedicated tests, not relaxation of offline rejection rules.

No formal maintenance branch, runtime tag, Installer selection, published package,
protected channel or CAT project changed. Windows results do not qualify Mac,
Android or iOS. Native fixes require a new Base identity; rollback must select
resources within each archived Base's real capabilities.
