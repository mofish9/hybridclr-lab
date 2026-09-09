# Boxed Base value adaptation candidate

Unity 2022 Windows probe proof-09 shows that a Payload boxed before DHE loading
cannot pass unbox.any in the selected frozen copy method afterward. The Base
object still has its original physical storage, while the Current method needs
a larger independent value. Widening the cast or copying Current-size bytes from
the original object would be incorrect.

Implement an explicit Base-to-Current value copy for interpreter unbox.any.
Bind retained fields by the same Base signature matching used during metadata
initialization; copy by their actual offsets, zero new/retyped fields and skip
removed fields. Recursively adapt nested values and compatible closed generic
storage. Preserve the original boxed allocation and object identity. No native
typed ABI exception is allowed.

This step does not solve unbox byref aliases, old arrays, or migration of changed
reference objects. A retained non-null reference whose physical type changes
must remain an explicit failure until the object-migration contract handles it.
These are remaining full-goal gates, not exclusions from final DHE support.

The mapping is completed under metadata preparation and read only after DHE
publication. Copies target the interpreter stack, not shared object memory;
the original object stays rooted as the instruction's argument. No cache of
unrooted object pointers or replacement of an existing allocation is introduced.

Required first regression: the same old boxed Payload retains Count=17 in both
the old box and its independent Current copy, with added fields defaulted.
Keep the complete core, nullable/AOT dispatch and generic/array/byref checks.
Extend reordered/nested/generic/reference-field and GC tests before broader
old-state qualification. No performance, memory or platform claim is implied.

## Verified Windows results

The initial retained-field implementation, runtime
`9c5ef72d62d4027e5cfee566d2bcb52a88dd4b83`, passed real-header native-02 and
proof-11 core (PID 46208, 35 checks), old-values (53748, 38), nullable (46980,
39), generics (25920, 37) and arrays-byref (35920, 38). Fixture lab was
`05d29b0`. Its GameAssembly SHA-256 is
`3E5C4C0B2990B422441ECE01FB79BD7BEE9CCC876255B80315A2DABB9AC1D9F8`.

Current runtime `42ecf89981f856c0bef2373153d1d5f7f1bb3ff3` excludes original
static fields from retained instance copies and restricts shared generic
fallback to actual changed argument representations. With IL2CPP
`8a13baf1ec45068fbb9535beea03425b717f501b`, package
`e48be87b4a0dad0375a823b6ebb548a467c32f37`, fixture lab
`c29e3212fc6ddbeea78cfd2bbdeac6db941a4190`, it passed
`artifacts/dhe-boxed-value-copy-20260909/native-03/DHE-Unity2022/native-gate.json`
(`mergeReady=true`, real headers, no surrogates). HybridCLR canonical source SHA
is `54A5AE4C076EDE383B44A1896EDDBD9BCDA8284DD16EB3AFFF325297A23E6FD7`.

| Suffix after `artifacts/dhe-frozen-entry-proof-12` | PID | Passed checks |
| --- | --- | --- |
| (core build) | 58128 | 35 |
| -old-values | 57328 | 45 |
| -nullable | 53280 | 39 |
| -generics | 23952 | 37 |
| -arrays-byref | 12984 | 38 |
| -order-swap (old-values) | 49216 | 45 |
| -order-reverse (nullable) | 46172 | 39 |

These replays use the same immutable Player, DLL/MV bytes and selections.
GameAssembly SHA-256 is
`AF64D3EF9521C94386DDD69AB340484A1242D472AC0C52CBDB5DAF8B2B22B94B`.
Each capability includes all 35 core checks. Added coverage verifies independent
repeated copies, nested fields, generic value contents, unchanged generic
reference identity and AOT dispatch, unrelated-box rejection and GC survival.
The GC checkpoint retains strong references; it does not qualify GC descriptors
or full object graph migration. Reordered/retyped fields, old arrays, unbox
aliases and typed reflection conversion remain separate gates.
