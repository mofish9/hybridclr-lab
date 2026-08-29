[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineRoot,

    [Parameter(Mandatory = $true)]
    [string]$CurrentRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string[]]$AssemblyNames,

    [string]$AssemblyListFile,

    [string]$SettingsFile,

    [string]$ProjectRoot,

    [string]$PackageLockPath,

    [string]$DnlibPath,

    [switch]$StrictCompatibility,

    [switch]$FailOnIncompatible,

    [switch]$RequireDheEqualsHotUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$resolvedHotUpdateAssemblies = @()
$resolvedDheAssemblies = @()
$resolvedSettingsProjectRoot = ""
$configurationErrors = New-Object System.Collections.Generic.List[string]

function Resolve-Root([string]$Path, [string]$Description) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not [IO.Directory]::Exists($resolved)) {
        throw "$Description was not found: $resolved"
    }
    return $resolved
}

function Resolve-AssemblyPath([string]$Root, [string]$AssemblyName) {
    $name = if ($AssemblyName.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)) {
        $AssemblyName
    } else {
        "$AssemblyName.dll"
    }
    return Join-Path $Root $name
}

function Assert-SafeAssemblyName([string]$AssemblyName) {
    if ([string]::IsNullOrWhiteSpace($AssemblyName) -or
        [IO.Path]::IsPathRooted($AssemblyName) -or
        $AssemblyName.Contains('/') -or $AssemblyName.Contains('\') -or
        $AssemblyName.Contains('..') -or
        [IO.Path]::GetFileName($AssemblyName) -ne $AssemblyName) {
        throw "Assembly name must be a simple file name without path traversal: '$AssemblyName'."
    }
}

$baselineRootPath = Resolve-Root $BaselineRoot "Baseline root"
$currentRootPath = Resolve-Root $CurrentRoot "Current root"
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
Assert-DheSafeOutputRoot -Path $outputRootPath -ProtectedPaths @($baselineRootPath, $currentRootPath)
[IO.Directory]::CreateDirectory($outputRootPath) | Out-Null
# Remove outputs that this invocation owns so a deleted assembly or an
# interrupted previous run cannot be mistaken for current evidence.
foreach ($stalePattern in @("*.mv.json", "*.mv.bytes", "dhe-batch-summary.json")) {
    Get-ChildItem -LiteralPath $outputRootPath -File -Filter $stalePattern -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

if (($null -eq $AssemblyNames -or $AssemblyNames.Count -eq 0) -and
    [string]::IsNullOrWhiteSpace($AssemblyListFile) -and
    [string]::IsNullOrWhiteSpace($SettingsFile)) {
    throw "Pass -AssemblyNames, -AssemblyListFile, or -SettingsFile."
}
if (-not [string]::IsNullOrWhiteSpace($AssemblyListFile)) {
    $listPath = [IO.Path]::GetFullPath($AssemblyListFile)
    if (-not [IO.File]::Exists($listPath)) {
        throw "Assembly list was not found: $listPath"
    }
    $AssemblyNames = @($AssemblyNames + (Get-Content -LiteralPath $listPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 -and -not $_.StartsWith("#") }))
}
if (-not [string]::IsNullOrWhiteSpace($SettingsFile)) {
    $settingsPath = [IO.Path]::GetFullPath($SettingsFile)
    $settings = Resolve-DheSettingsAssemblySets -SettingsFile $settingsPath -ProjectRoot $ProjectRoot
    $resolvedSettingsProjectRoot = [string]$settings.projectRoot
    $resolvedHotUpdateAssemblies = @($settings.hotUpdateAssemblies)
    $resolvedDheAssemblies = @($settings.dheAotAssemblies)
    if ($RequireDheEqualsHotUpdate) {
        if ($resolvedHotUpdateAssemblies.Count -eq 0) {
            $configurationErrors.Add("RequireDheEqualsHotUpdate requires at least one hotUpdateAssemblies entry.")
        }
        $hotUpdateKey = @($resolvedHotUpdateAssemblies | Sort-Object) -join ","
        $dheKey = @($resolvedDheAssemblies | Sort-Object) -join ","
        if ($hotUpdateKey -ne $dheKey) {
            $configurationErrors.Add("dheAotAssemblies must exactly match hotUpdateAssemblies in release coverage mode. Expected [$hotUpdateKey], got [$dheKey].")
        }
    }
    $settingsSource = $resolvedDheAssemblies
    $AssemblyNames = @($AssemblyNames + $settingsSource)
    foreach ($duplicate in @($settings.hotUpdateDuplicates + $settings.dheAotDuplicates)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$duplicate)) {
            $configurationErrors.Add("HybridCLR settings contain duplicate assembly name '$duplicate'.")
        }
    }
}
if ($RequireDheEqualsHotUpdate -and [string]::IsNullOrWhiteSpace($SettingsFile)) {
    $configurationErrors.Add("RequireDheEqualsHotUpdate requires -SettingsFile so the complete hot-update assembly set can be verified.")
}
$AssemblyNames = @($AssemblyNames | ForEach-Object { $_.Trim() } |
    Where-Object { $_.Length -gt 0 } |
    Select-Object -Unique)
if ($AssemblyNames.Count -eq 0) {
    throw "No hot-update assemblies were resolved from the supplied inputs."
}
$dnlibProjectRoot = if (-not [string]::IsNullOrWhiteSpace($resolvedSettingsProjectRoot)) {
    $resolvedSettingsProjectRoot
} else { $ProjectRoot }
$resolvedDnlibPath = Resolve-DheDnlibPath -RequestedPath $DnlibPath -ProjectRoot $dnlibProjectRoot -PackageLockPath $PackageLockPath

$records = New-Object System.Collections.Generic.List[object]
$failOnIncompatibleDetected = $false
$generator = Join-Path $PSScriptRoot "generate-dhe-mv.ps1"
$scriptHost = Resolve-DhePowerShellHost
foreach ($assemblyName in $AssemblyNames) {
    $normalizedName = if ($assemblyName.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)) {
        $assemblyName.Substring(0, $assemblyName.Length - 4)
    } else {
        $assemblyName
    }
    Assert-SafeAssemblyName $normalizedName
    $baselinePath = Resolve-AssemblyPath $baselineRootPath $normalizedName
    $currentPath = Resolve-AssemblyPath $currentRootPath $normalizedName
    $jsonPath = Join-Path $outputRootPath "$normalizedName.mv.json"
    $binaryPath = Join-Path $outputRootPath "$normalizedName.mv.bytes"

    $record = [ordered]@{
        assemblyName = $normalizedName
        baseline = $baselinePath
        current = $currentPath
        report = $jsonPath
        binary = $null
        status = "missing"
        error = $null
    }

    # Never let a previous run satisfy this run after a child process fails.
    foreach ($stalePath in @($jsonPath, $binaryPath)) {
        if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }

    if (-not [IO.File]::Exists($baselinePath) -or -not [IO.File]::Exists($currentPath)) {
        $missing = @()
        if (-not [IO.File]::Exists($baselinePath)) { $missing += "baseline" }
        if (-not [IO.File]::Exists($currentPath)) { $missing += "current" }
        $record.error = "Missing $($missing -join ', ') assembly file."
        $records.Add($record)
        continue
    }

    try {
        $args = @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $generator,
            "-BaselineAssembly", $baselinePath,
            "-CurrentAssembly", $currentPath,
            "-Output", $jsonPath
        )
        $args += @("-DnlibPath", $resolvedDnlibPath)
        if ($StrictCompatibility) {
            $args += "-StrictCompatibility"
        }
        if ($StrictCompatibility) {
            $args += @("-BinaryOutput", $binaryPath)
        }

        $childError = $null
        $childExitCode = 0
        try {
            & $scriptHost @args
            $childExitCode = $LASTEXITCODE
        }
        catch {
            $childError = $_
            $childExitCode = 1
        }

        $reportedStatus = $null
        $reportedJson = $null
        if ([IO.File]::Exists($jsonPath)) {
            try {
                $reportedJson = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
                $reportedStatus = [string]$reportedJson.compatibility.status
            }
            catch {
                $record.status = "error"
                $record.error = "DHE MV generator produced invalid JSON: $($_.Exception.Message)"
            }
        }

        # Strict MV generation intentionally exits non-zero after writing a
        # complete `incompatible` report. Preserve that classification so a
        # batch report distinguishes an expected compatibility rejection from
        # a generator crash or malformed partial output.
        $expectedStrictRejection = $StrictCompatibility -and
            $childExitCode -ne 0 -and $reportedStatus -eq "incompatible"
        if ($null -eq $record.status -or [string]::IsNullOrWhiteSpace([string]$record.status) -or
            $record.status -eq "missing") {
            if ($null -eq $reportedJson) {
                $record.status = "error"
                $record.error = if ($null -ne $childError) {
                    $childError.Exception.Message
                } elseif ($childExitCode -ne 0) {
                    "DHE MV generator exited with code $childExitCode."
                } else {
                    "DHE MV generator did not produce a report."
                }
            } elseif ($reportedStatus -notin @("compatible", "incompatible") -or
                ($childExitCode -ne 0 -and -not $expectedStrictRejection)) {
                $record.status = "error"
                $record.error = if ($null -ne $childError) {
                    $childError.Exception.Message
                } elseif ($childExitCode -ne 0) {
                    "DHE MV generator exited with code $childExitCode."
                } else {
                    "DHE MV report has an invalid compatibility status."
                }
            } else {
                $record.status = $reportedStatus
            }
        }

        if ($null -eq $reportedJson -and [string]::IsNullOrWhiteSpace([string]$record.error)) {
            $record.status = "error"
            $record.error = "DHE MV generator did not produce a report."
        }
        if ($record.status -eq "compatible" -and $StrictCompatibility) {
            if ([IO.File]::Exists($binaryPath)) {
                $record.binary = $binaryPath
            } else {
                $record.status = "error"
                $record.error = "Strict MV generation did not produce a binary report."
            }
        } elseif ($record.status -eq "incompatible" -and $StrictCompatibility -and
            [IO.File]::Exists($binaryPath)) {
            $record.status = "error"
            $record.error = "Incompatible strict MV generation unexpectedly produced a binary report."
        }
        if ($FailOnIncompatible -and $record.status -ne "compatible") {
            $failOnIncompatibleDetected = $true
        }
    }
    catch {
        $record.status = "error"
        $record.error = $_.Exception.Message
        if ($FailOnIncompatible) {
            $failOnIncompatibleDetected = $true
        }
    }
    $records.Add($record)
}

$summary = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-lite.batch-report.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    baselineRoot = $baselineRootPath
    currentRoot = $currentRootPath
    strictCompatibility = [bool]$StrictCompatibility
    requireDheEqualsHotUpdate = [bool]$RequireDheEqualsHotUpdate
    configurationPassed = $configurationErrors.Count -eq 0
    configurationErrors = $configurationErrors.ToArray()
    hotUpdateAssemblies = @($resolvedHotUpdateAssemblies)
    dheAotAssemblies = @($resolvedDheAssemblies)
    assemblies = $records.ToArray()
    counts = [ordered]@{
        total = $records.Count
        compatible = @($records | Where-Object { $_.status -eq "compatible" }).Count
        incompatible = @($records | Where-Object { $_.status -eq "incompatible" }).Count
        missing = @($records | Where-Object { $_.status -eq "missing" }).Count
        error = @($records | Where-Object { $_.status -eq "error" }).Count
    }
}

$summaryPath = Join-Path $outputRootPath "dhe-batch-summary.json"
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host ("DHE batch: {0}/{1} compatible, {2} incompatible, {3} missing, {4} errors" -f
    $summary.counts.compatible,
    $summary.counts.total,
    $summary.counts.incompatible,
    $summary.counts.missing,
    $summary.counts.error)
Write-Host "DHE batch summary: $summaryPath"

if ($configurationErrors.Count -gt 0 -or ($FailOnIncompatible -and ($failOnIncompatibleDetected -or
    $summary.counts.incompatible -gt 0 -or $summary.counts.missing -gt 0 -or $summary.counts.error -gt 0))) {
    exit 2
}
exit 0
