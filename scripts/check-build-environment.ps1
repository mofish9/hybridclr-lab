param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [ValidateSet("StandaloneWindows64", "Android")]
    [string]$Target = "StandaloneWindows64"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found." }
$workflow = $workflow[0]
$editor = $workflow.engine.executablePath
$editorExists = Test-Path $editor
$editorVersion = if ($editorExists) { (Get-Item $editor).VersionInfo.ProductVersion } else { "" }
$editorVersionPrefix = ($workflow.engine.unityVersion -split '[ft]')[0]
$editorVersionOk = $editorVersion -like "$editorVersionPrefix*"

$playbackEngines = if ($editorExists) { Join-Path (Split-Path $editor) "Data/PlaybackEngines" } else { "" }
$windowsSupport = if ($playbackEngines) { Test-Path (Join-Path $playbackEngines "windowsstandalonesupport") } else { $false }
$androidSupport = if ($playbackEngines) { Test-Path (Join-Path $playbackEngines "AndroidPlayer") } else { $false }
$androidSdk = if ($androidSupport) { Test-Path (Join-Path $playbackEngines "AndroidPlayer/SDK") } else { $false }
$androidNdk = if ($androidSupport) { Test-Path (Join-Path $playbackEngines "AndroidPlayer/NDK") } else { $false }
$androidJdk = if ($androidSupport) { Test-Path (Join-Path $playbackEngines "AndroidPlayer/OpenJDK") } else { $false }

$vswhereCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
)
$vswhere = $vswhereCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$vsInstallation = ""
if ($vswhere) {
    $vsInstallation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null | Select-Object -First 1).Trim()
}
$windowsSdk = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
    "${env:ProgramFiles}\Windows Kits\10\bin"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$requirements = [ordered]@{
    editor = $editorExists -and $editorVersionOk
    windowsStandaloneSupport = $windowsSupport
    androidSupport = $androidSupport -and $androidSdk -and $androidNdk -and $androidJdk
    visualStudioCpp = -not [string]::IsNullOrWhiteSpace($vsInstallation)
    windowsSdk = -not [string]::IsNullOrWhiteSpace($windowsSdk)
}
$targetReady = if ($Target -eq "Android") {
    $requirements.editor -and $requirements.androidSupport
} else {
    $requirements.editor -and $requirements.windowsStandaloneSupport -and $requirements.visualStudioCpp -and $requirements.windowsSdk
}

$report = [ordered]@{
    schemaVersion = 1
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    target = $Target
    engineWorkflow = $EngineWorkflow
    editorPath = $editor
    editorProductVersion = $editorVersion
    visualStudioInstallation = $vsInstallation
    windowsSdkPath = $windowsSdk
    requirements = $requirements
    ready = $targetReady
}
$reportPath = Join-Path $LabRoot "reports/build-environment.json"
New-Item -ItemType Directory -Force -Path (Split-Path $reportPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 8 | Write-Host
if (-not $targetReady) {
    $requiredNames = if ($Target -eq "Android") {
        @("editor", "androidSupport")
    } else {
        @("editor", "windowsStandaloneSupport", "visualStudioCpp", "windowsSdk")
    }
    $missingNames = @($requiredNames | Where-Object { -not [bool]$requirements[$_] })
    throw "$Target requirements are incomplete for '$EngineWorkflow': $($missingNames -join ', '). See $reportPath"
}
