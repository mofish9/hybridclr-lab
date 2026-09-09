# Frozen generic context dispatch candidate

Target: Unity 2022 Windows. Tuanjie port follows Windows correctness; Unity 2021
is outside the current requested scope. Ordinary AOT sources remain immutable.

The observed failure is Nullable<long> entering a definition-wide Current ABI
guard after Nullable<Payload> requires adaptation. The caller should retain AOT
when only generic arguments determine the dependency and its concrete arguments
are unaffected. Methods with concrete layout dependencies remain unconditional.

The compiler will mark a sorted subset of frozen selected method tokens as
generic-context-dependent. The native source transaction, pending-image retry
identity, published registration, resource records and managed validator must
carry the same subset. Conditional methods cannot belong to a physically changed
definition. Native registration must reject non-generic or unselected members.

Dispatch checks both argument remapping and arguments already using interpreter
storage. A Current MethodInfo remains Current; a Base closed method whose class
and method arguments are unaffected retains its native entry. No same-size ABI
exception or unvalidated source-name exemption is introduced.

Primary gates: affected Nullable copy/null correctness and unaffected long
nullable behavior with zero interpreter entries; original 34 core checks;
generic/container/array/byref preservation; negative selection validation and
unchanged Base behavior after a rejected transaction. Native compile/CTest uses
real Unity 2022 headers. All new Player evidence must bind committed candidate
runtime, package and lab sources. P50/P95/P99 and memory are not qualified by this
correctness fixture and no performance benefit is claimed.

Dispatch state remains one immutable snapshot published with release/acquire.
Conditional reads must consult that snapshot, and allocations retained after a
failed load must not permit a changed conditional subset on retry. Runtime API
capability admission must prevent older Bases from accepting plans requiring
this behavior. Reverting this candidate returns to conservative rejection;
ordinary hotfix startup remains the project fallback outside this experiment.
