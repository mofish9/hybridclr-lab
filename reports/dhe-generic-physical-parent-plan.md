# Generic physical parent evolution: candidate plan

The current producer rejects every existing class whose Base/Current parent is
a TypeSpec, and stops its boundary walk at generic instantiations. This blocks
valid hotfix edits such as changing an existing `Processor : ProcessorRoot` into
`Processor : GenericParent<Packet>`, even though the generic parent ultimately
derives from the same immutable external root.

The target remains resource-only evolution of originally hotfix assemblies,
with unchanged methods retaining AOT when safe and one Current serving several
archived Bases. This work must not merely suppress layout rejection: closed
arguments, parent substitution, physical fields/GC, inherited methods and
reflection must agree in the actual Windows Player.

Implementation sequence:

1. Compile a valid generic intermediate parent and probes with the real Unity
   2022 compiler. Wire the existing Processor's parent and constructor to a
   closed instantiation. Preserve original Current/Base inputs.
2. Record the preceding producer rejection and a passing CLR reference. Start
   with a value argument containing a reference, so an incorrect inherited
   layout is observable. Cover inherited generic fields/methods, virtual slots,
   property/event reflection, root/interface dispatch, GC and independent
   objects. Broader generic owners, argument replacement/removal and nested
   substitutions remain part of the full objective.
3. Represent parent instantiation structure explicitly in analysis; do not parse
   dnlib display names or reuse Current peers as missing Base peers. Preserve
   malformed arity, cycles, sealed/value/interface parent and external-root
   rejection. Binary MV identities stay unchanged unless proven necessary.
4. Test the candidate resource through existing immutable Base103 and older
   Players. Fix runtime code only where real execution or native tests establish
   a defect. Runtime changes require new capability binding and new Base builds;
   existing Players never acquire capabilities by rewriting their identity.
5. Commit the candidate before final source-bound tests. Re-run affected policy,
   resource and Player checks; retain every failed attempt as evidence.

Correctness and exact input identities are hard gates. No performance gain is
claimed here; no-op AOT routing must not regress. Native changes additionally
require real-header compile/CTest. Unity 2022 Windows is the only current target;
Unity 2021, Tuanjie and mobile remain outside this iteration. No CAT, formal
branch/tag or Installer change is authorized by this candidate experiment.

The starting matched candidate is HybridCLR `6180597`, IL2CPP `819f74c`, package
`ed4b7b5`, lab `c5a2541`. Work is isolated on
`research/dhe-generic-physical-parents-v8.13.0`. Rollback selects that preceding
combination and its archived compatible resources, then restarts the Player.

## AOT-parent Base continuation

Build a new Base from the qualified generic-parent Current using the existing
C# Base workflow and explicit producer DLL. Reset only its business revision to
59 as in previous archived Bases. Add an opt-in no-op probe to the source Player
fixture: execute the 29 generic-parent cases after resetting dispatch counters,
require unchanged parent methods, positive AOT entries and zero DHE interpreter
entries. Preserve the existing 25 virtual checks and all old Player bytes.
Then reuse the original Current on the new and historical Bases before testing
parent argument replacement/removal. This is still correctness/routing work,
not a throughput or ARM64 claim. Candidate sources must be committed before
building; generated project sources must not be edited to fix build failures.
