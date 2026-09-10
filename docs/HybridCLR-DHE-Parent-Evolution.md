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

The preserved first candidate fails real existing-type Players: Base-89 passes
eleven of sixteen parent checks but inherited fields/methods fail reflection;
Base-90 stops at the Current-call-frame guard. Base-81 passes as a new-type
control only. The native frame control at lab 3d54300 compiles against the
unchanged fdf1299 runtime and fails its fifteen positive physical-frame checks.
All unresolved/different-layout and existing receiver guards remain passing.

The next candidate compares actual physical signature classes, retains the
staged receiver/parent proof, and queries published Current ancestry for member
reflection without changing raw Class::GetParent or object layouts. Package
capabilities explicitly distinguish these changes. Existing-type parent updates
require both capabilities; no-op and wholly new types do not need the parent
capability. Old Base inventories stay immutable. Reflection traversal includes
properties/events; those need additional Player probes beyond the original
sixteen checks. Source locks select this candidate, not a passing Player claim.
