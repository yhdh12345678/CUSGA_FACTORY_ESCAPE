@echo off
where pwsh.exe >nul 2>nul
if %errorlevel%==0 (
  pwsh.exe -NoLogo -NoProfile -File "%~dp0Tools\AccessibilityPreviewer\Start-Preview.ps1"
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\AccessibilityPreviewer\Start-Preview.ps1"
)
if not %errorlevel%==0 pause
