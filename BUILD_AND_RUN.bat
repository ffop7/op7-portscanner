@echo off
title op7 port scanner v2 — BUILD
color 0A
echo.
echo  ╔══════════════════════════════════════════════════╗
echo  ║       op7 port scanner v2 — BUILD SCRIPT        ║
echo  ╚══════════════════════════════════════════════════╝
echo.

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo  [ERROR] .NET SDK not found!
    echo  Install from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause & exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version') do set VER=%%v
echo  [OK] .NET SDK %VER% found
echo.
echo  Building op7 port scanner v2...
echo.

dotnet publish "%~dp0Op7PortScanner.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "%~dp0dist"

if %errorlevel% neq 0 (
    echo.
    echo  [ERROR] Build failed. See errors above.
    pause & exit /b 1
)

echo.
echo  ╔══════════════════════════════════════════════════╗
echo  ║              BUILD SUCCESSFUL!                   ║
echo  ║  dist\Op7PortScanner.exe is ready.               ║
echo  ╚══════════════════════════════════════════════════╝
echo.
timeout /t 2 >nul
start "" "%~dp0dist\Op7PortScanner.exe"
exit /b 0
