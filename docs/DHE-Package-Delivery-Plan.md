# Package-owned DHE opt5 delivery completion

Scope: close the six findings in reports/dhe-package-boundary-review.md. Keep
HybridCLR v8.13.0-opt5 / IL2CPP v2022-8.11.0-opt5 unchanged and retain their
approved upstream ancestry. Do not update cat. Runtime changes, if unexpectedly
required, need a separate identity decision; no published tag may be moved.

User scenario: a project installs the reviewed package, supplies its repository
URLs, runs ordinary Installer, then calls the package's C# build/resource APIs
without the maintainer's Lab, Git checkouts or staging directories.

Implementation boundaries:

1. Package-owned tool source/build input and reproducible binary distribution;
   Lab fixtures consume that source instead of maintaining a duplicate.
2. Installer receipt and portable project build provenance, verified against the
   approved runtime source fingerprints and actual Editor/compiler. Preserve
   legacy Lab manifests as historical validation inputs, not a project dependency.
3. Parameterized lifecycle contexts and input callbacks; CLI delegates to the
   same implementation. Default new Bases require complete ordinary-AOT guards
   for structural evolution, with fail-closed coverage validation.
4. Correct SVN peg escaping and package-aware config/template generation.
5. Update package instructions and run a clean project consumer workflow using
   ordinary Installer, method/resource and structural evolution regression.

Primary gate: correctness and source identity; no performance claim. Record
build time/size observations if relevant but do not optimize by omitting required
guards. Keep Windows Unity 2022 as the current real-Editor test target. Android,
iOS and Tuanjie remain separate gates; no claimed mobile production readiness.

Verify source/compiler restoration on failure, output protection, package hash
checking, stale installation detection, SVN @ paths, and multiple consecutive
resource updates. Preserve immutable Base identity throughout resource updates.

Commit candidate source before final identity-bound tests, fast-forward the
reviewed package commit into repos/hybridclr_unity, then push package and Lab
locks/reports. All required production code belongs to the three repos;
Lab retains fixtures and evidence. Package has no opt tag. Recovery uses the
previous package commit 4fb36af with matching archived Base/build identities;
do not relabel existing Players or mix tool identities.
