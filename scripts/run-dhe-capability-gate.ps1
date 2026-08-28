[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$project = Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCasesAot/HybridCLR.ManagedCasesAot.csproj"
$dnlibPath = Join-Path $LabRoot "unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-capability-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
Assert-DheSafeOutputRoot -Path $outputRoot
Assert-DheOutputNotAncestor -Path $outputRoot -Root $LabRoot
Assert-DheSafeOutputRoot -Path $outputRoot -ProtectedPaths @($project)
$null = Initialize-DheOutputRoot -Path $outputRoot -Force:$ForceOutput -ProtectedPaths @($project)
$baselineRoot = Join-Path $outputRoot "baseline"
$currentRoot = Join-Path $outputRoot "current"
New-Item -ItemType Directory -Force -Path $baselineRoot,$currentRoot | Out-Null

dotnet build $project --configuration $Configuration --output $baselineRoot --nologo -v:minimal `
    "-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $project --configuration $Configuration --output $currentRoot --nologo -v:minimal `
    "-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK%3BDHE_CURRENT"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$baselineDll = Join-Path $baselineRoot "HybridCLR.ManagedCasesAot.dll"
$currentDll = Join-Path $currentRoot "HybridCLR.ManagedCasesAot.dll"
$diffJson = Join-Path $outputRoot "HybridCLR.ManagedCasesAot.mv.json"
$diffBinary = Join-Path $outputRoot "HybridCLR.ManagedCasesAot.mv.bytes"
& (Join-Path $LabRoot "scripts/generate-dhe-mv.ps1") `
    -BaselineAssembly $baselineDll `
    -CurrentAssembly $currentDll `
    -DnlibPath $dnlibPath `
    -Output $diffJson `
    -BinaryOutput $diffBinary `
    -StrictCompatibility
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$mv = Get-Content -Raw -LiteralPath $diffJson | ConvertFrom-Json
$changed = @($mv.methods | Where-Object { $_.kind -ne "unchanged" })
$categories = [ordered]@{
    ordinary = @($changed | Where-Object {
        -not [bool]$_.isVirtual -and [uint32]$_.genericParameterCount -eq 0 -and
        -not [bool]$_.isAbstract -and -not [bool]$_.isPInvoke -and
        ([string]$_.declaringType -notmatch '/<.*>')
    }).Count
    virtual = @($changed | Where-Object { [bool]$_.isVirtual }).Count
    generic = @($changed | Where-Object { [uint32]$_.genericParameterCount -gt 0 }).Count
    stateMachineOrIterator = @($changed | Where-Object { [string]$_.declaringType -match '/<.*>' }).Count
    abstract = @($changed | Where-Object { [bool]$_.isAbstract }).Count
    pinvoke = @($changed | Where-Object { [bool]$_.isPInvoke }).Count
}
$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-capability-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $true
    baselineAssembly = $baselineDll
    currentAssembly = $currentDll
    mvJson = $diffJson
    mvBinary = $diffBinary
    compatibility = $mv.compatibility
    summary = $mv.summary
    changedMethodCategories = $categories
    changedMethods = $changed
    errors = @()
}
$reportPath = Join-Path $outputRoot "capability-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 14), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE capability gate: $reportPath"
Write-Host ("Changed methods: {0}; ordinary={1}, virtual={2}, generic={3}, state-machine/iterator={4}" -f `
    $changed.Count, $categories.ordinary, $categories.virtual, $categories.generic, $categories.stateMachineOrIterator)
exit 0
