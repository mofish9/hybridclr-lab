param(
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputRoot = "staging/runtime",
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $output = & git -C $RepoPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$RepoPath': $($output -join [Environment]::NewLine)"
    }
    return $output
}

function Get-MeaningfulGitStatus([string[]]$StatusLines) {
    return @($StatusLines | Where-Object {
        $_ -notmatch [regex]::Escape("Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta")
    })
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Get-TreeHash([string]$Root) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
            Sort-Object @{Expression = { $_.FullName.Substring($Root.Length).Replace('\', '/') }}
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
            $nameBytes = [Text.Encoding]::UTF8.GetBytes("$relative`n")
            [void]$sha.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            $stream = [IO.File]::OpenRead($file.FullName)
            try {
                $buffer = New-Object byte[] 1048576
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    [void]$sha.TransformBlock($buffer, 0, $read, $buffer, 0)
                }
            } finally {
                $stream.Dispose()
            }
            $separator = [Text.Encoding]::UTF8.GetBytes("`n")
            [void]$sha.TransformBlock($separator, 0, $separator.Length, $separator, 0)
        }
        [void]$sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return ([BitConverter]::ToString($sha.Hash) -replace '-', '').ToUpperInvariant()
    } finally {
        $sha.Dispose()
    }
}

$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$lockPath = Join-Path $LabRoot "manifests/repo-lock.json"
$lock = Get-Content -Raw $lockPath | ConvertFrom-Json
$reposRoot = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos"))
$outputRootPath = [IO.Path]::GetFullPath((Join-Path $LabRoot $OutputRoot))
$stagingPath = Join-Path $outputRootPath $Profile
$stagedLibil2cpp = Join-Path $stagingPath "libil2cpp"
$stagedExternal = Join-Path $stagingPath "external"

$noCheckout = $Profile -ne "Baseline-Clean"
& (Join-Path $PSScriptRoot "bootstrap-repos.ps1") -LabRoot $LabRoot -AllowDirty:$AllowDirty -NoCheckout:$noCheckout

$hybridclrPath = if ([string]::IsNullOrWhiteSpace($HybridClrSource)) {
    Join-Path $reposRoot "hybridclr"
} else {
    [IO.Path]::GetFullPath($HybridClrSource)
}
$il2cppPath = if ([string]::IsNullOrWhiteSpace($Il2CppPlusSource)) {
    Join-Path $reposRoot "il2cpp_plus"
} else {
    [IO.Path]::GetFullPath($Il2CppPlusSource)
}
$hybridclrSpec = $lock.repositories.hybridclr
$il2cppSpec = $lock.repositories.il2cpp_plus

foreach ($item in @(@("hybridclr", $hybridclrPath, $hybridclrSpec.commit), @("il2cpp_plus", $il2cppPath, $il2cppSpec.commit))) {
    $name = $item[0]
    $path = $item[1]
    $expected = $item[2]
    $actual = (Invoke-Git $path @("rev-parse", "HEAD")).Trim()
    if (-not $noCheckout -and $actual -ne $expected) { throw "$name is at $actual, expected $expected" }
    $dirty = (Get-MeaningfulGitStatus @(Invoke-Git $path @("status", "--porcelain"))).Count -gt 0
    if ($dirty -and -not $AllowDirty) { throw "$name is dirty; pass -AllowDirty only for an explicit local profile." }
}

if (Test-Path $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null
Copy-DirectoryContents (Join-Path $il2cppPath "libil2cpp") $stagedLibil2cpp
$editorRoot = Split-Path -Parent $lock.engine.executablePath
$editorExternal = Join-Path $editorRoot "Data/il2cpp/external"
if (-not (Test-Path $editorExternal)) { throw "Tuanjie il2cpp external headers were not found: $editorExternal" }
Copy-DirectoryContents $editorExternal $stagedExternal
$stagedHybridclr = Join-Path $stagedLibil2cpp "hybridclr"
if (Test-Path $stagedHybridclr) { Remove-Item -LiteralPath $stagedHybridclr -Recurse -Force }
Copy-DirectoryContents (Join-Path $hybridclrPath "hybridclr") $stagedHybridclr

if ($Profile -eq "Baseline-Instrumented") {
    $instrumentationConfig = Join-Path $stagedHybridclr "lab/InstrumentationConfig.h"
    New-Item -ItemType Directory -Force -Path (Split-Path $instrumentationConfig) | Out-Null
    @'
#pragma once
#define HYBRIDCLR_LAB_INSTRUMENTED 1
'@ | Set-Content -LiteralPath $instrumentationConfig -Encoding ASCII
}

$runtimeHash = Get-TreeHash $stagedLibil2cpp
$manifest = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    source = [ordered]@{
        hybridclr = [ordered]@{ url = $hybridclrSpec.fork; path = $hybridclrPath; commit = (Invoke-Git $hybridclrPath @("rev-parse", "HEAD")).Trim(); dirty = ((Get-MeaningfulGitStatus @(Invoke-Git $hybridclrPath @("status", "--porcelain"))).Count -gt 0) }
        il2cpp_plus = [ordered]@{ url = $il2cppSpec.fork; path = $il2cppPath; commit = (Invoke-Git $il2cppPath @("rev-parse", "HEAD")).Trim(); dirty = ((Get-MeaningfulGitStatus @(Invoke-Git $il2cppPath @("status", "--porcelain"))).Count -gt 0) }
    }
    stagedLibil2cpp = $stagedLibil2cpp
    stagedRuntimeSha256 = $runtimeHash
}
$manifestPath = Join-Path $stagingPath "runtime-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Assembled $Profile runtime: $stagedLibil2cpp"
Write-Host "Runtime SHA-256: $runtimeHash"
Write-Host "Manifest: $manifestPath"
