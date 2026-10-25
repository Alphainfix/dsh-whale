$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $projectRoot 'dist\DSHWhale'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($name in @('DSHWhale.exe','whale.png','whale-windows.ico','whale-tray.ico','LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot "build\$name") -Destination $destination -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $destination -Force
Compress-Archive -LiteralPath $destination -DestinationPath (Join-Path $projectRoot 'dist\DSHWhale-windows.zip') -Force
Write-Output 'Packaged the Windows tray app.'
