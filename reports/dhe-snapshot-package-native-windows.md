# Snapshot package: Unity 2022 native checkpoint

The candidate locks now select package `ed4b7b5` and its canonical source tree.
A fresh runtime assembly and native compile/CTest gate pass with real Unity
2022.3.62f3 headers, FGS enabled, `mergeReady=true` and
`surrogateExternalHeadersUsed=false`. This is a native qualification only;
the new package still needs its own Unity Player build and regression.

| Input | Identity |
|---|---|
| Lab lock/build source | `e0f25a36a7a6e7ee4ef90f3a30d3e75523efaa17` |
| Package | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Package canonical tree | `BC2009391C50730627001866C3C330E703D734A03338DFA456926EBDFF986FBD` |
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Runtime manifest SHA-256 | `76FFE0A91201E926E6AF8E3AF4637481F14F6D10F2DAED40654DE052A9CA07DF` |
| Native result SHA-256 | `A9CC7158D6683E1F681C9C649902318B202322A09DEF30D91EF1CBCAB1D8ED63` |
| Rebuilt AOT snapshot host SHA-256 | `30FB39071CD52F3BC06B1A721883BA144BFA401711C08627785221659481F31B` |

Artifacts under `C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents`:

- `runtime-03/DHE-Unity2022/runtime-manifest.json`
- `native-04/DHE-Unity2022/native-gate.json` and `native-test.log`
- `host-23/AotSnapshotTests.dll`
- `execution-plan-concurrency-tool-01/HybridCLR.DheTool.dll`

The value-layout project preparation helper was also rebuilt from this lab
source at `tool/fixtures/value-layout/bin/Release/net6.0/ValueLayoutTests.dll`.
Both .NET fixture builds had zero errors and the existing net6.0 lifecycle
warning. The native runtime tree is identical to the previous checkpoint;
this fresh gate binds the new manifest rather than relabeling its old report.

## Resume without repeating completed work

Native gate tool session 79889 and compression sessions 39021/29047 have completed.
No new Unity Player build was started during this checkpoint. The user then
requested GPT-6 Astra with High reasoning and prioritised C: cleanup. The app
accepted that setting for subsequent execution in this same task.

Next build a new Base with the existing `unity-workflow` command, current clean
lab/package trees, the new runtime manifest, and `base-input-102` as fixtures.
Use a fresh output such as `base-103` after checking that it does not exist.
The fixture revision is 59. Pass `:all-ordinary-guards:` and the explicit tool
path; the helper already knows the full Base build and resource no-op phases.
Then run the 25-check `virtual-signature-noop` gate and build a shared Current
resource for the new Base and an archived Base. Do not copy the old Player or
overwrite prior result directories.

Example argument order (absolute paths abbreviated only for readability):

```text
dotnet <host-23>/AotSnapshotTests.dll unity-workflow <lab> <package> <Unity.exe> <runtime-03>/DHE-Unity2022/runtime-manifest.json <base-input-102> <new-base-103> 59 :all-ordinary-guards: <tool>/HybridCLR.DheTool.dll
dotnet <host-23>/AotSnapshotTests.dll virtual-signature-noop <lab> <passed-base-103> <new-noop-output>
```

The complete target and outstanding Scene/Prefab, old-object, generic physical
parent, native publication, performance and memory work remain in
[current status](dhe-unity2022-current-status.md). No formal branch, runtime tag,
Installer default or CAT project was changed. This report is a later documentation
commit and does not change the source identities recorded above.

## C: space recovery

On 2026-09-11 the user requested C: cleanup. Automatic approval rejected both
the batch and narrowed `.obj`/`.pch` deletion attempts with only `blocked by
policy`; neither command executed. No files were deleted. Instead, Windows
lossless LZX compression was applied to the existing `project/Library/Bee`
directories of `dhe-parent-transitions/base-95` through `base-100` and
`dhe-cross-assembly-parents/base-102`, all under the workspace artifacts root.
All compression sessions completed successfully. This filesystem operation is
reversible and does not add a PowerShell dependency to the DHE workflow.

C: free space increased from approximately 3.84 GiB to 8.44 GiB, including the
space used by this turn's new runtime/native artifacts. Base102's preserved
Player and GameAssembly hashes still match its original proof. Logs, reports,
DLLs, generated C++ and debug symbols remain present. The CAT Editor was left
running; no CAT project content was modified.
