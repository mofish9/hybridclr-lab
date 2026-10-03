# opt6 review fixes

Fix two review findings from 2026-10-03: reflected interface calls on boxed
Current value receivers, and repeated unsynchronized MethodInfo interpreter-bit
writes after publication. Upstream remains HybridCLR/package 8.13.0 and Unity2022
IL2CPP v2022-8.11.0. Published opt6 tags are immutable.

Primary gate: zero CLR/Player differences for the original 47-case Current and
the new reflected interface/concrete-method, old-box rejection, and concurrent
first/repeated generic receiver probes. Retain complete ordinary guards, concrete
signature/byref checks, frozen Base IL, and exception behavior. Startup loading;
no migration of live objects. FGS/OptimizeSize, no supplemental AOT metadata.

Change the boxed/managed receiver helper; do not widen raw native unboxed ABI
entry. Prepare selected generic definition flags before the existing release
publication, restore snapshots on failed registration, and use acquire-loaded
interpData with no guard-time flag mutation. No new cache or lock order.

Validate native tests and real Editor headers, managed reference, and freshly
built Unity2022 Windows Players on committed sources. Unity2021 is out of scope
for this opt7 baseline. Defer the Tuanjie implementation until the Unity2022
baseline is accepted, then repeat the same source, native, Player and device
workflow. Do not extrapolate Windows to ARM64. No throughput, memory or P99
claim. These remain device/release gates. Roll back to the parent runtime
source and a Base built from the matching combination, or select an archived
compatible resource in a fresh process. Do not relabel old Player evidence as
this candidate.
