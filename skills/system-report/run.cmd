@echo off
chcp 65001 >nul
set "PY=%LOCALAPPDATA%\Programs\Python\Python313\python.exe"
if not exist "%PY%" set "PY=python"
"%PY%" "%~dp0system_report.py" %*
echo.
pause