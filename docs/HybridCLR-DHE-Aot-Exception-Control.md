# Unregistered AOT exception control

## Question and boundaries

The v11 retained-AOT differential fails `divide_by_zero_catch` and
`invalid_cast_catch` while the same current IL passes in the interpreter and
CLR. Before changing DHE dispatch or the compiler, isolate registration and
generated guards from the AOT compiler's exception semantics.

Build a new Windows Player from an archived Base's exact managed inputs with
the installed, locked candidate runtime. Use the package's ordinary Base-input
binding and a clean Unity build, but no native finalizer or guard injection.
The `aot-exceptions` mode invokes already-loaded AOT methods through reflection;
it never loads resource DLLs, supplemental metadata, or DHE registrations.
This is an unregistered candidate-runtime AOT control, not an official-runtime
baseline and not the full 220-case DHE gate.

## Acceptance and evidence

- Record all four unchanged methods: NullReferenceCatch -> null,
  InvalidCastCatch -> cast, DivideByZeroCatch -> divide,
  IndexOutOfRangeCatch -> index. Differences must produce a failing report and
  nonzero Player exit code. Do not change the existing source bodies or golden.
- Bind source commits, exact archived/compiled DLLs, method body identities,
  Editor/compiler and native binary hashes to each result. Inspect generated
  C++ for the operations and absence of injected DHE guards.
- Use separate outputs for default compiler options and the supported
  `--enable-divide-by-zero-check` option. Restore the Editor setting afterward.
- Start with Unity 2021; apply the same control separately to Unity 2022 and
  Tuanjie if needed. Do not infer engine or ARM64 behavior from another profile.
- Correctness is the primary metric. These diagnostic builds support no
  throughput, startup, memory, or production-release claims.

The rollback boundary is the lab-only runner/build entry. Runtime/package
sources, archived Players, resources, failed evidence and Installer selection
remain unchanged. A compiler hypothesis is not a repair until the control and
the original cold differential have been executed.
