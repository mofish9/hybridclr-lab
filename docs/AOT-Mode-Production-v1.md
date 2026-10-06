# AOT execution-mode selection production candidate v1

Scope fixed on 2026-10-06, following the v2 timing/isolation experiments.

The public contract remains one AOT startup selection and one query. Persistence,
network configuration and restart UX belong to the game. One compatible Current
resource set serves both modes. The process never rebinds after selection.

Production work: remove the experiment's hook counters, poison handlers and lab
internal calls; keep only startup/load checks required to prevent ambiguous Base
and Current identities. Use the pre-DHE SUPERSET implementation for traditional
supplemental AOT metadata. Retain shared interpreter correctness fixes when they
do not depend on DHE state, and validate those paths against managed semantics.

Acceptance: identical payloads and workloads; differential zero against CLR and
independent traditional Player where the engine permits comparison; no runtime
diagnostic instrumentation; static dependency and startup ordering contracts;
supplemental metadata, generic/interface/delegate/exception/reflection and Unity
dynamic component coverage. Repeated and concurrent selection must not rebind.

Performance compares an uninstrumented opt8 DHE Base with an uninstrumented
selectable Base, using the same Current/consumer/AOT metadata payloads and build
settings. Report load+entry and warmed execution separately, with checksums,
P50/P95, sample count and per-process identity. Use at least 100 independent
processes before treating P99 as a release gate. Do not claim zero indirect-call
cost. No Android memory or performance claim from Windows measurements.

The feature remains opt-in at Base build time. Disabled builds retain direct
extension calls. Rollback is the previous runtime/package combination and a Base
rebuild, or a project-owned next-start traditional-mode choice on capable Bases.
No upstream version upgrade. No release tags or formal branch merge until the
applicable correctness, real-header matrix and performance evidence is reviewed.

Unity2022 Windows is the first complete Player gate. Unity2021 and Tuanjie headers
are available for native compatibility; Android/WebGL/iOS Player evidence remains
separate. A result from one engine/profile does not qualify another.
