# Reference hierarchy queries: Unity 2022 Windows

This continues the two-Base owner checkpoint in `dhe-reference-owner-windows.md`.
It remains an incomplete research candidate. Interface removal/replacement and
parent changes require actual Player qualification; policy admission alone is
not sufficient. No formal branch, runtime tag, Installer default or CAT changed.
Paths below are relative to `D:/hybridclr_artifacts`.

## Preserved reproduction

Lab f8db9f6 compiles the optional hierarchy probe into callback Current02 without
changing its 46 business cases. `dhe-reference-hierarchy-query-current-02` and
`dhe-reference-hierarchy-query-resource-02` pass CLR reference and the ordinary
resource workflow on immutable Base-70. Host03 SHA is
`0010CE495EDC0F152A66AEA25DBF157B38071AF86288F50E8A717567E62BD51F`;
the tool is owner-tool03 from the preceding report.

`dhe-reference-hierarchy-query-before-01` fails precisely three checks:
public-type-interface-list, instance-type-interface-list, public-interface-by-name.
The seven ancestry/generic-parameter/field/lifecycle-state controls pass. Player
and resource hashes are unchanged. Its result SHA is
`F418E6C85F51B1D0825CAF9B5326152329736ECA517BA9DF56414A41A96136C2`.
Current interfaces execute but the public reflection queries still enumerate Base.

The earlier query-current01/resource01 failure belongs to the fixture: CLR JIT
resolved Unity locals even with the optional flag absent. f8db9f6 separates a
NoInlining Unity body from the flag-only entry. Keep those original artifacts.

## Candidate sources and native gate

- HybridCLR c16abc97f4eecc96dfab2ef0eb8d1c812281cb26 selects the owner of a native
  interface cursor and excludes generic-parameter/pointer metadata from reference
  allocation TypeDef lookup. Canonical source SHA:
  `B21D998EACDFC4AEAF81D05A070B9E5140481B05A5965D622637F5D6589D3841`.
- Unity 2022 IL2CPP 12b0e900bbf0d28246664e6bcb7ead06a76d7826 resolves selected
  reference declarations in public interface, parent and assignability queries.
  Canonical source SHA:
  `A0E98B48C6D7B379B7AD72DA66E6B52AA03F3B9D492C0C5C0EA2ECFF24F23851`.
- Package 11feea7 adds the candidate interface-evolution capability, but has an
  Editor/runtime inventory mismatch; it does not produce a usable Player.
- Package 39ef40fdd5451a72d1775d246c64b1ae7b93c882 fixes that mismatch by deriving
  the Editor inventory from `DheRuntime.GetSupportedRuntimeCapabilities()`.
  It returns a copy, preserving runtime validation against caller mutation.
  Canonical package SHA, with the existing ignored meta path:
  `02E8F9921EAF5EF37DD52025BE80180006E9E1FB499887E2353244029E8E944D`.

Internal Class ancestry and receiver/offset guards remain unchanged. Initial
native interface enumeration selects Current; a non-null cursor inside the
immutable requested table finishes that same table, including repeated end calls.
The existing registration acquire and metadata lock govern selection; no new
cache or object migration is introduced. Windows x64 does not qualify ARM64.

At lab 9aefc23, runtime01/native01 pass real Unity 2022 headers, compile and CTest:
mergeReady=true, surrogateExternalHeadersUsed=false. Runtime01 manifest SHA:
`50468764E4E06203AA49A641A3E7447E262AD63800C3876393C68E7411906880`.
This manifest binds the earlier package 11feea7. Managed01 passes 113 checks;
owner-plan01 passes 38, generic-plan01 passes 17, policy-after02 passes 26.

## Preserved Base build failure

`dhe-reference-hierarchy-base-72` was built at lab 9aefc23 with hierarchy-host04,
hierarchy-tool01 and runtime01, using the same evolved input DLLs as Base-70.
Editor build, native finalization, ordinary guard coverage and identity schema
finish, but original Player startup rejects the embedded identity. The Editor
inventory omitted `physical-current-interface-evolution-v1` while the runtime
required exact inventory equality. No DHE assembly loads and no business runs.
Keep Base-72, its inputs, generated C++, logs and failure report unchanged.

The package fix and updated locks require a new Base and new startup/no-op,
hierarchy-query and adjacent regression evidence. None is claimed in this section.
Interface removal/replacement, actual parent changes, generic/value hierarchies,
old objects, scene/Prefab, native boundaries and performance/memory remain open.
