[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Prepare", "Player")]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeSource,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [ValidateSet("StandaloneWindows64")]
    [string]$Target = "StandaloneWindows64",
    [string]$Mode = "Exploratory",
    [string]$ProjectPlan = "",
    [string]$ProjectPlanValidation = "",
    [string]$BatchReport = "",
    [string]$SourcePreflight = "",
    [string]$CleanCheckoutGate = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$labRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $labRoot "scripts/dhe-workflow-common.ps1")

if ($Action -eq "Prepare") {
    # Build the fixture inputs from source on every run. Keeping them under
    # this invocation's output makes the adapter contract reproducible from a
    # clean checkout and prevents stale evidence from being reused silently.
    $outputPath = [IO.Path]::GetFullPath($OutputRoot)
    Assert-DheSafeOutputRoot -Path $outputPath -ProtectedPaths @([IO.Path]::GetFullPath($ProjectPath))
    $baselineRoot = Join-Path $outputPath "adapter/stripped/baseline"
    $currentRoot = Join-Path $outputPath "adapter/stripped/current"
    if (Test-Path -LiteralPath $baselineRoot) { Remove-Item -LiteralPath $baselineRoot -Recurse -Force }
    if (Test-Path -LiteralPath $currentRoot) { Remove-Item -LiteralPath $currentRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $baselineRoot, $currentRoot | Out-Null

    $buildScript = Join-Path $labRoot "scripts/build-managed-cases.ps1"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File $buildScript `
        -LabRoot $labRoot -UnityProjectRoot ([IO.Path]::GetFullPath($ProjectPath)) -Target StandaloneWindows64
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture managed-case build failed."
    }
    $managedRoot = Join-Path $labRoot "artifacts/managed-cases/StandaloneWindows64"
    $aotRoot = Join-Path $managedRoot "Aot"
    $assemblyNames = @(
        "HybridCLR.ManagedCasesAot.dll",
        "HybridCLR.ManagedCases.dll",
        "HybridCLR.MetadataStress.dll",
        "HybridCLR.CrossAssemblyDerived.dll"
    )
    foreach ($assemblyName in $assemblyNames) {
        $sourcePath = if ($assemblyName -eq "HybridCLR.ManagedCasesAot.dll") {
            Join-Path $aotRoot $assemblyName
        } else {
            Join-Path $managedRoot $assemblyName
        }
        if (-not [IO.File]::Exists($sourcePath)) {
            throw "Fixture managed-case build did not produce $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $baselineRoot $assemblyName) -Force
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $currentRoot $assemblyName) -Force
    }

    $currentAotProject = Join-Path $labRoot "managed-cases/HybridCLR.ManagedCasesAot/HybridCLR.ManagedCasesAot.csproj"
    & dotnet build $currentAotProject --configuration Release --output $currentRoot --nologo -v:minimal `
        "-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK%3BDHE_CURRENT"
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture current managed-case build failed."
    }
    if (-not [IO.File]::Exists((Join-Path $currentRoot "HybridCLR.ManagedCasesAot.dll"))) {
        throw "Fixture current managed-case build did not produce HybridCLR.ManagedCasesAot.dll"
    }
    $preparePath = Join-Path $OutputRoot "adapter/prepare.json"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preparePath)) | Out-Null
    $prepare = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-project-adapter-prepare.json"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        passed = [IO.Directory]::Exists($baselineRoot) -and [IO.Directory]::Exists($currentRoot)
        target = $Target
        pathSemantics = "workspace-absolute-v1"
        projectPath = [IO.Path]::GetFullPath($ProjectPath)
        settingsFile = [IO.Path]::GetFullPath($SettingsFile)
        baselineRoot = $baselineRoot
        currentRoot = $currentRoot
        aotAssemblies = @()
        errors = @()
    }
    if (-not $prepare.passed) {
        $prepare.errors = @("Fixture baseline/current roots were not produced during Prepare.")
    }
    [IO.File]::WriteAllText($preparePath, ($prepare | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    if (-not $prepare.passed) { exit 1 }
    exit 0
}

# The preflight-only contract test never enters this action. Keep a strict
# failure here so a caller cannot mistake the fixture for Player evidence.
$workflowPath = Join-Path $OutputRoot "workflow-report.json"
$failure = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-project-adapter-fixture-failure.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $false
    error = "The fixture adapter does not implement a Player action."
}
[IO.File]::WriteAllText($workflowPath, ($failure | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
exit 1
