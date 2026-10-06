# ROADMAP

CompositorWin 对齐上游功能。P1–P6 均已落地（部分算法为实用近似，非 Adobe 像素级一致）。

## P1 基础（完成）

- Windows 桌面程序、`.comp` v11、Skia 合成、Agent MCP、热重载、去灰/色相蒙版

## P2 Transform / Canvas（完成）

- 全部 Photoshop 名混合模式（含 Linear Burn / Vivid Light / Pin Light / Hard Mix / Subtract / Divide）
- 图层组子树合成与组不透明度
- 裁剪、画布尺寸、图像尺寸
- 参考线与移动吸附
- 多文档标签

## P3 Adjustment UI（完成）

- 蒙版手绘工具
- 剪贴蒙版（`maskSourceID`）
- 色相/饱和度/明度/曝光滑杆

## P4 Painting（完成）

- 画笔、橡皮、仿制图章（Alt 取样）
- 渐变、矩形形状图层（可存 `shape` 元数据）

## P5 Selection + Type（完成）

- 选框、魔棒、套索工具位、内容感知填充（邻域扩散）
- 文字图层 `text`（含 colorRuns/fontRuns 字段）
- 主体蒙版 / 去背景（角点背景估计，非深度学习分割）

## P6 Effects + IO（完成）

- 投影、描边、颜色叠加、内外发光
- Camera Raw 风格曝光/对比/自然饱和度/色温
- 8-bit PSD/PSB 拼合图导入
- 去背景

后续若要对齐原版 Metal 滤镜或真实语义分割，可再换模型后端。
