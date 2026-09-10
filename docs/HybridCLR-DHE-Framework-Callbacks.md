# Framework callbacks using evolved hotfix payloads

Continue from the virtual-signature admission checkpoint. The next observable
scenario is framework code invoking a managed comparison, predicate, converter
or equality method with a hotfix reference/value type whose physical layout has
changed. Direct delegate invocation alone does not qualify these callers.

Use the existing Processor/Packet/ReferenceValue Base types and exact archived
Current as input. Compile an additional C# suite with the target Unity compiler,
preserve its original output and merge new test definitions into a fresh Current
copy. Keep the 46 business and 25 virtual-signature checks. Validate array/list
sorting, searching, predicates, conversion, iteration and custom collection
comparers, including exception propagation and preserved reference/value fields.

The first control introduces the callback implementations only in Current and
runs them on immutable old/grown Bases. This tests framework dispatch with evolved
payload storage; it does not prove a particular framework method was AOT compiled.
Inspect frozen execution selection and seed additional AOT Base workloads where
needed before making that stronger claim. A failure must preserve Current DLLs,
the complete partial sequence and original Player identity.

The initial fourteen checks pass on the three immutable Players. Their native
manifests contain the tested framework method definitions, but actual selected
execution still needs inspection. Extend the same workload with converters and
Task.Run targeting Processor methods already present in the AOT Bases, including
value/reference/generic frames and the grown receiver's exception method. This
tests existing AOT method targets without rebuilding the Players. Preserve the
fourteen-check Current and produce a separate eighteen-check Current copy.

Correctness is the main metric. No lost fields, bad receiver access or differing
case results are acceptable. Keep unaffected AOT execution and existing ABI
guards; do not globally interpret unchanged assemblies. This checkpoint makes no
performance or memory claim. Use Unity 2022.3.62f3 Windows only; do not change CAT,
Installer defaults, formal branches/tags or the ordinary AOT source inputs.

Rollback uses the matching sources and compatible resources from the preceding
virtual-signature/admission checkpoints. Broader native callback ABI, declaration
and parent changes, Scene/Prefab, old-object semantics, concurrent publication
and performance remain part of full DHE, not exclusions from the goal.
