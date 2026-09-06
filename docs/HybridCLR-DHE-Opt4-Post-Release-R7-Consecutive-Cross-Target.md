# HybridCLR DHE opt4 r7 consecutive cross-target update

## Scope

This run derives a third current payload from the r6 cross-target payload without
rebuilding any Base Player. It reuses the same nine active Bases and registry
revision `6`, and stages one resource release for both payload variants:

- `default`: StandaloneWindows64;
- `android`: Android ARM64.

The run is an exploratory lifecycle proof. It is not a channel promotion because
the Android Base Players still have no device correctness evidence.

## Identities and checks

- registry revision: `6` (reused);
- current assembly-set SHA-256: `b0866513f7578cff5af3f65378e0631c6b80523a72ab00ae8baf0ae9710cf19b`;
- payload variant-set SHA-256: `c4e83a3783a95ba8ad1e74aabdcf09fca55c4a75181b4033a3c75ee8b96e40fd`;
- resource release build: `passed=true`, `activeBaseCount=9`,
  `unsupportedChangeCount=0`, `registryDisposition=reused`;
- Base staging: `9/9 passed`, `9/9 baseMetaVersionUnchanged=true`;
- staging schema gate: passed.

For both `default` and `android`, only `HybridCLR.ManagedCasesAot.dll` changed
from r6. `HybridCLR.CrossAssemblyDerived.dll`, `HybridCLR.ManagedCases.dll`,
and `HybridCLR.MetadataStress.dll` remained byte-for-byte identical. The r7
payload therefore exercises another consecutive update against every historical
Base while preserving each Base's embedded MetaVersion tree.

## Qualification boundary

The result proves repeated current derivation, variant selection, compatibility
validation, and resource-only staging. It does not prove Android runtime
correctness, PSS/RSS, thermal or weak-core behavior, and it cannot substitute for
the missing iOS/macOS/Xcode/device lane. Run `android-device-smoke` for all three
Android Bases, create their `resource-player-evidence`, and rerun the aggregate
release gate before promotion.
