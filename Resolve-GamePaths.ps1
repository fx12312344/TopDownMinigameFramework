# Build/install helper only. Resolves references from an existing local installation;
# never searches for, downloads, or copies game assemblies into a distribution.
function Resolve-LocalGameManaged {
    param([string]$GameManaged, [string]$GameRoot)
    if ([string]::IsNullOrWhiteSpace($GameManaged)) { $GameManaged = $env:GameManaged }
    if ([string]::IsNullOrWhiteSpace($GameManaged)) { $GameManaged = $env:PROBABLY_STOLEN_MANAGED }
    if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = $env:GameRoot }
    if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = $env:PROBABLY_STOLEN_ROOT }
    if ([string]::IsNullOrWhiteSpace($GameManaged) -and ![string]::IsNullOrWhiteSpace($GameRoot)) {
        $GameManaged = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
    }
    if ([string]::IsNullOrWhiteSpace($GameManaged)) {
        throw 'Local game references are not configured. Pass -GameManaged or -GameRoot, or set PROBABLY_STOLEN_MANAGED / PROBABLY_STOLEN_ROOT to an existing local installation. No game files are included or downloaded. Tests/Tests.csproj can run without game files.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $GameManaged 'Assembly-CSharp.dll') -PathType Leaf)) {
        throw "GameManaged must contain the local game's Assembly-CSharp.dll: $GameManaged"
    }
    return (Resolve-Path -LiteralPath $GameManaged).Path
}
function Resolve-LocalGameRoot {
    param([string]$GameRoot, [string]$GameManaged)
    if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = $env:GameRoot }
    if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = $env:PROBABLY_STOLEN_ROOT }
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $managed = Resolve-LocalGameManaged -GameManaged $GameManaged
        $GameRoot = Split-Path -Parent (Split-Path -Parent $managed)
    }
    if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'Probably Stolen.exe') -PathType Leaf)) {
        throw "GameRoot must contain the local game's Probably Stolen.exe: $GameRoot. Pass -GameRoot or set PROBABLY_STOLEN_ROOT; installation is optional."
    }
    return (Resolve-Path -LiteralPath $GameRoot).Path
}
