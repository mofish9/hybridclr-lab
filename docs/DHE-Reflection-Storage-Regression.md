# Reflection storage regression

Scope: Unity 2022 Windows first, per the current engine scope. The reported
method-only failure occurs in RuntimeType.GetFields_native: GetDheCurrentType
returns the hidden metadata view even when no type has been selected for new
physical storage. Base boxed values then fail the managed FieldInfo owner check.
This is a concrete hypothesis to test, not evidence that callers require
assembly-wide interpretation. Unaffected methods must remain eligible for AOT.

Acceptance: load a non-identical DLL with no selected physical types, read/write
fields on Base boxes, preserve declaring/reflected types, and reject a box from
the same-named type in a different assembly. Also replay the evolved-layout
fixture so Current physical fields still work. Check a changed method's actual
return value separately from the original semantic tests. Native compile/CTest
alone does not execute reflection in the managed VM.

The minimal fix belongs to the Unity 2022 RuntimeType hook and is reversible as
one source commit. It adds no cache, locks or publication state. Correctness is
the primary metric; no throughput, tail-latency, memory or mobile claim is made.
The public package loader, guarded native callers and per-Base identity checks
remain separate acceptance requirements; research probe success is insufficient.
