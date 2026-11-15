@echo off
setlocal
title Phrasor uninstall
set "DEST=%LOCALAPPDATA%\Programs\Phrasor"
cd /d "%TEMP%"

echo Removing Phrasor...
taskkill /IM Phrasor.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Phrasor /f >nul 2>&1
del /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Phrasor.lnk" >nul 2>&1

choice /C YN /M "Also delete your Phrasor settings, saved API keys and logs"
if errorlevel 2 goto keepdata
rmdir /S /Q "%APPDATA%\Phrasor" >nul 2>&1
echo Settings, keys and logs deleted.
:keepdata

echo Phrasor removed.
pause
rem This script may live inside the folder it deletes, so remove the folder from a separate process.
start "" /min cmd /c "timeout /t 2 /nobreak >nul & rmdir /S /Q "%DEST%""
exit /b 0
