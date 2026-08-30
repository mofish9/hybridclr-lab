param(
    [string]$LabRoot = "",
    [switch]$AllowDirty,
    [switch]$NoCheckout,
    [string]$ReposRoot = "",
    [ValidatePattern("^$|^[0-9a-fA-F]{40}$")]
    [string]$Il2CppPlusCommit = "",
    [ValidateSet("hybridclr", "il2cpp_plus")]
    [string[]]$SkipDirtyCheckFor = @()
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

function Ensure-Repository([string]$Name, $Spec, [string]$ReposRoot) {
    $repoPath = Join-Path $ReposRoot $Name
    $isRepository = $false
    if (Test-Path -LiteralPath $repoPath -PathType Container) {
        $null = & git -C $repoPath rev-parse --git-dir 2>$null
        $isRepository = $LASTEXITCODE -eq 0
    }
    if (-not $isRepository) {
        if ((Test-Path -LiteralPath $repoPath -PathType Container) -and
            (@(Get-ChildItem -LiteralPath $repoPath -Force -ErrorAction SilentlyContinue).Count -gt 0)) {
            throw "Repository path exists but is not a Git repository: $repoPath"
        }
        Write-Host "Cloning $Name from $($Spec.fork)"
        & git clone $Spec.fork $repoPath
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to clone $Name from $($Spec.fork)"
        }
    }

    $status = @(Invoke-Git $repoPath @("status", "--porcelain"))
    if ($status.Count -gt 0 -and -not $AllowDirty -and $Name -notin $SkipDirtyCheckFor) {
        throw "Repository '$Name' has local changes. Commit or stash them, or pass -AllowDirty.`n$($status -join [Environment]::NewLine)"
    }

    $origin = (Invoke-Git $repoPath @("remote", "get-url", "origin")).Trim()
    if ($origin -ne $Spec.fork -and $origin -ne "$($Spec.fork).git") {
        Invoke-Git $repoPath @("remote", "set-url", "origin", $Spec.fork) | Out-Null
    }

    $upstream = ""
    try {
        $upstream = (Invoke-Git $repoPath @("remote", "get-url", "upstream")).Trim()
    } catch {
        $upstream = ""
    }
    if ([string]::IsNullOrWhiteSpace($upstream)) {
        Invoke-Git $repoPath @("remote", "add", "upstream", $Spec.upstream) | Out-Null
    } elseif ($upstream -ne $Spec.upstream -and $upstream -ne "$($Spec.upstream).git") {
        Invoke-Git $repoPath @("remote", "set-url", "upstream", $Spec.upstream) | Out-Null
    }

    $head = (Invoke-Git $repoPath @("rev-parse", "HEAD")).Trim()
    if ($head -ne $Spec.commit -and -not $NoCheckout) {
        Write-Host "Checking out locked commit for $Name : $($Spec.commit)"
        Invoke-Git $repoPath @("fetch", "origin", "--tags", "--force") | Out-Null
        Invoke-Git $repoPath @("checkout", "--detach", $Spec.commit) | Out-Null
    }

    $head = (Invoke-Git $repoPath @("rev-parse", "HEAD")).Trim()
    if ($head -ne $Spec.commit -and -not $NoCheckout) {
        throw "Repository '$Name' is at '$head', expected '$($Spec.commit)'"
    }

    $status = @(Get-MeaningfulGitStatus @(Invoke-Git $repoPath @("status", "--porcelain")))
    [PSCustomObject]@{
        name = $Name
        path = $repoPath
        commit = $head
        dirty = ($status.Count -gt 0)
    }
}

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$lockPath = Join-Path $LabRoot "manifests/repo-lock.json"
$lock = Get-Content -Raw $lockPath | ConvertFrom-Json
$null = . (Join-Path $PSScriptRoot "resolve-repos-root.ps1")
$reposRoot = Resolve-LabReposRoot -LabRoot $LabRoot -Lock $lock -RequestedRoot $ReposRoot
New-Item -ItemType Directory -Force -Path $reposRoot | Out-Null

$il2cppSpec = $lock.repositories.il2cpp_plus
if (-not [string]::IsNullOrWhiteSpace($Il2CppPlusCommit)) {
    $il2cppSpec.commit = $Il2CppPlusCommit.ToLowerInvariant()
}
$results = @(
    (Ensure-Repository "hybridclr_unity" $lock.repositories.hybridclr_unity $reposRoot)
    (Ensure-Repository "hybridclr" $lock.repositories.hybridclr $reposRoot)
    (Ensure-Repository "il2cpp_plus" $il2cppSpec $reposRoot)
)

$results | ConvertTo-Json -Depth 5 | Write-Host
