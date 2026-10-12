@echo off
setlocal
title Stardew Valley for RGDSplus - Steam asset import
cd /d "%~dp0"
echo.
echo ============================================================
echo  Stardew Valley for RGDSplus - Steam asset import
echo ============================================================
echo.
if not exist "%~dp0Import Steam Assets.ps1" (
  echo ERROR: Import Steam Assets.ps1 is missing next to this file.
  echo Re-extract the complete Ports folder from the release ZIP.
  echo.
  pause
  exit /b 2
)
echo Paste your Steam Stardew Valley folder when asked, then press Enter.
echo It must be the folder containing Stardew Valley.dll and Content.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Import Steam Assets.ps1" -PromptSteamDir %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo Import failed with exit code %RC%.
  echo Existing game data and saves were not deleted.
  echo.
  pause
  exit /b %RC%
)
echo.
echo Import complete. You can close this window.
pause
