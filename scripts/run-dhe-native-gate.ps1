param(
    [string]$LabRoot = "",
    [ValidateSet("DHE-Tuanjie2022", "DHE-Unity2022", "DHE-Unity2021")]
    [string]$Profile = "DHE-Tuanjie2022",
    [string]$OutputRoot = "",
    [switch]$AllowSurrogateExternalHeaders,
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-native-gate/$Profile"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}

Assert-DheSafeOutputRoot -Path $OutputRoot
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
$null = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput
$logPath = Join-Path $OutputRoot "native-tests.log"
$reportPath = Join-Path $OutputRoot "native-gate-report.json"
$runtimeRoot = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$runtimeManifestPath = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"
$errors = New-Object System.Collections.Generic.List[string]
$nativeExitCode = 1

try {
    if (-not (Test-Path -LiteralPath $runtimeManifestPath -PathType Leaf)) {
        throw "Runtime manifest was not found: $runtimeManifestPath"
    }
    if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
        throw "Runtime source was not found: $runtimeRoot"
    }

    $runtimeManifest = Get-Content -Raw -LiteralPath $runtimeManifestPath | ConvertFrom-Json
    if ($null -eq $runtimeManifest.PSObject.Properties["stagedRuntimeSha256"] -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals(
            (Get-TreeHash $runtimeRoot), [string]$runtimeManifest.stagedRuntimeSha256)) {
        throw "Runtime tree does not match its manifest: $runtimeRoot"
    }
    if ($null -eq $runtimeManifest.PSObject.Properties["externalHeaders"] -or
        $null -eq $runtimeManifest.externalHeaders -or
        $null -eq $runtimeManifest.externalHeaders.PSObject.Properties["surrogate"] -or
        $runtimeManifest.externalHeaders.surrogate -isnot [bool]) {
        throw "DHE runtime manifest is missing a strict externalHeaders.surrogate provenance boolean."
    }
    $externalRoot = Join-Path ([IO.Path]::GetDirectoryName($runtimeRoot)) "external"
    if (-not (Test-Path -LiteralPath $externalRoot -PathType Container)) {
        throw "DHE staged external headers were not found: $externalRoot"
    }
    $externalHashProperty = $runtimeManifest.externalHeaders.PSObject.Properties["stagedTreeSha256"]
    if ($null -eq $externalHashProperty -or [string]$externalHashProperty.Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "DHE runtime manifest is missing a valid externalHeaders.stagedTreeSha256 value."
    }
    $externalTreeHash = Get-TreeHash $externalRoot
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($externalTreeHash, [string]$externalHashProperty.Value)) {
        throw "Staged external headers do not match runtime manifest: $externalRoot"
    }
    if ([bool]$runtimeManifest.externalHeaders.surrogate -and
        -not $AllowSurrogateExternalHeaders) {
        throw "DHE native gate refuses surrogate external headers by default. Pass -AllowSurrogateExternalHeaders only for exploratory validation."
    }

    $scriptHost = Resolve-DhePowerShellHost
    $nativeArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
        (Join-Path $PSScriptRoot "run-native-tests.ps1"),
        "-LabRoot", $LabRoot,
        "-Profile", $Profile
    )
    if ($AllowSurrogateExternalHeaders) {
        $nativeArgs += "-AllowSurrogateExternalHeaders"
    }
    & $scriptHost @nativeArgs *> $logPath
    $nativeExitCode = [int]$LASTEXITCODE
    if ($nativeExitCode -ne 0) {
        $errors.Add("Native compile/test command exited with code $nativeExitCode. See $logPath")
    }
} catch {
    $errors.Add($_.Exception.Message)
}

$runtimeHash = if (Test-Path -LiteralPath $runtimeRoot -PathType Container) {
    Get-TreeHash $runtimeRoot
} else { $null }
$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-native-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0 -and $nativeExitCode -eq 0
    profile = $Profile
    runtimeRoot = $runtimeRoot
    runtimeManifest = $runtimeManifestPath
    runtimeTreeSha256 = $runtimeHash
    externalTreeSha256 = if (Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($runtimeRoot)) "external") -PathType Container) {
        Get-TreeHash (Join-Path ([IO.Path]::GetDirectoryName($runtimeRoot)) "external")
    } else { $null }
    nativeExitCode = $nativeExitCode
    surrogateHeadersAllowed = [bool]$AllowSurrogateExternalHeaders
    log = $logPath
    errors = $errors.ToArray()
}
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE native gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE native gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host "DHE native compile/test passed."
exit 0
