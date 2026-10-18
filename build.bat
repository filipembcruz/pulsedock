@echo off
setlocal
cd /d "%~dp0"
set "INSTALL=%LOCALAPPDATA%\Programs\PulseDock"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo  .NET 8 SDK not found.
  echo  Install it with:  winget install Microsoft.DotNet.SDK.8
  echo  or download from: https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  pause
  exit /b 1
)

rem Close PulseDock if it is running (otherwise the .exe is locked)
taskkill /im PulseDock.exe /f >nul 2>nul
timeout /t 1 /nobreak >nul

echo Building PulseDock...
dotnet publish src\PulseDock\PulseDock.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -o dist
if errorlevel 1 (
  echo.
  echo  Build failed. See the messages above.
  pause
  exit /b 1
)

echo Installing to %INSTALL% ...
if not exist "%INSTALL%" mkdir "%INSTALL%"
copy /y "dist\PulseDock.exe" "%INSTALL%\PulseDock.exe" >nul
if errorlevel 1 (
  echo  Could not copy to %INSTALL%. Use dist\PulseDock.exe instead.
  pause
  exit /b 1
)

echo.
echo  Done! Installed to: %INSTALL%\PulseDock.exe
echo  A "PulseDock" shortcut is created in the Start menu and on the desktop.
echo.
if /i not "%1"=="--no-run" start "" "%INSTALL%\PulseDock.exe"
endlocal
