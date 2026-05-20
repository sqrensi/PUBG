@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\start_local_stack.ps1"
if errorlevel 1 (
  echo.
  echo Failed to start local stack.
)
pause
