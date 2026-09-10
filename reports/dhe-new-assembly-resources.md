# New assemblies through frozen-source resource updates

Continue the Unity 2022 Windows standard-resource checkpoint on HybridCLR
`cdb2a5f`, IL2CPP `8a13baf` and package `187af4f`. Build immutable old-layout and
grown-layout Bases with the three original hotfix DLLs. A later shared Current
adds an interpreter-only DLL while those original assemblies retain DHE modes.
Ordinary AOT DLLs remain the captured Base bytes. No project-specific loader or
changes to generated source/archived Players may hide missing runtime support.

The fixture must accept an explicit expected Current assembly count, report the
public loader's differential/interpreter sets and actual loaded names, and use
the existing public loading APIs. Begin with differential plus frozen sources,
then load new interpreter assemblies; reproduce any metadata dependency or
ordering failure before changing the package/runtime protocol. New resource
tests must cover cross-assembly values/generics, new declaration dependencies,
virtual/interface calls and an ordinary frozen boundary, in addition to the
existing 23 C# cases. Require a CLR reference, exact case sequences, original
Base hashes, complete mode tables and one identical Current across both Bases.

Missing/corrupt new DLLs and attempts to reclassify original ordinary AOT code
must be rejected. Keep per-Base support assets and original snapshot source
authentication. Research sources must be committed before source-bound runs.
No performance, mobile, Tuanjie or production qualification follows from these
Windows correctness cases. Native sources are unchanged until a real failure
requires a correction; the prior real-header gate remains tied to runtime-04.
