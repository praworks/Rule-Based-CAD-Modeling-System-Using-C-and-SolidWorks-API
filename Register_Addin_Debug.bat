@echo off
setlocal
REM Run as Administrator
REM Ensure elevated privileges; relaunch elevated if needed
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
  echo Requesting administrative privileges...
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

REM Locate RegAsm (prefer 64-bit, fall back to 32-bit)
set "REGASM64=%windir%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set "REGASM32=%windir%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
if exist "%REGASM64%" (
  set "REGASM=%REGASM64%"
) else if exist "%REGASM32%" (
  set "REGASM=%REGASM32%"
) else (
  echo Could not find RegAsm in Framework folders.
  exit /b 1
)

set "DLL_PATH=%~dp0bin\Debug\net48\AI-CAD-December.dll"

if not exist "%REGASM%" (
  echo Could not find RegAsm at %REGASM%
  exit /b 1
)
if not exist "%DLL_PATH%" (
  echo Build output not found at "%DLL_PATH%"
  exit /b 1
)

echo Registering "%DLL_PATH%" with "%REGASM%"
"%REGASM%" "%DLL_PATH%" /codebase
if %ERRORLEVEL% NEQ 0 (
  echo Register failed with exit code %ERRORLEVEL%. Try running this script as Administrator.
  exit /b %ERRORLEVEL%
)

echo Registered AI-CAD-December add-in successfully.
endlocal
exit /b 0
