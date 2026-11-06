@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install.ps1" %*
set "PW_INSTALL_RESULT=%ERRORLEVEL%"
pause
exit /b %PW_INSTALL_RESULT%
