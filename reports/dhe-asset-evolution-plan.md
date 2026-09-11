# Asset evolution qualification

Public image identity fixes old-Base/latest-bundle loading (42 checks). An old
bundle still fails at the renamed nested Number field, even on an evolved Base
with no code change. First isolate this from DHE using the stock Unity 2022
IL2CPP runtime and the same asset declarations and immutable old/latest bundles.
Preserve original DLLs, bundles and failed Player evidence. Keep all 42 expected
checks; a matching failure is a diagnosis, not a migration pass.

The control project contains only the original UnityAssets declarations in the
same named assembly, plus an empty Factory type required by the shared driver.
Other business types and module initializers are removed from a copied DLL; no
DHE package, bootstrap or custom runtime is installed. Record the extracted
assembly, source, engine, runtime and Player identities. Verify the latest bundle
first, so a general control-fixture failure cannot explain an old-bundle failure.

Then qualify newly added serialized types and implement coherent code/asset
selection in the package workflow. Reuse capable immutable Bases where possible.
All large outputs go to F:, Windows only; no mobile or production claim.
