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

## Verified Windows result

Runtime `5b56060a9ef7392044f7150ad2a696266a339abd`, IL2CPP
`8a13baf1ec45068fbb9535beea03425b717f501b`, package
`e48be87b4a0dad0375a823b6ebb548a467c32f37`, Player fixture lab `2c3a90e`.
`artifacts/dhe-frozen-entry-proof-10` passes all 35 core checks (PID 52172),
including rejection of changed generic conditions on a pending-MV retry.

Immutable replays: `-nullable` (PID 18384, 39 checks), `-generics` (PID 32860,
37 checks), `-arrays-byref` (PID 6800, 38 checks), all passed. The nullable
probe preserves added long/object fields and null semantics, then requires
positive AOT entries and zero interpreter entries for unaffected Nullable<long>.
Every run retains GameAssembly SHA-256
`F6172B0CC53E6D73102F0C36AACE148E9392FAC824A53653BD73A318B1636843`.

Native compile/CTest passed in
`artifacts/dhe-generic-context-20260909/native-01/DHE-Unity2022` with real headers
and no surrogates. Its runtime source tree SHA-256
`9DFE32F33E7584E4D090C984EDF25F1C4A0CC1C3912EA2234F9FF3B1CA8D46F7`
also belongs to runtime-02 used by this Player; package path validation changed
between these manifests, while native source did not.

Managed host checks passed 68 cases in `managed-03.json`; after lab `83899ab`
added staging comparisons, `managed-04.json` passed 71. Both use the committed
package above. Host tests record native arguments and are not native execution.
This resolves the reproduced generic dispatch failure, not universal generic
coverage, old-object migration, multi-Base admission or release qualification.
