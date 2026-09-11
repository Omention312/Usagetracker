param()
$ws = New-Object -ComObject WScript.Shell
$ws.Popup("UsageTracker UI acceptance needs ~60s of exclusive input.`nPlease do NOT touch mouse/keyboard until you hear me resume.`n(If you already moved away, just ignore this and wait.)", 15, "UsageTracker - waiting for user", 64) | Out-Null
