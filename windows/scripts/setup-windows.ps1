$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'DSHWhale-Vibe.exe'
if (-not (Test-Path -LiteralPath $executable)) { $executable = Join-Path $projectRoot 'build\DSHWhale-Vibe.exe' }
[void][Reflection.Assembly]::LoadFrom($executable)
$launch = [DshWhale.Discovery]::Find((New-Object DshWhale.Settings))
$moduleRoot = Split-Path -Parent $launch.Entry
while ($moduleRoot -and -not (Test-Path -LiteralPath (Join-Path $moduleRoot 'yaml\package.json'))) { $moduleRoot = Split-Path -Parent $moduleRoot }
if (-not $moduleRoot) { throw 'Cannot find the installed DSH dependencies.' }
& $launch.Node (Join-Path $PSScriptRoot 'enable-chatgpt.cjs') $moduleRoot
if ($LASTEXITCODE -ne 0) { throw 'ChatGPT route setup failed.' }
& $launch.Node (Join-Path $PSScriptRoot 'install-companion-plugin.cjs') $moduleRoot
if ($LASTEXITCODE -ne 0) { throw 'Companion plugin setup failed.' }
Write-Output 'DSH profile configured. Open the Whale ChatGPT login page to authorize your account.'
