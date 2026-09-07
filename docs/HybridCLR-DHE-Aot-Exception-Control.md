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

## Compiler experiment

The ordinary Unity 2021 AOT control reproduces both differences. Enabling
divide checks repairs division but leaves scalar unboxing omitted. The next
lab-only experiment uses dnlib to change Code.Pop emission to evaluate the
discarded expression as `(void)(expression);`. It does not rewrite test IL,
golden, generated C++, native runtime source, or the installed Editor compiler.
The patcher requires an explicit input SHA, a new output, and the exact dispatch
shape; all other compiler method bodies and assembly references must remain
unchanged. Already patched or unknown inputs fail closed. No compiler binary
is distributed or added to source control.

This addresses the compiler's general discard behavior, not a test-name-based
special case. Actual conversion/Player tests must check pure values, reference
values, unboxing, exceptions and existing exception-handler stack pops. Default
and divide-check-only controls remain negative evidence. Native/compiler repair
integration and the full DHE/multi-Base replay are separate subsequent gates.
