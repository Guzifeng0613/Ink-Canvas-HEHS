# 实测证据

这里存放支撑架构决策的原始测量结果，便于复核而不必重跑探针。
对应结论见 [设计文档](../specs/2026-11-13-winui3-migration-design.md) §2。

## 透明覆盖层探针

判据：先截一张桌面基线，再显示覆盖层（只画一个不透明品红矩形），
然后再截一张，比较**基线中非近白（即有信息量）的采样点**：

- `backgroundMatchRatio` —— 这些点保持不变的比例。越高越透明。
- `whiteFloodRatio` —— 这些点被刷成近白的比例。不透明窗口会接近 1。

| 文件 | 手法 | 结果 |
|---|---|---|
| `verdict-opaque.json` | 对照组：真正不透明的 WinUI 3 窗口 | match 0.0119 / flood 0.9881 |
| `verdict-t1.json` | 空 backdrop + 透明根 + `WS_EX_NOREDIRECTIONBITMAP`（XAML 初始化后） | 与对照组**完全一致** |
| `verdict-t4.json` | 同上，但 ex-style 在 `InitializeComponent()` **之前**设置 | 与对照组**完全一致** |
| `verdict-t5.json` | `SetWindowCompositionAttribute` + `ACCENT_ENABLE_BLURBEHIND` | 与对照组**完全一致** |
| `verdict-layered.json` | **Win32 分层窗口 + Win2D 预乘 alpha + `UpdateLayeredWindow`** | match **0.9765** / flood **0** |

`winui3-xaml-opaque.png` 是 t1 的截图：整屏被刷成白色，只有品红矩形可见 —— 透明失败。
`layered-overlay-alpha.png` 是分层窗口的截图：品红矩形与**半透明青色笔画**叠在桌面之上，
笔画下方文字透出，其余区域完全透明 —— 逐像素 alpha 与部分 alpha 均成立。

> **后续修正（重要）**：上面这组探针用的是 alpha=0 清屏。实际产品里**不能**这么做 ——
> 逐像素 alpha 的分层窗口是**按 alpha 做命中测试**的，alpha=0 的区域点击会直接穿透，
> 于是第一笔永远落不到覆盖层上，表现为「鼠标画不出来」。
> 产品改为以 **alpha=1**（预乘，故 RGB 也为 1）清屏：视觉上不可见（0.4% 白），
> 但整窗可命中。这与 WPF 原版 `Main_Grid Background="#01FFFFFF"` 的用意一致。
> 穿透模式仍由 `WS_EX_TRANSPARENT` 承担，不依赖 alpha。

> 注：另有 t2（DWM blur-behind）与 t3（transparent-gradient accent）在第一轮探针中同样失败；
> 该轮判据较弱（未排除白底桌面），故此处只保留判据收紧后重跑的 t1/t4/t5 与对照组。

## 运行时自检

`runtime-selftest.json` 由 `InkCanvas.exe --selftest=<目录>` 产出，走真实输入路径：

- `widthsIdentical` / `outlineIdentical` = true —— **提交后的笔画几何与抬笔前最后一帧完全一致**，
  这是笔锋闪烁的判据。
- `taperPresentWhileDrawing` = true，`tipWidth` 2.8 对 `settledWidth` 14 ——
  笔锋在**书写过程中**就已存在，不是抬笔后才补上。
- `highlighter.uniform` = true —— 荧光笔不收细。
- `3D coordinate system` 提交为 3 条轴。
- `selection corner drag scales and undo restores` —— 选中后拖右下角把宽度从 635 放大到 757.57，
  撤销后精确回到 635。
- `delete selection removes and undo restores` —— 删除 1 笔，撤销后数量还原。
- `replay reveals strokes monotonically and completes` —— 回放可见点数单调不减且能播完。
- `edge gesture property applied` = true —— 边缘手势抑制属性成功写入覆盖层窗口。
- `gpu readback stays well below full-screen-per-frame` —— 见下。

### 覆盖层回读量

整屏一帧为 8,294,400 字节（1920×1080×4）。

| 指标 | 数值 |
|---|---|
| 连续书写帧数 | 69 |
| 连续书写每帧回读 | **205,778 字节** |
| 占整屏比例 | **2.48%**（约 40× 降低） |
| 整轮自检平均占整屏逐帧方案比例 | 10.42% |

整轮比例高于书写态，是因为自检包含多次提交，而**每次提交都必须重建已提交层缓存**（整屏回读一次）。
真实使用中提交是离散的，连续书写才是热点路径。

`app-running.png` 是该次运行的整屏截图：透明覆盖层上有红色收锋笔画、
半透明黄色荧光笔、蓝色空间坐标系与圆，底部为 WinUI 3 浮动工具栏。
`selection-handles.png` 是选区特写：蓝色选框、四角白色缩放手柄、上方带连线的旋转手柄。