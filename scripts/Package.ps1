param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = [xml](Get-Content -LiteralPath (Join-Path $root 'src\bridge\PWFeedback.csproj') -Raw)
$version = $project.Project.PropertyGroup.Version
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'releases' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$zip = Join-Path $OutputDirectory "PW-MOZA-FFB-$version.zip"
if (Test-Path -LiteralPath $zip) { throw "Package already exists: $zip" }
$files = @('DCS.exe', 'payload\PWVibration.dll', 'mod\PWTelemetry\Scripts\main.lua',
    'presets\{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset', 'licenses\MinHook-LICENSE.txt',
    'Install.cmd', 'Uninstall.cmd', 'README.md', 'README_zh.md', 'LICENSE',
    'scripts\Common.ps1', 'scripts\Install.ps1', 'scripts\Uninstall.ps1')
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'development\artifacts'))
$stage = Join-Path $artifacts ('package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage, $OutputDirectory -Force | Out-Null
try {
    foreach ($file in $files) {
        $target = Join-Path $stage $file
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    $digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", $digest + '  ' + (Split-Path -Leaf $zip) + "`n", [Text.Encoding]::ASCII)
    Write-Output "Packaged: $zip"
} finally {
    if (-not ([IO.Path]::GetFullPath($stage)).StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package stage is outside the artifact directory.' }
    Remove-Item -LiteralPath $stage -Recurse -Force
}
