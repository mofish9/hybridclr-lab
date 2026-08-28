[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityExe,
    [string]$LabRoot = "",
    [string]$ProjectPath = "",
    [string]$OutputRoot = "",
    [Parameter(Mandatory = $true)]
    [string]$RuntimeSource,
    [string]$ArchiveRoot = "",
    [ValidateSet("Standalone", "AdapterPrepare", "AdapterPlayer")]
    [string]$Invocation = "Standalone",
    [ValidateRange(1, 1)]
    [int]$ToolchainContractVersion = 1,
    [string]$ProjectPlan = "",
    [string]$ProjectPlanValidation = "",
    [string]$BatchReport = "",
    [string]$SourcePreflight = "",
    [string]$CleanCheckoutGate = "",
    [ValidateSet("Release", "Exploratory")]
    [string]$Mode = "Release",
    [switch]$RequireCompleteCoverage,
    [switch]$ForceOutput,
    [ValidateRange(1, 3600)]
    [int]$PlayerTimeoutSeconds = 120,
    [ValidateRange(10, 3600)]
    [int]$UnityTimeoutSeconds = 600,
    [ValidateRange(0, 3600)]
    [int]$WorkflowLockTimeoutSeconds = 0,
    [switch]$WorkflowLockAlreadyHeld
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")
$coverageRequired = $Mode -eq "Release" -or [bool]$RequireCompleteCoverage
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$ProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    Join-Path $LabRoot "unity2021-dhe-demo"
} else {
    [IO.Path]::GetFullPath($ProjectPath)
}
$settingsPath = Join-Path $ProjectPath "ProjectSettings/HybridCLRSettings.asset"
$settingsSnapshot = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    [IO.File]::ReadAllBytes($settingsPath)
} else {
    $null
}
$identitySourcePath = Join-Path $ProjectPath "Assets/Runtime/HybridCLRDheBuildIdentity.cs"
$identitySourceSnapshot = if (Test-Path -LiteralPath $identitySourcePath -PathType Leaf) {
    [IO.File]::ReadAllBytes($identitySourcePath)
} else {
    $null
}

function Restore-SettingsSnapshot {
    if ($null -ne $settingsSnapshot) {
        [IO.File]::WriteAllBytes($settingsPath, $settingsSnapshot)
    } elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
}

function Restore-IdentitySourceSnapshot {
    if ($null -ne $identitySourceSnapshot) {
        [IO.File]::WriteAllBytes($identitySourcePath, $identitySourceSnapshot)
    } elseif (Test-Path -LiteralPath $identitySourcePath) {
        Remove-Item -LiteralPath $identitySourcePath -Force
    }
}

function Invoke-Unity([string[]]$Arguments, [string]$LogPath) {
    if (Test-Path -LiteralPath $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }
    # Start-Process joins an argument array into one command line and does not
    # quote values containing spaces. JSON lists also contain embedded quotes,
    # so quote each value explicitly before handing it to Unity's argv parser.
    $argumentString = ($Arguments | ForEach-Object {
        $value = [string]$_
        if ($value -match '[\s"]') {
            '"' + ($value -replace '"', '\"') + '"'
        } else {
            $value
        }
    }) -join ' '
    $process = Start-Process -FilePath $unityExePath -ArgumentList $argumentString -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($UnityTimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw "Unity timed out after $UnityTimeoutSeconds seconds. See $LogPath"
    }
    if ($process.ExitCode -ne 0) {
        throw "Unity exited with code $($process.ExitCode). See $LogPath"
    }
}

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Require-Directory([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description was not found: $Path"
    }
}

function ConvertTo-UnityArgumentList([string[]]$Values) {
    # Unity's command-line parser accepts the legacy semicolon-delimited form;
    # using it here avoids a second layer of JSON quote escaping in Windows
    # ProcessStartInfo while still preserving spaces in each quoted argument.
    return @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        ForEach-Object { ([string]$_).Trim() }) -join ';'
}

$reportPath = $null
$outputRootSafe = $false
$workflowLock = $null

try {
    if (-not $WorkflowLockAlreadyHeld) {
        $workflowLock = Enter-DheWorkflowLock -LabRoot $LabRoot -TimeoutSeconds $WorkflowLockTimeoutSeconds
    }
    $OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        Join-Path $LabRoot "artifacts/dhe-demo-workflow"
    } else {
        [IO.Path]::GetFullPath($OutputRoot)
    }
    $ArchiveRoot = if ([string]::IsNullOrWhiteSpace($ArchiveRoot)) {
        $OutputRoot + "-archive"
    } else {
        [IO.Path]::GetFullPath($ArchiveRoot)
    }
    Assert-DheSafeOutputRoot -Path $OutputRoot -ProtectedPaths @($ProjectPath, [IO.Path]::GetFullPath($RuntimeSource))
    Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
    if ($Invocation -eq "Standalone") {
        Assert-DheSafeOutputRoot -Path $ArchiveRoot -ProtectedPaths @($ProjectPath, [IO.Path]::GetFullPath($RuntimeSource), $OutputRoot)
        Assert-DheOutputNotAncestor -Path $ArchiveRoot -Root $LabRoot
    }
    $unityExePath = [IO.Path]::GetFullPath($UnityExe)
    if ($Invocation -eq "Standalone") {
        if (Test-Path -LiteralPath $OutputRoot) {
            if (-not $ForceOutput -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -gt 0) {
                throw "OutputRoot is not empty: $OutputRoot. Pass -ForceOutput to replace a prior run."
            }
            if ($ForceOutput) {
                Remove-Item -LiteralPath $OutputRoot -Recurse -Force
            }
        }
        New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    } elseif (-not (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
        throw "Adapter OutputRoot must be initialized by run-dhe-project-workflow.ps1: $OutputRoot"
    }
    # Only write a failure report after this invocation has prepared the output
    # directory. A rejected stale output must remain untouched for diagnosis.
    $outputRootSafe = $true

    if ($Invocation -ne "AdapterPlayer") {
        $buildDependencies = Join-Path $LabRoot "scripts/build-managed-cases.ps1"
        & $buildDependencies -LabRoot $LabRoot -UnityProjectRoot $ProjectPath -Target StandaloneWindows64
        if ($LASTEXITCODE -ne 0) { throw "Failed to build demo managed dependencies." }
    }

    $runtimeSourcePath = [IO.Path]::GetFullPath($RuntimeSource)
    Require-Directory $runtimeSourcePath "DHE runtime source"
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeSourcePath "hybridclr") -PathType Container)) {
        throw "DHE runtime source must be a merged libil2cpp directory containing 'hybridclr': $runtimeSourcePath"
    }
    $runtimeManifestInput = Join-Path ([IO.Path]::GetDirectoryName($runtimeSourcePath)) "runtime-manifest.json"
    Require-File $runtimeManifestInput "DHE runtime manifest"
    $runtimeManifest = Get-Content -Raw -LiteralPath $runtimeManifestInput | ConvertFrom-Json
    if ($null -eq $runtimeManifest.PSObject.Properties["pathSemantics"] -or
        [string]$runtimeManifest.pathSemantics -ne "workspace-absolute-v1") {
        throw "DHE runtime manifest must declare workspace-absolute-v1 path semantics. Reassemble the runtime."
    }
    if ($null -eq $runtimeManifest.PSObject.Properties["stagedRuntimeSha256"] -or
        (Get-TreeHash $runtimeSourcePath) -ne [string]$runtimeManifest.stagedRuntimeSha256) {
        throw "DHE runtime source does not match its runtime manifest: $runtimeSourcePath"
    }
    $runtimeLockHash = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/dhe-runtime-lock.json") -Algorithm SHA256).Hash.ToLowerInvariant()
    $runtimeDheEnabled = Get-DheStrictBooleanProperty $runtimeManifest "dheEnabled" "DHE runtime manifest dheEnabled"
    if (-not $runtimeDheEnabled -or
        $null -eq $runtimeManifest.PSObject.Properties["dheRuntimeLockSha256"] -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals([string]$runtimeManifest.dheRuntimeLockSha256, $runtimeLockHash)) {
        throw "DHE runtime manifest was produced from a different runtime lock. Reassemble the runtime."
    }
    $runtimeEngineFamily = if ($null -ne $runtimeManifest.engine -and $null -ne $runtimeManifest.engine.PSObject.Properties["family"]) {
        [string]$runtimeManifest.engine.family
    } else { "" }
    $editorFileName = [IO.Path]::GetFileName($unityExePath)
    if ($runtimeEngineFamily -eq "Tuanjie" -and -not $editorFileName.Equals("Tuanjie.exe", [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE runtime targets Tuanjie, but the supplied Editor is '$editorFileName'. Use the matching Tuanjie Editor/runtime pair."
    }
    $packageRoot = Join-Path $ProjectPath "Packages/com.code-philosophy.hybridclr"
    Require-Directory $packageRoot "Embedded HybridCLR package"
    $packageLockPath = Join-Path $LabRoot "manifests/dhe-package-lock.json"
    Require-File $packageLockPath "DHE package lock"
    $packageLock = Get-Content -Raw -LiteralPath $packageLockPath | ConvertFrom-Json
    if ([int]$packageLock.schemaVersion -ne 1 -or
        [string]$packageLock.format -ne "hybridclr.dhe-package-lock.json" -or
        [string]$packageLock.repository -ne "hybridclr_unity" -or
        [string]$packageLock.baseCommit -notmatch '^[0-9a-f]{40}$' -or
        [string]$packageLock.pathBase -ne "project-root-v1") {
        throw "DHE package lock has an invalid schema, repository identity, or path base."
    }
    $lockedPackageReference = [string]$packageLock.packagePath
    if ([string]::IsNullOrWhiteSpace($lockedPackageReference) -or
        [IO.Path]::IsPathRooted($lockedPackageReference) -or
        $lockedPackageReference.Replace('\', '/') -match '(^|/)\.\.(/|$)') {
        throw "DHE package lock packagePath must be a safe project-root-relative path."
    }
    $expectedPackagePath = [IO.Path]::GetFullPath((Join-Path $ProjectPath $lockedPackageReference))
    if (-not [IO.Path]::GetFullPath($packageRoot).Equals($expectedPackagePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE package path does not match package lock: $packageRoot"
    }
    $packageTreeHash = Get-TreeHash $packageRoot
    if ($null -eq $packageLock.PSObject.Properties["treeSha256"] -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals($packageTreeHash, [string]$packageLock.treeSha256)) {
        throw "Embedded HybridCLR package does not match its package lock: $packageRoot"
    }
    $runtimePackageSource = [string]$runtimeManifest.source.hybridclr_unity.commit
    if (-not $runtimePackageSource.Equals([string]$packageLock.baseCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE package lock base commit differs from the runtime manifest source commit."
    }
    $runtimeLockForPackage = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "manifests/dhe-runtime-lock.json") | ConvertFrom-Json
    $runtimePackagePatchIds = @($runtimeLockForPackage.patches |
        Where-Object { $_.applyRoot -eq "package" } | ForEach-Object { [string]$_.id } | Sort-Object)
    $lockedPackagePatchIds = @($packageLock.patches | ForEach-Object { [string]$_ } | Sort-Object)
    if (($runtimePackagePatchIds -join ",") -ne ($lockedPackagePatchIds -join ",")) {
        throw "DHE package lock patch IDs do not match the runtime patch lock."
    }
    if ($Invocation -eq "Standalone") {
        $sourcePreflightRoot = Join-Path $OutputRoot "source-preflight"
        $sourcePreflightParameters = @{
            LabRoot = $LabRoot
            ProjectPath = $ProjectPath
            RuntimeSource = $runtimeSourcePath
            OutputRoot = $sourcePreflightRoot
            PackageLockPath = $packageLockPath
            IdentityTemplatePath = $identitySourcePath
            RequireRuntime = $true
            RequireDheEqualsHotUpdate = $true
            RequireEmbeddedPackage = $true
            RequireCleanRuntimeSources = ($Mode -eq "Release")
            RequireIdentityTemplate = $true
            ForceOutput = $true
        }
        if ($Mode -eq "Release") {
            $sourcePreflightParameters.RequireNonSurrogateExternalHeaders = $true
        }
        & (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1") @sourcePreflightParameters
        if ($LASTEXITCODE -ne 0) { throw "DHE source preflight failed. See $sourcePreflightRoot" }
        $cleanCheckoutRoot = Join-Path $OutputRoot "clean-checkout-gate"
        $cleanCheckoutParameters = @{
            LabRoot = $LabRoot
            ProjectPath = $ProjectPath
            RuntimeSource = $runtimeSourcePath
            OutputRoot = $cleanCheckoutRoot
            IdentityTemplatePath = $identitySourcePath
            ToolGitRoot = $LabRoot
            ToolSourceBoundaryPath = (Join-Path $LabRoot "manifests/dhe-source-boundary.json")
            RequireIdentityTemplate = $true
            RequireEmbeddedPackage = $true
            ForceOutput = $true
        }
        if ($Mode -eq "Release") {
            $cleanCheckoutParameters.GitRoot = $LabRoot
            $cleanCheckoutParameters.RequireGitClean = $true
            $cleanCheckoutParameters.RequireTrackedSources = $true
            $cleanCheckoutParameters.RequireToolGitClean = $true
            $cleanCheckoutParameters.RequireToolTrackedSources = $true
        }
        & (Join-Path $LabRoot "scripts/run-dhe-clean-checkout-gate.ps1") @cleanCheckoutParameters
        if ($LASTEXITCODE -ne 0) { throw "DHE clean-checkout gate failed. See $cleanCheckoutRoot" }
    }
    $hybridclrUnitySource = if ($null -ne $runtimeManifest.source.PSObject.Properties["hybridclr_unity"]) {
        [IO.Path]::GetFullPath([string]$runtimeManifest.source.hybridclr_unity.path)
    } else {
        throw "DHE runtime manifest has no hybridclr_unity source provenance."
    }
    & (Join-Path $LabRoot "scripts/apply-dhe-runtime-patches.ps1") `
        -NativeRoot $runtimeSourcePath `
        -HybridClrSource ([IO.Path]::GetFullPath([string]$runtimeManifest.source.hybridclr.path)) `
        -Il2CppPlusSource ([IO.Path]::GetFullPath([string]$runtimeManifest.source.il2cpp_plus.path)) `
        -PackageRoot $packageRoot `
        -HybridClrUnitySource $hybridclrUnitySource `
        -LabRoot $LabRoot `
        -VerifyOnly `
        -RequireApplied
    if ($LASTEXITCODE -ne 0) { throw "DHE runtime or package patch verification failed." }
    if ($Invocation -eq "Standalone") {
        $fixtureGateRoot = Join-Path $OutputRoot "script-fixture-gate"
        & (Join-Path $LabRoot "scripts/run-dhe-script-fixture-gate.ps1") `
            -LabRoot $LabRoot -OutputRoot $fixtureGateRoot -WorkflowLockAlreadyHeld -ForceOutput
        if ($LASTEXITCODE -ne 0) { throw "DHE script fixture gate failed." }
    }
    if ($Invocation -ne "AdapterPlayer") {
        Invoke-Unity @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ProjectPath,
            "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.InstallRuntime",
            "-labRoot", $LabRoot,
            "-labTarget", "StandaloneWindows64",
            "-labRuntimeSource", $runtimeSourcePath,
            "-logFile", (Join-Path $ProjectPath "unity-dhe-install-runtime.log")
        ) (Join-Path $ProjectPath "unity-dhe-install-runtime.log")
    }
    $installedRuntimeRoot = Join-Path $ProjectPath "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp"
    $sourceDheRuntime = Join-Path $runtimeSourcePath "hybridclr/DheRuntime.cpp"
    $installedDheRuntime = Join-Path $installedRuntimeRoot "hybridclr/DheRuntime.cpp"
    Require-File $sourceDheRuntime "DHE runtime source implementation"
    Require-File $installedDheRuntime "Installed DHE runtime implementation"
    if ((Get-FileHash -LiteralPath $sourceDheRuntime -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $installedDheRuntime -Algorithm SHA256).Hash) {
        throw "Installed DHE runtime does not match the requested runtime source."
    }
    if ($Invocation -ne "AdapterPlayer") {
        $runtimeSourceFiles = @(Get-ChildItem -LiteralPath $runtimeSourcePath -Recurse -File -Force)
        foreach ($sourceFile in $runtimeSourceFiles) {
            $relative = $sourceFile.FullName.Substring($runtimeSourcePath.Length).TrimStart('\', '/')
            $installedFile = Join-Path $installedRuntimeRoot $relative
            Require-File $installedFile "Installed runtime file '$relative'"
            if ((Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash) {
                throw "Installed runtime file differs from source: $relative"
            }
        }
    }
    $runtimeDheRuntimeSha256 = (Get-FileHash -LiteralPath $sourceDheRuntime -Algorithm SHA256).Hash.ToLowerInvariant()

$dheAssemblyNames = @(
    "HybridCLR.ManagedCasesAot",
    "HybridCLR.ManagedCases",
    "HybridCLR.MetadataStress",
    "HybridCLR.CrossAssemblyDerived"
)
$capabilityRoot = Join-Path $OutputRoot "capability"
$identityPath = Join-Path $OutputRoot "build-identity.json"
$runtimeManifestOutput = Join-Path $OutputRoot "runtime-manifest.json"
$strippedRoot = Join-Path $OutputRoot "stripped"
$baselineStrippedRoot = Join-Path $strippedRoot "baseline"
$currentStrippedRoot = Join-Path $strippedRoot "current"
$baselineRawPaths = @($dheAssemblyNames | ForEach-Object { Join-Path $capabilityRoot ("baseline/{0}.dll" -f $_) })
$currentRawPaths = @($dheAssemblyNames | ForEach-Object { Join-Path $capabilityRoot ("current/{0}.dll" -f $_) })

if ($Invocation -ne "AdapterPlayer") {
& (Join-Path $LabRoot "scripts/run-dhe-capability-gate.ps1") -LabRoot $LabRoot -OutputRoot $capabilityRoot -ForceOutput
if ($LASTEXITCODE -ne 0) { throw "DHE capability gate failed." }

$dependencyProjects = @(
    (Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCases/HybridCLR.ManagedCases.csproj"),
    (Join-Path $LabRoot "managed-cases/HybridCLR.MetadataStress/HybridCLR.MetadataStress.csproj"),
    (Join-Path $LabRoot "managed-cases/HybridCLR.CrossAssemblyDerived/HybridCLR.CrossAssemblyDerived.csproj")
)
foreach ($variant in @(
    @{ Name = "baseline"; DefineConstants = "" },
    @{ Name = "current"; DefineConstants = "DHE_CURRENT" }
)) {
    $variantRoot = Join-Path $capabilityRoot $variant.Name
    foreach ($project in $dependencyProjects) {
        $buildArguments = @("build", $project, "--configuration", "Release", "--output", $variantRoot, "--nologo", "-v:minimal")
        if (-not [string]::IsNullOrWhiteSpace($variant.DefineConstants)) {
            $buildArguments += "-p:DefineConstants=$($variant.DefineConstants)"
        }
        dotnet @buildArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build DHE $($variant.Name) dependency variant: $project"
        }
    }
}
foreach ($assemblyName in @("HybridCLR.ManagedCases.dll", "HybridCLR.MetadataStress.dll", "HybridCLR.CrossAssemblyDerived.dll")) {
    foreach ($variant in @("baseline", "current")) {
        Require-File (Join-Path $capabilityRoot "$variant/$assemblyName") "DHE $variant dependency assembly"
    }
}

foreach ($path in @($baselineRawPaths + $currentRawPaths)) { Require-File $path "DHE assembly input" }
$baselineRaw = $baselineRawPaths[0]
$currentRaw = $currentRawPaths[0]
Copy-Item -LiteralPath $runtimeManifestInput -Destination $runtimeManifestOutput -Force
$identity = [ordered]@{
    schemaVersion = 1
    workflow = "DHE-demo"
    target = "StandaloneWindows64"
    pathSemantics = "workspace-absolute-v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    baselineAssemblySha256 = (Get-FileHash -LiteralPath $baselineRaw -Algorithm SHA256).Hash.ToLowerInvariant()
    currentAssemblySha256 = (Get-FileHash -LiteralPath $currentRaw -Algorithm SHA256).Hash.ToLowerInvariant()
    dheAotAssemblies = $dheAssemblyNames
    # The snapshot hash belongs to the primary AOT image, not to whichever MV
    # happens to be first in a caller-provided multi-assembly list.
    aotSnapshotAssembly = $dheAssemblyNames[0]
    runtimeSource = $runtimeSourcePath
    runtimeDheRuntimeSha256 = $runtimeDheRuntimeSha256
    packageTreeSha256 = $packageTreeHash
    runtimeManifest = if (Test-Path -LiteralPath $runtimeManifestOutput -PathType Leaf) {
        $runtimeManifestOutput
    } else { $null }
}
[IO.File]::WriteAllText($identityPath, ($identity | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
New-Item -ItemType Directory -Force -Path $baselineStrippedRoot, $currentStrippedRoot | Out-Null

function Generate-Stripped([string[]]$inputAssemblies, [string]$destinationRoot, [string]$logName) {
    $logPath = Join-Path $ProjectPath $logName
    $fullInputs = @($inputAssemblies | ForEach-Object { [IO.Path]::GetFullPath($_) })
    Invoke-Unity @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ProjectPath,
        "-executeMethod", "HybridCLR.Lab.Editor.HybridCLRLabBuild.GenerateOnly",
        "-labTarget", "StandaloneWindows64",
        "-labManagedDll", $fullInputs[0],
        "-labDheAotAssemblies", (ConvertTo-UnityArgumentList -Values $fullInputs),
        "-labHotUpdateAssemblies", (ConvertTo-UnityArgumentList -Values $dheAssemblyNames),
        "-labDheForceRegenerate", "true",
        "-labBuildIdentity", $identityPath,
        "-labAotMetadataPackaging", "include",
        "-logFile", $logPath
    ) $logPath
    $generatedRoot = Join-Path $ProjectPath "HybridCLRData/AssembliesPostIl2CppStrip/StandaloneWindows64"
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($assemblyName in $dheAssemblyNames) {
        $generated = Join-Path $generatedRoot ($assemblyName + ".dll")
        Require-File $generated "Generated stripped DHE assembly '$assemblyName'"
        $destination = Join-Path $destinationRoot ($assemblyName + ".dll")
        Copy-Item -LiteralPath $generated -Destination $destination -Force
        $result.Add($destination)
    }
    return $result.ToArray()
}

$baselineStrippedPaths = Generate-Stripped $baselineRawPaths $baselineStrippedRoot "unity-dhe-workflow-baseline.log"
$currentStrippedPaths = Generate-Stripped $currentRawPaths $currentStrippedRoot "unity-dhe-workflow-current.log"
$baselineStripped = $baselineStrippedPaths[0]

$baselineSnapshotHash = (Get-FileHash -LiteralPath $baselineStripped -Algorithm SHA256).Hash.ToLowerInvariant()
$identityJson = Get-Content -Raw -LiteralPath $identityPath | ConvertFrom-Json
# The Player consumes the stripped baseline image. Bind the identity's
# baselineAssemblySha256 to that exact image; retain the raw compile input
# separately for auditability.
$identityJson | Add-Member -NotePropertyName aotInputAssemblySha256 -NotePropertyValue ([string]$identityJson.baselineAssemblySha256) -Force
$identityJson | Add-Member -NotePropertyName baselineAssemblySha256 -NotePropertyValue $baselineSnapshotHash -Force
$identityJson | Add-Member -NotePropertyName aotSnapshotSha256 -NotePropertyValue $baselineSnapshotHash -Force
$identityJson | Add-Member -NotePropertyName aotSnapshotKind -NotePropertyValue "stripped-managed-assembly-sha256" -Force
[IO.File]::WriteAllText($identityPath, ($identityJson | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
} else {
    Require-File $identityPath "Prepared DHE build identity"
    Require-File $runtimeManifestOutput "Prepared DHE runtime manifest"
    foreach ($path in @($baselineRawPaths + $currentRawPaths)) { Require-File $path "Prepared DHE assembly input" }
    $baselineStrippedPaths = @($dheAssemblyNames | ForEach-Object { Join-Path $baselineStrippedRoot ($_.ToString() + ".dll") })
    $currentStrippedPaths = @($dheAssemblyNames | ForEach-Object { Join-Path $currentStrippedRoot ($_.ToString() + ".dll") })
    foreach ($path in @($baselineStrippedPaths + $currentStrippedPaths)) { Require-File $path "Prepared stripped DHE assembly" }
    $baselineStripped = $baselineStrippedPaths[0]
    $preparedIdentity = Get-Content -Raw -LiteralPath $identityPath | ConvertFrom-Json
    $baselineSnapshotHash = ([string]$preparedIdentity.aotSnapshotSha256).ToLowerInvariant()
    if ($baselineSnapshotHash -notmatch '^[0-9a-f]{64}$') {
        throw "Prepared DHE identity has no valid AOT snapshot hash: $identityPath"
    }
}

if ($Invocation -eq "AdapterPrepare") {
    $adapterPreparePath = Join-Path $OutputRoot "adapter/prepare.json"
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($adapterPreparePath)) | Out-Null
    $adapterPrepare = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-project-adapter-prepare.json"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        passed = $true
        toolchainContractVersion = $ToolchainContractVersion
        target = "StandaloneWindows64"
        pathSemantics = "workspace-absolute-v1"
        projectPath = $ProjectPath
        settingsFile = $settingsPath
        baselineRoot = $baselineStrippedRoot
        currentRoot = $currentStrippedRoot
        aotAssemblies = $dheAssemblyNames
        errors = @()
    }
    [IO.File]::WriteAllText($adapterPreparePath, ($adapterPrepare | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    Write-Host "DHE demo adapter Prepare passed: $adapterPreparePath"
    return
}

$preflightRoot = Join-Path $OutputRoot "project-preflight"
if ($Invocation -eq "AdapterPlayer") {
    if ([string]::IsNullOrWhiteSpace($ProjectPlan) -or
        [string]::IsNullOrWhiteSpace($ProjectPlanValidation) -or
        [string]::IsNullOrWhiteSpace($BatchReport) -or
        [string]::IsNullOrWhiteSpace($SourcePreflight) -or
        [string]::IsNullOrWhiteSpace($CleanCheckoutGate)) {
        throw "AdapterPlayer requires the validated project plan, batch, source-preflight, and clean-checkout reports."
    }
    $planPath = [IO.Path]::GetFullPath($ProjectPlan)
    $projectPlanValidationPath = [IO.Path]::GetFullPath($ProjectPlanValidation)
    $batchReportPath = [IO.Path]::GetFullPath($BatchReport)
    $sourcePreflightReportPath = [IO.Path]::GetFullPath($SourcePreflight)
    $cleanCheckoutReportPath = [IO.Path]::GetFullPath($CleanCheckoutGate)
    foreach ($requiredAdapterFile in @($planPath, $projectPlanValidationPath, $batchReportPath, $sourcePreflightReportPath, $cleanCheckoutReportPath)) {
        Require-File $requiredAdapterFile "Validated DHE adapter input"
    }
} else {
    & (Join-Path $LabRoot "scripts/run-dhe-project-preflight.ps1") `
        -SettingsFile $settingsPath `
        -BaselineRoot $baselineStrippedRoot `
        -CurrentRoot $currentStrippedRoot `
        -OutputRoot $preflightRoot `
        -ProjectRoot $ProjectPath `
        -RequireDheEqualsHotUpdate `
        -ForceOutput
    if ($LASTEXITCODE -ne 0) { throw "Strict multi-assembly DHE preflight failed." }
    $planPath = Join-Path $preflightRoot "dhe-project-plan.json"
    $projectPlanValidationPath = Join-Path $preflightRoot "project-plan-validation.json"
    $batchReportPath = Join-Path $preflightRoot "batch/dhe-batch-summary.json"
    $sourcePreflightReportPath = Join-Path $OutputRoot "source-preflight/source-preflight-report.json"
    $cleanCheckoutReportPath = Join-Path $OutputRoot "clean-checkout-gate/clean-checkout-gate-report.json"
}
Require-File $planPath "DHE project plan"
$projectPlanValidationDocument = Get-Content -Raw -LiteralPath $projectPlanValidationPath | ConvertFrom-Json
if (-not (Get-DheStrictBooleanProperty $projectPlanValidationDocument "passed" "DHE project plan validation passed") -or
    -not (Get-DheStrictBooleanProperty $projectPlanValidationDocument "coverageComplete" "DHE project plan validation coverageComplete")) {
    throw "DHE project plan validation is not a complete pass: $projectPlanValidationPath"
}
$projectPlanDocument = Get-Content -Raw -LiteralPath $planPath | ConvertFrom-Json
if (-not (Get-DheStrictBooleanProperty $projectPlanDocument "complete" "DHE project plan complete") -or
    @($projectPlanDocument.assemblies).Count -ne $dheAssemblyNames.Count) {
    throw "DHE project plan is incomplete for the configured assembly set."
}
$planAssemblyNames = @($projectPlanDocument.assemblies | ForEach-Object { [string]$_.assemblyName } | Sort-Object -Unique)
$expectedAssemblyNames = @($dheAssemblyNames | Sort-Object -Unique)
if (($planAssemblyNames -join ",") -ne ($expectedAssemblyNames -join ",")) {
    throw "DHE project plan assembly set does not match the configured demo AOT set. Expected [$($expectedAssemblyNames -join ', ')], got [$($planAssemblyNames -join ', ')]."
}
$orderedPlanAssemblies = @($projectPlanDocument.assemblies | Sort-Object @{ Expression = { if ([string]$_.assemblyName -eq "HybridCLR.ManagedCasesAot") { 0 } else { 1 } } }, assemblyName)
$orderedBaselineStrippedPaths = @($orderedPlanAssemblies | ForEach-Object {
    Join-Path $baselineStrippedRoot ($_.assemblyName + ".dll")
})
$orderedCurrentStrippedPaths = @($orderedPlanAssemblies | ForEach-Object {
    Join-Path $currentStrippedRoot ($_.assemblyName + ".dll")
})
$mvJsonPaths = @($orderedPlanAssemblies | ForEach-Object {
    if ([string]$_.status -ne "compatible") { throw "DHE assembly '$($_.assemblyName)' is not compatible: $($_.status)" }
    [string]$_.mvJson
})

$stage = Join-Path $ProjectPath "Assets/StreamingAssets/HybridCLRLab/DheDemo"
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$runtimePlanRecords = New-Object System.Collections.Generic.List[object]
foreach ($assembly in $orderedPlanAssemblies) {
    $assemblyName = [string]$assembly.assemblyName
    $currentPath = Join-Path $currentStrippedRoot ($assemblyName + ".dll")
    $baselinePath = Join-Path $baselineStrippedRoot ($assemblyName + ".dll")
    $mvPath = [string]$assembly.mvBytes
    $currentStage = Join-Path $stage ($assemblyName + ".dll.bytes")
    $baselineStage = Join-Path $stage ($assemblyName + ".baseline.dll.bytes")
    $mvStage = Join-Path $stage ($assemblyName + ".mv.bytes")
    $snapshotStage = Join-Path $stage ($assemblyName + ".aot-snapshot.bytes")
    Copy-Item $currentPath $currentStage -Force
    Copy-Item $baselinePath $baselineStage -Force
    Copy-Item $mvPath $mvStage -Force
    $baselineBytes = [IO.File]::ReadAllBytes($baselinePath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $snapshot = $sha.ComputeHash($baselineBytes) } finally { $sha.Dispose() }
    [IO.File]::WriteAllBytes($snapshotStage, $snapshot)
    $runtimePlanRecords.Add([ordered]@{
        assemblyName = $assemblyName
        current = "HybridCLRLab/DheDemo/$assemblyName.dll.bytes"
        baseline = "HybridCLRLab/DheDemo/$assemblyName.baseline.dll.bytes"
        mv = "HybridCLRLab/DheDemo/$assemblyName.mv.bytes"
        snapshot = "HybridCLRLab/DheDemo/$assemblyName.aot-snapshot.bytes"
        baselineSha256 = (Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
        currentSha256 = (Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash
    })
}
$runtimePlan = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-runtime-plan.json"
    assemblies = $runtimePlanRecords.ToArray()
}
$runtimePlanNames = @($runtimePlanRecords.ToArray() | ForEach-Object { [string]$_['assemblyName'] })
if ($runtimePlanRecords.Count -ne $dheAssemblyNames.Count -or
    @($runtimePlanNames | Group-Object | Where-Object Count -gt 1).Count -gt 0) {
    throw "DHE runtime plan assembly set is incomplete or duplicated."
}
foreach ($record in $runtimePlanRecords) {
    foreach ($field in @("baselineSha256", "currentSha256")) {
        if ([string]$record[$field] -notmatch '^[0-9a-fA-F]{64}$') {
            throw "DHE runtime plan has an invalid $field for $($record.assemblyName)."
        }
    }
}
$runtimePlanPath = Join-Path $stage "dhe-runtime-plan.json"
[IO.File]::WriteAllText($runtimePlanPath, ($runtimePlan | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))

# Keep a self-contained copy of the exact runtime handoff in the workflow
# artifact. The project copy is intentionally ignored build input; reports
# must remain useful after that cache is cleaned.
$runtimePlanArchiveDirectory = Join-Path $OutputRoot "runtime-plan"
if (Test-Path -LiteralPath $runtimePlanArchiveDirectory) {
    Remove-Item -LiteralPath $runtimePlanArchiveDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $runtimePlanArchiveDirectory | Out-Null
$runtimePlanArchiveRecords = New-Object System.Collections.Generic.List[object]
foreach ($record in $runtimePlanRecords) {
    $assemblyName = [string]$record.assemblyName
    foreach ($field in @("current", "baseline", "mv", "snapshot")) {
        $projectRelative = ([string]$record[$field]).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $sourcePath = Join-Path $ProjectPath (Join-Path "Assets/StreamingAssets" $projectRelative)
        $archiveFileName = [IO.Path]::GetFileName($projectRelative)
        $archivePath = Join-Path $runtimePlanArchiveDirectory $archiveFileName
        Require-File $sourcePath "Staged runtime plan payload '$projectRelative'"
        Copy-Item -LiteralPath $sourcePath -Destination $archivePath -Force
        $record[$field] = $archiveFileName
    }
    $runtimePlanArchiveRecords.Add($record)
}
$runtimePlanArchive = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-runtime-plan.json"
    assemblies = $runtimePlanArchiveRecords.ToArray()
}
$runtimePlanArchivePath = Join-Path $runtimePlanArchiveDirectory "dhe-runtime-plan.json"
[IO.File]::WriteAllText($runtimePlanArchivePath, ($runtimePlanArchive | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))

$playerPath = Join-Path $ProjectPath "Builds/DHE-Demo-Workflow/HybridCLRLab.exe"
# Compile the player from the baseline AOT image. The current assembly is
# loaded later as the differential image; this keeps a missing guard from
# being masked by compiling the new body into the player.
& (Join-Path $LabRoot "scripts/run-dhe-deterministic-player-build.ps1") `
    -UnityExe $unityExePath `
    -ProjectPath $ProjectPath `
    -BuildPath $playerPath `
    -MvJson $mvJsonPaths `
    -TransformerScript (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
    -RequireCompleteCoverage:$coverageRequired `
    -DheAotAssemblies $baselineRawPaths `
    -BuildIdentity $identityPath `
    -UnityTimeoutSeconds $UnityTimeoutSeconds
if ($LASTEXITCODE -ne 0) { throw "Deterministic DHE Player build failed." }

$resultPath = Join-Path $OutputRoot "dhe-player-result.json"
$playerArguments = @(
    "-batchmode", "-nographics", "-labMode", "dhe", "-labTarget", "StandaloneWindows64",
    "-labAotMetadataMode", "supplemental", "-labResult", $resultPath)
$playerArgumentString = ($playerArguments | ForEach-Object {
    $value = [string]$_
    if ($value -match '[\s"]') {
        '"' + ($value -replace '"', '\\"') + '"'
    } else {
        $value
    }
}) -join ' '
$playerProcess = Start-Process -FilePath $playerPath -ArgumentList $playerArgumentString `
    -WorkingDirectory ([IO.Path]::GetDirectoryName($playerPath)) -PassThru -WindowStyle Hidden
if (-not $playerProcess.WaitForExit($PlayerTimeoutSeconds * 1000)) {
    try { $playerProcess.Kill() } catch { }
    try { $playerProcess.WaitForExit() } catch { }
    throw "DHE Player timed out after $PlayerTimeoutSeconds seconds."
}
if ($playerProcess.ExitCode -ne 0) {
    throw "DHE Player exited with code $($playerProcess.ExitCode)."
}
$resultDeadline = [DateTime]::UtcNow.AddSeconds(10)
while (-not (Test-Path -LiteralPath $resultPath -PathType Leaf) -and [DateTime]::UtcNow -lt $resultDeadline) {
    Start-Sleep -Milliseconds 100
}
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    throw "DHE Player did not produce a result report."
}
$playerResult = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
$playerPassed = Get-DheStrictBooleanProperty $playerResult "passed" "DHE Player passed"
if (-not $playerPassed) {
    throw "DHE Player gate failed; inspect $resultPath"
}

# The final deterministic build updates identity v2 after guard injection.
# Never publish a report using the pre-injection identity snapshot.
$finalBuildIdentity = Get-Content -Raw -LiteralPath $identityPath | ConvertFrom-Json
if ($null -eq $finalBuildIdentity -or [int]$finalBuildIdentity.identityVersion -ne 2 -or
    [string]$finalBuildIdentity.aotSnapshotKind -ne "managed-assembly-plus-generated-cpp-v1" -or
    [string]$finalBuildIdentity.baselineAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    [string]$finalBuildIdentity.aotSnapshotSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    [string]$finalBuildIdentity.nativeGuardSourceSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    [string]$finalBuildIdentity.nativeManifestSha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "Final DHE build identity is incomplete or not identity v2: $identityPath"
}
$baselineSnapshotHash = ([string]$finalBuildIdentity.aotSnapshotSha256).ToLowerInvariant()

$mvs = @($mvJsonPaths | ForEach-Object { Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json })
$aggregateMethodCount = [int](($mvs | ForEach-Object { [int]$_.summary.methodCount } | Measure-Object -Sum).Sum)
$aggregateChangedMethodCount = [int](($mvs | ForEach-Object { [int]$_.summary.changedMethodCount } | Measure-Object -Sum).Sum)
$aggregateTypeChangeCount = [int](($mvs | ForEach-Object { [int]$_.summary.typeChangeCount } | Measure-Object -Sum).Sum)
$nativeManifestPath = Join-Path $ProjectPath "Library/Bee/artifacts/WinPlayerBuildProgram/il2cppOutput/cpp/dhe-native-manifest.json"
$nativeManifestOutput = Join-Path $OutputRoot "dhe-native-manifest.json"
if (Test-Path -LiteralPath $nativeManifestPath -PathType Leaf) {
    Copy-Item -LiteralPath $nativeManifestPath -Destination $nativeManifestOutput -Force
}
$nativeManifest = if (Test-Path -LiteralPath $nativeManifestPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $nativeManifestPath | ConvertFrom-Json
} else {
    $null
}
$nativeCoverage = New-DheNativeGuardCoverage `
    -NativeManifest $nativeManifest `
    -ChangedMethodCount $aggregateChangedMethodCount
$nativeManifestAvailable = [bool]$nativeCoverage.manifestAvailable
$coverageComplete = [bool]$nativeCoverage.complete
$validationPassed = $playerPassed
$coverageGatePassed = $nativeManifestAvailable -and (-not $coverageRequired -or $coverageComplete)
$releaseReady = $Mode -eq "Release" -and $validationPassed -and $coverageComplete -and
    @($mvs | Where-Object { [string]$_.compatibility.status -ne "compatible" }).Count -eq 0 -and
    [int]$finalBuildIdentity.identityVersion -eq 2 -and
    [string]$finalBuildIdentity.aotSnapshotKind -eq "managed-assembly-plus-generated-cpp-v1"
$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-demo-workflow.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    # Workspace reports intentionally carry absolute material references. The
    # archive adapter rewrites them to archive-relative-v1 before handoff.
    pathSemantics = "workspace-absolute-v1"
    # Keep `passed` as the invocation result for compatibility, while making
    # the two release-relevant dimensions explicit for CI and reviewers.
    passed = $validationPassed -and $coverageGatePassed
    validationPassed = $validationPassed
    target = "StandaloneWindows64"
    mode = $Mode
    coverageRequired = [bool]$coverageRequired
    coverageGatePassed = $coverageGatePassed
    releaseReady = $releaseReady
    artifactValidationPassed = $false
    capability = [ordered]@{
        methodCount = $aggregateMethodCount
        changedMethodCount = $aggregateChangedMethodCount
        typeChangeCount = $aggregateTypeChangeCount
        compatibility = if (@($mvs | Where-Object { [string]$_.compatibility.status -ne "compatible" }).Count -eq 0) { "compatible" } else { "incompatible" }
    }
    assemblyScope = [ordered]@{
        strategy = "multi-assembly-dhe"
        aotAssemblies = $dheAssemblyNames
        loadedDheAssemblies = @($playerResult.loadedDheAssemblies)
        stagedDependencies = @("HybridCLR.ManagedCases", "HybridCLR.MetadataStress", "HybridCLR.CrossAssemblyDerived")
        stagedDependenciesLoadedAsDhe = Get-DheStrictBooleanProperty $playerResult "multiAssemblyValidated" "DHE Player multiAssemblyValidated"
        secondaryAssemblyChangedValidated = Get-DheStrictBooleanProperty $playerResult "secondaryAssemblyChangedValidated" "DHE Player secondaryAssemblyChangedValidated"
        secondaryAssemblyDirectValidated = Get-DheStrictBooleanProperty $playerResult "secondaryAssemblyDirectValidated" "DHE Player secondaryAssemblyDirectValidated"
    }
    transaction = [ordered]@{
        retryValidated = Get-DheStrictBooleanProperty $playerResult "retryValidated" "DHE Player retryValidated"
        retryAssemblyName = [string]$playerResult.retryAssemblyName
        retryFailure = [string]$playerResult.retryFailure
    }
    nativeGuardCoverage = $nativeCoverage
    runtimeSource = $runtimeSourcePath
    runtimeDheRuntimeSha256 = $runtimeDheRuntimeSha256
    packageTreeSha256 = $packageTreeHash
    packageLock = $packageLockPath
    runtimeManifest = if (Test-Path -LiteralPath $runtimeManifestOutput -PathType Leaf) {
        $runtimeManifestOutput
    } else { $null }
    sourcePreflight = $sourcePreflightReportPath
    cleanCheckoutGate = $cleanCheckoutReportPath
    projectPlan = $planPath
    projectPlanValidation = $projectPlanValidationPath
    batchReport = $batchReportPath
    buildIdentity = $identityPath
    buildIdentityReady = $true
    identityVersion = [int]$finalBuildIdentity.identityVersion
    aotSnapshotKind = [string]$finalBuildIdentity.aotSnapshotKind
    aotSnapshotSha256 = $baselineSnapshotHash
    nativeGuardSourceSha256 = [string]$finalBuildIdentity.nativeGuardSourceSha256
    nativeManifestSha256 = [string]$finalBuildIdentity.nativeManifestSha256
    nativeManifest = if (Test-Path -LiteralPath $nativeManifestOutput -PathType Leaf) {
        $nativeManifestOutput
    } else { $null }
    mvJson = $mvJsonPaths
    mvBytes = @($orderedPlanAssemblies | ForEach-Object { [string]$_.mvBytes })
    runtimePlan = $runtimePlanArchivePath
    runtimePlanProjectPath = $runtimePlanPath
    archiveManifest = Join-Path $ArchiveRoot "archive-manifest.json"
    archiveGate = $ArchiveRoot + ".gate.json"
    playerResult = $resultPath
    artifactValidation = Join-Path $OutputRoot "artifact-validation.json"
    player = $playerResult
}
$reportPath = Join-Path $OutputRoot "workflow-report.json"
$artifactValidationPath = Join-Path $OutputRoot "artifact-validation.json"
# Validate the artifacts before publishing a positive workflow report. This
# first pass does not consume the report itself, so a malformed report cannot
# mask a bad MV/native artifact.
    & (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson $mvJsonPaths `
        -MvBytes @($orderedPlanAssemblies | ForEach-Object { [string]$_.mvBytes }) `
        -BaselineAssembly $orderedBaselineStrippedPaths `
        -CurrentAssembly $orderedCurrentStrippedPaths `
        -NativeManifest $nativeManifestOutput `
        -BuildIdentity $identityPath `
        -RuntimePlan $runtimePlanArchivePath `
        -RequireCompleteCoverage:$coverageRequired `
        -Output $artifactValidationPath
    if ($LASTEXITCODE -ne 0) {
        throw "DHE artifact validation failed. See $artifactValidationPath"
    }
    $artifactValidation = Get-Content -Raw -LiteralPath $artifactValidationPath | ConvertFrom-Json
    $artifactValidationPassed = Get-DheStrictBooleanProperty $artifactValidation "passed" "DHE artifact validation passed"
    $report.artifactValidationPassed = $artifactValidationPassed
    $report.passed = $validationPassed -and $coverageGatePassed -and $artifactValidationPassed
    $report.releaseReady = $releaseReady -and $artifactValidationPassed
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 16), (New-Object Text.UTF8Encoding($false)))

    # Validate the published report as well. If this pass fails, the catch
    # block downgrades the report before surfacing the failure.
    & (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson $mvJsonPaths `
        -MvBytes @($orderedPlanAssemblies | ForEach-Object { [string]$_.mvBytes }) `
        -BaselineAssembly $orderedBaselineStrippedPaths `
        -CurrentAssembly $orderedCurrentStrippedPaths `
        -NativeManifest $nativeManifestOutput `
        -BuildIdentity $identityPath `
        -WorkflowReport $reportPath `
        -RuntimePlan $runtimePlanArchivePath `
        -RequireCompleteCoverage:$coverageRequired `
        -Output $artifactValidationPath
    if ($LASTEXITCODE -ne 0) {
        throw "Published DHE workflow report failed artifact validation. See $artifactValidationPath"
    }
    if ($Invocation -eq "Standalone") {
        # Script parameter splatting requires a hashtable. A string array such
        # as `("-InputRoot", $OutputRoot, ...)` is only valid for an external
        # host and is bound positionally when invoking another .ps1.
        $archiveGateParameters = @{
            InputRoot = $OutputRoot
            ArchiveRoot = $ArchiveRoot
            LabRoot = $LabRoot
            Output = $ArchiveRoot + ".gate.json"
        }
        if ($ForceOutput) { $archiveGateParameters.ForceOutput = $true }
        if ($coverageRequired) { $archiveGateParameters.RequireCompleteCoverage = $true }
        & (Join-Path $LabRoot "scripts/run-dhe-archive-gate.ps1") @archiveGateParameters
        if ($LASTEXITCODE -ne 0) {
            throw "DHE portable archive gate failed. See $ArchiveRoot.gate.json"
        }
    }
    if (-not $coverageGatePassed) {
        if (-not $nativeManifestAvailable) {
            throw "DHE workflow did not produce a native guard manifest. See $reportPath"
        }
        throw "DHE workflow has $($nativeCoverage.unsupportedChangedMethodCount) changed methods without a native guard. See $reportPath"
    }
    if ($releaseReady) {
        Write-Host "DHE demo workflow passed: $reportPath"
    } elseif (-not $coverageComplete) {
        Write-Host "DHE demo validation passed; release coverage is incomplete: $reportPath"
    } else {
        Write-Host "DHE demo adapter Player passed in $Mode mode: $reportPath"
    }
}
catch {
    try {
        if ($outputRootSafe -and (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
            $failure = [ordered]@{
                schemaVersion = 1
                format = "hybridclr.dhe-demo-workflow-failure.json"
                generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
                passed = $false
                error = $_.Exception.ToString()
                outputRoot = $OutputRoot
            }
            [IO.File]::WriteAllText(
                (Join-Path $OutputRoot "workflow-failure.json"),
                ($failure | ConvertTo-Json -Depth 8),
                (New-Object Text.UTF8Encoding($false)))
            if ($null -ne $reportPath -and (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                try {
                    $publishedReport = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
                    $publishedReport.passed = $false
                    $publishedReport.releaseReady = $false
                    $publishedReport.artifactValidationPassed = $false
                    [IO.File]::WriteAllText(
                        $reportPath,
                        ($publishedReport | ConvertTo-Json -Depth 16),
                        (New-Object Text.UTF8Encoding($false)))
                } catch {
                    Write-Warning "Unable to downgrade workflow report after failure: $($_.Exception.Message)"
                }
            }
        }
    } catch {
        Write-Warning "Unable to write DHE workflow failure report: $($_.Exception.Message)"
    }
    throw
}
finally {
    Restore-IdentitySourceSnapshot
    Restore-SettingsSnapshot
    Exit-DheWorkflowLock -Lock $workflowLock
}
exit 0
