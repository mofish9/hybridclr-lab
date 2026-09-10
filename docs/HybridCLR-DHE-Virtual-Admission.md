# Admission for Current virtual signatures

The fdf1299 HybridCLR / 816778a Unity 2022 runtime passes the fixed old-layout,
grown-layout and new-type-control Players. Resource compilation must now identify
which existing Bases need those native fixes. A package update cannot install a
native correction in an immutable Player, and archived capability lists must not
be edited to make a resource pass.

Add separate capability requirements for generic MethodImpl declaration owners,
Current virtual signature frames, and scalar instance frames whose receiver keeps
Base storage. Detect these from compiler metadata and the existing per-Base
execution plan. Do not change MV bytes/schema, execute methods during analysis,
globally select interpretation, or broaden native ABI admission.

Verify the real old/grown/Current DLL sets and a Base without the new workload.
Require negative capability checks, unchanged-plan controls, the new-type control,
and byte-identical MV output. Preserve the existing declaration and managed plan
gates. Package capabilities and source locks must identify the matching runtime.
Body-only changes with compatible existing signatures do not require new physical
frame support. A control changing one reference-valued virtual body reproduces
the initial over-rejection; require storage/execution-plan selection for this
capability while retaining the separate startup MethodImpl requirement.
Rebuild Players with the new package before claiming an end-to-end admitted
resource; the preceding Players remain runtime evidence only.

Current scope is Unity 2022.3.62f3 Windows. Full DHE still requires broader native
callbacks, declaration/parent changes, Scene/Prefab and old-object semantics,
publication stress, performance and memory validation. No formal publication,
Installer-default change, CAT migration or Android/iOS qualification is claimed.
