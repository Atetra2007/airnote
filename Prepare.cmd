@echo off
setlocal
cd /d "%~dp0"
echo Download pinned originals and verify checksums. This does not start the player.
powershell.exe -NoLogo -NoProfile -File "%~dp0scripts\Prepare.ps1"
if errorlevel 1 (
  echo Preparation failed. See the message above and README.md for manual downloads.
  pause
  exit /b 1
)
echo Done. Open index.html for the song catalogue and instructions.
pause
