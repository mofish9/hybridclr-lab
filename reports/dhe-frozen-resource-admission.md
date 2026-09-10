# Frozen AOT admission through the standard resource workflow

Target Unity 2022 Windows first. The update runs at startup before hotfix business
entry. Original hotfix assemblies may evolve; ordinary AOT assemblies may only
use their captured Base IL to adapt those dependencies. Unity 2021 is out of the
current user scope; Tuanjie follows Windows qualification.

Admission requires a Base-authenticated complete ordinary guard inventory and
exact, freshly compiled frozen selections. Discharge individual layout/method
obligations only after matching their source, token and stable identity. Keep
native-only ABI, excluded identity, ordinary static storage, ThreadStatic and
RVA obligations closed until separately implemented and tested.

The resource includes the original analysis manifest at
`payload/frozen-aot/<baseId>/snapshot.json`. The loader derives its path and
authenticates the bytes against the analysis hash embedded in the Player. Each
frozen source must be an ordinary assembly in that manifest, with the exact Base
DLL hash; its MV must bind the same DLL. Manifest/validation/plan agreement alone
does not authenticate a source. A new capability prevents older Bases from
accepting resources which rely on this loader guarantee. MV schema stays 1.

Required tests: valid standard resource loading, rejected missing/substituted
snapshots, a consistently substituted DLL/MV/record, wrong source role, source
hash mismatch inside the MV, missing native coverage, and unresolved ABI cases.
Reject before any native registration or business entry. Preserve retry behavior.
Then build a new immutable Windows Base and load resources through the public
resource API. Existing proof-15 remains historical native-transaction evidence.

Correctness and evidence identity are the primary gates. No performance claim
is made here; P50/P95/P99, startup cost and memory qualification remain separate
work with production-equivalent sources. Package loader/source binding and lab
admission are independently revertible candidate commits. No CAT, formal branch,
tag, remote or Installer-default changes belong to this step.
