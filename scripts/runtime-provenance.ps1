function Get-TreeHash([string]$Root) {
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "Tree hash root was not found: $resolvedRoot"
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $files = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force |
            Sort-Object @{Expression = { $_.FullName.Substring($resolvedRoot.Length).Replace('\', '/') }}
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/').Replace('\', '/')
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

function Get-GitWorktreeHash([string]$RepoPath) {
    $resolvedRepo = [IO.Path]::GetFullPath($RepoPath)
    $relativeFiles = @(& git -C $resolvedRepo ls-files --cached --others --exclude-standard | Sort-Object)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate Git worktree files: $resolvedRepo" }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in $relativeFiles) {
            $normalized = $relative.Replace('\\', '/')
            $nameBytes = [Text.Encoding]::UTF8.GetBytes("$normalized`n")
            [void]$sha.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            $path = Join-Path $resolvedRepo $relative
            $stream = [IO.File]::OpenRead($path)
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
