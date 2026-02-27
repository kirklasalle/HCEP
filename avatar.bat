@echo off
title HCEP - True Gaze Avatar
cd /d "%~dp0"

echo ========================================
echo  HCEP - True Gaze Avatar
echo  Copyright (c) 2026 Kirk LaSalle
echo ========================================
echo.

echo [*] Building solution...
dotnet build HCEP.sln --nologo -v q
if errorlevel 1 (
    echo.
    echo [!] Build FAILED. Fix errors above and try again.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.
echo [*] Clearing logs...
del /q "%LOCALAPPDATA%\HCEP\Logs\*" 2>nul
echo [*] Launching Avatar window...
echo.
dotnet run --project src/HCEP.App -- --window avatar
if errorlevel 1 (
    echo.
    echo [!] App exited with error code %errorlevel%.
    pause
)
exit /b 0
