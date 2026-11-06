@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Uninstall.ps1" %*
set "PW_UNINSTALL_RESULT=%ERRORLEVEL%"
pause
exit /b %PW_UNINSTALL_RESULT%
