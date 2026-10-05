param(
    [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
    [string]$EditorRoot = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3',
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'reports/dhe-startup-platform-preflight.json')
)

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$EditorRoot = [IO.Path]::GetFullPath($EditorRoot)
$Output = [IO.Path]::GetFullPath($Output)
$androidRoot = Join-Path $EditorRoot 'Editor/Data/PlaybackEngines/AndroidPlayer'
$adbPath = Join-Path $androidRoot 'SDK/platform-tools/adb.exe'
$llvmRoot = Join-Path $androidRoot 'NDK/toolchains/llvm/prebuilt/windows-x86_64/bin'
$readelfPath = Join-Path $llvmRoot 'llvm-readelf.exe'
$stringsPath = Join-Path $llvmRoot 'llvm-strings.exe'
$unityLibrary = Join-Path $androidRoot 'Variations/il2cpp/Release/Libs/arm64-v8a/libunity.so'
$mainLibrary = Join-Path $androidRoot 'Variations/il2cpp/Release/Libs/arm64-v8a/libmain.so'

function Invoke-ProbeCommand {
    param([string]$Executable, [string[]]$Arguments)
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw "Probe executable does not exist: $Executable"
    }
    # Capture complete output before filtering: early-closing a pipe can make
    # LLVM report an IO failure, obscuring whether inspection actually passed.
    $captured = @(& $Executable @Arguments 2>&1 | ForEach-Object { [string]$_ })
    if ($LASTEXITCODE -ne 0) {
        throw "Probe failed ($LASTEXITCODE): $Executable`n$($captured -join "`n")"
    }
    return $captured
}

$adbOutput = @(Invoke-ProbeCommand -Executable $adbPath -Arguments @('devices', '-l'))
$deviceRows = @($adbOutput | Where-Object { $_ -match '^\S+\s+(device|offline|unauthorized)\b' })
$readyDevices = @($deviceRows | Where-Object { $_ -match '^\S+\s+device\b' })
$dynamicOutput = @(Invoke-ProbeCommand -Executable $readelfPath -Arguments @('--dynamic', $unityLibrary))
$symbolOutput = @(Invoke-ProbeCommand -Executable $readelfPath -Arguments @('--dyn-syms', $unityLibrary))
$unityStrings = @(Invoke-ProbeCommand -Executable $stringsPath -Arguments @($unityLibrary))
$mainStrings = @(Invoke-ProbeCommand -Executable $stringsPath -Arguments @($mainLibrary))

$moduleEntries = Get-Content -LiteralPath (Join-Path $EditorRoot 'modules.json') -Raw | ConvertFrom-Json -AsHashtable
$webModule = @($moduleEntries | Where-Object { $_['id'] -eq 'webgl' })
$webInstaller = if ($webModule.Count -eq 1) {
    @{ url = $webModule[0]['downloadUrl']; downloadBytes = $webModule[0]['downloadSize']; installedBytes = $webModule[0]['installedSize'] }
} else { $null }

$sourceIdentities = [ordered]@{}
foreach ($repoName in @('hybridclr', 'il2cpp_plus', 'hybridclr_unity')) {
    $repoPath = Join-Path $WorkspaceRoot "repos/$repoName"
    $commit = @(& git -C $repoPath rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw "Unable to resolve source identity: $repoName" }
    $sourceIdentities[$repoName] = $commit[0]
}

$alternativeModules = @(
    'C:/Program Files/Unity/Hub/Editor/2021.3.45f2',
    'C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t10',
    'C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t12'
) | ForEach-Object {
    @{ editorRoot = $_; webGlModulePresent = (Test-Path -LiteralPath (Join-Path $_ 'Editor/Data/PlaybackEngines/WebGLSupport')) }
}

$report = [ordered]@{
    schemaVersion = 1
    format = 'hybridclr.dhe-startup-platform-preflight.json'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    scope = 'environment-and-loader-inspection-only'
    dualBackendGateStatus = 'not-run'
    sourceIdentities = $sourceIdentities
    android = @{
        editorRoot = $EditorRoot
        adbPath = $adbPath
        adbDeviceRows = $deviceRows
        readyDeviceCount = $readyDevices.Count
        playerCorrectnessStatus = 'not-run'
        libraryInspection = @{
            unityLibrary = $unityLibrary
            unityLibrarySha256 = (Get-FileHash -LiteralPath $unityLibrary -Algorithm SHA256).Hash.ToLowerInvariant()
            mainLibrary = $mainLibrary
            mainLibrarySha256 = (Get-FileHash -LiteralPath $mainLibrary -Algorithm SHA256).Hash.ToLowerInvariant()
            dynamicLibraryEntries = @($dynamicOutput | Where-Object { $_ -match 'NEEDED|SONAME' })
            undefinedLoaderSymbols = @($symbolOutput | Where-Object { $_ -match 'UND.*(dlopen|dlsym|il2cpp_)' })
            unityRuntimeStrings = @($unityStrings | Where-Object { $_ -match '^il2cpp_init$|^il2cpp_set_data_dir$|libil2cpp' })
            mainLibraryStrings = @($mainStrings | Where-Object { $_ -match '^libil2cpp\.so$|^libunity\.so$' })
        }
    }
    webGl = @{
        targetEditorRoot = $EditorRoot
        targetModulePresent = (Test-Path -LiteralPath (Join-Path $EditorRoot 'Editor/Data/PlaybackEngines/WebGLSupport'))
        targetModuleInstaller = $webInstaller
        otherEditors = @($alternativeModules)
        otherEditorEvidenceCannotQualifyTarget = $true
        dualProfilePlayerStatus = 'not-run'
    }
    ios = @{ status = 'deferred-by-user'; qualifiesIos = $false }
}

$outputDirectory = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$reportJson = ($report | ConvertTo-Json -Depth 12).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText($Output, $reportJson, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    Report = $Output
    ReadyAndroidDevices = $readyDevices.Count
    TargetWebGlModulePresent = $report.webGl.targetModulePresent
    DualBackendGateStatus = $report.dualBackendGateStatus
}
