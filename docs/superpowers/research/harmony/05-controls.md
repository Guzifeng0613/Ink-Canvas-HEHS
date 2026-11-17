# 05 — InkCanvas.Controls 层源码盘点（WinUI 3 → HarmonyOS ArkTS）

- 日期：2026-11-18
- 仓库：`D:\InkCanvas`，分支 `feat/winui3-migration`，工作树（含未提交的 3.4.0 改动）为准
- 范围：`src/InkCanvas.Controls/` 全部源码；因为任务点名，同时核对了直接消费方（`src/InkCanvas.App/Views/FloatingBarWindow.cs`、`BarPanelWindow.cs`、`FoldedBarHandleWindow.cs`、`PickerWindows.cs`）以及 `src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs`、`AppSettings.cs`
- 性质：只读盘点。本报告之外未修改任何文件；未构建、未运行、未使用 HarmonyOS SDK、未实机验证。
- 结论标注：源码事实（代码即证据）与移植建议分开写；所有“未验证”都明确标出。

---

## 0. 文件清单与依赖

`src/InkCanvas.Controls/` 只有 9 个源码/XAML 文件（不含 bin/obj）：

| 文件 | 行数 | 作用 |
|---|---:|---|
| `Controls/FloatingBar.cs` | 944 | 浮动批注栏本体、更多设置/手势面板内容、形状目录 |
| `Controls/PenPalette.cs` | 527 | 笔/荧光笔调色盘 |
| `Controls/TouchIconButton.cs` | 323 | ≥40 触摸目标的图标按钮 |
| `Controls/ShapePreview.cs` | 660 | 图形菜单目录、矢量预览、长按/固定 |
| `Controls/InkSurface.cs` | 155 | 白板/回放用不透明 Win2D 墨迹表面 |
| `Controls/CountdownRing.cs` | 99 | 计时器圆环 |
| `Controls/Glyphs.cs` | 74 | 图标码点表 |
| `Animation/Motion.cs` | 118 | 共享动画参数 |
| `Themes/Tokens.xaml` | 57 | 全局主题 token |
| `Theming/PopupBrushes.cs` | 116 | WPF 弹窗/浮条精确调色板 |
| `InkCanvas.Controls.csproj` | — | net10.0-windows10.0.26100.0、UseWinUI、Win2D、WindowsAppSDK.WinUI 1.8.260803003；引用 Core/Platform/Ink |

依赖方向：Controls → Ink → Platform → Core，只使用 Core 的设置/颜色/形状枚举与 Ink 的 `InkController`。Controls 不直接调用 Win32/COM；平台绑定集中在 **WinUI 3 XAML、Microsoft.UI.Composition、Win2D、WinRT UISettings**。

---

## 1. 全局度量基线：WPF Viewbox 缩放因子 S

`FloatingBar.WpfScale = 50.0 / 36.0 = 1.388888…`。上游 WPF 把 36 DIP 高的内容放进 Height=50 的 Viewbox，因此移植版把每个 WPF 度量乘以 S，使 `FloatingBarScale=100` 时屏幕尺寸与 WPF 一致。

| 常量 | 公式 | 精确值（DIP） | 用途 |
|---|---|---:|---|
| `WpfScale` / `S` | 50/36 | 1.3888888889 | 全部浮条度量的乘数 |
| `PartH` | 36*S | 50 | 浮条每张卡/整行高度 |
| `BtnW` | 30*S | 41.6666667 | 主栏/画布控制按钮宽 |
| `BtnH` | 32*S | 44.4444444 | 主栏/画布控制按钮高（≥40） |
| `IconSz` | 16*S | 22.2222222 | 主栏图标字号 |
| `LabelSz` | 8*S | 11.1111111 | 主栏图标下文字字号 |
| `PartCorner` | 5*S | 6.9444444 | 卡片/按钮圆角 |
| `PartBorder` | 1*S | 1.3888889 | 卡片/徽标边框、画板分隔线 |
| `PartGap` | 2*S | 2.7777778 | 独立卡片间距（WPF Margin 2,0,0,0） |
| `MainPadH` | 4*S | 5.5555556 | 主卡每侧 padding(2)+margin(2) |
| `MainPadV` | 1*S | 1.3888889 | 主卡上下 padding |
| `MoreBtn` | 45*S | 62.5 | 更多设置按钮边长/行高 |
| `MoreIcon` | 22*S | 30.5555556 | 更多设置图标字号 |
| `MoreLabel` | 10*S | 13.8888889 | 更多设置文字字号 |
| `PptExitW` | 34*S | 47.2222222 | PPT 退出牌宽度 |
| `GestureRowH` | 25*S | 34.7222222 | 手势选项行高 |
| `GestureRowSpacing` | -2.5*S | -3.4722222 | 手势行垂直间距（负值重叠） |

其它常用换算：

| 原式 | DIP |
|---|---:|
| 2*S | 2.7777778 |
| 4*S | 5.5555556 |
| 5*S | 6.9444444 |
| 8*S | 11.1111111 |
| 10*S | 13.8888889 |
| 12*S | 16.6666667 |
| 14*S | 19.4444444 |
| 15*S | 20.8333333 |
| 16*S | 22.2222222 |
| 18*S | 25.0 |
| 20*S | 27.7777778 |
| 22*S | 30.5555556 |
| 24*S | 33.3333333 |
| 25*S | 34.7222222 |
| 28*S | 38.8888889 |
| 30*S | 41.6666667 |
| 32*S | 44.4444444 |
| 34*S | 47.2222222 |
| 36*S | 50.0 |
| 42*S | 58.3333333 |
| 45*S | 62.5 |

宿主还会再乘 `FloatingBarScale/100`（`FloatingBarWindow.UserScale = clamp(scalePercent, 50, 200)/100`）。注意设置窗口滑杆本身只给 50–150（`SettingsWindow.cs:493`），而宿主/自检允许到 200；移植时应以 50–200 的宿主 clamp 为准，或统一后再钉住。

---

## 2. 主题 token（`Themes/Tokens.xaml`）

`App.xaml` 合并 `ms-appx:///InkCanvas.Controls/Themes/Tokens.xaml`。所有颜色在 Light/Dark 两个 `ThemeDictionaries` 中同名定义；下游可用 `{ThemeResource}`，也可用 `Application.Current.Resources[key]`（代码里 `TouchIconButton` 就是这么取 `IcAccentBrush` / `IcTextPrimaryBrush` / `IcAccentSoftBrush`）。

### 2.1 颜色

| Key | Light | Dark |
|---|---|---|
| `IcSurfaceColor` | `#FFF6F7F9` | `#FF202124` |
| `IcSurfaceAltColor` | `#FFFFFFFF` | `#FF2B2C2F` |
| `IcStrokeColor` | `#1F000000`（alpha 31） | `#24FFFFFF`（alpha 36） |
| `IcTextPrimaryColor` | `#E4000000`（alpha 228） | `#F2FFFFFF`（alpha 242） |
| `IcTextSecondaryColor` | `#9E000000`（alpha 158） | `#A8FFFFFF`（alpha 168） |
| `IcAccentColor` | `#FF0067C0` | `#FF4CC2FF` |
| `IcAccentSoftColor` | `#1A0067C0`（alpha 26） | `#264CC2FF`（alpha 38） |
| `IcBoardBlackColor` | `#FF1F3B2C` | `#FF16281E` |
| `IcBoardWhiteColor` | `#FFFAFAFA` | `#FF1B1B1B` |

同名 Brush：`IcSurfaceBrush`、`IcSurfaceAltBrush`、`IcStrokeBrush`、`IcTextPrimaryBrush`、`IcTextSecondaryBrush`、`IcAccentBrush`、`IcAccentSoftBrush`。

### 2.2 尺寸/间距 token

| Key | 值 | 说明 |
|---|---|---|
| `IcTouchTargetMin` | 40 | 触摸目标下限（DIP） |
| `IcTouchTargetComfortable` | 48 | 舒适触摸目标 |
| `IcFloatingBarIcon` | 22 | 图标字号 |
| `IcControlCorner` | 8 | 控件圆角 |
| `IcBarCorner` | 14 | 浮动条圆角 |
| `IcBarPadding` | `8,6,8,6` | 浮条内边距 |
| `IcCardPadding` | `16,12,16,12` | 卡片内边距 |

注意：`IcBarCorner=14`、`IcControlCorner=8` 是通用 token；浮条实际使用的是 S 换算后的 `PartCorner=6.944`、按钮选中 wash 圆角 `4*S=5.556`、`TouchIconButton` 默认圆角 8。两边不能混用，移植时按组件各自的值复刻。

### 2.3 token 的实际使用

- `IcSurfaceBrush`：`WindowChrome.cs:342/357`、`ReplayWindow.cs:85`。
- `IcAccentBrush` / `IcTextPrimaryBrush` / `IcAccentSoftBrush`：`TouchIconButton.ApplyVisualState()`。
- `IcBoardBlackColor` / `IcBoardWhiteColor`、`IcSurfaceAlt`、`IcStroke` 在 Controls 内没有直接读取，属于为后续窗口预留的 token；移植时仍应进入共享 `tokens.json`。

---

## 3. `Theming/PopupBrushes.cs` — 精确调色板

`theme` 约定：`0` = 跟随系统（按浅色处理，等同 WPF 默认），`1` = 浅色，`2` = 深色；`IsDark(theme) => theme == 2`。全部是 ARGB，注释里的 `@0.95` 等是 WPF 不透明度。

| 方法 | Light | Dark |
|---|---|---|
| `Surface` | `#F2F0F3F9`（240,243,249 @0.95） | `#F21F1F1F`（31,31,31 @0.95） |
| `Border` | `#CC0066BF`（0,102,191 @0.8） | `#E6FFFFFF`（白 @0.9） |
| `Foreground` | Black | White |
| `BadgeBackground` | `#E60066BF`（0,102,191 @0.9） | `#E61E3A8A`（30,58,138 @0.9） |
| `BadgeForeground` | White | White @0.8 = `#CCFFFFFF` |
| `RingTrack` | `#FFEAEAEA` | `#FF3A3A3A` |
| `FloatBarBackground` | White @0.75 = `#BFFFFFFF`（191） | Black @0.6 = `#99000000`（153） |
| `FloatBarBorder` | Black @0.8 = `#CC000000`（204） | White @0.6 = `#99FFFFFF`（153） |
| `FloatBarForeground` | `#FF000000` | `#FFFFFFFF` |
| `FloatBarTitleBackground` | `#40588BFC`（88,139,252 @0.25 = 64） | `#802156C9`（33,86,201 @0.5 = 128） |
| `FloatBarButtonHover` | White @0.4 = `#66FFFFFF`（102） | `#2200CDCD`（0,205,205 @0.133 = 34） |
| `FloatBarButtonPressed` | White @0.6 = `#99FFFFFF`（153） | `#4400CDCD`（0,205,205 @0.267 = 68） |
| `FloatBarSelected` | 两主题相同：`#801296DB`（18,150,219 @0.5 = 128） | 同左 |
| `FloatBarColorful` | 线性渐变 `#9580B0FF` → `#95C0FFC0`，StartPoint (0,0)，EndPoint (1,1) | 同左 |

固定语义色：

| 方法 | 值 |
|---|---|
| `Accent` | `#FF0066BF`（两主题） |
| `Danger` | `#FFE32A34` |
| `DisabledCover` | `#FFBFBFBF` |
| `RingActive` | `#FF0067C1` |
| `RingPaused` | `#FF1A71C8` |
| `YesGreen` | `#FF15803D` |
| `YesGreenHover` | `#FF166534` |
| `YesGreenPressed` | `#FF306534` |

**已发现的主题不一致（移植前必须决策）**：

1. `CountdownRing` 的轨道颜色硬编码为 `#FFEAEAEA`，没有使用 `PopupBrushes.RingTrack(theme)`；因此深色主题下计时环轨道仍是浅灰。`PopupBrushes.RingTrack` 的深色值 `#FF3A3A3A` 在 Controls 内无人调用。
2. `ShapePreview` 在“画板 + 深色”时把外框背景硬编码为 `#BF000000`（191），与 `PopupBrushes.FloatBarBackground(2)` 的 153 不同；这是上游 BoardBarBackground 的单独值，移植时要按 boardMode 保留，不能简单统一。

---

## 4. `Controls/FloatingBar.cs` — 浮动栏

### 4.1 视觉结构

整行只包含 4 张独立圆角卡（`_barRow`，`StackPanel` Horizontal，`Spacing = PartGap = 2.7778`，水平/垂直居中）：

1. `_dragHandle`：50×50，笑脸拖动手柄，`FontIcon Glyphs.Emoji`（U+E76E），字号 `20*S=27.7778`，宽 `28*S=38.8889`，高 `22*S=30.5556`；整卡可拖窗，点击阈值 ≤10 DIP 时折叠/展开主栏。
2. `_mainHost`：宽自适应，高 50；`Padding=(2*S, 1*S)`，内部 `_primary.Margin=(2*S,0)`；主工具栏。
3. `_gestureBorder`：50×50，内部手势按钮 47.222×47.222。
4. `_pptExitBorder`：47.222×50，初始 `Collapsed`，仅放映模式显示；圆角 `4*S=5.5556`。

每张卡：`Background`/`BorderBrush` 来自主题，`BorderThickness=PartBorder=1.3889`，`CornerRadius=PartCorner=6.9444`。外层 `Content` 是透明、无边框、无 padding、无圆角的 `Border`，保证卡间 2.778 DIP 空隙可见。

### 4.2 主栏按钮完整顺序（左→右）

主栏 `_primary` 的实际 child 顺序如下。所有 `AddPrimary` 按钮都是 `TouchIconButton`，`ConfigureBarButton(more:false)` 后为 **41.667×44.444**，`MinWidth/MinHeight=0`，`Padding=0`，圆角 6.944，选中圆角 5.556，图标字号 22.222，文字字号 11.111，图标区高 30.556，文字 Margin `(0,-1.389,0,0)`。

| # | 标签 | Glyph | 尺寸/特殊 | Tooltip / 快捷键 | 行为 |
|---:|---|---|---|---|---|
| 1 | 鼠标 | `Cursor` U+E8B0 | 左右 Margin `-2.778` | `鼠标模式（点击穿透到下方程序）— 快捷键 Alt+Q` | 未穿透时 `Shell.ToggleClickThrough()` |
| 2 | 批注 | `PenWpf` U+EE56 | — | `批注（点击打开墨迹选项）— 快捷键 Alt+D` | `SetTool(Pen)` + `Shell.OpenPenPalette(false)` |
| 3 | 荧光笔 | `HighlightWpf` U+E7E6 | `IconFont` 显式改为 `Segoe MDL2 Assets` | `荧光笔（点击打开颜色与不透明度）` | `SetTool(Highlighter)` + `OpenPenPalette(true)` |
| 4 | 清空 | `Delete` U+E74D | — | `清空（有选中时删除选中墨迹）— 快捷键 Ctrl+E` | 有选区 `DeleteSelection()`，否则 `Clear()` |
| 5 | （嵌套 `_canvasControls`） | — | 横向 StackPanel，穿透时整体 Collapsed | — | 见 #6–#14 |
| 6 | 圆形擦 | `Eraser` U+E75C + 覆盖 `EraserOverlayCircle` U+F0B6 | 组合图标：底 22.222，覆盖 12*S=16.667，右下，右 Margin 2.778；Grid 41.667×22.222 | `圆形擦（按面积擦除）— 快捷键 Alt+E` | `SetTool(Eraser)` + `EraserMode=Area` |
| 7 | 墨迹擦 | `Eraser` + `EraserOverlayStroke` U+F128 | 覆盖 8*S=11.111 | `墨迹擦（整笔擦除）` | `EraserMode=WholeStroke` |
| 8 | 选择 | `SelectWpf` U+E958 + `SelectOverlay` U+F8AF | 覆盖 10*S=13.889 | `选择 — 快捷键 Alt+S` | `SetTool(Select)` |
| 9 | 图形 | `ShapesWpf` U+F158 | — | `图形` | `Shell.OpenShapePicker()` |
| 10 | 插入图片 | `InsertMedia` U+EE71 | 禁用，Opacity 0.4 | `插入图片：桌面批注层尚未接入图片插入` | 无 |
| 11 | 插入视频 | `InsertMedia` U+EE71 | 禁用，Opacity 0.4 | `插入视频：桌面批注层尚未接入视频插入` | 无 |
| 12 | 撤销 | `Undo` U+E7A7 | 不可撤销时 Opacity 0.35 | `撤销 — 快捷键 Ctrl+Z` | `History.CanUndo` 时 `Undo()` |
| 13 | 重做 | `Redo` U+E7A6 | 不可重做时 Opacity 0.35 | `重做 — 快捷键 Ctrl+Y` | `History.CanRedo` 时 `Redo()` |
| 14 | 清并鼠 | `Cursor` + `Delete` | 覆盖 12*S=16.667；Grid 41.667×22.222 | `清空并进入鼠标模式` | 清空/删选区 + 未穿透时切穿透 |
| 15 | 隐藏控制 | `HideControl` U+E7B3 | 默认隐藏（`isShowHideControlButton=false`） | `收起/展开主工具栏` | `ToggleMainCollapsed()` |
| 16 | 左右切换 | `LRSwitch` U+E8AB | 默认隐藏；即使显示也禁用 0.4 | `左右切换：尚未接入` | 无（onClick=null） |
| 17 | 画板 | `BoardWpf` U+E929 | 左 Margin `PartBorder=1.389`，按钮内部左缘画 1.389 宽分隔线，上下 Margin 2.778 | `画板 — 快捷键 Alt+B` | `Shell.ToggleBoard()` |
| 18 | 工具 | `Tools` U+E8A9 | — | `更多设置` / 展开后 `收起更多设置` | `ToggleOverflow()` |
| 19 | 隐藏 | `FoldWpf` U+ED1A | 左右 Margin `-2.778` | `收纳工具栏到屏幕侧边 — 快捷键 Alt+V`；折叠后 `展开工具栏` | `Shell.ToggleFold()` |

刷新顺序说明：`Build()` 先清空所有面板，再按上表创建；`_canvasControls` 是在第 4 个按钮之后 `Add` 到 `_primary` 的，因此嵌套按钮位于“清空”和“隐藏控制”之间。

### 4.3 手势卡（第 3 张卡）

- `TouchIconButton`：`Glyph=Gesture` U+F271，`Label="手势"`。
- 先 `ConfigureBarButton(more:false)`，再覆盖：`Width=Height=34*S=47.222`，`IconSize=20*S=27.778`，`LabelMargin=(0, -2*S, 0, -1*S) = (0,-2.778,0,-1.389)`。
- Tooltip：`手势选项`。
- 点击 `ToggleGesturePanel()`。

### 4.4 PPT 退出牌（第 4 张卡）

- 矢量图，整体 `Viewbox 30*S × 20*S = 41.667×27.778`；内部画布 32×30（未缩放）。
- 屏幕轮廓：Rect 24×16，`StrokeThickness=1.5`，Margin (3,3)。
- X 形 Path，Data 逐字为：
  `M13.5355339,10.9497475L10,14.4852814 11.4142136,15.8994949 14.9497475,12.363961 18.4852814,15.8994949 19.8994949,14.4852814 16.363961,10.9497475 19.8994949,7.41421356 18.4852814,6 14.9497475,9.53553391 11.4142136,6 10,7.41421356 13.5355339,10.9497475z`
  填充前景色。
- 4 个实心矩形（x,y,w,h）：`(14,19,2,8)` 支架、`(0,2,30,2)` 顶部线、`(13,0,4,2)` 顶部缺口、`(7,26,16,2)` 底座。
- Tooltip `退出放映`；点击走 `TryHandleTap` → `Shell.PptExit()`。

### 4.5 布局公式（ArkTS 侧必须 1:1）

`ComputeLayoutSize()`：

```
primaryWidth = MeasureRow(_primary)
mainWidth    = primaryWidth + 2 * (MainPadH + PartBorder) = primaryWidth + 13.8889
rowWidth     = 50                                  // 拖手永远存在
if mainHost visible:  rowWidth += 2.7778 + mainWidth
if gesture visible:   rowWidth += 2.7778 + 50
if pptExit visible:   rowWidth += 2.7778 + 47.2222
return (rowWidth, 50)
```

`MeasureRow(row)`：

```
total=0; visible=0
for child in row.Children:
  if collapsed: continue
  margin = child.Margin.Left + child.Margin.Right
  if child is StackPanel nested:
     w = MeasureRow(nested)
     if w <= 0: continue
     total += w + margin
  else:
     w = child.Width if finite&&>0 else child.ActualWidth if >0 else max(DesiredSize.Width - margin, MinWidth)
     total += w + margin
  visible++
if visible > 1: total += (visible-1) * row.Spacing
```

`CountVisibleItems()`：统计可见按钮与分隔线；`_canvasControls` 里递归统计，`_gestureBorder` 和 `_pptExitBorder` 各算 1 个按钮，拖手算 1 个按钮。分隔线只在 `child is Border` 且直接位于行内时计数；当前实现的分隔线嵌在画板按钮内部的 Grid 里，所以 `Separators` 实际上恒为 0（移植时不要依赖该值）。

默认设置下的自然宽度估算（labels 显示与否不影响宽度）：

- 默认（隐藏控制/左右切换默认隐藏，其余 16 个按钮可见）
  `16*41.6667 - 5.5556(cursor) + 1.3889(board) - 5.5556(fold) + 13.8889 + 50 + 2.7778 + 50 + 2.7778 ≈ 776.39 DIP`
- 所有显隐门都打开（18 个按钮）≈ **859.72 DIP**
- 高度恒 50 DIP。再乘用户缩放 0.5–2.0。

### 4.6 选中（selection wash）与视觉状态

`RefreshSelection()`：

```
tool = Controller?.Tool ?? Pen
ink  = !ClickThroughActive
pen selected        = ink && tool==Pen
highlighter selected= ink && tool==Highlighter
eraserCircle selected = ink && tool==Eraser && EraserMode==Area
eraserStroke selected = ink && tool==Eraser && EraserMode==WholeStroke
select selected     = ink && tool==Select
shape selected      = ink && tool==Shape
cursor selected     = false（永远）
_canvasControls.Visibility = ClickThrough ? Collapsed : Visible
RefreshGestureBorderVisibility()
RefreshUndoRedo()
Opacity = pptMode ? (colorful ? 0.8 : 0.75)
                  : (colorful ? 0.95 : 1.0)
```

选中 wash：

- 颜色固定 `FloatBarSelected = #801296DB`（18,150,219，alpha 128），两个主题相同。
- 画在 `TouchIconButton._contentBorder`（图标+文字的容器）上，圆角 `SelectionCornerRadius = 4*S = 5.5556`；未选中时该容器透明。
- 按钮本体背景不承担选中色；选中时前景色仍是 `FloatBarForeground`（黑/白），不变色。
- 按钮底：`isTransparentButtonBackground=true`（默认）→ 全透明；否则深色 `ARGB(20,255,255,255)`、浅色 `ARGB(20,0,0,0)`。
- Hover/Pressed 来自 `FloatBarButtonHover/Pressed`，通过模板资源 `ButtonBackgroundPointerOver` / `ButtonBackgroundPressed` 注入；禁用态背景透明、前景仍用当前前景色。

禁用/降透明：

- `AddPrimary(..., onClick:null)` 与 `AddMore(..., onClick:null)`：`IsEnabled=false; Opacity=0.4`。
- 撤销/重做：can=false → `Opacity=0.35`，can=true → `1`。
- 手势按钮：`Opacity = (multiTouch || !IsEnableTwoFingerGesture) ? 0.5 : 1`。
- 手势行 1–3（双指移动/缩放/旋转）：`Opacity = multiTouch ? 0.5 : 1`；第 0 行“多指书写”不受影响。

### 4.7 可见性 / 设置门控

`RefreshSettingsVisuals()` 读取 `Settings.Appearance`：

| JSON 键 | 效果 |
|---|---|
| `isEnableDisPlayFloatBarText` | 所有主栏/手势按钮的 `ShowLabel`（更多面板始终显示文字，不受此键影响） |
| `isShowEraserButton` | 两个橡皮按钮 Visible/Collapsed |
| `isShowExitButton` | 更多面板“退出”按钮 Visible/Collapsed |
| `isShowHideControlButton` | “隐藏控制”按钮 Visible/Collapsed |
| `isShowLRSwitchButton` | “左右切换”按钮 Visible/Collapsed（但按钮本身仍禁用） |
| `isShowModeFingerToggleSwitch` | 手势卡的显示门之一 |
| `isColorfulViewboxFloatingBar` | 所有卡片/更多弹窗背景换 `FloatBarColorful` 渐变；浮条 Opacity 见 4.6 |
| `isTransparentButtonBackground` | 按钮底透明 vs 20/255 的轻微着色 |

手势卡可见性 `RefreshGestureBorderVisibility()`：

```
show = !ClickThroughActive
    && !_pptMode
    && !_mainCollapsed
    && Cfg.Appearance.IsShowModeFingerToggleSwitch
    && _mainHost.Visibility == Visible
```

主题：`ApplyThemeBrushes()` 由 `ActualTheme` 决定 `_theme`：Dark→2，Light→1，Default→`Application.Current.RequestedTheme`（Dark→2，否则 1）。重新计算全部 brush 后对四张卡、拖手图标、分隔线、徽标、手势图标/文字、PPT 矢量、全部按钮应用；最后触发 `ThemeRefreshed`（更多/手势弹窗的 chrome 跟随）。

### 4.8 折叠 / 收起

- `_mainCollapsed`：笑脸或“隐藏控制”调用 `ToggleMainCollapsed()`，`_mainHost.Visibility` 取反；收起时 `ClosePanels()`；没有 `LayoutChanged`，宿主通过 `CountVisibleItems()` 原地重排。手势卡因为要求 `_mainHost` 可见而随之隐藏；拖手始终保留。
- 折叠到屏幕边缘（`Shell.ToggleFold()`）是 App 层行为，Controls 只提供：
  - `SetFoldedVisual(true)`：`ClosePanels()` + 把折叠按钮 tooltip 改为 `展开工具栏`；`false` 改回 `收纳工具栏到屏幕侧边`。
  - `FloatingBarWindow.SetFolded()`（消费方）：隐藏浮条窗口；在工作区垂直居中创建左右两个 `FoldedBarHandleWindow`，各 **30×50 DIP**，仅露出 **14 DIP**：左窗 x = work.X-(30-14)，右窗 x = work.X+work.Width-14。
  - 折叠把手视觉：背景 `ARGB(185,235,238,240)`，边框 `ARGB(100,80,95,100)` 宽 1，左侧圆角 `(0,8,8,0)`、右侧 `(8,0,0,8)`，图标 `Pen` U+E70F 字号 16，左边把手图标右对齐、右边把手图标左对齐，边距 3；按钮可访问名 `展开工具栏`；点击回到 `Shell.ToggleFold()`。
- PPT 模式下手势卡隐藏、浮条整体降透明度；退出牌点击后 `Shell.PptExit()`。

### 4.9 面板（更多设置 / 手势选项）

`FloatingBar` 只创建内容并抛 `PanelRequested(BarPanelKind, bool)`；宿主 `BarPanelWindow` 用独立窗口承载，标题带由宿主绘制。

更多设置 `_moreRows`（纵向 StackPanel，Spacing=0，每行 `MoreRow()`：横向、高度 62.5、居中；第一行 `Margin=(0, 5.5556, 0, 0)`）：

| 行 | 按钮（标签 / Glyph） | 尺寸 |
|---|---|---|
| 1 | 倒计时 `TimerWpf` U+EA8F；随机抽选 `Rand` U+E8B1；单次抽选 `Rand` + 徽标 | 每个 62.5×62.5，图标 30.556，文字 13.889 |
| 2 | 保存墨迹 `SaveStrokes` U+E74E；打开墨迹 `OpenStrokes` U+ED25；播放墨迹 `ReplayWpf` U+EC57 | 同上 |
| 3 | 截屏 `Camera` U+E722；指南 `GuideWpf` U+E82D；设置 `Settings` U+E713；退出 `ExitWpf` U+E7E8 | 同上 |

- 更多按钮 `ShowLabel=true` 始终显示，不受 `isEnableDisPlayFloatBarText` 影响。
- Tooltip = 标签文本。
- 点击任一项后关闭面板（`_moreOpen=false` + `PanelRequested(More,false)`），对应 WPF `HideSubPanels`。
- 单次抽选徽标：直径 `9*S=12.5` 的圆，圆角 `4.5*S=6.25`，边框 1.389，中心文字 `1` 字号 `7*S=9.722`，Margin `(-2.778, 0, 0, -2.778)`，水平居中、底部对齐；边框/文字用前景色。

手势选项 `_gestureRows`（纵向，Spacing=-3.4722；每行 34.7222 高、横向 Spacing=11.1111、居中）：

| 标签 | Glyph | 设置键 | 控件 |
|---|---|---|---|
| 多指书写 | `People` U+E716 | `gesture.isEnableMultiTouchMode` | ToggleSwitch |
| 双指移动 | `GestureTranslate` U+E7C2 | `gesture.isEnableTwoFingerTranslate` | ToggleSwitch |
| 双指缩放 | `GestureZoom` U+E740 | `gesture.isEnableTwoFingerZoom` | ToggleSwitch |
| 双指旋转 | `GestureRotate` U+E7AD | `gesture.isEnableTwoFingerRotation` | ToggleSwitch |

- 行内：图标字号 `16*S=22.222`、宽高 `20*S=27.778`；文字字号 `12*S=16.667`；ToggleSwitch `Width=42*S=58.3333`、`MinWidth=0`、On/OffContent 为空。
- Toggled：写 setter；`Controller.MultiTouchEnabled = Gesture.IsEnableMultiTouchMode`；`RefreshGestureDim()`。
- **源码事实/缺口**：`setter` 只改内存中的 `AppSettings.Gesture`，没有调用 `SaveSettings()`；双指移动/缩放/旋转也没有在 Toggled 里调用 `controller.ConfigureGestures(...)`（只有 Notify 重读或设置窗口提交时才 `ApplySettings`）。`IsEnableMultiTouchMode` 会即时更新 `MultiTouchEnabled`。移植时需决定是否补齐持久化与 live 应用。

宿主 `BarPanelWindow` 的 UI 度量（消费方，用于一致性）：标题 `FontSize = (isMore?12:10)*S = 16.667 / 13.889`、粗体；标题带 `Margin=(-S,-S,-S,0)`、`Padding=(15S,5S)`、边框 S、圆角 `(5S,5S,0,0)`；外卡边框 S、圆角 5S；背景 `ColorfulBackground ? FloatBarColorful : FloatBarBackground(theme)`；标题底 `FloatBarTitleBackground`。退化测量回退尺寸：更多面板 256×236 DIP，手势面板 180×165 DIP。弹窗放在工具锚点上方 4*S=5.556 DIP 间隙处。

### 4.10 点击路由

宿主 `FloatingBarWindow` 在根 `PointerPressed` 记录 source/position，`PointerReleased` 时若位移 ≤10 DIP 调用 `Bar.TryHandleTap(source)`：

- 命中 `_dragHandle`（VisualTree 向上找）→ `ToggleMainCollapsed()`，返回 true；
- 命中 `_pptExitBorder` → `Shell.PptExit()`，返回 true。

按钮自己处理 Click，不走这条路由。

### 4.11 曲目：`FloatingBar.ShapeCatalog`

静态数组，24 项，顺序 = WPF `BorderDrawShape` 行优先；5 行的 Kind/标签顺序与 `ShapeMenuCatalog.Rows` 完全一致：

```
Line 直线 / DashedLine 虚线 / DotLine 点线 / Arrow 箭头 / ParallelLine 平行线
CoordinateBoth 坐标系（双向） / CoordinateXOneWay 坐标系（x 单向） /
CoordinateYOneWay 坐标系（y 单向） / CoordinateFirstQuadrant 坐标系（第一象限） / Coordinate3D 空间坐标系
RectangleFromCenter 中心矩形 / Circle 圆 / DashedCircle 虚线圆 /
EllipseFromCenter 中心椭圆 / EllipseWithFoci 椭圆（含焦点）
Hyperbola 双曲线 / HyperbolaWithFoci 双曲线（含焦点） / ParabolaUp 抛物线（上开口） /
ParabolaWithFoci 抛物线（含焦点） / ParabolaRight 抛物线（右开口）
Rectangle 矩形 / Cylinder 圆柱 / Cone 圆锥 / Cuboid 长方体
```

`ShapeKind.Ellipse`（包围盒椭圆）与上游 Tetrahedron 在菜单中被折叠/缺失，故不列出。

---

## 5. `Controls/PenPalette.cs` — 笔/荧光笔调色盘

### 5.1 两种模式与顺序

`PenPalette(bool highlighter=false)`：

- 普通笔（“墨迹选项”）：**笔锋 → 粗细 → 5×2 色板**。
- 荧光笔（“荧光笔”）：**不透明度 → 3×2 色板 → 粗细 → 透明度可用性说明**。
- 面板 `StackPanel.Spacing = 10`。
- 外层：`.card Width = DesignWidthDip`，圆角 5，边框 1，背景 `FloatBarBackground(1)`（构造时浅色，随后 `ApplyChrome(theme)` 覆盖）。
- 标题带（自绘，无系统 caption）：`Margin=(-1,-1,-1,0)`、`Padding=(15,5,15,5)`、`BorderThickness=1`、`CornerRadius=(5,5,0,0)`、文字字号 12 粗体，`Foreground` 随主题黑/白；文本 `墨迹选项` / `荧光笔`。
- 标题带下 `Border{Padding=12}` 包住面板。

### 5.2 固定尺寸

| 常量 | 值 (DIP) |
|---|---:|
| `SwatchSize` | 40 |
| `SwatchCell` | 44（= 40 + 2×2 margin） |
| `RowHeight` | 32 |
| 设计宽度（笔） | 248 |
| 设计宽度（荧光笔） | 208 |
| 设计高度（笔） | 225 |
| 设计高度（荧光笔，未接 alpha） | 267 |
| 设计高度（荧光笔，已接 alpha） | 225 |

`DesignHeightDip` 的精确公式：

```
27 + 2 + 24 + 2*32 + 2*10 + 44*2
  + (highlighter && alpha note visible ? 10 + 32 : 0)
= 225 + (highlighter && note ? 42 : 0)
```

说明：27 = 标题带（12pt 粗体约 16 行高 + 上下 padding 10 + 边框 2 - 上 margin 1）；2 = 外边框；24 = 内 padding；2×32 = 两行控件；2×10 = 两个面板间距；44×2 = 两行色板。荧光笔多一行说明时再加 10 间距 + 32 两行 12pt 文本。

### 5.3 行/控件度量

- `RowGrid()`：`MinHeight=32`；两列：Auto + `1*`。
- 标签 `TextBlock`：字号 14，右边距 10，垂直居中，前景随主题。
- 数值读数 `TextBlock`：`MinWidth=32`，右对齐，前景随主题。
- Slider 横向拉伸，默认 WinUI 外观；`StepFrequency` 见下。
- 颜色网格：`Grid` 水平居中；列宽 = `SwatchCell=44`，行高 44；每格按钮 40×40、Margin 2、无内边距、透明底、无边框。
- 圆形色块：边框 1.5、圆角 20（=40/2）；方形色块：边框 1、圆角 4。
- 选中勾 `FontIcon`：Glyph `\uE73E`，字号 16，白色；色名为“白色”时用黑色勾；未选中 Collapsed。
- 主题格（仅普通笔，第 0 格）：40×40、透明底、边框 1.5、圆角 20；内部纵向 StackPanel：`FontIcon` 字号 14 + `TextBlock` 字号 9；当前浅色方案时 Glyph `\uE708`、文字 `暗系`；当前深色方案时 Glyph `\uE706`、文字 `亮系`（显示“将要切换到的目标色系”）。
- 透明度说明 `TextBlock`：字号 12、自动换行；未接 alpha 时文案 `透明度尚未接入笔迹模型，因此不可调整。`；接入后文案 `原菜单范围 20–240，步长 20。`，Visibility 切换。

### 5.4 数值范围与换算（权威在 `OriginalMenuCatalog`）

```
PenWidthMinimum = 1
PenWidthMaximum = 30
PenWidthStep    = 1
ToStoredPenWidth(menu) = menu / 2        // 0.5 – 15，步长 0.5
ToMenuPenWidth(stored) = stored * 2

HighlighterAlphaMinimum = 20
HighlighterAlphaMaximum = 240
HighlighterAlphaStep    = 20
默认 HighlighterAlpha = 140
```

滑动吸附公式（`Snap`）：

```
snapped = minimum + round((value - minimum) / step, MidpointRounding.AwayFromZero) * step
```

宽度：若 `|snapped - e.NewValue| > 0.001`，把 slider 值设为 snapped 并 return（避免递归）；否则显示 `snapped.ToString("0")`，把 `Controller.CurrentAttributes.BaseWidth = (float)stored`，依次调用 `InkWidthChanged(stored)`、`AttributesChanged()`。

透明度：snapped 为 int；`_highlighterAlpha=snapped`；读数用整数；`InkAlphaChanged(snapped)`；`AttributesChanged()`。

笔锋：`ComboBox` 项 `点集笔锋`、`速率笔锋`、`关闭笔锋`（索引 0–2）。选择映射：

| 索引 | 文本 | `TaperKind` |
|---:|---|---|
| 0 | 点集笔锋 | `Tail` |
| 1 | 速率笔锋 | `Velocity` |
| 2 | 关闭笔锋 | `None` |

回调顺序：`Controller.CurrentAttributes.TaperKind = ...`；`InkStyleChanged(index)`；`AttributesChanged()`。

颜色点击：`CurrentAttributes.Color = GetSwatchColor(swatch)`；`AttributesChanged()`；`RefreshFromController()`；`RequestClose()`（WPF `ColorSwitchCheck → HideSubPanels("color")`，选色后关闭调色盘；改宽度/笔锋/透明度不关闭）。

### 5.5 色板（`OriginalMenuCatalog`，深色/浅色两套）

普通笔 9 色（名称 / DarkTheme / LightTheme）：

| 名称 | Dark RGB | Light RGB |
|---|---|---|
| 黑色 | 0,0,0 | 0,0,0 |
| 白色 | 255,255,255 | 255,255,255 |
| 红色 | 220,38,38 | 239,68,68 |
| 粉色 | 147,51,234 | 236,72,153 |
| 橙色 | 234,88,12 | 249,115,22 |
| 黄色 | 234,179,8 | 250,204,21 |
| 绿色 | 22,163,74 | 34,197,94 |
| 青色 | 13,148,136 | 20,184,166 |
| 蓝色 | 37,99,235 | 59,130,246 |

荧光笔 6 色（两套主题相同）：

| 名称 | RGB |
|---|---|
| 荧光笔红色 | 220,38,38 |
| 荧光笔橙色 | 234,88,12 |
| 荧光笔黄色 | 234,179,8 |
| 荧光笔青色 | 13,148,136 |
| 荧光笔蓝色 | 37,99,235 |
| 荧光笔紫色 | 147,51,234 |

色值均为不透明（A=255）。`LightColorScheme` 为 true 时用 `LightTheme`，否则用 `DarkTheme`；`ShellController.OpenPenPalette` 用 `Settings.Appearance.Theme == 1` 初始化它（严格等于浅色主题，不是“跟随系统时也按浅色”）。

### 5.6 Chrome 与主题

`ApplyChrome(theme)`：

- `_card.Background = FloatBarBackground(theme)`；`_card.BorderBrush = FloatBarBorder(theme)`。
- `_band.Background = FloatBarTitleBackground(theme)`；`_band.BorderBrush = FloatBarBorder(theme)`。
- 前景：深色 White / 浅色 Black。
- 色块边框：深色 `ARGB(153,255,255,255)`，浅色 `ARGB(204,0,0,0)`；已创建色块的 face 立即更新。
- 刷新主题格。
- 所有色块 face 在 `RefreshFromController()` 中按当前方案重算背景，勾选只比较 `R/G/B`（忽略 alpha）。

### 5.7 公开 API 与可访问性

公开成员：`DesignWidthDip`、`DesignHeightDip`、`ApplyChrome(int)`、`CardSurface`、`Controller`、`RequestClose`、`InkWidthChanged`、`InkStyleChanged`、`InkAlphaChanged`、`HighlighterAlpha`、`AttributesChanged`、`LightColorScheme`、`RefreshFromController()`。

`HighlighterAlpha` setter 先 clamp 到 20–240，再按 20 吸附；仅当 `InkAlphaChanged != null` 时更新 UI，否则直接返回（因此未接线时 slider 保持禁用）。

可访问性字符串（必须保留，除非 HarmonyOS 有无障碍替代）：

- 笔锋 ComboBox：Name `笔迹风格`，HelpText `点集笔锋、速率笔锋、关闭笔锋`。
- 粗细 Slider：Name `画笔粗细，范围 1 到 30，步长 1`。
- 透明度 Slider：Name `荧光笔透明度，范围 20 到 240，步长 20`；未接线时 `暂不可用：荧光笔透明度尚未接入笔迹模型`。
- 每个色块：Name = 色名，HelpText `选择此墨迹颜色`。
- 主题格：Name `切换亮系/暗系墨迹颜色`。

宿主 `PenPaletteWindow` 把卡片放进 `Viewbox`，`Width/Height = design * scale`；scale = 当前 bar/board 的 50–200%。自检覆盖 50/100/200。

---

## 6. `Controls/TouchIconButton.cs` — 触摸图标按钮

### 6.1 默认契约

| 属性 | 默认值 |
|---|---|
| `MinWidth` / `MinHeight` | **40 / 40**（触摸契约） |
| `Padding` | 8 |
| `CornerRadius`（Button 模板） | 8 |
| `BorderThickness` | 0 |
| `Background` | 透明 |
| `Horizontal/VerticalAlignment` | Center |
| `Horizontal/VerticalContentAlignment` | Center |
| 图标 `FontIcon` | Segoe Fluent Icons，`FontSize=20`，居中 |
| 内容 Border | 圆角 4，Stretch |
| 文字 | `FontSize=11`，居中，Margin `(0,-1,0,0)`，默认可见 |

浮条会覆写为 41.667×44.444（`MinWidth=0/MinHeight=0`）；因为实际宽高仍 ≥40，维度契约满足。ArkTS 侧建议保留 40×40 的**命中区**，视觉尺寸可小于命中区。

### 6.2 公开面

依赖属性：`IsSelected`（bool，变更回调重刷视觉）、`Glyph`（string，变更回调写 `FontIcon.Glyph`）。

普通属性/方法：

- `IconSize`（写 `_icon.FontSize`）
- `IconFont`（覆盖字体）
- `Label`（有值时图标与文字纵向 StackPanel；null=纯图标）
- `ShowLabel`
- `LabelFontSize`
- `IconAreaHeight`（NaN=自然测量；>0 时给图标一个固定高 Grid，使文字显隐不改变图标中心）
- `LabelMargin`
- `ContentForeground`（非空时覆盖 token 前景）
- `SelectionBackground`（非空时选中洗色画在内容 Border）
- `SelectionCornerRadius`
- `HoverBackground` / `PressedBackground`
- `SetContent(UIElement)`（组合图标/矢量图）
- `AddFontIcon(FontIcon)`（把额外图标纳入前景刷新）
- `UseMousePointerIcon()`（矢量鼠标指针）
- `RefreshVisual()`
- `ApplyHoverResources(Brush foreground)`

### 6.3 内容构建

`RebuildContent()`：

- body = 自定义内容 ?? `_icon`。
- 显式从旧容器移除 body（WinUI 未根化时 Parent 不可靠）：`_lastIconArea`、`_lastStack`、`_contentBorder.Child`。
- 无 Label：内容 Border 直接放 body。
- 有 Label：纵向 StackPanel；若 `IconAreaHeight>0`，先放固定高 Grid（body 居中），再放 TextBlock；否则直接 body + TextBlock。整体居中。

### 6.4 视觉状态

`ApplyVisualState()`：

```
fg = ContentForeground ?? Resources[IsSelected ? "IcAccentBrush" : "IcTextPrimaryBrush"]
所有 _icons 和 label 的 Foreground = fg

if SelectionBackground != null:
    _contentBorder.Background = IsSelected ? SelectionBackground : Transparent
    // 按钮 Background 保持外部设定（透明或着色）
else:
    Background = IsSelected ? IcAccentSoftBrush : Transparent
```

`ApplyHoverResources(fg)` 注入：

```
ButtonBackgroundPointerOver = HoverBackground（若有）
ButtonBackgroundPressed     = PressedBackground（若有）
ButtonBackgroundDisabled    = Transparent
ButtonForegroundPointerOver/Pressed/Disabled = fg
```

按下反馈：`PointerPressed` 时若 `Motion.AnimationsEnabled` 则 `Motion.Pulse(this, 0.92f)`。

### 6.5 矢量鼠标指针

`UseMousePointerIcon()`（当前无调用方，但 API 被保留）：

- PathFigure 起点 (3,2)，闭合、填充；折线点依次：
  `(3,22) (8,17) (12,23) (15,21) (11,15) (19,15)`，回到 (3,2)。
- `Path`：白色填充、`StrokeThickness=1.6`、`PenLineJoin.Round`、24×24、居中。
- 外层 Grid 24×24，通过 `SetContent` 替换图标。
- 注意：`_customIconOutline.Stroke = fg`，但 Fill 未被重刷为 fg（构造时固定 White），主题切换时会保留白色填充；移植时按需要决定填充是否跟随前景。

---

## 7. `Controls/Glyphs.cs` — 全部图标码点

字体：默认 `ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons`；`HighlightWpf` 在浮条里显式改用 `ms-appx:///Assets/SegoeMDL2Assets.ttf#Segoe MDL2 Assets`。`Shapes` 是 `Segoe UI Symbol` 的几何字符 `△` U+25B3，常量 `ShapesFont = "Segoe UI Symbol"`，使用时要设置 `IconFont`。

| 常量 | 码点 | 使用位置/备注 |
|---|---|---|
| `Pen` | U+E70F | 折叠把手（App） |
| `Highlight` | U+E7E6 | 暂无 Controls 调用 |
| `Eraser` | U+E75C | 浮条两个橡皮底图 |
| `Select` | U+E8B3 | 暂无 Controls 调用 |
| `Shapes` | U+25B3 | `ShapesFont` 配套；暂无 Controls 调用 |
| `Undo` | U+E7A7 | 浮条撤销 |
| `Redo` | U+E7A6 | 浮条重做 |
| `Delete` | U+E74D | 浮条清空、清并鼠覆盖 |
| `Add` | U+E710 | 暂无 |
| `Settings` | U+E713 | 更多“设置” |
| `Camera` | U+E722 | 更多“截屏” |
| `Stopwatch` | U+E916 | 暂无 |
| `People` | U+E716 | 手势“多指书写” |
| `Board` | U+E7F4 | 暂无 |
| `ChevronUp` | U+E70E | 暂无 |
| `ChevronDown` | U+E70D | 暂无 |
| `ChevronLeft` | U+E76B | 暂无 |
| `ChevronRight` | U+E76C | 暂无 |
| `Lock` | U+E72E | 暂无 |
| `Unlock` | U+E785 | 暂无 |
| `Close` | U+E711 | 回放关闭 |
| `Exit` | U+E8BB | 暂无 |
| `Play` | U+E768 | 回放播放 |
| `Pause` | U+E769 | 回放暂停 |
| `Restart` | U+E72C | 回放重播 |
| `Replay` | U+E823 | 暂无 |
| `Help` | U+E897 | 暂无 |
| `Info` | U+E946 | 暂无 |
| `Download` | U+E896 | 暂无 |
| `Cursor` | U+E8B0 | 浮条“鼠标”、清并鼠底图 |
| `PenWpf` | U+EE56 | 浮条“批注” |
| `HighlightWpf` | U+E7E6（源码写作 `"\uE7E6"` 转义） | 浮条“荧光笔” |
| `EraserOverlayCircle` | U+F0B6 | 圆形擦覆盖 |
| `EraserOverlayStroke` | U+F128 | 墨迹擦覆盖 |
| `SelectWpf` | U+E958 | 浮条“选择” |
| `SelectOverlay` | U+F8AF | 选择覆盖 |
| `ShapesWpf` | U+F158 | 浮条“图形” |
| `InsertMedia` | U+EE71 | 插入图片/视频（同一 glyph） |
| `BoardWpf` | U+E929 | 浮条“画板” |
| `Tools` | U+E8A9 | 浮条“工具” |
| `FoldWpf` | U+ED1A | 浮条“隐藏” |
| `Emoji` | U+E76E | 拖手笑脸 |
| `EmojiDrag` | U+E899 | 暂无 |
| `Gesture` | U+F271 | 手势卡 |
| `TimerWpf` | U+EA8F | 更多“倒计时” |
| `Rand` | U+E8B1 | 更多“随机抽选”“单次抽选” |
| `SaveStrokes` | U+E74E | 更多“保存墨迹” |
| `OpenStrokes` | U+ED25 | 更多“打开墨迹” |
| `ReplayWpf` | U+EC57 | 更多“播放墨迹” |
| `GuideWpf` | U+E82D | 更多“指南” |
| `ExitWpf` | U+E7E8 | 更多“退出” |
| `HideControl` | U+E7B3 | 浮条“隐藏控制” |
| `LRSwitch` | U+E8AB | 浮条“左右切换” |
| `GestureTranslate` | U+E7C2 | 手势“双指移动” |
| `GestureZoom` | U+E740 | 手势“双指缩放” |
| `GestureRotate` | U+E7AD | 手势“双指旋转” |

另有硬编码 glyph：

| 位置 | 码点 | 含义 |
|---|---|---|
| `PenPalette` 勾选 | U+E73E | CheckMark |
| `PenPalette` 主题格 | U+E706 / U+E708 | 亮系 / 暗系 |
| `ShapePreview` 图钉 | U+E718 / U+E77A | 自动隐藏 / 固定 |

字体文件位于 App 层：`src/InkCanvas.App/Assets/SegoeFluentIcons.ttf`、`SegoeMDL2Assets.ttf`（git status 显示为未跟踪新增）。Controls 假设字体已由 App 以 `ms-appx:///` 提供。

---

## 8. `Controls/CountdownRing.cs` — 计时圆环

设计坐标：**34×34**，半径 14，线宽 1.5，圆头端点。控件自适应拉伸，实际缩放 `scale = min(width,height)/34`。

颜色（硬编码，未走 `PopupBrushes`）：

| 用途 | 值 |
|---|---|
| 轨道 `TrackColor` | `#FFEAEAEA`（234,234,234） |
| 进行中 `ActiveColor` | `#FF0067C1`（0,103,193） |
| 暂停 `PausedColor` | `#FF1A71C8`（26,113,200） |

绘制算法（Win2D，角度约定：0° 在 3 点方向、顺时针为正、y 向下）：

```
size = min(width,height); if (size <= 0) return
scale = size / 34
center = (width/2, height/2)
radius = 14 * scale
thickness = 1.5 * scale
caps = { StartCap: Round, EndCap: Round }

DrawCircle(center, radius, track #EAEAEA, thickness, caps)
if (Value <= 0) return                       // 0 时只留轨道
sweep = Value * 360
if (sweep >= 360) sweep = 359.99             // 避免退化整圆

start = (center.X, center.Y - radius)        // 12 点
endAngleRad = (-90 + sweep) * π / 180
end = (center.X + radius*cos(endAngleRad), center.Y + radius*sin(endAngleRad))

BeginFigure(start)
AddArc(end, radius, radius, 0,
       SweepDirection.Clockwise,
       sweep > 180 ? ArcSize.Large : ArcSize.Small)
EndFigure(Open)
DrawGeometry(arc, IsPaused ? #1A71C8 : #0067C1, thickness, caps)
```

属性：`Value`（double，clamp 0–1，每次 set 调 `Invalidate()`）；`IsPaused`（bool，同上）。

消费方 `CountdownWindow` 固定 `Width=150, Height=150`，因此实际 `scale=4.4118`、半径 61.76 DIP、线宽 6.62 DIP。设计坐标的 34 是 WPF 原始尺寸，不是屏幕尺寸。

移植注意：Win2D 的 `AddArc` 需要起点、半径、旋转角、sweep 方向、arc size；ArkUI Canvas 的 `arc(cx,cy,r,startAngle,endAngle,anticlockwise)` 可直接用 `-π/2` 到 `-π/2 + sweep`，但 `lineCap` 需设为 `round`，且 0 时不要画 arc。深色主题是否改用 `#3A3A3A` 需先决策（见 §3 不一致项）。

---

## 9. `Animation/Motion.cs` — 动画参数

### 9.1 时长

| 名称 | 启用动画 | 关闭动画 |
|---|---:|---:|
| `Fast` | 140 ms | 0 |
| `Normal` | 240 ms | 0 |
| `Slow` | 380 ms | 0 |

`AnimationsEnabled` 读 `Windows.UI.ViewManagement.UISettings().AnimationsEnabled`；缓存 5 秒（`DateTime.UtcNow`），异常时回退 `true`。关闭系统动画后所有时长归零，调用方不需要逐个判断。

### 9.2 `EnableImplicitScale(element)`

- 取 `ElementCompositionPreview.GetElementVisual(fe)`；`CenterPoint = (ActualWidth/2, ActualHeight/2, 0)`。
- 关闭动画时直接 return（仍设置了 CenterPoint）。
- 创建 `Vector3KeyFrameAnimation`：keyframe 1.0 → `Vector3.One`，`Duration=Normal`，Target `"Scale"`；放入 `ImplicitAnimationCollection["Scale"]`。

### 9.3 `PlayEntrance(element, offsetPx = 12)`

- 关闭动画/元素为 null 时 return。
- `SetIsTranslationEnabled(element, true)`；取 visual。
- 透明度：0 → 1，时长 Normal。
- 平移：`(0, offsetPx, 0)` → `(0,0,0)`，时长 Normal，Target `"Translation"`。
- 同时 `StartAnimation("Opacity")` 与 `("Translation")`。
- 调用点偏移：`FloatingBar` 16、`FloatingBarWindow` 16、`BoardWindow` 20、`CountdownWindow` 10、`LotteryWindow` 10、`InfoWindow` 10、`YesOrNoWindow` 10、`ReplayWindow` 16。

### 9.4 `Pulse(element, to = 1.08f)`

- 关闭动画/ null 时 return。
- CenterPoint = 元素实际中心。
- 第一段 spring：`FinalValue=(to,to,1)`，`DampingRatio=0.55`，`Period=60ms`，Target `"Scale"`。
- 第二段 spring：`FinalValue=(1,1,1)`，`DampingRatio=0.7`，`Period=80ms`，`DelayTime=120ms`，Target `"Scale"`。
- `TouchIconButton` 按下用 `to=0.92`；`PptNavigationWindow` 按压也用 `0.92`。
- 两段都调用 `StartAnimation("Scale", ...)`；第二段带 120ms 延迟，负责回弹。

### 9.5 ArkTS 映射要点

- `animateTo({ duration: 240, curve: Curve.EaseOut }, ...)` 对应 XAML keyframe 线性近似；若需要同等回弹，用 `curves.springMotion` / `springCurve`，但参数语义（`response`/`dampingFraction` 与 `Period`/`DampingRatio`）不是一一对应，需要视觉回归。
- 关闭动画应读取 HarmonyOS 无障碍“减少动态效果”设置，而不是硬编码。
- Composition 的 `CenterPoint` 在 ArkUI 中对应 `.scale({centerX, centerY})` 或 `transform` 原点。

---

## 10. `Controls/InkSurface.cs` — 白板/回放墨迹表面

用途：不透明窗口（白板、回放）里的墨迹表面。透明桌面批注层不能用它（WinUI 3 窗口始终不透明），那一侧使用 `OverlayInkPresenter`。

### 10.1 结构

- 内容是一个 Win2D `CanvasControl`，`ClearColor = ARGB(0,0,0,0)`，Draw 事件绑定。
- `ManipulationMode = None`（手势交给 `InkController`）。
- 事件：`PointerPressed/Moved/Released/Canceled`、`PointerCaptureLost`。

### 10.2 Controller 重绑

```
if old controller:
  对每个 active pointer 调 old.OnPointerUp(sample)
  activePointers.Clear()
  解绑 old.Invalidated / old.Document.Changed
controller = value
_scene?.Dispose()
_scene = new InkSceneRenderer()
if new: 绑定 Invalidated / Document.Changed
_canvas.Invalidate()
```

重绑时先解旧再建新，确保旧文档不会驱动新表面的重绘。

### 10.3 绘制

```
if controller==null || scene==null: return
controller.ViewCenter = (ActualWidth/2, ActualHeight/2)
scene.Draw(args.DrawingSession, controller)
```

`Document.Changed` → `scene.Strokes.InvalidateAll()` + `_canvas.Invalidate()`；`Invalidated` → `_canvas.Invalidate()`。延迟契约（源码注释）：XAML 指针事件在 UI 线程同步进入 controller，`Invalidate()` 排到下一次合成，Win2D 会合并重复 invalidate；无 dispatcher hop、无定时器、无空闲重绘。

### 10.4 指针翻译

`Translate(PointerRoutedEventArgs)` 取相对本控件的点：

| 字段 | 来源 |
|---|---|
| id | `e.Pointer.PointerId` |
| x,y | `point.Position.X/Y` |
| pressure | `props.Pressure <= 0 ? 0.5f : props.Pressure` |
| contactWidth/Height | `props.ContactRect.Width/Height` |
| kind | Pen/Touch/Mouse（`Microsoft.UI.Input.PointerDeviceType`） |
| timestamp | `(uint)point.Timestamp / 1000` |

`OverlayPointer` 构造参数顺序：`(id, x, y, pressure, contactWidth, contactHeight, kind, timestamp)`。

事件流程：

- Pressed：`CapturePointer`；写入 `_activePointers[id]`；`controller.OnPointerDown(sample)`；`Handled=true`。
- Moved：翻译；若 id 不在 active 中则忽略；更新；`OnPointerMove`。
- Released：`Remove(id)` 成功才继续；`OnPointerUp(Translate(e))`；`ReleasePointerCapture`；`Handled=true`。
- Canceled / CaptureLost：`Remove(id, out last)` 成功则 `OnPointerUp(last)`（capture-lost 可能已无当前点，且会跟在正常 up 之后）。

`ClearColor` 属性直接转发 `CanvasControl.ClearColor`，宿主决定白板底色。

---

## 11. `Controls/ShapePreview.cs` — 图形菜单

### 11.1 目录（`ShapeMenuCatalog`）

标题：`图形(第一行支持长按保持选中)`（ASCII 括号）。

5 行，行高 50，行间距 `{14,15,10,10,10}`，行间 StackPanel Spacing = `-8`（负重叠）。单元格内容为矢量预览 `Viewbox`，高度 = 50 - 上 margin - 下 margin；`SlotWidth>0` 时固定宽；`Rotation=90` 时以 (0.5,0.5) 为中心旋转。`Ellipse`（包围盒椭圆）与 Tetrahedron 被上游折叠，未列出。

| 行 | 项（ShapeKind / 标签 / IconKey / Margin / SlotWidth / Rotation / LongPress） |
|---|---|
| 1 | Line/直线/Line/(16,12,0,12)/0/0/true；DashedLine/虚线/DashedLine/(0,12,0,12)/0/0/true；DotLine/点线/DotLine/(0,12,0,12)/0/0/true；Arrow/箭头/ArrowLine/(0,12,0,12)/0/0/true；ParallelLine/平行线/ParallelLine/(0,11.5,16,11.5)/0/0/true |
| 2 | CoordinateBoth/坐标系（双向）/Coordinate1/(16,12,0,12)；CoordinateXOneWay/坐标系（x 单向）/Coordinate2/(0,12,0,12)；CoordinateYOneWay/坐标系（y 单向）/Coordinate3/(0,12,0,12)；CoordinateFirstQuadrant/坐标系（第一象限）/Coordinate4/(0,12,0,12)；Coordinate3D/空间坐标系/Coordinate5/(-1,11.5,-1,11.5) |
| 3 | RectangleFromCenter/中心矩形/RectangleCenter/(16,11,2,11)；Circle/圆/Circle/(0,10,0,10)；DashedCircle/虚线圆/DashedCircle/(0,10,0,10)；EllipseFromCenter/中心椭圆/EllipseCenter/(0,10,0,10)/30；EllipseWithFoci/椭圆（含焦点）/EllipseCenterWithFocalPoint/(0,10,0,10)/30 |
| 4 | Hyperbola/双曲线/Hyperbola/(16,10,2,10)/26；HyperbolaWithFoci/双曲线（含焦点）/HyperbolaWithFocalPoint/(4,10,2,10)/26；ParabolaUp/抛物线（上开口）/Parabola/(2.5,10,2,10)/26；ParabolaWithFoci/抛物线（含焦点）/ParabolaWithFocalPoint/(1,10,2,10)/26/90；ParabolaRight/抛物线（右开口）/Parabola/(2,10,2,10)/26/90 |
| 5 | Rectangle/矩形/Rectangle/(15,11,2,11)；Cylinder/圆柱/Cylinder/(6,10,2,10)；Cone/圆锥/Cone/(8,12.5,0,10)；Cuboid/长方体/Cuboid/(5.5,12.5,2.5,10) |

### 11.2 外观

- 外框：背景 `FloatBarBackground(theme)`；但“画板模式 + 深色”用 `ARGB(191,0,0,0)`；边框 `FloatBarBorder(theme)` 宽 1，圆角 5。
- 标题带：`Margin=(-1,-1,-1,0)`、`Padding=(15,5,0,5)`、边框 1、圆角 `(5,5,0,0)`；背景浅色 `ARGB(64,88,139,252)` / 深色 `ARGB(128,33,86,201)`；标题字号屏幕 10 / 画板 14，粗体。
- 图钉按钮：28×24，透明底、无边框、无 padding；图标字号屏幕 16 / 画板 18；Margin 屏幕 `(22,-1,0,-1)` / 画板 `(16,0,0,0)`；Tooltip `自动隐藏：选择图形后关闭菜单（点击固定）`；Glyph `E718`（自动隐藏）/`E77A`（固定）；默认 `AutoHide=true`。
- 内容 Viewbox：屏幕宽 200、画板宽 240；画板顶部 Margin 10；`Stretch.Uniform`。

### 11.3 交互

- 每格 `Grid` 透明底，`Tag=ShapeMenuItem`，`AutomationProperties.Name` 与 Tooltip 都设为标签。
- PointerPressed：鼠标只接受左键；`CapturePointer`；`_pressedCell=cell`；`_holdFired=false`；若该行支持长按，启动 500ms `DispatcherQueueTimer`；`Handled=true`。
- 500ms 到：`_holdFired=true`；`Opacity` 在 **100ms** 内从 1 淡到 0.3；`Controller.ShapeKind=kind`；`Tool=Shape`；不发 SelectionMade（等释放）。
- PointerReleased：若仍是同一格；停止计时器；释放捕获；若 `_holdFired` → 恢复 `Opacity=1`，`SelectionMade(kind, sticky:true)`；否则设置 kind/tool，`SelectionMade(kind, sticky:false)`。
- Cancel：停表、`Opacity=1`、清状态。
- `SelectItem(index)`：程序化选择，设置 kind/tool，`SelectionMade(kind, false)`。
- `AutoHide` setter：切换 glyph 并触发 `AutoHideChanged`，宿主据此持久化。
- 宿主 `ShapePickerWindow` 用 `ShapePreview` 作为内容；自检 `CheckPopupLayout` 的 `shapes` 分支检查卡片适配。

### 11.4 矢量预览几何（`ShapeIcons`）

统一规则：`L()` 画线，默认 `StrokeThickness=50`、圆头、`Miter` join；`E()` 画椭圆（宽=rx*2、高=ry*2）；`P()` 用 XAML Path Data + 平移；`Dash()` 为 StrokeDashArray。所有图标使用同一个前景 brush。

下面是每个 ShapeKind 的**精确几何**（坐标单位与笔宽均来自源码；下游按此绘制即可，不要在 ArkTS 侧四舍五入）：

| Kind | Canvas (W×H) | 图元 |
|---|---|---|
| Line | 750.75×750.75 | L(25,725.75 → 725.75,25) |
| DashedLine | 750.75×750.75 | L(25,725.75→60.36,690.39)；L(130.36,620.39→655.39,95.36, dash 1.98/1.98)；L(690.39,60.36→725.75,25) |
| DotLine | 749.84×749.84 | L(30,719.84→719.84,30, pen 60, dash 0.0166666666666667/2) |
| Arrow | 753.64×751.14 | L(25,726.14→725.75,25.39)；L(725.62,25→375.38,132.77)；L(615.38,375.77→728.64,25.47) |
| ParallelLine | 755.38×698.08 | L(25.42,25→726.79,25)；L(25,241.08→726.38,241.08)；L(29,673.08→730.38,673.08)；L(25,457.08→726.38,457.08) |
| CoordinateBoth | 813.58×813.58 | `CoordX(y=457.61)` + `CoordY(x=357.61)` |
| CoordinateXOneWay | 813.58×813.58 | `CoordX(457.61)` + `CoordY(157.61)` |
| CoordinateYOneWay | 813.58×813.58 | `CoordX(657.61)` + `CoordY(357.61)` |
| CoordinateFirstQuadrant | 813.58×813.58 | `CoordX(657.61)` + `CoordY(157.61)` |
| Coordinate3D | 796.54×798.42 | 9 条 L： (290.48,508.88→770.01,508.88)；(770.6,508.42→601.29,418.79)；(600.18,597.33→771.54,509.71)；(290.3,509.06→290.3,26.53)；(289.84,25.94→200.21,195.25)；(378.75,196.36→291.13,25)；(290.39,508.97→26.67,772.68)；(26.58,773.42→209.68,717.08)；(84.21,590.05→25,773.18) |
| Rectangle | 753.21×750.96 | `RectEdges`: L(25,25→725,25.5)；L(25,725.25→25,25.25)；L(728.21,725.96→728.21,25.96)；L(26,725.25→726,725.75) |
| RectangleFromCenter | 753.21×750.96 | RectEdges + L(376.56,375.53→376.66,375.43, pen 150) |
| Ellipse | 825×825 | E(cx 412.5, cy 412.5, rx 387.5, ry 387.5) |
| Circle | 825×825 | 同上 E + L(412.45,412.55→412.55,412.45, pen 150) |
| DashedCircle | 825×825 | E(..., dash 2.029/2.029) + 同心点 |
| EllipseFromCenter | 825×649.3 | E(412.5,324.65,387.5,299.65) + 中心点 pen150 |
| EllipseWithFoci | 825×649.3 | 同上 + 焦点 L(212.45,324.7→212.55,324.6, pen75)；L(612.45,324.7→612.55,324.6, pen75) |
| Hyperbola / HyperbolaWithFoci | 777.7×772.73 | 4 条 Path，pen 57，平移 (-111.15,-113.63)；见下方路径 1–4；含焦点时另加 pen85 点 (108.8,386.42) 与 (668.8,386.42) |
| ParabolaUp / ParabolaRight / ParabolaWithFoci | 684.27×635.2 | 2 条 Path，pen 50，平移 (-157.87,-182.4)；见下方路径 5–6；含焦点时加 pen75 点 (342.08,417.65) |
| Cylinder | 579.5×850 | E(290,125,264.5,100)；`BottomArcs(-210,-75)`；L(25,125→25,725)；L(554,125→554,725) |
| Cone | 579.5×750 | `BottomArcs(-210,-175)`；L(290,25→554,625)；L(25,625→290,25) |
| Cuboid | 801.77×798.2 | 8 条外棱：L(25.23,224.28→572.99,224.67)；L(25.23,772.23→25.23,224.47)；L(575.5,772.79→575.5,225.03)；L(26.01,772.23→573.77,772.62)；L(25,225→225,25)；L(575.75,225.25→775.75,25.25)；L(224.5,25.5→775.5,25.5)；L(576.77,773.2→776.77,573.2)；L(775.75,25.5→776.25,573.5)。隐藏棱：L(223.77,573.08→223.77,523.08)；L(223.77,433.53→223.77,120.1, dash 1.791/1.791)；L(223.77,75.32→223.77,25.32)；L(25.77,772.2→61.12,736.85)；L(104.22,693.75→168.86,629.1, dash 1.219/1.219)；L(190.41,607.56→225.77,572.2)；L(223.89,572.01→273.89,572.04)；L(363.44,572.11→676.87,572.33, dash 1.791/1.791)；L(721.65,572.36→771.65,572.4) |
| default | 32×32 | 空画布 |

辅助函数：

```
CoordX(y): L(25,y→787.05,y)；L(787.64,y-0.46→618.33,y-90.09)；L(617.22,y+88.45→788.58,y+0.83)
CoordY(x): L(x,788.58→x,26.53)；L(x-0.46,25.94→x-90.09,195.25)；L(x+88.45,196.36→x+0.83,25)
```

Path 数据（逐字，ArkTS 可直接放到 SVG `path d`）：

1. `M136.54,861.37A395.1,395.1,0,0,0,216.54,814.37C254.61,785.46,277.79,756.9,284.54,748.37A351.08,351.08,0,0,0,330.54,672.37C345.4,638.21,350.64,610.37,355.54,583.37A523.22,523.22,0,0,0,363.85,500.5`
2. `M136.15,138.63A395.1,395.1,0,0,1,216.15,185.63C254.22,214.54,277.41,243.1,284.15,251.63A350.65,350.65,0,0,1,330.15,327.63C345.01,361.79,350.25,389.63,355.15,416.63A522,522,0,0,1,363.46,499.5`
3. `M863.46,861.37A395.1,395.1,0,0,1,783.46,814.37C745.39,785.46,722.21,756.9,715.46,748.37A351.08,351.08,0,0,1,669.46,672.37C654.6,638.21,649.36,610.37,644.46,583.37A523.22,523.22,0,0,1,636.15,500.5`
4. `M863.85,138.63A395.1,395.1,0,0,0,783.85,185.63C745.78,214.54,722.59,243.1,715.85,251.63A350.65,350.65,0,0,0,669.85,327.63C654.99,361.79,649.75,389.63,644.85,416.63A522,522,0,0,0,636.54,499.5`
5. `M182.87,207.4C191.75,296.78 210.54,423.16 252.87,553.03 260.15,575.33 290.28,665.09 352.09,727.67 378.68,754.59 401.88,767.11 413.96,772.86A203.34,203.34,0,0,0,496.84,792.4`
6. `M817.13,207.6C808.25,296.98 789.46,423.36 747.13,553.23 739.85,575.53 709.72,665.29 647.91,727.87 621.32,754.79 598.12,767.31 586.04,773.06A203.34,203.34,0,0,1,503.16,792.6`
7. `M764.5,800C764.5,855.23 646.08,900 500,900 353.92,900 235.5,855.23 235.5,800`
8. `M235.5,800C235.5,785,244.17,770.87,259.72,758.14`
9. `M350.75,717.43C393.23,706.43 444.63,700 500,700 578.18,700 648.44,712.82 696.87,733.21`
10. `M740.28,758.14C755.83,770.87,764.5,785.05,764.5,800`

（7–10 是 `BottomArcs` 的四个路径，pen 50；9 带 dash `2.0084/2.0084`。）

所有 Path 的 `StrokeStartLineCap/EndLineCap=Round`、`StrokeLineJoin=Miter`；带 dash 时 `StrokeDashCap=Round`。

---

## 12. 公开类型与消费方

| 公开类型 | 关键公开成员 | 消费方（源码） |
|---|---|---|
| `IShellActions` | 19 个动作：ToggleBoard/OpenSettings/OpenTimer/OpenLottery/OpenLotteryQuick/SaveStrokes/OpenStrokes/OpenReplay/OpenGuide/OpenPenPalette/OpenShapePicker/ScreenshotAsync/ToggleFold/ToggleClickThrough/PptNext/PptPrevious/PptExit/PptOpenNavigation/Exit | `ShellController` 实现；`FloatingBar.Shell` |
| `BarPanelKind` | `More`, `Gesture` | `FloatingBar.PanelRequested`、`FloatingBarWindow`、`BarPanelWindow` |
| `FloatingBar` | `WpfScale`、`Settings`、`PanelRequested`、`ThemeRefreshed`、`Controller`、`LayoutChanged`、`Shell`、`IsExpanded`、`IsMainCollapsed`、`NotifyPanelClosed`、`Theme`、`ColorfulBackground`、`CreateMorePanelContent`、`CreateGesturePanelContent`、`SetFoldedVisual`、`PrimaryChildCount`、`ComputeLayoutSize`、`ChromeSurfaces`、`GetToolButton`、`GetPanelButton`、`CountVisibleItems`、`ComputedPrimaryWidth`、`PrimaryRowActualWidth`、`SecondaryRowActualWidth`、`ClickThroughActive`、`Build`、`RefreshSelection`、`RefreshSettingsVisuals`、`TryHandleTap`、`ShapeCatalog` | `FloatingBarWindow`、`BarPanelWindow`、`ShellController`、`SelfTest`、`FoldedBarHandleWindow`（间接） |
| `PenPalette` | 见 §5.7 | `PenPaletteWindow`、`SelfTest.CheckPaletteLayout` |
| `TouchIconButton` | 见 §6.2 | `FloatingBar`、`ReplayWindow`、`PptNavigationWindow`（按压反馈契约）、`SelfTest` |
| `Glyphs` | 全部 const | `FloatingBar`、`ReplayWindow`、App 各视图 |
| `CountdownRing` | `Value`、`IsPaused` | `CountdownWindow`（150×150） |
| `InkSurface` | `Controller`、`ClearColor` | `BoardWindow`、`ReplayWindow` |
| `ShapeMenuItem` | `Kind`、`Label`、`IconKey`、`Margin`、`SlotWidth`、`LongPress`、`Rotation` | `ShapeMenuCatalog`、`ShapePreview` |
| `ShapeMenuCatalog` | `Title`、`Rows`、`RowSpacings` | `ShapePreview` |
| `ShapePreview` | `SelectionMade`、`AutoHideChanged`、`AutoHide`、`SelectItem` | `ShapePickerWindow`、`SelfTest` |
| `Motion` | `AnimationsEnabled`、`Fast`、`Normal`、`Slow`、`EnableImplicitScale`、`PlayEntrance`、`Pulse` | FloatingBar、FloatingBarWindow、BoardWindow、CountdownWindow、LotteryWindow、InfoWindow、YesOrNoWindow、ReplayWindow、SettingsWindow、PptNavigationWindow |
| `PopupBrushes` | 见 §3 | FloatingBar、PenPalette、ShapePreview、BarPanelWindow、其余 App 弹窗 |
| `OriginalMenuCatalog`（Core） | `Swatch`、`PenColors`、`HighlighterColors`、`PenStyles`、宽度/alpha 上下限、`ToStoredPenWidth`、`ToMenuPenWidth` | `PenPalette`、`AppServices`、`SettingsWindow` |

---

## 13. JSON / 数据格式

Controls 层**不直接读写** `.icink` 或 `Settings.json`；它消费内存中的 `AppSettings` 与 `InkController`。相关键名（两侧必须一致）：

| 顶层 | 键 | 类型/默认 | 使用 |
|---|---|---|---|
| `appearance` | `isEnableDisPlayFloatBarText` | bool / false | 浮条文字 |
| `appearance` | `isColorfulViewboxFloatingBar` | bool / false | 彩色渐变 |
| `appearance` | `enableViewboxFloatingBarScaleTransform` | double / 100 | 浮条/调色盘缩放 |
| `appearance` | `enableViewboxBlackBoardScaleTransform` | double / 100 | 画板缩放（画板模式下 palette 跟随） |
| `appearance` | `floatingBarBottomMargin` | double / 100 | 浮条底部边距 |
| `appearance` | `isTransparentButtonBackground` | bool / true | 按钮底透明 |
| `appearance` | `isShowExitButton` | bool / true | 更多“退出” |
| `appearance` | `isShowEraserButton` | bool / true | 两个橡皮 |
| `appearance` | `isShowHideControlButton` | bool / false | 隐藏控制按钮 |
| `appearance` | `isShowLRSwitchButton` | bool / false | 左右切换按钮 |
| `appearance` | `isShowModeFingerToggleSwitch` | bool / true | 手势卡 |
| `appearance` | `theme` | int / 0（0 跟随、1 浅、2 深） | palette 主题、`LightColorScheme=theme==1` |
| `canvas` | `inkWidth` | double / 2.5 | 持久化笔宽（=菜单值/2） |
| `canvas` | `inkAlpha` | int / 140 | 荧光笔 alpha |
| `canvas` | `inkStyle` | int / 0 | 0 点集、1 速率、2 关闭 |
| `canvas` | `eraserSize` | int / 2 | 0–4 |
| `canvas` | `eraserType` | int / 0 | 0 图标切换、1 面积、2 线条 |
| `canvas` | `usingWhiteboard` | bool / false | 画板主题选择 |
| `gesture` | `isEnableMultiTouchMode` | bool / true | 手势开关/控制器 |
| `gesture` | `isEnableTwoFingerZoom` | bool / true | 手势开关 |
| `gesture` | `isEnableTwoFingerTranslate` | bool / true | 手势开关 |
| `gesture` | `isEnableTwoFingerRotation` | bool / false | 手势开关 |
| `gesture` | `AutoSwitchTwoFingerGesture` | bool / true | 未在 Controls 读取 |
| `gesture` | `isEnableTwoFingerRotationOnSelection` | bool / false | 选区旋转 |
| `gesture` | `matrixTransformCenterPoint` | enum | 坐标变换中心 |

`AppSettings` 的默认值来自 `new AppSettings()`；`FloatingBar.Settings == null` 时用静态 `_defaultSettings`。移植时必须保持这些默认值（尤其 `isShowEraserButton=true`、`isTransparentButtonBackground=true`、`isShowModeFingerToggleSwitch=true`、`gesture.isEnableMultiTouchMode=true`）。

---

## 14. 测试与验证现状

**没有单元测试直接钉住 Controls。** `tests/InkCanvas.Core.Tests/` 只测 Core/Ink 的几何、撤销、设置等；`DomainTests.cs:75` 的 `EveryShapeKindProducesGeometry` 覆盖全部 `ShapeKind`，间接保证 `ShapeCatalog` 中的形状枚举都可由 `ShapeGenerator` 生成，但不校验菜单顺序/标签/度量。

现有“行为钉住”是 App 层自检 `src/InkCanvas.App/Diagnostics/SelfTest.cs`（诊断，不是 `dotnet test`）：

- `CheckFloatingBarLayout`：标签显示/隐藏 × 缩放 `{50,100,200}`，检查每个 `TouchIconButton` 的实际包围盒在窗口内（±1），并检查 `ChromeSurfaces` 的原生窗口 region 只覆盖四张卡；这钉住了卡间距/窗口裁剪契约。
- `CheckPaletteLayout`：主题 `{1,2}` × highlighter `{false,true}` × 缩放 `{50,100,200}`；检查 palette 卡片适配、每个色块 face 在按钮内且 `button.ActualWidth + margin ≤ 44.5`、region 适配、锚点位置误差 ≤1 DIP。
- `CheckPopupLayout`：缩放 × `{more, gesture, shapes}`；检查更多/手势/图形弹窗卡片适配与 region。
- `CheckUnfoldHandles`：两侧折叠把手 Invoke 后 1.2s（跨维护轮询）仍展开。
- `CheckTapInk`：墨迹点击提交/撤销/重做（Ink 层，不是 Controls）。
- `CheckPopupOrder`：维护轮询后候选弹窗仍在浮条之上（Z-order）。

这些自检在正常启动时才会运行；本轮**未运行**，因此不能声称通过。`parity/` 目录当前不存在（`manifest.json`、`vectors/*.json`、`run-vectors.mjs` 均无），Controls 也没有任何 `@parity` 锚点（全仓 `@parity` 计数为 0）。这是移植基础设施缺口，见开放问题。

---

## 15. 逐功能移植分类

| id | 功能 | 分类 | 说明 |
|---|---|---|---|
| `floating-bar-layout` | 浮条度量/布局计算 | **pure-logic** | S=50/36、ComputeLayoutSize/MeasureRow/CountVisibleItems 公式可 1:1 写成 ArkTS 纯函数并用黄金向量钉住 |
| `floating-bar-ui` | 浮条四卡/按钮/选中/主题 | **adapt** | 结构、顺序、glyph、尺寸可 1:1；容器换 ArkUI Row/Column/Stack，窗口换 HarmonyOS 浮窗 |
| `floating-bar-panels` | 更多设置/手势面板 | **adapt** | 行/按钮/开关度量 1:1；独立窗口/弹层机制需适配 |
| `floating-bar-visibility` | 设置门控/穿透/PPT 透明度 | **pure-logic** | 全部是布尔/透明度公式，建议单独抽函数 |
| `pen-palette` | 笔/荧光笔调色盘 UI | **adapt** | Slider/Select/Grid 换 ArkUI；颜色、范围、吸附 1:1 |
| `pen-palette-catalog` | 色板/范围/笔锋目录 | **pure-logic** | `OriginalMenuCatalog` 可直接作为共享数据/向量 |
| `touch-icon-button` | ≥40 触摸图标按钮 | **adapt** | Button 语义、选中洗色、按下 pulse 需 ArkUI 复刻；尺寸契约纯逻辑 |
| `glyph-codepoints` | Segoe 图标码点 | **platform-bound** | PUA 码点依赖 Segoe Fluent/MDL2 字体，HarmonyOS 无同名字体；需 SymbolGlyph 映射或 SVG 资源，不能直接复用字体 |
| `countdown-ring` | 计时圆环 | **adapt** | 34/14/1.5、360/359.99、颜色为纯常量；Win2D → Canvas arc |
| `motion` | 动画时长/spring/entrance | **adapt** | 时长与参数可复用；Composition 隐式动画/spring 需换 ArkUI animateTo/curves |
| `theme-tokens` | 浅/深色 token | **pure-logic**（数据） | 颜色/尺寸值可完全 1:1 进 `tokens.json`；ThemeResource 解析机制本身需适配 |
| `popup-brushes` | 弹窗/浮条调色板 | **pure-logic**（数据） | 所有 ARGB/渐变值可 1:1；返回 Brush 的 API 换 ResourceColor/字符串 |
| `ink-surface` | 白板/回放墨迹表面 | **adapt** | 指针→OverlayPointer 的翻译逻辑可 1:1；CanvasControl 换 ArkUI XComponent/Canvas，压力/时间戳字段需按 HarmonyOS 触摸事件重映射 |
| `shape-menu-catalog` | 图形目录/行列度量 | **pure-logic** | 24 项、行距、margin、slot、rotation、longpress 全部可向量化 |
| `shape-icons` | 24 个矢量预览 | **adapt** | 几何路径完全可 1:1（SVG path/ArkUI Path）；XamlReader/Path 换 ArkUI Shape |
| `shape-preview` | 图形菜单交互/长按/固定 | **adapt** | 500ms/100ms/0.3 等常量 1:1；pointer/timer/pin UI 需适配 |
| `ppt-exit-plate` | PPT 退出矢量牌 | **adapt** | 32×30 画布、24×16 轮廓、X path、4 个矩形可 1:1；点击走宿主 tap 路由 |
| `folded-bar-handle` | 折叠把手 | **adapt** | 30×50/14 露出、圆角、颜色、Pen glyph 可 1:1；窗口创建需 HarmonyOS 子窗口/浮窗 |
| `bar-popup-window` | 更多/手势独立弹窗窗口 | **adapt** | 标题带/尺寸/锚点 1:1；独立窗口→overlay/子窗口，外部点击/Escape 语义需重做 |
| `bar-acrylic-transparency` | 浮条半透明/桌面透出 | **drop-candidate / platform-bound** | WinUI 用 `DesktopAcrylicBackdrop` + DWM，HarmonyOS 无等价桌面穿透；建议改用 `backgroundBlurStyle` + 半透明色，或根据设备形态放弃“看穿桌面” |
| `accessibility-labels` | 中文无障碍名/提示 | **pure-logic**（字符串） | 文案可 1:1；API 换 ArkUI accessibility |

---

## 16. HarmonyOS 移植建议与坑

1. **尺寸单位**：DIP→vp 可视为 1:1。S=1.3889 必须保留，不能把浮条按钮直接当 40/44 设计；WPF Viewbox 的先乘 S、后乘用户 scale 的两级缩放要保留，否则 100% 时与 WPF 不一致。
2. **四张独立卡**：HarmonyOS 若用一个全屏透明浮窗，卡间空隙需要真正透明（不能画背景），否则“独立圆角矩形”会变成一张长条。每个卡片单独 `borderRadius` + `backgroundColor`，容器透明。
3. **半透明**：`DesktopAcrylicBackdrop` 无直接等价；`backgroundBlurStyle(BlurStyle.Thin)` 只作用于组件后方内容，桌面透出在 HarmonyOS 浮窗上可能不可用。建议把 `FloatBarBackground` 的 0.75/0.6 alpha 作为基线，Acrylic 作为可选增强；这是本层最大的视觉差异来源。
4. **图标**：所有 PUA 码点（U+E7xx/U+Exxx/U+Fxxx）依赖 Segoe 字体，HarmonyOS 设备不保证有。优先逐个映射到 `SymbolGlyph($r('sys.symbol.…'))`；没有对应系统符号的（EraserOverlayCircle/Stroke、SelectOverlay、Gesture F271、ShapesWpf F158）用内置 SVG。不要把 .ttf 直接打进 HAP 当作长期方案（授权/字形覆盖率/多设备不一致）。
5. **弹出菜单**：WPF/WinUI 用独立 HWND 窗口保证“打开菜单不改变浮条尺寸”。HarmonyOS 可用 `bindPopup`、`CustomDialog` 或 `WindowStage.createSubWindow`；若用 `bindPopup`，注意它默认锚定组件并可能被屏幕边缘裁剪，需复刻 `BarPanelWindow` 的“工作区适配缩放”逻辑（设计尺寸超屏时按 `fit` 缩小）。
6. **ToggleSwitch**：ArkUI `Toggle({type: ToggleType.Switch})` 默认尺寸与 WinUI 不同；必须显式设宽 58.333 vp，并把文字放外部（WinUI 的 OnContent/OffContent 为空）。
7. **Slider 吸附**：ArkUI `Slider` 有 `step`，但吸附取整规则（AwayFromZero）与显示格式化需自己实现，不能依赖框架四舍五入。
8. **Canvas 圆环**：ArkUI Canvas 的 `arc` 角度单位是弧度；`lineCap='round'`；`Value=0` 时只画轨道；`sweep=360` 用 359.99 避免闭合退化。
9. **触摸压力/时间戳**：HarmonyOS `TouchEvent` 的 `pressure` 可能为 0，沿用 `<=0 → 0.5`；时间戳单位需按 API 文档换算成毫秒后传给 `OverlayPointer`，否则 Ink 层的速度笔锋会异常。
10. **动画**：`Pulse` 的 spring 参数无法直接映射；建议先移植“按下缩到 0.92、120ms 后回弹”的视觉结果，再调 `springMotion`。系统“减少动态效果”必须能一键归零。
11. **静态集合**：`FloatingBar` 的 `_boardDividers`、`_badgeBorders`、`_gestureIcons`、`_gestureLabels`、`_pptExitShapes` 是 `static readonly`，跨实例共享；`Build()` 只清前两者，后三者会累积。当前单浮条实例下无影响，ArkTS 侧应改为实例字段，避免未来多窗口复用出现重复引用。
12. **持久化缺口**：手势 Toggle 直接改 `AppSettings` 但不 `SaveSettings`，也不 live `ConfigureGestures`。若 HarmonyOS 侧按源码照搬，会出现“切换双指缩放后本次不生效/重启丢失”的问题；建议在移植时补齐（但这属于行为变更，需要产品确认）。

---

## 17. 开放问题

1. `parity/` 基础设施尚未落地：没有 `manifest.json`、`vectors/*.json`、`tokens.json`；Controls 无 `@parity` 锚点。需要先决定浮条度量/色板/图形目录哪些进入黄金向量，否则 ArkTS 侧只能靠本报告人工对照。
2. HarmonyOS 图标方案未定：`SymbolGlyph` 系统符号与 Segoe 的逐码点映射表由谁维护？无法映射的 PUA（尤其覆盖层图标）是否允许重新设计？
3. `CountdownRing` 深色轨道：源码硬编码 `#EAEAEA`，而 `PopupBrushes.RingTrack(2)=#3A3A3A` 未使用。移植以“WPF 1:1 固定浅灰”还是“跟随深色主题”为准？
4. `ShapePreview` 画板深色背景用 `#BF000000`（191），与浮条深色背景 153 不同；这是有意的 BoardBarBackground 还是历史遗留？移植是否保留两套？
5. 手势面板 Toggle 不保存、不 live 生效：是上游行为还是 3.4.0 遗漏？移植是否补齐 `SaveSettings` + `ConfigureGestures`？
6. 浮条设计高 50 DIP，而 `CLAUDE.md` 写“浮条 55”；55 实际对应画板三浮条（`BoardWindow` 的 55-DIP 条）。`tokens.json` 应采用哪个作为“浮条”基准？
7. 浮条 “插入图片/插入视频/左右切换” 三个按钮在当前桌面实现中是禁用占位。HarmonyOS 平板/2in1 是否要真正实现插入媒体与左右手切换，还是继续保留禁用？
8. 独立弹窗模型：更多/手势/调色盘/图形菜单在 HarmonyOS 是继续用独立子窗口，还是改为同一页面内的浮层？两种方案对“打开菜单不改变浮条尺寸”“点击外部/Escape 关闭”的验收标准不同。
9. `FloatingBar.ShapeCatalog` 与 `ShapeMenuCatalog.Rows` 是两份重复数据（前者只含 Kind+Label，后者含几何度量）。是否需要先合并成单一数据源再移植，避免两侧顺序漂移？
10. `PenPalette` 宽度滑杆设置窗口上限 150%，宿主 clamp 200%，自检用 200%。移植统一到多少？这会影响 4K/2in1 上的浮条最大尺寸与触摸目标。
11. `TouchIconButton.UseMousePointerIcon` 的 Path Fill 固定白色，主题切换后不刷新；这是否算已知缺陷？HarmonyOS 复刻时是否改为跟随前景色？
12. WinUI 的 `ToolTipService` 在 HarmonyOS 的对应物（`bindTips`/`bindPopup`/长按提示）尚未确定；触摸设备上 tooltip 的触发方式需要产品定义。

---

## 附录 A — 逐文件关键行号索引

（路径相对 `D:\InkCanvas`）

- `src/InkCanvas.Controls/Controls/FloatingBar.cs`
  - 常量/S：L54–71
  - 构造/四卡：L97–151
  - Build/按钮顺序：L329–441
  - 组合图标/更多/手势：L454–640
  - 卡片与 PPT 矢量：L645–697
  - 选中/撤销重做/手势可见性：L717–811
  - 设置门控/主题：L817–890
  - 形状目录：L912–943
- `src/InkCanvas.Controls/Controls/PenPalette.cs`
  - 常量：L22–24
  - 构造/顺序：L63–168
  - 尺寸公式：L171–181
  - Chrome：L184–199
  - 色板/主题格：L245–389
  - 范围/回调：L406–526
- `src/InkCanvas.Controls/Controls/TouchIconButton.cs`
  - 默认值：L44–86
  - 内容构建：L224–279
  - 视觉状态/资源：L281–323
- `src/InkCanvas.Controls/Controls/Glyphs.cs`：L8–74
- `src/InkCanvas.Controls/Controls/CountdownRing.cs`：L17–99
- `src/InkCanvas.Controls/Animation/Motion.cs`：L16–118
- `src/InkCanvas.Controls/Themes/Tokens.xaml`：L9–56
- `src/InkCanvas.Controls/Theming/PopupBrushes.cs`：L17–116
- `src/InkCanvas.Controls/Controls/InkSurface.cs`：L19–155
- `src/InkCanvas.Controls/Controls/ShapePreview.cs`
  - 目录：L34–87
  - 矢量几何：L96–407
  - 菜单 UI：L416–578
  - 长按/交互：L580–660
- 消费方：
  - `src/InkCanvas.App/Views/FloatingBarWindow.cs`（尺寸/缩放/折叠/面板）
  - `src/InkCanvas.App/Views/BarPanelWindow.cs`（更多/手势弹窗）
  - `src/InkCanvas.App/Views/FoldedBarHandleWindow.cs`（折叠把手）
  - `src/InkCanvas.App/Views/PickerWindows.cs`（PenPaletteWindow/ShapePickerWindow）
  - `src/InkCanvas.App/Services/ShellController.cs`（OpenPenPalette 接线）
  - `src/InkCanvas.App/Diagnostics/SelfTest.cs`（布局自检）
  - `src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs`（范围/色板）
  - `src/InkCanvas.Core/Settings/AppSettings.cs`（JSON 键）

## 附录 B — 关键默认值速查

```
WpfScale              = 1.3888888889
PartH                 = 50
BtnW × BtnH           = 41.6667 × 44.4444
IconSz / LabelSz      = 22.2222 / 11.1111
PartCorner/Border/Gap = 6.9444 / 1.3889 / 2.7778
MoreBtn / MoreIcon    = 62.5 / 30.5556
PptExitW              = 47.2222
Gesture switch width  = 58.3333
Selected wash         = #801296DB, corner 5.5556
Bar PPT opacity       = 0.75 (0.8 colorful)
Bar normal opacity    = 1.0 (0.95 colorful)
Undo/Redo dim         = 0.35
Disabled button dim   = 0.4

Pen palette width     = 248 (pen) / 208 (highlighter)
Palette height        = 225 (pen); 267 (highlighter, alpha not wired)
Swatch / cell / row   = 40 / 44 / 32
Pen width menu        = 1..30 step 1; stored = /2
Highlighter alpha     = 20..240 step 20; default 140
Taper mapping         = 0 Tail, 1 Velocity, 2 None

Countdown design      = 34×34, r14, stroke 1.5
Countdown colors      = track #EAEAEA, active #0067C1, paused #1A71C8
Countdown max sweep   = 359.99°

Motion fast/normal/slow = 140 / 240 / 380 ms (0 when disabled)
Entrance offset          = 12 default; fade+rise Normal
Pulse press              = 0.92; spring 0.55/60ms, back 0.7/80ms delay 120ms

Touch target min         = 40
Shape row height         = 50, spacing {14,15,10,10,10}, overlap -8
Shape long press         = 500 ms, dim to 0.3 over 100 ms
Shape menu width         = 200 (screen) / 240 (board)
Pin glyph                = E718 (auto-hide) / E77A (pinned)
```
