param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,
    [string]$EditorProcessName = "Tuanjie.exe",
    [int]$TimeoutSeconds = 900,
    [int]$StableAbsenceSeconds = 5
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$absenceStartedAt = $null

while ($true) {
    $active = Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq $EditorProcessName -and $_.CommandLine -like "*$ProjectRoot*"
    }

    if ($active) {
        $absenceStartedAt = $null
    } elseif ($null -eq $absenceStartedAt) {
        $absenceStartedAt = Get-Date
    } elseif (((Get-Date) - $absenceStartedAt).TotalSeconds -ge $StableAbsenceSeconds) {
        return
    }

    if ((Get-Date) -ge $deadline) {
        $pids = ($active | Select-Object -ExpandProperty ProcessId) -join ", "
        throw "Timed out waiting for $EditorProcessName processes to exit for project $ProjectRoot. Active PIDs: $pids"
    }

    Start-Sleep -Milliseconds 500
}
