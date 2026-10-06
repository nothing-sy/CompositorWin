$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "publish\win-x64"
if (-not (Test-Path (Join-Path $src "CompositorWin.exe"))) {
    Write-Host "Publishing first..."
    & (Join-Path $PSScriptRoot "publish.ps1")
}
$dest = Join-Path $env:LOCALAPPDATA "Programs\CompositorWin"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src "*") -Destination $dest -Recurse -Force
$exe = Join-Path $dest "CompositorWin.exe"
$startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $startDir | Out-Null
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut((Join-Path $startDir "CompositorWin.lnk"))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Save()
$desktop = [Environment]::GetFolderPath("Desktop")
$deskLnk = $ws.CreateShortcut((Join-Path $desktop "CompositorWin.lnk"))
$deskLnk.TargetPath = $exe
$deskLnk.WorkingDirectory = $dest
$deskLnk.Description = "CompositorWin"
$deskLnk.Save()
Write-Host "Installed to $dest"
Write-Host "Start Menu shortcut: CompositorWin"
Write-Host "Desktop shortcut: $desktop\CompositorWin.lnk"
Start-Process $exe
