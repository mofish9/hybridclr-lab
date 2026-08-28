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
    [string]$ToolGitRoot = "",
    [string]$ToolSourceBoundaryPath = "",
    [switch]$RequireIdentityTemplate,
    [switch]$RequireEmbeddedPackage,
    [switch]$RequireTrackedSources,
    [switch]$RequireGitClean,
    [switch]$RequireToolTrackedSources,
    [switch]$RequireToolGitClean,
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

function Get-GitSourceIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$RequestedRoot,
        [Parameter(Mandatory = $true)]
        [string]$OwnedPath,
        [string]$BoundaryPath = "",
        [switch]$RequireClean,
        [switch]$RequireTracked
    )

    $identityErrors = New-Object System.Collections.Generic.List[string]
    $identityWarnings = New-Object System.Collections.Generic.List[string]
    $missingSources = New-Object System.Collections.Generic.List[string]
    $resolvedRequestedRoot = [IO.Path]::GetFullPath($RequestedRoot)
    $resolvedOwnedPath = [IO.Path]::GetFullPath($OwnedPath)
    $resolvedGitRoot = $null
    $gitHead = $null
    $gitTree = $null
    $gitCleanValue = $null
    $trackedSourcesTestedValue = $false
    $trackedSourcesCompleteValue = $null
    $resolvedBoundaryPath = if ([string]::IsNullOrWhiteSpace($BoundaryPath)) { $null } else { [IO.Path]::GetFullPath($BoundaryPath) }
    $boundarySha256 = $null

    if (-not (Test-Path -LiteralPath $resolvedRequestedRoot -PathType Container)) {
        $identityErrors.Add("$Name Git source root was not found: $resolvedRequestedRoot")
    } else {
        $gitTop = @(& git -C $resolvedRequestedRoot rev-parse --show-toplevel 2>&1)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($gitTop -join "").Trim())) {
            $identityErrors.Add("$Name Git source root is not a repository: $resolvedRequestedRoot")
        } else {
            $resolvedGitRoot = [IO.Path]::GetFullPath(($gitTop -join "").Trim())
            if (-not (Test-PathWithinRoot -Path $resolvedOwnedPath -Root $resolvedGitRoot)) {
                $identityErrors.Add("$Name owned path is outside the verified Git repository: ownedPath=$resolvedOwnedPath; gitRoot=$resolvedGitRoot")
            }

            $headOutput = @(& git -C $resolvedGitRoot rev-parse HEAD 2>&1)
            if ($LASTEXITCODE -ne 0 -or ($headOutput -join "").Trim() -notmatch '^[0-9a-fA-F]{40,64}$') {
                $identityErrors.Add("Unable to resolve $Name Git HEAD: $resolvedGitRoot")
            } else {
                $gitHead = ($headOutput -join "").Trim().ToLowerInvariant()
            }
            $treeOutput = @(& git -C $resolvedGitRoot rev-parse 'HEAD^{tree}' 2>&1)
            if ($LASTEXITCODE -ne 0 -or ($treeOutput -join "").Trim() -notmatch '^[0-9a-fA-F]{40,64}$') {
                $identityErrors.Add("Unable to resolve $Name Git tree: $resolvedGitRoot")
            } else {
                $gitTree = ($treeOutput -join "").Trim().ToLowerInvariant()
            }

            $gitStatus = @(& git -C $resolvedGitRoot status --porcelain=v1 --untracked-files=all 2>&1)
            if ($LASTEXITCODE -ne 0) {
                $identityErrors.Add("Unable to inspect $Name Git source status: $resolvedGitRoot")
            } else {
                $gitCleanValue = $gitStatus.Count -eq 0
                if (-not $gitCleanValue) {
                    $message = "$Name Git source root has tracked or untracked changes: $resolvedGitRoot"
                    if ($RequireClean) { $identityErrors.Add($message) } else { $identityWarnings.Add($message) }
                }
            }

            if ($null -ne $resolvedBoundaryPath -and (Test-Path -LiteralPath $resolvedBoundaryPath -PathType Leaf)) {
                $boundarySha256 = (Get-FileHash -LiteralPath $resolvedBoundaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            if ($RequireTracked) {
                $trackedSourcesTestedValue = $true
                if ($null -eq $resolvedBoundaryPath) {
                    $identityErrors.Add("$Name tracked-source verification requires a source boundary manifest.")
                } else {
                    $boundary = $null
                    try {
                        $boundary = Get-Content -Raw -LiteralPath $resolvedBoundaryPath | ConvertFrom-Json
                    } catch {
                        $identityErrors.Add("Unable to read $Name source boundary manifest: $resolvedBoundaryPath")
                    }
                    if ($null -ne $boundary) {
                        $boundaryFormat = if ($null -ne $boundary.PSObject.Properties["format"]) { [string]$boundary.format } else { "" }
                        $boundarySchema = if ($null -ne $boundary.PSObject.Properties["schemaVersion"]) { [int]$boundary.schemaVersion } else { 0 }
                        [object[]]$boundaryExact = if ($null -ne $boundary.PSObject.Properties["exactPaths"]) { @($boundary.exactPaths) } else { @() }
                        [object[]]$boundaryPrefixes = if ($null -ne $boundary.PSObject.Properties["prefixes"]) { @($boundary.prefixes) } else { @() }
                        $boundaryExactCount = if ($null -eq $boundary.PSObject.Properties["exactPaths"]) { 0 } else { @($boundary.exactPaths).Count }
                        $boundaryPrefixCount = if ($null -eq $boundary.PSObject.Properties["prefixes"]) { 0 } else { @($boundary.prefixes).Count }
                        if ($boundarySchema -ne 1 -or $boundaryFormat -ne "hybridclr.dhe-source-boundary.json" -or
                            $boundaryExactCount -eq 0 -or $boundaryPrefixCount -eq 0) {
                            $identityErrors.Add("$Name source boundary manifest has an invalid schema, format, or empty exactPaths/prefixes: $resolvedBoundaryPath")
                            $boundary = $null
                        } else {
                            $boundaryValues = @($boundaryExact + $boundaryPrefixes | ForEach-Object { ([string]$_).Replace('\', '/') })
                            $unsafeBoundaryValues = @($boundaryValues | Where-Object {
                                [string]::IsNullOrWhiteSpace($_) -or [IO.Path]::IsPathRooted($_) -or $_ -match '(^|/)\.\.(/|$)'
                            })
                            $duplicateBoundaryValues = @($boundaryValues | Group-Object | Where-Object { $_.Count -gt 1 })
                            if ($unsafeBoundaryValues.Count -gt 0 -or $duplicateBoundaryValues.Count -gt 0) {
                                $identityErrors.Add("$Name source boundary manifest contains unsafe or duplicate paths: $resolvedBoundaryPath")
                                $boundary = $null
                            }
                        }
                    }
                    if ($null -ne $boundary) {
                        $trackedFiles = @(& git -C $resolvedGitRoot ls-files 2>&1)
                        if ($LASTEXITCODE -ne 0) {
                            $identityErrors.Add("Unable to enumerate $Name tracked source files: $resolvedGitRoot")
                        } else {
                            $trackedSet = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
                            foreach ($trackedFile in @($trackedFiles)) {
                                if (-not [string]::IsNullOrWhiteSpace([string]$trackedFile)) {
                                    $null = $trackedSet.Add(([string]$trackedFile).Replace('\', '/'))
                                }
                            }
                            if (-not (Test-PathWithinRoot -Path $resolvedBoundaryPath -Root $resolvedGitRoot)) {
                                $missingSources.Add("source-boundary-outside-git-root:$resolvedBoundaryPath")
                            } else {
                                $boundaryRelativePath = $resolvedBoundaryPath.Substring(
                                    $resolvedGitRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
                                if (-not $trackedSet.Contains($boundaryRelativePath)) { $missingSources.Add($boundaryRelativePath) }
                            }
                            foreach ($requiredPath in @($boundary.exactPaths | ForEach-Object { ([string]$_).Replace('\', '/') })) {
                                if (-not $trackedSet.Contains($requiredPath)) { $missingSources.Add($requiredPath) }
                            }
                            foreach ($requiredPrefix in @($boundary.prefixes | ForEach-Object { ([string]$_).Replace('\', '/') })) {
                                $prefixMatches = @($trackedSet | Where-Object {
                                    if ($requiredPrefix.Contains('*')) { $_ -like $requiredPrefix }
                                    else {
                                        $prefix = $requiredPrefix.TrimEnd('/')
                                        $_.Equals($prefix, [StringComparison]::OrdinalIgnoreCase) -or
                                            $_.StartsWith($prefix + '/', [StringComparison]::OrdinalIgnoreCase)
                                    }
                                })
                                if ($prefixMatches.Count -eq 0) { $missingSources.Add($requiredPrefix) }
                            }
                        }
                    }
                }
                $trackedSourcesCompleteValue = $missingSources.Count -eq 0 -and
                    @($identityErrors | Where-Object { $_ -like "$Name source boundary*" -or $_ -like "Unable to *$Name*source*" }).Count -eq 0
                if (-not $trackedSourcesCompleteValue) {
                    $identityErrors.Add("$Name formal source paths are not fully tracked by Git: $($missingSources -join ', ')")
                }
            }
        }
    }

    return [pscustomobject][ordered]@{
        name = $Name
        tested = $true
        root = $resolvedGitRoot
        ownedPath = $resolvedOwnedPath
        head = $gitHead
        tree = $gitTree
        clean = $gitCleanValue
        cleanRequired = [bool]$RequireClean
        trackedSourcesTested = $trackedSourcesTestedValue
        trackedSourcesComplete = $trackedSourcesCompleteValue
        trackedSourcesRequired = [bool]$RequireTracked
        sourceBoundaryPath = $resolvedBoundaryPath
        sourceBoundarySha256 = $boundarySha256
        missingTrackedSources = $missingSources.ToArray()
        passed = $identityErrors.Count -eq 0
        errors = $identityErrors.ToArray()
        warnings = $identityWarnings.ToArray()
    }
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

$gitTested = $RequireGitClean -or $RequireTrackedSources -or -not [string]::IsNullOrWhiteSpace($GitRoot)
$projectGit = $null
if ($gitTested) {
    $projectGitRoot = if ([string]::IsNullOrWhiteSpace($GitRoot)) { $ProjectPath } else { [IO.Path]::GetFullPath($GitRoot) }
    $projectBoundaryPath = if ([string]::IsNullOrWhiteSpace($SourceBoundaryPath)) { "" } else { [IO.Path]::GetFullPath($SourceBoundaryPath) }
    $projectGit = Get-GitSourceIdentity -Name "project" -RequestedRoot $projectGitRoot -OwnedPath $ProjectPath `
        -BoundaryPath $projectBoundaryPath -RequireClean:$RequireGitClean -RequireTracked:$RequireTrackedSources
    foreach ($identityError in @($projectGit.errors)) { $errors.Add([string]$identityError) }
    foreach ($identityWarning in @($projectGit.warnings)) { Write-Warning ([string]$identityWarning) }
}

$toolGitTested = $RequireToolGitClean -or $RequireToolTrackedSources -or -not [string]::IsNullOrWhiteSpace($ToolGitRoot)
$toolGit = $null
if ($toolGitTested) {
    $resolvedToolGitRoot = if ([string]::IsNullOrWhiteSpace($ToolGitRoot)) { $LabRoot } else { [IO.Path]::GetFullPath($ToolGitRoot) }
    $toolBoundaryPath = if ([string]::IsNullOrWhiteSpace($ToolSourceBoundaryPath)) {
        Join-Path $LabRoot "manifests/dhe-source-boundary.json"
    } else { [IO.Path]::GetFullPath($ToolSourceBoundaryPath) }
    $toolGit = Get-GitSourceIdentity -Name "tool" -RequestedRoot $resolvedToolGitRoot -OwnedPath $LabRoot `
        -BoundaryPath $toolBoundaryPath -RequireClean:$RequireToolGitClean -RequireTracked:$RequireToolTrackedSources
    foreach ($identityError in @($toolGit.errors)) { $errors.Add([string]$identityError) }
    foreach ($identityWarning in @($toolGit.warnings)) { Write-Warning ([string]$identityWarning) }
}

$gitRootPath = if ($null -eq $projectGit) { "" } else { [string]$projectGit.root }
$gitClean = if ($null -eq $projectGit) { $null } else { $projectGit.clean }
$trackedSourcesTested = $null -ne $projectGit -and [bool]$projectGit.trackedSourcesTested
$trackedSourcesComplete = if ($null -eq $projectGit) { $null } else { $projectGit.trackedSourcesComplete }
$missingTrackedSources = if ($null -eq $projectGit) { @() } else { @($projectGit.missingTrackedSources) }

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
    gitHead = if ($null -eq $projectGit) { $null } else { $projectGit.head }
    gitTree = if ($null -eq $projectGit) { $null } else { $projectGit.tree }
    gitClean = $gitClean
    gitCleanRequired = [bool]$RequireGitClean
    trackedSourcesTested = $trackedSourcesTested
    trackedSourcesComplete = $trackedSourcesComplete
    trackedSourcesRequired = [bool]$RequireTrackedSources
    sourceBoundaryPath = if ([string]::IsNullOrWhiteSpace($SourceBoundaryPath)) {
        Join-Path $LabRoot "manifests/dhe-source-boundary.json"
    } else { [IO.Path]::GetFullPath($SourceBoundaryPath) }
    sourceBoundarySha256 = if ($null -eq $projectGit) { $null } else { $projectGit.sourceBoundarySha256 }
    missingTrackedSources = @($missingTrackedSources)
    projectGit = $projectGit
    toolGit = $toolGit
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
