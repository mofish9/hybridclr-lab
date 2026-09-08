# Unity batch log sharing

The archived `base-cross-virtual-tuanjie/project-workflow-failure.json` records
a sharing violation reading `unity-stage.log`. Its Editor log later records
a successful exit. The existing workflow already waits for Editor exit and
project release, but `File.ReadAllText` denies sharing with a remaining writer.
Do not infer which process owned the historical handle from that error alone.

Reproduce with a writable FileStream held open while the host reads its flushed
contents. A compatible reader must preserve all text, including batch failure
markers, while still propagating genuinely exclusive-access failures. Keep the
existing process exit, timeout, project-release and failure-marker checks.
Use C# only and change no runtime, engine hook, MV, Base identity or resource.

First run the fixture against the old read semantics and retain the failed
report. Then allow ReadWrite/Delete sharing and rerun the fixture, build and
authenticate a clean exploratory tool snapshot, and retry the actual Tuanjie
workflow in a new output directory. Existing Players and failed outputs remain
immutable. All three engines share the host code; only Windows is executable
here. This repair makes no performance or mobile compatibility claim.

The regression and subsequent Player reports bind their own source identities.
Rollback selects the previous matched host snapshot; no native rebuild is
required for already successful Bases. The broader DHE evolution goal remains
open regardless of this workflow repair.
