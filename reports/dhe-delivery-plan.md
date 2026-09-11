# Code and asset delivery

Goal: one immutable delivery selects the same Current code and authored assets
for each supported Base. Existing code-only resource validation remains the
authority for Base/MV/runtime compatibility. Add a package-owned C# delivery layer,
without coupling it to YooAsset, Addressables, or an automatic download service.

The delivery manifest binds target, engine workflow, Current-set identity, the
code resource manifest, and a complete inventory of file paths, byte lengths and
SHA-256 hashes. Asset IDs map to entries in that inventory. An externally supplied
manifest hash binds the selected release. Code lives under code/ and assets under
assets/; names are portable and case-unique. Embedded Base MV cannot be overridden.

Prepare verifies the entire inventory and target before configuring DHE, then
mounts the code files at the existing virtual runtime asset root. Only embedded
Base MV may fall back to the Base provider; an absent Current file is an error.
Bind the prepared handle to that exact configured plan so a stale handle cannot
load after reset or reconfiguration. Read/verify all selected Current DLLs and
recheck asset availability before the native batch. Asset reads are exposed only
after successful activation and are hash checked again. Initialization failure
uses the existing retry/restart state machine; it must not publish a usable
delivery handle. Underlying resources are treated as a selected immutable store;
no mutable 'latest' asset lookup is permitted through the handle.

Do not hold all asset payloads in memory: verify sequentially, using an optional
streaming provider where available, and return verified bytes only on explicit
asset reads. Cache code metadata needed for consistent configuration. Retain the
existing IDheRuntimeAssetProvider boundary and C# cross-platform implementation.

Implement the manifest builder in package Editor tools so projects can package
their resource output and asset catalog without lab-specific scripts. Caller
supplies already built assets; automatic field/save migration is not inferred.

Gates: malformed/unsafe/duplicate paths, wrong target/code set/manifest digest,
missing/corrupt assets, mutation between prepare and load, stale handles, and
asset reads before successful load must fail before new native effects. Validate
the shared package source in managed tests, then new Windows Players with this
package (old Base117/118 lack the new bootstrap API), two distinct Base layouts,
identical delivery bytes, asset/new-type checks and recovery regressions. Preserve
old failure artifacts. No mobile or performance claim and no formal release.

Rollback: return to package ed4b7b5 and the code-only fixture; native runtime
remains b0fe826/ecad8a0. Changing selection after native publication requires a
process restart. A delivery rejection before publication must remain recoverable.
