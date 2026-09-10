# Complete ordinary AOT guard inventory

Target: Unity 2022 Windows Base construction, startup, and DHE resource loading
before the application enters hotfix business code. Tuanjie follows Windows
correctness. Ordinary AOT code remains frozen to its Base source bytes.

The existing native probe protects the Native fixture assembly and Nullable<T>
methods in corlib. A reusable workflow must derive guard requests from the
complete stripped Base input set, so a later Current layout change can adapt
its dependencies without knowing those changes at Base build time.

Add a host C# `ordinary-guard-inventory` command. It takes the real stripped AOT
root, HybridCLR settings and generated identity type, and writes complete MV
method requests for every ordinary assembly. Exclude only that generated
identity type; its fields are finalized during the build. It never changes the
configured hotfix set. Native finalization must resolve every eligible request
to all generated specializations or prove it has no generated native entry.

First qualification uses the frozen-entry fixture with `all-ordinary-guards`.
Require complete native coverage, successful final Player construction and
startup, all existing direct/inline/reflection/nullable/generic/array probes,
and unchanged-method AOT behavior. Any startup metadata recursion, unsupported
ABI signature, shared native owner collision or lost source binding is a failure
to fix, not a reason to omit a source assembly from the inventory.

This correctness build contains dispatch counters and makes no performance
claim. Guard cost, metadata lookups, code size and memory need subsequent
production-equivalent measurement. The research switch only selects guard
coverage in the fixture; formal Installer defaults and CAT remain untouched.
The standalone inventory command and fixture invocation are independently
revertible. A completed inventory is not resource ABI admission by itself.
