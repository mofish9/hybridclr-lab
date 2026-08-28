param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate")]
    [string]$Profile = "Baseline-Clean",
    [switch]$SkipAssembly,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = Join-Path $LabRoot "unity-test-project"
$lock = Get-Content -Raw (Join-Path $LabRoot "manifests/repo-lock.json") | ConvertFrom-Json
$editor = $lock.engine.executablePath
$runtimeManifest = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"
$runtimeSource = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$managedBuild = Join-Path $LabRoot "artifacts/managed-cases/StandaloneWindows64/HybridCLR.ManagedCases.dll"
$packagePath = Join-Path $LabRoot "../repos/hybridclr_unity"
$profileSlug = $Profile.ToLowerInvariant()
$installEditorLog = Join-Path $LabRoot "reports/$profileSlug-install-editor.log"
$generateEditorLog = Join-Path $LabRoot "reports/$profileSlug-generate-editor.log"
$installedVersion = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/libil2cpp-version.txt"
$methodBridge = Join-Path $projectRoot "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/MethodBridge.cpp"

function Restore-GeneratedUnityMeta([string]$RepoPath) {
    $metaPath = Join-Path $RepoPath "Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta"
    [System.IO.File]::WriteAllText($metaPath, "fileFormatVersion: 2`nguid: 2fa46135129b046a28014d58fdfd18ca")
}

if (-not (Test-Path $editor)) { throw "Tuanjie editor was not found: $editor" }
if (-not $SkipAssembly) {
    & (Join-Path $PSScriptRoot "assemble-runtime.ps1") -LabRoot $LabRoot -Profile $Profile -AllowDirty:$AllowDirty
}
if (-not (Test-Path $runtimeManifest)) { throw "Runtime manifest not found: $runtimeManifest" }
if (-not (Test-Path $runtimeSource)) { throw "Runtime source not found: $runtimeSource" }

& (Join-Path $PSScriptRoot "build-managed-cases.ps1") -LabRoot $LabRoot
if ($LASTEXITCODE -ne 0) { throw "Managed cases build failed." }

& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
foreach ($logPath in @($installEditorLog, $generateEditorLog)) {
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
    "-labRuntimeSource", $runtimeSource,
    "-logFile", $installEditorLog
)
$installProcess = Start-Process -FilePath $editor -ArgumentList $installArgs -PassThru -WindowStyle Hidden
$installProcess.WaitForExit()
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
if ($installProcess.ExitCode -ne 0) {
    throw "Tuanjie runtime installation process exited with code $($installProcess.ExitCode). See $installEditorLog"
}
if (-not (Test-Path $installedVersion) -or (Get-Content -Raw $installedVersion).Trim() -ne "8.13.0") {
    throw "Tuanjie runtime installation did not produce the locked 8.13.0 runtime. See $installEditorLog"
}

& (Join-Path $PSScriptRoot "clear-unity-project-locks.ps1") -ProjectRoot $projectRoot
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot

$generateArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectRoot,
    "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.GenerateOnly",
    "-labProfile", $Profile,
    "-labManagedDll", $managedBuild,
    "-logFile", $generateEditorLog
)
if (Test-Path $methodBridge) {
    Remove-Item -LiteralPath $methodBridge -Force
}
$generateProcess = Start-Process -FilePath $editor -ArgumentList $generateArgs -PassThru -WindowStyle Hidden
$generateProcess.WaitForExit()
& (Join-Path $PSScriptRoot "wait-for-tuanjie-project-exit.ps1") -ProjectRoot $projectRoot
if ($generateProcess.ExitCode -ne 0) {
    throw "Tuanjie generation process exited with code $($generateProcess.ExitCode). See $generateEditorLog"
}
if (-not (Test-Path $methodBridge)) { throw "HybridCLR generation did not produce MethodBridge.cpp. See $generateEditorLog" }
Restore-GeneratedUnityMeta -RepoPath $packagePath
Write-Host "Generated runtime profile: $Profile"
Write-Host "MethodBridge: $methodBridge"
