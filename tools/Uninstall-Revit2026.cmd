@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DESINSTALAR-Revit2026.ps1"
if errorlevel 1 (
  echo.
  echo A desinstalacao nao foi concluida. Revise a mensagem acima.
)
echo.
pause
