# Generic and array reference identity probe

Extend the fourteen-check Current-only reference probe with eighteen independent
checks using an existing evolving MonoBehaviour as the reference argument. Cover
the existing hotfix GenericOwner<T>, ordinary List<T>, nested List<List<T>>, array
Type identity, IsInstanceOfType, reflection construction/field/method operations,
array reflection, unchanged generic invariance and reference array covariance.
Four managed worker threads repeat Type identity comparisons; Unity operations
remain on the main thread. Log each failure and retain the complete sequence.

`unity-reference-current ... generic` appends the optional native probe to the
unchanged latest Current without rebuilding business DLLs from Base input.
`unity-serialization-replay ... reference-generic` verifies all eighteen records
and the complete 46-case sequence, retaining failed results and immutable hashes.
The CLR reference skips the separately non-inlined native body. This is not yet
a passing result or generic/value-argument qualification. Run the same Current
on original/evolved immutable Bases and keep the ordinary fourteen-check and
serialization/lifecycle gates independent.
