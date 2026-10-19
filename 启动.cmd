@echo off
cd /d "%~dp0"
if not exist "%~dp0WhaleWidget.exe" (
  echo Build first: powershell -NoProfile -ExecutionPolicy Bypass -File .\编译.ps1
  pause
  exit /b 1
)
start "" "%~dp0WhaleWidget.exe"
