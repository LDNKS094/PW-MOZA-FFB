param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = $projectRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$outputExe = Join-Path $OutputDirectory 'DCS.exe'
$running = Get-CimInstance Win32_Process -Filter "Name='DCS.exe'" | Where-Object { $_.ExecutablePath -eq $outputExe }
if ($running) { throw 'Exit this project''s DCS.exe before building into its directory.' }
$nativeBuild = Join-Path $projectRoot 'development/build/vibration'
$managedBuild = Join-Path $projectRoot 'development/build/bridge'
$publishDirectory = Join-Path $projectRoot 'development/artifacts/bridge'
& cmake -S (Join-Path $projectRoot 'src/vibration') -B $nativeBuild -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'Native configure failed.' }
& cmake --build $nativeBuild --config Release
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$vibrationDll = Join-Path $nativeBuild 'Release/PWVibration.dll'
& dotnet publish (Join-Path $projectRoot 'src/bridge/PWFeedback.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true "-p:BaseIntermediateOutputPath=$managedBuild/obj/" "-p:BaseOutputPath=$managedBuild/bin/" -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
$payload = Join-Path $OutputDirectory 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath $vibrationDll -Destination (Join-Path $payload 'PWVibration.dll') -Force
Copy-Item -LiteralPath (Join-Path $publishDirectory 'DCS.exe') -Destination $outputExe -Force
Write-Output "Built: $outputExe"
Write-Output "Built: $(Join-Path $payload 'PWVibration.dll')"
