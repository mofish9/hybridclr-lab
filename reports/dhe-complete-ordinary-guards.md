# Complete ordinary AOT guard inventory

Target: Unity 2022 Windows Base construction, startup, and DHE resource loading
before the application enters hotfix business code. Tuanjie follows Windows
correctness. Ordinary AOT code remains frozen to its Base source bytes.

The existing native probe protects the Native fixture assembly and Nullable<T>
methods in corlib. A reusable workflow must derive guard requests from the
complete stripped Base input set, so a later Current layout change can adapt
its dependencies without knowing those changes at Base build time.

The package's C# `DheOrdinaryGuardInventory` takes the current stripped AOT root,
mutable assembly names and generated identity type, and writes complete method
requests for every ordinary assembly. Exclude only that generated identity type;
its fields are finalized during the build. It never changes the configured
hotfix set. `GuardOrdinaryAotMethods` connects generation to native finalization,
including each final build and Bee reapplication. Native finalization must
resolve every eligible request to all generated specializations or prove it has
no generated native entry. No project script or external process generates these
requests. The host MV compiler remains an independent protocol reference.

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
The package inventory helper and fixture invocation are independently
revertible. A completed inventory is not resource ABI admission by itself.

## Reproduced final-strip mismatch

Lab `526d778`, package `f1111dc`, runtime `42ecf89`, Unity 2022 IL2CPP `8a13baf`:
proof-13 generated 40 ordinary assembly requests (54,406 declarations, excluding
two generated identity methods). BuildScriptsOnly injected its guard set, but
BuildFinalPlayer failed on UnityEngine.CoreModule token `100664556`, which was
outside the final codegen module. The Prepare-stage inventory no longer matched
Unity's final stripped method table. No completed Player evidence exists for
this attempt. Its native runtime compile/CTest still passed in
`artifacts/dhe-complete-ordinary-guards-20260910/native-01/DHE-Unity2022`.

Generate requests from each actual final-build stripped set inside the package.
The earlier host command has been removed from the workflow. Compare package
requests with the independent MV compiler using both real script-only and final
stripped inputs, then rebuild a new Base. Existing immutable Players are never
patched to accommodate a changed token table.

## Final-strip correction and startup finding

Package `6859fb4`, lab `d791cd9`: the package inventory policy passes 13 checks
in `artifacts/dhe-complete-ordinary-guards-20260910/policy-01/result.json`.
Proof-14 then completes the final native build and full ordinary coverage:
40 assemblies, 49,084 executable method requests, zero missing entries. The
native manifest SHA is
`280CEACF76228273BA50CC8116872A1B65F98735152457D2210678A5CF4A9BCF`.

Base startup (PID 16168) spends over 211 CPU seconds before producing its
business result. Three live stack samples in `startup-stack-01.json` under the
same artifact root show SHA256Managed.RotateRight calling the generated guard,
then ResolveMethodByToken -> ResolveMethodInAssembly -> Image.GetTypes during
DheRuntime.Initialize. With no DHE publication, this metadata enumeration cannot
select an interpreter method and is unnecessary. The sampled Player was stopped
after preserving the diagnosis; no Player correctness pass is claimed.

The next fix gives generated AOT guards a published-state-only lookup. It returns
only changed/removed Base methods and never enumerates metadata or allocates
classes. Keep the original resolver for pre-publication metadata preparation.
Tests must cover empty/unregistered/unchanged states, changed and removed tokens,
failed publication and concurrent reads, requiring zero metadata enumerations.
The lookup uses the existing immutable release/acquire publication and introduces
no additional mutable cache. Rebuild a new Player to qualify startup and all
existing frozen-entry capabilities; never modify proof-14's native binaries.
