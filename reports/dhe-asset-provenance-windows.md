# DHE asset provenance and multi-Base validation (Unity 2022 Windows)

Package `research/dhe-asset-provenance-v8.13.0` commit
`2345b3415fea168f90e97e963cefdaae1699971b`; canonical tree SHA-256
`1FC7CFE293B362238AE6709E5017421DD524F6D784BAA5716BD41AA446DA1DBF`.
Lab commit: `49535b01fcd3c8dd30b3a107bff815bc0c69f088`. Unity `2022.3.62f3`
Windows x64, FGS workflow.

The real Unity asset authoring workflow completed in fresh output
`F:/hybridclr_artifacts/dhe-asset-provenance/authoring-03`. The provenance suite
passed 16/16 checks, including method-only schema changes allowed, serialized
schema changes rejected, wrong Current/target/inventory rejected, changed assets
rejected, and builder rejection before output. The delivery managed suite passed
49 checks. The resulting delivery replayed successfully on Base119 and Base120:
each passed 46 business checks and 42 asset checks, including new serializable
class, array, SerializeReference, cloning and post-GC checks. Missing assets and
wrong manifest hashes were rejected during prepare with no native effects.

Delivery manifest hash: `5BEB0AC57F336856D83CF40EBCCD6F18E3C750ED9D0821A2EC4853A6023170DB`.
Authoring result SHA-256: `33BB6B70037795CE99E1F5E7C4D5682652957430B6E1BE043B290AAC016224D6`.
Managed result SHA-256: `4E4110C7BD24CCB7B7EFEADFEE45DF262B47F037CA0F62E3A69AEFBA86EC65CA`.
Player result SHA-256: `3EFF3DB4A9305518B3B19C368C250C7F5AA5B475BB0727D4C1A2981DAEE73C24`.

This is conditional Unity 2022 Windows evidence. Android/iOS/ARM64, performance,
memory benefit, automatic migration and all serialization features remain unproven.
The package now exposes the explicit capability token
`public-assembly-image-resolution-v1`, and the build pipeline requires the native
header macro `HYBRIDCLR_DHE_HAS_PUBLIC_ASSEMBLY_IMAGE`. This token has not yet been
validated by a newly rebuilt Player identity; existing Bases must not be relabeled
until that gate passes.

## Capability Base checkpoint

A fresh Base Player was built at
`F:/hybridclr_artifacts/dhe-asset-provenance/new-base-05`. Its
`base/build-identity.json` contains `public-assembly-image-resolution-v1` and
has Base ID `f3ab99dfacffeb2f50a1259b6123bf7a41a8b0b719c2e62e18c8559139292fc1`.
The first direct delivery attempt used a resource generated for Base119 and was
correctly rejected during prepare because the Base identity did not match. A
second attempt copied the Player to a fresh directory to avoid a stale Windows
file lock; it reached the same prepare validation and was rejected for the same
reason. This is a valid negative gate, not a successful delivery result. A
resource must be generated from the new Base's own snapshot before claiming the
new capability Base can consume a delivery.

## Three-Base delivery result

That resource was subsequently generated from the new Base snapshot and replayed
with one delivery directory at
`F:/hybridclr_artifacts/dhe-asset-provenance/three-base-delivery-01`.
The workflow passed 33 checks across the new capability Base, Base119 and Base120:
each valid run completed 46 business and 42 asset checks; missing-asset and
wrong-manifest-hash variants were rejected before native effects; all Player and
delivery files remained immutable. Delivery manifest SHA-256 is
`904F4624C27F502757B87561F90F70F4752F268131472AF08134FA7BBEDF8910`.
This is the strongest current Windows evidence for one Current payload serving
multiple Base identities. It remains a candidate result pending final performance,
memory, ARM64 and release gates.
