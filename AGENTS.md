# Notes for AI agents

CompositorWin 是 Windows 图层编辑器。改图时走 MCP 工具，不要手写半截 `manifest.json`。

## 改一张用户图片（例如「把海水变蓝」）

1. 若用户给了图片路径或对话附件，先 `create_project`（宽高等于图片）再 `import_image`。
2. `open_in_app`，保证软件窗口打开同一份 `.comp`。
3. `render_preview` 看图，`analyze_image` 看主色相。
4. 「只改海水」：`add_adjustment`（Hue/Saturation，hue 朝蓝约 +10~+25，saturation +20~+50），然后 `create_hue_mask`（centerHue 约 175–210，range 25–40）。浑浊水可把 range 加大。
5. 「发灰」：再加 `Levels` 或 `Curves`，或在 Hue/Saturation 上提高 saturation、微调 lightness。天空过饱和则减小 saturation 或收窄蒙版。
6. 每一步后再 `render_preview`。用户开着的窗口应在约 0.3s 内重绘，无需让用户点重新打开。
7. 需要交件时 `export_flattened`。

## 其余工具（P2–P6）

- 画布：`crop` `resize_canvas` `resize_image`
- 图层：`add_text` `add_shape` `add_group` `clip_to_below` `add_drop_shadow`
- 图像：`remove_background` `subject_mask` `camera_raw` `import_psd`

快捷键：V 移动 / B 画笔 / E 橡皮 / M 选框 / W 魔棒 / T 文字 / G 渐变 / C 裁剪。仿制图章：Alt+点击取样。

## 写 `.comp` 文件

若必须直接写盘：先写 `images/*.png`，再把 `manifest.json` 写到临时文件后 rename。规则见 [docs/writing-comp-files.md](docs/writing-comp-files.md)。

## 构建

```
dotnet test CompositorWin.sln
dotnet run --project src/CompositorWin/CompositorWin.csproj
```
