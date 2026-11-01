param([string]$GameManaged, [string]$GameRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Resolve-GamePaths.ps1')
$GameManaged = Resolve-LocalGameManaged -GameManaged $GameManaged -GameRoot $GameRoot
& dotnet build (Join-Path $PSScriptRoot 'RaidPrototype.csproj') -c Release "-p:GameManaged=$GameManaged" --nologo
if($LASTEXITCODE -ne 0){throw 'Mod build failed'}
& dotnet run --project (Join-Path $PSScriptRoot 'Tests\Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Model tests failed'}
$out=Join-Path $PSScriptRoot 'dist\RaidPrototype'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\RaidPrototype.dll'),(Join-Path $PSScriptRoot 'manifest.xml') -Destination $out -Force
Write-Output "Built: $out"
