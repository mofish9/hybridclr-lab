# HybridCLR DHE Android resource overlay

## Problem

An Android Base is an immutable APK. Its `assets/HybridCLRLab/DheDemo/BaseMetaVersion`
files and `build-identity.json` are part of that APK and cannot be overwritten by
a later resource package. A resource-only DHE update therefore needs two roots:

- the immutable APK, which supplies the installed Base identity and Base
  MetaVersion set; and
- an external application-files directory, which supplies the downloaded
  current DLL/MV payload, runtime plan, manifest, validation, and release ledger.

The current payload is shared by all compatible Bases. The runtime compares it
against the MetaVersion set read from the installed APK, so each Base can report
a different changed-method count while consuming one resource revision.

## Staging contract

`stage-resource-update` accepts `-BasePlayerApk` for an Android Base. It reads
the APK as a ZIP and requires:

1. exactly one `assets/<runtime-parent>/build-identity.json` whose bytes equal
   `-BaseBuildIdentity`;
2. exactly one complete `assets/<baseMetaVersionAssetRoot>` MV set whose bytes,
   names, assembly identities, and set hash equal that BuildIdentity and the
   selected registry entry; and
3. every current DLL/MV, runtime plan, manifest, compatibility validation, and
   release ledger to be copied below the writable `-AssetRoot` only.

The APK is automatically included in `immutableFiles` with its SHA-256 before
and after staging. The report records `embeddedBaseSourceKind=android-apk`,
the APK path/hash, ZIP entry root, selected Base ID, and unchanged Base
MetaVersion tree. A mismatch fails before any release evidence can be produced.

The old directory mode remains available for Windows and macOS/iOS filesystem
staging. A project adapter must choose the mode matching its real packaging;
copying an APK's entries into a temporary directory is evidence preparation,
not permission to mutate the APK.

## Device gate

`android-device-smoke` is a C# host command. It requires exactly one ready ADB
device unless `-DeviceSerial` selects one. It installs the exact APK, clears only
the selected application data, pushes every staged file below
`/sdcard/Android/data/<applicationId>/files/`, pulls each file back and compares
SHA-256, then launches the Player with `-labDheAssetRoot`.

The demo provider treats that argument as a strict overlay. The external
manifest, runtime plan, and payload files cannot fall back to APK contents when
the overlay is selected; missing files fail closed. `BaseMetaVersion` remains
APK-only. The command captures a unique application PID, waits for the Player
result, pulls logcat, requires process exit, and writes
`dhe-device-player-run.json` only after all bindings pass. That report binds:

- device serial/state/model/API/ABI and ADB identity;
- APK path and SHA-256;
- Base BuildIdentity and selected Base ID;
- staged asset set and round-trip hashes;
- resource manifest, release ledger, stage report and current assembly-set
  hashes; and
- the actual Android Player result and logcat.

`resource-player-evidence` requires this report for `target=Android` and
rechecks every binding. An Android Player result copied from a desktop run or
hand-authored without this report cannot enter the aggregate release gate.

## Multiple Bases and releases

For each active Android Base, repeat APK-aware staging and device smoke against
that Base's own APK/archive. The resource release itself is built once from the
complete registry and current payload variants. All device reports must select
their registry Base IDs exactly once and must agree on the release ledger,
manifest, variant set, and current assembly set. Promotion remains the same
filesystem-CAS operation used by desktop evidence.

If a new Android app version is shipped, archive its new APK and append its
BuildIdentity as a new registry Base. It must pass its own device smoke before
the next resource revision is promoted. Existing APKs remain online and keep
their original Base MetaVersion sets.

## Current evidence status

The candidate workflow has been compiled with zero warnings, and a real Unity
2022 ARM64 APK was staged successfully without changing its bytes. A release
stage was also produced and the no-device path rejected itself without writing
passing device evidence. This does not constitute Android ARM64 device
correctness, PSS/RSS, thermal, weak-core, or production-network evidence:
the current machine has no connected device or AVD. Those gates must run on the
actual target devices before a toolchain or game release is called mobile-ready.
