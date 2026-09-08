# DHE cross-assembly interfaces: focused Unity 2021 checkpoint

Clean replay `32708292354e974aa7e3cc11dc73c256ae86a37b` passes three independent
Windows processes on one evolved Base. Every process executes 52 evolution groups
and 220 differential cases with zero differences. Consecutive and skipped updates
reuse the same native Player and embedded Base MVs. No pre-touch or inline padding
is used. This is exploratory correctness evidence, not the full goal or performance
qualification. Other engines and original generations are still pending.

## Exact successful sources

| Component | Commit |
|---|---|
| HybridCLR | `6ed809b4e372fe91f0283c9ed3ad80c81cd11eef` |
| Unity 2021 IL2CPP | `01c17253b198590671280eecf91b819a5f2d622b` |
| Unity package | `92e27c2d13aec4913e15c2f8dad0682ae677b58d` |
| Base/resource tool snapshot | `4774f05` |
| Native compile configuration repair | `309e8f2` |

BaseId is `1ee0b432ce1bbc2edc2830930c583c0c0be5f529bbc3999a188e009d9f6b6b1d`,
contract `dhe-runtime-v19`. MV remains `DHEMETA1`, schema 1. The first resource
contains unchanged case bodies/metadata but 12 changed dependency fingerprints
because identical compiler array constants moved RVA. Native state matches the
exact Base/current MV for all 220 entries: 208 retain AOT, 12 are interpreted.
First payload has no entry instrumentation; zero receipts is not an all-AOT claim.
Latest and skipped-latest each have 220 interpreted entry receipts.

All 19 distinct protected files were independently rehashed after replay and remain
unchanged. Each resource still contains one common DLL/MV payload. The new tests
cover inherited implementations, ordinary/generic interface calls, delegates,
reflection, interface maps, new child/generic interfaces, cross-image explicit
MemberRef declarations and canonical type identity. They do not yet cover evolving
an existing native generic-interface definition.

The code fixes three separately reproduced faults: MethodImpl MemberRefs using
Base-only declaration tables; inherited Current interfaces paired with old parent
offsets; and Base virtual slots used against a newly interpreted descendant's
Current vtable. The last repair is shared by interpreter, reflection, direct/generic
AOT calls and ldvirtftn. Broader hook coverage and performance still require tests.

## Artifacts

Paths below are under `C:/hybridclr_optimize/artifacts/dhe-evolution-20260908`.

- `base-cross-virtual-u21/project-workflow-report.json`: Base/no-op/schema pass.
- `replay-cross-virtual-u21-cold/report.json`: SHA-256
  `0C22A8BAAB899C0F165C01EFFE6ED0069C36FEFD8A6B49AEA16F69AEC2D6732D`.
- `native-cross-virtual-verified/DHE-Unity2021/native-gate.json`: real-header
  compile/CTest pass, `mergeReady=true`, no surrogate headers.
- `cross-interface-descendant-capability/report.json`: 29 focused checks pass.
- `cross-inheritance-slot-regression/report.json`: 26 checks pass, including
  preserving v16 eligibility for the earlier local-interface fixture.

Earlier failed replays and crash dumps remain intact. The initial native test
configuration lacked Boehm headers for newly compiled Object.cpp; it was repaired
without dropping Object.cpp coverage. Unity 2022's v19 native compile/CTest passes;
Tuanjie's Object.cpp required an explicit DHE include. The Tuanjie source repair
and the v20 package identity are later sources, not part of this v19 Player result.

New Unity 2021 input stashes: `059a4d8`, `9119ba0`, `a7ed1cf`; older stashes remain.
No CAT files, formal maintenance branches, runtime tags or Installer defaults were
changed. Rollback selects a matched previous runtime/package/tool and resources
within that Base's proven capabilities; an already built Base cannot acquire a
native fix from a resource package. Full multi-generation Windows qualification,
RVA fingerprint stability, broader evolution and performance remain open.
