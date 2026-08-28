function Resolve-LabReposRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LabRoot,
        [Parameter(Mandatory = $true)]
        [object]$Lock,
        [string]$RequestedRoot = ""
    )

    $LabRoot = [IO.Path]::GetFullPath($LabRoot)
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return [IO.Path]::GetFullPath($RequestedRoot)
    }

    $candidates = @(
        [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos")),
        [IO.Path]::GetFullPath((Join-Path $LabRoot "../../repos"))
    ) | Select-Object -Unique
    $matching = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in $candidates) {
        $matchesLock = Test-Path -LiteralPath $candidate -PathType Container
        foreach ($name in @("hybridclr_unity", "hybridclr", "il2cpp_plus")) {
            $repoPath = Join-Path $candidate $name
            if (-not (Test-Path -LiteralPath $repoPath -PathType Container)) {
                $matchesLock = $false
                break
            }
            $head = (& git -C $repoPath rev-parse HEAD 2>$null)
            if ($LASTEXITCODE -ne 0 -or ([string]$head).Trim() -ne [string]$Lock.repositories.$name.commit) {
                $matchesLock = $false
                break
            }
        }
        if ($matchesLock) { $matching.Add($candidate) }
    }
    if ($matching.Count -eq 1) {
        return $matching[0]
    }
    if ($matching.Count -gt 1) {
        throw "Multiple repository roots match repo-lock.json: $($matching -join ', '). Pass -ReposRoot explicitly."
    }

    # bootstrap-repos may need to clone missing repositories. Keep the
    # historical location as the creation target when no locked root exists.
    return $candidates[0]
}
