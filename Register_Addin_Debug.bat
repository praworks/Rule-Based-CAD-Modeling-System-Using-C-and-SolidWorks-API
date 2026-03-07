@echo off
setlocal
REM Run as Administrator
REM Ensure elevated privileges; relaunch elevated if needed
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
  echo Requesting administrative privileges...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -Wait"
  if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%
  exit /b 0
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
set "CURRENT_GUID={D5B8E2F9-2F3E-4D44-907F-2B983D32AF37}"
set "ADDIN_TITLE=AI-CAD-December"
set "ADDIN_DESC=AI-CAD SolidWorks add-in"

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
REM Create SolidWorks Addin registry entries so SolidWorks will load the add-in
echo Creating SolidWorks registry keys for add-in registration...
reg add "HKLM\SOFTWARE\SolidWorks\Addins\%CURRENT_GUID%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\SolidWorks\Addins\%CURRENT_GUID%" /ve /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKLM\SOFTWARE\SolidWorks\Addins\%CURRENT_GUID%" /v "Title" /t REG_SZ /d "%ADDIN_TITLE%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\SolidWorks\Addins\%CURRENT_GUID%" /v "Description" /t REG_SZ /d "%ADDIN_DESC%" /f >nul 2>&1
reg add "HKLM\SOFTWARE\SolidWorks\Addins\%CURRENT_GUID%" /v "LoadAtStartup" /t REG_DWORD /d 1 /f >nul 2>&1

REM Ensure per-user startup entry exists (makes the add-in load for current user)
reg add "HKCU\Software\SolidWorks\AddInsStartup\%CURRENT_GUID%" /ve /t REG_DWORD /d 1 /f >nul 2>&1

echo Registered AI-CAD-December add-in and created SolidWorks registry entries.
endlocal
exit /b 0
