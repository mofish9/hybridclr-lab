param(
    [string]$WorkspaceRoot='C:/hybridclr_optimize',
    [string]$PackageRoot='C:/hybridclr_optimize/worktrees/hybridclr-unity-dhe-startup-selection-v1',
    [string]$BackendRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/sr'),
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-router-player'),
    [ValidateSet('Prepare','DHE','LegacyInterpreter','Package')][string]$Stage='Prepare'
)
$ErrorActionPreference='Stop'
$labRoot=Split-Path -Parent $PSScriptRoot
if ($Stage -eq 'Prepare') {
    & (Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1') -WorkspaceRoot $WorkspaceRoot -OutputRoot $BackendRoot -Stage Prepare
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        $runtime=Join-Path $BackendRoot "projects/$profile/Assets/Runtime"
        Copy-Item -LiteralPath (Join-Path $labRoot 'fixtures/startup-router/PlayerRunner.cs') -Destination (Join-Path $runtime 'PlayerRunner.cs') -Force
        Copy-Item -LiteralPath (Join-Path $PackageRoot 'Runtime/Startup/HybridStartup.cs') -Destination (Join-Path $runtime 'HybridStartup.cs')
    }
} elseif ($Stage -eq 'Package') {
    $dotnet='C:/Program Files/dotnet/dotnet.exe'
    $tool=Join-Path $BackendRoot 'sources/DHE/hybridclr_unity/Tools~/DHE/HybridCLR.DheTool.dll'
    foreach ($side in @('base','current')) {
        $assembly=if ($side -eq 'base') { Join-Path $BackendRoot 'DHE/baseline/StartupHotfix.dll' } else { Join-Path $BackendRoot 'shared/StartupHotfix.dll' }
        & $dotnet $tool mv -Assembly $assembly -Output (Join-Path $BackendRoot "shared/$side.mv.json") -Binary (Join-Path $BackendRoot "shared/$side.mv")
        if ($LASTEXITCODE -ne 0) { throw 'MV build failed.' }
    }
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        if ((Get-FileHash (Join-Path $BackendRoot "projects/$profile/Assets/Runtime/HybridStartup.cs")).Hash -ne (Get-FileHash (Join-Path $PackageRoot 'Runtime/Startup/HybridStartup.cs')).Hash) { throw 'Managed API source identity drift.' }
        if ((Get-FileHash (Join-Path $BackendRoot "projects/$profile/Assets/Runtime/PlayerRunner.cs")).Hash -ne (Get-FileHash (Join-Path $labRoot 'fixtures/startup-router/PlayerRunner.cs')).Hash) { throw 'Player fixture source identity drift.' }
    }
    & (Join-Path $PackageRoot 'Tools~/Startup/Package-Windows.ps1') -DhePlayer (Join-Path $BackendRoot 'DHE/player') -LegacyPlayer (Join-Path $BackendRoot 'LegacyInterpreter/player') -Output $OutputRoot -Scope 'windows-startup-router-v1' -Engine 'Unity2022.3.62f3' -SourceIdentityFile (Join-Path $BackendRoot 'source-identities.json') -ManagedOnly
    Copy-Item -LiteralPath (Join-Path $BackendRoot 'shared') -Destination $OutputRoot -Recurse
    $manifestPath=Join-Path $OutputRoot 'startup-pair.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $manifest.currentSha256=(Get-FileHash (Join-Path $OutputRoot 'shared/StartupHotfix.dll')).Hash.ToLowerInvariant()
    $manifest.fixtureCommit=(& git -C $labRoot rev-parse HEAD).Trim()
    $manifest.fixtureSha256=(Get-FileHash (Join-Path $labRoot 'fixtures/startup-router/PlayerRunner.cs')).Hash.ToLowerInvariant()
    $manifest.managedApiSha256=(Get-FileHash (Join-Path $PackageRoot 'Runtime/Startup/HybridStartup.cs')).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 10).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
} else {
    & (Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1') -WorkspaceRoot $WorkspaceRoot -OutputRoot $BackendRoot -Stage $Stage
}
