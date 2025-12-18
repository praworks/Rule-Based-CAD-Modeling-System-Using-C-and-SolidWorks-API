@echo off
setlocal
rmdir /s /q "%~dp0bin" 2>nul
rmdir /s /q "%~dp0obj" 2>nul
rmdir /s /q "%~dp0.vs" 2>nul
echo Cleaned bin/ obj/ .vs/
endlocal
