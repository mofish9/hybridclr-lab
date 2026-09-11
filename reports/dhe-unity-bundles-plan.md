# Typed AssetBundle qualification

The preceding baked Resources/Scene experiment fails after a structural Current
update because its serialized files have no type trees. This round builds real
Prefab and scene AssetBundles with type trees enabled and inspects those files
using the package's UnityFS reader. A unique bundle-only scene path prevents a
test from accidentally loading the similarly authored baked scene.

Create old/evolved-layout Bases with AssetBundle loading APIs rooted in ordinary
AOT. Preserve both Base binaries and both bundle sets. First pass each Base's
no-op bundle. Then use one identical Current across both Bases with the old typed
bundle, and with the latest typed bundle. Check all existing asset assertions,
bundle provenance and immutable file identities. A bundle control passing on
the evolved Base does not qualify the old Base by itself.

Keep the baked failure; do not change expected values or silently rewrite its
assets. If typed old bundles still fail, diagnose the runtime; if only latest
bundles work, demonstrate a coherent code-and-asset delivery before defining
project integration. Package asset admission/loading and pre-selection objects
remain later workflow requirements, not waived goals.

Work starts from lab a306ccd, producer db4d17e, runtime 6180597/819f74c and package
ed4b7b5. Use F:/hybridclr_artifacts/dhe-unity-bundles for new artifacts. Unity
2022.3.62f3 Windows only; no CAT, formal release, Installer or mobile change.
Rollback uses compatible archived code/assets and restarts the Player. All tests
bind committed source and unique output paths; no old artifact is overwritten.

## Native type resolution experiment

Base111/112 no-op bundle probes pass 36/42 checks. With one Current, Base111
loses nested State with either old or latest typed bundles; Base112 plus the old
bundle does not restore the renamed nested field, while Base112/latest passes.
Thus preserving type trees alone does not qualify this workflow.

The C API maps System.Type to the selected physical reference class, but its
Il2CppType/name/element lookup APIs still return the original class. Hypothesis:
Unity's nested serialization traversal caches metadata inconsistent with the
selected reference allocation. An isolated Unity 2022 IL2CPP candidate applies
the same existing mapping at those four type-resolution boundaries. Internal
Class helpers, actual object class queries and receiver/ABI validation stay
strict. The mapping excludes value layouts and uses its existing publication
and metadata locks. Require real-header compile/CTest and a new immutable Base
against the same unchanged Current/bundles before accepting this hypothesis.
Old failing Players are controls and cannot acquire this behavior retroactively.
