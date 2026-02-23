@echo off
title HCEP - Human Communication Eye Protocol
cd /d "%~dp0"

echo ========================================
echo  HCEP - Human Communication Eye Protocol
echo  Copyright (c) 2026 Kirk LaSalle
echo ========================================
echo.

if "%1"=="test" goto :test
if "%1"=="build" goto :build
if "%1"=="clean" goto :clean
if "%1"=="rebuild" goto :rebuild
if "%1"=="" goto :run

echo Unknown command: %1
echo.
echo Usage: run.bat [command]
echo.
echo   (none)    Build and run the app
echo   test      Build and run all tests
echo   build     Build only (no run)
echo   clean     Clean all build outputs
echo   rebuild   Clean, build, and run
echo.
exit /b 1

:run
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
echo [*] Launching HCEP...
echo.
dotnet run --project src/HCEP.App
if errorlevel 1 (
    echo.
    echo [!] App exited with error code %errorlevel%.
    pause
)
exit /b 0

:test
echo [*] Building solution...
dotnet build HCEP.sln --nologo -v q
if errorlevel 1 (
    echo.
    echo [!] Build FAILED.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.
echo [*] Running tests...
echo.
dotnet test HCEP.sln --no-build --nologo --verbosity normal
echo.
pause
exit /b 0

:build
echo [*] Building solution...
dotnet build HCEP.sln --nologo
if errorlevel 1 (
    echo.
    echo [!] Build FAILED.
) else (
    echo.
    echo [OK] Build succeeded.
)
pause
exit /b 0

:clean
echo [*] Cleaning solution...
dotnet clean HCEP.sln --nologo -v q
echo [OK] Clean complete.
pause
exit /b 0

:rebuild
echo [*] Cleaning solution...
dotnet clean HCEP.sln --nologo -v q
echo.
echo [*] Building solution...
dotnet build HCEP.sln --nologo -v q
if errorlevel 1 (
    echo.
    echo [!] Build FAILED.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.
echo [*] Clearing logs...
del /q "%LOCALAPPDATA%\HCEP\Logs\*" 2>nul
echo [*] Launching HCEP...
echo.
dotnet run --project src/HCEP.App
if errorlevel 1 (
    echo.
    echo [!] App exited with error code %errorlevel%.
    pause
)
exit /b 0
