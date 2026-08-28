param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [switch]$SkipAssembly,
    [switch]$AllowDirty,
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [string[]]$RequiredOpcode = @()
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

if (-not $SkipAssembly) {
    Write-Host "[correctness-gate] assemble runtime profile: $Profile"
    & (Join-Path $PSScriptRoot "assemble-runtime.ps1") `
        -LabRoot $LabRoot `
        -Profile $Profile `
        -AllowDirty:$AllowDirty `
        -HybridClrSource $HybridClrSource `
        -Il2CppPlusSource $Il2CppPlusSource
    if ($LASTEXITCODE -ne 0) { throw "Runtime assembly failed." }
    $SkipAssembly = $true
}

Write-Host "[correctness-gate] native C++ tests"
& (Join-Path $PSScriptRoot "run-native-tests.ps1") -LabRoot $LabRoot -Profile $Profile
if ($LASTEXITCODE -ne 0) { throw "Native C++ tests failed." }

Write-Host "[correctness-gate] .NET reference tests"
& (Join-Path $PSScriptRoot "run-reference.ps1") -Output "reports/reference-result.json"
if ($LASTEXITCODE -ne 0) { throw "Reference managed tests failed." }

Write-Host "[correctness-gate] Tuanjie Player profile: $Profile"
$buildParameters = @{
    LabRoot = $LabRoot
    Profile = $Profile
    SkipAssembly = $SkipAssembly
    AllowDirty = $AllowDirty
    HybridClrSource = $HybridClrSource
    Il2CppPlusSource = $Il2CppPlusSource
}
if ($RequiredOpcode.Count -gt 0) {
    if ($Profile -ne "Baseline-Instrumented") {
        throw "RequiredOpcode needs the Baseline-Instrumented profile."
    }
    $buildParameters.InstrumentationOutput = "reports/$($Profile.ToLowerInvariant())-correctness-instrumentation.json"
}
& (Join-Path $PSScriptRoot "build-clean-baseline.ps1") @buildParameters
if ($LASTEXITCODE -ne 0) { throw "Tuanjie Player correctness tests failed." }
if ($RequiredOpcode.Count -gt 0) {
    & (Join-Path $PSScriptRoot "assert-instrumentation-opcodes.ps1") `
        -LabRoot $LabRoot `
        -ProfilePath $buildParameters.InstrumentationOutput `
        -RequiredOpcode $RequiredOpcode
    if ($LASTEXITCODE -ne 0) { throw "Required opcode execution gate failed." }
}

$reference = Get-Content -Raw (Join-Path $LabRoot "reports/reference-result.json") | ConvertFrom-Json
$playerPath = Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-player-result.json"
$player = Get-Content -Raw $playerPath | ConvertFrom-Json
$buildManifestPath = Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-build-manifest.json"
$buildManifest = Get-Content -Raw $buildManifestPath | ConvertFrom-Json
$goldenHash = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/test-golden.json") -Algorithm SHA256).Hash
if ($reference.goldenContractSha256 -ne $goldenHash -or
    $player.goldenContractSha256 -ne $buildManifest.testGoldenContractSha256 -or
    $buildManifest.testGoldenContractSha256 -ne $goldenHash) {
    throw "Correctness golden contract is not bound consistently to the reference, player, and build manifest."
}
$differentialPath = Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-differential-result.json"
$differential = Get-Content -Raw $differentialPath | ConvertFrom-Json
& (Join-Path $PSScriptRoot "summarize-coverage.ps1") -LabRoot $LabRoot -Manifest "manifests/test-manifest.json" -Reference "reports/reference-result.json" -Player "reports/$($Profile.ToLowerInvariant())-player-result.json" -Output "reports/$($Profile.ToLowerInvariant())-coverage-summary.json"
if ($LASTEXITCODE -ne 0) { throw "Coverage summary failed." }
Write-Host "[correctness-gate] native: passed"
Write-Host "[correctness-gate] managed reference: $($reference.summary.passed)/$($reference.summary.total)"
Write-Host "[correctness-gate] Tuanjie Player: $($player.summary.passed)/$($player.summary.total)"
Write-Host "[correctness-gate] differential differences: $($differential.summary.differences)"
