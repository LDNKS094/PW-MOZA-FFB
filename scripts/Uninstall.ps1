[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$GameDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$game = Resolve-GameDirectory $GameDirectory
if (-not $WhatIfPreference) { Assert-Closed }
$mods = Get-ModDirectory $game
$lists = Get-ModLists $mods $false
$state = Get-PresetState
if ($state.Index) { $state.Index.presets = @($state.Index.presets | Where-Object { $_.uuid -ne $PresetUuid }) }
$mapping = if ($state.Match -and $state.Match.Base -and $state.Match.Base.AB6) { $state.Match.Base.AB6.dcs_world } else { $null }
if ($mapping -and $mapping.PSObject.Properties['PW Default'] -and $mapping.'PW Default' -eq $PresetUuid) { $mapping.PSObject.Properties.Remove('PW Default') }
if (-not $PSCmdlet.ShouldProcess("$game; $($state.Library)", 'Remove PWVibration, PWTelemetry and this project Cockpit preset')) { return }
$module = Join-Path $game 'PWVibration.dll'
if (Test-Path -LiteralPath $module) { Remove-Item -LiteralPath $module }
$telemetry = [IO.Path]::GetFullPath((Join-Path $mods 'PWTelemetry'))
if (-not $telemetry.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Telemetry path is outside the selected game directory.' }
if (Test-Path -LiteralPath $telemetry) { Remove-Item -LiteralPath $telemetry -Recurse -Force }
if (Test-Path -LiteralPath $mods) {
    $textPath = Join-Path $mods 'mods.txt'
    $jsonPath = Join-Path $mods 'mods.json'
    if (Test-Path -LiteralPath $textPath) { Write-Utf8 $textPath $lists.Text }
    if (Test-Path -LiteralPath $jsonPath) { Write-Utf8 $jsonPath $lists.Json }
}
$presetPath = Join-Path $state.Library "$PresetUuid.preset"
if (Test-Path -LiteralPath $presetPath) { Remove-Item -LiteralPath $presetPath }
Write-PresetState $state
Write-Output 'Removed PWVibration, PWTelemetry and PW Default.'
