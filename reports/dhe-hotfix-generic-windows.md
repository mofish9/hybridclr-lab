# Hotfix generic execution: Unity 2022 Windows

The allocation and typed-reference follow-up is recorded separately in
`dhe-reference-owner-windows.md`. Its Base-70 results do not change the historical
Base-68/69 identities or preserved failures below.

## Result and remaining failures

The generic dispatch candidate repairs the preserved Base-66 failure. At runtime
971bba2, immutable Base-68/69 consume the exact callback Current02 DLLs and each
passes all 46 business cases. A second shared resource changes Identity<T>'s body;
both Bases pass the same 46 cases and the exact four-call Current-body oracle.
On Base-69, a separate unchanged-method probe proves that the existing
Identity<Payload> and Identity<Envelope<Payload>> retain AOT with zero changed
interpreter entries. This is an incomplete research candidate, not a release.

The wider callback regression passes on Base-68, but Base-69 exposes two further
boundaries. Four custom generic-owner type/reflection checks fail, cold and
cached. Full lifecycle also fails: its old oracle assumes unchanged Delta means
unchanged dispatch, and logs additionally contain a coroutine Base-frame guard
exception. The two-Base public preparation/recovery aggregate is therefore
failed. Do not replace these failures with the successful 46-case resource result.

The allocation follow-up e3d6604 reapplies selected physical storage when a
constructor carries raw Current metadata for an unselected owner. Lab 9c71e01
replaces the Delta-based lifecycle oracle with bound plan/method-version
expectations. These follow-ups require separate native/Player verification;
Base-68/69 results below do not qualify their new source identity.

Unity 2022 Windows remains first. No new Unity 2021 work; Tuanjie follows Windows
stability. Android/iOS, performance, memory and production qualification remain
outstanding.

## Source and artifact identities

All artifact paths below are relative to `D:/hybridclr_artifacts`.

| Component | Commit used by Base-68/69 |
| --- | --- |
| HybridCLR, research/dhe-generic-context-v8.13.0 | 971bba2e4d06882d9d10532736f0771689b587a7 |
| IL2CPP, research/dhe-reference-storage-unity2022-v8.13.0 | 6951d3052190ce3b62853434b4788d4b4780ccb4 |
| Package, research/dhe-generic-context-v8.13.0 | fc1ba89770479dbb47f23af9c9baa20fffebf7e6 |
| Lab, Base builds and first two resource workflows | c5ae123ef50c4b42a5a94cc502b432dbef20b26d |
| Lab, dispatch fixture/resource and adjacent replay launch | 71a37b8db040b4abb4886b614aeb6fd05b540af2 |
| Lab, existing-method dispatch replay | 675865b |

Runtime02 manifest SHA:
`5B5FD94353F65A7F3A89F29FEC43E0E478B5E420AE35787E94EBC5842CCDEF73`.
HybridCLR canonical tree:
`5F05F3DD4A65A9F726ECA87F94629B6C94EFF920EB465AE8765F425A1395D99C`.
IL2CPP canonical tree:
`E02D1B4588E3305D2B97C9A57814AE0A1E8FBA56C14ED90DB2C909A04DA48FA6`.
Package canonical tree:
`7F618558A8D46AEC8C94A9B4D6393960627D989800EB4BDDDB2CBDC8C525C359`,
with the ignored meta path in the package lock. Runtime contract remains the
experimental `dhe-runtime-v32`; MV remains DHEMETA1/schema 1.

| Immutable Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 68, original layout | 08ead1690df7dc499c32fd86473aa316fb96f8438941a3f84a4cadd3d4b3b775 | 6CB7B7D74EC858933BD0E7F7291F94F838C8CAF7F39BCB763AE5F46B7857619E |
| 69, evolved layout | f419086f26f0412c0b57f318eeada50f427b24a72b0947dff64e60fb038a53d1 | D2B00C5257B88FF9044692061227EA4B1E93EB440BCFD9E5DC79BC0FECA3A609 |

Both Base build/startup/no-op workflows pass. Their binary hashes are rechecked by
each resource/replay; no Player binary was replaced.

`dhe-hotfix-generic-tool-01` was built at lab dd1f041; its code is unchanged by the
later fixture commits. Tool SHA:
`A82C38F6E327FA13A069C4DF5650711F04EDDC9FE0006A0077DE61B94DE1A8D9`.
Host-03 was built at c5ae123:
`A21ABC18199A725DD9A16AF4DD55CD520F4E7855BADC877CCD60F10F56EE12A1`.
Host-04 at 71a37b8:
`23A97A3BDE2075A5B3342554201497AC62253917BA9FBB8705CC871623A4FDD1`.
Host-05 at 675865b:
`104E8827380858C86ADB2C722B07F22512CEC04209D2AFA03FC446B82BB0C5D7`.
Replay result labHead records launch-time checkout, while hostSha256 identifies
the actual earlier compiled host; neither rebases historical evidence.

## Verified tests

The first two preserved native failures establish missing mutable conditional
registration and missing closed generic Base-frame admission. Native02 passes
real Unity 2022 header compilation/CTest at lab 09dc359, with mergeReady=true and
surrogateExternalHeadersUsed=false. Native tests include token reorder/collision,
changed-body rejection of conditional admission, valid scalar closed frames,
missing/open contexts and previous native receiver checks. Plan01 passes 17
checks, including a concrete cross-assembly dependency despite equal local MV
hashes. Managed02 passes 113 package validation/argument-forwarding checks; these
record native calls rather than execute them.

All three resources below pass 16 workflow checks and share identical Current
DLLs across both Bases. Each independent audit checks two Bases, 46 cases, four
successful/restored runs and 126 files.

| Resource output | Current-set SHA-256 | Manifest SHA-256 |
| --- | --- | --- |
| dhe-hotfix-generic-resource-01 | e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24 | 969125DE1FD00C69349544F7BC0C8440F3F45531642C396F01856FD9DE55CD77 |
| dhe-hotfix-generic-body-resource-01 | 1e58232fb8f0e723071e8b2cb959935148c7e416712f706c85ea80bd90047cb0 | 1D007AED0899AA7915A3F5A877175FD577BF5E9C09E1A3B387881F095E559D09 |
| dhe-hotfix-generic-dispatch-resource-01 | 339bec559c47d90bd2293f44169d07ed32a0bb65098feb84378aef869a24e17c | BF6C3BF344E523E8C97A913BB642A20B5CC8A58822F4490E6392DC84673048E3 |

Audit filenames are the resource names with `resource-audit-01.json` replacing
`resource-01`. The first resource retains callback Current02 Model SHA
`4FE3BE882007F3601DCD9133A85B555CE331A281E8A81EFCE468A78137DA99B3`.

Changed-body Current02 requires exactly four calls: two direct value copies,
one reflection invocation and one delegate invocation. Every successful Player
log contains `DHE changed generic body pass: 4`. Current01 is preserved as a
failed test fixture: its expected count of three omitted the existing reflection
call. Its failed CLR reference is not a runtime defect or a passing result.

`dhe-hotfix-generic-dispatch-replay-01` passes 13 checks on Base-69. Its unchanged
Identity method exists in that Base and is explicitly conditional in the plan.
Payload and Envelope each report selected=False, interpreter=0; AOT totals are
30 and 37. AOT totals include reflection wrappers, so this is execution-path
evidence, not timing. Base-68 introduces this helper as new Current code and
cannot supply retained-AOT evidence for it.

## Wider Unity regression and preserved failures

Replay directories use `dhe-hotfix-generic-base-<base>-<mode>-01`.

| Mode | Base-68 | Base-69 |
| --- | --- | --- |
| reference-callbacks | 9/9 | 9/9 |
| reference-callbacks-cached | cache 11/11, callbacks 9/9 | cache 11/11, callbacks 9/9 |
| reference-cached | cache 11/11, reference 14/14 | cache 11/11, reference 14/14 |
| reference-generic-cached | cache 11/11, generic 18/18 | cache 11/11, generic 14/18: failed |
| reference-generic (cold) | not rerun | generic 14/18: failed |
| full-unity | serialization 11/11, lifecycle 17/17 | serialization passes; lifecycle fails |
| full-unity-cached | cache 11/11, serialization 11/11, lifecycle 17/17 | cache/serialization pass; lifecycle fails |

The four Base-69 generic failures are owner type identity, instance acceptance,
reflected field roundtrip and reflected construction. List, array, nested-list,
variance, concurrent identity and fixture-state controls all pass. The resource
selects EvolvingBehaviour storage but does not select GenericOwner's definition.
The Current constructor metadata and the public generic type disagree on that
definition, motivating the allocation follow-up described above.

`dhe-hotfix-generic-public-probes-01` preserves a failed aggregate. Both Bases
pass their original Unity control and the full expected native preparation-fault
sequence without business effects. Base-68 fresh-process recovery passes.
Base-69 fresh-process recovery reaches revision 73 but fails lifecycle validation.
Its log also records the coroutine exception; the old Delta-based selection
assertion is not the only issue to investigate.

## Next gates and rollback

Use the new allocation source and a new immutable evolved Base for the exact
failing Current02 bytes, then rerun generic reflection, callbacks, lifecycle and
public recovery. Host-06 at lab 9c71e01 calculates Awake and ReadUnchanged
selection from the bound Current DLL, Base MV and execution plan. New Players
require those explicit lifecycle expectations, and replay checks require their
log marker. Do not use this new host to relabel old lifecycle evidence.

Broader instance generic frames, changed byref/native buffers, interface removal,
inheritance, old-object serialization/migration, scene/Prefab changes, performance
and memory remain open. No CAT source, formal branch/tag, remote publication or
Installer default changed. No files were deleted. Large artifacts remain on D;
C had about 11.1 GiB free before the allocation follow-up build.

Revert experimental changes only as a matched source/lock/Base/resource set.
Base-68/69 retain the documented failures; they are not a production rollback
target. Existing formal defaults remain the deployment fallback.
