# DHE mixed-call evidence validation

## Problem and boundary

An unchanged AOT entry can invoke changed or added interpreter methods.
The Windows generic-interface first payload proves this, but formal evidence
validators still require changedProbeChanged=true. A separate count bug uses
guardRequiredMethodCount as the total changed method count, omitting changed
bodyless interface declarations. The real latest Player reports 619 changed
methods; its native guard subset plus additions totals 618.

Accept the existing changed-probe proof or an executed entry receipt bound to
the selected Base and Current method versions, identity, presence and counters.
Authenticate DLLs/MVs against Base identity and the selected resource manifest.
Use changedMethodCount plus addedMethodCount for runtime counts; keep native
guard counts for native coverage. Do not relabel unchanged entries as changed.
No runtime ABI, layout, cache or synchronization changes are required.

Unity 2022 and Tuanjie 2022 Windows Players provide the actual payload cases.
Tests must reject missing/incorrect method identity, routing, Base membership
and execution counts. Existing rollback, AOT-path, payload, registry, ledger and
source checks remain required. Recheck cold Player behavior after binding the
tool changes; a passed helper test alone is not release qualification.

Keep recognized Unity 2021 registry IDs readable for historical inspection,
while qualification still requires only the two active 2022 engines.
Rollback reverts the tool validator and its package-layout entry together;
Base Players and MV schema are unchanged.
