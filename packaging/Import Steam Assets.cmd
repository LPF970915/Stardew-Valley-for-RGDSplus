@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Import Steam Assets.ps1" %*
if errorlevel 1 (
  echo Import failed. Existing game data and saves were not deleted.
  pause
  exit /b 1
)
echo Import complete.
pause
