param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [switch]$SkipAssembly,
    [switch]$AllowDirty,
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$projectRoot = Join-Path $LabRoot "unity-test-project"
$lock = Get-Content -Raw (Join-Path $LabRoot "manifests/repo-lock.json") | ConvertFrom-Json
$runtimeManifestPath = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"
$runtimeSource = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$managedBuild = Join-Path $LabRoot "artifacts/managed-cases/Android/HybridCLR.ManagedCases.dll"
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$profileSlug = $buildVariant.ToLowerInvariant()
$buildDirectory = Join-Path $projectRoot "Builds/Android-ARM64/$buildVariant"
$apk = Join-Path $buildDirectory "HybridCLRLab-arm64.apk"
$packageName = "com.mofish.hybridclrlab"
$installEditorLog = Join-Path $LabRoot "reports/$profileSlug-android-arm64-install-editor.log"
$buildEditorLog = Join-Path $LabRoot "reports/$profileSlug-android-arm64-build-editor.log"
$installedVersion = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/libil2cpp-version.txt"

function Get-MeaningfulGitStatus([string[]]$StatusLines) {
    return @($StatusLines | Where-Object {
        $_ -notmatch [regex]::Escape("Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta")
    })
}

function Restore-GeneratedUnityMeta([string]$RepoPath) {
    $metaPath = Join-Path $RepoPath "Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta"
    [System.IO.File]::WriteAllText($metaPath, "fileFormatVersion: 2`nguid: 2fa46135129b046a28014d58fdfd18ca")
}

& (Join-Path $PSScriptRoot "check-build-environment.ps1") -LabRoot $LabRoot -Target Android
if (-not $SkipAssembly) {
    & (Join-Path $PSScriptRoot "assemble-runtime.ps1") -LabRoot $LabRoot -Profile $Profile -AllowDirty:$AllowDirty
    if ($LASTEXITCODE -ne 0) { throw "Runtime assembly failed." }
}
if (-not (Test-Path -LiteralPath $runtimeManifestPath)) { throw "Runtime manifest not found: $runtimeManifestPath" }
if (-not (Test-Path -LiteralPath $runtimeSource)) { throw "Runtime source not found: $runtimeSource" }

& (Join-Path $PSScriptRoot "build-managed-cases.ps1") -LabRoot $LabRoot -Target Android
if ($LASTEXITCODE -ne 0) { throw "Managed cases build failed." }
$metadataStressBuild = Join-Path (Split-Path $managedBuild) "HybridCLR.MetadataStress.dll"
$prewarmManifestPath = Join-Path $LabRoot "reports/prewarm-manifest-stress-Android.json"
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

$runtime = Get-Content -Raw $runtimeManifestPath | ConvertFrom-Json
$buildIdentityDirectory = Join-Path $LabRoot "staging/build-identities"
New-Item -ItemType Directory -Force -Path $buildIdentityDirectory | Out-Null
$buildIdentityPath = Join-Path $buildIdentityDirectory "$profileSlug-android-arm64.json"
$buildIdentity = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    target = "Android"
    architecture = "arm64-v8a"
    il2cppCodeGeneration = $Il2CppCodeGeneration
    aotMetadataPackaging = $AotMetadataPackaging
    stagedRuntimeSha256 = $runtime.stagedRuntimeSha256
    managedAssemblySha256 = (Get-FileHash -LiteralPath $managedBuild -Algorithm SHA256).Hash
    crossAssemblyDerivedSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $managedBuild) "HybridCLR.CrossAssemblyDerived.dll") -Algorithm SHA256).Hash
    testGoldenContractSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/test-golden.json") -Algorithm SHA256).Hash
    benchmarkGoldenSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-golden.json") -Algorithm SHA256).Hash
}
$buildIdentity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $buildIdentityPath -Encoding UTF8
$buildIdentitySha256 = (Get-FileHash -LiteralPath $buildIdentityPath -Algorithm SHA256).Hash

if (Test-Path -LiteralPath $buildDirectory) { Remove-Item -LiteralPath $buildDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $installEditorLog) | Out-Null

& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot
foreach ($logPath in @($installEditorLog, $buildEditorLog)) {
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
}
if (Test-Path -LiteralPath $installedVersion) { Remove-Item -LiteralPath $installedVersion -Force }

$installArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectRoot,
    "-buildTarget", "Android",
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.InstallRuntime",
    "-labTarget", "Android",
    "-labProfile", $Profile,
    "-labRuntimeSource", $runtimeSource,
    "-logFile", $installEditorLog
)
$installProcess = Start-Process -FilePath $tools.Editor -ArgumentList $installArgs -PassThru -WindowStyle Hidden
$installProcess.WaitForExit()
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
if ($installProcess.ExitCode -ne 0) {
    throw "Tuanjie Android runtime installation exited with code $($installProcess.ExitCode). See $installEditorLog"
}
if (-not (Test-Path -LiteralPath $installedVersion) -or (Get-Content -Raw $installedVersion).Trim() -ne "8.13.0") {
    throw "Tuanjie runtime installation did not produce the locked 8.13.0 runtime. See $installEditorLog"
}

& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot
$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectRoot,
    "-buildTarget", "Android",
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.GenerateAndBuild",
    "-labTarget", "Android",
    "-labProfile", $Profile,
    "-labManagedDll", $managedBuild,
    "-labPrewarmManifest", $prewarmManifestPath,
    "-labBuildPath", $apk,
    "-labIl2CppCodeGeneration", $Il2CppCodeGeneration,
    "-labAotMetadataPackaging", $AotMetadataPackaging,
    "-labBuildIdentity", $buildIdentityPath,
    "-logFile", $buildEditorLog
)
$buildProcess = Start-Process -FilePath $tools.Editor -ArgumentList $buildArgs -PassThru -WindowStyle Hidden
$buildProcess.WaitForExit()
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
if ($buildProcess.ExitCode -ne 0) {
    throw "Tuanjie Android Player build exited with code $($buildProcess.ExitCode). See $buildEditorLog"
}
if (-not (Test-Path -LiteralPath $apk)) { throw "Android APK was not produced: $apk" }

$apkEntries = @(& $tools.Aapt list $apk 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Unable to inspect APK with aapt: $apk" }
$nativeLibraries = @($apkEntries | Where-Object { $_ -match '^lib/[^/]+/[^/]+\.so$' } | Sort-Object -Unique)
$abis = @($nativeLibraries | ForEach-Object {
    if ($_ -match '^lib/([^/]+)/') { $Matches[1] }
} | Sort-Object -Unique)
if (($abis -join ',') -ne "arm64-v8a") {
    throw "APK must contain only arm64-v8a native libraries; found: $($abis -join ', ')"
}
if (@($nativeLibraries | Where-Object { $_ -eq "lib/arm64-v8a/libil2cpp.so" }).Count -ne 1) {
    throw "APK does not contain lib/arm64-v8a/libil2cpp.so."
}
$archive = [IO.Compression.ZipFile]::OpenRead($apk)
try {
	$packagedAotMetadata = @($archive.Entries | Where-Object {
		$_.FullName -match '(^|/)HybridCLRLab/AotMetadata/.+\.dll\.bytes$'
	})
    $packagedAotMetadataBytes = [int64](($packagedAotMetadata | Measure-Object -Property Length -Sum).Sum)
} finally {
    $archive.Dispose()
}
if ($AotMetadataPackaging -eq "exclude" -and $packagedAotMetadataBytes -ne 0) {
    throw "No-metadata APK still contains $packagedAotMetadataBytes bytes of supplemental AOT metadata."
}
if ($AotMetadataPackaging -eq "include" -and $packagedAotMetadata.Count -lt 1) {
    throw "Metadata-included APK does not contain supplemental AOT metadata."
}

$packagePath = Join-Path $LabRoot "../repos/hybridclr_unity"
$generatedCpp = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/MethodBridge.cpp"
$buildManifest = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    engine = [ordered]@{
        family = $lock.engine.family
        version = $lock.engine.tuanjieVersion
        compatibilityVersion = $lock.engine.unityCompatibilityVersion
        editorPath = $tools.Editor
    }
    repositories = [ordered]@{
        hybridclr_unity = [ordered]@{ url = $lock.repositories.hybridclr_unity.fork; commit = (git -C $packagePath rev-parse HEAD).Trim(); dirty = ((Get-MeaningfulGitStatus @(& git -C $packagePath status --porcelain)).Count -gt 0) }
        hybridclr = $runtime.source.hybridclr
        il2cpp_plus = $runtime.source.il2cpp_plus
    }
    target = "Android"
    architecture = "arm64-v8a"
    applicationIdentifier = $packageName
    configuration = "Release"
    developmentBuild = $false
    il2cppCodeGeneration = $Il2CppCodeGeneration
    aotMetadataPackaging = $AotMetadataPackaging
    buildIdentitySha256 = $buildIdentitySha256
    stagedRuntimeSha256 = $runtime.stagedRuntimeSha256
    managedAssemblySha256 = (Get-FileHash -LiteralPath $managedBuild -Algorithm SHA256).Hash
    crossAssemblyDerivedSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $managedBuild) "HybridCLR.CrossAssemblyDerived.dll") -Algorithm SHA256).Hash
    metadataStressAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $managedBuild) "HybridCLR.MetadataStress.dll") -Algorithm SHA256).Hash
    metadataStressPrewarmManifestSha256 = $prewarmManifestSha256
    metadataStressSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "managed-cases/HybridCLR.MetadataStress/Generated/MetadataStress.Generated.cs") -Algorithm SHA256).Hash
    metadataBenchmarkPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/metadata-benchmark-policy.json") -Algorithm SHA256).Hash
    aotBenchmarkAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $managedBuild) "Aot/HybridCLR.ManagedCasesAot.dll") -Algorithm SHA256).Hash
    performanceWorkloadSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCases/PerformanceWorkload.cs") -Algorithm SHA256).Hash
    benchmarkPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-policy.json") -Algorithm SHA256).Hash
    testGoldenContractSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/test-golden.json") -Algorithm SHA256).Hash
    benchmarkGoldenSha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-golden.json") -Algorithm SHA256).Hash
    androidToolchain = [ordered]@{
        sdkRoot = $tools.Sdk
        ndkRoot = $tools.Ndk
        buildToolsVersion = $tools.BuildToolsVersion
    }
    apk = [ordered]@{
        path = [IO.Path]::GetRelativePath($LabRoot, $apk).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
        bytes = [int64](Get-Item -LiteralPath $apk).Length
        nativeLibraries = $nativeLibraries
        supplementalAotMetadataBytes = $packagedAotMetadataBytes
    }
}
if (Test-Path -LiteralPath $generatedCpp) {
    $buildManifest.generatedCppSha256 = (Get-FileHash -LiteralPath $generatedCpp -Algorithm SHA256).Hash
}
$aotMetadataDirectory = Join-Path $projectRoot "Assets/StreamingAssets/HybridCLRLab/AotMetadata"
$aotMetadataHashes = [ordered]@{}
if (Test-Path -LiteralPath $aotMetadataDirectory) {
    foreach ($metadataFile in Get-ChildItem -LiteralPath $aotMetadataDirectory -Filter "*.dll.bytes" -File | Sort-Object Name) {
        $aotMetadataHashes[$metadataFile.Name] = (Get-FileHash -LiteralPath $metadataFile.FullName -Algorithm SHA256).Hash
    }
}
$buildManifest.aotMetadataSha256 = $aotMetadataHashes
$manifestOutput = Join-Path $LabRoot "reports/$profileSlug-android-arm64-build-manifest.json"
$buildManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestOutput -Encoding UTF8
Restore-GeneratedUnityMeta -RepoPath $packagePath
Write-Host "Android ARM64 APK: $apk"
Write-Host "Build manifest: $manifestOutput"
