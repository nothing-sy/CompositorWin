# CompositorWin

Windows 图层合成编辑器（第一期），兼容 [robbietilton/Compositor](https://github.com/robbietilton/Compositor) 的 `.comp` 包，并让 **Cursor Agent 真正改图**——色相/饱和度/灰度/色阶/曲线/色彩平衡，且 **已打开的窗口会实时看到变化**。

桌面端使用 **WPF + SkiaSharp**（本机无 Windows App SDK 工作负载时的等价原生 Windows 方案）。图像运算在 `CompositorWin.Core`，Cursor 通过 MCP 调用。

## 要求

- Windows 10 19041+
- 开发：.NET 8 SDK（可用 `https://dot.net/v1/dotnet-install.ps1 -Channel 8.0`）

## 开发

```powershell
# 若 dotnet 不在 PATH：
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"

dotnet test CompositorWin.sln
dotnet run --project src/CompositorWin/CompositorWin.csproj
```

MCP 服务（Cursor 调用）：

```powershell
dotnet run --project src/CompositorWin.Mcp/CompositorWin.Mcp.csproj
```

项目已包含 [`.cursor/mcp.json`](.cursor/mcp.json)。在 Cursor 设置里启用 `compositorwin` MCP 后，先打开 CompositorWin 窗口，再对话：「把这张图的海水变蓝」。

## 安装包

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

产物在 `dist/`：自包含目录的 zip。本机若已装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)，同一脚本会再生成 `CompositorWin-Setup.exe`。没有 Inno 时可用：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install.ps1
```

这会复制到 `%LOCALAPPDATA%\Programs\CompositorWin` 并创建开始菜单快捷方式。

## Agent 约定

见 [AGENTS.md](AGENTS.md) 与 [docs/writing-comp-files.md](docs/writing-comp-files.md)。MCP 写盘后会通知 `http://127.0.0.1:47321/`，窗口重绘画布和图层面板。

## 许可

MIT。`.comp` 格式说明摘录自上游 Compositor（MIT）。
