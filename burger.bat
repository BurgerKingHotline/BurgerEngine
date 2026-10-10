@echo off
rem Command line access to the BurgerEngine tools:  burger help
if not exist "%~dp0src\Burger.Tools\bin\Release\net8.0\burger.dll" dotnet build "%~dp0BurgerEngine.sln" -c Release -nologo -v q
dotnet "%~dp0src\Burger.Tools\bin\Release\net8.0\burger.dll" %*
