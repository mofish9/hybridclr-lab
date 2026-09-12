# Existing hotfix thread-static values

The package currently rejects every existing thread-static field whose value
layout evolves. Runtime SelectCurrentStaticValueField already remaps evolved
static values to a physical interpreter field; InterpreterImage recognizes TLS
attributes and the interpreter uses the physical class's per-thread allocation.
This is a hypothesis of existing support, not proof that removing admission is safe.

Use a separate package candidate to admit only hotfix TLS values, retaining the
ordinary-AOT and RVA obligations. Replay the actual unchanged TLS owner from
evolved-current-03 (negative in the original tool), after the constrained-call
runtime correction. Require complete reference order and values, worker default
state and main/worker isolation. Do not replace the field or rename its owner.

This is startup resource loading, not migration of live thread state after
business execution. Native identity and full Player testing remain mandatory;
the relaxed candidate must not be published before these pass. If runtime field
mapping lacks an owner/GC/cctor invariant, implement it and add the corresponding
negative and Player coverage instead of declaring success from the planner.
