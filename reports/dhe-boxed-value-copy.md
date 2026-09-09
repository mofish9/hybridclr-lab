# Boxed Base value adaptation candidate

Unity 2022 Windows probe proof-09 shows that a Payload boxed before DHE loading
cannot pass unbox.any in the selected frozen copy method afterward. The Base
object still has its original physical storage, while the Current method needs
a larger independent value. Widening the cast or copying Current-size bytes from
the original object would be incorrect.

Implement an explicit Base-to-Current value copy for interpreter unbox.any.
Bind retained fields by the same Base signature matching used during metadata
initialization; copy by their actual offsets, zero new/retyped fields and skip
removed fields. Recursively adapt nested values and compatible closed generic
storage. Preserve the original boxed allocation and object identity. No native
typed ABI exception is allowed.

This step does not solve unbox byref aliases, old arrays, or migration of changed
reference objects. A retained non-null reference whose physical type changes
must remain an explicit failure until the object-migration contract handles it.
These are remaining full-goal gates, not exclusions from final DHE support.

The mapping is completed under metadata preparation and read only after DHE
publication. Copies target the interpreter stack, not shared object memory;
the original object stays rooted as the instruction's argument. No cache of
unrooted object pointers or replacement of an existing allocation is introduced.

Required first regression: the same old boxed Payload retains Count=17 in both
the old box and its independent Current copy, with added fields defaulted.
Keep the complete core, nullable/AOT dispatch and generic/array/byref checks.
Extend reordered/nested/generic/reference-field and GC tests before broader
old-state qualification. No performance, memory or platform claim is implied.
