function Get-AndroidLabTools {
    param([Parameter(Mandatory = $true)][string]$LabRoot)

    $lock = Get-Content -Raw (Join-Path $LabRoot "manifests/repo-lock.json") | ConvertFrom-Json
    $editor = [IO.Path]::GetFullPath([string]$lock.engine.executablePath)
    $androidRoot = Join-Path (Split-Path $editor) "Data/PlaybackEngines/AndroidPlayer"
    $sdk = Join-Path $androidRoot "SDK"
    $ndk = Join-Path $androidRoot "NDK"
    $adb = Join-Path $sdk "platform-tools/adb.exe"
    $buildTools = Get-ChildItem -LiteralPath (Join-Path $sdk "build-tools") -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    if (-not (Test-Path -LiteralPath $adb)) { throw "ADB was not found: $adb" }
    if (-not (Test-Path -LiteralPath $ndk)) { throw "Android NDK was not found: $ndk" }
    if ($null -eq $buildTools) { throw "Android SDK build-tools were not found under $sdk" }

    return [PSCustomObject]@{
        Editor = $editor
        AndroidRoot = $androidRoot
        Sdk = $sdk
        Ndk = $ndk
        Adb = $adb
        Aapt = Join-Path $buildTools.FullName "aapt.exe"
        BuildToolsVersion = $buildTools.Name
    }
}

function Invoke-LabAdb {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string[]]$CommandArguments,
        [switch]$AllowFailure
    )

    $output = @(& $Adb -s $Serial @CommandArguments 2>&1)
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "ADB command failed ($exitCode): adb -s $Serial $($CommandArguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return [PSCustomObject]@{ ExitCode = $exitCode; Output = $output }
}

function Wake-AndroidLabDevice {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "input", "keyevent", "KEYCODE_WAKEUP") -AllowFailure | Out-Null
    Start-Sleep -Milliseconds 250
}

function Assert-AndroidLabInstallReady {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $policy = (Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "dumpsys", "window", "policy")).Output -join [Environment]::NewLine
    if ($policy -match '(?m)^\s*secure=true\s*$' -and
        $policy -match '(?m)^\s*showingAndNotOccluded=true\s*$') {
        throw "Android device '$Serial' is at a secure lock screen. Unlock it and confirm USB installation before installing an APK."
    }
    if ($policy -match '(?m)^\s*interactiveState=INTERACTIVE_STATE_SLEEP\s*$') {
        throw "Android device '$Serial' is asleep. Wake it before installing an APK."
    }
}

function Install-AndroidLabApk {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Apk,
        [Parameter(Mandatory = $true)][string]$PackageName
    )

    $Apk = [IO.Path]::GetFullPath($Apk)
    if (-not (Test-Path -LiteralPath $Apk)) { throw "Android APK was not found: $Apk" }
    $expectedHash = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash.ToLowerInvariant()
    $remoteApk = "/data/local/tmp/hybridclr-lab-install-$([IO.Path]::GetFileNameWithoutExtension($Apk)).apk"

    Wake-AndroidLabDevice -Adb $Adb -Serial $Serial
    Assert-AndroidLabInstallReady -Adb $Adb -Serial $Serial
    $push = @(& $Adb -s $Serial push $Apk $remoteApk 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to push Android APK for installation.`n$($push -join [Environment]::NewLine)"
    }

    try {
        $install = Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "pm", "install", "-r", $remoteApk)
        $installText = @($install.Output | ForEach-Object { [string]$_ })
        if (@($installText | Where-Object { $_ -match "^Success\s*$" }).Count -ne 1) {
            throw "Android package manager did not report Success.`n$($installText -join [Environment]::NewLine)"
        }
        Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "input", "keyevent", "KEYCODE_HOME") -AllowFailure | Out-Null
        Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "am", "force-stop", "com.android.packageinstaller") -AllowFailure | Out-Null
    }
    finally {
        Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "rm", "-f", $remoteApk) -AllowFailure | Out-Null
    }

    $devicePathOutput = Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "pm", "path", $PackageName)
    $devicePath = @($devicePathOutput.Output | ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -match "^package:.+base\.apk$" } | Select-Object -First 1)
    if ($devicePath.Count -ne 1) { throw "Unable to resolve installed base.apk for '$PackageName'." }
    $devicePath = $devicePath[0].Substring(8)
    $deviceHashOutput = Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "sha256sum", $devicePath)
    $deviceHash = @($deviceHashOutput.Output | ForEach-Object { [string]$_ } |
        Where-Object { $_ -match "^([0-9A-Fa-f]{64})\s" } | ForEach-Object { $Matches[1].ToLowerInvariant() } |
        Select-Object -First 1)
    if ($deviceHash.Count -ne 1 -or $deviceHash[0] -ne $expectedHash) {
        throw "Installed APK hash mismatch for '$PackageName': expected $expectedHash, actual $($deviceHash -join ', ')"
    }

    Write-Host "Installed and verified Android APK: $PackageName ($expectedHash)"
}

function Get-AndroidLabDevice {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [string]$Serial = ""
    )

    $lines = @(& $Adb devices -l 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate ADB devices.`n$($lines -join [Environment]::NewLine)" }
    $records = @($lines | Select-Object -Skip 1 | Where-Object { $_ -match '^([^\s]+)\s+([^\s]+)' } | ForEach-Object {
        [PSCustomObject]@{ Serial = $Matches[1]; State = $Matches[2]; Raw = [string]$_ }
    })

    if (-not [string]::IsNullOrWhiteSpace($Serial)) {
        $matches = @($records | Where-Object { $_.Serial -eq $Serial })
        if ($matches.Count -ne 1) { throw "ADB device '$Serial' was not found." }
        if ($matches[0].State -ne "device") { throw "ADB device '$Serial' is $($matches[0].State), not ready." }
        $device = $matches[0]
    }
    else {
        $ready = @($records | Where-Object { $_.State -eq "device" })
        if ($ready.Count -ne 1) {
            throw "Expected exactly one ready ADB device, found $($ready.Count). Pass -DeviceSerial when multiple devices are connected."
        }
        $device = $ready[0]
    }

    $abi = ((Invoke-LabAdb -Adb $Adb -Serial $device.Serial -CommandArguments @("shell", "getprop", "ro.product.cpu.abi")).Output -join "").Trim()
    $abilist = ((Invoke-LabAdb -Adb $Adb -Serial $device.Serial -CommandArguments @("shell", "getprop", "ro.product.cpu.abilist")).Output -join "").Trim()
    if ($abi -ne "arm64-v8a" -or @($abilist -split ',' | Where-Object { $_ -eq "arm64-v8a" }).Count -eq 0) {
        throw "Device '$($device.Serial)' is not an ARM64 primary-ABI device (abi='$abi', abilist='$abilist')."
    }

    return [PSCustomObject]@{ Serial = $device.Serial; Abi = $abi; AbiList = $abilist; Description = $device.Raw }
}

function Get-AndroidLabActivity {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$PackageName
    )

    $resolved = Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @(
        "shell", "cmd", "package", "resolve-activity", "--brief", $PackageName)
    $component = @($resolved.Output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -match '^[^\s]+/[^\s]+$' } | Select-Object -Last 1)
    if ($component.Count -ne 1) { throw "Unable to resolve the launch activity for '$PackageName'." }
    return [string]$component[0]
}

function Start-AndroidLabPlayer {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Component,
        [Parameter(Mandatory = $true)][string[]]$UnityArguments
    )

    foreach ($argument in $UnityArguments) {
        if ($argument -match '[\s"'']') { throw "Android Unity argument contains unsupported whitespace or quotes: $argument" }
    }
    Wake-AndroidLabDevice -Adb $Adb -Serial $Serial
    $unityCommandLine = $UnityArguments -join ' '
    $quotedUnityCommandLine = '"' + $unityCommandLine + '"'
    Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @(
        "shell", "am", "start", "-S", "-n", $Component, "-e", "unity", $quotedUnityCommandLine) | Out-Null
}

function Receive-AndroidLabResult {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$DevicePath,
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [int]$TimeoutSeconds = 300,
        [string]$FailureLogPath = ""
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $probe = Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "ls", $DevicePath) -AllowFailure
        if ($probe.ExitCode -eq 0) {
            New-Item -ItemType Directory -Force -Path (Split-Path $LocalPath) | Out-Null
            $pull = @(& $Adb -s $Serial pull $DevicePath $LocalPath 2>&1)
            if ($LASTEXITCODE -ne 0) { throw "Unable to pull Android result '$DevicePath'.`n$($pull -join [Environment]::NewLine)" }
            return
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not [string]::IsNullOrWhiteSpace($FailureLogPath)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $FailureLogPath) | Out-Null
        @(& $Adb -s $Serial logcat -d -v threadtime "Unity:*" "AndroidRuntime:E" "ActivityManager:W" "*:S" 2>&1) |
            Set-Content -LiteralPath $FailureLogPath -Encoding UTF8
    }
    throw "Android Player did not produce '$DevicePath' within $TimeoutSeconds seconds."
}

function Remove-AndroidLabDeviceFile {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$DevicePath
    )

    if (-not $DevicePath.StartsWith("/sdcard/Android/data/", [StringComparison]::Ordinal)) {
        throw "Refusing to remove a device file outside the app external-data root: $DevicePath"
    }
    Invoke-LabAdb -Adb $Adb -Serial $Serial -CommandArguments @("shell", "rm", "-f", $DevicePath) | Out-Null
}
