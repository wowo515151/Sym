@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\..\"
set "CLI_PROJECT=%REPO_ROOT%src\SymCLI\SymCLI.csproj"
set "CLI_RELEASE=%REPO_ROOT%src\SymCLI\bin\Release\net10.0\SymCLI.exe"
set "CLI_DEBUG=%REPO_ROOT%src\SymCLI\bin\Debug\net10.0\SymCLI.exe"

if not exist "%CLI_PROJECT%" (
    echo Error: could not find SymCLI project at "%CLI_PROJECT%"
    exit /b 1
)

if exist "%CLI_RELEASE%" (
    "%CLI_RELEASE%" %*
    exit /b %errorlevel%
)

if exist "%CLI_DEBUG%" (
    "%CLI_DEBUG%" %*
    exit /b %errorlevel%
)

dotnet run --project "%CLI_PROJECT%" -- %*
exit /b %errorlevel%
