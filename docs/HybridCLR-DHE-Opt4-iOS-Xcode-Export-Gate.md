# HybridCLR DHE opt4 iOS Xcode export gate

## Scope

Commit `c108d87` adds an offline integrity gate for the iOS leg of the DHE
workflow. Unity iOS output is an Xcode project directory, so an ordinary
`BuildReport` is not enough to prove that the native finalization handoff is
complete.

The package validates and records:

- exactly one top-level `.xcodeproj`;
- its `project.pbxproj` file;
- the `Classes`, `Libraries`, and `Data` export directories;
- a canonical SHA-256 over the complete export tree.

The C# host rechecks the same path, tree hash, project count, required file, and
directories from `adapter/native-finalize.json` and
`adapter/build-final-player.json`. Any missing directory, changed byte, wrong
artifact kind, or Android native-library record in an iOS report fails closed.

## What this proves

It proves that the Unity export handed to the macOS build lane has the minimum
Xcode project structure expected by the DHE workflow and that the evidence is
bound to the exact exported directory. The regression includes both a complete
export and a missing-`Data` negative case.

## What remains conditional

No Windows result can stand in for macOS/Xcode evidence. A real iOS release still
needs the Unity iOS module, Xcode generation and compilation, native link, code
signing, IPA/archive output, device resource overlay smoke, correctness,
memory, and tail-latency measurements. Until those reports are produced, the
iOS status is conditionally qualified only for export integrity.
