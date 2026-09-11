# Native serialization trace, Unity 2022 Windows

This is diagnostic evidence, not a performance or release qualification.
Artifacts: `F:/hybridclr_artifacts/dhe-unity-serialization-trace`.

## First diagnostic Base

Base115 uses IL2CPP `fa267cf8292e069460030856c1afbf5271577443`, lab
`2675917`, HybridCLR `6180597`, package `ed4b7b5`, and the unchanged producer
`db4d17e` / host from the typed-bundle report. Real-header compile/CTest passed
at `native-gate-01/DHE-Unity2022/native-gate.json`. Base114 stopped before Unity
because the new lab worktree lacked its value-layout host dependency; that
dependency was compiled before starting the fresh Base115 output.

`noop-base115-trace-01` passes 36 checks with
`HYBRIDCLR_DHE_TRACE_UNITY_ASSETS=1`. `shared-two-base-01` preserves the existing
Current bytes and passes the standard resource workflow on Base115 and Base112.
`current-base115-bundle112-trace-01` fails after 6/42 asset checks.

The native and managed logs establish:

- Unity uses class_from_type and queries field offsets directly. The captured
  trace has no calls to the traced field-set wrappers.
- In Current, AssetBehaviour is the selected 88-byte class with ten fields;
  State is at offset 40 and Items at 48.
- Native class_from_type already returns the Current AssetState with three
  fields, 40-byte instance size and flags 0x00102101. The unmapped API was not
  returning the old class for this field; simply mapping it is not a supported
  explanation for this failure.
- In no-op, Unity enumerates AssetState's Number/Text fields and allocates it.
  In Current, it queries AssetState's type and flags but never enumerates its
  fields or calls object_new for that class.
- Managed tracing finds State and Items null immediately after loading the
  Prefab, before Instantiate. Node, Target and the deserialize callback survive.
  The same nulls remain after cloning.

The next diagnostic pass observes serialization eligibility predicates, class
attributes, generic/byref flags and type equality. Preserve this first trace;
do not claim a stale-offset or clone-only fix from it. No assertion was relaxed.

## Eligibility diagnostic Base116

IL2CPP `e82b67b`, lab `30f0461`, and `runtime-03` / `native-gate-03` passed
real-header compile/CTest and Base build checks. `noop-base116-trace-01` passes
36 asset checks. `shared-two-base-02` passes the unchanged Current on both Bases.
`current-base116-bundle112-trace-01` still fails after 6/42 checks.

AssetState's Serializable flags, generic=0, inflated=0, kind=18, byref=0 and
negative Delegate/AssetBehaviour/UnityEngine.Object inheritance tests match the
no-op control. Native field SerializeReference is false in both. No-op continues
from class_get_image into callback discovery and field traversal; Current stops
after class_get_image. That returns a hidden metadata image with the same name
but a different pointer from the registered Base image. Assembly.cpp already
assigns the hidden image's assembly to the public Base assembly. This motivates
the separate public-image candidate; it is not yet proof that the candidate fixes
serialization. The diagnostic runtime will not be shipped.
