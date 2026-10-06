$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = Get-Location }
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Set-Location $root
$publish = Join-Path $root "publish\win-x64"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $publish, $dist | Out-Null

dotnet publish src/CompositorWin/CompositorWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publish
dotnet publish src/CompositorWin.Mcp/CompositorWin.Mcp.csproj -c Release -r win-x64 --self-contained true -o (Join-Path $publish "mcp")

$zip = Join-Path $dist "CompositorWin-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
Write-Host "Published zip: $zip"

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc (Join-Path $root "installer\compositorwin.iss")
    Write-Host "Installer built in dist/"
} else {
    Write-Host "Inno Setup not installed. Zip is ready; install Inno Setup 6 to build CompositorWin-Setup.exe."
}
