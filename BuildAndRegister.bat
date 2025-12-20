@echo off
REM ===========================================================================
REM   BUILD, REGISTER, AND RELAUNCH SOLIDWORKS (FIXED VERSION)
REM ===========================================================================

REM --- 1. ADMIN PRIVILEGES CHECK ---
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process cmd.exe -ArgumentList '/k','\"\"%~f0\"\"','/elevated' -WorkingDirectory '%cd%' -Verb RunAs -WindowStyle Normal" -Wait
    exit /b
)
if "%~1"=="/elevated" shift

REM --- 2. SET PROJECT DIRECTORY ---
REM First try the directory where this script is located
cd /d "%~dp0"

REM If sln not found here, force the specific path
if not exist "AI-CAD-December.sln" (
    if exist "d:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API" (
        cd /d "d:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API"
    )
)

REM --- 3. CLOSE SOLIDWORKS ---
echo.
echo [STEP 1] Checking for running SOLIDWORKS instance...
tasklist /FI "IMAGENAME eq SLDWORKS.EXE" | find /I "SLDWORKS.EXE" >nul
if %ERRORLEVEL% EQU 0 (
    echo Found running instance. Closing gracefully...
    taskkill /IM SLDWORKS.EXE /T >nul 2>&1
    timeout /t 3 /nobreak >nul
    
    REM Double check if it's still there
    tasklist /FI "IMAGENAME eq SLDWORKS.EXE" | find /I "SLDWORKS.EXE" >nul
    if %ERRORLEVEL% EQU 0 (
        echo Still running. Forcing close...
        taskkill /IM SLDWORKS.EXE /T /F >nul 2>&1
        timeout /t 2 /nobreak >nul
    )
) else (
    echo SolidWorks is not running.
)

REM --- 4. FIND MSBUILD ---
echo.
echo [STEP 2] Locating MSBuild...
set "MSBUILD="

REM Method A: Check standard VS2022 path
if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)

REM Method B: Use vswhere (Works for Pro/Enterprise/Community)
if not defined MSBUILD (
    if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
        for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
            set "MSBUILD=%%i"
        )
    )
)

if not defined MSBUILD (
    color 4F
    echo.
    echo [ERROR] MSBuild not found!
    echo Please ensure Visual Studio Build Tools are installed.
    echo.
    pause
    exit /b
)

echo Found MSBuild: "%MSBUILD%"

REM --- 5. BUILD SOLUTION ---
echo.
REM FIX: Replaced pipe '|' with dash '-' to prevent crash
echo [STEP 3] Building AI-CAD-December.sln (Debug - Any CPU)... 
echo.

"%MSBUILD%" "AI-CAD-December.sln" /p:Configuration=Debug "/p:Platform=Any CPU" /t:Rebuild

if %ERRORLEVEL% NEQ 0 (
    color 4F
    echo.
    echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    echo          BUILD FAILED
    echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    echo.
    pause
    exit /b
)

echo.
echo Build Successful!

REM --- 6. REGISTER ADD-IN ---
echo.
echo [STEP 4] Registering COM Add-in...

if exist "Register_Addin_Debug.bat" (
    REM FIX: Uses cmd /c to prevent Register script from killing this window
    cmd /c call "Register_Addin_Debug.bat"
    REM Note: RegAsm returns non-zero even on success (warning about unsigned assembly)
    REM So we ignore the exit code and proceed to launch
) else (
    echo [WARNING] Register_Addin_Debug.bat not found. Skipping...
)

echo Registration completed. Proceeding to launch SOLIDWORKS...

REM --- 7. RELAUNCH SOLIDWORKS ---
echo.
echo [STEP 5] Relaunching SOLIDWORKS...

set "SW_EXE="

REM Try direct path first (most reliable)
if exist "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe" set "SW_EXE=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe"

REM Attempt to find SW via Registry
if not defined SW_EXE (
    for /f "tokens=2*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe" /ve 2^>nul') do set "SW_EXE=%%B"
)

REM Additional fallback checks
if not defined SW_EXE if exist "%ProgramFiles%\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe" set "SW_EXE=%ProgramFiles%\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe"
if not defined SW_EXE if exist "%ProgramFiles(x86)%\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe" set "SW_EXE=%ProgramFiles(x86)%\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe"

echo Resolved path: "%SW_EXE%"

if defined SW_EXE (
    if exist "%SW_EXE%" (
        echo Launching SOLIDWORKS in 3 seconds...
        timeout /t 3 /nobreak >nul
        start "" "%SW_EXE%"
        timeout /t 2 /nobreak >nul
        echo SOLIDWORKS launched successfully!
    ) else (
        echo [ERROR] Path was set but file does not exist: "%SW_EXE%"
        goto :sw_fail
    )
) else (
    :sw_fail
    color 6F
    echo.
    echo [ERROR] Could not locate SOLIDWORKS executable.
    echo Please manually open SOLIDWORKS from: C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe
    echo.
)

echo.
color 0A
echo ========================================
echo        DONE - Process Complete
echo ========================================
echo.
pause