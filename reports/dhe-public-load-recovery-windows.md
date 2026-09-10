# Public DHE load outcome and process recovery

Continue after the v31 three-Base module/literal/inline checkpoint. Target Unity
2022 Windows first, then Tuanjie; no new Unity 2021 work. Starting identities:
HybridCLR `8417ea0`, IL2CPP `4d5052e`, package `6d59a27`, lab `b1e3da8`.

The public loader catches a module exception after native graph publication and
reports registration failure without completing its managed bookkeeping. Reset
clears managed state but cannot undo native registration or initializer effects.
This is a release blocker. Reproduce using a real compiler-generated module cctor
whose initializer records its version and run count, then throws. Use the same
Current on Base-36 (mixed/frozen sources) and Base-37 (existing differential only).
Preserve all failed resources, Players and original source identities.

The new tracked native batch entry reports a per-call phase through an out int:
not started, preparing metadata, metadata prepared, committed, initialized.
Write committed immediately after successful atomic graph registration, before
running user code; exceptions must not erase it. The out field belongs to the
managed load attempt and is inspected only when the synchronous call returns or
throws. It is not a new shared native cache or a substitute for the existing
metadata publication barriers. Legacy native entry points retain their behavior.

Public loading serializes attempts with an atomic busy flag, releasing it before
return. No managed monitor stays locked while native code runs initializers.
Callbacks and other threads receive an explicit in-progress result. Expose stable
load state, confirmed metadata commit and the original error. A rejected prepared
graph can retry the same validated bytes. Exceptions during metadata preparation
or after commit require a new process. Reset/reinitialize must never erase the
fact that native metadata has been touched. A successful load is not reloadable
with another Current graph in the same process.

Correctness gates: preserve the existing resource argument-selection tests;
exercise public pre-validation rejection/corrected retry, native MV rejection/
corrected retry, post-commit module failure, rejected reentry/reset/reinitialize,
stable failure details, and fresh-process recovery to a valid resource. Test mixed
new-assembly/frozen sources and existing-only resources on immutable Windows
Players. Keep all 46 reference cases and unaffected AOT dispatch assertions.
Run real-header native compile/CTest and independently bind artifacts to commits.
There is no performance claim. The new API needs a matching Base runtime/package
capability; an old shipped Player cannot acquire it through Current DLLs.

Rollback selects the preceding source combination when building a new Base and
retains the v31 module checkpoint. A loaded process is recovered by restarting and
selecting a known accepted resource, never by resetting initializer side effects.
No formal branches/tags, Installer defaults, CAT files or remotes are in scope.
