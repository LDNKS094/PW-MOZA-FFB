[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$GameDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Resolve-GameDirectory $GameDirectory
if (-not $WhatIfPreference) { Assert-Closed }
$mods = Get-ModDirectory $game
$ue4ss = Join-Path (Split-Path -Parent $mods) 'UE4SS.dll'
foreach ($dependency in @((Join-Path $game 'dwmapi.dll'), $ue4ss)) {
    if (-not (Test-Path -LiteralPath $dependency -PathType Leaf)) { throw "Missing UE4SS dependency: $dependency. See README.md." }
}
$scriptSource = Join-Path $ProjectRoot 'mod\PWTelemetry\Scripts\main.lua'
$presetSource = Join-Path $ProjectRoot "presets\$PresetUuid.preset"
foreach ($source in @($scriptSource, $presetSource, (Join-Path $ProjectRoot 'DCS.exe'), (Join-Path $ProjectRoot 'payload\PWVibration.dll'))) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing package file: $source" }
}
$lists = Get-ModLists $mods $true
$state = Get-PresetState
if (-not (Test-Path -LiteralPath $state.Library -PathType Container)) { throw 'Run MOZA Cockpit once to create its PresetLibrary, then exit it before installing.' }
$preset = Read-JsonFile $presetSource
if (-not $state.Index) { $state.Index = [pscustomobject]@{ presets = @() } }
if (-not $state.Match) { $state.Match = [pscustomobject]@{ Base = [pscustomobject]@{}; version = '1.0.4' } }
foreach ($device in $preset.devices) {
    foreach ($presetGame in $preset.games) {
        $parent = $state.Match
        foreach ($level in @($preset.device_type, $device, $presetGame)) {
            if (-not $parent.PSObject.Properties[$level]) { $parent | Add-Member -MemberType NoteProperty -Name $level -Value ([pscustomobject]@{}) }
            $parent = $parent.$level
        }
        $parent | Add-Member -MemberType NoteProperty -Name $preset.vehicles -Value $PresetUuid -Force
    }
}
$destination = Join-Path $state.Library "$PresetUuid.preset"
$entry = [pscustomobject]@{ date = $preset.date; device_type = $preset.device_type; devices = @($preset.devices)
    file_path = $destination.Replace('\', '/'); games = @($preset.games); name = $preset.name
    product_type = $preset.product_type; uuid = $PresetUuid; vehicles = $preset.vehicles; version = $preset.version }
$state.Index.presets = @($state.Index.presets | Where-Object { $_.uuid -ne $PresetUuid }) + @($entry)
if (-not $PSCmdlet.ShouldProcess("$game; $($state.Library)", 'Install PWTelemetry and overwrite the PW Default Cockpit preset')) { return }
$target = Join-Path $mods 'PWTelemetry\Scripts'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath $scriptSource -Destination (Join-Path $target 'main.lua') -Force
Write-Utf8 (Join-Path $mods 'mods.txt') $lists.Text
Write-Utf8 (Join-Path $mods 'mods.json') $lists.Json
Copy-Item -LiteralPath $presetSource -Destination $destination -Force
Write-PresetState $state
Write-Output "Installed PWTelemetry: $mods"
Write-Output "Installed PW Default: $destination"
Write-Output 'Open Cockpit, run DCS.exe, then start Project Wingman.'
