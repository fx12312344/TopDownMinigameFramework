param([string]$GameManaged, [string]$GameRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\Resolve-GamePaths.ps1')
$GameManaged = Resolve-LocalGameManaged -GameManaged $GameManaged -GameRoot $GameRoot
# JSON is a build-time input to the game's supported localization asset, not DLL source.
& (Join-Path $PSScriptRoot 'generate-content-localization.ps1')
# Keep existing PNG artwork unchanged. generate-textures.ps1 is opt-in, not a build step.
& dotnet build (Join-Path $PSScriptRoot 'RaidDocksExample.csproj') -c Release --nologo "-p:GameManaged=$GameManaged"
if ($LASTEXITCODE -ne 0) { throw 'Independent game-only example DLL build failed.' }
$out = Join-Path $PSScriptRoot 'dist\RaidDocksExample'
New-Item -ItemType Directory -Force -Path (Join-Path $out 'Textures\Items'), (Join-Path $out 'Localization') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\RaidDocksExample.dll'), (Join-Path $PSScriptRoot 'manifest.xml') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Localization\en.csv') -Destination (Join-Path $out 'Localization\en.csv') -Force
foreach ($png in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Textures\Items') -Filter '*.png') {
    Copy-Item -LiteralPath $png.FullName -Destination (Join-Path $out 'Textures\Items') -Force
}
if (Test-Path -LiteralPath (Join-Path $out 'RaidPrototype.dll')) { throw 'Distribution must NOT contain a private copy of RaidPrototype.dll.' }
Write-Output "Built independent game-only example: $out"
Write-Output 'Install alongside RaidPrototype, not inside it. The framework ModLoader bridge reads Localization/en.csv; rebuild assets after editing pack.json.'
