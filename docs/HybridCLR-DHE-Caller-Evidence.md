# Caller evidence across Base generations

The first v26 six-Base replay passes each Player workload, but its host rejects
the Integer/Reference/Value caller records on original and evolved Bases. These
methods and their declaring type do not exist in those Base DLL/MVs. Their
records are nativeChanged=0, AOT delta=1, DHE interpreter-entry delta=0.

Runtime source is authoritative: DheRuntime.cpp IsChangedMethod requires a
methodBaseTokens entry and a changed Base token. It returns false for a newly
interpreted method with no Base token. RecordInterpreterEntry counts DHE guard/
bridge entries, not every interpreter-to-interpreter call. Treating either
value as a total interpreter method count is incorrect.

Keep the frozen DLL/MV payloads. Bind each named caller to Current metadata and
Base membership before interpreting its receipt. An existing unchanged caller
must still report nativeChanged=false and positive AOT entry evidence.
A newly added caller must have no Base method/declaring type; its successful
typed invocation and checked result come from the same immutable Current probe.
It cannot claim a changed Base guard. Do not count its diagnostic AOT delta as
proof that this new method ran AOT, or its zero DHE entry delta as proof that
it did not execute in the interpreter.

The generic-interface Bases remain the required retained-AOT caller coverage.
Earlier generations cover added callers and their primitive/reference/struct
results. All 61 evolution groups and 220 differential cases stay required.
Use actual failed receipts in regression, retain every failed replay and rerun
all six Bases. No native/package/source payload change or new Base is needed.
This corrects test applicability; it does not establish performance or general
structural hot-update support.
