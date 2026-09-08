# FGS field-address repair: two-engine Windows checkpoint

## Scope and conclusion

The user's 2026-09-08 instruction retires Unity 2021 from new DHE implementation
and qualification. Active targets are Unity 2022 for the app and Tuanjie 2022 for
mini games. This supersedes the default three-engine requirement for this task;
old source and evidence remain historical. DHE applies only to originally
configured hot-update assemblies, not ordinary AOT dependencies.

Both active engines now pass real-header native compile/CTest, a fresh
generic-interface Base/no-op build, and consecutive/skipped resource replay.
This is a focused correctness checkpoint, not completion of general DHE,
multi-generation qualification, Release qualification, or device performance.

## Frozen identities

| Component | Candidate commit |
|---|---|
| HybridCLR | 0787aad2e3c62d071113d028d050836b0bc68b8d |
| Unity 2022 IL2CPP | 7fa10da3fc29b4a9a78bfa83e5d8bfba9cef32c2 |
| Tuanjie 2022 IL2CPP | 589dad732d34702f7d95432c4fc0ed75453a424f |
| Unity package | f80d4c3a56e57fc61114c922735614b84561a82a |
| Lab locks, package declarations and tool snapshot | 0ee45e0 |
| Replay source/config | eaa4435df866b92b39dd872846eca9f64e54cb88 |
| Unity 2022 scratch project | f359caf |
| Tuanjie 2022 scratch project | 0c19e77 |

Editors: Unity 2022.3.62f3 and Tuanjie 2022.3.62t12. Both build Windows x64
Players with OptimizeSize/FGS and no supplemental AOT metadata.
The native contract is dhe-runtime-v26; MV remains DHEMETA1 schema 1.

All paths below are relative to
C:/hybridclr_optimize/artifacts/dhe-evolution-20260908.

Tool snapshot: toolchain-field-bound, host: host-field-bound.
Package ID: e96604636f8be4f1f4a3df0691b4b0e1a7ac9254ec0d7f97d80d20b44599d389.
The snapshot is Exploratory, releaseReady=false.

| Engine | Base root | Base ID |
|---|---|---|
| Unity 2022 | base-field-generic-u22 | edf415faf8878f9635f697070ba707ce1d783c311b52d6fb7988dc43c587497f |
| Tuanjie 2022 | base-field-generic-tj | ca39966655eefc841faeb243e8f611d861060107a3b8e96b927fae61234ba899 |

## Repair and verification

The two codegen helpers now check a null instance before computing its field
address. The original FGS AOT ref-return case previously crashed on both
engines instead of throwing NullReferenceException. VM low-level offset
calculation, ABI and layout are unchanged. See
../docs/HybridCLR-DHE-Fgs-Field-Address-Null.md for diagnosis and the exact scope.

The first package revision missed the Base generator's duplicated capability
declarations. Offline regression caught the mismatch; the base-fn-generic-u22
and base-fn-generic-tj archives confirm that both Players rejected their
identity before executing cases. Package f80d4c3 corrects the generator.
Regression now checks both the contract and complete capability sets.

Native reports under native-field-bound/DHE-Unity2022 and
native-field-bound/DHE-Tuanjie2022 record passed=true, mergeReady=true and
surrogateExternalHeadersUsed=false. They compile the repaired codegen C++
translation unit using the actual Editor headers. The matching runtime
manifests are under runtime-field-bound.

Both fresh Base/no-op workflows and schema gates pass with zero changed
methods and zero interpreter entries. AOT entry counts are 1876 (Unity) and
1878 (Tuanjie). The same frozen generic-interface Base DLLs are used.

Replay config: manifests/dhe-field-address-generic-two-windows.json.
Report: replay-field-two/report.json.
SHA-256: BDA7F33CE665D6DE51C14B3824B4C629716AACEAEA7CD9590FA03B1480D23B61.

- Six independent process IDs: 49016, 29968, 27012, 27428, 15472, 34760.
- Every run: 61 executed evolution groups, 220 differential cases, zero differences.
- Both engines cover first, latest after first, and latest with first skipped.
- First leaves the 220 case entries unchanged. Latest and skipped runs each
  record 220 interpreted case-entry receipts.
- Integer, Reference and Value generic callers remain AOT in all runs, while
  their changed callees execute through the interpreter.
- Each generic Base reports 40 applicable legacy assertions and 8 explicitly
  inapplicable assertions because the old members are absent from this Base.
  These are not counted as executed successes; original/evolved Bases remain
  necessary for those scenarios.
- Independent rehash checks 56 unique files across original and replayed
  Players/identities; all match. See replay-field-two/independent-audit.json.
- Both resource releases' 16 DLL/MV files are byte-identical to the preceding
  resource-mixed-call-u21-first/latest payloads. Only current Base selection
  documents are newly generated.

Registry: registry-field-generic-two.json.
Resources: resource-field-two-first and resource-field-two-latest.

The 45 actual-payload capability checks pass at the clean ce201ab source
(fgs-null-capabilities-frozen); subsequent changes affect package declaration
binding and the replay configuration, not that analyzer implementation.
At clean 0ee45e0, field-bound-tool-regression.json passes the two-engine role,
schema and package contract/capability checks. Its overall passed=false is
retained: authenticated registry/release-ledger inputs were not supplied.
It is not a complete toolchain release qualification.

## Remaining work and rollback

Rebuild original and evolved Base generations on both active engines, then
replay the same payloads across all six Bases. The current two Bases share the
generic-interface generation; older runtime results cannot qualify the new one.
General class virtual/inheritance and value-layout evolution, Unity behavior,
concurrency/GC/ABI coverage, AOT retention/performance/memory, and the formal
mixed-call release-evidence validators still require work. In particular,
formal validators still requiring changedProbeChanged=true must be aligned
with authenticated unchanged-entry/interpreted-callee evidence.

Windows x64 evidence does not establish Android ARM64, iOS or mini-game
correctness/performance. The user will perform real-project Android validation.
Updates in this replay replace resources before a fresh process; they do not
replace already loaded assemblies in a running process.

All changes remain on candidate worktrees. Published maintenance branches,
runtime tags, package default selections and CAT integration were not changed.
Rollback uses a matching previously proven native/package/tool combination and
its compatible resources. Installed Players with the old native null helper
cannot obtain this repair from a resource payload.

Scratch inputs are recoverable in these stashes (all older stashes retained):

| Scratch | Stash | Contents |
|---|---|---|
| Unity 2022 | a11c86944022cbd152a23aa0889537944c4c466d | Failed identity-build DLLs and compiler option |
| Tuanjie 2022 | dbd527bcbe85531bc4bfa3166b40aa288dfc9d54 | Failed identity-build DLLs and compiler option |
| Unity 2022 | 299405025aa9cda39041f8f2098eccc31cb2c55a | Verified generic Base DLL inputs |
| Tuanjie 2022 | 1a16b987a4b0ccebbc77bf610b7feb6a9901f664 | Verified generic Base DLL inputs |

No related Player or Unity build remains active at this checkpoint.
