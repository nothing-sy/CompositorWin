# Writing CompositorWin / Compositor projects

A `.comp` project is a **folder** (not a single file):

```
Example.comp/
  manifest.json
  images/
    <LAYER-UUID>.png
    <LAYER-UUID>.mask.png   (optional)
```

Windows 上请把 `.comp` 当成目录打开。格式标识：

```json
{
  "format": "com.compositor.project",
  "version": 11,
  "colorSpace": "sRGB",
  "documentID": "...",
  "width": 1920,
  "height": 1080,
  "resolution": 72,
  "activeLayerID": "...",
  "layers": []
}
```

图层数组 **自下而上**（最后一个在最上）。`imageFile` 必须是该层 `id` 的大写 UUID + `.png`。

安全写入（项目打开时）：

1. 先写/覆盖 PNG
2. 写 `.manifest.json.tmp` 再 rename 成 `manifest.json`

CompositorWin 监视目录，并监听 `http://127.0.0.1:47321/reload`。MCP 工具在保存后会自动通知窗口。

完整字段（文件夹、调整、文字、效果）见上游 [project-format.md](https://github.com/robbietilton/Compositor/blob/main/docs/project-format.md)。摘录副本在 [../vendor/format-docs/](../vendor/format-docs/)。
