# HybridCLR DHE resource release planning design

## Observable workflow and boundary

The Base registry defines which immutable Players remain online, while the
resource candidate defines which payload variant each active Base must run.
Neither document describes where those Players or platform reports are located.
Previously, every hotfix release had to repeat that mapping manually in a large
`resource-release-qualify` config.

`resource-release-plan` makes the mapping a maintained release input. A project
updates one Base runner catalog when a Base is onboarded, replaced, or retired.
For each resource release, the command authenticates the Release toolchain,
resource ledger, protected channel snapshot, registry identity, and historical
toolchain authorities, then joins the catalog to the exact active Base set. It
atomically emits:

- `dhe-resource-release-plan.json`, the auditable Base/job/variant plan;
- `dhe-resource-release-qualification-config.json`, ready for the existing
  qualification command;
- immutable audit copies of the plan config and Base runner catalog.

Planning does not stage resources, start a Player, accept a Player result,
promote a channel, or publish files. It creates no mobile correctness evidence.

## Catalog lifecycle

The catalog is bound to `baseRegistryId`, `baseRegistryRevision`, and the exact
registry SHA-256. Its Base IDs must equal the candidate's complete compatible
Base set. A registry change therefore requires a reviewed catalog update, but a
normal body-only hotfix using the same registry does not.

A `process` catalog entry stores the immutable test Player location, Base build
identity, Base workflow report, working directory, argument array, and files
that must remain unchanged during execution. The generated qualification config
preserves the existing direct-process contract, including standalone `{result}`
and `{log}` tokens. No shell is used.

A `prequalified` entry stores a report path template for Android, iOS, or another
external platform job. Supported tokens are `{baseId}`, `{releaseChannelId}`,
`{releaseRevision}`, and `{releaseLedgerSha256}`. The Base ID and release ledger
tokens must each occur exactly once and remain present in the normalized path.
This ensures two Bases or two consecutive releases cannot resolve to the same
report. Unknown tokens and duplicate output paths fail before a plan is written.

## Release sequence

1. Capture a SHA-256-pinned snapshot of the protected channel head.
2. Run `resource-release-build` once to produce the next Release resource
   candidate for every active Base.
3. Run `resource-release-plan` with that same snapshot and the registry-bound
   runner catalog.
4. For process runners, run the generated config directly. For external
   runners, distribute the candidate and the planned job identity, then place
   each authenticated Player workflow report at its generated path.
5. Run `resource-release-qualify` with the generated config. It revalidates all
   reports and emits the exact-coverage aggregate gate.
6. Promote the channel only through the existing compare-and-swap
   `channel-state promote` operation.

The generated qualification output path must be new. Replanning also requires a
new plan output path, so audit evidence is never silently replaced.

## Cross-platform scope

The planner is C# and has no Unity-version or host-shell branch. Unity 2021,
Unity 2022, and Tuanjie 2022 are selected from the authenticated resource/Base
records. Windows can exercise process runners locally. Android and iOS normally
use prequalified paths populated by their platform build/device jobs.

Windows validation proves only the planning, schema, identity, and Windows
Player paths. Android still requires a real APK/device result and production
memory/performance evidence. iOS still requires macOS/Xcode/signing/device
evidence. Those reports flow through the same generated qualification config but
cannot be substituted by a Windows result.

## Failure and rollback

Missing, duplicate, extra, or registry-mismatched Base records; a stale snapshot;
an unauthorized evidence package; an invalid template; an incomplete engine
matrix; or an unsafe output relationship fails before publication. Staging
directories are removed on failure.

Rollback deletes an unpromoted plan/qualification output and pins the preceding
immutable tool package. Once a resource revision is promoted, application
rollback remains a new forward release from the actual protected channel head.
