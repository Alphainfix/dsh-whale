$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'build\DSHWhale-Vibe.exe'
$parent = Start-Process -FilePath $exe -ArgumentList '--no-start' -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 3
    $worker = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe -and $_.ParentProcessId -eq $parent.Id -and $_.CommandLine -match '--worker' }
    if (@($worker).Count -ne 1) { throw 'Expected one tray worker.' }
    $workerId = $worker.ProcessId
    Write-Output "PASS: hidden tray worker started (PID $workerId)."
    Start-Sleep -Seconds 25
    if (-not (Get-Process -Id $workerId -ErrorAction SilentlyContinue)) { throw 'Tray worker exited unexpectedly.' }
    Start-Sleep -Seconds 25
    if (-not (Get-Process -Id $workerId -ErrorAction SilentlyContinue)) { throw 'Tray worker failed repeated refresh cycles.' }
    Write-Output 'PASS: same tray process survives multiple 20-second refresh cycles.'
    Stop-Process -Id $workerId -Force
    Start-Sleep -Seconds 6
    $replacement = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe -and $_.ParentProcessId -eq $parent.Id -and $_.CommandLine -match '--worker' }
    if (@($replacement).Count -ne 1 -or $replacement.ProcessId -eq $workerId) { throw 'Crash recovery failed.' }
    Write-Output 'PASS: abnormal tray exit recovered automatically.'
    Start-Process -FilePath $exe -ArgumentList '--shutdown' -WindowStyle Hidden -Wait
    if (-not $parent.WaitForExit(8000)) { throw 'Clean shutdown did not stop supervisor.' }
    if ($parent.ExitCode -ne 0) { throw 'Clean shutdown returned an error.' }
    Write-Output 'PASS: clean shutdown exits worker and supervisor without relaunch.'
} finally {
    Start-Process -FilePath $exe -ArgumentList '--shutdown' -WindowStyle Hidden -Wait
    if (-not $parent.HasExited) { $parent.WaitForExit(3000) | Out-Null }
}
