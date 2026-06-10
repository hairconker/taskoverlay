$ErrorActionPreference = 'SilentlyContinue'

$processes = Get-Process -Name 'TaskOverlay.App' -ErrorAction SilentlyContinue
if (-not $processes) {
    Write-Host 'TaskOverlay.App is not running.'
    exit 0
}

foreach ($process in $processes) {
    Write-Host "Stopping TaskOverlay.App PID $($process.Id)..."
    Stop-Process -Id $process.Id -Force
}

Start-Sleep -Milliseconds 300
$remaining = Get-Process -Name 'TaskOverlay.App' -ErrorAction SilentlyContinue
if ($remaining) {
    Write-Error 'TaskOverlay.App is still running. Try running this script as Administrator.'
    exit 1
}

Write-Host 'TaskOverlay.App stopped.'
