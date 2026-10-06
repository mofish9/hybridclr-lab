param(
    [string]$WorkspaceRoot='C:/hybridclr_optimize',
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/as4'),
    [ValidateSet('Prepare','DHE','LegacyInterpreter','Package')][string]$Stage='Prepare',
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$labRoot=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $labRoot 'fixtures/aot-isolation-v2'
$hclrRepo=Join-Path $WorkspaceRoot 'worktrees/hybridclr-aot-select-v1'
$il2cppRepo=Join-Path $WorkspaceRoot 'worktrees/il2cpp-aot-select-v1'
$hclr=(& git -C $hclrRepo rev-parse HEAD).Trim(); if($LASTEXITCODE -ne 0){throw 'Runtime ref missing'}
$packageRepo=Join-Path $WorkspaceRoot 'worktrees/hybridclr-unity-aot-select-v1'
$package=(& git -C $packageRepo rev-parse HEAD).Trim()
$il2cpp=(& git -C $il2cppRepo rev-parse HEAD).Trim(); if($LASTEXITCODE -ne 0){throw 'IL2CPP ref missing'}
if($Stage -eq 'Prepare') {
    if(@(& git -C $packageRepo status --porcelain).Count -or @(& git -C $hclrRepo status --porcelain).Count -or @(& git -C $il2cppRepo status --porcelain).Count){throw 'Commit source candidates before exporting'}
} else {
    # Later stages build the immutable exported combination, even if work on a
    # subsequent candidate has begun in the source worktrees.
    $exported=Get-Content (Join-Path $OutputRoot 'source-identities.json') -Raw | ConvertFrom-Json
    $hclr=$exported.DHE.hybridclr; $il2cpp=$exported.DHE.il2cpp_plus; $package=$exported.DHE.hybridclr_unity
}
$dotnet='C:/Program Files/dotnet/dotnet.exe'
if($Stage -eq 'Prepare'){
    & (Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1') -WorkspaceRoot $WorkspaceRoot -OutputRoot $OutputRoot -EditorRoot $EditorRoot -Stage Prepare -DheHybridClrRef $hclr -DheIl2CppRef $il2cpp -DhePackageRef $package -FixtureRootOverride $fixture
    & $dotnet 'C:/Program Files/dotnet/sdk/6.0.428/Roslyn/bincore/csc.dll' /nologo /target:library /optimize+ /deterministic /nostdlib "/reference:$(Join-Path $EditorRoot 'Editor/Data/MonoBleedingEdge/lib/mono/4.5/mscorlib.dll')" "/reference:$(Join-Path $OutputRoot 'shared/StartupHotfix.dll')" "/out:$(Join-Path $OutputRoot 'shared/StartupConsumer.dll')" (Join-Path $fixture 'Consumer.cs')
    if($LASTEXITCODE -ne 0){throw 'Consumer compilation failed'}
}elseif($Stage -eq 'Package'){
    $tool=Join-Path $OutputRoot 'sources/DHE/hybridclr_unity/Tools~/DHE/HybridCLR.DheTool.dll'
    foreach($side in @('base','current')){
        $assembly=if($side -eq 'base'){Join-Path $OutputRoot 'DHE/baseline/StartupHotfix.dll'}else{Join-Path $OutputRoot 'shared/StartupHotfix.dll'}
        & $dotnet $tool mv -Assembly $assembly -Output (Join-Path $OutputRoot "shared/$side.mv.json") -Binary (Join-Path $OutputRoot "shared/$side.mv")
        if($LASTEXITCODE -ne 0){throw 'MetaVersion generation failed'}
    }
    $sourceHashes=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $fixture -File){$sourceHashes[$file.Name]=(Get-FileHash $file.FullName).Hash.ToLowerInvariant()}
    foreach($profile in @('DHE','LegacyInterpreter')){
        foreach($name in @('PlayerRunner.cs','FixtureBuild.cs','StartupControl.cs','Workload.cs')){
            $area=if($name -eq 'FixtureBuild.cs'){'Editor'}else{'Runtime'}
            if((Get-FileHash (Join-Path $OutputRoot "projects/$profile/Assets/$area/$name")).Hash.ToLowerInvariant() -ne $sourceHashes[$name]){throw 'Stale staged fixture'}
        }
    }
    $identities=Get-Content (Join-Path $OutputRoot 'source-identities.json') -Raw | ConvertFrom-Json
    if($identities.DHE.hybridclr_unity -ne $package -or $identities.DHE.hybridclr -ne $hclr -or $identities.DHE.il2cpp_plus -ne $il2cpp){throw 'Exported runtime refs drifted'}
    $manifest=[ordered]@{
        format='hybridclr.aot-isolation-build.v2';scope='aot-selection-with-extension-binding-and-build-rejection';engine='Unity2022.3.62f3'
        fixtureCommit=(& git -C $labRoot rev-parse HEAD).Trim();fixtureHashes=$sourceHashes;sources=$identities
        currentSha256=(Get-FileHash (Join-Path $OutputRoot 'shared/StartupHotfix.dll')).Hash.ToLowerInvariant()
        consumerSha256=(Get-FileHash (Join-Path $OutputRoot 'shared/StartupConsumer.dll')).Hash.ToLowerInvariant()
        ordinaryAotGuards=$false;diagnostics=$true;runtimeSwapping=$false;supplementalAotMetadata=$false
    }
    foreach($profile in @('DHE','LegacyInterpreter')){
        $manifest[$profile]=@{gameAssemblySha256=(Get-FileHash (Join-Path $OutputRoot "$profile/player/GameAssembly.dll")).Hash.ToLowerInvariant();metadataSha256=(Get-FileHash (Join-Path $OutputRoot "$profile/player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat")).Hash.ToLowerInvariant()}
    }
    [IO.File]::WriteAllText((Join-Path $OutputRoot 'probe-build.json'),($manifest | ConvertTo-Json -Depth 7).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
    & $dotnet build (Join-Path $fixture 'Reference.csproj') -c Release -o (Join-Path $OutputRoot 'reference') --nologo
    if($LASTEXITCODE -ne 0){throw 'CLR reference build failed'}
}else{
    & (Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1') -WorkspaceRoot $WorkspaceRoot -OutputRoot $OutputRoot -EditorRoot $EditorRoot -Stage $Stage -DheHybridClrRef $hclr -DheIl2CppRef $il2cpp -DhePackageRef $package -FixtureRootOverride $fixture
}
