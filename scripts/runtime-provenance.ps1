function Get-TreeHash([string]$Root) {
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "Tree hash root was not found: $resolvedRoot"
    }

    # Sort through StringComparer.Ordinal instead of PowerShell Sort-Object.
    # The latter uses culture-sensitive ordering that differs between Windows
    # PowerShell 5.1 and pwsh 7, which would make the same staged tree hash
    # differently across supported shells.
    $filesByRelativePath = @{}
    $relativePaths = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if ($filesByRelativePath.ContainsKey($relative)) {
            throw "Tree hash contains duplicate relative path: $relative"
        }
        $filesByRelativePath[$relative] = $file.FullName
        $relativePaths.Add($relative)
    }
    $relativePaths.Sort([StringComparer]::Ordinal)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in $relativePaths) {
            $filePath = [string]$filesByRelativePath[$relative]
            $nameBytes = [Text.Encoding]::UTF8.GetBytes("$relative`n")
            [void]$sha.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            $stream = [IO.File]::OpenRead($filePath)
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

function Get-TreeHashExcludingGit([string]$Root) {
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
        throw "Tree hash root was not found: $resolvedRoot"
    }

    # A repository checkout contains VCS administrative files that are not
    # copied into an embedded Unity package. Keep the integrated-source lock
    # portable by hashing the same content tree while excluding .git.
    $filesByRelativePath = @{}
    $relativePaths = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if ($relative -eq '.git' -or $relative.StartsWith('.git/', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($filesByRelativePath.ContainsKey($relative)) {
            throw "Tree hash contains duplicate relative path: $relative"
        }
        $filesByRelativePath[$relative] = $file.FullName
        $relativePaths.Add($relative)
    }
    $relativePaths.Sort([StringComparer]::Ordinal)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in $relativePaths) {
            $filePath = [string]$filesByRelativePath[$relative]
            $nameBytes = [Text.Encoding]::UTF8.GetBytes("$relative`n")
            [void]$sha.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            $stream = [IO.File]::OpenRead($filePath)
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
