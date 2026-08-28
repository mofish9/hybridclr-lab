param(
    [Parameter(Mandatory = $true)]
    [string]$GeneratedCppRoot,
    [Parameter(Mandatory = $true)]
    [string]$DeclaringType,
    [Parameter(Mandatory = $true)]
    [string]$MethodName
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($GeneratedCppRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Generated C++ directory was not found: $root"
}

$marker = "HYBRIDCLR_DHE_AOT_PROBE:$DeclaringType::$MethodName"
$escapedType = [regex]::Escape($DeclaringType)
$escapedMethod = [regex]::Escape($MethodName)
$pattern = "(?m)^// [^\r\n]*$escapedType::$escapedMethod\([^\r\n]*\)\r?\n(?<signature>[^\r\n{};]*\b[A-Za-z_][A-Za-z0-9_]*\s*\([^\r\n{};]*\)\s*)\{"
$matches = New-Object System.Collections.Generic.List[object]
foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cpp)) {
    $source = [IO.File]::ReadAllText($file.FullName)
    if ($source.Contains($marker)) {
        continue
    }
    $match = [regex]::Match($source, $pattern)
    if ($match.Success) {
        $matches.Add([ordered]@{ file = $file.FullName; source = $source; match = $match })
    }
}
if ($matches.Count -ne 1) {
    if ($matches.Count -eq 0) {
        $alreadyPresent = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cpp | Where-Object {
            [IO.File]::ReadAllText($_.FullName).Contains($marker)
        })
        if ($alreadyPresent.Count -eq 1) {
            Write-Host "DHE AOT probe already present: $DeclaringType::$MethodName -> $($alreadyPresent[0].FullName)"
            exit 0
        }
    }
    throw "Expected one generated definition for '$DeclaringType::$MethodName', found $($matches.Count)."
}

$resolved = $matches[0]
$signature = [string]$resolved.match.Groups["signature"].Value
if ($signature -notmatch '\bconst\s+RuntimeMethod\s*\*\s*method\b') {
    throw "Generated definition '$DeclaringType::$MethodName' has no RuntimeMethod parameter."
}
$probe = "`r`n    // $marker`r`n    hybridclr::dhe::RecordAotEntry();`r`n"
$insertAt = $resolved.match.Index + $resolved.match.Length
$text = $resolved.source.Insert($insertAt, $probe)
if ($text -notmatch '(?m)^#include\s+"hybridclr/DheRuntime\.h"') {
    $text = '#include "hybridclr/DheRuntime.h"' + "`r`n" + $text
}
[IO.File]::WriteAllText($resolved.file, $text, (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE AOT probe injected: $DeclaringType::$MethodName -> $($resolved.file)"
exit 0
