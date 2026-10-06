param(
    [string]$WorkspaceRoot='C:/hybridclr_optimize',
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/ar1'),
    [ValidateSet('Prepare','Candidate','Baseline','Reference','Package')][string]$Stage='Prepare',
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$labRoot=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $labRoot 'fixtures/aot-mode-release'
$helper=Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1'
$dotnet='C:/Program Files/dotnet/dotnet.exe'
if($Stage -eq 'Prepare') {
    $refs=@{}
    foreach($entry in @(@('hybridclr','hybridclr-aot-select-v1'),@('il2cpp_plus','il2cpp-aot-select-v1'),@('hybridclr_unity','hybridclr-unity-aot-select-v1'))) {
        $repo=Join-Path $WorkspaceRoot ('worktrees/'+$entry[1]);if(@(& git -C $repo status --porcelain).Count){throw "Commit $repo before export"}
        $refs[$entry[0]]=(& git -C $repo rev-parse HEAD).Trim()
    }
    & $helper -WorkspaceRoot $WorkspaceRoot -OutputRoot (Join-Path $OutputRoot 'candidate') -EditorRoot $EditorRoot -Stage Prepare -DheHybridClrRef $refs.hybridclr -DheIl2CppRef $refs.il2cpp_plus -DhePackageRef $refs.hybridclr_unity -FixtureRootOverride $fixture -DheScriptingDefines STARTUP_MODE_SELECTION
    & $helper -WorkspaceRoot $WorkspaceRoot -OutputRoot (Join-Path $OutputRoot 'baseline') -EditorRoot $EditorRoot -Stage Prepare -DhePackageRef $refs.hybridclr_unity -FixtureRootOverride $fixture -Profiles DHE
    foreach($side in @('candidate','baseline')) {
        & $dotnet 'C:/Program Files/dotnet/sdk/6.0.428/Roslyn/bincore/csc.dll' /nologo /target:library /deterministic /optimize+ /nostdlib "/reference:$EditorRoot/Editor/Data/MonoBleedingEdge/lib/mono/4.5/mscorlib.dll" "/reference:$OutputRoot/$side/shared/StartupHotfix.dll" "/out:$OutputRoot/$side/shared/StartupConsumer.dll" (Join-Path $fixture 'Consumer.cs')
        if($LASTEXITCODE -ne 0){throw 'Consumer compilation failed'}
    }
    foreach($name in @('StartupHotfix','StartupConsumer','StartupUnityHotfix','StartupAotSupport')) {
        if((Get-FileHash "$OutputRoot/candidate/shared/$name.dll").Hash -ne (Get-FileHash "$OutputRoot/baseline/shared/$name.dll").Hash){throw "Payload drift: $name"}
    }
} elseif($Stage -eq 'Package') {
    $manifest=[ordered]@{format='hybridclr.aot-mode.release-build.v1';engine='Unity2022.3.62f3';fixtureCommit=(& git -C $labRoot rev-parse HEAD).Trim();diagnostics=$false;ordinaryAotGuards=$true;supplementalAotMetadata=$true;payloads=@{};profiles=@{};fixtureHashes=@{}}
    foreach($file in Get-ChildItem -LiteralPath $fixture -File){$manifest.fixtureHashes[$file.Name]=(Get-FileHash $file.FullName).Hash.ToLowerInvariant()}
    foreach($side in @('candidate','baseline')) {
        $tool=Join-Path $OutputRoot "$side/sources/DHE/hybridclr_unity/Tools~/DHE/HybridCLR.DheTool.dll"
        foreach($name in @('StartupHotfix','StartupUnityHotfix')) {
            foreach($kind in @('base','current')) {
                $assembly=if($kind -eq 'base'){"$OutputRoot/$side/DHE/baseline/$name.dll"}else{"$OutputRoot/$side/shared/$name.dll"}
                & $dotnet $tool mv -Assembly $assembly -Output "$OutputRoot/$side/shared/$name.$kind.mv.json" -Binary "$OutputRoot/$side/shared/$name.$kind.mv"
                if($LASTEXITCODE -ne 0){throw "MV generation failed: $side $name $kind"}
            }
        }
        foreach($profile in $(if($side -eq 'candidate'){@('DHE','LegacyInterpreter')}else{@('DHE')})) {
            $key="$side/$profile";$player="$OutputRoot/$key/player"
            foreach($name in @('PlayerRunner.cs','FixtureBuild.cs','StartupControl.cs','Workload.cs')) {
                $area=if($name -eq 'FixtureBuild.cs'){'Editor'}else{'Runtime'}
                if((Get-FileHash "$OutputRoot/$side/projects/$profile/Assets/$area/$name").Hash.ToLowerInvariant() -ne $manifest.fixtureHashes[$name]){throw 'Stale staged fixture'}
            }
            $manifest.profiles[$key]=@{sources=(Get-Content "$OutputRoot/$side/source-identities.json" -Raw | ConvertFrom-Json).$profile;gameAssemblySha256=(Get-FileHash "$player/GameAssembly.dll").Hash.ToLowerInvariant();metadataSha256=(Get-FileHash "$player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat").Hash.ToLowerInvariant()}
        }
    }
    foreach($name in @('StartupHotfix','StartupConsumer','StartupUnityHotfix','StartupAotSupport')) {
        $candidate=(Get-FileHash "$OutputRoot/candidate/shared/$name.dll").Hash.ToLowerInvariant()
        if($candidate -ne (Get-FileHash "$OutputRoot/baseline/shared/$name.dll").Hash.ToLowerInvariant()){throw "Payload drift: $name"}
        $manifest.payloads[$name]=$candidate
    }
    [IO.File]::WriteAllText((Join-Path $OutputRoot 'build.json'),($manifest | ConvertTo-Json -Depth 7).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
    & $dotnet build (Join-Path $fixture 'Reference.csproj') -c Release -o (Join-Path $OutputRoot 'reference') --nologo
    if($LASTEXITCODE -ne 0){throw 'CLR reference build failed'}
} else {
    $side=if($Stage -eq 'Baseline'){'baseline'}else{'candidate'}
    $profile=if($Stage -eq 'Reference'){'LegacyInterpreter'}else{'DHE'}
    & $helper -WorkspaceRoot $WorkspaceRoot -OutputRoot (Join-Path $OutputRoot $side) -EditorRoot $EditorRoot -Stage $profile -FixtureRootOverride $fixture
}
