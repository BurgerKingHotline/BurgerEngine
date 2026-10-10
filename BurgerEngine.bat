@echo off
setlocal EnableExtensions
title BurgerEngine
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK is not installed. Download it from https://dotnet.microsoft.com/download ^(version 8 or newer^).
  pause
  exit /b 1
)

if not exist "src\Burger.Tools\bin\Release\net8.0\burger.dll" goto build
if not exist "src\Burger.Game\bin\Release\net8.0\Burger.Game.exe" goto build
if not exist "src\Burger.Studio\bin\Release\net8.0\BurgerStudio.exe" goto build
goto menu

:build
echo Building BurgerEngine (first run takes a minute)...
dotnet build BurgerEngine.sln -c Release -nologo -v q
if errorlevel 1 (
  echo.
  echo Build failed - see the messages above.
  pause
  exit /b 1
)

:menu
echo.
echo ================= BurgerEngine =================
echo   Projects:
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll list
echo.
echo   E  Open Burger Studio (the editor)
echo   1  Play a project
echo   2  Create a new project
echo   3  Check a project for errors (validate)
echo   4  Export a project as an EXE
echo   5  Name new province colors (generate-locations)
echo   6  Write the script reference (docs)
echo   7  Rebuild the engine (after changing engine code)
echo   0  Quit
echo ================================================
set "choice="
set /p choice=Choose: 
if /i "%choice%"=="E" goto editor
if "%choice%"=="1" goto play
if "%choice%"=="2" goto new
if "%choice%"=="3" goto validate
if "%choice%"=="4" goto export
if "%choice%"=="5" goto genloc
if "%choice%"=="6" goto docs
if "%choice%"=="7" goto build
if "%choice%"=="0" exit /b 0
goto menu

:askproject
set "project="
set /p project=Project name [SampleGame]: 
if "%project%"=="" set "project=SampleGame"
exit /b 0

:editor
start "" "src\Burger.Studio\bin\Release\net8.0\BurgerStudio.exe"
goto menu

:play
call :askproject
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll run "%project%"
goto menu

:new
set "name="
set /p name=New project name (no spaces): 
if "%name%"=="" goto menu
set "blank="
set /p blank=Start from the sample game? [Y/n]: 
if /i "%blank%"=="n" (
  dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll new "%name%" --blank
) else (
  dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll new "%name%"
)
goto menu

:validate
call :askproject
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll validate "%project%"
goto menu

:export
call :askproject
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll export "%project%"
goto menu

:genloc
call :askproject
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll generate-locations "%project%"
goto menu

:docs
dotnet src\Burger.Tools\bin\Release\net8.0\burger.dll docs
goto menu
