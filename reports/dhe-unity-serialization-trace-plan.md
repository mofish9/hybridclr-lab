# Native Unity serialization diagnosis

Base113 fails after six asset checks with old or latest typed bundles, although
its no-op passes 36 checks and Base112/latest passes 42. Four type-query mappings
alone are insufficient. In particular class_from_type remains distinct from
class_from_il2cpp_type; the actual Unity call path is not yet observed.

Build an isolated diagnostic Base with opt-in native logs for only the
HybridCLR.Lab.UnityAssets namespace. Observe native type queries, field iteration,
flags/types/offsets and allocation. Preserve return values and existing mapping
semantics. Run the unchanged Current and bundle payloads with tracing enabled,
and compare no-op with Current. Record whether native deserialization sees Base
or Current classes and whether field offsets match allocated storage. Existing
failures, expected check sequences and Player hashes remain immutable.

Diagnostic overhead is not a performance result. No release, CAT, Android,
Unity 2021 or Tuanjie change. Compile/CTest with real Unity 2022 headers before
building the Windows Player. Native trace source is an independent IL2CPP worktree
based on 2ff64a8; remove that diagnostic candidate from production consideration.
Subsequent fixes must have a separate source identity and uninstrumented checks.
