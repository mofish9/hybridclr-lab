# DHE release ledger design

## Problem and observable workflow

The DHE Base registry authenticates one document and its direct parent, but
toolchain 0.1.21 has no persistent input that identifies the registry used by the
last published hotfix. An operator can therefore create another revision 1 with
the same `registryId`, omit previously supported Bases, and still produce a green
resource update. This was reproduced against the five-Base 0.1.21 evidence by
creating a one-Base Unity 2021 registry and successfully running
`resource-update`.

The required observable workflow is a long-lived release channel:

1. A Base Player may be added whenever Unity 2021, Unity 2022, or Tuanjie 2022
   ships a new app version.
2. Every later hotfix is built once and must be checked against every active Base.
3. A hotfix can retain the registry, advance it by adding or explicitly retiring
   Bases, but cannot reset or fork it.
4. The output becomes the only accepted head for the next hotfix in that channel.

## Gates

The primary metric is correctness: a Release resource update must have one stable
channel ID, a monotonically increasing release revision, an exact previous-ledger
SHA-256, and a registry that is either byte-identical to the previous head or its
direct successor. Reset, stale-head, fork, registry omission, ledger tamper, and
manifest/ledger mismatch cases must all fail before a publishable manifest is
accepted.

The secondary metrics are deterministic package identity, Base count, current
assembly-set identity, and unchanged Base MetaVersion/native files across
consecutive staging. Host execution time and memory must not regress materially,
but this change does not alter Player steady-state execution and makes no runtime
performance claim.

The evidence matrix must cover Unity 2021 Standard, Unity 2022 FGS, and Tuanjie
2022 FGS Bases. Engine-specific managed payload variants remain supported inside
one resource release. Data from one engine cannot substitute for another
engine's Player correctness gate.

Formal evidence must contain exactly one passing changed-Player report for every
active Base in the release manifest. A subset is not sufficient even when it
covers all three engine workflows. The regression binds that complete set to the
exact consecutive `ResourceUpdateRoot2` manifest and ledger, records the validated
head identity, and release-evidence independently compares the same identity.
The next hotfix may keep the registry unchanged. If its active Base set changes,
the registry must be the authenticated direct successor; the workflow does not
assume a fixed number of Bases or require every hotfix to add a Base.

## Contract

`resource-update -Mode Release` requires registry mode and exactly one of:

- `-InitializeReleaseLedger -ReleaseChannelId <stable-id>` for the one-time
  migration/genesis release; or
- `-PreviousReleaseLedger <ledger.json>
  -ExpectedPreviousReleaseLedgerSha256 <sha256>` for every continuation.

The expected previous hash is a release-system input, not a value inferred from
the candidate files. The output `dhe-release-ledger.json` binds the channel and
release revision, its direct parent ledger hash, Base registry head, resource
manifest and validation hashes, payload-variant set, and current assembly set.
The manifest, validation, runtime plan, stage report, and Player workflow evidence
carry the same release identity.

Initialization is a privileged migration operation, not a reusable escape hatch.
Before genesis, the release owner must audit that the registry contains every
live Base. Once a channel head exists, protected CI/release state must reject all
later uses of `-InitializeReleaseLedger`; the host can authenticate a supplied
head, but cannot discover global publication history from local files alone.

For a continuation, the current registry must either equal the previous ledger's
registry SHA/revision or advance exactly one registry revision whose parent is
that SHA. A new revision 1, a skipped registry revision, a changed registry ID,
or a previous registry that differs from the published ledger is rejected.

Exploratory mode remains available for diagnostics and compatibility with 0.1.21
artifacts, but it is not Release-ready and cannot create or advance a ledger.

## Scope and rollback

This candidate changes only the cross-platform C# host, JSON schemas, staging
evidence, and documentation. It does not change HybridCLR, il2cpp_plus, generated
C++, ABI, Player dispatch, supplemental metadata behavior, or the Base identity.
The same implementation applies on Windows and macOS hosts.

The all-FGS case is also corrected: explicit `aotMetadataRoot: null` entries form
the authenticated empty metadata set even if the shared project settings retain a
non-empty `patchAOTAssemblies` list. Legacy parallel arguments still require
explicit metadata roots.

Rollback is the independent release-ledger commit boundary. Projects can pin the
authenticated 0.1.21 package and use its exploratory resource command, but a
production channel that has adopted a ledger must never discard or reinitialize
its published head.
