# DHE performance v1: Unity 2022 opt7 follow-up

Optimize production steady-state native/unchanged methods, virtual/interface
dispatch, ordinary allocation and reflection, and Current payload startup.
Keep HybridCLR/package 8.13.0 and Unity2022 IL2CPP v2022-8.11.0; Unity2021 and
Tuanjie are deferred by the user. Keep all Current/physical receiver, byref/FGS,
metadata publication, GC rooting and transactional failure semantics.

Primary metrics: production-equivalent steady-state throughput and allocation
counts; secondary metrics: Load + first entry, native/managed allocation, peak
and resident memory, P50/P95/P99 and concurrency. Correctness differential must
remain zero. No improvement from diagnostic builds is a production claim. P99
hard gates require >=100 independent unique process IDs; Android ARM64 requires
the matching APK and actual device evidence. Short desktop samples are exploratory.

Implement diagnostics as an explicit build feature disabled by default; omit
both counter operations and counter-only classification lookups in production.
Keep diagnostic availability queryable so smoke does not silently certify a
counter-disabled Player. Production correctness must compare observable results.

Use bounded per-thread caches with no allocation on hits, keyed by the acquire
loaded immutable DHE publication identity. Positive and negative cached mappings
must invalidate on publication. Never share mutable std::unordered_map reads
without their existing locks. Dispatch nodes and mapped metadata remain alive
for the process lifetime; first construction retains its metadata lock.

Give the supplemental-field registry its own release/acquire publication identity,
and remove ordinary-field locking with cached classification. Do not cache raw
managed sidecar objects outside their existing GC/lock lifetime protection.
Common invoke-args buffers use bounded stack storage and spill oversized frames.
Unify payload ownership/bookkeeping and avoid duplicate successful DLL clones;
do not remove defensive ownership copies or failure-state accounting.

Baseline: hybridclr a4807e563c0cb245519a44c6ea7cc633246dd337;
il2cpp_plus a1ec0324a8a58cb8e175c7b86665d70b5afae57e;
package bf62316bafa6e2c84dbb922faace3af06ee3f6ca;
lab 9c65c66. Candidate worktrees are isolated from formal repos and the SVN project.
Add meaningful publication/negative-cache, counters-on/off, oversized-buffer,
multithread, metadata and loading ownership regressions before implementation.
Run native real-header compile/CTest, managed package regressions and freshly
built Players. Benchmark matched workloads at clean commits, with diagnostics
off in both baseline/candidate where comparing dispatch optimization itself;
also record the original opt7 shipped diagnostic overhead separately.

Rollback each candidate by its own commit boundary and rebuild a matching Base;
published opt7 tags are immutable. Guard coverage and ABI guards remain required.
Runtime/Package identity changes need a new source lock and new Base validation;
the current SVN project and production release locks are not overwritten by a
candidate. Publish only after exact identity validation, following runtime-tag
then package then lab ordering. External production gates not passed must remain
explicitly pending, not relabeled as complete.
