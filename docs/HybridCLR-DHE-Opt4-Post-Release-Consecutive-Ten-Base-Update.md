# HybridCLR DHE opt4 consecutive ten-Base update

## Scope

This isolated lab run proves that the protected ten-Base channel can publish a
second resource-only update after revision 4. It did not create, replace, or
retire a Base. The same ten original Players consumed one revision 5 resource
package and reached the same current managed version.

CAT was not modified. All four configured hot-update assemblies remained in the
DHE set; no BattleAOT exception was introduced. The test used the released C#
toolchain `HybridCLRDhe-0.1.32-opt4.17`, Package ID
`5cee1f8aeda8e23576572a63ec0a1fd2a68104f327b2296ec3b4c8a9c518c964`,
source commit `e5208f38dae4ee625da9ce0a0990d26af5d6f6f0`, and source tree
`f8e5cf2b0e8276f2b15351b8c2fcbdbd55dcace1`.

## Protected input

Revision 5 was built from channel snapshot SHA-256
`2f1b45cf5f1301f2cce778cd92140dca5480e464d7f585f62c29861cf7b78fbc`.
It bound revision 4 head
`d6fce4a2727d2a4b7f8413965306c17c36cfc110c347f41a0e4e5b5685d23a25`
and parent ledger
`9799b299f9d866ca13d176cf1b786d32dbc225d02062f97a4e4dbf5d08c420f5`.

The build reused registry revision 3 byte-for-byte. Its SHA-256 remained
`4dd2a9b969609f85c94c2faa480c4afebf4f4194b862a46e6817423cc7df24d4`,
with 10 active and zero retired Bases. The direct revision 2 parent registry
was supplied so lineage was revalidated rather than inferred.

## Consecutive current generation

The `current-next` fixture was applied independently to the authenticated
revision 4 Windows and Android current roots. In each variant,
`HybridCLR.ManagedCasesAot.dll` changed while these assemblies remained
byte-for-byte equal to revision 4:

- `HybridCLR.ManagedCases.dll`;
- `HybridCLR.MetadataStress.dll`; and
- `HybridCLR.CrossAssemblyDerived.dll`.

The revision 5 current assembly-set SHA-256 values are:

| Payload variant | Current assembly-set SHA-256 |
|---|---|
| `windows` | `a1716f620b696868cf3d8407a59ef7f258df1c485ec8e4d9e4167fc289dd8b3b` |
| `android` | `48a04c09e231b2a7c53d332bfd4d0ec6cfa6667f7683d469807708b70becc9da` |

The combined payload variant-set SHA-256 is
`5e1e880f02e3ed8d34cff6f3bd23da7fa7eef10c68003dc2ba7d799d7d7190b3`.
The release build reported `registryDisposition=reused`,
`addedBaseCount=0`, exact ten-Base coverage, and
`unsupportedChangeCount=0`.

## Player results

| Player | Workflow | Variant | Changed | Interpreter | AOT |
|---|---|---|---:|---:|---:|
| `u21` | Unity 2021 | `windows` | 29 | 10 | 37 |
| `u22` | Unity 2022 | `android` | 29 | 10 | 37 |
| `tuanjie` | Tuanjie 2022 | `windows` | 29 | 10 | 37 |
| `u22-legacy` | Unity 2022 | `android` | 29 | 10 | 37 |
| `u22-base2` | Unity 2022 | `windows` | 29 | 10 | 37 |
| `u21-new` | Unity 2021 | `android` | 3 | 3 | 60 |
| `tuanjie-new` | Tuanjie 2022 | `windows` | 29 | 10 | 37 |
| `fresh-u21` | Unity 2021 | `windows` | 29 | 20 | 60 |
| `fresh-u22` | Unity 2022 | `windows` | 29 | 20 | 60 |
| `fresh-tuanjie` | Tuanjie 2022 | `windows` | 29 | 20 | 60 |

All ten Players passed resource selection, current/base/MV hash checks,
multi-assembly execution, direct and reflection paths, transaction rollback,
and same-process retry. The different changed-method counts demonstrate that
one current payload is evaluated against each Player's own embedded Base rather
than against one globally assumed Base.

## Release identity and qualification

The immutable revision 5 identities are:

- release-build config SHA-256:
  `44cb0eacaf1a8123d8376386779f5bdae383dc40d8beda14459bd1302ba2c38a`;
- release-build report SHA-256:
  `d5fc2ebe8d8cb1fc7b1f8c97d6743712fc23755a909b184b4a9354727a193215`;
- resource manifest SHA-256:
  `da6433be30cff1b521206effcb6ddfd054249608a9c87ebb6f1ebd9afbedfc6a`;
- compatibility validation SHA-256:
  `96c839476d299618fc8c88577454b7d542b157aaf960443cf6669a6a6acd0d52`;
- runtime plan SHA-256:
  `ea3b819699baaa8f9d56d26ceb5f04c169dd210a1d73387fe95d81d530e9a380`;
  and
- revision 5 ledger SHA-256:
  `3281696217b977644c423402d52c6d4b151f939315c9716f6deefddf7fbd926d`.

The aggregate gate revalidated all ten Base reports, the complete three-engine
matrix, the current package, and four authenticated historical packages. It
reported `releaseReady=true` and `exactActiveBaseCoverage=true`; its SHA-256 is
`be2553b1133c4681516c0a0bd875a38584de89b645c7d2390453ffa04ece0b4e`.

The complete revision 5 artifact schema gate SHA-256 is
`c326f631f7a08c3619dd299bc2f02d8ad00c1a29bb7f21fe8a3bf183ba09e127`.
Package verify and doctor also passed, with report SHA-256 values
`6bda49fce2036d9bcdb69f3b1ad0adf85fb279c0f2a0b1c93a3021ba8fc1a4e2`
and
`c746f7b6965d231e1cef4ac660729c8f8368d0ffaf56fc44b87a05635924785b`.

## Promotion and conclusion

CAS promotion regenerated the aggregate gate and advanced the isolated channel
from revision 4 to revision 5. The new channel-head SHA-256 is
`d77938768d7061317a4ed86382adc10b50d57a71231df48f7c4031a537647dc8`;
the published artifact tree SHA-256 is
`9d87f955deff0ee4e888fc9eef9aeb27788bb7cae40cca099ef882fb133fa4ae`.
The promoted and recovered-current snapshot SHA-256 values are
`2bd13c603d98659b67ca3aace0f43da3c3c45cb9caf7f116bfed1a8ad0670ef4`
and
`b7f6b38cd333cf019e161715cd0c5d7d429f2313a24084c2ba53567e1cab2214`.

This proves the ten-Base workflow is not a one-shot onboarding result: an
already updated protected channel can build, qualify, and atomically publish a
later resource-only revision without rebuilding any Base Player. It remains a
Windows lab proof. Android ARM64 and iOS device correctness, macOS filesystem
behavior, Xcode generation/link/signing, PSS/RSS, tail latency, temperature,
weak-core performance, and CAT-specific validation remain pending. Unsupported
layout, vtable, ABI, and GC-sensitive changes continue to fail closed and still
require a new Base.
