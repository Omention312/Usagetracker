# T1 foreground precision check (run on an IDLE desktop - do not touch the PC!)
# Duration ~80s. A probe window steals foreground; engine must record it accurately.
# Usage: powershell -ExecutionPolicy Bypass -File verify\t1-idle.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'release'
$engine = Join-Path $release 'UsageTracker.Engine.exe'
$cli = Join-Path $release 'UsageTracker.Cli.exe'
$env:USAGETRACKER_DATA_DIR = Join-Path $root '.runtime-t1'
$env:USAGETRACKER_MAINT_DELAY_MS = '999999999'
if (Test-Path $env:USAGETRACKER_DATA_DIR) { Remove-Item $env:USAGETRACKER_DATA_DIR -Recurse -Force }

Write-Host 'T1 precision check: please DO NOT touch the mouse/keyboard for the next ~90s.'
Write-Host 'A probe window will appear and stay in the foreground. Starting in 6s ...'
Start-Sleep -Seconds 6

$eng = Start-Process -FilePath $engine -ArgumentList '--headless','85' -PassThru
Start-Sleep -Seconds 8
$probeStart = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$probe = Start-Process -FilePath $engine -ArgumentList '--probe-window','60000' -PassThru
$eng.WaitForExit()
if (-not $probe.WaitForExit(5000)) { Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue }
$check = & $cli accept-check 'UsageTracker.Engine' $probeStart 60000
Write-Host $check
Remove-Item Env:USAGETRACKER_DATA_DIR -ErrorAction SilentlyContinue
Remove-Item Env:USAGETRACKER_MAINT_DELAY_MS -ErrorAction SilentlyContinue
if ($check -match 'PASS') { Write-Host 'T1 PASS'; exit 0 } else { Write-Host 'T1 FAIL'; exit 1 }
