# M3 acceptance automation (design v2 section 7: automatable subset)
#   T2  lifecycle close-loop          (engine --selftest child start/stop)
#   T4  force-kill crash -> restart reconcile (checked via status exit=2 count)
#   T1  foreground precision          AUTO when desktop is idle; else SKIP.
#        Run verify/t1-idle.ps1 on a quiet desktop for the precision check.
# Usage: powershell -ExecutionPolicy Bypass -File acceptance.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root 'release'
$engine = Join-Path $release 'UsageTracker.Engine.exe'
$cli = Join-Path $release 'UsageTracker.Cli.exe'

function CleanDir($dir) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }

function Verdict($name, $verdict) {
    Write-Host ("{0,-40} {1}" -f $name, $verdict)
    return $verdict
}

# ---------- T2 lifecycle ----------
Write-Host '== T2: lifecycle self-test (child start/stop) =='
$env:USAGETRACKER_DATA_DIR = Join-Path $root '.runtime-accept-t2'
$env:USAGETRACKER_MAINT_DELAY_MS = '999999999'
CleanDir $env:USAGETRACKER_DATA_DIR
$p = Start-Process -FilePath $engine -ArgumentList '--selftest','25' -Wait -PassThru
$t2 = Verdict 'T2 lifecycle (selftest exit)' $(if ($p.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })

# ---------- T4 force-kill -> restart reconcile ----------
Write-Host '== T4: force-kill crash -> restart reconcile =='
$env:USAGETRACKER_DATA_DIR = Join-Path $root '.runtime-accept-t4'
CleanDir $env:USAGETRACKER_DATA_DIR
$k = Start-Process -FilePath $engine -ArgumentList '--headless','12' -PassThru
Start-Sleep -Seconds 6
Stop-Process -Id $k.Id -Force          # simulate crash
Start-Sleep -Seconds 2
$null = Start-Process -FilePath $engine -ArgumentList '--headless','8' -Wait -PassThru
$status = & $cli status
Write-Host $status
$m = [regex]::Match($status, 'reconciled\(exit=2\):\s*(\d+)')
$exit2 = if ($m.Success) { [int]$m.Groups[1].Value } else { -1 }
$t4a = Verdict 'T4 restart reconcile (>0)' $(if ($exit2 -gt 0) { 'PASS' } else { 'FAIL' })
$t4b = Verdict 'T4 integrity=ok' $(if ($status -match 'integrity  : ok') { 'PASS' } else { 'FAIL' })

# ---------- T1 foreground precision (idle-desktop dependent) ----------
Write-Host '== T1: foreground precision (engine 70s + probe window 60s) =='
Write-Host 'NOTE: probe window must win foreground. If you are actively using the PC'
Write-Host '      this run will SKIP (not FAIL). For a real check, close apps / stop using'
Write-Host "      the PC for ~80s, then run: powershell -ExecutionPolicy Bypass -File verify\t1-idle.ps1"
$env:USAGETRACKER_DATA_DIR = Join-Path $root '.runtime-accept-t1'
CleanDir $env:USAGETRACKER_DATA_DIR
$eng = Start-Process -FilePath $engine -ArgumentList '--headless','70' -PassThru
Start-Sleep -Seconds 8
$probeStart = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$probe = Start-Process -FilePath $engine -ArgumentList '--probe-window','60000' -PassThru
$eng.WaitForExit()
if (-not $probe.WaitForExit(5000)) { Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue }
$check = & $cli accept-check 'UsageTracker.Engine' $probeStart 60000
Write-Host $check
if ($check -match 'verdict=PASS') {
    $t1 = Verdict 'T1 foreground precision' 'PASS'
} elseif ($check -match 'verdict=ZERO') {
    $t1 = Verdict 'T1 foreground precision' 'SKIP (desktop busy; run t1-idle.ps1)'
} else {
    $t1 = Verdict 'T1 foreground precision' 'FAIL'
}

Remove-Item Env:USAGETRACKER_DATA_DIR -ErrorAction SilentlyContinue
Remove-Item Env:USAGETRACKER_MAINT_DELAY_MS -ErrorAction SilentlyContinue

Write-Host ''
$allPass = ($t2 -eq 'PASS') -and ($t4a -eq 'PASS') -and ($t4b -eq 'PASS')
Write-Host ('SUMMARY: ' + $(if ($allPass) { 'T2/T4 ALL PASS (T1: ' + $t1 + ')' } else { 'HAS FAIL' }))
exit $(if ($allPass) { 0 } else { 1 })
