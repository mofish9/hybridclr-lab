param(
    [string]$Reference = "reports/reference-result.json",
    [string]$Actual,
    [string]$Output = "reports/differential-result.json",
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
if ([string]::IsNullOrWhiteSpace($Actual)) { throw "-Actual is required." }

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

$referencePath = Resolve-LabPath $Reference
$actualPath = Resolve-LabPath $Actual
$outputPath = Resolve-LabPath $Output
$referenceResult = Get-Content -Raw $referencePath | ConvertFrom-Json
$actualResult = Get-Content -Raw $actualPath | ConvertFrom-Json

$referenceCases = @{}
foreach ($case in $referenceResult.cases) {
    if ($referenceCases.ContainsKey($case.id)) { throw "Duplicate reference case: $($case.id)" }
    $referenceCases[$case.id] = $case
}
$actualCases = @{}
foreach ($case in $actualResult.cases) {
    if ($actualCases.ContainsKey($case.id)) { throw "Duplicate actual case: $($case.id)" }
    $actualCases[$case.id] = $case
}

$differences = [Collections.Generic.List[object]]::new()
$fields = @("category", "layer", "status", "returnValue", "sideEffect", "exceptionType")
function Compare-FeatureList($expected, $actual) {
    $expectedValues = @($expected | ForEach-Object { [string]$_ })
    $actualValues = @($actual | ForEach-Object { [string]$_ })
    if ($expectedValues.Count -ne $actualValues.Count) { return $false }
    for ($i = 0; $i -lt $expectedValues.Count; $i++) {
        if ($expectedValues[$i] -cne $actualValues[$i]) { return $false }
    }
    return $true
}
foreach ($id in @($referenceCases.Keys | Sort-Object)) {
    if (-not $actualCases.ContainsKey($id)) {
        $differences.Add([ordered]@{ case = $id; field = "case"; expected = "present"; actual = "missing" })
        continue
    }
    foreach ($field in $fields) {
        $expected = $referenceCases[$id].$field
        $actualValue = $actualCases[$id].$field
        if ($expected -cne $actualValue) {
            $differences.Add([ordered]@{ case = $id; field = $field; expected = $expected; actual = $actualValue })
        }
    }
    if (-not (Compare-FeatureList $referenceCases[$id].features $actualCases[$id].features)) {
        $differences.Add([ordered]@{ case = $id; field = "features"; expected = @($referenceCases[$id].features); actual = @($actualCases[$id].features) })
    }
}
foreach ($id in @($actualCases.Keys | Sort-Object)) {
    if (-not $referenceCases.ContainsKey($id)) {
        $differences.Add([ordered]@{ case = $id; field = "case"; expected = "missing"; actual = "present" })
    }
}

$report = [ordered]@{
    schemaVersion = 2
    comparedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    reference = $referencePath
    actual = $actualPath
    summary = [ordered]@{
        referenceCases = $referenceCases.Count
        actualCases = $actualCases.Count
        differences = $differences.Count
        passed = $differences.Count -eq 0
    }
    differences = $differences
}
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Differential result: $($differences.Count) differences"
Write-Host "Report: $outputPath"
if ($differences.Count -gt 0) { throw "Managed differential comparison failed." }
