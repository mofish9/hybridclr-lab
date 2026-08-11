param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReposRoot = "",
    [switch]$VerifyOnly,
    [switch]$AssembleRuntime
)

$ErrorActionPreference = "Stop"

function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $output = & git -C $RepoPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$RepoPath': $($output -join [Environment]::NewLine)"
    }
    return $output
}

function Ensure-Repository([string]$Name, $Spec, [string]$Root, [bool]$ReadOnly) {
    $repoPath = Join-Path $Root $Name
    if (-not (Test-Path (Join-Path $repoPath ".git"))) {
        if ($ReadOnly) {
            throw "Repository '$Name' is missing: $repoPath"
        }

        Write-Host "Cloning $Name from $($Spec.fork)"
        & git clone --no-checkout $Spec.fork $repoPath
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to clone $Name from $($Spec.fork)"
        }
    }

    $status = @(Invoke-Git $repoPath @("status", "--porcelain"))
    if ($status.Count -gt 0) {
        throw "Repository '$Name' has local changes. Commit or remove them before packaging.`n$($status -join [Environment]::NewLine)"
    }

    $origin = (Invoke-Git $repoPath @("remote", "get-url", "origin")).Trim()
    if ($origin -ne $Spec.fork -and $origin -ne "$($Spec.fork).git") {
        if ($ReadOnly) {
            throw "Repository '$Name' origin mismatch. Expected $($Spec.fork), actual $origin"
        }
        Invoke-Git $repoPath @("remote", "set-url", "origin", $Spec.fork) | Out-Null
    }

    if (-not $ReadOnly) {
        Invoke-Git $repoPath @("fetch", "origin", "--tags", "--prune") | Out-Null
    }

    $commit = $Spec.commit
    try {
        Invoke-Git $repoPath @("cat-file", "-e", "$commit`^{commit}") | Out-Null
    }
    catch {
        throw "Repository '$Name' does not contain required commit $commit"
    }

    if (-not $ReadOnly) {
        Invoke-Git $repoPath @("checkout", "--detach", $commit) | Out-Null
    }

    $actual = (Invoke-Git $repoPath @("rev-parse", "HEAD")).Trim()
    if ($actual -ne $commit) {
        throw "Repository '$Name' is at '$actual', expected '$commit'"
    }

    Write-Host "$Name is ready at $actual"
}

function Get-Sha256([string]$FilePath) {
    return (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
}

$LabRoot = [IO.Path]::GetFullPath($LabRoot)
if ([string]::IsNullOrWhiteSpace($ReposRoot)) {
    $ReposRoot = Join-Path $LabRoot "../repos"
}
$ReposRoot = [IO.Path]::GetFullPath($ReposRoot)
$manifestPath = Join-Path $LabRoot "manifests/package-candidate-lock.json"
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found in PATH. Install Git before running this script."
}

$expectedRoot = "C:\hybridclr_optimize"
$actualRoot = [IO.Path]::GetFullPath((Join-Path $LabRoot ".."))
if ($actualRoot -ne $expectedRoot) {
    throw "This packaging workflow requires '$expectedRoot'. Actual root: $actualRoot"
}

$editorPath = $manifest.engine.executablePath.Replace('/', '\')
if (-not (Test-Path -LiteralPath $editorPath)) {
    throw "Tuanjie editor was not found: $editorPath"
}

New-Item -ItemType Directory -Force -Path $ReposRoot | Out-Null
foreach ($name in @("hybridclr_unity", "hybridclr", "il2cpp_plus")) {
    Ensure-Repository $name $manifest.repositories.$name $ReposRoot $VerifyOnly.IsPresent
}

$sourceRuntimeApi = Join-Path $ReposRoot "hybridclr/hybridclr/RuntimeApi.cpp"
if (-not (Test-Path -LiteralPath $sourceRuntimeApi)) {
    throw "Optimized HybridCLR RuntimeApi.cpp was not found: $sourceRuntimeApi"
}

$sourceHash = Get-Sha256 $sourceRuntimeApi
if ($sourceHash -ne $manifest.expectedRuntimeApiSha256) {
    throw "RuntimeApi.cpp hash mismatch. Expected $($manifest.expectedRuntimeApiSha256), actual $sourceHash"
}

if ($AssembleRuntime) {
    $assembleScript = Join-Path $LabRoot "scripts/assemble-runtime.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $assembleScript -Profile Candidate
    if ($LASTEXITCODE -ne 0) {
        throw "Candidate runtime assembly failed with exit code $LASTEXITCODE"
    }

    $runtimeManifestPath = Join-Path $LabRoot "staging/runtime/Candidate/runtime-manifest.json"
    if (-not (Test-Path -LiteralPath $runtimeManifestPath)) {
        throw "Candidate runtime manifest was not generated: $runtimeManifestPath"
    }

    $runtimeManifest = Get-Content -Raw -LiteralPath $runtimeManifestPath | ConvertFrom-Json
    if ($runtimeManifest.source.hybridclr.commit -ne $manifest.repositories.hybridclr.commit -or
        $runtimeManifest.source.il2cpp_plus.commit -ne $manifest.repositories.il2cpp_plus.commit -or
        $runtimeManifest.stagedRuntimeSha256 -ne $manifest.expectedStagedRuntimeSha256) {
        throw "Candidate runtime manifest does not match package-candidate-lock.json"
    }
}

Write-Host "Packaging environment is ready. LabRoot: $LabRoot"
Write-Host "ReposRoot: $ReposRoot"
Write-Host "RuntimeApi SHA-256: $sourceHash"
