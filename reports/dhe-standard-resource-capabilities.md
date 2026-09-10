# Broader behavior through standard resources

Continue from the immutable Unity 2022 Windows proof-16 and proof-17 Bases.
Compile a Current-only C# suite with the target Unity compiler and captured Base
references, then merge its new definitions into the existing hotfix Model DLL.
The actual business entry invokes all assertions before returning revision 73.
Neither Base nor ordinary AOT source bytes are modified for these new scenarios.

The shared Current payload exercises value/generic copies, Nullable, arrays and
byref, List/Dictionary growth and enumeration, reflection/default arguments,
generic delegates, exception filters/finally, new generic/static/thread-static
storage, cyclic initializers, interface/virtual dispatch and concurrent first
static touch. Require the same Current bytes in both Bases, CLR reference success,
zero Player assertion differences, retained immutable binary hashes and the
existing snapshot rejection/recovery checks. A failing case must remain visible;
do not reduce the suite to obtain a passing result.

These are correctness fixtures, with case progress logging. They do not qualify
production-equivalent throughput, tail latency or memory. New Current-only static
storage is distinct from evolving an ordinary AOT static field that already
exists in a Base; the latter admission gate remains a separate implementation
task. Tuanjie follows Windows qualification. Candidate changes remain in the
research worktrees, with no CAT, formal branch, tag, remote or Installer changes.
