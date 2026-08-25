param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)

$lockFiles = @(
    (Join-Path $ProjectRoot "Library/ArtifactDB-lock"),
    (Join-Path $ProjectRoot "Library/SourceAssetDB-lock"),
    (Join-Path $ProjectRoot "Temp/UnityLockfile")
)

$removed = New-Object System.Collections.Generic.List[string]
foreach ($lockFile in $lockFiles) {
    if (Test-Path $lockFile) {
        [System.IO.File]::Delete($lockFile)
        [void]$removed.Add($lockFile)
    }
}

if ($removed.Count -gt 0) {
    Write-Host "[HybridCLR Lab] Cleared Unity lock files:"
    foreach ($path in $removed) {
        Write-Host "  $path"
    }
}
