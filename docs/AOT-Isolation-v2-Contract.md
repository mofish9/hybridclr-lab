# AOT-selected hotfix isolation v2 candidate contract

2026-10-06. Continues the managed-entry experiment. AOT bootstrap selects once;
the package must reject static dependencies from ordinary AOT inputs to deferred
hotfix assemblies. Unity startup scripts/assets referring to deferred types are
a separate pre-selection boundary: unsafe configurations must fail closed.

Runtime boundary: DHE-specific hooks bind through one immutable table selected
before hotfix loading. DHE entries call existing implementations; traditional
entries preserve ordinary types/methods/fields and reject DHE-only operations.
The table is published release/acquire and never changed after selection. There
are no new mode conditionals in steady execution. Indirection is real overhead,
not a zero-cost claim. A DHE compilation without this feature keeps direct calls.

Primary gates: same Player, same Current/consumer; CLR and independent traditional
reference differential zero; previous DHE-hook poison no longer affects ordinary
interpretation; all configured DHE hook implementations remain unentered in that
mode; DHE retains dispatch; static AOT-to-hotfix references rejected at build;
invalid/repeated/concurrent/late selection and unsupported loads reject.

This work does not promise recovery from bugs in shared IL2CPP/GC/ordinary
interpreter code. Routing DHE extension hooks is not equivalent to swapping an
entire old IL2CPP binary. Any remaining shared semantic modifications must be
audited against opt3 and gated; passing one poison test is not a completeness proof.

Windows Unity2022 is the first Player gate. Three-engine headers/CTest, real
Android/WebGL/iOS and production asset/serialization/callback/GC/performance gates
remain required before release. Diagnostics are excluded from performance claims.
No upstream version upgrade, runtime release tag or formal maintenance merge.
Rollback: revert candidate commits as a unit and rebuild Base; do not reuse old
native/metadata artifacts with a new selector. Project owns remote config/storage.
