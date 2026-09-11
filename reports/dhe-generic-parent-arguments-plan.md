# Generic parent argument evolution

Replace `Processor : GenericParent<Packet>` with
`Processor : GenericParent<ReferencePacket>`, where Packet is an existing value
type containing a reference and ReferencePacket is a new reference type. Keep
the ordinary AOT root and business virtual signatures fixed. This targets a
real parent layout/instantiation change in a Base which already compiled the
generic parent to AOT, plus earlier Bases sharing the identical Current.

Reuse the 29 generic parent probes with the argument type selected by a compiler
define; retain independent 46 business, 25 virtual and 18 framework probes.
First require a passing CLR reference from Unity-compiled inputs. Then exercise
resource admission, staging and immutable Player execution against Base104 and
earlier Bases. Do not relax admission or rewrite old Player identities to make
this pass. Any runtime failure requires diagnosis and an isolated runtime fix.

Correctness and exact identities are hard gates. No performance claim, CAT
integration or new platform is included. Preserve original Current/Players and
failed outputs. Rollback selects the preceding compatible resources and restarts
the Player; candidate producer rollback is `5b4a35c`. Native source changes, if
needed, require a new Base and real-header gate rather than altered old evidence.
