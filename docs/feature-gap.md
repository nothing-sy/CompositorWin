# CompositorWin 与原版功能差异及后续计划

对照上游 [robbietilton/Compositor](https://github.com/robbietilton/Compositor) README Features（2026-10）。  
本文只记录差距和怎么补，不宣称已像素级还原 Photoshop / 原版 Metal。

**结论：没有全部还原。** 仓库 [ROADMAP.md](../ROADMAP.md) 的 P1–P6「完成」= Windows 上有对应入口和可用近似，不是原版全集。

现有自动测试约 7 个（反相、原子写盘、Linear Burn、组不透明度、文字往返、BlueSea 等），覆盖远小于菜单与 MCP 表面积。

---

## 已具备（可用，深度不够）

| 能力 | Win 现状 |
|------|----------|
| `.comp` v11、MCP、热重载、多标签、中文工作台 | 有 |
| 图层 / 组 / 不透明度 / Photoshop 名混合模式、空白层、删除、可见性 | 有；组不透明度会传到子树 |
| 剪贴蒙版、手绘蒙版、MCP 色相范围蒙版 | 有 |
| 调整层：色相/饱和度、色阶、曲线、曝光、黑白、色彩平衡、反相 | 多数只加默认层，**无独立完整滑杆面板** |
| 移动数值框、参考线吸附、裁剪 / 画布 / 图像尺寸 | 画布与图像尺寸为固定加减，无对话框 |
| 矩形选框、套索、魔棒、内容识别填充 | 填充为邻域扩散，非 Adobe 内容感知 |
| 画笔 / 橡皮 / 仿制（Alt 取样）/ 涂抹式 Blur / 渐变 / 矩形形状 / 文字对话框 / 吸管 | 有；污点修复现为偏移仿制 |
| 投影、描边、颜色叠加 | 有默认参数；无完整效果面板 |
| Camera Raw | 仅曝光 / 对比 / 自然饱和 / 色温 |
| PSD/PSB | **拼合**导入，不保留图层结构 |
| 去背景 | 角点颜色估计，非深度学习 |
| 导出 JPEG/PNG、Agent 热重载 | 有 |
| 合成 | CPU Skia + 图层光栅 LRU，非 Metal 纹理画布 |

MCP 已有：`create_project` `import_image` `inspect_project` `list_layers` `render_preview` `analyze_image` `add_adjustment` `set_adjustment` `create_hue_mask` `set_layer` `export_flattened` `open_in_app` `crop` `resize_canvas` `resize_image` `add_text` `add_shape` `add_group` `clip_to_below` `remove_background` `subject_mask` `import_psd` `add_drop_shadow` `camera_raw`。

---

## 未还原或仅占位（原版有）

### 编辑与历史

- 撤销 / 重做、历史记录
- 键盘快捷键编辑器（菜单「键盘快捷键…」为空）
- 数字标签拖拽微调（scrub）

### 图层

- 拖拽排序 / 嵌套、内联改名、图层面板右键菜单
- 复制粘贴图层与组、Option 拖复制、跨文档拖图层
- Merge Down / Merge Visible / Merge Group

### 蒙版

- 填充 / 反相 / 模糊 / 羽化
- 蒙版与图层链接 / 取消链接
- 超出图层像素边界绘制的完整语义

### 调整层（原版有、Win 无或未入口）

- Gradient Map、Grain
- 作为调整层的 Gaussian Blur、Motion Blur、Noise

### 图层效果

- Inner Shadow、Outer Glow、Inner Glow 的完整可编辑 GPU 面板

### 变换

- 自由扭曲（⌘-拖手柄）
- 旋转 / 翻转图层与画布
- 多图层或整组一起变换
- 吸附到图层边缘 / 中心 / 网格 / 文档边界（现主要是参考线）
- 角度与缩放的完整非破坏变换绘制

### 选区

- 椭圆选框、多边形套索
- Object / Select Subject（ML）、Tab 切换 Wand/Object
- Expand / Contract / Feather
- 选区相加 / 相减、移动选区轮廓、移动或复制选区内像素
- 载入图层透明或蒙版为选区

### 绘画与对象

- 画笔 smoothing、Shift 直线
- 污点修复 content-aware（现为附近仿制）
- 图章 aligned / 单层或全层采样
- 可编辑形状：圆角矩形、椭圆、直线（矢量编辑，非立刻栅格化）
- 文字：画布内多行、fontRuns、对齐与字距、作剪贴蒙版

### 滤镜

- 完整 Camera Raw（曲线、HSL、色彩分级、细节、光学、几何）
- Vignette、Bloom/Glow、Tonal Contrast、Lens Correction
- 真实语义分割去背

### 画布与文件

- 从标尺拖出参考线、布局网格及 Snap To
- Trim；裁剪比例（3:4、9:16）、对称裁剪
- 放大时像素网格；缩小时高质量降采样金字塔（已有 MIP 缓存雏形，行为未对齐原版）
- 导入 HEIC / SVG / 相机 RAW 显影
- PSD 保留文件夹、蒙版、混合、简单横排文字；转换报告
- Copy Merged、后台保存、自动更新

### 渲染

- Metal GPU 画布（图层常驻纹理，平移缩放只重摆放）

---

## 后续开发顺序（建议）

按「先稳已实现，再补交互，最后对齐原版深度」排，可按迭代拆 PR。

### A. 质量与自测（不扩功能）

1. 给已实现 Core 补 xUnit：合成不透明度 / Multiply、Crop 与尺寸、色相蒙版、选框与画笔改像素、文字形状组写盘读回、投影改变合成、`LayerRasterCache`、`ExportFlattened`。
2. `dotnet test CompositorWin.sln` 作为回归门槛。
3. 手工：多标签、工具悬停提示、画笔跟手、Agent 写盘后约 0.3s 刷新（WPF 点击自动化本阶段不做）。

### B. 编辑体验（原版日常最高频缺口）

1. 撤销 / 重做（文档快照或命令栈，与热重载共存：Agent 写入视为新基线）。
2. 图层列表：拖拽排序与进组、双击改名、右键复制/删除/栅格化。
3. 图层复制粘贴与 Merge Down。
4. 画布 / 图像尺寸 / 裁剪对话框（宽高、锚点、比例）。

### C. 选区与绘画对齐原版工具语义

1. 椭圆选框、多边形套索、选区加减、羽化。
2. 载入透明 / 蒙版为选区。
3. 画笔 smoothing、Shift 直线；图章 aligned。
4. 形状椭圆 / 线保持 `shape` 元数据可再编辑。
5. 文字在画布上直接改，而不是模态对话框。

### D. 调整、效果、滤镜

1. 选中调整层时显示对应滑杆（色相、色阶、曲线）。
2. Gradient Map、Blur、Noise 调整层。
3. Inner/Outer Glow、Inner Shadow 参数面板。
4. Camera Raw 分面板（至少曲线 + 饱和），再考虑光学 / 几何。
5. 去背：可换 ONNX 分割，角点法保留为回退。

### E. 文件与画布

1. 标尺拖参考线、网格吸附。
2. PSD 分层导入（文件夹、蒙版、混合）；失败项出报告。
3. HEIC / TIFF / SVG；RAW 走简化显影。
4. Copy Merged、Trim。

### F. 性能（原版 GPUCanvas 方向）

当前已是「图层 PNG LRU + Skia 绘制 + 笔划工作位图」。下一步才是 GPU 纹理画布（Direct3D / Skia GPU），平移缩放不重合成。不与 B/C 抢同一迭代，除非又出现整窗卡死。

---

## 本文件怎么用

- 新功能 PR 先在对应章节勾进度或改「Win 现状」列，避免 ROADMAP 再次写成「已全部完成」。
- 不要在未实现项上改 README 成「完整 Photoshop 替代」。
- Agent / MCP 新工具与桌面菜单成对加，并在 A 类测试里加一条文件或像素断言。
