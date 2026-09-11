# Actual hotfix type deletion: Unity 2022 Windows

The same Current DLLs now pass all 22 deletion checks on immutable Base-99 and
Base-100, together with the complete 18 framework, 25 virtual-signature and 46
business sequences. The existing-parent Base actually loses six type definitions
and 47 methods; the root-only Base has no corresponding removals. Both consume
one identical resource payload with their own execution plans.

This conditionally qualifies these cold type-deletion scenarios on Unity
2022.3.62f3 Windows. It does not qualify deletion with pre-load cached handles or
objects, arbitrary ordinary AOT references to deleted types, cross-assembly or
generic parent evolution, full Scene/Prefab/startup behavior, publication stress,
performance or memory. Full DHE and project handoff remain open.

## Sources and immutable inputs

No runtime/package change was needed for this checkpoint. HybridCLR remains
6180597d2c0e455ab09fe0920d34d6dea5ad00fc, Unity 2022 IL2CPP remains
819f74c08e466a0d2a8fe5b1afaad5b1d784e482 and package remains
841abfd46e122343717fe4115186b97a215b58df. Their exact real-header native03 gate
is recorded in dhe-parent-transitions-windows.md and is reused, not relabelled.

Lab branch: research/dhe-type-deletion-v8.13.0.

| Lab commit | Evidence role |
| --- | --- |
| cf763b1c1feffa84548b7d359d31b2dca536f9a6 | Design, compiler/fixture wiring, host01 and original Current |
| 425112dde50ffcea74134e536a9b970a8291cc2a | Host02, corrected CLR reference, single/two-Base resource launch |
| 619c42442f51abfbc237ed1c14f6d4758e20be56 | Host03 deletion-plan audit; checkout identity for final replays/recovery |

Final replays and public recovery use unchanged host02 built at 425112d; their
recorded checkout is 619c424. Host03 is used only for the independent deletion
audit. Later documentation does not change those binary/source identities.
The standard resource tool is parent-transitions/tool-02, built at lab85949a2;
SHA-256 7B50FBFDE8AFD5A1BF2E007F0D22D0D5D1BCC4C6C2E1187E0B4CB61E26D5DA61.
Host02 SHA-256:
B5733F1B016B38A4E72372DDB3ADF7DFFB164AB5FFEBBC33213CEBB6EB63DE70.
Host03 SHA-256:
82142345C74D3C25D05B536197B973506CBB67E49F7917E83263AEF80EFC0AE5.

Base-99 and Base-100 identities/build sources are recorded in the parent
transitions report. Base-99 originally contains ProcessorMiddle and the larger
layout. Base-100 originally contains Processor directly inheriting Root and the
smaller Packet layout. No original Player, Current DLL or ordinary AOT input was
edited. Base updates and resource recovery run in fresh processes.

## Current construction and preserved reference failure

Paths below are relative to C:/hybridclr_optimize/artifacts/dhe-type-deletion.
current-01 starts from the unchanged extended insertion Current at
../dhe-parent-evolution/current-02/current. The actual Unity compiler builds the
new assertions. The recorded fixture wiring retargets Processor's parent and
constructor call to Root, removes the optional insertion probe call, and removes
the ParentEvolution top-level definitions and their nested types. It then appends
the optional deletion probe. The compiler/merged output is preserved separately.
All 46 business cases and framework/virtual probes remain intact.

ValidateCurrent verifies that both the removed definitions and references are
absent from the final DLL and that the child directly inherits Root. This is
actual metadata deletion, not merely removing Middle from the ancestry while
keeping its declaration.

reference-01 fails three whole-assembly enumeration assertions because the CLR
host lacks UnityEngine.CoreModule for the existing MonoBehaviour declarations.
It passes the other 19 assertions and all prior sequences. Keep that failure.
Host02 loads the actual Unity CoreModule definitions, records their hash and
passes all 22 assertions in reference-02 using the exact same Current DLLs.
The CLR oracle does not execute Unity native functionality.

Current-set SHA-256:
f04cf6409563132b471b4121b9a10dfa58d062acb547c826167113b5b019bba8.
Model DLL SHA-256:
9CC0993C5FAC2555BA9CA05488070CEBC75B56F2CA527E76690D93CCC9439ABD.

## Windows execution and independent audits

- shared-base99-01 and probe-base99-01 establish initial actual deletion on the
  original existing-parent Player, through standard resource generation/loading.
- shared-two-base-01 uses identical Current DLLs on Bases99/100. Its 13 workflow
  checks pass. Result SHA-256:
  42931CFEF370F69449339B644135F6EFB67A2FB74F241E0A968D617E717734CB.
- probe-base99-02 and probe-base100-02 each pass the exact 22/18/25/46 sequences.
  They check throwing/nonthrowing/case-insensitive type lookup, GetTypes,
  GetExportedTypes, DefinedTypes, companion-type absence, retained ancestry,
  member absence, field layout, reflection construction/invocation, virtual,
  interface/generic calls, GC retention and independent objects.
- shared-two-base-audit-01.json.resource.json verifies 121 files and three
  successful business executions. The companion deletion-plan audit compares
  original Base DLL definitions and stable method identities with Current and
  the published validation plan. Base99 removes six ParentEvolution definitions,
  47 methods and requires removed-types-v1. Base100 removes zero types/methods
  and does not require that capability. Deletion audit SHA-256:
  802767C72B48628AEC5717989BDBC8BC7E991D5A3A5AA6ED1409A54ED2C5D83C.
- shared-public-probes-01 passes all 16 native preparation failure/fresh-process
  recovery checks and binds 96 files. Result SHA-256:
  0B27A71F219E8D9F4DF549DD98BF0B4D7C71D283538A3F832B711AEBF5A74CF4.

## Reproduction and remaining work

Use the matching lab checkout, tool-02 and the stated host binaries. Commands:

```text
dotnet <host02> type-deletion-reference <original Current> <original Native DLL> <new output> <Unity CoreModule DLL>
dotnet <host02> frozen-resource-workflow <lab> <Base99>,<Base100> <tool02> <new shared output> <original Current> <settings>
dotnet <host02> type-deletion-replay <lab> <tool02> <Base proof> <shared output> <new replay output>
dotnet <host03> type-deletion-audit <shared output> <original Current> <new audit.json>
dotnet <host02> unity-public-probes <lab> <Base99>,<Base100> <shared output> :current: <new output>
```

Source rollback uses the preceding matched runtime/package/tool combination.
Resource rollback selects a compatible archived resource/no-op and restarts.
No CAT, formal branch/tag/push, Installer-default, Unity2021, Tuanjie or mobile
changes occurred. Candidate sources are committed; no stash or evidence deletion.
Continue with cached deletion and the broader parent/Unity boundaries rather than
treating these 22 cold assertions as proof of full DHE completion.
