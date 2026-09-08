# Ordinary AOT descendants of DHE classes

Extend the fixed-Base DHE workflow across an unchanged ordinary AOT assembly
whose classes inherit hotfix classes. The ordinary assembly is never included
in the four-assembly update payload. Keep the eight-Base class-virtual
checkpoint, its DLL/MV files and its failed experiments intact.

The concrete correctness issue is GetBaseDefinition on an ordinary AOT
override: its immutable slot indexes the Base parent table, while the Current
parent table can have an inserted method at that same number. Walk parent
declarations by logical identity across that boundary. Preserve the distinction
between the root definition and the immediate inherited implementation used by
attribute inheritance. New-slot methods must stop at their own declaration.

Add HybridCLR.NativeDescendants as an ordinary linked AOT test plugin. Its
unchanged methods exercise retained native overrides, inherited new overrides,
native new-slot hiding, closed generics, generic virtual value returns,
delegates, reflection, inherited attributes, explicit base calls, and calls
with a concretely constructed sealed hotfix receiver. The existing DLL/MV
resources remain unchanged. The Demo invokes this optional native test plugin
after the normal DHE checks; a configured native boundary Base must produce all
named receipts. Older Bases without the plugin continue their existing checks.

First build a Base with the previous runtime and reproduce the failure using
the unchanged resource; compare the same ordinary DLL against Base and Current
assemblies on CLR. Then fix native lookup, freeze commits, run real-header
compile/CTest and fixed Windows Player/no-op/resource tests on Unity 2022 and
Tuanjie 2022. Unity 2021 is excluded by the user's updated scope. Do not use
Android/iOS unavailability to stop Windows work.

Primary acceptance is zero semantic differences and unchanged native plugin
bytes across resource updates. Keep the existing 71 groups / 220 differential
cases and per-caller AOT receipts. No timing claims are made: steady virtual
lookup, P50/P95/P99, end-to-end first touch and memory remain performance gates.
The metadata cache remains process-lived and lock-published; audit changes to
publication/exception paths independently of Windows x64 evidence.

Use research/dhe-native-descendants-v8.13.0 worktrees. Do not modify CAT,
generated C++ or installed runtime source directly. Runtime fixes do not change
MV schema. This remains an unpublished exploratory candidate; preserve formal
tags and Installer defaults. A failed native experiment cannot be repaired by
shipping DLL/MV alone; rebuild only the new candidate Base, preserving the old
Player and failure report. Roll back to the exact eight-Base checkpoint and
its compatible resources if this candidate fails.
