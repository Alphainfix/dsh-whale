$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4 compiler not found.' }
$output = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @((Join-Path $projectRoot 'src\Core.cs'), (Join-Path $projectRoot 'src\App.cs'), (Join-Path $projectRoot 'src\Lifecycle.cs'), (Join-Path $projectRoot 'src\MacStyle.cs'), (Join-Path $projectRoot 'src\AssemblyInfo.cs'))
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/win32icon:$projectRoot\packaging\whale-windows.ico" "/win32manifest:$projectRoot\src\app.manifest" "/out:$output\DSHWhale.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\whale-glyph@2x.png') -Destination (Join-Path $output 'whale.png')
Copy-Item -LiteralPath (Join-Path $projectRoot '..\LICENSE') -Destination (Join-Path $output 'LICENSE')
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\whale-windows.ico') -Destination (Join-Path $output 'whale-windows.ico')
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\whale-tray.ico') -Destination (Join-Path $output 'whale-tray.ico')
Write-Output "Built: $output\DSHWhale.exe"
