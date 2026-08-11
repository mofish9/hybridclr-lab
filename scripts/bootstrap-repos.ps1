param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$AllowDirty,
    [switch]$NoCheckout
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
    if (-not (Test-Path (Join-Path $repoPath ".git"))) {
        Write-Host "Cloning $Name from $($Spec.fork)"
        & git clone $Spec.fork $repoPath
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to clone $Name from $($Spec.fork)"
        }
    }

    $status = @(Invoke-Git $repoPath @("status", "--porcelain"))
    if ($status.Count -gt 0 -and -not $AllowDirty) {
        throw "Repository '$Name' has local changes. Commit or stash them, or pass -AllowDirty.`n$($status -join [Environment]::NewLine)"
    }

    $origin = (Invoke-Git $repoPath @("remote", "get-url", "origin")).Trim()
    if ($origin -ne $Spec.fork -and $origin -ne "$($Spec.fork).git") {
        Invoke-Git $repoPath @("remote", "set-url", "origin", $Spec.fork) | Out-Null
    }

    $upstream = ""
    try {
        $upstream = (Invoke-Git $repoPath @("remote", "get-url", "upstream")).Trim()
    }
    catch {
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

    $status = Get-MeaningfulGitStatus @(Invoke-Git $repoPath @("status", "--porcelain"))
    [PSCustomObject]@{
        name = $Name
        path = $repoPath
        commit = $head
        dirty = ($status.Count -gt 0)
    }
}

$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$lockPath = Join-Path $LabRoot "manifests/repo-lock.json"
$lock = Get-Content -Raw $lockPath | ConvertFrom-Json
$reposRoot = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos"))
New-Item -ItemType Directory -Force -Path $reposRoot | Out-Null

$results = @(
    (Ensure-Repository "hybridclr_unity" $lock.repositories.hybridclr_unity $reposRoot)
    (Ensure-Repository "hybridclr" $lock.repositories.hybridclr $reposRoot)
    (Ensure-Repository "il2cpp_plus" $lock.repositories.il2cpp_plus $reposRoot)
)

$results | ConvertTo-Json -Depth 5 | Write-Host
