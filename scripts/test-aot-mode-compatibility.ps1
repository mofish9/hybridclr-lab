param(
    [string]$WorkspaceRoot='C:/hybridclr_optimize',
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/amc1')
)
$ErrorActionPreference='Stop'
$labRoot=Split-Path -Parent $PSScriptRoot
$runtimeRepo=Join-Path $WorkspaceRoot 'worktrees/hybridclr-aot-select-v1'
if(@(& git -C $runtimeRepo status --porcelain).Count){throw 'Runtime must be committed'}
$runtimeCommit=(& git -C $runtimeRepo rev-parse HEAD).Trim()
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $OutputRoot){throw 'Use a new output directory'}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$cmake='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest=Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
$profiles=@(
    @{name='Unity2021';commit='10cbacd02b3ed291af6a6a34d444ae50c85462e0';editor='C:/Program Files/Unity/Hub/Editor/2021.3.45f2';unity=20210345;tuanjie=0;fgs=0},
    @{name='Tuanjie2022';commit='52968ad6c88416f212d09d919b9a1b6afdc8a53b';editor='C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t12';unity=20220362;tuanjie=11000;fgs=1}
)
$results=@()
foreach($profile in $profiles) {
    $root=Join-Path $OutputRoot $profile.name
    New-Item -ItemType Directory -Path $root | Out-Null
    & git -C (Join-Path $WorkspaceRoot 'repos/il2cpp_plus') archive --format=tar "--output=$root/il2cpp.tar" $profile.commit
    if($LASTEXITCODE -ne 0){throw 'IL2CPP archive failed'}
    & tar -xf "$root/il2cpp.tar" -C $root
    if($LASTEXITCODE -ne 0){throw 'IL2CPP extract failed'}
    & git -C $runtimeRepo archive --format=tar "--output=$root/hybridclr.tar" $runtimeCommit hybridclr
    if($LASTEXITCODE -ne 0){throw 'HybridCLR archive failed'}
    & tar -xf "$root/hybridclr.tar" -C "$root/libil2cpp"
    if($LASTEXITCODE -ne 0){throw 'HybridCLR extract failed'}
    $external=Join-Path $profile.editor 'Editor/Data/il2cpp/external'
    New-Item -ItemType Junction -Path "$root/external" -Target $external | Out-Null
    & $cmake -S "$labRoot/native-unit-tests" -B "$root/build" -G 'Visual Studio 17 2022' -A x64 "-DHYBRIDCLR_RUNTIME_ROOT=$root/libil2cpp" "-DIL2CPP_EXTERNAL=$external" "-DIL2CPP_BASELIB_INCLUDE=$external/baselib/Include" "-DIL2CPP_BASELIB_PLATFORM_INCLUDE=$external/baselib/Platforms/Windows/Include" "-DHYBRIDCLR_TEST_UNITY_VERSION=$($profile.unity)" "-DHYBRIDCLR_TEST_TUANJIE_VERSION=$($profile.tuanjie)" "-DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=$($profile.fgs)" -DHYBRIDCLR_TEST_AOT_SELECTION=0 *> "$root/configure.log"
    if($LASTEXITCODE -ne 0){throw 'Configure failed'}
    & $cmake --build "$root/build" --config Release --parallel 4 *> "$root/build.log"
    $compiled=$LASTEXITCODE -eq 0
    $passed=$false
    if($compiled) {
        & $ctest --test-dir "$root/build" -C Release --output-on-failure *> "$root/ctest.log"
        $passed=$LASTEXITCODE -eq 0
    }
    $results+=@{profile=$profile.name;hybridclrCommit=$runtimeCommit;il2cppCommit=$profile.commit;selectionEnabled=$false;compiled=$compiled;ctest=$passed;external=$external;surrogateExternalHeadersUsed=$false}
    Write-Host "$($profile.name): compiled=$compiled, CTest=$passed"
}
$report=@{format='hybridclr.aot-mode.compatibility.v1';labCommit=(& git -C $labRoot rev-parse HEAD).Trim();profiles=$results;passed=(@($results | Where-Object {-not $_.ctest}).Count -eq 0);mergeReady=$false;surrogateExternalHeadersUsed=$false;scope='Feature-disabled compatibility with existing maintenance lines; active selection hooks require separate engine qualification.'}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$OutputRoot/summary.json" -Encoding utf8
if(-not $report.passed){throw 'Compatibility matrix failed; inspect per-profile logs'}
