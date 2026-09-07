# HybridCLR DHE multi-Base qualification design

## Observable workflow and boundary

`resource-release-build` already produces one resource candidate for every
active Base, but qualification still requires a project to repeat
`stage-resource-update`, Player execution, `resource-player-evidence`, and the
final aggregate gate by hand. That manual loop is a release-integrity risk as
the Base registry grows.

`resource-release-qualify` turns that loop into one config-driven C# operation.
It consumes one immutable resource candidate, the SHA-256-pinned channel
snapshot used to build it, and exactly one runner record for every active Base.
It emits one qualification directory and one state-bound aggregate gate. It
does not build a Base Player, upload a resource, sign a platform package, or
promote the channel head.

The primary metric is exact active-Base correctness coverage. Qualification
passes only when the configured Base ID set equals the candidate manifest set,
every Player report passes, and the aggregate release gate reports
`exactActiveBaseCoverage=true`. Secondary evidence records per-process duration,
PID, exit code, payload selection, report hashes, and engine-matrix coverage.
This workflow makes no performance claim.

## Runner modes

`process` starts an existing test copy of a Player directly with
`ProcessStartInfo.ArgumentList`. The config supplies an argument array containing
exact `{result}` and `{log}` tokens; no shell parses or expands those values.
Before launch, the command stages the resource candidate using that Base's
immutable build identity and records hashes for the configured immutable Player
files. After launch, the canonical `resource-player-evidence` validator binds
the result back to the Base workflow and staged resource.

`prequalified` accepts a complete resource Player workflow report produced by a
separate device or platform job. This is the aggregation path for Android and
iOS runners that cannot execute on the release host. It does not downgrade the
evidence contract: the final `resource-release-gate` revalidates the report,
toolchain authority, release ledger, selected payload variant, and exact Base
identity in the same way as a local process result.

## Fail-closed rules

- The config Base IDs must be unique and exactly equal the active Base IDs in
  the resource manifest before any Player starts.
- A process record's BuildIdentity Base ID and a prequalified report's selected
  Base ID must equal the configured ID.
- Process runners require at least one immutable file, and the Player executable
  must be in that set.
- `{result}` and `{log}` must each appear exactly once as standalone argument
  values. Timeouts terminate the complete process tree.
- The expected current package ID, channel snapshot SHA-256, release ledger,
  historical package roots, and engine-matrix policy flow into the canonical
  aggregate gate. The orchestrator does not reproduce or weaken gate logic.
- Qualification output must be new and outside all package, resource, channel,
  Player asset, workflow, executable, and historical authority roots. A failed
  run may retain partial diagnostic output, but it never emits a passing summary
  or advances the channel.

## Engine and host scope

The command has no Unity-version-specific branch and uses only .NET and the
existing DHE C# validators. Unity 2021 Standard, Unity 2022 FGS, and Tuanjie 2022
FGS are distinguished by immutable Base identity and existing engine workflow
contracts. Direct process execution is supported on Windows and macOS hosts;
device-specific execution remains in platform jobs and returns through
`prequalified` mode.

Windows Player evidence cannot be presented as Android or iOS device evidence.
Android still requires APK/device/PSS/thermal qualification, and iOS still
requires macOS/Xcode/signing/device qualification.

## Rollback

The command only creates a qualification directory and reads or stages test
copies of existing Players. It never mutates the protected channel. Tool
rollback pins the preceding immutable package and resumes the explicit
stage/run/evidence/gate sequence. A promoted application rollback remains a new
forward resource revision from the actual protected head.
