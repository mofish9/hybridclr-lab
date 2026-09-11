# Public DHE assembly image for native serializers

Base116's paired native traces have equal serialization flags, non-generic and
non-byref classes, and equal negative Delegate/UnityEngine.Object/subclass tests.
After class_get_image, no-op proceeds to callback discovery and field traversal;
Current stops. The Current image has the same name but is the hidden metadata
image. Assembly.cpp already gives that hidden image the public Base assembly,
whose registered image is different.

Hypothesis: Unity's serialization eligibility cache indexes registered assembly
images. Give the public C API class_get_image the already-published assembly's
registered image, keeping klass->image and all internal metadata/attribute readers
physical. Only a published DHE assembly is eligible. Do not rewrite image pointers,
change token/offset data, expose hidden assemblies or map ordinary AOT assemblies.

Require native tests for null/unregistered/published image identity and unchanged
physical metadata, real-header compile/CTest, then a new uninstrumented Windows
Base with the exact existing Current and latest bundle inputs. Compare no-op,
same-resource old/new Bases, old bundle and latest bundle. Passing the first asset
gate does not qualify field renames, newly added serialized types, save migration,
preselection objects, concurrency or mobile platforms without their own checks.

Runtime rollback returns to HybridCLR 6180597 and IL2CPP 2ff64a8 for new builds;
existing Base115/116 diagnostics are immutable controls. No production release,
CAT integration, Unity 2021 or Tuanjie work in this experiment.
