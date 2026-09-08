# Existing value-type layout evolution

Continue the fixed-Base DHE objective; do not reduce it to method-body edits or
field reordering. Only the original hotfix set participates in DHE. Qualify
Unity 2022 App and Tuanjie 2022 mini-game runtime on Windows; no new Unity 2021
gate is required. Android verification follows with the user.

Freeze the preceding native-descendant checkpoint: lab 821b011 and HybridCLR
760634e, with the same U/T/P commits recorded in the ten-Base report. Work in
research/dhe-value-layout-v8.13.0 worktrees; leave those archived Players,
payloads, failed reports, ordinary AOT DLL and formal release references intact.

The first implementation checkpoint is recorded in
reports/dhe-value-layout-impact.md: 35 impact checks, 14 CLR semantic groups per
version, and 40 byte-identical archived MVs. This is planning/reference evidence,
not a value-layout Player pass. Native storage and dispatch work remains open.

## Representation and correctness requirements

An existing struct may grow, shrink, reorder fields, gain a managed reference,
or change a nested/generic value field. Local copies, arrays, boxed values,
by-ref aliases, nullable values, containers, reflection and GC must see Current
semantics. A per-object reference-class sidecar alone cannot provide independent
copies for unboxed values, stack locals or array elements.

Use the existing Current metadata image as the starting point for a Current
value representation. Before switching storage, determine the entire layout
dependency closure: nested value fields, closed generics, class fields and
derived reference layouts, plus methods whose old native code embeds these
representations. Unchanged IL does not imply an unchanged ABI. Ordinary AOT
inline storage or typed ABI consumers require an explicit bridge solution;
silently loading larger Current values through old native storage is forbidden.

Do not mutate generated C++ or the installed runtime. Do not remove the existing
value-layout rejection until native storage, calls and publication have been
implemented and exercised. A diagnostic/planning result is not permission to
release such a resource and must not be reported as a passing Player.

## Implementation and evidence sequence

1. Add a reusable managed Base/Current fixture with direct, nested, generic,
   cross-assembly and same-name/different-assembly types. Verify value semantics
   against CLR and reproduce the current compatibility rejection. Also expose
   native dependency gaps: arrays, closed generic signatures, by-ref types,
   called method signatures and cross-assembly references must not be considered
   safe merely because the old method-version hash happens to match.
2. Implement a deterministic, assembly-qualified layout-impact analysis over
   the complete Base/Current hotfix set. Include instance layout propagation and
   explain every affected method. Keep unrelated AOT code eligible for retention.
   Report open generic context and ordinary AOT boundaries explicitly; an
   unresolved dependency cannot be silently labeled safe.
3. Implement Current layout/type/field selection and native dispatch rules,
   including value copying, array/boxing paths and appropriate AOT boundaries.
   Preserve one public logical identity without confusing native and Current
   physical representations. Publish registration only after the complete set
   is prepared; audit release/acquire and rollback of partial initialization.
   First separate Base method identity from Current execution metadata in the
   registration transaction. Explicit internal Current bindings must force
   interpretation even if the MV method version is unchanged, preserve native
   Base pointers, reject invalid bindings before preparation, and roll back
   partial Current preparation. Block entry through an incompatible Base ABI;
   only a matching static primitive/reference frame can use that native guard.
   This internal mechanism alone does not enable value-layout resource loading.
4. Freeze native/package/engine/tool identities, run real-header compile/CTest,
   managed differential and two-engine Windows Base/no-op/resource tests.
   Extend the multi-Base matrix with an evolved value-layout Base consuming the
   same latest resource as the older Base; consecutive/skipped updates remain
   new processes. Reuse archived DLL/MV identity checks.

Keep MV named MetaVersion, retaining DHEMETA1/schema 1 where possible. Changing
the fingerprint algorithm must not make archived Base DLLs reconstruct different
MV bytes or falsely claim unchanged native callers have been validated. Separate
a runtime dependency decision from a method-body change when required.

Primary acceptance: zero semantic differences, independent copies, correct
aliasing and GC, unaffected no-op AOT execution, and unchanged original Player
files across updates. P50/P95/P99, end-to-end first touch, AOT retention and memory
are later gates; no performance claims are made from correctness counters.
The rollback boundary is this isolated candidate and the preceding ten-Base
source/artifact combination. A Base without future native layout support cannot
receive that support through DLL/MV alone.
