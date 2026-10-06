param(
    [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-windows-v1'),
    [string]$EditorRoot = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3',
    [ValidateSet('Prepare', 'DHE', 'LegacyInterpreter', 'Package')][string]$Stage = 'Prepare',
    [string]$DheHybridClrRef = '9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071',
    [string]$DheIl2CppRef = 'e426adc57c283865126423b169051558b339388c',
    [string]$DhePackageRef = 'f2946d5ba35a879724b76afd857176feb1a4adca',
    [string]$FixtureRootOverride,
    [switch]$SkipInstall
)
$ErrorActionPreference = 'Stop'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$labRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $labRoot 'fixtures/startup-windows'
if ($FixtureRootOverride) { $fixtureRoot = [IO.Path]::GetFullPath($FixtureRootOverride) }
$editor = Join-Path $EditorRoot 'Editor/Unity.exe'
$dotnet = 'C:/Program Files/dotnet/dotnet.exe'
$sources = @{
    DHE = @{ hybridclr=$DheHybridClrRef; il2cpp_plus=$DheIl2CppRef; hybridclr_unity=$DhePackageRef }
    LegacyInterpreter = @{ hybridclr='f40c6f08ccd0391ad9285276b4cc21ada3a180ab'; il2cpp_plus='bf15337e189ae7da5876aa51c9b896a36c52a155'; hybridclr_unity='ac0fdc5c6363a1b6323d017e068c536dd22127dc' }
}
function Export-Repo([string]$Repo, [string]$Commit, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $archive = $Destination + '.tar'
    & git -C (Join-Path $WorkspaceRoot "repos/$Repo") archive --format=tar --output=$archive $Commit
    if ($LASTEXITCODE -ne 0) { throw "Archive failed: $Repo $Commit" }
    & tar -xf $archive -C $Destination
    if ($LASTEXITCODE -ne 0) { throw "Extract failed: $archive" }
}
function Write-Text([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
function Invoke-Editor([string]$Project, [string]$Method, [string]$Profile) {
    $profileRoot = Join-Path $OutputRoot $Profile
    $runtime = Join-Path $OutputRoot "sources/$Profile/il2cpp_plus/libil2cpp"
    $log = Join-Path $OutputRoot "$Profile-$($Method.Split('.')[-1]).log"
    $start = [Diagnostics.ProcessStartInfo]::new($editor)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('-batchmode','-nographics','-quit','-projectPath',$Project,'-buildTarget','Win64','-executeMethod',$Method,
        '-startupRuntime',$runtime,'-startupBaseRoot',(Join-Path $OutputRoot 'base'),'-startupOutput',$profileRoot,'-logFile',$log)) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(1800000)) { $process.Kill($true); throw "Unity timed out. See $log" }
        if ($process.ExitCode -ne 0) { throw "Unity $Method failed ($($process.ExitCode)). See $log" }
    } finally { $process.Dispose() }
    if ($Method.EndsWith('.Build') -and -not (Test-Path -LiteralPath (Join-Path $profileRoot 'player/StartupPlayer.exe'))) {
        throw "Unity returned without producing the Player: $Profile"
    }
}
if ($Stage -eq 'Prepare') {
    if (Test-Path -LiteralPath $OutputRoot) { throw "OutputRoot must be new: $OutputRoot" }
    New-Item -ItemType Directory -Path $OutputRoot | Out-Null
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        $sourceRoot = Join-Path $OutputRoot "sources/$profile"
        foreach ($repo in @('hybridclr','il2cpp_plus','hybridclr_unity')) {
            Export-Repo $repo $sources[$profile][$repo] (Join-Path $sourceRoot $repo)
        }
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'hybridclr/hybridclr') -Destination (Join-Path $sourceRoot 'il2cpp_plus/libil2cpp/hybridclr') -Recurse
        $project = Join-Path $OutputRoot "projects/$profile"
        foreach ($dir in @('Assets/Runtime','Assets/Editor','Assets/Plugins','Packages','ProjectSettings')) {
            New-Item -ItemType Directory -Path (Join-Path $project $dir) -Force | Out-Null
        }
        Copy-Item (Join-Path $fixtureRoot 'StartupControl.cs') (Join-Path $project 'Assets/Runtime/StartupControl.cs')
        Copy-Item (Join-Path $fixtureRoot 'PlayerRunner.cs') (Join-Path $project 'Assets/Runtime/PlayerRunner.cs')
        Copy-Item (Join-Path $fixtureRoot 'FixtureBuild.cs') (Join-Path $project 'Assets/Editor/FixtureBuild.cs')
        if (Test-Path -LiteralPath (Join-Path $fixtureRoot 'Workload.cs')) {
            Copy-Item (Join-Path $fixtureRoot 'Workload.cs') (Join-Path $project 'Assets/Runtime/Workload.cs')
        }
        $package = (Join-Path $sourceRoot 'hybridclr_unity').Replace('\','/')
        Write-Text (Join-Path $project 'Packages/manifest.json') (@{dependencies=@{'com.code-philosophy.hybridclr'="file:$package";'com.unity.modules.jsonserialize'='1.0.0'}} | ConvertTo-Json -Depth 3)
        Write-Text (Join-Path $project 'ProjectSettings/ProjectVersion.txt') "m_EditorVersion: 2022.3.62f3`nm_EditorVersionWithRevision: 2022.3.62f3 (96770f904ca7)`n"
        if ($profile -eq 'DHE') {
            Write-Text (Join-Path $project 'Assets/csc.rsp') "-define:STARTUP_DHE_PROFILE`n"
            $template = Get-Content (Join-Path $sourceRoot 'hybridclr_unity/ToolsSource~/DHE/templates/DheBuildIdentity.cs') -Raw
            Write-Text (Join-Path $project 'Assets/Runtime/DheBuildIdentity.cs') $template.Replace('__DHE_IDENTITY_NAMESPACE__','StartupWindowsFixture')
        }
    }
    foreach ($dir in @('base','shared')) { New-Item -ItemType Directory -Path (Join-Path $OutputRoot $dir) | Out-Null }
    $csc = 'C:/Program Files/dotnet/sdk/6.0.428/Roslyn/bincore/csc.dll'
    $mscorlib = Join-Path $EditorRoot 'Editor/Data/MonoBleedingEdge/lib/mono/4.5/mscorlib.dll'
    foreach ($kind in @('base','shared')) {
        $arguments = @($csc,'/nologo','/target:library','/optimize+','/nostdlib',"/reference:$mscorlib","/out:$(Join-Path $OutputRoot "$kind/StartupHotfix.dll")",(Join-Path $fixtureRoot 'Hotfix.cs'))
        if ($kind -eq 'shared') { $arguments += '/define:CURRENT_PAYLOAD' }
        & $dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Hotfix compilation failed: $kind" }
    }
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        Copy-Item (Join-Path $OutputRoot 'base/StartupHotfix.dll') (Join-Path $OutputRoot "projects/$profile/Assets/Plugins/StartupHotfix.dll")
    }
    Write-Text (Join-Path $OutputRoot 'source-identities.json') ($sources | ConvertTo-Json -Depth 4)
    Write-Output "Prepared source-locked projects: $OutputRoot"
} elseif ($Stage -eq 'DHE' -or $Stage -eq 'LegacyInterpreter') {
    $project = Join-Path $OutputRoot "projects/$Stage"
    if (-not $SkipInstall) { Invoke-Editor $project 'StartupWindowsFixture.FixtureBuild.Install' $Stage }
    Invoke-Editor $project 'StartupWindowsFixture.FixtureBuild.Build' $Stage
} elseif ($Stage -eq 'Package') {
    $tool = Join-Path $OutputRoot 'sources/DHE/hybridclr_unity/Tools~/DHE/HybridCLR.DheTool.dll'
    foreach ($side in @('base','current')) {
        $assembly = if ($side -eq 'base') { Join-Path $OutputRoot 'DHE/baseline/StartupHotfix.dll' } else { Join-Path $OutputRoot 'shared/StartupHotfix.dll' }
        & $dotnet $tool mv -Assembly $assembly -Output (Join-Path $OutputRoot "shared/$side.mv.json") -Binary (Join-Path $OutputRoot "shared/$side.mv")
        if ($LASTEXITCODE -ne 0) { throw "MV generation failed: $side" }
    }
    $codeCommit = (& git -C $labRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve fixture commit.' }
    $fixtureHashes = [ordered]@{}
    foreach ($name in @('StartupControl.cs','PlayerRunner.cs','FixtureBuild.cs','Hotfix.cs','Launcher.cs','Launcher.csproj')) {
        $fixtureHashes[$name] = (Get-FileHash (Join-Path $fixtureRoot $name)).Hash.ToLowerInvariant()
    }
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        foreach ($name in @('StartupControl.cs','PlayerRunner.cs','FixtureBuild.cs')) {
            $area = if ($name -eq 'FixtureBuild.cs') { 'Editor' } else { 'Runtime' }
            $stagedHash = (Get-FileHash (Join-Path $OutputRoot "projects/$profile/Assets/$area/$name")).Hash.ToLowerInvariant()
            if ($stagedHash -ne $fixtureHashes[$name]) { throw "Project fixture source is stale: $profile $name" }
        }
    }
    $pair = [ordered]@{
        format='hybridclr.startup-windows-pair.json'; scope='two-independent-windows-il2cpp-players'
        fixtureCommit=$codeCommit; fixtureSourceSha256=$fixtureHashes
        engine='Unity2022.3.62f3'; il2cppCodeGeneration='OptimizeSize'
        dheDispatchDiagnostics=$true; ordinaryAotGuards=$false
        currentSha256=(Get-FileHash (Join-Path $OutputRoot 'shared/StartupHotfix.dll')).Hash.ToLowerInvariant(); sourceIdentities=$sources
    }
    foreach ($profile in @('DHE','LegacyInterpreter')) {
        $prefix = "$profile/player"
        $pair[$profile] = @{
            player="$prefix/StartupPlayer.exe"; gameAssembly="$prefix/GameAssembly.dll"; metadata="$prefix/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat"
            playerSha256=(Get-FileHash (Join-Path $OutputRoot "$prefix/StartupPlayer.exe")).Hash.ToLowerInvariant()
            gameAssemblySha256=(Get-FileHash (Join-Path $OutputRoot "$prefix/GameAssembly.dll")).Hash.ToLowerInvariant()
            metadataSha256=(Get-FileHash (Join-Path $OutputRoot "$prefix/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat")).Hash.ToLowerInvariant()
            gameAssemblyBytes=(Get-Item (Join-Path $OutputRoot "$prefix/GameAssembly.dll")).Length
        }
    }
    Write-Text (Join-Path $OutputRoot 'pair.json') ($pair | ConvertTo-Json -Depth 5)
    & $dotnet build (Join-Path $fixtureRoot 'Launcher.csproj') -c Release -o (Join-Path $OutputRoot 'launcher') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
}
