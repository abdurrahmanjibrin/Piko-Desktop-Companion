@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo       Piko Desktop Companion — Setup Installer Builder
echo ========================================================
echo.

cd /d "%~dp0"

:: 1. Ensure dotnet is in PATH
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK not found! Please install .NET 8 SDK or check DOTNET_ROOT.
    pause
    exit /b 1
)

:: 2. Find Inno Setup Compiler (ISCC.exe)
set "ISCC_PATH="
where iscc >nul 2>&1
if %ERRORLEVEL% equ 0 (
    for /f "delims=" %%I in ('where iscc') do set "ISCC_PATH=%%I"
)
if not defined ISCC_PATH if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC_PATH if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC_PATH if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not defined ISCC_PATH (
    echo [ERROR] Inno Setup 6 compiler ISCC.exe was not found!
    echo Please install Inno Setup: winget install JRSoftware.InnoSetup
    pause
    exit /b 1
)

echo [OK] Using Inno Setup: "%ISCC_PATH%"
echo.

:: 3. Run unit tests
echo [1/4] Running automated unit tests...
dotnet test Piko.Tests/Piko.Tests.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Unit tests failed! Aborting build.
    pause
    exit /b %ERRORLEVEL%
)

:: 4. Publish Self-Contained Release
echo.
echo [2/4] Publishing Piko (Self-Contained win-x64)...
if exist "release_bundle" rd /s /q "release_bundle"
mkdir release_bundle

dotnet publish Piko/Piko.csproj -c Release -r win-x64 --self-contained true -o release_bundle/Piko
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Dotnet publish failed!
    pause
    exit /b %ERRORLEVEL%
)

:: 5. Bundle Extension and Docs
echo.
echo [3/4] Bundling documentation and browser extension...
xcopy /E /I /Y "PikoBrowserExtension" "release_bundle\PikoBrowserExtension" >nul
copy /Y "README.md" "release_bundle\README.md" >nul
copy /Y "README.id.md" "release_bundle\README.id.md" >nul

:: 6. Compile Installer with Inno Setup
echo.
echo [4/4] Compiling Windows Installer (Piko-Setup.exe)...
if not exist "dist" mkdir dist

"%ISCC_PATH%" piko_setup.iss
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Installer compilation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo [SUCCESS] Official Windows Installer created successfully!
echo Installer location:
dir /b dist\*.exe
echo.
echo Full path: %~dp0dist
echo ========================================================
echo.
pause
