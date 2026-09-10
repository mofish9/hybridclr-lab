# Existing hotfix parent evolution on immutable AOT Bases

Continue after the framework callback checkpoint. Insert a Current-only managed
intermediate parent between the existing Processor and ProcessorRoot types.
The intermediate parent owns new reference/value fields and constructor logic;
Processor retains its public root contract and original virtual signatures.
Retarget its constructor to the compiler-produced intermediate constructor.

Use a fresh copy of the eighteen-check framework Current and the already-built
old/grown Bases 89/90, plus Base-81 as a new-type control. Preserve all original
Current DLLs, Players, failures and the 46 business, 25 virtual-signature and 18
framework checks. Verify inherited field offsets, parent construction and
exceptions, direct/reflected hierarchy queries, inherited reflection, virtual and
generic dispatch, object identity and reference survival across GC.

First compile with the real Unity 2022 compiler and establish CLR reference
behavior. Run the unmodified resource compiler to reproduce the actual admission
failure. Fix planning/admission only with explicit native/Player evidence; do not
disable unrelated layout, ABI, external-parent or old-receiver checks. If native
changes are required, use isolated HybridCLR and Unity 2022 IL2CPP worktrees,
commit first, and build new immutable Players with matching package capabilities.

The main metric is zero semantic differences. An unchanged Base/control must
retain its AOT path. Parent insertion is the first case, not the entire goal:
replacement/removal, generic/cross-assembly parents, Unity Scene/Prefab, startup
object semantics, concurrent publication and performance remain required work.
This checkpoint targets Unity 2022.3.62f3 Windows and makes no Android/iOS,
production, memory or throughput claim. No CAT, formal publication, Installer
default or ordinary AOT source change is authorized by this experiment.

The original compiler rejects both existing-type Bases at
existing-type-layout-or-vtable-change:Processor, while CLR passes all suites.
The first candidate permits physical reference-parent changes only when the
owner's other declaration/layout properties remain unchanged and both local,
nongeneric parent chains terminate at the same immutable AOT boundary. Missing,
cyclic, sealed and unresolved/generic parents are rejected. Existing Current
storage, reference dispatch and virtual-frame capabilities remain required;
no claim of runtime support is made until the same Current passes the Players.

Rollback selects the preceding matching source locks and a compatible archived
resource, then restarts the Player. Do not rewrite Base identities/capabilities
or unload a committed Current set in process.
