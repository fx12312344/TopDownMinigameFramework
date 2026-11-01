$ErrorActionPreference = 'Stop'
$example = Join-Path $PSScriptRoot '..\RaidDocks'
$generator = Join-Path $example 'generate-content-localization.ps1'
$fixtures = Join-Path $PSScriptRoot 'obj\localization-fixtures'
New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
$manifest = Join-Path $example 'manifest.xml'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$sourceJson = [System.IO.File]::ReadAllText((Join-Path $example 'pack.json'), $utf8)
$script:assertions = 0
function Assert([bool]$Condition, [string]$Message) {
    $script:assertions++
    if (!$Condition) { throw "FAIL: $Message" }
}
function Check-Rejected([string]$Name, [string]$Json, [string]$ExpectedMessage) {
    $pack = Join-Path $fixtures ($Name + '.json')
    $csv = Join-Path $fixtures ($Name + '.csv')
    [System.IO.File]::WriteAllText($pack, $Json, $utf8)
    [System.IO.File]::WriteAllText($csv, 'untouched sentinel', $utf8)
    $failure = $null
    try { & $generator -PackPath $pack -ManifestPath $manifest -OutputPath $csv | Out-Null } catch { $failure = $_.Exception.Message }
    Assert ($null -ne $failure -and $failure.Contains($ExpectedMessage)) ($Name + ' rejected for expected reason')
    Assert ([System.IO.File]::ReadAllText($csv) -ceq 'untouched sentinel') ($Name + ' leaves output untouched')
}
foreach ($case in @(
    @{ Name = 'actual-lf'; Value = "unsafe`nname" },
    @{ Name = 'actual-cr'; Value = "unsafe`rname" },
    @{ Name = 'literal-backslash-n'; Value = 'unsafe\name' }
)) {
    $pack = ConvertFrom-Json -InputObject $sourceJson
    # Put hazards in an unknown nested field, not just nameZh: ALL JSON strings must be checked.
    Add-Member -InputObject $pack -NotePropertyName futureField -NotePropertyValue @(@{ nested = $case.Value })
    Check-Rejected $case.Name (ConvertTo-Json -InputObject $pack -Depth 100) 'newline or literal backslash+n'
}
$pack = ConvertFrom-Json -InputObject $sourceJson
Add-Member -InputObject $pack -NotePropertyName 'unsafe\name' -NotePropertyValue 1
Check-Rejected 'property-name' (ConvertTo-Json -InputObject $pack -Depth 100) 'newline or literal backslash+n'
$pack = ConvertFrom-Json -InputObject $sourceJson
$pack.id = 'EXAMPLE.raiddocks'
Check-Rejected 'provider-case-mismatch' (ConvertTo-Json -InputObject $pack -Depth 100) 'pack.id must exactly equal manifest.ID'
$pack = ConvertFrom-Json -InputObject $sourceJson
$pack.apiVersion = 2
Check-Rejected 'wrong-version' (ConvertTo-Json -InputObject $pack -Depth 100) 'apiVersion must explicitly be 1'
Check-Rejected 'invalid-json' '{invalid}' ''
# Exercise first-comma split, doubled quotes, edge spaces, Unicode and safe backslashes.
$pack = ConvertFrom-Json -InputObject $sourceJson
$pack.nameEn = '  Dock, "quoted" \\path / 中文  '
$goodPack = Join-Path $fixtures 'quoted-comma.json'
$goodCsv = Join-Path $fixtures 'quoted-comma.csv'
[System.IO.File]::WriteAllText($goodPack, (ConvertTo-Json -InputObject $pack -Depth 100), $utf8)
& $generator -PackPath $goodPack -ManifestPath $manifest -OutputPath $goodCsv | Out-Null
$line = [System.IO.File]::ReadAllText($goodCsv, $utf8).TrimEnd("`n")
$value = $line.Substring($line.IndexOf(',') + 1).Trim()
if ($value.StartsWith('"') -and $value.EndsWith('"')) { $value = $value.Substring(1, $value.Length - 2).Replace('""', '"') }
$value = $value.Replace('\n', "`n")
$restored = ConvertFrom-Json -InputObject $value
Assert ($restored.nameEn -ceq $pack.nameEn) 'game CSV parsing preserves commas/quotes/spaces/Unicode/safe backslashes'
Assert ($restored.id -ceq $pack.id) 'valid fixture keeps pack identity'
Write-Output "PASS: $script:assertions localization-generator safety assertions."
