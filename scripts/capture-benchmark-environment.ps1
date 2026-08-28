param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = "reports/benchmark-environment.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$cpu = @(Get-CimInstance Win32_Processor | Select-Object -First 1)
$computer = @(Get-CimInstance Win32_ComputerSystem | Select-Object -First 1)
$os = @(Get-CimInstance Win32_OperatingSystem | Select-Object -First 1)
$activePowerScheme = (& powercfg.exe /GETACTIVESCHEME 2>$null | Out-String).Trim()
$environment = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    os = [ordered]@{
        caption = if ($os.Count -gt 0) { [string]$os[0].Caption } else { [string]$env:OS }
        version = if ($os.Count -gt 0) { [string]$os[0].Version } else { "unknown" }
        buildNumber = if ($os.Count -gt 0) { [string]$os[0].BuildNumber } else { "unknown" }
    }
    cpu = [ordered]@{
        name = if ($cpu.Count -gt 0) { [string]$cpu[0].Name } else { [string]$env:PROCESSOR_IDENTIFIER }
        manufacturer = if ($cpu.Count -gt 0) { [string]$cpu[0].Manufacturer } else { "unknown" }
        physicalCores = if ($cpu.Count -gt 0) { [int]$cpu[0].NumberOfCores } else { 0 }
        logicalProcessors = if ($cpu.Count -gt 0) { [int]$cpu[0].NumberOfLogicalProcessors } else { [int]$env:NUMBER_OF_PROCESSORS }
        maxClockMhz = if ($cpu.Count -gt 0) { [int]$cpu[0].MaxClockSpeed } else { 0 }
    }
    memory = [ordered]@{
        totalPhysicalBytes = if ($computer.Count -gt 0) { [int64]$computer[0].TotalPhysicalMemory } else { 0 }
    }
    powerScheme = $activePowerScheme
    process = [ordered]@{
        priorityClass = "Normal"
        processorAffinity = "default"
        target = "StandaloneWindows64"
        configuration = "Release"
    }
}
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $LabRoot $Output }
$outputPath = [IO.Path]::GetFullPath($outputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$environment | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Benchmark environment: $outputPath"
