param(
    [string]$Manifest = "manifests/test-manifest.json",
    [string]$Reference = "reports/reference-result.json",
    [string]$Player,
    [string]$Output = "reports/coverage-summary.json",
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Read-Json([string]$Path) {
    $resolved = Resolve-LabPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) { throw "Required input does not exist: $resolved" }
    $document = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    return ,$document
}

function Count-Values($Values) {
    $counts = [ordered]@{}
    foreach ($value in @($Values)) {
        $key = [string]$value
        if (-not $counts.Contains($key)) { $counts[$key] = 0 }
        $counts[$key]++
    }
    return $counts
}

function Summarize-Cases($Cases, [switch]$IncludeStatus) {
    $caseArray = @($Cases)
    $featureValues = foreach ($case in $caseArray) { @($case.features) }
    $layerCategoryValues = foreach ($case in $caseArray) { "$($case.layer)/$($case.category)" }
    $summary = [ordered]@{
        total = $caseArray.Count
        byLayer = Count-Values ($caseArray | ForEach-Object layer)
        byCategory = Count-Values ($caseArray | ForEach-Object category)
        byFeature = Count-Values $featureValues
        byLayerCategory = Count-Values $layerCategoryValues
    }
    if ($IncludeStatus) {
        $summary.passed = @($caseArray | Where-Object status -eq "passed").Count
        $summary.failed = @($caseArray | Where-Object status -eq "failed").Count
    }
    return $summary
}

$manifestPath = Resolve-LabPath $Manifest
$referencePath = Resolve-LabPath $Reference
$manifestDocument = Read-Json $Manifest
$referenceDocument = Read-Json $Reference
$playerResult = $null
$playerPath = $null
if (-not [string]::IsNullOrWhiteSpace($Player)) {
    $playerPath = Resolve-LabPath $Player
    $playerResult = Read-Json $Player
}

$manifestIds = @($manifestDocument.cases | ForEach-Object id)
$referenceIds = @($referenceDocument.cases | ForEach-Object id)
if ((@($manifestIds | Sort-Object) -join "`n") -cne (@($referenceIds | Sort-Object) -join "`n")) {
    throw "Manifest and reference result case IDs differ."
}
if ($playerResult -and ((@($manifestIds | Sort-Object) -join "`n") -cne (@($playerResult.cases | ForEach-Object id | Sort-Object) -join "`n"))) {
    throw "Manifest and Player result case IDs differ."
}

$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    manifest = [ordered]@{
        path = $manifestPath
        sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        suiteId = $manifestDocument.suiteId
        summary = Summarize-Cases $manifestDocument.cases
    }
    reference = [ordered]@{
        path = $referencePath
        summary = Summarize-Cases $referenceDocument.cases -IncludeStatus
    }
}
if ($playerResult) {
    $report.player = [ordered]@{
        path = $playerPath
        summary = Summarize-Cases $playerResult.cases -IncludeStatus
    }
}

$outputPath = Resolve-LabPath $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Coverage: $($manifestDocument.cases.Count) manifest cases; $($report.reference.summary.byCategory.Count) categories; $($report.reference.summary.byFeature.Count) features"
Write-Host "Coverage report: $outputPath"
