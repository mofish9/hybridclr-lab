# Unity 2022 DHE project-trial release plan

The active goal is a new immutable optimization tag plus the package commit that
projects can install and use in their C# packaging workflow. Opt5 is retired.
Reserve opt6 (confirmed absent from both runtime remotes on 2026-09-13); do not
move historical tags. Upstream remains HybridCLR/package 8.13.0 and IL2CPP
v2022-8.11.0. Unity 2021 stays official; Tuanjie and mobile qualification are
deferred by the user.

Start from the physical-dispatch runtime fix 1b7d5a9, IL2CPP ecad8a0 and package
5476c95. Add an explicit native physical-receiver capability, require it at the
IL2CPP integration/build boundary, and advance the managed build contract to v34.
Reject updates requiring this capability for archived older runtimes. Keep the
MV schema unchanged. Synchronize installed source hashes, runtime commits/refs,
and the rebuilt package-owned tool bundle.

Correctness is the primary metric: native compile/CTest with real Unity 2022
headers, package/managed/tool regressions, ordinary Installer receipts, complete
C# BuildBase including failure restoration, no-op and 47-case structural update
on two distinct new Bases, public preparation/lifecycle recovery, and actual
asset delivery/release rollback checks where supplied by the existing demo.
Record exact source and binary identities at every phase. No performance,
memory, P99 or device claims; the bundle remains Exploratory for project trials.
Do not weaken Release certification to make packaging trials pass.

Freeze source before validation. Runtime tags are created only on formal repos
maintenance commits after relevant native and runtime gates pass. Verify remote
tags before package promotion. Ordinary Install then proves the final package
against these immutable tags; no edited generated projects or staging sources.
Project repository URLs remain project settings, never the version-selection JSON.

The package owns all reusable C# building/resource primitives and its DLL. The
project owns scenes, target, resources, signing, upload and bootstrap adapters.
No cat migration is required for this framework release gate. Export only tracked
package files, retaining Tools~ and excluding ignored maintenance bin/obj.

Rollback discards the new candidate/Base or selects a previously validated
complete package/runtime/Base combination. Old opt5 native Players cannot gain
the runtime fix from managed resources alone. A release report must distinguish
project packaging trials from production and list any remaining platform gates.
