# Restore the approved Unity 2022 upstream baseline

The approved upstreams are package/HybridCLR 8.13.0 and IL2CPP v2022-8.11.0.
The earlier 658aa64 merge imported official v2022-8.14.0 (11251b9) without an
approved baseline change. Rolling back only the Unity package did not undo it.

Restore the full pre-upgrade DHE commit ecad8a0 to the existing formal
`optimize/unity2022-v8.11.0` line by fast-forward. Relative to the incorrect
release this removes only the Type.cpp nested-name parser change. Verify that
v2022-8.11.0 is an ancestor, v2022-8.14.0 is not, and the complete tree equals
ecad8a0. This retains every DHE fix present before the upstream merge.

Candidate acceptance: clean source assembly against locked repositories, real
Unity 2022 headers with native compile/CTest, new Windows Base + Current resource
update on the same Player, and project runtime installation/hash verification.
Old 8.14-based binaries/results remain historical evidence only. No new mobile,
performance or production qualification is inferred. Unity 2021/Tuanjie are not
changed by this restoration.

Publish the unused annotated runtime tag `v2022-8.11.0-opt5` on the formal line;
do not move/reuse the published 8.14 tag. Stop selecting that superseded tag in
the package, bundled workflow policy, current release locks and project source
lock. Package distribution continues by maintenance commit, not package tag.

Rebuild the package-owned tool distribution because its authenticated workflow
policy embeds the runtime identity. Preserve the recent repository-settings,
Release-evidence, output-protection and configuration-precedence fixes. Keep all
addresses in project settings and explicit source identities in release locks.

Recovery is a whole source/runtime/artifact identity change; never relabel an
8.14-built Player as an 8.11 build. Save the previous project package/runtime
before replacing them. Keep the old wrong release distinguishable for audit,
not as a recommended or default build source.
