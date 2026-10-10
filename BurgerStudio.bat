@echo off
rem Opens Burger Studio, the BurgerEngine editor (builds the engine the first time).
cd /d "%~dp0"
if not exist "src\Burger.Studio\bin\Release\net8.0\BurgerStudio.exe" (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo The .NET SDK is not installed. Download it from https://dotnet.microsoft.com/download ^(version 8 or newer^).
    pause
    exit /b 1
  )
  echo Building BurgerEngine ^(first run takes a minute^)...
  dotnet build BurgerEngine.sln -c Release -nologo -v q
  if errorlevel 1 (
    echo Build failed - see the messages above.
    pause
    exit /b 1
  )
)
start "" "src\Burger.Studio\bin\Release\net8.0\BurgerStudio.exe" %*
