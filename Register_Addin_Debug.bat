@echo off
setlocal
REM Run as Administrator
set REGASM64="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
set DLL_PATH="%~dp0bin\Debug\SolidWorks.TaskpaneCalculator.dll"

if not exist %REGASM64% (
  echo Could not find RegAsm at %REGASM64%
  exit /b 1
)
if not exist %DLL_PATH% (
  echo Build output not found at %DLL_PATH%
  echo Please build the project in Debug first.
  exit /b 1
)

%REGASM64% %DLL_PATH% /codebase
if %ERRORLEVEL% NEQ 0 (
  echo Registration failed. Try running this script as Administrator.
  exit /b %ERRORLEVEL%
)

echo Registered add-in successfully.
endlocal
