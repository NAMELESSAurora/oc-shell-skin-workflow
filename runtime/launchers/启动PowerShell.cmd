@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Open-PortableTerminal.ps1" -Kind PowerShell
if errorlevel 1 (pause & exit /b 1)
