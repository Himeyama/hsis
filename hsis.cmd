@echo off
dotnet run --project "%~dp0src\Hsis.Cli\Hsis.Cli.csproj" -- %*
exit /b %ERRORLEVEL%
