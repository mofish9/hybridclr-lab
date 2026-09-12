# Opt5 package completeness review

Scope: restore the full DHE managed workflow on upstream package v8.13.0,
keeping the existing immutable native opt5 tags. Unity 2021 remains official;
Tuanjie is outside this Unity 2022 qualification. No upstream package upgrade.

Regression: package 65581c1 omits the later execution plan, Delivery, asset
provenance and compiler integration while locks claim runtime contract v33.
It also copies the upstream 8.14.1 Installer selection code into an 8.13 package.
Hash and JSON schema checks alone cannot detect this semantic mismatch.

Acceptance: restore the existing pre-upgrade DHE history, retain commit/repository
pinning, compile the managed execution and native-binding fixtures, run managed
execution/Delivery/provenance checks, and run the real Unity 2022 default Installer
against both immutable tags. Bind all results to the new clean package commit.
Review complete Base/Player coverage separately; do not reuse earlier package
results as current evidence. No new performance or ARM64 correctness claim.

Rollback: a source rollback must use a complete package/runtime combination and
rebuild its Base. Package 65581c1 is incomplete and must not be a trial fallback.
Existing Players can only select a compatible archived delivery and restart.
