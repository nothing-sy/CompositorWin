# compositor-edit

用 CompositorWin MCP 改用户图片，并让桌面窗口跟着变。

## 流程

1. `create_project` + `import_image`
2. `open_in_app`
3. `render_preview` / `analyze_image`
4. 局部改色：`add_adjustment` + `create_hue_mask`（海水偏蓝：centerHue 190 左右）
5. 灰度/发灰：Levels、Curves 或 Black & White
6. 再 `render_preview`，确认窗口状态栏出现「已从 Agent 更新」
7. `export_flattened`

不要跳过预览。不要在未原子替换 manifest 时留下半截 JSON。
