param(
    [string]$PackPath = (Join-Path $PSScriptRoot 'pack.json'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'manifest.xml'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'Localization\en.csv')
)
$ErrorActionPreference = 'Stop'
# Build-time only. The game loads CSV assets; the framework reads mod.Localization['en'].
function Assert-SafeJsonStrings($Value, [string]$Location) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        # Original ParseCsvValue unconditionally replaces the two characters \n with LF.
        # Both actual line breaks (JSON escapes) and literal backslash+n are unsafe.
        if ($Value.Contains("`r") -or $Value.Contains("`n") -or $Value.Contains('\n')) {
            throw "JSON string at $Location contains a newline or literal backslash+n; the game CSV parser would corrupt it."
        }
        return
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            Assert-SafeJsonStrings $property.Name ($Location + ' (property name)')
            Assert-SafeJsonStrings $property.Value ($Location + '.' + $property.Name)
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($entry in $Value) {
            Assert-SafeJsonStrings $entry ($Location + '[' + $index + ']')
            $index++
        }
    }
}
function Parse-GameCsvValue([string]$Value) {
    # Exact original rule order; not a general RFC 4180 parser.
    $Value = $Value.Trim()
    if ($Value.StartsWith('"') -and $Value.EndsWith('"')) {
        $Value = $Value.Substring(1, $Value.Length - 2).Replace('""', '"')
    }
    return $Value.Replace('\n', "`n")
}
$json = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PackPath).Path, [System.Text.Encoding]::UTF8)
$parsed = ConvertFrom-Json -InputObject $json
if ($null -eq $parsed -or $parsed -isnot [System.Management.Automation.PSCustomObject]) { throw 'pack.json root must be an object.' }
if ($parsed.apiVersion -cne 1) { throw 'pack.json apiVersion must explicitly be 1.' }
[xml]$manifest = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ManifestPath).Path, [System.Text.Encoding]::UTF8)
$providerId = [string]$manifest.Manifest.ID
if ([string]::IsNullOrWhiteSpace($providerId) -or $parsed.id -cne $providerId) { throw 'pack.id must exactly equal manifest.ID (case-sensitive).' }
Assert-SafeJsonStrings $parsed '$'
$compact = ConvertTo-Json -InputObject $parsed -Depth 100 -Compress
if ($compact.Contains("`r") -or $compact.Contains("`n") -or $compact.Contains('\n')) { throw 'Minified JSON still contains unsafe newline sequences.' }
$key = 'raid_framework_pack_v1'
$quoted = '"' + $compact.Replace('"', '""') + '"'
$csv = $key + ',' + $quoted
$restored = Parse-GameCsvValue $csv.Substring($csv.IndexOf(',') + 1)
# Parse again to prove the game-restored value remains legal JSON and unchanged data.
$roundTrip = ConvertTo-Json -InputObject (ConvertFrom-Json -InputObject $restored) -Depth 100 -Compress
if ($restored -cne $compact -or $roundTrip -cne $compact) { throw 'Game ParseCsvValue round trip changed pack JSON.' }
$absoluteOutput = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $absoluteOutput) | Out-Null
[System.IO.File]::WriteAllText($absoluteOutput, $csv + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated one-row Localization CSV and verified game parser round trip: $absoluteOutput"
