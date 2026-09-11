# Cross-assembly hotfix parent evolution

Continue the full DHE objective on Unity 2022.3.62f3 Windows. Use immutable
existing-parent Base101 and root-only/smaller-layout Base100. Produce one Current
from the preserved deletion Current, adding CrossParent to the Other hotfix DLL
and making the existing Model.Processor inherit it. CrossParent itself inherits
the original Model.ProcessorRoot, so the type hierarchy is acyclic and retains
the original immutable System.Object boundary. Compile the parent against Model,
then compile the assertions against the updated Other and record the fixture
parent/constructor/entry wiring. This intentionally exercises assembly dependency
cycles already representable by the DLL workflow; it does not change Unity asmdef
compilation rules. Ordinary AOT inputs and archived Players remain untouched.

Cover parent construction/fields/properties/events, reflected declaring and
reflected types, root virtual and generic dispatch, actual assembly identity,
GC and independent instances. Retain the complete 18 framework, 25 virtual and
46 business sequences and the unchanged-method AOT no-op proof. Primary gate is
the exact CLR assertion sequence and zero Player differences on both Bases;
secondary gates are immutable hashes, single Current, original Base ancestry,
negative capability/layout/ABI cases and public failure/fresh-process recovery.
No median/P95/P99 or memory benefit follows from correctness; those remain
separate production-equivalent full-goal gates.

First retain the real resource admission failure from the unmodified producer.
For admission, resolve both ancestry graphs against their own complete snapshot
sets. Never use Current declarations to fill a missing original Base parent.
Use assembly-qualified keys for cycle detection and parent identity. Reject
missing/ambiguous graphs, generic/sealed/value parents, changed immutable external
boundaries and unrelated owner declaration changes. Keep physical storage and
all existing native frame/member capabilities required. A per-assembly caller
without peer Base snapshots must remain conservative.

Planner acceptance alone is not runtime qualification. If real Player execution
fails, retain the exact Current and original failed Player, isolate the native
fix with any needed package capability, commit before tests and build new Bases.
Later qualify a Base that originally already has a cross-assembly parent, then
removal/replacement: existing snapshots on both sides are essential to that case.
Generic parent evolution, broader Unity assets/startup objects, publication stress,
performance/memory and full project handoff remain open. No CAT, Installer default,
formal branch/tag/push, Unity2021, Tuanjie or mobile work. Roll back with the matched
source combination or a compatible archived resource plus a process restart.
