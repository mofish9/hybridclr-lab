[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityExe,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$BuildPath,
    [Parameter(Mandatory = $true)]
    [string[]]$MvJson,
    [Parameter(Mandatory = $true)]
    [string]$TransformerScript,
    [string]$ResolvedManifest = "",
    [Alias("DheAotAssembly")]
    [string[]]$DheAotAssemblies = @(),
    [Parameter(Mandatory = $true)]
    [string]$BuildIdentity,
    [switch]$RequireCompleteCoverage,
    [switch]$EnableDispatchDiagnostics,
    [string]$AotProbeDeclaringType = "",
    [string]$AotProbeMethodName = "",
    [ValidateRange(10, 3600)]
    [int]$UnityTimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$scriptHost = Resolve-DhePowerShellHost

$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
$BuildPath = [IO.Path]::GetFullPath($BuildPath)
$MvJson = @($MvJson | ForEach-Object { [IO.Path]::GetFullPath($_) })
$TransformerScript = [IO.Path]::GetFullPath($TransformerScript)
$buildDirectory = [IO.Path]::GetDirectoryName($BuildPath)
$manifestWasExplicitlyProvided = -not [string]::IsNullOrWhiteSpace($ResolvedManifest)
# The supported Demo layout keeps the Player under `Builds/`, inside the
# Unity project. Protect all other project-owned trees so a mistyped
# BuildPath cannot recursively remove source, package, settings, or generated
# IL2CPP inputs while still allowing the intended Builds/ output.
$protectedProjectPaths = @(
    (Join-Path $ProjectPath "Assets"),
    (Join-Path $ProjectPath "Packages"),
    (Join-Path $ProjectPath "ProjectSettings"),
    (Join-Path $ProjectPath "Library"),
    (Join-Path $ProjectPath "HybridCLRData")
)
Assert-DheSafeOutputRoot -Path $buildDirectory -ProtectedPaths $protectedProjectPaths
$null = New-Item -ItemType Directory -Force -Path $buildDirectory
$scriptsOnlyPath = $BuildPath
$generateLog = Join-Path $ProjectPath "unity-dhe-deterministic-generate.log"
$buildLog = Join-Path $ProjectPath "unity-dhe-deterministic-build.log"

function Invoke-Unity([string[]]$Arguments, [string]$LogPath) {
    if (Test-Path -LiteralPath $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }
    $argumentString = ($Arguments | ForEach-Object {
        $value = [string]$_
        if ($value -match '[\s"]') {
            '"' + ($value -replace '"', '\"') + '"'
        } else {
            $value
        }
    }) -join ' '
    $process = Start-Process -FilePath $UnityExe -ArgumentList $argumentString -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($UnityTimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        try { $process.WaitForExit() } catch { }
        throw "Unity timed out after $UnityTimeoutSeconds seconds. See $LogPath"
    }
    if ($process.ExitCode -ne 0) {
        throw "Unity exited with code $($process.ExitCode). See $LogPath"
    }
}

function ConvertTo-UnityArgumentList([string[]]$Values) {
    return @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        ForEach-Object { ([string]$_).Trim() }) -join ';'
}

function Get-ManifestSourceFiles([string]$ManifestPath) {
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "DHE native manifest was not found: $ManifestPath" }
    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    $files = @($manifest.methods | ForEach-Object { [IO.Path]::GetFullPath([string]$_.sourceFile) } | Sort-Object -Unique)
    return $files
}

function Update-BuildIdentity([string]$Path, [string[]]$GeneratedCppPaths, [string]$NativeManifestPath) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }
    $identityPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
        throw "Build identity was not found: $identityPath"
    }
    if (-not (Test-Path -LiteralPath $NativeManifestPath -PathType Leaf)) {
        throw "DHE native manifest was not found: $NativeManifestPath"
    }
    $identity = Get-Content -Raw -LiteralPath $identityPath | ConvertFrom-Json
    if ($null -eq $identity) {
        throw "Build identity is not valid JSON: $identityPath"
    }
    $identity | Add-Member -NotePropertyName identityVersion -NotePropertyValue 2 -Force
    $identity | Add-Member -NotePropertyName pathSemantics -NotePropertyValue "workspace-absolute-v1" -Force
    $identity | Add-Member -NotePropertyName aotSnapshotKind -NotePropertyValue "managed-assembly-plus-generated-cpp-v1" -Force
    $generatedPaths = @($GeneratedCppPaths | ForEach-Object { [IO.Path]::GetFullPath($_) } | Sort-Object -Unique)
    $nativeManifest = Get-Content -Raw -LiteralPath $NativeManifestPath | ConvertFrom-Json
    $generatedCppRoot = [IO.Path]::GetFullPath([string]$nativeManifest.generatedCppRoot)
    $identity | Add-Member -NotePropertyName generatedCppRoot -NotePropertyValue $generatedCppRoot -Force
    $identity | Add-Member -NotePropertyName nativeGuardSourceSha256 -NotePropertyValue (Get-DheFileSetHashOrEmpty $generatedPaths $generatedCppRoot) -Force
    $identity | Add-Member -NotePropertyName nativeManifestSha256 -NotePropertyValue ((Get-FileHash -LiteralPath $NativeManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()) -Force
    $identity | Add-Member -NotePropertyName generatedCppPath -NotePropertyValue $(if ($generatedPaths.Count -eq 0) { $null } else { $generatedPaths[0] }) -Force
    $identity | Add-Member -NotePropertyName generatedCppPaths -NotePropertyValue $generatedPaths -Force
    $identity | Add-Member -NotePropertyName nativeManifestPath -NotePropertyValue ([IO.Path]::GetFullPath($NativeManifestPath)) -Force
    [IO.File]::WriteAllText($identityPath, ($identity | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
}

$settingsPath = Join-Path $ProjectPath "ProjectSettings/HybridCLRSettings.asset"
$identitySourcePath = Join-Path $ProjectPath "Assets/Runtime/HybridCLRDheBuildIdentity.cs"
$hadSettings = Test-Path -LiteralPath $settingsPath -PathType Leaf
$hadIdentitySource = Test-Path -LiteralPath $identitySourcePath -PathType Leaf
$settingsSnapshot = if ($hadSettings) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$identitySourceSnapshot = if ($hadIdentitySource) { [IO.File]::ReadAllBytes($identitySourcePath) } else { $null }

try {
$generateArguments = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $ProjectPath,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.BuildPlayerOnly",
    "-labTarget", "StandaloneWindows64",
    "-labBuildPath", $scriptsOnlyPath,
    "-labDheBuildScriptsOnly", "true",
    "-labDheForceRegenerate", "true",
    "-logFile", $generateLog
)
if (@($DheAotAssemblies).Count -gt 0) {
    $generateArguments += @("-labManagedDll", [IO.Path]::GetFullPath($DheAotAssemblies[0]))
    $generateArguments += @("-labDheAotAssemblies", (ConvertTo-UnityArgumentList -Values @($DheAotAssemblies | ForEach-Object { [IO.Path]::GetFullPath($_) })))
}
if (-not [string]::IsNullOrWhiteSpace($BuildIdentity)) {
    $generateArguments += @("-labBuildIdentity", [IO.Path]::GetFullPath($BuildIdentity))
}
Invoke-Unity $generateArguments $generateLog

foreach ($mvPath in $MvJson) {
    if (-not (Test-Path -LiteralPath $mvPath -PathType Leaf)) {
        throw "mv JSON was not found: $mvPath"
    }
}

$cppRoot = Join-Path $ProjectPath "Library\Bee\artifacts\WinPlayerBuildProgram\il2cppOutput\cpp"
if (-not (Test-Path -LiteralPath $cppRoot -PathType Container)) {
    throw "IL2CPP generated C++ directory was not found: $cppRoot"
}
# Older transformer versions wrote per-method manifests/reports beside the
# requested Player output. They are transaction sidecars now, but clean any
# stale copies in this owned build directory so an old run cannot be mistaken
# for current evidence. Keep the published native manifest untouched.
Get-ChildItem -LiteralPath $buildDirectory -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^dhe-(?:\d{3}-|report-\d{3}-|resolved-mv-\d{3}-)' } |
    Remove-Item -Force -ErrorAction SilentlyContinue
$resolvedManifestPath = if ([string]::IsNullOrWhiteSpace($ResolvedManifest)) {
    Join-Path $buildDirectory "dhe-native-manifest.json"
} else {
    [IO.Path]::GetFullPath($ResolvedManifest)
}

function New-TransformArguments([bool]$ManifestAlreadyResolved) {
    $manifestParameter = if ($ManifestAlreadyResolved) {
        @("-ResolvedManifestFile", $resolvedManifestPath)
    } else {
        # The first pass must resolve the MV methods against the freshly
        # generated C++. `-ResolvedManifestFile` is intentionally strict and
        # only accepts an existing manifest for later reinjection passes.
        @("-ManifestFile", $resolvedManifestPath)
    }
    return @("-MvJsonList", (ConvertTo-DheStringListArgument (@($MvJson) | ForEach-Object { [IO.Path]::GetFullPath($_) }))) + @(
        "-GeneratedCppRoot", $cppRoot,
        "-InPlace"
    ) + $manifestParameter
}

$transformArgs = New-TransformArguments $manifestWasExplicitlyProvided
if ($EnableDispatchDiagnostics) {
    if ([string]::IsNullOrWhiteSpace($AotProbeDeclaringType) -or
        [string]::IsNullOrWhiteSpace($AotProbeMethodName)) {
        throw "Dispatch diagnostics require -AotProbeDeclaringType and -AotProbeMethodName."
    }
    $transformArgs += @(
        "-AotProbeDeclaringType", $AotProbeDeclaringType,
        "-AotProbeMethodName", $AotProbeMethodName
    )
}
if ($RequireCompleteCoverage) {
    $transformArgs += "-RequireCompleteCoverage"
}
& $scriptHost -NoProfile -ExecutionPolicy Bypass -File $TransformerScript @transformArgs
if ($LASTEXITCODE -ne 0) {
    throw "Failed to inject DHE guards into generated C++."
}

$nativeManifestPath = $resolvedManifestPath
$transformedCppPaths = @(Get-ManifestSourceFiles $nativeManifestPath)
Update-BuildIdentity $BuildIdentity $transformedCppPaths $nativeManifestPath

# Staging identity v2 changes the generated C# source. Let Unity settle that
# source and regenerate the baseline C++ once, then apply the guards to this
# final source snapshot. The final build below can therefore reuse it without
# a script-content change that would trigger another IL2CPP frontend pass.
$identitySettledArguments = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $ProjectPath,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.BuildPlayerOnly",
    "-labTarget", "StandaloneWindows64",
    "-labBuildPath", $scriptsOnlyPath,
    "-labDheBuildScriptsOnly", "true",
    "-labDheForceRegenerate", "true",
    "-logFile", $generateLog
)
if (@($DheAotAssemblies).Count -gt 0) {
    $identitySettledArguments += @("-labManagedDll", [IO.Path]::GetFullPath($DheAotAssemblies[0]))
    $identitySettledArguments += @("-labDheAotAssemblies", (ConvertTo-UnityArgumentList -Values @($DheAotAssemblies | ForEach-Object { [IO.Path]::GetFullPath($_) })))
}
if (-not [string]::IsNullOrWhiteSpace($BuildIdentity)) {
    $identitySettledArguments += @("-labBuildIdentity", [IO.Path]::GetFullPath($BuildIdentity))
}
Invoke-Unity $identitySettledArguments $generateLog

$transformArgs = New-TransformArguments $true
& $scriptHost -NoProfile -ExecutionPolicy Bypass -File $TransformerScript @transformArgs
if ($LASTEXITCODE -ne 0) {
    throw "Failed to reinject DHE guards after settling build identity."
}
$nativeManifestPath = $resolvedManifestPath
$transformedCppPaths = @(Get-ManifestSourceFiles $nativeManifestPath)
Update-BuildIdentity $BuildIdentity $transformedCppPaths $nativeManifestPath
# Unity keeps a backup copy beside the Player. Remove only the generated
# assembly source from this build output so an older backup cannot satisfy the
# post-build guard check when the current build failed to copy the patch.
if (-not [IO.Path]::GetFullPath($scriptsOnlyPath).Equals($BuildPath, [StringComparison]::OrdinalIgnoreCase)) {
    foreach ($sourcePath in $transformedCppPaths) {
        Get-ChildItem -LiteralPath $buildDirectory -Recurse -File -Filter ([IO.Path]::GetFileName($sourcePath)) -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    }
}

# BuildOptions.None reuses Bee's generated-code graph, but Unity may otherwise
# retain the previous Player Data directory and its stale StreamingAssets.
# Remove only the explicitly requested Player output before the final build.
$playerOutputBaseName = [IO.Path]::GetFileNameWithoutExtension($BuildPath)
$playerDataPath = Join-Path $buildDirectory ($playerOutputBaseName + "_Data")
if (Test-Path -LiteralPath $BuildPath -PathType Leaf) {
    Remove-Item -LiteralPath $BuildPath -Force
}
if (Test-Path -LiteralPath $playerDataPath -PathType Container) {
    Remove-Item -LiteralPath $playerDataPath -Recurse -Force
}

$finalBuildArguments = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $ProjectPath,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.BuildPlayerOnly",
    "-labTarget", "StandaloneWindows64",
    "-labBuildPath", $BuildPath,
    "-labDheReuseGeneratedCpp", "true",
    "-labDhePreserveBeeInputs", "true",
    "-logFile", $buildLog
)
if (-not [string]::IsNullOrWhiteSpace($BuildIdentity)) {
    $finalBuildArguments += @("-labBuildIdentity", [IO.Path]::GetFullPath($BuildIdentity))
}
if (@($DheAotAssemblies).Count -gt 0) {
    $finalBuildArguments += @("-labManagedDll", [IO.Path]::GetFullPath($DheAotAssemblies[0]))
    $finalBuildArguments += @("-labDheAotAssemblies", (ConvertTo-UnityArgumentList -Values @($DheAotAssemblies | ForEach-Object { [IO.Path]::GetFullPath($_) })))
}
Invoke-Unity $finalBuildArguments $buildLog

$beeStagingSnapshotPath = Join-Path $buildDirectory "DHE-BeeStaging"
if (-not (Test-Path -LiteralPath $beeStagingSnapshotPath -PathType Container)) {
    throw "Final Unity build did not preserve Bee staging inputs: $beeStagingSnapshotPath"
}
$tempStagingRoot = Join-Path $ProjectPath "Temp/StagingArea"
New-Item -ItemType Directory -Force -Path $tempStagingRoot | Out-Null
Get-ChildItem -LiteralPath $beeStagingSnapshotPath -Recurse -File -Force | ForEach-Object {
    $relative = $_.FullName.Substring($beeStagingSnapshotPath.Length).TrimStart('\', '/')
    $destination = Join-Path $tempStagingRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
}

# Unity has now emitted the final Player DAG and its unpatched C++ inputs.
# Transform those inputs and invoke the same Bee target so GameAssembly.dll
# and the shipped backup are rebuilt from the audited source snapshot.
$transformArgs = New-TransformArguments $true
& $scriptHost -NoProfile -ExecutionPolicy Bypass -File $TransformerScript @transformArgs
if ($LASTEXITCODE -ne 0) {
    throw "Failed to inject DHE guards into the final Player C++ source."
}
$nativeManifestPath = $resolvedManifestPath
$transformedCppPaths = @(Get-ManifestSourceFiles $nativeManifestPath)
Update-BuildIdentity $BuildIdentity $transformedCppPaths $nativeManifestPath
$beeBackendPath = Join-Path (Split-Path -Parent $UnityExe) "Data/bee_backend.exe"
if (-not (Test-Path -LiteralPath $beeBackendPath -PathType Leaf)) {
    throw "Bee backend was not found beside Unity: $beeBackendPath"
}
$dagPath = Get-ChildItem -LiteralPath (Join-Path $ProjectPath "Library/Bee") -File -Filter "Player*.dag" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $dagPath) {
    throw "Unity did not produce a Player Bee DAG."
}
$beeExitCode = 1
for ($beeAttempt = 1; $beeAttempt -le 3; $beeAttempt++) {
    & $beeBackendPath -C $ProjectPath -R $dagPath.FullName "Player"
    $beeExitCode = $LASTEXITCODE
    if ($beeExitCode -eq 0) { break }
    # Bee may update its DAG after observing the transformed source and
    # explicitly request one more evaluation pass.
    if ($beeExitCode -ne 4) { break }
}
if ($beeExitCode -ne 0) {
    throw "Bee failed to rebuild the final Player from patched C++ (exit code $beeExitCode)."
}
$projectNativeManifestPath = Join-Path $cppRoot "dhe-native-manifest.json"
if (-not [IO.Path]::GetFullPath($nativeManifestPath).Equals([IO.Path]::GetFullPath($projectNativeManifestPath), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $nativeManifestPath -Destination $projectNativeManifestPath -Force
}

$backupSources = @()
foreach ($sourcePath in $transformedCppPaths) {
    $backupSources += @(Get-ChildItem -LiteralPath $buildDirectory -Recurse -File -Filter ([IO.Path]::GetFileName($sourcePath)) |
        Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash })
}
$patchedSources = @($backupSources | Where-Object {
    [IO.File]::ReadAllText($_.FullName).Contains("HYBRIDCLR_DHE_GUARD_V4:")
})
$supportedGuardCount = 0
if (-not (Test-Path -LiteralPath $nativeManifestPath -PathType Leaf)) {
    throw "DHE native manifest was not found after transformation: $nativeManifestPath"
}
$nativeManifest = Get-Content -Raw -LiteralPath $nativeManifestPath | ConvertFrom-Json
if ($null -ne $nativeManifest.PSObject.Properties["supportedChangedMethodCount"]) {
    $supportedGuardCount = [int]$nativeManifest.supportedChangedMethodCount
}
if ($supportedGuardCount -gt 0 -and $patchedSources.Count -eq 0) {
    throw "Final Player backup contains no DHE guard marker despite $supportedGuardCount supported changed methods."
}
if ($supportedGuardCount -gt 0 -and $backupSources.Count -lt $transformedCppPaths.Count) {
    throw "Final Player backup does not contain every transformed generated C++ source. Expected $($transformedCppPaths.Count), found $($backupSources.Count)."
}
if ($supportedGuardCount -gt 0 -and $patchedSources.Count -lt $transformedCppPaths.Count) {
    throw "Final Player backup is missing DHE guard markers for one or more transformed generated C++ sources."
}

Write-Host "DHE deterministic Player build succeeded: $BuildPath"
Write-Host "Patched generated sources: $($patchedSources.Count)"
} finally {
    if ($hadSettings) {
        [IO.File]::WriteAllBytes($settingsPath, $settingsSnapshot)
    } elseif (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
    if ($hadIdentitySource) {
        [IO.File]::WriteAllBytes($identitySourcePath, $identitySourceSnapshot)
    } elseif (Test-Path -LiteralPath $identitySourcePath -PathType Leaf) {
        Remove-Item -LiteralPath $identitySourcePath -Force
    }
}
exit 0
