# Compiler method declarations: Unity 2022 Windows

## Result and limits

The previously rejected compiler-produced interface removal now passes real
Windows Players. Base-75 originally contains the two AOT interface methods;
after loading the unchanged failing Current DLLs, their retained public methods
are nonvirtual, callable and visible through both fresh and previously warmed
MethodInfo objects. Method identity and declaring type stay stable, and removed
native serialization callbacks do not execute. Base-76 originally has neither
callback method; it passes the same Current, including cached type enumeration
of the newly added nonvirtual methods. Its absent Base methods are not counted
as successful cached-method evolution tests.

Both new Bases and the older Base-73 pass the identical compiler-removal Current
and a separate identical callback-retaining Current. These are two resources,
each shared by three immutable Players. Base-73 keeps its old native runtime;
the compiler-removal resource adds methods there and does not require the new
declaration capability. Base-74, whose existing methods need that capability,
is correctly rejected. No old Player or capability inventory was patched.

This is conditional Unity 2022 Windows correctness for this compiler transition,
not completion of the full DHE goal or a production release. The reverse
nonvirtual-to-existing-interface-slot transition and simultaneous parameter
default changes pass policy tests but still need explicit Player fixtures.
The retained callback control adds previously absent methods on Base-76/73;
it is not that reverse-transition proof. Broader declarations, inherited virtual
reflection combinations, parents, generic/value hierarchies, scene/Prefab and
pre-existing-object evolution, native boundaries and performance/memory remain
required work. There is no Android/iOS, new Unity 2021 or Tuanjie qualification.

## Committed source and native gate

| Component | Commit |
| --- | --- |
| HybridCLR, unchanged from hierarchy candidate | c16abc97f4eecc96dfab2ef0eb8d1c812281cb26 |
| Unity 2022 IL2CPP | 4314160ceded62b9b9efc47da5ce4c2f44418044 |
| Package | 8e076ff4f583c15e266ecee2d58c6bd9c29e8c48 |
| Lab policy reproduction | d44237a |
| Lab policy/Player implementation, host04/tool01 build | d6bacfe |
| Lab source locks, runtime assembly | bf04e10 |
| Lab MonoMethodInfo native gate, both Base builds and replays | 58a731932f11b21cda8164e8825784af3c693a0f |

I/P/Lab use independent research/dhe-method-declarations-v8.13.0 worktrees.
HybridCLR remains on the reference-hierarchy worktree. No formal branch/tag,
remote, Installer default or CAT files changed. MV remains DHEMETA1/schema 1
and the runtime contract remains dhe-runtime-v32.

IL2CPP public attributes and implementation flags now use the existing
GetDheCurrentMethodMetadata lookup. GetBaseDefinition checks the Current
declaration before following virtual parents. Method enumeration uses Current
declaration flags/slots while returning stable logical method handles. The
existing publication acquire and immutable metadata maps remain in use; no Base
MethodInfo is overwritten and physical receiver/ABI checks remain strict.

Admission adds ignored-in-JSON facts that distinguish only the supported
virtual/final/newslot transition during physical reference-interface evolution.
Access, implementation, abstract/PInvoke, generic, parent and unrelated metadata
changes keep their gates. The new capability is
current-implicit-interface-method-declarations-v1. Editor identities derive it
from the package's single runtime inventory.

Runtime: C:/hybridclr_optimize/artifacts/dhe-method-declarations-runtime-01/DHE-Unity2022/runtime-manifest.json.
Manifest SHA: 076995F89A513F3748D4ED43DE0B72D61C5973F3CD671FEFF61A627830D4E06B.
Native gate: C:/hybridclr_optimize/artifacts/dhe-method-declarations-native-01/DHE-Unity2022/native-gate.json.
It passes real Unity 2022.3.62f3 headers, compile and CTest, including the newly
included MonoMethodInfo translation unit: mergeReady=true,
surrogateExternalHeadersUsed=false. This is not a performance gate.

Canonical source trees:

- HybridCLR: B21D998EACDFC4AEAF81D05A070B9E5140481B05A5965D622637F5D6589D3841.
- IL2CPP: CB89C8D1810F7314135FD0144FD4A529A0C14AEFFDB39A6A8B16B0BF751D0E00.
- Package: 73AA8E9C771E4DA5B321DDB87C9AA1C2DCC43B917C9E10CC9E1D0EBF960874F2,
  excluding .git and the existing ignored Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta.

Tools under C:/hybridclr_optimize/artifacts:

- dhe-method-declarations-host-04/AotSnapshotTests.dll:
  7869E02B5696AC0B036586DFFEF2EBB0D04B88CD414FE17A753912EA0C682539.
- dhe-method-declarations-tool-01/HybridCLR.DheTool.dll:
  8467EA36237B8DBE506E01874E241141E2F89F49902A8DAD00BE89D55AEDE108.

## Base and shared-resource identities

New Base roots are C:/hybridclr_optimize/artifacts/dhe-method-declarations-base-75
and -76. Both pass original startup, generated no-op, identity schema and complete
ordinary AOT guard coverage. Their inputs are respectively
D:/hybridclr_artifacts/dhe-reference-hierarchy-interface-base-input-01 and
D:/hybridclr_artifacts/dhe-unity-input-base-04, expected Base revision 59.

| Base | Base ID | GameAssembly SHA-256 |
| --- | --- | --- |
| 75, interface and methods in AOT | 6ee82fce3763c74265075b73ce96e888b8227098708f13373b3028c0f23b12be | 1C2E0C7681154C235824BC371ACD62324BFC74BF91F6B6422FECA41799D4C588 |
| 76, no callback methods in AOT | ff6608dca86ce92a1dd89da7a0654f29ca2f6fa470e6a0694958bcbed5bc369b | 3DEEF52DEF5E56A8C8262FBB3CDC2C4F82A1B583813164AF46482498330D7382 |
| 73, retained older runtime | 012d6f2ea8b0da0e2325b24c886b19bd8747c859e1040e006ddb5496cf6331b3 | 1BB59EBD30A89170475C2B38FCAE74872B66C79AF72A7CAE84B6B92471128BE7 |

Base-73 is D:/hybridclr_artifacts/dhe-reference-hierarchy-base-73, bound to the
previous report's runtime02. The ordered final Base set is 75,76,73.

Final resources under D:/hybridclr_artifacts:

| Resource | Current-set SHA-256 | Resource manifest SHA-256 |
| --- | --- | --- |
| dhe-method-declarations-shared-resource-02 | 137a34b0c489ea92c26654c4ff3b21b72dfa8646cbbcaefa8bd45577f2dd740c | A5451FA54346BAA666B93B5800A06DE5855C2F4600091B03495B6223B56560D7 |
| dhe-method-declarations-retained-resource-01 | e62ce11e326987b906e16505b2a5d391585c021677173b4019993172efb3be24 | 63A4DE4E58999623D9944AF4D304AD4BB1109B58EF974FAD13438A7201707BDF |

Each passes all 46 reference business cases on all three Bases. Independent
dhe-method-declarations-shared-audit-02.json and
dhe-method-declarations-retained-audit-01.json each pass three Bases, three
successful runs and 167 rehashed files. Only Base-75 requires the new capability
for the removal resource; none require it for the retained resource.

## Explicit Player checks and recovery

For each of Base-75/76, the removal resource passes four replays at
D:/hybridclr_artifacts/dhe-method-declarations-base<N>-<mode>-02:

| Mode | Exact successful checks |
| --- | --- |
| interface-remove-compiler | 16 interface/callback/method/field/GC checks |
| interface-remove-compiler-cached, Base-75 | 11 receivers + 9 declaration/cache checks + 16 interface checks |
| interface-remove-compiler-cached, Base-76 | 11 receivers + 4 fresh declaration checks + 1 absent-Base-method control + 16 interface checks |
| reference-generic-cached | 11 receivers + 18 generic/reference checks |
| full-unity-cached | 11 receivers + 11 serialization + 17 lifecycle checks |

There are eight removal-resource replays. The retained resource also passes six
replays at dhe-method-declarations-base<75|76|73>-retained-reference-callbacks[-cached]-01:
nine positive native/direct callback checks per run, with eleven receiver checks
for cached runs. They prove callback absence in the removal case is not caused
by globally disabling callback execution.

dhe-method-declarations-public-probes-02 passes 22 checks across all three Bases
and binds 138 files. It generates the fault from the exact Current using
:current:, preserves the native preparation exception, verifies no business
effects in failed processes and succeeds after restoring input in fresh
processes. Result SHA: ECFEC14AEC909AAC5106F4579E778AFF3A63A081C237DE3E40D387BA2502F25F.
Resource replacement here occurs between processes, not by unloading a committed
Current assembly set from a running process.

Preliminary C:/hybridclr_optimize/artifacts/dhe-method-declarations-shared-resource-01
uses Bases 75,73 and the same removal Current. Its independent audit passes two
Bases/117 files. Base-75 cold/cached/generic/lifecycle and Base-73 cold removal
replays pass there, as does public-probes-01 with 16 recovery checks. These are
separate preliminary evidence, not extra runs of the final three-Base resource.

## Policy regression, preserved failures and review

Under C:/hybridclr_optimize/artifacts, policy-before-01 (with the full
dhe-method-declarations- prefix) reproduces ten failures out of 26 checks before
the implementation. policy-01 passes all 26; interface-policy-01 passes the 26
existing interface-policy checks; managed-plan-01.json passes all 114 package,
execution-plan, source-binding and failure-state checks.

Both original MV hashes are identical before and after the parser changes:

- callback source MV: B3158153FF9F9BE6C7CBD7A689DD664B991DAABFDB3BB17B312C649CA60816A5.
- compiler removal MV: 2F562B7F0748B76C99C1AC1247EDA0E17564D34BB2277F94C94E34B10A34E5F4.

Preserve D:/hybridclr_artifacts/dhe-method-declarations-admission-before-01:
the old tool rejects the two existing method metadata changes. The new
C:/hybridclr_optimize/artifacts/dhe-method-declarations-old-base-rejection-01
instead rejects exactly base-missing-runtime-capability:current-implicit-interface-method-declarations-v1
on Base-74, before Player execution. The original compiler Current files remain
unchanged throughout.

Review found no remaining failure in this reproduced removal scenario. Public
method metadata stays separate from physical receiver validation, both Base
method presence cases are explicit, no MV hash was changed, and the new
capability prevents assigning unsupported semantics to older Players. The
untested declaration/hierarchy combinations listed above still constrain the
candidate; these results do not freeze the entire runtime as release-ready.

No files were deleted. After Base-75 completed, NTFS compression of its archived
project/Library and project/HybridCLRData retained all file contents and reduced
physical storage by 1,336,540,549 bytes (about 1.24 GiB). No build was running in
those directories. Base binaries, snapshots, source inputs, generated C++, logs
and failed artifacts remain available. No stash was created or removed.

Rollback selects the previous matched source/lock/package combination from
dhe-reference-interface-evolution-windows.md in separate worktrees. Existing
Players keep their embedded native runtime; restore only resources compatible
with each Base and restart the process. Do not move runtime tags or rewrite an
archived Base to install a native correction.
