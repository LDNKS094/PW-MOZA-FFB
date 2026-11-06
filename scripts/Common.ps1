$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PresetUuid = '{5a70e8ad-f59c-4b37-b598-2d8083691f5f}'

function Resolve-GameDirectory([string]$Directory) {
    if ($Directory) {
        $base = (Resolve-Path -LiteralPath $Directory).Path
        $candidates = @($base, (Join-Path $base 'ProjectWingman\Binaries\Win64'))
    } else {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
        $libraries = @($steam)
        if ($steam) {
            $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $vdf) {
                foreach ($match in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                    $libraries += $match.Groups[1].Value.Replace('\\', '\')
                }
            }
        }
        $candidates = @($libraries | Where-Object { $_ } | ForEach-Object {
            $manifest = Join-Path $_ 'steamapps\appmanifest_895870.acf'
            if (Test-Path -LiteralPath $manifest) {
                $install = [regex]::Match([IO.File]::ReadAllText($manifest), '"installdir"\s+"([^"]+)"')
                if ($install.Success) { Join-Path $_ ('steamapps\common\' + $install.Groups[1].Value + '\ProjectWingman\Binaries\Win64') }
            }
        })
    }
    $games = @($candidates | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ 'ProjectWingman-Win64-Shipping.exe') -PathType Leaf
    } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') } | Select-Object -Unique)
    if ($games.Count -ne 1) { throw 'Specify -GameDirectory with the Project Wingman folder or its ProjectWingman\Binaries\Win64 directory.' }
    return $games[0]
}
function Assert-Closed {
    if (Get-Process 'ProjectWingman-Win64-Shipping' -ErrorAction SilentlyContinue) { throw 'Exit Project Wingman before installing or uninstalling.' }
    if (Get-Process 'MOZA Cockpit' -ErrorAction SilentlyContinue) { throw 'Exit MOZA Cockpit before updating its preset library.' }
    $exe = Join-Path $ProjectRoot 'DCS.exe'
    if (Get-CimInstance Win32_Process -Filter "Name='DCS.exe'" | Where-Object { $_.ExecutablePath -eq $exe }) { throw 'Exit this project bridge before installing or uninstalling.' }
}
function Get-ModDirectory([string]$Game) {
    $nested = Join-Path $Game 'ue4ss'
    $rootDll = Test-Path -LiteralPath (Join-Path $Game 'UE4SS.dll')
    $nestedDll = Test-Path -LiteralPath (Join-Path $nested 'UE4SS.dll')
    if ($rootDll -and $nestedDll) { throw 'Two UE4SS installations found. Keep one active installation.' }
    if ($nestedDll -or (-not $rootDll -and (Test-Path -LiteralPath (Join-Path $nested 'Mods\PWTelemetry')))) { return Join-Path $nested 'Mods' }
    return Join-Path $Game 'Mods'
}
function Read-JsonFile([string]$Path) {
    if (Test-Path -LiteralPath $Path) { ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($Path)) }
}
function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Get-ModLists([string]$Mods, [bool]$Enabled) {
    $path = Join-Path $Mods 'mods.txt'
    $text = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) } else { '' }
    $text = [regex]::Replace($text, '(?im)^[ \t]*PWTelemetry[ \t]*:[ \t]*\d+[^\r\n]*(?:\r?\n|$)', '')
    if ($Enabled) { $text = $text.TrimEnd() + "`r`nPWTelemetry : 1`r`n" }
    $entries = @(Read-JsonFile (Join-Path $Mods 'mods.json') | ForEach-Object { $_ } | Where-Object { $_.mod_name -ne 'PWTelemetry' })
    if ($Enabled) { $entries += [pscustomobject]@{ mod_name = 'PWTelemetry'; mod_enabled = $true } }
    return @{ Text = $text; Json = (ConvertTo-Json -InputObject @($entries) -Depth 20) }
}
function Get-PresetState {
    $library = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MOZA Cockpit\PresetLibrary'
    $indexPath = Join-Path $library 'PresetIndex.index'
    $matchPath = Join-Path $library 'OptimalMatch.vconfig'
    return @{ Library = $library; IndexPath = $indexPath; MatchPath = $matchPath
        Index = (Read-JsonFile $indexPath); Match = (Read-JsonFile $matchPath) }
}
function Write-PresetState($State) {
    if ($State.Index) { Write-Utf8 $State.IndexPath ($State.Index | ConvertTo-Json -Depth 30) }
    if ($State.Match) { Write-Utf8 $State.MatchPath ($State.Match | ConvertTo-Json -Depth 30) }
}
