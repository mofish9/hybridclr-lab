# Frozen empty value methods

Independent package hypothesis: a frozen parameterless instance void method
whose IL is only nop/ret, has no locals/EH, and whose value owner has no cctor,
does not observe its receiver's layout or transfer value data across the ABI.
Its original native entry remains valid. This is an exact bytecode predicate,
not an exception based on the method name Dispose or a fixture-only rewrite.

Keep every other method's Current selection, ABI guards and full Base coverage.
Admission must check the same predicate against authenticated original Base IL.
Do not excuse methods with parameters, return values, constructor semantics,
type initialization, receiver reads or other instructions. Prove these negatives
and the actual archived corlib method with unit checks.

Replay all 46 cases on the original b0fe826 opt5 Player, using the original TLS
owner and evolved value layout. This can remove an unnecessary fallback, but
Dispose runs in finally and may be masking another error: full sequence and
reference equality remain required. Diagnostic native mapping candidates are
separate and do not count as success for this package-only hypothesis.
