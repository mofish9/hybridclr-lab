# HybridCLR Lab

Correctness, differential testing, and performance benchmarking for the
HybridCLR community runtime on Tuanjie 1.10.0.

## Fixed baseline

- Tuanjie: `1.10.0` (`2022.3.62t12`)
- HybridCLR package: `v8.13.0`
- HybridCLR runtime: `v8.13.0`
- il2cpp_plus: `v2022-tuanjie-8.13.0`

The runtime source repositories live beside this repository under `../repos`.
Their immutable inputs are recorded in `manifests/repo-lock.json`.

## Current phase

The current phase establishes the engine-independent correctness contract:

1. Build `managed-cases/HybridCLR.ManagedCases`.
2. Run the same case registry with the .NET reference runner.
3. Persist a schema-validated result before creating the Tuanjie Player host.

Run the reference suite from this repository root:

```powershell
./scripts/run-reference.ps1
```

Runtime optimization must not begin until the Clean Baseline Tuanjie Player
passes the managed differential suite.

