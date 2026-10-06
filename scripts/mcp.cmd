@echo off
setlocal
set PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set ROOT=%~dp0..
dotnet build "%ROOT%\src\CompositorWin.Mcp\CompositorWin.Mcp.csproj" -nologo -v q >nul
dotnet "%ROOT%\src\CompositorWin.Mcp\bin\Debug\net8.0\CompositorWin.Mcp.dll"
