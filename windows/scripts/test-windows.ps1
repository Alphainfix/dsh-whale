$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build-windows.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$core = Join-Path $projectRoot 'src\Core.cs'
foreach ($entry in @(@{source='Tests.cs';output='Tests.exe'}, @{source='Launcher.cs';output='TestLauncher.exe'})) {
    & $compiler /nologo /utf8output /target:exe "/out:$projectRoot\build\$($entry.output)" /r:System.Web.Extensions.dll /r:System.Management.dll $core (Join-Path $projectRoot "tests\$($entry.source)")
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
}
& (Join-Path $projectRoot 'build\Tests.exe') $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
