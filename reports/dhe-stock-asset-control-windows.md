# Stock IL2CPP old-bundle control

Unity 2022.3.62f3 Windows, lab `0423c9a5de0ea4ed281bfd6f47708faa6e01bdc2`.
Evidence: `F:/hybridclr_artifacts/dhe-asset-evolution/stock-control-01/result.json`.

The control has no DHE package or initialization and uses the Editor's stock
IL2CPP runtime. Its copied asset-only DLL preserves the Current asset declarations
and assembly name, with an empty Factory type solely for the shared test driver.
The original Current, bundles, Player files and runtime are unchanged after both
runs. Source, DLL, Editor, runtime and Player hashes are stored in the result.
Runtime hash (ordered relative-path/file-hash records):
`2033B06AB7CAF8141002ACB55E42597F205DCB416DA64A24F5518A1DCBB50714`.

| Same stock Player | Result |
|---|---|
| Base112 latest typed bundle | 42/42 checks pass |
| Base117 old typed bundle | Fails after 7/42, `prefab:nested-number` |

This reproduces the exact renamed-field failure without DHE, while validating
the control with latest resources. It does not prove every Unity version or
serialization pathway has the same behavior. FormerlySerializedAs("Number") is
present in the original Current and evolved Base's stripped assembly.

For the target DHE workflow, do not assume Player-side field-name migration merely
because the attribute is present. Qualify rebuilt/migrated resources alongside the
Current code and bind their version selection. This control is diagnostic evidence;
the old-resource migration goal itself remains unresolved.
