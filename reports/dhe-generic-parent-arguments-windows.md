# Generic parent AOT Base and argument evolution (Unity 2022 Windows)

## Result and limits

Base104 now contains `Processor : GenericParent<Packet>` in AOT. Its generated
no-op resource preserves seven generic-parent methods and passes all 29 generic
checks with 3,410 AOT entries and zero DHE interpreter entries. The separate
25-check virtual suite preserves six methods, records 4,151 AOT entries and zero
interpreter entries. These counters establish routing, not performance gains.

One Current changes the parent argument to a new reference type:
`Processor : GenericParent<ReferencePacket>`. Packet is a value type containing
a reference; this changes the inherited generic storage representation. The
same unmodified Current passes on Base104, Base103 and Base102, preserving all
three original Player/GameAssembly binaries.

| Gate | Base104 | Base103 | Base102 |
|---|---:|---:|---:|
| Business reference sequence | 46/46 | 46/46 | 46/46 |
| Virtual signatures | 25/25 | 25/25 | 25/25 |
| Framework callbacks | 18/18 | 18/18 | 18/18 |
| Generic parent argument cases | 29/29 | 29/29 | 29/29 |
| Resource identity/tampering checks | pass | pass | pass |

The independent audit passes three Bases, six successful runs and 179 files.
Base104 also passes native preparation failure, fresh-process recovery and
Component lifecycle probes. The new Unity-compiled workload has a passing CLR
reference including all four case sequences above.

A separate unchanged archived Current restores direct inheritance from
ProcessorRoot and contains no GenericParent definition. Base104 and Base103
both pass the standard 46/25/18 suites and 15 parent-removal checks. These verify
the new parent, removal of inherited fields/property/event, virtual/root calls,
reflection construction and own-data GC. The observed sequence was separately
compared with the archived CLR reference: 15/15 on both Bases. Independent audit
passes two Bases, four successful runs and 125 files. This does not separately
prove that every reflective lookup/enumeration of the removed generic definition
is hidden; the reused removal probe names the earlier non-generic parent there.

Qualification remains conditional and restricted to Unity 2022.3.62f3 Windows.
Generic owners themselves remain rejected. Reference-to-value argument changes
from a reference-parent AOT Base, arbitrary nested/constraint evolution,
generic Scene/Prefab parents, retained pre-update objects/caches, native
publication stress, final unified regression and performance/memory remain
unfinished. Android/iOS and Tuanjie were not tested. No universal-hotfix or
production-release claim is made.

## Changes and exact identities

This round changes only the lab C# workflows and fixtures: explicit tool selection
for the existing Base builder; an opt-in generic-parent no-op Player probe;
and a Unity compiler define to reuse the generic tests with a reference argument.
The original Current payloads, old Players and prior reports are preserved.
No runtime/package implementation or Installer default changed.

| Component | Identity |
|---|---|
| Base104 build and no-op lab commit | `5b4a35c02c26fc8bfbb530f16b8da737b4cd36d1` |
| Argument workload and resource/replay lab commit | `2d4bb9c25b2d58d67a4d579c1f019f35ec0ccadf` |
| HybridCLR | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| Unity 2022 IL2CPP | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| Base104/Base103 package | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Base102 package | `841abfd46e122343717fe4115186b97a215b58df` |
| Base host (`host-08`) SHA-256 | `12F033BDFE170FF645AC49A0B9BF81B9C44665FF6CDA3DF990FD5FA3516860CA` |
| Argument host (`argument-host-01`) SHA-256 | `640517DDA5A53185E3C443C048FA954DAD5C153CFB9F2720B3929B8EB76A9205` |
| Tool (`tool-03`, built at `695fa72`) SHA-256 | `49FF5AD2D488D7ADD43CE9F2BC0127EC348919FE48C6374798A457EBEAE7A599` |
| Base104 ID | `46a1deb3786a4725aadf1d013baf3255c21a4ad106082fd60ea564534564e840` |
| Base104 GameAssembly SHA-256 | `3EB2E409401C0D028CD3F429F8DEB395A197B91E952892D43A3EB9F67E51FB8B` |
| Argument Current set SHA-256 | `1b5615000c29e3338abf5bb68934b62f91859e523291f57a09e630b7f634b216` |

Both source trees were clean during their respective tests. The documentation
commit containing this report is not a replacement for those execution identities.
Native compile/CTest evidence remains the previously recorded, unchanged-runtime
[snapshot-package gate](dhe-snapshot-package-native-windows.md), with real headers,
`mergeReady=true` and `surrogateExternalHeadersUsed=false`. It was not rerun here.

## Evidence

Root: `C:/hybridclr_optimize/artifacts/dhe-generic-physical-parents`.

| Result | SHA-256 |
|---|---|
| `base-104/result.json` | `D1626FC2EDBE8F280E0640717C3B14BFC505CCC47CB5174E46D3B1A8F23ECFFE` |
| `noop-base104-01/result.json` | `171E6B2EC927FF79E604A0FA9B1A1C59D703C4EFAF34D9F30DB9343042594A7A` |
| `reference-argument-clr-01/result.json` | `03EA4518F762D4A45E253AC0FA02194E2AF35204B0CC2C2D9F0B46FB88A3BDC4` |
| `reference-argument-three-base-01/result.json` | `DB923238DD406F66D5BF0033E3E8C87D140690B303D83F4C40FA45F9C6A429EB` |
| `reference-argument-three-base-audit-01.json` | `85FF225052F3565CFD1FF9CEFF33A429F089E65B19EB91C7ECC562808A1041C7` |
| `reference-argument-replay-base-104-shared-01/result.json` | `9ED1DA30043A7E7228A797709524B6ADF68939BF29049A4F187AFC2F118AA559` |
| `reference-argument-replay-base-103-shared-01/result.json` | `D5571FC1F44FF6EE462020C8822E9331126F5A46227872E3209FD67F37E3FF38` |
| `reference-argument-replay-base-102-shared-01/result.json` | `EB6A5064AD11031D967821DECE97FC4FAFA5AD595646A364341419899EC223DB` |
| `reference-argument-public-base104-01/result.json` | `9EF904D9452B4860CBB4CFFBC96958AECA976432AFD44CE6A2EDCB4CA19C3F43` |
| `generic-parent-removal-two-base-01/result.json` | `93FCBAF80EFB55C5FABA6BBAD5357E0F5AA6CE5722239731E1F88BC8B04ABDA6` |
| `generic-parent-removal-audit-01.json` | `F418D3FBE60961E179647FB05CF11E1848A62C6D7D6A6289CC276E95E2B7F7CF` |
| `generic-parent-removal-replay-base-104-01/result.json` | `6B80A6FA810CA3CEC9DD1A88D7838D38BCF96B88587EFDCE86D8F69D8E9B1B33` |
| `generic-parent-removal-replay-base-103-01/result.json` | `84D31E46D616940C2FCB1F1AD550083892F033AE6B14EF64008CD2E791805059` |

## Reproduction, rollback and continuation

Use `frozen-resource-new-base` with the previous proof Base103, original
`current-02/current`, explicit `tool-03` and a fresh output. Run
`virtual-signature-noop` with the extra `generic-parent` argument. Generate the
new argument Current with `generic-physical-parent-current` and its
`reference-argument` option. Run `generic-physical-parent-reference`, then
`frozen-resource-workflow` with ordered Base104/103/102 and that exact Current.
Follow with per-Base `generic-physical-parent-replay`, `frozen-resource-audit`
and Base104 `unity-public-probes` using `:current:`.

For removal use archived `dhe-cross-assembly-parents/current-removal-12/current`
with Base104/103, followed by `cross-parent-removal-replay` and the audit.
Its CLR oracle is the unchanged `removal-reference-base103-01/result.json` in
the cross-assembly artifact root. All outputs must be new directories.

The next implementation boundary is generic owners and broader generic graph
evolution; additionally strengthen deleted-generic-type lookup coverage before
claiming full generic deletion semantics. Rollback selects the preceding
compatible resource and restarts the Player. Revert the lab fixture commits or
select their parent to undo this test extension; never mutate old Base identities
or reset published native metadata in place. No formal branch/tag was published.

Base104's completed Bee cache was losslessly compressed after the build, saving
about 1 GiB without deleting artifacts. No tests or build sessions remain live
at this checkpoint. Both lab worktrees are clean after documentation commit;
no stash was created.
