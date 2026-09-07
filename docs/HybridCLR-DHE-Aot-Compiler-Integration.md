# Package-owned DHE AOT compiler integration

## Objective and gate

Carry the verified discarded-expression repair and explicit divide checks into
normal C# Base builds. Unchanged AOT exception behavior must match the original
220-case CLR contract, without changing test IL, MV, golden or archived Bases.
Correctness, restoration after failure, compiler identity and stable multi-stage
generation are hard gates. Build overhead is secondary; no performance or memory
benefit is inferred from these tests. This compiler change requires no FGS,
supplemental metadata or prewarming, and changes no native ABI or publication.

## Ownership and lifecycle

- Package: one dnlib patch implementation shared with the lab test host; a
  project-local compiler transaction; automatic scopes around current generation,
  Base Player builds and native finalization. No new PS1 or project-owned logic.
- Never modify the Editor installation. Validate the original compiler and its
  DataModel against the Editor, back up immutable bytes, write a recovery journal,
  then atomically replace only the installed project-local compiler. Restore both
  compiler and additional IL2CPP arguments in finally. A later process recovers
  a known interrupted transaction; unknown changes fail closed and remain intact.
- Hold an exclusive filesystem lock for the transaction. Nested package calls
  share one same-thread scope. A different thread/process cannot acquire it.
- Record the successful generated-source snapshot and effective compiler identity.
  Native finalization must match this provenance before editing guards. Record
  the guarded snapshot afterward, including any package-owned Bee regeneration.
- Native manifests include original/patched compiler and DataModel hashes, the
  patch contract, Editor version and effective arguments. Their existing hash
  already participates in BaseId; do not introduce another MV format or relabel
  old runtime capabilities. Historical native manifests remain readable.

## Validation and rollback

Run package-linked tests for positive patching, unknown input, repeated patching,
normal/exception restoration, interrupted recovery, lock contention, argument
conflict, foreign compiler/journal changes, path escapes and stale generated
sources. Use actual compiler inputs separately for Unity 2021, Unity 2022 and
Tuanjie. Check real Editor compilation, then fresh Windows Base/no-op and full
cold differential runs. All prior failures and immutable Base archives remain.

Mac uses the same managed code and path APIs, but lacks actual host execution
evidence here. Missing or unsupported compiler layouts fail explicitly. Android
device testing remains the later project handoff, not a reason to wait now.
Rollback removes the package integration commits and uses a separately identified
Base. The transaction restores local compiler state; it cannot retroactively
repair or relabel a shipped native Player.
