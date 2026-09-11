# DHE Unity 2022 Windows handoff checkpoint

The candidate workflow has now proven one Current delivery across three immutable
Base identities on Unity 2022.3.62f3 Windows x64.

## Locked source

- Package branch `research/dhe-asset-provenance-v8.13.0`, commit
  `c3d61a9d9c2997753685b5f3cb14a235614f46cb`.
- Lab branch `research/dhe-asset-provenance-v8.13.0`, commit
  `208d96cdba502e3c262222455160ab45288500a1`.
- Native runtime candidates remain HybridCLR `b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`
  and Unity 2022 IL2CPP `ecad8a09d1eb9b91a57c59fcdc69b268377bad59`.

## Evidence

`F:/hybridclr_artifacts/dhe-asset-provenance/three-base-delivery-01/result.json`
has SHA-256 `09C3C1A58CB85BBAC3A27CEB3752A9C8DF1101747687CB5CD44551D15ACD410C`
and `passed=true`. Its manifest SHA-256 is
`904F4624C27F502757B87561F90F70F4752F268131472AF08134FA7BBEDF8910`.
The three valid Player runs passed; negative missing-asset and wrong-manifest-hash
runs were rejected before native effects. The associated resource workflow result
is `F:/hybridclr_artifacts/dhe-asset-provenance/shared-three-base-01/result.json`
with `passed=true`, one Current payload, three distinct Base IDs and exact Current
byte preservation.

## Project trial sequence

1. Build a Base with the candidate package and matching Unity 2022 IL2CPP runtime;
   retain `build-identity.json`, native manifest, AOT snapshot and GameAssembly SHA.
2. Generate Current DLL/MV resources from that Base snapshot. Do not reuse a
   resource generated for another Base identity.
3. Author assets in a clean Editor project through `DheAssetBuild.Build`; pass its
   generated `asset-build.json` and the asset files to `DheDeliveryBuilder.Build`.
4. Publish the resulting immutable delivery directory and its manifest SHA through
   the project download/provider layer.
5. On startup, select the delivery by expected target/workflow and manifest SHA,
   call `DheRuntime.TryPrepareDelivery`, then load through the returned handle.
6. Repeat steps 1–5 for another Base and confirm the same delivery is accepted.

The project trial should first use Windows with the exact package/runtime identities
above, then Android. Android/iOS/ARM64 correctness, performance/memory gates,
download authentication, save-data migration and all Unity serialization forms are
outside this Windows checkpoint. No runtime tags or maintenance branches were
created; this remains a candidate pending those gates.

## Exploratory timing sample

Ten independent Windows processes loaded the three-Base delivery using the new
capability Base copy. All ten completed successfully at revision 73. Wall-clock
times were 9015–9057 ms (mean 9026.8 ms; exploratory maximum 9057 ms). These
times include process startup, Unity initialization and asset checks, rather than
isolated DHE load time; the sample is below the 100-process policy for a P99 gate.
Raw records are in `F:/hybridclr_artifacts/dhe-asset-provenance/timing-02`.

A follow-up 100-process attempt was intentionally not accepted as a P99 gate:
the sequential runner stopped after 55 completed samples when Windows reused a
previously exited PID. The partial records are in
`F:/hybridclr_artifacts/dhe-asset-provenance/timing-100-01`; this identifies a
runner design issue. A formal gate must use a concurrent process window or record
PID plus process creation identity while still enforcing the policy's unique-PID
requirement.

The corrected C# concurrent sampler then completed 100/100 independent Player
processes successfully. Unique process identities (PID plus creation time) were
100; Windows reused three numeric PIDs after earlier processes exited, so numeric
PID cardinality was 97. P50 was 16,574 ms, P95 17,026 ms, P99 18,769 ms, maximum
20,653 ms and mean 15,891.03 ms. Raw records are in
`F:/hybridclr_artifacts/dhe-asset-provenance/timing-concurrent-100b`.
This measures startup plus prepare/load/assets, not isolated method execution or
Android performance.

The final audit intentionally retried the older `shared-two-base-01` resource
with the package after the explicit public-image capability token was added. It
was rejected at `prepare-0` because that historical resource/Base identity does
not contain the new capability. This is expected stale-identity fail-closed
behavior, not a regression of the current three-Base delivery. Current evidence
must use `shared-three-base-01` and `three-base-delivery-01`.
