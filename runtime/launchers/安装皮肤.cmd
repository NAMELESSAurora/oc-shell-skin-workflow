@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-CharacterSkins.ps1"
if errorlevel 1 (pause & exit /b 1)
pause
