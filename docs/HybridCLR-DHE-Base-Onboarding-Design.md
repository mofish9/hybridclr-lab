# HybridCLR DHE Base onboarding and one-build release design

## Problem and observable workflow

The existing `base-registry` and `resource-update` commands already implement
the required primitives, but a project must manually coordinate two commands
and several parallel argument lists. The production workflow needs one
structured operation with two supported cases:

1. A normal hotfix reuses the current active Base registry.
2. A newly shipped application version appends its immutable Base archive to
   that registry before building the next hotfix.

Both cases must produce one resource-release directory. That directory may
contain several target-specific managed payload variants, but every active Base
must be compatible with and select exactly one variant. The operation must not
publish a registry-only or resource-only partial result.

## Inputs and identity

`resource-release-build` consumes one
`hybridclr.dhe-resource-release-build-config.json` document. Paths use
`config-relative-v1` semantics. The document binds:

- the HybridCLR settings file and all named current assembly roots;
- one primary current variant;
- the existing Base registry, or a registry ID for the first release;
- the direct parent registry when an unchanged registry at revision 2 or later
  is reused, so its lineage can be revalidated without weakening the existing
  `resource-update` contract;
- zero or more structured new-Base archive records;
- explicit Base retirements and their reason;
- a SHA-256-pinned protected channel snapshot for Release mode;
- the final output root.

Each new-Base record contains its BuildIdentity, baseline DLL root, native
manifest, engine workflow, payload variant, label, and optional AOT metadata
root. These are passed through the existing Base artifact validator; the
orchestrator does not trust config values as identity claims.

## Correctness and publication boundary

The command builds in a unique sibling work directory. It first validates the
config schema and every input path. When the active Base set changes it invokes
the canonical registry builder and requires a direct registry successor. It
then invokes the canonical resource builder with that registry, its parent,
the pinned channel snapshot, and all current variants.

Before publication it re-reads and schema-validates the registry, resource
manifest, validation, runtime plan, and release ledger. It requires:

- exact active-Base count and Base ID equality between registry and resource;
- exact registry and parent SHA-256 binding;
- exact named variant set and one selection for every active Base;
- `unsupportedChangeCount == 0` and complete native guard coverage for every
  active Base;
- Release mode, release readiness, channel ID/revision, and ledger identity to
  agree with the pinned snapshot;
- the complete output tree to pass `schema-gate`.

Only after all checks pass is the work directory renamed to the final output.
Replacement is opt-in and uses a sibling backup so a failed final rename can
restore the prior output. Release automation should normally use a new output
path and content-addressed channel promotion rather than replacement.

The primary metric is correctness: one report, one resource candidate, exact
active-Base coverage, and zero unsupported changes. Secondary metrics are the
active Base count, added/retired Base counts, payload variant count, and output
hashes. This command makes no performance claim and must not hide Player/device
work behind build-time success.

## Engine scope

The command contains no engine-specific shell logic. Base records are accepted
only for the locked `Unity2021Standard`, `Unity2022Fgs`, and `Tuanjie2022Fgs`
workflows, and all three continue through the existing identity, ABI, native
guard, metadata, and runtime-capability validators. The same C# path is intended
for Windows and macOS hosts. Windows validation cannot stand in for iOS/Xcode or
Android/iOS device evidence.

## Included and excluded work

Included:

- atomic orchestration of registry reuse/extension and one resource build;
- structured new-Base inputs and target variants;
- lineage, snapshot, schema, tamper, and partial-output regression coverage;
- a Demo lifecycle with consecutive Base additions across all three engines.

Excluded:

- building or signing a Player;
- uploading resources or advancing the protected channel head;
- fabricating Base archives from mutable Unity build directories;
- bypassing unsupported AOT layout, vtable, GC, reflection, or ABI changes;
- Android/iOS device correctness and production performance claims.

The output still requires one Player result per active Base, followed by
`resource-release-gate` and `channel-state promote`.

## Rollback

The implementation is isolated to the new command, its schemas, regression,
and documentation. Projects can roll back by pinning toolchain 0.1.28 and using
the existing explicit `base-registry` plus `resource-update` sequence. A
published registry revision or channel head is immutable; rollback of live
content is always a forward release from the actual protected head.
