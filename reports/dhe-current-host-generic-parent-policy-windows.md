# Current host generic-parent policy: Unity 2022 Windows

The generic-parent policy fixture now builds against the current DHE source
because its project explicitly links both `ResourceExecutionPlan.cs` and
`DheValueLayoutImpact.cs`. The rebuilt host passed all ten policy checks for
the Unity 2022 original/evolved inputs, including closed-parent capability
negotiation, intermediate descendants, and conservative rejection when the
assembly context is absent.

This is an offline admission/capability result. It covers a newly interpreted
descendant reaching an existing closed generic parent. It does not qualify
changing an existing physical Base type to a generic parent, generic TypeSpec
storage evolution, or any Unity Player behavior beyond the separately recorded
generic-parent Player checkpoint.

## Identity and evidence

| Item | Value |
|---|---|
| Lab source | `1f4a148` (`research/dhe-cross-assembly-parents-v8.13.0`) |
| Package source | `841abfd46e122343717fe4115186b97a215b58df` |
| Fixture result | `C:/hybridclr_optimize/artifacts/dhe-cross-assembly-parents/generic-parent-currenthost-03/result/report.json` |
| Result SHA-256 | `8C0BED67E013E7BD5A59E72B3C316DE97ABAD080413BA4D66C8F6B80B8B031E4` |
| Host SHA-256 | `D815E7185345B3190651958CE73D3150CF046C90A7C04A16F11FE3E7F5FC8922` |

The result reports `passed=true` and ten named checks. The two earlier failed
build attempts are retained in their separate artifact directories and were
not overwritten.
