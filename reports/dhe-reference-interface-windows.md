# Existing reference interface additions: Unity 2022 Windows

## Result and blockers

This is an incomplete research candidate. The original reference-storage
checkpoint in `dhe-reference-storage-windows.md` passes its complete two-Base
regression. The new existing-type interface fixture passes nine direct/native
callback checks on Base-65 after cold selection, but cached cloning omits both
callbacks. Base-66 fails the pre-existing open-generic-value-copy business case
before callbacks run. The combined resource workflow is failed, not qualified.

The native interface-query correction is IL2CPP 6951d30. Real-header native04
passes. Its new uninstrumented Base-67 passes all nine existing-interface callback
checks both cold and cached, with 11 cached receiver checks, the full previous
reference/generic/serialization/lifecycle suites and public preparation/recovery.
This resolves the cached clone reproduction. The Base-66 conditional generic
failure remains a separate blocker; no new evolved-layout Base was rebuilt for
this query-only correction.
Do not publish runtime tags, package defaults or production claims from these
results. Tuanjie follows Windows correctness; no new Unity 2021 work.

## Source and artifact identities

All artifact paths below are relative to `D:/hybridclr_artifacts`.

| Component | Source |
| --- | --- |
| HybridCLR research/dhe-reference-storage-v8.13.0 | b69128f855f1704cbc3b8bc30620dc17cb996b07 |
| Base-65/66 IL2CPP research/dhe-reference-storage-unity2022-v8.13.0 | eaf006d4b929226a86f53a47eacc42f63b5d017e |
| Interface-query IL2CPP candidate, same branch | 6951d3052190ce3b62853434b4788d4b4780ccb4 |
| Package research/dhe-reference-storage-v8.13.0 | 102cfd72b06b479ce1da8632da0fe17d23f9f6d2 |
| Lab Base-65/66 build / tool / host-14 | e4183c968532c7b3a454bb51da41e691bed1f9f8 |
| Lab resource02, replay, native03 and managed checks | 53f21b46f13571490edfc0c59ec32f1532c14370 |
| Lab native04 and Base-67 build | 7de74049f1865170c0885af8219f18ef2140e382 |
| Lab resource03 and Base-67 replays | 7e7f0d50f265c5c69e6d27d27d70d4dd3f2f4b15 |

Package canonical tree is
`C69790B373A4014A079C313BF87AB96F11C3EBF0A9406CC9D62F83F4DB38C827`.
Host-14 SHA: `A6DC0923B8F03509F19FE40E88454DA82FC60B1D47A0B30642A3F4944225B071`.
Interface-tool-01 SHA: `C9BCABBF3A437FD7760D28E8C0E70C62CA80565B5A49CADABE23056840CFAB2C`.
The older default tool and host-08 remain intact for earlier evidence.
The new optional explicit tool path in the fixture build host avoids overwriting them.

Runtime01 manifest SHA:
`581C90741FA278F1A975A35C19AC4E833B23B2A8E739297DC421F09289EED885`.
Runtime02 manifest SHA:
`D0D13F9C11F3342FF8339A4F5F7B1A4E104217DDB0B31090254F3C5E0093C441`.
The runtime contract remains experimental dhe-runtime-v32; MV is DHEMETA1/schema 1.

| Immutable Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 65, original layout | 0f4ad2eb6be6e3c068120b6666f2f1023bfa24a668f5ff1e06efed9c9715373f | A6AC587841FD78D7A52524986540A53EEB2018303DFD5E4618A32DB5315877D4 |
| 66, evolved layout | d2c4bb7a193368052ca8e69745d41197ff94325a5e5acb104acf94f42d88e9e1 | CD99398CCCEA361D24120A06C03CFD6BEE55A69E9393AE038A8D8DF9368CED06 |
| 67, original layout with corrected native query | 780ba8a0dfc803df3bea897cd45294abd5fc80906bfe628fb7df63a1133157de | FB8DC47FDCC76B04839E6F8BFF3A4419D5EBC799ECD2FA64B45379C7567EF579 |

Both build, startup and generated no-op resources pass. Current02 lives in
`dhe-unity-serialization-callback-current-02/current`; its Model DLL SHA is
`4FE3BE882007F3601DCD9133A85B555CE331A281E8A81EFCE468A78137DA99B3`.
Resource02 manifest SHA:
`F1D281B330AE30326479BF2062AA75384BC4ECE39299D3423FD10D17E1D15B32`.
The same four Current DLLs are used for both Bases.

Base-67 also passes build/startup/no-op. Resource03 uses the exact unchanged
Current02 DLLs, Current-set SHA
`e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24`,
manifest SHA `2EACE54B39A658D9CB95DF3FCBEF47BB30583DA093E8EC91192ED7E2972F8C04`.

## Verified boundaries

Admission requires selected physical Current storage for a non-generic reference
class, additive direct non-generic interfaces and unchanged qualified parent,
attributes, packing, class size and generic declaration. Removal/replacement,
duplicate interfaces, inheritance and generic changes remain rejected. It also
requires physical-current-interface-additions-v1 in every target Base.
Candidate capability declaration is not release qualification.

- `dhe-reference-interface-policy-before-01`: intended failing-before result.
- `dhe-reference-interface-policy-after-01/02`: all 18 admission/negative checks pass.
- `dhe-reference-interface-mv-stability-01`: old and new tools emit identical MV
  bytes for Current02; SHA B3158153FF9F9BE6C7CBD7A689DD664B991DAABFDB3BB17B312C649CA60816A5.
- `dhe-native-callback-capability-rejected-01`: real resource workflow rejects
  immutable Base-63 for its missing capability, before Player execution.
- `dhe-reference-interface-native-03`: real-header compile/CTest passes with
  40 native method-selection signature combinations. Matching scalar/string/object
  frames select Current only for physical Current receivers or their descendants.
  Old/unrelated/null receivers, value/generic/pointer buffers, byrefs and mismatch
  cases retain the guarded route. This checks selection, not all Unity marshalling.
  Native02 preserves the preceding test-fixture compile failure.
- `dhe-reference-interface-managed-01.json`: all 106 package resource, plan,
  failure-state and retry checks pass at package 102cfd7; native calls are recorded
  by the host, not executed there.

`dhe-native-callback-resource-02` validates resource compatibility and the 46-case
CLR reference. Base-65 passes 46 Player business cases and rejection/restoration.
Base-66 fails at open-generic-value-copy, so no successful aggregate result or
independent aggregate audit exists. Explicit Base-65 replays use its valid generated
resource while preserving that workflow failure:

| Replay | Business | Cache safety | Existing-interface callbacks |
| --- | --- | --- | --- |
| dhe-native-callback-base-65-cold-01 | 46 pass | not requested | 9/9 |
| dhe-native-callback-base-65-cached-01 | 46 pass | 11/11 | 7/9 |

In the cached failure, direct interface invocation and native JSON before/after
callbacks pass. Clone before/after counters both remain zero. This differs from
the historical new-component control, which cannot qualify an existing-type change.

At the corrected runtime, `dhe-native-callback-resource-03` passes 46 business
cases and rejection/restoration on Base-67.
`dhe-native-callback-resource-audit-03.json` independently verifies one Base,
three successful/restored runs and 76 files; this is not a two-Base result.

| Base-67 replay | Result |
| --- | --- |
| dhe-native-callback-base-67-cold-01 | 9/9 existing-interface callbacks |
| dhe-native-callback-base-67-cached-01 | 11/11 receiver checks and 9/9 callbacks |
| dhe-reference-cache-base-67-01 | 11/11 receiver checks and 14/14 reference checks |
| dhe-reference-generic-cache-base-67-01 | 11/11 receiver checks and 18/18 generic checks |
| dhe-reference-full-base-67-01 | 11/11 serialization and 17/17 lifecycle |
| dhe-reference-full-cache-base-67-01 | 11/11 receivers, 11/11 serialization, 17/17 lifecycle |
| dhe-native-callback-public-probes-03 | 9/9 preparation failure/fresh-process recovery checks |

Cached native JSON before and after counts are each 1, with expected values 37/49.
Clone before and after counts are each 1, replacing the preserved 0/0 failure.
Full lifecycle includes selected dependent execution and the unaffected AOT sentinel.

## Remaining implementation

IL2CPP 6951d30 resolves selected reference descriptors in exported
il2cpp_class_is_subclass_of. Internal ancestry and physical receivers stay strict.
The prior trace observes the cached Base descriptor at this exact interface-query
boundary. The unchanged cached clone failure now passes on Base-67.

For Base-66, the plan marks FrozenResourceCases.Identity<T> as
inspect-generic-context with no concrete changed dependencies. Its stable ID,
MV version and token are unchanged; the generated AOT generic entry contains the
universal guard for token 100663330. An unaffected AOT caller enters that selected
generic guard and is rejected for the old frame. Evidence is in resource02,
`dhe-reference-generic-plan-diagnosis-01/base-66.json`, Current02 MV and the
preserved Base-66 generated GenericMethods.cpp.

Mutable hotfix plans need explicit conditional generic selections, with closed
arguments checked before choosing AOT or Current. Do not merely remove all
inspect-generic-context selections or infer safety from equal method hashes:
body, metadata and concrete cross-assembly dependencies must remain unconditional.
The existing frozen-AOT conditional path is a reference, not proof for mutable
hotfix sources. Also test changed generic bodies with otherwise unchanged concrete
arguments: they must interpret through a valid closed ABI, not opt back into AOT
or fail because all generic definitions were assigned incompatible frames.
Same-physical-layout cache expectations must also distinguish
interface-driven Current selection from an unselected evolved-layout control.

Broader interface removal/inheritance, old-object serialization, scene/Prefab
evolution, native argument marshalling, generic/value identity, performance and
memory remain required gates. No CAT source, formal branch/tag, remote or Installer
default was changed. Roll back experimental work only as matched source/Base/
resource sets; preserve all failed artifacts. Existing formal defaults remain the
deployment fallback.
