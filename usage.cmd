@echo off
rem UsageTracker query helper (double-click or run: usage.cmd [today|top|recent|status])
chcp 65001 >nul
set "CLI=%~dp0release\UsageTracker.Cli.exe"
if "%~1"=="" (
  "%CLI%" status
  echo.
  echo Usage: usage.cmd today ^| top [days] ^| recent [n] ^| status ^| overview ^| report [days] ^| repair-spans [--apply] ^| autostart status
  echo Example: usage.cmd top 7
) else (
  "%CLI%" %*
)
echo.
pause
