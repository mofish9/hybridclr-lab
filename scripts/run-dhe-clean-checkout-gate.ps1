[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$ProjectPath = "",
    [string]$RuntimeSource = "",
    [string]$OutputRoot = "",
    [string]$PackageLockPath = "",
    [string]$IdentityTemplatePath = "",
    [string]$SourceBoundaryPath = "",
    [string]$GitRoot = "",
    [switch]$RequireIdentityTemplate,
    [switch]$RequireEmbeddedPackage,
    [switch]$RequireTrackedSources,
    [switch]$RequireGitClean,
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
$ProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    Join-Path $LabRoot "unity2021-dhe-demo"
} else {
    [IO.Path]::GetFullPath($ProjectPath)
}
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-clean-checkout-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}

Assert-DheSafeOutputRoot -Path $OutputRoot -ProtectedPaths @($ProjectPath, $RuntimeSource)
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
if (Test-Path -LiteralPath $OutputRoot) {
    if (-not $ForceOutput -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -gt 0) {
        throw "OutputRoot is not empty: $OutputRoot. Pass -ForceOutput to replace a prior run."
    }
    if ($ForceOutput) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$scriptHost = Resolve-DhePowerShellHost
$sourcePreflightScript = Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"
$packageLockPath = if ([string]::IsNullOrWhiteSpace($PackageLockPath)) {
    Join-Path $LabRoot "manifests/dhe-package-lock.json"
} else {
    [IO.Path]::GetFullPath($PackageLockPath)
}
$errors = New-Object System.Collections.Generic.List[string]

function Invoke-SourcePreflight([string[]]$Arguments) {
    # These invocations intentionally exercise failing preflight cases. Keep
    # their expected error text inside the machine-readable report instead of
    # leaking stderr that can be mistaken for a gate failure by CI wrappers.
    # Windows PowerShell 5.1 promotes a redirected child Write-Error into a
    # terminating NativeCommandError when the caller uses ErrorAction Stop;
    # temporarily use Continue so every negative case still emits its report.
    $callerErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $scriptHost -NoProfile -ExecutionPolicy Bypass -File $sourcePreflightScript @Arguments 2>&1 | Out-Null
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $callerErrorActionPreference
    }
}

function Read-Report([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json }
    catch { return $null }
}

function Read-StrictBoolean($Report, [string]$Name) {
    if ($null -eq $Report) { return $null }
    $property = $Report.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) { return $null }
    return [bool]$property.Value
}

function Test-PathWithinRoot([string]$Path, [string]$Root) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $resolvedPath.Equals($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

$cleanRoot = Join-Path $OutputRoot "clean-source"
$identityArguments = @()
if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
    $identityArguments += @("-IdentityTemplatePath", [IO.Path]::GetFullPath($IdentityTemplatePath))
}
if ($RequireIdentityTemplate) { $identityArguments += "-RequireIdentityTemplate" }
$packageArguments = if ($RequireEmbeddedPackage) {
    @("-PackageLockPath", $packageLockPath, "-RequireEmbeddedPackage")
} else {
    @()
}
$cleanArgs = @(
    "-LabRoot", $LabRoot,
    "-ProjectPath", $ProjectPath,
    "-OutputRoot", $cleanRoot
) + $packageArguments + $identityArguments
$cleanExit = Invoke-SourcePreflight $cleanArgs
$cleanReport = Read-Report (Join-Path $cleanRoot "source-preflight-report.json")
$cleanPassedValue = Read-StrictBoolean $cleanReport "passed"
$cleanRuntimeReadyValue = Read-StrictBoolean $cleanReport "runtimeReady"
$cleanPassed = $cleanExit -eq 0 -and $null -ne $cleanReport -and
    $null -ne $cleanPassedValue -and $cleanPassedValue -and
    $null -ne $cleanRuntimeReadyValue -and -not $cleanRuntimeReadyValue
if (-not $cleanPassed) { $errors.Add("Clean source preflight did not pass in source-only mode.") }

$gitTested = $false
$gitClean = $null
$trackedSourcesTested = $false
$trackedSourcesComplete = $null
$missingTrackedSources = New-Object System.Collections.Generic.List[string]
$gitRootPath = if ([string]::IsNullOrWhiteSpace($GitRoot)) { "" } else { [IO.Path]::GetFullPath($GitRoot) }
if ($RequireGitClean -or $RequireTrackedSources -or -not [string]::IsNullOrWhiteSpace($gitRootPath)) {
    $gitTested = $true
    if ([string]::IsNullOrWhiteSpace($gitRootPath)) {
        $gitRootPath = $LabRoot
    }
    if (-not (Test-Path -LiteralPath $gitRootPath -PathType Container)) {
        $errors.Add("Git source root was not found: $gitRootPath")
    } else {
        $gitTop = @(& git -C $gitRootPath rev-parse --show-toplevel 2>&1)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($gitTop -join "").Trim())) {
            $errors.Add("Git source root is not a repository: $gitRootPath")
        } else {
            $gitRootPath = [IO.Path]::GetFullPath(($gitTop -join "").Trim())
            if (-not (Test-PathWithinRoot -Path $ProjectPath -Root $gitRootPath)) {
                $errors.Add("DHE project path is outside the verified Git repository: project=$ProjectPath; gitRoot=$gitRootPath")
            }
            $gitStatus = @(& git -C $gitRootPath status --porcelain=v1 --untracked-files=all 2>&1)
            if ($LASTEXITCODE -ne 0) {
                $errors.Add("Unable to inspect Git source status: $gitRootPath")
            } else {
                $gitClean = $gitStatus.Count -eq 0
                if (-not $gitClean) {
                    $message = "Git source root has tracked or untracked changes: $gitRootPath"
                    if ($RequireGitClean) { $errors.Add($message) } else { Write-Warning $message }
                }

                if ($RequireTrackedSources) {
                    $trackedSourcesTested = $true
                    $boundaryPath = if ([string]::IsNullOrWhiteSpace($SourceBoundaryPath)) {
                        Join-Path $LabRoot "manifests/dhe-source-boundary.json"
                    } else {
                        [IO.Path]::GetFullPath($SourceBoundaryPath)
                    }
                    $boundary = $null
                    try {
                        $boundary = Get-Content -Raw -LiteralPath $boundaryPath | ConvertFrom-Json
                    } catch {
                        $errors.Add("Unable to read DHE source boundary manifest for tracked-source verification: $boundaryPath")
                    }
                    if ($null -ne $boundary) {
                        $boundaryFormat = if ($null -ne $boundary.PSObject.Properties["format"]) { [string]$boundary.format } else { "" }
                        $boundarySchema = if ($null -ne $boundary.PSObject.Properties["schemaVersion"]) { [int]$boundary.schemaVersion } else { 0 }
                        $boundaryExact = if ($null -ne $boundary.PSObject.Properties["exactPaths"]) { @($boundary.exactPaths) } else { @() }
                        $boundaryPrefixes = if ($null -ne $boundary.PSObject.Properties["prefixes"]) { @($boundary.prefixes) } else { @() }
                        if ($boundarySchema -ne 1 -or
                            $boundaryFormat -ne "hybridclr.dhe-source-boundary.json" -or
                            @($boundaryExact).Count -eq 0 -or @($boundaryPrefixes).Count -eq 0) {
                            $errors.Add("DHE source boundary manifest has an invalid schema, format, or empty exactPaths/prefixes: $boundaryPath")
                            $boundary = $null
                        } else {
                            $boundaryValues = @($boundaryExact + $boundaryPrefixes | ForEach-Object { ([string]$_).Replace('\', '/') })
                            $unsafeBoundaryValues = @($boundaryValues | Where-Object {
                                [string]::IsNullOrWhiteSpace($_) -or [IO.Path]::IsPathRooted($_) -or $_ -match '(^|/)\.\.(/|$)'
                            })
                            $duplicateBoundaryValues = @($boundaryValues | Group-Object | Where-Object { $_.Count -gt 1 })
                            if ($unsafeBoundaryValues.Count -gt 0 -or $duplicateBoundaryValues.Count -gt 0) {
                                $errors.Add("DHE source boundary manifest contains unsafe or duplicate paths: $boundaryPath")
                                $boundary = $null
                            }
                        }
                    }
                    if ($null -ne $boundary) {
                        $trackedFiles = @(& git -C $gitRootPath ls-files 2>&1)
                        if ($LASTEXITCODE -ne 0) {
                            $errors.Add("Unable to enumerate tracked source files: $gitRootPath")
                        } else {
                            $trackedSet = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
                            foreach ($trackedFile in @($trackedFiles)) {
                                if (-not [string]::IsNullOrWhiteSpace([string]$trackedFile)) {
                                    $null = $trackedSet.Add(([string]$trackedFile).Replace('\', '/'))
                                }
                            }
                            $boundaryFullPath = [IO.Path]::GetFullPath($boundaryPath)
                            if (-not (Test-PathWithinRoot -Path $boundaryFullPath -Root $gitRootPath)) {
                                $missingTrackedSources.Add("source-boundary-outside-git-root:$boundaryFullPath")
                            } else {
                                $boundaryRelativePath = $boundaryFullPath.Substring(
                                    $gitRootPath.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
                                if (-not $trackedSet.Contains($boundaryRelativePath)) {
                                    $missingTrackedSources.Add($boundaryRelativePath)
                                }
                            }
                            $exactSourcePaths = @($boundary.exactPaths | ForEach-Object { ([string]$_).Replace('\', '/') })
                            foreach ($requiredPath in $exactSourcePaths) {
                                if (-not $trackedSet.Contains($requiredPath)) {
                                    $missingTrackedSources.Add($requiredPath)
                                }
                            }
                            foreach ($requiredPrefix in @($boundary.prefixes | ForEach-Object { ([string]$_).Replace('\', '/') })) {
                                $prefixMatches = @($trackedSet | Where-Object {
                                    if ($requiredPrefix.Contains('*')) { $_ -like $requiredPrefix }
                                    else { $_.StartsWith($requiredPrefix.TrimEnd('/'), [StringComparison]::OrdinalIgnoreCase) }
                                })
                                if ($prefixMatches.Count -eq 0) {
                                    $missingTrackedSources.Add($requiredPrefix)
                                }
                            }
                        }
                    }
                    $trackedSourcesComplete = $missingTrackedSources.Count -eq 0
                    if (-not $trackedSourcesComplete) {
                        $errors.Add("Formal DHE source paths are not fully tracked by Git: $($missingTrackedSources -join ', ')")
                    }
                }
            }
        }
    }
}

$staleOutputRoot = Join-Path $OutputRoot "stale-output"
New-Item -ItemType Directory -Force -Path $staleOutputRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $staleOutputRoot "stale.marker"), "stale", (New-Object Text.UTF8Encoding($false)))
$staleExit = Invoke-SourcePreflight (@(
    "-LabRoot", $LabRoot,
    "-ProjectPath", $ProjectPath,
    "-OutputRoot", $staleOutputRoot
) + $packageArguments + $identityArguments)
$staleOutputRejected = $staleExit -ne 0 -and
    (Test-Path -LiteralPath (Join-Path $staleOutputRoot "stale.marker") -PathType Leaf)
if (-not $staleOutputRejected) { $errors.Add("Source preflight accepted a non-empty output root without -ForceOutput.") }

$missingRuntimeRoot = Join-Path $OutputRoot "missing-runtime"
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("dhe-clean-checkout-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$missingRuntimePath = Join-Path $fixtureRoot "does-not-exist/runtime/libil2cpp"
$missingExit = Invoke-SourcePreflight (@(
    "-LabRoot", $LabRoot,
    "-ProjectPath", $ProjectPath,
    "-RuntimeSource", $missingRuntimePath,
    "-OutputRoot", $missingRuntimeRoot,
    "-RequireRuntime"
) + $packageArguments + $identityArguments)
$missingReport = Read-Report (Join-Path $missingRuntimeRoot "source-preflight-report.json")
$missingPassedValue = Read-StrictBoolean $missingReport "passed"
$missingRuntimeReadyValue = Read-StrictBoolean $missingReport "runtimeReady"
$missingRuntimeRejected = $missingExit -ne 0 -and $null -ne $missingReport -and
    $null -ne $missingPassedValue -and -not $missingPassedValue -and
    $null -ne $missingRuntimeReadyValue -and -not $missingRuntimeReadyValue
if (-not $missingRuntimeRejected) { $errors.Add("Source preflight did not reject a missing required runtime.") }

$runtimeTested = $false
$staleManifestTested = $false
$staleManifestRejected = $null
if (-not [string]::IsNullOrWhiteSpace($RuntimeSource)) {
    $runtimePath = [IO.Path]::GetFullPath($RuntimeSource)
    $runtimeImplementation = Join-Path $runtimePath "hybridclr/DheRuntime.cpp"
    if (-not (Test-Path -LiteralPath $runtimeImplementation -PathType Leaf)) {
        $errors.Add("RuntimeSource was supplied but hybridclr/DheRuntime.cpp was not found: $runtimePath")
    } else {
        $runtimeTested = $true
        $staleManifestTested = $true
        $staleRuntimeRoot = Join-Path $fixtureRoot "stale-runtime/libil2cpp"
        New-Item -ItemType Directory -Force -Path (Join-Path $staleRuntimeRoot "hybridclr") | Out-Null
        Copy-Item -LiteralPath $runtimeImplementation -Destination (Join-Path $staleRuntimeRoot "hybridclr/DheRuntime.cpp") -Force
        $lockPath = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
        $lockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $staleManifest = [ordered]@{
            schemaVersion = 1
            profile = "DHE-clean-checkout-fixture"
            dheEnabled = $true
            stagedRuntimeSha256 = ("0" * 64)
            dheRuntimeLockSha256 = $lockHash
        }
        [IO.File]::WriteAllText(
            (Join-Path (Split-Path -Parent $staleRuntimeRoot) "runtime-manifest.json"),
            ($staleManifest | ConvertTo-Json -Depth 8),
            (New-Object Text.UTF8Encoding($false)))
        $staleManifestRoot = Join-Path $OutputRoot "stale-manifest"
        $staleManifestExit = Invoke-SourcePreflight (@(
            "-LabRoot", $LabRoot,
            "-ProjectPath", $ProjectPath,
            "-RuntimeSource", $staleRuntimeRoot,
            "-OutputRoot", $staleManifestRoot,
            "-RequireRuntime"
        ) + $packageArguments + $identityArguments)
        $staleManifestReport = Read-Report (Join-Path $staleManifestRoot "source-preflight-report.json")
        $staleManifestPassedValue = Read-StrictBoolean $staleManifestReport "passed"
        $staleManifestRejected = $staleManifestExit -ne 0 -and $null -ne $staleManifestReport -and
            $null -ne $staleManifestPassedValue -and -not $staleManifestPassedValue -and
            (@($staleManifestReport.errors | Where-Object { $_ -match "tree does not match|runtime verification" }).Count -gt 0)
        if (-not $staleManifestRejected) { $errors.Add("Source preflight accepted a stale runtime manifest.") }
    }
}

if (Test-Path -LiteralPath $fixtureRoot) {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-clean-checkout-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    cleanSourcePreflightPassed = $cleanPassed
    staleOutputRejected = $staleOutputRejected
    missingRuntimeRejected = $missingRuntimeRejected
    runtimeTested = $runtimeTested
    staleManifestTested = $staleManifestTested
    staleManifestRejected = $staleManifestRejected
    gitRoot = if ($gitTested) { $gitRootPath } else { $null }
    gitTested = $gitTested
    gitClean = $gitClean
    gitCleanRequired = [bool]$RequireGitClean
    trackedSourcesTested = $trackedSourcesTested
    trackedSourcesComplete = $trackedSourcesComplete
    trackedSourcesRequired = [bool]$RequireTrackedSources
    sourceBoundaryPath = if ([string]::IsNullOrWhiteSpace($SourceBoundaryPath)) {
        Join-Path $LabRoot "manifests/dhe-source-boundary.json"
    } else { [IO.Path]::GetFullPath($SourceBoundaryPath) }
    missingTrackedSources = $missingTrackedSources.ToArray()
    errors = $errors.ToArray()
}
$reportPath = Join-Path $OutputRoot "clean-checkout-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE clean-checkout gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE clean-checkout gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
exit 0
