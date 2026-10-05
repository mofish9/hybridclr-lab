param(
    [string]$PackageRoot='C:/hybridclr_optimize/worktrees/hybridclr-unity-dhe-startup-selection-v1',
    [string]$PairRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-router-frozen'),
    [string]$ReportRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'reports/startup-protocols-36a7a38'),
    [string]$BrowserArtifactRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-controller-native'),
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$ReportRoot=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $ReportRoot){throw 'Protocol report directory must be new.'}
New-Item -ItemType Directory -Path $ReportRoot | Out-Null
function Check([string]$Operation){if($LASTEXITCODE -ne 0){throw "$Operation failed, exit $LASTEXITCODE"}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()}
$native=Join-Path $PairRoot 'native-build/Release/startup_store_tests.exe'
$nativeLog=Join-Path $PairRoot 'native-build/Testing/Temporary/LastTest.log'
if(-not (Test-Path -LiteralPath $nativeLog) -or (Get-Content -LiteralPath $nativeLog -Raw) -notmatch 'Test Passed\.'){throw 'Paired native CTest evidence is missing or failed.'}
$vector=Join-Path $ReportRoot 'native-record.bin'
& $native --vector $vector; Check 'Native vector generation'
$java=Join-Path $EditorRoot 'Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe'
$javac=Join-Path (Split-Path -Parent $java) 'javac.exe'
$androidJar=Join-Path $EditorRoot 'Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platforms/android-35/android.jar'
$javaSource=Join-Path $PackageRoot 'ToolsSource~/Startup/Android/StartupSelectionStore.java'
$javaTest=Join-Path (Split-Path -Parent $PSScriptRoot) 'fixtures/startup-router/AndroidProtocolTest.java'
$classes=Join-Path $ReportRoot 'java-classes'
& $javac -encoding UTF-8 -source 8 -target 8 -Xlint:all,-options -Werror -classpath $androidJar -d $classes $javaSource $javaTest; Check 'Android SDK source compilation'
& $java -classpath "$classes;$androidJar" com.codephilosophy.hybridclr.startup.AndroidProtocolTest $vector (Join-Path $ReportRoot 'java-record.bin'); Check 'Java protocol differential'
& $native --read-vector (Join-Path $ReportRoot 'java-record.bin'); Check 'Native read of Java record'
$browser=Get-Content -LiteralPath (Join-Path $BrowserArtifactRoot 'browser-report.json') -Raw | ConvertFrom-Json
if(-not $browser.passed -or $browser.caseCount -ne 12){throw 'Real-browser evidence is missing or failed.'}
if((Hash $vector) -ne (Hash (Join-Path $BrowserArtifactRoot 'native-record.bin')) -or (Hash $vector) -ne (Hash (Join-Path $BrowserArtifactRoot 'js-record.bin'))){throw 'Browser/native byte record differs.'}
Copy-Item -LiteralPath (Join-Path $BrowserArtifactRoot 'js-record.bin') -Destination $ReportRoot
& $native --read-vector (Join-Path $ReportRoot 'js-record.bin'); Check 'Native read of browser record'
$api=Join-Path $PackageRoot 'Runtime/Startup/HybridStartup.cs'
$netstandard=Join-Path $EditorRoot 'Editor/Data/NetStandard/ref/2.1.0/netstandard.dll'
$core=Join-Path $EditorRoot 'Editor/Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_nondevelopment_il2cpp/Data/Managed/UnityEngine.CoreModule.dll'
& 'C:/Program Files/dotnet/dotnet.exe' 'C:/Program Files/dotnet/sdk/6.0.428/Roslyn/bincore/csc.dll' /nologo /target:library /nostdlib /define:UNITY_WEBGL "/reference:$netstandard" "/reference:$core" "/out:$(Join-Path $ReportRoot 'WebGlApi.dll')" $api; Check 'Managed WebGL branch compilation'
$report=[ordered]@{
    format='hybridclr.startup-cross-host-protocol-report.v1';passed=$true;generatedAtUtc=[DateTime]::UtcNow.ToString('o')
    packageCommit=(& git -C $PackageRoot rev-parse HEAD).Trim();fixtureCommit=(& git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD).Trim()
    nativeTestExeSha256=(Hash $native);nativeCTest='passed-see-paired-native-build-LastTest.log';nativeCTestLogSha256=(Hash $nativeLog)
    javaSdkCompilation=$true;javaSdkJarSha256=(Hash $androidJar);javaAdapterSha256=(Hash $javaSource)
    byteRecordDifferential=0;recordSha256=(Hash $vector);nativeJavaBrowserEquivalent=$true
    browser=$browser;browserHostSha256=(Hash (Join-Path $PackageRoot 'Tools~/Startup/HybridStartupHost.mjs'))
    managedWebGlBranchCompilation=$true;managedApiSha256=(Hash $api);managedReferencePlatform='Windows IL2CPP engine APIs and Unity NetStandard reference, compile-only'
    wasmCallbackIntegrationQualified=$false;webglPlayerQualified=$false;androidStorageQualified=$false;androidPlayerQualified=$false;iosQualified=$false;mergeReady=$false
}
[IO.File]::WriteAllText((Join-Path $ReportRoot 'summary.json'),($report | ConvertTo-Json -Depth 8).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
Write-Output 'Cross-host startup protocol gates passed; platform Player qualifications remain pending.'
