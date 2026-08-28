param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Tuanjie2022-Fgs", "Compatibility-Unity2022-Fgs", "Compatibility-Unity2021-Standard")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [string]$ProjectRoot = "",
    [switch]$SkipAssembly,
    [switch]$ReuseVerifiedStagedRuntime,
    [switch]$AllowDirty,
    [switch]$SkipPlayerRun,
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [string]$InstrumentationOutput = "",
    [int]$PlayerTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { Join-Path $LabRoot "unity-test-project" } else { [IO.Path]::GetFullPath($ProjectRoot) }
$lock = Get-Content -Raw (Join-Path $LabRoot "manifests/repo-lock.json") | ConvertFrom-Json
$workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found." }
$workflow = $workflow[0]
$unity2022Profiles = @("Metadata-Unity2022", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Unity2022-Fgs")
if ($EngineWorkflow -eq "Unity2022Fgs" -and $Profile -notin $unity2022Profiles) {
    throw "Unity2022Fgs requires a Unity2022-owned profile; received '$Profile'."
}
if ($EngineWorkflow -ne "Unity2022Fgs" -and $Profile -in $unity2022Profiles) {
    throw "Profile '$Profile' is reserved for Unity2022Fgs."
}
$editor = $workflow.engine.executablePath
$editorProcessName = Split-Path -Leaf $editor
$production = $workflow.productionWorkflow
if ($EngineWorkflow -eq "Unity2021Standard" -and
    ($Il2CppCodeGeneration -ne $production.il2cppCodeGeneration -or $AotMetadataPackaging -ne $production.aotMetadataPackaging)) {
    throw "Unity2021Standard requires $($production.il2cppCodeGeneration) with metadata packaging '$($production.aotMetadataPackaging)'."
}
$runtimeManifest = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"
$runtimeSource = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$managedBuild = Join-Path $LabRoot "artifacts/managed-cases/StandaloneWindows64/HybridCLR.ManagedCases.dll"
$packagePath = Join-Path $LabRoot "../repos/hybridclr_unity"
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$buildDirectory = Join-Path $projectRoot "Builds/$buildVariant"
$player = Join-Path $buildDirectory "HybridCLRLab.exe"
$profileSlug = $buildVariant.ToLowerInvariant()
$resultPath = Join-Path $LabRoot "reports/$profileSlug-player-result.json"
$installEditorLog = Join-Path $LabRoot "reports/$profileSlug-install-editor.log"
$buildEditorLog = Join-Path $LabRoot "reports/$profileSlug-build-editor.log"
$installedVersion = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/libil2cpp-version.txt"

if ($ReuseVerifiedStagedRuntime -and -not $SkipAssembly) {
    throw "ReuseVerifiedStagedRuntime requires SkipAssembly."
}

function Get-MeaningfulGitStatus([string[]]$StatusLines) {
    return @($StatusLines | Where-Object {
        $_ -notmatch [regex]::Escape("Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta")
    })
}

function Restore-GeneratedUnityMeta([string]$RepoPath) {
    $metaPath = Join-Path $RepoPath "Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta"
    [System.IO.File]::WriteAllText($metaPath, "fileFormatVersion: 2`nguid: 2fa46135129b046a28014d58fdfd18ca")
}

function Wait-ForExpectedFileContent([string]$Path, [string]$Expected, [int]$TimeoutSeconds = 15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $Path) {
            try {
                if ((Get-Content -Raw -LiteralPath $Path).Trim() -eq $Expected) {
                    return
                }
            } catch {
                # The Editor may still be completing an atomic replace after exit.
            }
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Expected '$Expected' was not written to '$Path' within $TimeoutSeconds seconds."
}

function Test-FileContainsText([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }
    try {
        return [IO.File]::ReadAllText($Path).Contains($Expected)
    } catch {
        return $false
    }
}

function Wait-ForPlayerBuildCompletion([string]$PlayerPath, [string]$LogPath, [int]$TimeoutSeconds = 900) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ((Test-Path -LiteralPath $PlayerPath) -and
            (Test-FileContainsText -Path $LogPath -Expected "[HybridCLR Lab] Player build result: Succeeded")) {
            return
        }
        if (Test-FileContainsText -Path $LogPath -Expected "HybridCLR Lab Player build failed") {
            throw "Player build failed. See $LogPath"
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Player build did not produce a success marker and executable within $TimeoutSeconds seconds. See $LogPath"
}

function Complete-TuanjieExportedPlayer([string]$BuildDirectory, [string]$PlayerPath) {
    $solution = Join-Path $BuildDirectory "unity-test-project.sln"
    if (-not (Test-Path -LiteralPath $solution)) { return $false }

    $msbuild = "C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe"
    if (-not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild was not found: $msbuild" }
    & $msbuild $solution /m /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "Tuanjie exported Player solution failed to build." }

    $binaryDirectory = Join-Path $BuildDirectory "build/bin/x64/Release"
    $exportedPlayer = @(Get-ChildItem -LiteralPath $binaryDirectory -Filter "*.exe" -File |
        Where-Object Name -NotMatch "CrashHandler" | Select-Object -First 1)
    $dataDirectory = @(Get-ChildItem -LiteralPath (Join-Path $BuildDirectory "build/bin") -Filter "*_Data" -Directory |
        Select-Object -First 1)
    if ($exportedPlayer.Count -ne 1 -or $dataDirectory.Count -ne 1) {
        throw "Tuanjie exported Player build did not produce a unique executable and data directory."
    }

    Copy-Item -LiteralPath $exportedPlayer[0].FullName -Destination $PlayerPath -Force
    foreach ($name in @("GameAssembly.dll", "TuanjiePlayer.dll", "baselib.dll", "WinPixEventRuntime.dll", "TuanjieCrashHandler64.exe")) {
        $source = Join-Path $binaryDirectory $name
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $BuildDirectory $name) -Force
        }
    }
    Copy-Item -LiteralPath $dataDirectory[0].FullName -Destination (Join-Path $BuildDirectory "HybridCLRLab_Data") -Recurse -Force
    Write-Host "Completed exported Tuanjie Player with MSBuild: $PlayerPath"
    return $true
}

if (-not (Test-Path $editor)) { throw "Engine editor was not found: $editor" }
& (Join-Path $PSScriptRoot "check-build-environment.ps1") -LabRoot $LabRoot -EngineWorkflow $EngineWorkflow -Target StandaloneWindows64
if (-not $SkipAssembly) {
    & (Join-Path $PSScriptRoot "assemble-runtime.ps1") `
        -LabRoot $LabRoot `
        -Profile $Profile `
        -EngineWorkflow $EngineWorkflow `
        -HybridClrSource $HybridClrSource `
        -Il2CppPlusSource $Il2CppPlusSource `
        -AllowDirty:$AllowDirty
    if ($LASTEXITCODE -ne 0) { throw "Runtime assembly failed." }
}
if (-not (Test-Path $runtimeManifest)) { throw "Runtime manifest not found: $runtimeManifest" }
if (-not (Test-Path $runtimeSource)) { throw "Runtime source not found: $runtimeSource" }

$runtime = Get-Content -Raw $runtimeManifest | ConvertFrom-Json
$requestedHybridClrPath = if ([string]::IsNullOrWhiteSpace($HybridClrSource)) {
    [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos/hybridclr"))
} else {
    [IO.Path]::GetFullPath($HybridClrSource)
}
$requestedIl2CppPlusPath = if ([string]::IsNullOrWhiteSpace($Il2CppPlusSource)) {
    [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos/il2cpp_plus"))
} else {
    [IO.Path]::GetFullPath($Il2CppPlusSource)
}
if ($runtime.profile -ne $Profile -or $runtime.engineWorkflow -ne $EngineWorkflow) {
    throw "Runtime manifest does not match profile '$Profile' and workflow '$EngineWorkflow'."
}
if (-not $ReuseVerifiedStagedRuntime) {
    foreach ($source in @(
        @("hybridclr", $requestedHybridClrPath, "hybridclr", $runtime.source.hybridclr),
        @("il2cpp_plus", $requestedIl2CppPlusPath, "libil2cpp", $runtime.source.il2cpp_plus)
    )) {
        $name = $source[0]
        $requestedPath = [IO.Path]::GetFullPath([string]$source[1])
        $treeDirectory = Join-Path $requestedPath $source[2]
        $manifestSource = $source[3]
        $currentCommit = (& git -C $requestedPath rev-parse HEAD 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Unable to read $name commit from '$requestedPath': $($currentCommit -join [Environment]::NewLine)" }
        $manifestPath = [IO.Path]::GetFullPath([string]$manifestSource.path)
        $currentTreeSha256 = Get-TreeHash $treeDirectory
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($manifestPath, $requestedPath) -or
            [string]$manifestSource.commit -ne ([string]$currentCommit).Trim() -or
            [string]$manifestSource.treeSha256 -ne $currentTreeSha256) {
            throw "Runtime manifest source for '$name' is stale or does not match '$requestedPath'. Reassemble the runtime."
        }
    }
} else {
    Write-Host "Reusing manifest-bound staged runtime: $runtimeSource"
}
if ((Get-TreeHash $runtimeSource) -ne [string]$runtime.stagedRuntimeSha256) {
    throw "Staged runtime content does not match its manifest. Reassemble the runtime."
}

& (Join-Path $PSScriptRoot "build-managed-cases.ps1") -LabRoot $LabRoot -UnityProjectRoot $projectRoot
if ($LASTEXITCODE -ne 0) { throw "Managed cases build failed." }
$metadataStressBuild = Join-Path (Split-Path $managedBuild) "HybridCLR.MetadataStress.dll"
$crossAssemblyBuild = Join-Path (Split-Path $managedBuild) "HybridCLR.CrossAssemblyDerived.dll"
$aotBenchmarkBuild = Join-Path (Split-Path $managedBuild) "Aot/HybridCLR.ManagedCasesAot.dll"
$managedAssemblySha256 = (Get-FileHash -LiteralPath $managedBuild -Algorithm SHA256).Hash
$metadataStressAssemblySha256 = (Get-FileHash -LiteralPath $metadataStressBuild -Algorithm SHA256).Hash
$prewarmManifestPath = Join-Path $LabRoot "reports/prewarm-manifest-stress-StandaloneWindows64.json"
& (Join-Path $PSScriptRoot "generate-prewarm-manifest.ps1") `
    -LabRoot $LabRoot `
    -Assembly $metadataStressBuild `
    -RootType "HybridCLR.Lab.MetadataStress.MetadataStressEntry" `
    -RootMethod "Touch" `
    -RootParameterCount 0 `
    -OutputJson $prewarmManifestPath `
    -OutputCSharp (Join-Path $LabRoot "reports/MetadataStressPrewarmManifest.cs")
if ($LASTEXITCODE -ne 0) { throw "Prewarm manifest generation failed." }
$prewarmManifestSha256 = (Get-FileHash -LiteralPath $prewarmManifestPath -Algorithm SHA256).Hash
$crossAssemblyDerivedSha256 = (Get-FileHash -LiteralPath $crossAssemblyBuild -Algorithm SHA256).Hash
$aotBenchmarkAssemblySha256 = (Get-FileHash -LiteralPath $aotBenchmarkBuild -Algorithm SHA256).Hash
Restore-GeneratedUnityMeta -RepoPath $packagePath
$hybridclrUnityTreeSha256 = Get-GitWorktreeHash $packagePath

$buildIdentityDirectory = Join-Path $LabRoot "staging/build-identities"
New-Item -ItemType Directory -Force -Path $buildIdentityDirectory | Out-Null
$buildIdentityPath = Join-Path $buildIdentityDirectory "$profileSlug.json"
$buildIdentity = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    engineWorkflow = $EngineWorkflow
    target = "StandaloneWindows64"
    architecture = "x64"
    il2cppCodeGeneration = $Il2CppCodeGeneration
    aotMetadataPackaging = $AotMetadataPackaging
    fullGenericSharingDiagnostics = [bool]$runtime.fullGenericSharingDiagnostics
    hybridclrUnityTreeSha256 = $hybridclrUnityTreeSha256
    stagedRuntimeSha256 = $runtime.stagedRuntimeSha256
    managedAssemblySha256 = $managedAssemblySha256
    crossAssemblyDerivedSha256 = $crossAssemblyDerivedSha256
    testGoldenContractSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/test-golden.json") -Algorithm SHA256).Hash
    benchmarkGoldenSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-golden.json") -Algorithm SHA256).Hash
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($buildIdentityPath, ($buildIdentity | ConvertTo-Json -Depth 4), $utf8NoBom)
$buildIdentitySha256 = (Get-FileHash -LiteralPath $buildIdentityPath -Algorithm SHA256).Hash

if (Test-Path $buildDirectory) { Remove-Item -LiteralPath $buildDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Split-Path $installEditorLog) | Out-Null

& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot -EditorProcessName $editorProcessName
& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot
foreach ($logPath in @($installEditorLog, $buildEditorLog)) {
    if (Test-Path $logPath) {
        Remove-Item -LiteralPath $logPath -Force
    }
}

if (Test-Path $installedVersion) {
    Remove-Item -LiteralPath $installedVersion -Force
}

$installArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectRoot,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.InstallRuntime",
    "-labProfile", $Profile,
    "-labRoot", $LabRoot,
    "-labRuntimeSource", $runtimeSource,
    "-logFile", $installEditorLog
)
$installProcess = Start-Process -FilePath $editor -ArgumentList $installArgs -PassThru -WindowStyle Hidden
$installProcess.WaitForExit()
$installExitCode = $installProcess.ExitCode
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot -EditorProcessName $editorProcessName -InitialDiscoverySeconds 15
try {
    Wait-ForExpectedFileContent -Path $installedVersion -Expected "8.13.0"
} catch {
    if ($installExitCode -ne 0) {
        throw "Runtime installation process exited with code $installExitCode and did not install the expected runtime. See $installEditorLog"
    }
    throw
}

& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot -EditorProcessName $editorProcessName
& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot

$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectRoot,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.GenerateAndBuild",
    "-labProfile", $Profile,
    "-labRoot", $LabRoot,
    "-labManagedDll", $managedBuild,
    "-labPrewarmManifest", $prewarmManifestPath,
    "-labBuildPath", $player,
    "-labIl2CppCodeGeneration", $Il2CppCodeGeneration,
    "-labAotMetadataPackaging", $AotMetadataPackaging,
    "-labBuildIdentity", $buildIdentityPath,
    "-logFile", $buildEditorLog
)
$buildProcess = Start-Process -FilePath $editor -ArgumentList $buildArgs -PassThru -WindowStyle Hidden
$buildProcess.WaitForExit()
$buildExitCode = $buildProcess.ExitCode
$unityWorkflow = $EngineWorkflow -in @("Unity2021Standard", "Unity2022Fgs")
if ($unityWorkflow) {
    Wait-ForPlayerBuildCompletion -PlayerPath $player -LogPath $buildEditorLog
}
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot -EditorProcessName $editorProcessName -InitialDiscoverySeconds 15
$unityRestartCompletedBuild = $unityWorkflow -and
    (Test-Path -LiteralPath $player) -and
    (Test-FileContainsText -Path $buildEditorLog -Expected "[HybridCLR Lab] Player build result: Succeeded")
$tuanjieCompletedBuild = $false
if ($EngineWorkflow -eq "Tuanjie2022Fgs") {
    if (-not (Test-Path $player)) {
        [void](Complete-TuanjieExportedPlayer -BuildDirectory $buildDirectory -PlayerPath $player)
    }
    $tuanjieCompletedBuild = Test-Path -LiteralPath $player
}
if ($buildExitCode -ne 0 -and -not $unityRestartCompletedBuild -and -not $tuanjieCompletedBuild) {
    throw "Player build process exited with code $buildExitCode. See $buildEditorLog"
}
if (-not (Test-Path $player)) { throw "Player was not produced: $player" }

if (-not $SkipPlayerRun) {
    if (Test-Path $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
    $playerMetadataMode = if ($AotMetadataPackaging -eq "exclude") { "none" } else { "supplemental" }
    $playerArgs = @("-batchmode", "-nographics", "-labTarget", "StandaloneWindows64", "-labAotMetadataMode", $playerMetadataMode, "-labResult", $resultPath)
    if (-not [string]::IsNullOrWhiteSpace($InstrumentationOutput)) {
        $instrumentationPath = if ([IO.Path]::IsPathRooted($InstrumentationOutput)) {
            [IO.Path]::GetFullPath($InstrumentationOutput)
        } else {
            [IO.Path]::GetFullPath((Join-Path $LabRoot $InstrumentationOutput))
        }
        if (Test-Path $instrumentationPath) { Remove-Item -LiteralPath $instrumentationPath -Force }
        $playerArgs += @("-labInstrumentationResult", $instrumentationPath)
    }
    $playerProcess = Start-Process -FilePath $player -ArgumentList $playerArgs -PassThru -WindowStyle Hidden
    $playerExited = $playerProcess.WaitForExit($PlayerTimeoutSeconds * 1000)
    if (-not $playerExited) {
        try { $playerProcess.Kill() } catch { }
        throw "Player process timed out after $PlayerTimeoutSeconds seconds. See $resultPath"
    }
    if ($playerProcess.ExitCode -ne 0) {
        throw "Player process exited with code $($playerProcess.ExitCode). See $resultPath"
    }
    if (-not (Test-Path $resultPath)) { throw "Player did not produce a result: $resultPath" }
    if (-not [string]::IsNullOrWhiteSpace($InstrumentationOutput) -and -not (Test-Path $instrumentationPath)) {
        throw "Player did not produce an instrumentation snapshot: $instrumentationPath"
    }
    $playerResult = Get-Content -Raw $resultPath | ConvertFrom-Json
    if ($playerResult.summary.failed -ne 0) { throw "Player correctness suite failed: $($playerResult.summary.failed) cases. See $resultPath" }
    if ($playerResult.correctnessProbes.crossAssemblyLazyVTable -ne $true -or
        $playerResult.correctnessProbes.lazyMetadataConcurrentFirstTouch -ne $true) {
        throw "Player lazy metadata correctness probes did not pass. See $resultPath"
    }
    $diffPath = "reports/$($Profile.ToLowerInvariant())-differential-result.json"
    & (Join-Path $PSScriptRoot "compare-results.ps1") -LabRoot $LabRoot -Actual $resultPath -Output $diffPath
}

$generatedCpp = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/MethodBridge.cpp"
$buildManifest = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    engine = [ordered]@{
        workflow = $EngineWorkflow
        family = $workflow.engine.family
        version = $workflow.engine.version
        compatibilityVersion = $workflow.engine.unityVersion
        editorPath = $editor
    }
    repositories = [ordered]@{
        hybridclr_unity = [ordered]@{ url = $lock.repositories.hybridclr_unity.fork; commit = (git -C $packagePath rev-parse HEAD).Trim(); dirty = ((Get-MeaningfulGitStatus @(& git -C $packagePath status --porcelain)).Count -gt 0); treeSha256 = $hybridclrUnityTreeSha256 }
        hybridclr = $runtime.source.hybridclr
        il2cpp_plus = $runtime.source.il2cpp_plus
    }
    target = "StandaloneWindows64"
    configuration = "Release"
    il2cppCodeGeneration = $Il2CppCodeGeneration
    aotMetadataPackaging = $AotMetadataPackaging
    fullGenericSharingDiagnostics = [bool]$runtime.fullGenericSharingDiagnostics
    buildIdentitySha256 = $buildIdentitySha256
    hybridclrUnityTreeSha256 = $hybridclrUnityTreeSha256
    stagedRuntimeSha256 = $runtime.stagedRuntimeSha256
    managedAssemblySha256 = $managedAssemblySha256
    crossAssemblyDerivedSha256 = $crossAssemblyDerivedSha256
    metadataStressAssemblySha256 = $metadataStressAssemblySha256
    metadataStressPrewarmManifestSha256 = $prewarmManifestSha256
    metadataStressSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "managed-cases/HybridCLR.MetadataStress/Generated/MetadataStress.Generated.cs") -Algorithm SHA256).Hash
    metadataBenchmarkPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/metadata-benchmark-policy.json") -Algorithm SHA256).Hash
    aotBenchmarkAssemblySha256 = $aotBenchmarkAssemblySha256
    performanceWorkloadSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCases/PerformanceWorkload.cs") -Algorithm SHA256).Hash
    benchmarkPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-policy.json") -Algorithm SHA256).Hash
    testGoldenContractSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/test-golden.json") -Algorithm SHA256).Hash
    benchmarkGoldenSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-golden.json") -Algorithm SHA256).Hash
}
$buildFiles = @(Get-ChildItem -LiteralPath $buildDirectory -Recurse -File)
$gameAssembly = @($buildFiles | Where-Object { $_.Name -eq "GameAssembly.dll" } | Select-Object -First 1)
$builtAotMetadata = @($buildFiles | Where-Object {
    $_.FullName -match '[\\/]StreamingAssets[\\/]HybridCLRLab[\\/]AotMetadata[\\/].+\.dll\.bytes$'
})
$dataDirectory = Join-Path $buildDirectory "HybridCLRLab_Data"
$dataFiles = if (Test-Path $dataDirectory) { @(Get-ChildItem -LiteralPath $dataDirectory -Recurse -File) } else { @() }
$buildManifest.playerSize = [ordered]@{
    totalFilesBytes = [int64](($buildFiles | Measure-Object -Property Length -Sum).Sum)
    executableBytes = [int64](Get-Item -LiteralPath $player).Length
    gameAssemblyBytes = if ($gameAssembly.Count -gt 0) { [int64]$gameAssembly[0].Length } else { 0 }
    dataDirectoryBytes = [int64](($dataFiles | Measure-Object -Property Length -Sum).Sum)
    supplementalAotMetadataBytes = [int64](($builtAotMetadata | Measure-Object -Property Length -Sum).Sum)
}
$buildManifest.playerSha256 = (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash
if ($gameAssembly.Count -ne 1) {
    throw "Expected exactly one GameAssembly.dll in the player build, found $($gameAssembly.Count)."
}
$buildManifest.gameAssemblySha256 = (Get-FileHash -LiteralPath $gameAssembly[0].FullName -Algorithm SHA256).Hash
if ($AotMetadataPackaging -eq "exclude" -and $buildManifest.playerSize.supplementalAotMetadataBytes -ne 0) {
    throw "No-metadata build still contains $($buildManifest.playerSize.supplementalAotMetadataBytes) bytes of supplemental AOT metadata."
}
$aotMetadataDirectory = Join-Path $projectRoot "Assets/StreamingAssets/HybridCLRLab/AotMetadata"
$aotMetadataHashes = [ordered]@{}
if (Test-Path $aotMetadataDirectory) {
    foreach ($metadataFile in Get-ChildItem $aotMetadataDirectory -Filter "*.dll.bytes" -File | Sort-Object Name) {
        $aotMetadataHashes[$metadataFile.Name] = (Get-FileHash -LiteralPath $metadataFile.FullName -Algorithm SHA256).Hash
    }
}
$buildManifest.aotMetadataSha256 = $aotMetadataHashes
if (Test-Path $generatedCpp) {
    $buildManifest.generatedCppSha256 = (Get-FileHash -LiteralPath $generatedCpp -Algorithm SHA256).Hash
}
$manifestOutput = Join-Path $LabRoot "reports/$profileSlug-build-manifest.json"
Restore-GeneratedUnityMeta -RepoPath $packagePath
if ((Get-GitWorktreeHash $packagePath) -ne $hybridclrUnityTreeSha256) {
    throw "hybridclr_unity worktree changed while the player was being built."
}
$buildManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestOutput -Encoding UTF8
Write-Host "Clean baseline player: $player"
if (-not $SkipPlayerRun) { Write-Host "Player result: $resultPath" }
Write-Host "Build manifest: $manifestOutput"
