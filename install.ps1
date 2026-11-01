param([string]$GameRoot, [string]$GameManaged)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Resolve-GamePaths.ps1')
$GameRoot = Resolve-LocalGameRoot -GameRoot $GameRoot -GameManaged $GameManaged
if(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Exit the game before installing. No running-game files were changed.'}
$packages=@(@{Name='RaidPrototype';Source=(Join-Path $PSScriptRoot 'dist\RaidPrototype')},@{Name='RaidDocksExample';Source=(Join-Path $PSScriptRoot 'Examples\RaidDocks\dist\RaidDocksExample')})
foreach($p in $packages){if(!(Test-Path -LiteralPath (Join-Path $p.Source 'manifest.xml'))){throw "Build first: $($p.Source)"}}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach($p in $packages){
 $target=Join-Path (Join-Path $GameRoot 'Mods') $p.Name
 if(Test-Path -LiteralPath $target){$backup=Join-Path (Join-Path $GameRoot 'ModBackups') ($p.Name+'-'+$stamp);if(Test-Path -LiteralPath $backup){throw 'Backup target exists'};Copy-Item -LiteralPath $target -Destination $backup -Recurse}
 New-Item -ItemType Directory -Force -Path $target | Out-Null
 foreach($file in Get-ChildItem -LiteralPath $p.Source -Recurse -File){$rel=$file.FullName.Substring($p.Source.Length+1);$dest=Join-Path $target $rel;New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest)|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $dest -Force;if((Get-FileHash -LiteralPath $dest).Hash-ne(Get-FileHash -LiteralPath $file.FullName).Hash){throw "Hash mismatch: $dest"}}
 Write-Output "Installed and verified: $target"
}
Write-Output 'Enable both mods in the game, then return to the main menu. No save files were modified by this installer.'
