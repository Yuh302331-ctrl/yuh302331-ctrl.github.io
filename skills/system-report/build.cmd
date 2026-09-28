@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Cannot find csc.exe - .NET Framework 4.x is required.
  pause
  exit /b 1
)
echo Building SysReport.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /win32icon:"%~dp0app.ico" ^
  /win32manifest:"%~dp0app.manifest" ^
  /out:"%~dp0SysReport.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "%~dp0SysReport.cs"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
) else (
  echo.
  echo BUILD OK -^> %~dp0SysReport.exe
)
pause