param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string[]]$Manifest = @(
        "reports/baseline-clean-android-arm64-build-manifest.json",
        "reports/candidate-android-arm64-build-manifest.json"
    ),
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Get-ZipEntrySha256([IO.Compression.ZipArchiveEntry]$Entry) {
    $stream = $Entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($sha.ComputeHash($stream))
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

$results = @()
foreach ($manifestInput in $Manifest) {
    $manifestPath = Resolve-LabPath $manifestInput
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Android build manifest was not found: $manifestPath"
    }
    $build = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($build.schemaVersion -ne 1 -or $build.target -ne "Android" -or
        $build.architecture -ne "arm64-v8a") {
        throw "Manifest is not an Android arm64-v8a build: $manifestPath"
    }

    $apkPath = Resolve-LabPath ([string]$build.apk.path)
    if (-not (Test-Path -LiteralPath $apkPath)) {
        throw "Manifest APK was not found: $apkPath"
    }
    $apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
    if ($apkHash -ne [string]$build.apk.sha256) {
        throw "APK hash does not match its manifest: $apkPath"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($apkPath)
    try {
        $nativeLibraries = @($archive.Entries | Where-Object {
            $_.FullName -match '^lib/[^/]+/[^/]+\.so$'
        })
        $abis = @($nativeLibraries | ForEach-Object {
            if ($_.FullName -match '^lib/([^/]+)/') { $Matches[1] }
        } | Sort-Object -Unique)
        if (($abis -join ",") -ne "arm64-v8a" -or
            @($nativeLibraries | Where-Object FullName -eq "lib/arm64-v8a/libil2cpp.so").Count -ne 1) {
            throw "APK is not an arm64-v8a-only libil2cpp build: $apkPath"
        }

        $metadataEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(^|/)HybridCLRLab/AotMetadata/.+\.dll\.bytes$'
        })
        $metadataBytes = [int64](($metadataEntries | Measure-Object -Property Length -Sum).Sum)
        if ($build.aotMetadataPackaging -eq "include" -and $metadataEntries.Count -lt 1) {
            throw "Metadata-included APK has no supplemental AOT metadata: $apkPath"
        }
        if ($build.aotMetadataPackaging -eq "exclude" -and $metadataBytes -ne 0) {
            throw "No-metadata APK contains $metadataBytes supplemental AOT metadata bytes: $apkPath"
        }

        $expectedHashes = $build.aotMetadataSha256
        if ($metadataEntries.Count -gt 0) {
            if ($null -eq $expectedHashes -or
                @($expectedHashes.PSObject.Properties).Count -ne $metadataEntries.Count) {
                throw "APK metadata file count does not match aotMetadataSha256: $manifestPath"
            }
            foreach ($entry in $metadataEntries) {
                $expected = $expectedHashes.PSObject.Properties[$entry.Name]
                if ($null -eq $expected) {
                    throw "APK metadata entry is absent from aotMetadataSha256: $($entry.Name)"
                }
                $actualHash = Get-ZipEntrySha256 $entry
                if ($actualHash -ne [string]$expected.Value) {
                    throw "APK metadata hash mismatch for '$($entry.Name)'."
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $previousBytes = [int64]$build.apk.supplementalAotMetadataBytes
    if ($CheckOnly -and $previousBytes -ne $metadataBytes) {
        throw "Manifest metadata size mismatch: recorded=$previousBytes, actual=$metadataBytes, path=$manifestPath"
    }
    if (-not $CheckOnly -and $previousBytes -ne $metadataBytes) {
        $build.apk.supplementalAotMetadataBytes = $metadataBytes
        $build | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    }
    $results += [pscustomobject]@{
        Profile = [string]$build.profile
        ApkSha256 = $apkHash
        MetadataFiles = $metadataEntries.Count
        MetadataBytes = $metadataBytes
        Changed = $previousBytes -ne $metadataBytes
        Manifest = $manifestPath
    }
}

$results
