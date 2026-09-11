# Generic owner parent evolution

Change the already-AOT generic definition `GenericParent<T> : ProcessorRoot`
to `GenericParent<T> : GenericMiddle<T>`, retaining the same immutable root and
generic parameter declarations/constraints. Base104 already contains the owner;
earlier Base103 does not. One Current must execute correctly on both.

The producer currently rejects generic owners and closes parent signatures with
an empty context. First compile a real Unity workload, retain the rejection
control and pass the CLR reference. Then support symbolic owner parameters when
comparing the original/current immutable boundary. Preserve rejection of invalid
arity, changed constraints, swapped external arguments, cycles and unresolved
peers. Do not change MV binary identity or simply disable layout checks.

Player probes must exercise value/reference owner instances, fields at multiple
inheritance levels, generic methods, virtual dispatch, reflection, construction,
static isolation and inherited GC references. Keep the earlier business and
generic-parent suites. Use archived Base104/103 without rewriting their identity;
if runtime changes become necessary, build new Bases and bind real-header gates.

Correctness/identity gates precede performance. Unity 2022.3.62f3 Windows only;
no CAT, platform port, formal branch/tag or Installer change. The previous
candidate is lab `897fa5a`, HybridCLR `6180597`, IL2CPP `819f74c`, package `ed4b7b5`.
Rollback restores a compatible resource and restarts the process. Generic owner
arity/constraint changes, live object migration and arbitrary external root
changes are not established by this experiment and remain part of the wider goal.
