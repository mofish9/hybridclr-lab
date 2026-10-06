param(
    [string]$BuildRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/as3'),
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
$labRoot=Split-Path -Parent $PSScriptRoot
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest=Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
$engineExternal=Join-Path $EditorRoot 'Editor/Data/il2cpp/external'
$sourceRoot=Join-Path $BuildRoot 'sources/DHE/il2cpp_plus'
$externalLink=Join-Path $sourceRoot 'external'
if(-not(Test-Path -LiteralPath $externalLink)) { New-Item -ItemType Junction -Path $externalLink -Target $engineExternal | Out-Null }
if((Get-Item -LiteralPath $externalLink).Target -ne $engineExternal.Replace('/','\')) {
    if([IO.Path]::GetFullPath((Get-Item -LiteralPath $externalLink).Target) -ne [IO.Path]::GetFullPath($engineExternal)) { throw 'Wrong engine headers' }
}
foreach($enabled in @(1,0)) {
    $output=Join-Path $BuildRoot "native-$enabled"
    & $cmake -S (Join-Path $labRoot 'native-unit-tests') -B $output -G 'Visual Studio 17 2022' -A x64 "-DHYBRIDCLR_RUNTIME_ROOT=$sourceRoot/libil2cpp" "-DIL2CPP_EXTERNAL=$engineExternal" "-DIL2CPP_BASELIB_INCLUDE=$engineExternal/baselib/Include" "-DIL2CPP_BASELIB_PLATFORM_INCLUDE=$engineExternal/baselib/Platforms/Windows/Include" -DHYBRIDCLR_TEST_UNITY_VERSION=20220362 -DHYBRIDCLR_TEST_TUANJIE_VERSION=0 -DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=1 "-DHYBRIDCLR_TEST_AOT_SELECTION=$enabled" *> (Join-Path $BuildRoot "native-$enabled-configure.log")
    if($LASTEXITCODE -ne 0) { throw "Native $enabled configure failed" }
    & $cmake --build $output --config Release --parallel 4 *> (Join-Path $BuildRoot "native-$enabled-build.log")
    if($LASTEXITCODE -ne 0) { throw "Native $enabled compile failed" }
    & $ctest --test-dir $output -C Release --output-on-failure *> (Join-Path $BuildRoot "native-$enabled-ctest.log")
    if($LASTEXITCODE -ne 0) { throw "Native $enabled CTest failed" }
    Write-Host "Unity2022 real-header compile and ordinary native CTest passed; selection=$enabled"
}
$report=@{format='hybridclr.aot-isolation-native.v2';engine='Unity2022.3.62f3';surrogateExternalHeadersUsed=$false;selectionEnabledCompile=$true;selectionDisabledCompile=$true;ordinaryNativeCtest=$true;startupBehaviorProvenByThisCtest=$false;engineExternal=$engineExternal;labCommit=(& git -C $labRoot rev-parse HEAD).Trim();sources=(Get-Content (Join-Path $BuildRoot 'source-identities.json') -Raw | ConvertFrom-Json).DHE}
[IO.File]::WriteAllText((Join-Path $BuildRoot 'native-gate.json'),($report | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
