# AOT bootstrap mode selection: validation contract

Question: can an ordinary managed AOT bootstrap run first, select DHE or genuine
traditional interpretation once, then load the same Current without native host
changes? This is a diagnostic candidate, not a production implementation.

The minimal mechanism under test delays publication/name lookup of the fixture's
Base hotfix assembly until AOT chooses DHE. Legacy selection leaves Base hidden
and loads Current with ordinary Assembly.Load. Static generated metadata remains
intact on purpose: counterexamples must reveal whether publication alone is enough.
This experiment must not be reported as full deferred module activation.

Acceptance dimensions are separate: (1) managed AOT callback can choose once;
(2) DHE retains changed-interpreter/unchanged-AOT behavior; (3) ordinary Current
load matches a non-DHE Player and CLR reference; (4) name/type identity and a second
interpreted assembly referencing Current agree; (5) pre-selection generated AOT
references cannot escape isolation; (6) legacy execution does not depend on DHE
hooks. A deliberate failpoint in a DHE-specific hook tests (6). Failure of (5)/(6)
means the small mechanism is insufficient, not that all possible module designs
are impossible. Native hooks/failpoints are diagnostic-only and make performance
measurements invalid. No throughput, P99, memory or platform release claim.

Fixture methods cover added type/method, changed body, generic, delegate, value
type, interface, exception and cross-assembly reference. Ordinary bootstrap has
no direct hotfix dependency except a separate explicitly invoked negative-control
method. Unity preloaded objects, serialized hotfix types and multiple scenes are
not qualified by the empty-scene fixture. No supplemental AOT metadata is used.

Windows Unity 2022.3.62f3 is the only available Player gate in this experiment.
Unity 2021/Tuanjie and Android/WebGL/iOS cannot inherit a pass. Upstream stays
HybridCLR/package 8.13.0 and IL2CPP v2022-8.11.0. Isolated research commits are
reverted by dropping these worktrees; no formal runtime tag/package lock changes.
The normal shipped runtime has no dependency on the diagnostic selectors/counters.
