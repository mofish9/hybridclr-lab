# Virtual signature capabilities through the Windows package workflow

## Result

The new package inventory and resource compiler now complete the actual Base
build, no-op, shared resource, invocation and recovery workflow on Unity
2022.3.62f3 Windows. Old-layout Base-89 and grown-layout Base-90 use the updated
package; preserved Base-81 remains a compatible new-type control with its older
runtime and original capability inventory. All three consume the exact Current
DLL set from the preceding virtual-signature checkpoint.

This closes that checkpoint's package-admission follow-up. It does not complete
full DHE, qualify Android/iOS, or publish a formal optimization release.

## Source identity

| Component | Source |
| --- | --- |
| HybridCLR | fdf1299b44cf5b4d5a6afe41dc449fb89cb888c9 |
| Unity 2022 IL2CPP | 816778af772b67db5080d1569a27e41fbbea7452 |
| Package | a96d3b9734fdbf51ebec8106d3935e3e32e83c1a |
| Lab for Base-89/90 builds | c9c95f0361821b83579b2106ec67312c7de0ace6 |
| Lab for corrected policy/tool sources | e5041df, followed by documentation-only 4c5b004 |

Lab and package use research/dhe-virtual-admission-v8.13.0. Native sources remain
on research/dhe-virtual-signatures-v8.13.0. Formal branches, runtime tags, remotes,
Installer defaults and CAT were not changed. MV is still DHEMETA1/schema 1;
runtime contract remains dhe-runtime-v32.

Package canonical source hash:
79D55B5C9A51F264B289E5C175D8EDDE0D1B87CE95E4A655458342CD9B6CB4A6.
The package/runtime/repository locks select the matching commits. The package
hash uses the existing excluded meta-file rule recorded in the lock.

Paths below are relative to C:/hybridclr_optimize/artifacts/dhe-virtual-admission.
runtime-01/DHE-Unity2022/runtime-manifest.json has SHA-256
DB60C37A213E72F3087207999ECA4BB03E90D9B37249534B3DBA8040B39B7B53.
Its native tree is byte-identical to ../dhe-virtual-signatures/runtime-06:
B98F4626051DE0A550174F579D5ABFD3DEAF8151BC0EC4E4C79E33E8E7CC34C8.
The real Editor header tree is also identical. Therefore native compile/CTest
evidence remains that checkpoint's native-06 at these same native commits,
with mergeReady=true and surrogateExternalHeadersUsed=false. No new native
implementation or independently repeated native gate is claimed here.

## Admission behavior and review correction

The package declares three distinct capabilities:

- current-generic-methodimpl-owners-v1: retained generic MethodImpl declaration
  owners need the corrected Current container resolution, including during no-op
  preparation;
- current-virtual-signature-frames-v1: existing nonscalar virtual signatures
  selected by the Current storage/execution plan need the corrected frame route;
- current-scalar-instance-frames-v1: selected scalar instance bodies retaining
  Base receiver storage need the exact-owner native frame proof.

The runtime still validates the actual receiver, physical ancestry and ABI. A
capability name does not bypass those checks. Generic MethodImpl admission facts
are build-time-only and do not alter encoded MV bytes.

policy-02-control reproduces an unnecessary capability requirement when just one
reference-valued virtual method body changes. e5041df removes the body-version
trigger and keeps storage/execution selection as the requirement. policy-03
passes 45 checks, including old/grown/no-op/new-type cases, missing-capability
rejection, preserved inputs and identical archived MV bytes. The before/after
body-only fixture DLL is byte-identical, SHA-256
B4FE4C397D89BB4A7B4912F255E98EAEAA9D90C3301CCC39EE5A25638B9B0FEF.
The failed control remains preserved. declaration-policy-02 passes all 32
existing declaration checks on the corrected host-03 sources.

managed-plan-01.json passes 114 package-loader/plan checks at c9c95f0 with the same
a96d3b9 package. This is earlier tool-source evidence, not a rerun at e5041df;
the subsequent change only removes the reproduced unnecessary virtual-body
capability requirement. Native calls in this managed fixture are recorded,
not executed. Actual Player execution is recorded separately below.

old-base-rejection-01 uses the actual resource-update command against Base-88's
immutable older inventory. Its validation reports exactly the three missing
capabilities and rejects before any Player runs. Base-88 has the corrected
native code but never declared these capabilities; its inventory is not edited
retroactively. Base-81 is accepted when none of the three new capabilities is
required. This demonstrates per-Base admission rather than a blanket version
upgrade requirement.

## Immutable Players and final shared resource

| Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 89, old layouts | 08a6585f7d5713f56b01462b1c09511e93ef61ead823d27c90d43c04aba7f06c | 3946CC1BE088720FE066A56676BAF04F27EACBF52C1920DD6E738CA251715838 |
| 90, grown layouts | a703621f8c4a127e906811ec90c37b72f2f3869dfd8ae0fadd4a61c43a740bd6 | EE35BA9B01C9D35A9C93D805C47AFFE1FB41DE7FE2DA5A750D9A15F208F816ED |

Each passes construction, startup and its generated no-op resource. Their
noop-base89-01/noop-base90-01 results each pass 25 signature checks, six unchanged
implementations, positive AOT entries and zero DHE interpreter entries. The
no-op tests are correctness/routing evidence, not throughput measurements.

shared-three-base-01 uses tool-02 and the exact preceding current-01/current
DLL set, SHA-256 c3d2911f1e4228560e62433736b1a7f4ee8a8a7701bfc4e99e2b8338d5570906.
Its manifest SHA is 193C36FF9739608160447EFD3FF9EAD15C141BFB0CED3E925034950632E3A8CC.
It requires all three capabilities on Base-89, only the MethodImpl capability
on Base-90, and none of these three on ../dhe-declaration-transitions/base-81.
The latter has no original signature workload types and retains its earlier
ec5684e/b1e3952 native runtime.

All three ordinary resource runs pass 46 business checks. The explicit
shared-base89-probe-01, shared-base90-probe-01 and shared-base81-probe-01 each
pass all 25 signature and 46 business checks. Base-89 additionally passes the
six cached old-receiver checks, including safe rejection and preserved data;
this is not automatic object migration.

shared-three-base-audit-01.json independently verifies 167 files and original
Current/Player identities. SHA-256:
466555376691EFABEDAD920FC5D060BDE7C5BECDAB0DE47EC7A058E8098ACA9C.
shared-public-probes-01/result.json passes 22 recovery checks and binds 138 files.
SHA-256: 2EE69A144AA15513413325056B8142E33E21B8F23D1673D8DB7347CC39A039B2.
It verifies the original native preparation failure, no business effects in the
failed process and restoration with valid inputs in a fresh process.

## Remaining scope and rollback

Continue framework/native callbacks beyond the qualified direct/virtual calls,
declaration and parent changes, Scene/Prefab and existing-object semantics,
simultaneous publication stress, performance and memory validation. Windows x64
does not establish Android ARM64 correctness or memory ordering. Source candidates
remain conditional; the resource manifests correctly retain releaseReady=false.

For a resource rollback, restore a resource compatible with the immutable Base
and restart. Each new Base's archived generated no-op resource is a concrete
original-code control. Replacing its embedded native runtime requires a new
Player. To roll back this source checkpoint, use the matching source combination
and resources in dhe-virtual-signatures-windows.md; never rewrite Base identities,
capabilities or existing tags and do not live-unload committed Current assemblies.

The temporary admission-review worktree was removed after its commits were
fast-forwarded into the candidate; it can be recreated from 4c5b004. No stash was
created or removed. Archived projects 17/21/24/26/27 were losslessly compressed;
their Players, input bytes and failure evidence remain preserved.
