@echo off
setlocal

rem Runs DriveCLI from the repo without requiring installation.

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."

dotnet run --project "%REPO_ROOT%\DriveCLI\DriveCLI.csproj" -- %*
exit /b %ERRORLEVEL%
