@echo off
rem Phrasor installs itself: this just runs Phrasor.exe, which offers to install or update.
set "SRC=%~dp0Phrasor.exe"
if not exist "%SRC%" set "SRC=%~dp0..\dist\Phrasor.exe"
if not exist "%SRC%" (
  echo Phrasor.exe not found next to this script or in ..\dist.
  pause
  exit /b 1
)
start "" "%SRC%"
