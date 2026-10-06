param(
    [string]$PackageRoot='C:/hybridclr_optimize/worktrees/hybridclr-unity-aot-select-v1',
    [string]$PreviousBuildRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/as'),
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/aot-mode-validator'),
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$labRoot=Split-Path -Parent $PSScriptRoot
$dotnet='C:/Program Files/dotnet/dotnet.exe'
$csc='C:/Program Files/dotnet/sdk/6.0.428/Roslyn/bincore/csc.dll'
$cases=@()
foreach($variant in @('COMPONENT','SCRIPTABLE','CALLBACK')) {
    $dir=Join-Path $OutputRoot $variant;New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $dll=Join-Path $dir 'StartupHotfix.dll'
    & $dotnet $csc /nologo /target:library /nostdlib /deterministic /optimize+ "/define:$variant" "/out:$dll" "/reference:$EditorRoot/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll" "/reference:$EditorRoot/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll" (Join-Path $labRoot 'fixtures/aot-isolation-validator/UnsafeHotfix.cs')
    if($LASTEXITCODE -ne 0){throw "Negative fixture compile failed: $variant"}
    $cases+=$dll
}
& $dotnet build (Join-Path $labRoot 'fixtures/aot-mode-validator/Validator.csproj') -c Release "-p:PackageRoot=$PackageRoot" -o (Join-Path $OutputRoot 'runner') --nologo *> (Join-Path $OutputRoot 'build.log')
if($LASTEXITCODE -ne 0){throw 'Validator harness compile failed'}
$result=& $dotnet (Join-Path $OutputRoot 'runner/Validator.dll') $PreviousBuildRoot @cases
if($LASTEXITCODE -ne 0){throw 'Validator rejection gate failed'}
$result | Write-Output
$report=@{result=($result | ConvertFrom-Json);packageCommit=(& git -C $PackageRoot rev-parse HEAD).Trim();validatorSha256=(Get-FileHash (Join-Path $PackageRoot 'Editor/BuildProcessors/AotModeDependencyValidator.cs')).Hash.ToLowerInvariant();fixtureSha256=(Get-FileHash (Join-Path $labRoot 'fixtures/aot-isolation-validator/UnsafeHotfix.cs')).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $OutputRoot 'validator-gate.json'),($report | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
