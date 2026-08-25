param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow,
    [string]$OutputRoot = "artifacts/engine-projects"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$source = Join-Path $LabRoot "unity-test-project"
$outputBase = [IO.Path]::GetFullPath((Join-Path $LabRoot $OutputRoot))
$destination = [IO.Path]::GetFullPath((Join-Path $outputBase $EngineWorkflow))
if (-not $destination.StartsWith($outputBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Generated project path escaped its output root: $destination"
}

$workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found." }
$workflow = $workflow[0]

if (Test-Path $destination) {
    $lastDeleteError = $null
    for ($attempt = 1; $attempt -le 5 -and (Test-Path $destination); $attempt++) {
        try {
            Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop
            $lastDeleteError = $null
        } catch {
            $lastDeleteError = $_
            if ($attempt -lt 5) {
                Start-Sleep -Milliseconds 200
            }
        }
    }
    if (Test-Path $destination) {
        throw "Failed to remove generated project '$destination' after 5 attempts: $lastDeleteError"
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $destination "Assets") | Out-Null
Copy-Item -LiteralPath (Join-Path $source "Packages") -Destination $destination -Recurse
Copy-Item -LiteralPath (Join-Path $source "ProjectSettings") -Destination $destination -Recurse
foreach ($asset in @("Editor", "Editor.meta", "Runtime", "Runtime.meta", "Scenes", "Scenes.meta")) {
    Copy-Item -LiteralPath (Join-Path $source "Assets/$asset") -Destination (Join-Path $destination "Assets") -Recurse
}
Get-ChildItem -LiteralPath (Join-Path $destination "Assets") -Filter "*.meta" -File -Recurse |
    Remove-Item -Force

$packageSource = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos/hybridclr_unity"))
$embeddedPackagePath = Join-Path $destination "Packages/com.code-philosophy.hybridclr"
New-Item -ItemType Directory -Force -Path $embeddedPackagePath | Out-Null
Get-ChildItem -LiteralPath $packageSource -Force |
    Where-Object Name -ne '.git' |
    Copy-Item -Destination $embeddedPackagePath -Recurse
$packageManifestPath = Join-Path $destination "Packages/manifest.json"
$packageManifest = Get-Content -Raw $packageManifestPath | ConvertFrom-Json
[void]$packageManifest.dependencies.PSObject.Properties.Remove('com.code-philosophy.hybridclr')
if ($workflow.engine.family -eq "Unity") {
    $unsupportedPackages = @('com.unity.modules.infinity')
    if ($EngineWorkflow -eq "Unity2021Standard") {
        $unsupportedPackages += 'com.unity.ai.navigation'
    }
    foreach ($packageName in $unsupportedPackages) {
        [void]$packageManifest.dependencies.PSObject.Properties.Remove($packageName)
    }
}
$packageManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $packageManifestPath -Encoding UTF8
Remove-Item -LiteralPath (Join-Path $destination "Packages/packages-lock.json") -Force -ErrorAction SilentlyContinue

$projectVersionPath = Join-Path $destination "ProjectSettings/ProjectVersion.txt"
[IO.File]::WriteAllText($projectVersionPath, "m_EditorVersion: $($workflow.engine.unityVersion)`n", (New-Object Text.UTF8Encoding($false)))

Write-Host "Prepared $EngineWorkflow test project: $destination"
Write-Output $destination
