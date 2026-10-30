@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo        Piko Desktop Companion — Build & Package
echo ========================================================
echo.

cd /d "%~dp0"

echo [1/3] Running automated unit tests...
dotnet test Piko.Tests/Piko.Tests.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Unit tests failed! Aborting release packaging.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Publishing Piko Standalone Package...
if exist "release_bundle" rd /s /q "release_bundle"
mkdir release_bundle

dotnet publish Piko/Piko.csproj -c Release -r win-x64 --self-contained false -o release_bundle/Piko
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Dotnet publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Bundling Browser Extension and Documentation...
xcopy /E /I /Y "PikoBrowserExtension" "release_bundle\PikoBrowserExtension"
copy /Y "README.md" "release_bundle\README.md"
copy /Y "README.id.md" "release_bundle\README.id.md"
copy /Y "Piko\PikoLauncher.vbs" "release_bundle\Piko\PikoLauncher.vbs"

echo.
echo ========================================================
echo [SUCCESS] Release package created in:
echo           %~dp0release_bundle
echo ========================================================
echo.
pause
