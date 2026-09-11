# Public assembly image: Windows asset regression

The old-layout Base117 now loads the unchanged Current and latest typed Prefab /
Scene bundles successfully: 42/42 checks. Previously the same inputs on Base113,
115 and 116 failed after six checks because nested State and Items were null.
The change is a native public image-identity correction, not a field-offset or
type-tree rewrite. The complete DHE workflow is still not release-qualified.

## Source and artifact identities

Unity 2022.3.62f3 Windows x64, FGS enabled. Root:
`F:/hybridclr_artifacts/dhe-public-image`.

| Component | Branch / commit |
|---|---|
| HybridCLR | `research/dhe-public-image-v8.13.0`, `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7` |
| Unity 2022 IL2CPP | `research/dhe-public-image-v8.13.0`, `ecad8a09d1eb9b91a57c59fcdc69b268377bad59` |
| Package | `research/dhe-parent-transitions-v8.13.0`, `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| Lab build/replay | `research/dhe-public-image-v8.13.0`, `6981233db8b87b516af1066adb3c5b7714d289a0` |

Producer remains `db4d17e`, binary SHA-256
`409FA61C806BB1095731D8EB0D9320DBE4D64D3B3F40CFF2D09AF21422EB82A4`.
Host SHA-256 is `BC9B9EC92E3D2256A0428F6CF4DCE3C24321665FD6A97282437D9A103B015B18`.
Base117 runtime manifest SHA-256:
`67A5DBCE7267AC48FA13C5F934D6CFFC1D15D8A736DAD3F210BFD2C9810E5A7E`.
GameAssembly SHA-256:
`F49D5BDFA2906AFC95E4F59B9A47C6478B443C15F52385D32F89B08C714A8434`.
These are uninstrumented native runtime sources, without the preceding trace patch.

## Verification

| Evidence under artifact root | Result |
|---|---|
| `native-gate-01/DHE-Unity2022/native-gate.json` | Real-header compile/CTest; mergeReady true; surrogateExternalHeadersUsed false |
| Native public-image checks | Null, unattached and unpublished images retain identity; published hidden image maps to public image without mutating ownership |
| `base-117/result.json` | Base phases, final snapshot and no-op resource workflow pass |
| `noop-base117-01` | Own old-layout bundle, 36/36 |
| `shared-two-base-01` | Same original Current bytes on Base117 and Base112, 46 business cases each |
| `current-base117-bundle112-01` | Old-layout Base117, latest Base112 bundle, 42/42 |
| `current-base112-bundle112-01` | New-layout Base112, identical Current/latest bundle, 42/42 |
| `current-base117-bundle117-01` | Own old-layout bundle: nested type restored; fails after 7/42 at renamed nested field |
| `regression-base117-01` | 46 business, 25 virtual, 18 framework, 29 generic-parent and 31 generic-owner checks pass |
| `public-probes-01` | Both Bases pass eight public lifecycle/failure runs |
| `shared-two-base-audit-01.json` | Original identities, exact Current bytes, two successful Base runs and 117 files verified |

The 42 asset checks cover authored fields, nested references, lists, SerializeReference,
Unity references, callbacks, added fields, clone storage, GC and independent bundle
scene loading. They do not prove every Unity serialization feature.

Base112 is the earlier control runtime (`6180597` / `819f74c`), not this candidate.
Its passing data cannot be attributed to the new native implementation. Earlier
failed Players and all diagnostic traces remain intact.

## Mechanism and remaining gates

Assembly.cpp already associates the hidden Current image with the public Base
assembly. Unity's public class_get_image query previously exposed the unregistered
hidden image. It now returns that assembly's registered image only after acquiring
completed DHE publication. Internal klass->image, field/attribute metadata, token
and offset values, allocations and receiver validation are unchanged. Ordinary
unregistered assemblies retain their existing image identity.

Old bundles still do not restore Number into RenamedNumber. Read-only dnlib
inspection confirms FormerlySerializedAs("Number") exists in both the original
Current DLL and Base112's ManagedStripped DLL. The no-op Base112/old-bundle failure
also remains a control; do not label this a proven Unity limitation without a
separate non-DHE comparison.

Next: qualify old-resource migration and newly added serialized types; then make
code-and-asset version selection part of the package workflow. The current fixture
supplies the bundle root separately from the shared code resource; a single
production delivery manifest/package has not yet been qualified. Built-in assets
without type trees, saved data, preselection objects, concurrent native use and
performance/memory still have their own outstanding gates. Windows results do not
establish Android ARM64 or iOS correctness. No formal branch, tag or Installer
default was updated.

Rollback for new candidate builds is HybridCLR `6180597` plus IL2CPP `2ff64a8` and
their matching lab lock. For an existing capable Base, select its compatible
archived code/assets and restart. A resource update cannot retrofit this native
fix into the old failing Base113/115/116 binaries.
