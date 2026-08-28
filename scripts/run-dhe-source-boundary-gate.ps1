[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$OutputRoot = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$labPath = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$outputPath = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $labPath "artifacts/dhe-source-boundary-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
$manifestPath = Join-Path $labPath "manifests/dhe-source-boundary.json"

Assert-DheSafeOutputRoot -Path $outputPath
Assert-DheOutputNotAncestor -Path $outputPath -Root $labPath
$null = Initialize-DheOutputRoot -Path $outputPath -Force:$ForceOutput

$errors = New-Object System.Collections.Generic.List[string]
$unexpectedUntracked = New-Object System.Collections.Generic.List[string]
$unexpectedTracked = New-Object System.Collections.Generic.List[string]
$generatedPathsIgnored = New-Object System.Collections.Generic.List[string]

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Normalize-RepoPath([string]$Path) {
    if ($null -eq $Path) { return "" }
    $normalized = $Path.Trim().Replace('\', '/')
    while ($normalized.StartsWith('./')) { $normalized = $normalized.Substring(2) }
    return $normalized.TrimStart('/')
}

function Test-PathInBoundary([string]$Path, [string[]]$ExactPaths, [string[]]$Prefixes) {
    $normalized = Normalize-RepoPath $Path
    foreach ($exact in @($ExactPaths)) {
        if ($normalized.Equals((Normalize-RepoPath $exact), [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    foreach ($prefix in @($Prefixes)) {
        $normalizedPrefix = Normalize-RepoPath $prefix
        if ($normalizedPrefix.Contains('*')) {
            if ($normalized -like $normalizedPrefix) { return $true }
            continue
        }
        if (-not $normalizedPrefix.EndsWith('/')) { $normalizedPrefix += '/' }
        if ($normalized.StartsWith($normalizedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Test-GitIgnored([string]$GitRoot, [string]$RepoPath) {
    & git -C $GitRoot check-ignore -q -- $RepoPath 2>$null
    return $LASTEXITCODE -eq 0
}

$gitRoot = $labPath
$gitTop = @(& git -C $labPath rev-parse --show-toplevel 2>&1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($gitTop -join "").Trim())) {
    $errors.Add("DHE source boundary root is not a Git repository: $labPath")
} else {
    $gitRoot = [IO.Path]::GetFullPath(($gitTop -join "").Trim())
}

$manifest = $null
if (-not [IO.File]::Exists($manifestPath)) {
    $errors.Add("DHE source boundary manifest was not found: $manifestPath")
} else {
    try { $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json }
    catch { $errors.Add("DHE source boundary manifest is not valid JSON: $($_.Exception.Message)") }
}

$exactPaths = if ($null -eq $manifest) { @() } else { @((Get-PropertyValue $manifest "exactPaths")) | ForEach-Object { Normalize-RepoPath ([string]$_) } }
$prefixes = if ($null -eq $manifest) { @() } else { @((Get-PropertyValue $manifest "prefixes")) | ForEach-Object { Normalize-RepoPath ([string]$_) } }
$generatedPrefixes = if ($null -eq $manifest) { @() } else { @((Get-PropertyValue $manifest "generatedPrefixes")) | ForEach-Object { Normalize-RepoPath ([string]$_) } }

if ($null -ne $manifest) {
    if ([int](Get-PropertyValue $manifest "schemaVersion") -ne 1 -or
        [string](Get-PropertyValue $manifest "format") -ne "hybridclr.dhe-source-boundary.json") {
        $errors.Add("DHE source boundary manifest schema or format is invalid.")
    }
    foreach ($field in @("exactPaths", "prefixes", "generatedPrefixes")) {
        $property = $manifest.PSObject.Properties[$field]
        $values = @((Get-PropertyValue $manifest $field))
        if ($null -eq $property -or $values.Count -eq 0) {
            $errors.Add("DHE source boundary manifest field '$field' must be a non-empty array.")
        }
        $normalizedValues = @($values | ForEach-Object { Normalize-RepoPath ([string]$_) })
        if (@($normalizedValues | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
            @($normalizedValues | Group-Object | Where-Object { $_.Count -gt 1 }).Count -gt 0) {
            $errors.Add("DHE source boundary manifest field '$field' contains an empty or duplicate path.")
        }
        foreach ($value in $normalizedValues) {
            if ([IO.Path]::IsPathRooted($value) -or $value -match '(^|/)\.\.(/|$)') {
                $errors.Add("DHE source boundary manifest field '$field' contains an unsafe path: $value")
            }
        }
    }
    foreach ($path in @($exactPaths)) {
        $fullPath = Join-Path $gitRoot ($path.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $fullPath)) {
            $errors.Add("Required DHE source path is missing: $path")
        } elseif (Test-GitIgnored $gitRoot $path) {
            $errors.Add("Required DHE source path is ignored by Git: $path")
        }
    }
    foreach ($prefix in @($prefixes)) {
        $prefixPath = Join-Path $gitRoot $prefix.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $prefixExists = if ($prefix.Contains('*')) {
            @((Get-ChildItem -Path $prefixPath -Recurse -File -ErrorAction SilentlyContinue)).Count -gt 0
        } else {
            Test-Path -LiteralPath $prefixPath
        }
        if (-not $prefixExists) {
            $errors.Add("Required DHE source directory is missing: $prefix")
        }
    }
    foreach ($generated in @($generatedPrefixes)) {
        $probe = if ($generated.EndsWith('/')) {
            $generated + ".dhe-boundary-probe.json"
        } else {
            $generated
        }
        if (Test-GitIgnored $gitRoot $probe) {
            $generatedPathsIgnored.Add($generated)
        } else {
            $errors.Add("Generated/diagnostic path is not ignored by Git: $generated")
        }
    }
}

$statusLines = @()
if ($errors.Count -eq 0) {
    $statusLines = @(& git -C $gitRoot -c core.quotepath=false status --porcelain=v1 --untracked-files=all --ignored=no 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $errors.Add("Unable to inspect Git source status: $gitRoot")
    }
}
foreach ($line in @($statusLines)) {
    if ([string]::IsNullOrWhiteSpace([string]$line) -or $line.Length -lt 3) { continue }
    $status = $line.Substring(0, 2)
    $path = Normalize-RepoPath $line.Substring(3)
    if ($status -eq "??") {
        if (-not (Test-PathInBoundary $path $exactPaths $prefixes)) {
            $unexpectedUntracked.Add($path)
        }
    } elseif ($status -ne "!!" -and -not (Test-PathInBoundary $path $exactPaths $prefixes)) {
        $unexpectedTracked.Add($path)
    }
}
foreach ($path in @($unexpectedUntracked)) {
    $errors.Add("Non-ignored untracked path is outside the DHE source boundary: $path")
}
foreach ($path in @($unexpectedTracked)) {
    $errors.Add("Tracked change is outside the declared DHE change boundary: $path")
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-source-boundary-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    manifest = $manifestPath
    gitRoot = $gitRoot
    untrackedCount = @($statusLines | Where-Object { $_.StartsWith("??") }).Count
    unexpectedUntracked = $unexpectedUntracked.ToArray()
    unexpectedTracked = $unexpectedTracked.ToArray()
    generatedPathsIgnored = $generatedPathsIgnored.ToArray()
    errors = $errors.ToArray()
}
$reportPath = Join-Path $outputPath "source-boundary-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE source boundary gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE source boundary gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host ("DHE source boundary passed; non-ignored untracked paths: {0}" -f $report.untrackedCount)
exit 0
