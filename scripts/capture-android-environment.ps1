param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DeviceSerial = "",
    [string]$Output = "reports/android-arm64-environment.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial

function Get-DeviceProperty([string]$Name) {
    return (((Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "getprop", $Name)).Output -join "").Trim())
}

function Read-DeviceFile([string]$Path) {
    $read = Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "cat", $Path) -AllowFailure
    if ($read.ExitCode -ne 0) { return $null }
    return (($read.Output -join "`n").Trim())
}

$batteryRaw = @((Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "dumpsys", "battery")).Output)
$battery = [ordered]@{}
foreach ($line in $batteryRaw) {
    if ([string]$line -match '^\s*([^:]+):\s*(.*)$') {
        $battery[$Matches[1].Trim()] = $Matches[2].Trim()
    }
}

$thermalRaw = @((Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "dumpsys", "thermalservice") -AllowFailure).Output)
$thermalStatus = @($thermalRaw | Where-Object { $_ -match '(?i)(thermal status|status):' } | Select-Object -First 1)
$cpuOnline = Read-DeviceFile "/sys/devices/system/cpu/online"
$cpuRecords = @()
for ($index = 0; $index -lt 32; $index++) {
    $base = "/sys/devices/system/cpu/cpu$index/cpufreq"
    $max = Read-DeviceFile "$base/cpuinfo_max_freq"
    if ($null -eq $max) { continue }
    $cpuRecords += [ordered]@{
        cpu = $index
        governor = Read-DeviceFile "$base/scaling_governor"
        currentKHz = Read-DeviceFile "$base/scaling_cur_freq"
        maximumKHz = $max
        minimumKHz = Read-DeviceFile "$base/cpuinfo_min_freq"
    }
}

$outputPath = if ([IO.Path]::IsPathRooted($Output)) {
    [IO.Path]::GetFullPath($Output)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $Output))
}
$rawThermalPath = [IO.Path]::ChangeExtension($outputPath, ".thermal.txt")
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$thermalRaw | Set-Content -LiteralPath $rawThermalPath -Encoding UTF8

$ndkProperties = Join-Path $tools.Ndk "source.properties"
$java = Join-Path $tools.AndroidRoot "OpenJDK/bin/java.exe"
$javaVersion = if (Test-Path -LiteralPath $java) { @(& $java -version 2>&1) -join "`n" } else { "" }
$report = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    device = [ordered]@{
        serial = $device.Serial
        manufacturer = Get-DeviceProperty "ro.product.manufacturer"
        model = Get-DeviceProperty "ro.product.model"
        product = Get-DeviceProperty "ro.product.name"
        hardware = Get-DeviceProperty "ro.hardware"
        socManufacturer = Get-DeviceProperty "ro.soc.manufacturer"
        socModel = Get-DeviceProperty "ro.soc.model"
        primaryAbi = $device.Abi
        abiList = $device.AbiList
        androidRelease = Get-DeviceProperty "ro.build.version.release"
        androidSdk = Get-DeviceProperty "ro.build.version.sdk"
        securityPatch = Get-DeviceProperty "ro.build.version.security_patch"
        buildFingerprint = Get-DeviceProperty "ro.build.fingerprint"
        kernel = ((Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "uname", "-a")).Output -join " ").Trim()
    }
    state = [ordered]@{
        battery = $battery
        thermalStatusLine = if ($thermalStatus.Count -gt 0) { [string]$thermalStatus[0] } else { $null }
        thermalDump = [IO.Path]::GetRelativePath($LabRoot, $rawThermalPath).Replace('\', '/')
        cpuOnline = $cpuOnline
        cpuFrequency = $cpuRecords
    }
    toolchain = [ordered]@{
        adbVersion = (@(& $tools.Adb version 2>&1) -join "`n")
        sdkRoot = $tools.Sdk
        buildToolsVersion = $tools.BuildToolsVersion
        ndkRoot = $tools.Ndk
        ndkProperties = if (Test-Path -LiteralPath $ndkProperties) { (Get-Content -Raw $ndkProperties).Trim() } else { "" }
        javaVersion = $javaVersion
    }
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
$report | ConvertTo-Json -Depth 12 | Write-Host
Write-Host "Android environment: $outputPath"
