# DHE portable evidence authority set

## Problem and observable workflow

A long-lived release channel can contain Base Players created by different DHE
toolchain releases. The 0.1.25 five-Base fixture already contains four Players
authorized by package `7757d0...` and one authorized by package `3982ee...`.
`resource-release-gate` accepts that set only when `ValidationSourceRoot` provides
the complete Git ancestry from both historical package commits to the current
validator. A project that installs immutable Release packages but does not retain
the tool repository cannot therefore keep those Bases active after a tool upgrade.

The required workflow is:

1. The currently pinned Release package is the publishing authority.
2. That package explicitly names every older Release package whose Base evidence
   remains admissible.
3. Each Player report is revalidated against its own immutable package and the
   current resource/gate implementation.
4. One resource release can contain active Bases from several authorized package
   generations, Unity 2021, Unity 2022, and Tuanjie 2022.
5. An unknown, altered, or removed historical package ID fails before promotion.

## Contract

`manifests/dhe-toolchain-evidence-authorities.json` uses
`explicit-package-id-set-v1`. It records historical package ID, toolchain version,
source commit, and source tree. The file is an authenticated package-layout input,
so its SHA-256 contributes to the current package ID. The current package does not
authorize itself through the list; `-ExpectedToolchainPackageId` remains the
external trust pin for the publisher.

Before publishing a successor toolchain, add every preceding official Release
package that may have built a still-online Base. In particular, the immediately
preceding package must move from `current-package` authority in its own release to
`authorized-historical-package` authority in the successor. Omitting it creates
an upgrade gap even when the runtime contract is unchanged. Published package
directories and Package IDs are immutable; a missed authority is corrected by a
new toolchain version, never by rewriting the earlier manifest.

For every `resource-player-workflow` report, `resource-release-gate` applies one
of three modes:

- `current-package`: report package ID equals the pinned current package ID;
- `authorized-historical-package`: the ID and source identity match one record in
  the current package's authority set, and the referenced historical package
  independently passes `verify-package -RequireRelease`;
- `git-ancestry`: compatibility path used while qualifying a new package before
  that package can authorize historical evidence itself.

Normal project publication must use the first two modes and does not need
`ValidationSourceRoot`. The gate records the authority-set SHA, all distinct
evidence package IDs, and each Player's selected mode. `channel-state promote`
regenerates those fields before its compare-and-swap.

## Gates

Correctness requires the existing exact active-Base, resource ledger, payload,
runtime, native, and Player checks plus:

- at least two real historical package IDs in the mixed-authority regression;
- exact package ID/version/source-head/source-tree agreement;
- exact coverage of every authority record by an independently verified historical
  Release package during toolchain publication;
- duplicate authority ID rejection;
- unknown, wrong-ID, unused-root, current-package-substitution, and source-tampered
  authority rejection;
- portable aggregation without Git after the new Release package exists;
- unchanged five-Base and three-engine coverage.

This feature changes no HybridCLR runtime, IL2CPP ABI, Player dispatch, managed
compatibility subset, or payload bytes. Performance is gate-time only; Player
steady-state performance and memory are not affected. Windows proves the current
five-Base workflow. macOS/iOS and Android device evidence remain independent.

## Rollback

The implementation is one C# tool/schema/manifest commit. Rolling back before
adoption pins 0.1.25 and retains Git-ancestry validation. After a current package
has authorized a historical package, removing that ID is an explicit revocation:
all active Bases built under it must be retired or replaced before the next gate
can pass. Channel and release-ledger history must not be reset.
