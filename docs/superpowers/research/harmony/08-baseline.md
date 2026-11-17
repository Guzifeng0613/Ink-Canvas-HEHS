# HarmonyOS 移植基线盘点 08 — 权威功能清单、测试清单与验证边界

日期：2026-11-18
工作树：`feat/winui3-migration`，HEAD `0d26d63` + 未提交的 3.4.0 改动
基线版本：`3.4.0`（`src/InkCanvas.App/InkCanvas.App.csproj` 第 19 行）
范围：`tests/InkCanvas.Core.Tests/` 全部 10 个测试源文件；`docs/PORTING-NOTES.md`、`docs/menu-porting-notes.md`、`docs/board-porting-notes.md`、`docs/ppt-porting-notes.md`、`docs/menu-verification-status.md`、`CHANGELOG.md`、`README.md`。为核对功能面与常量，另行只读引用了 `src/` 与 `docs/superpowers/evidence/runtime-selftest.json`。
验证边界：**本报告是静态源码/文档阅读。没有运行 `build.ps1`，没有运行 124 项单测，没有启动应用，没有触摸/压感、PowerPoint/WPS、音视频或 HarmonyOS 工具链实测。** 文中“已验证”均指项目文档中的声明，不是本次研究复现的结论。

---

## 0. 给下游移植者的 18 条硬结论

1. **基线是 3.4.0 工作树，不是 3.3.0。** `docs/menu-verification-status.md` 与 `docs/PORTING-NOTES.md` 的 3.3.0 段落写“96 项单测未运行 / 只跑 3 项几何测试”；`CHANGELOG.md` 的 3.4.0 段写“124 项单测通过”。当前测试源码确实有 **124 个可执行用例**（100 个 `[Fact]` + 24 行 `[InlineData]`），因此 3.4.0 的 124 是当前分母，3.3.0 的 96 是历史快照。
2. **98% 覆盖率分母建议用本报告第 2 节的 62 个功能单元**，状态三态为：已实现 / 部分实现 / 明确未移植。若按等权顶层功能计，5 项 Windows 专有附加功能占 5/62=8.1%，与设计 G2 的“不移植 ≤2%”冲突；建议把 5 项合并为一个“桌面专有附加 UI/旧格式”单元（1/58≈1.7%），或按第 2 节列出的约 240 个可核查行为子项计。该口径需在 `parity/manifest.json` 定稿前确认（见第 9 节开放问题）。
3. **墨迹宽度只算一次、抬笔不重算。** `InkPoint.Width` 是存储值；`InkStroke.Complete()` 对几何是 no-op；提交前后 `Points[].Width` 与 `StrokeGeometry.BuildOutline` 必须逐元素相等。这是修复“笔锋闪烁”的核心契约，ArkTS 必须保留。
4. **画板与单表面 `.icink` 是两种容器，扩展名相同。** 单表面只有 `Version + Strokes`；画板包顶层有 `Format="inkcanvas-board"`（大小写敏感嗅探）、`Version=1`、`Background`、`GridSpacing`、`Pages[]`。不要把两者混成一种格式。
5. **画板 Stroke 子格式缺 `HighlighterAlpha`**（`BoardService.StrokeFile` 没有该字段），同一笔画经画板保存/加载后荧光笔透明度回落到默认 96；单表面 `InkFileStore` 则有该字段。这是当前真实缺口，移植时要显式决定“保留兼容 bug”还是升版本。
6. **`Settings.json` 顶层 PPT 键名是 `behavior`，不是 `powerPoint`。** 9 个顶层键为 `advanced / appearance / automation / behavior / canvas / gesture / inkToShape / startup / randSettings`。旧 WPF 文件可直接读，因为所有子键名逐字保留（camelCase）。
7. **图形生成必须逐常量移植。** 24 个可见图形（5/5/5/5/4 行）；虚线 dash=5、period=13.8；点线 period=8.28、pressure=0.8；椭圆角向 dash 0.05π、period 0.083π；圆柱/圆锥椭圆短长比 2.646；箭头头 30×10；坐标轴箭头头 20×7、尾部 25；平行线偏移 ±25/±75，角度吸附带 1/12、0.52–0.63、0.92–1.08、1.63–1.95。漏一个常量就会产生可见几何差异。
8. **触摸分档有 4 个输入域相乘**：`IsQuadIR`（取 √(w·h)）、`IsSpecialScreen`、`TouchMultiplier`（为 0 时禁用橡皮）、笔尖/手指各自独立的 `BoundsWidth / Threshold / EraserSize`。默认值：nib 10 / 2.5 / 0.8，finger 30 / 2.5 / 0.8，TouchMultiplier 0.25（推荐值 0.3）。
9. **手掌拒绝没有视觉副作用。** 原 WPF 在检测手掌时把整层墨迹压到 `Opacity=0.1`，这是第二处闪烁源；WinUI 版刻意不移植。`PalmContactThreshold=30`，`PenHasPriority=true`。
10. **画板上限 99 页；网格默认 40 DIP，加载夹取 12..160；背景全板共享**（Black/White/Grid/Lined），不是页属性。页导航：末页点“下一页”会新增页；删除最后一页时清空该页而不是变成 0 页。
11. **PPT 轮询契约存在两个值**：`PptInkSession.RecommendedPollInterval` 为放映中 200ms / 非放映 1000ms，而 `ShellController` 实际非放映用 900ms（初始 900ms）。翻页前截图后有 **5 秒抑制窗口**，防止外部 SlideChange 重复截图；截图条件为墨迹数 **严格大于** `MinimumAutomationStrokeNumber`。
12. **PPT 每页墨迹以稳定 `SlideID` 为键**，不是页码；页码（1-based）只用于 `GotoSlide`；自定义放映的 `ShowPosition` 与 `Slide.Index` 独立保存。
13. **UI 度量必须两侧同值**：浮动栏内容 36 DIP 经 50/36 放大；主按钮 30×32→41.7×44.4 DIP，图标 22.2，标签 11.1；设置面板宽 440 DIP、上下各 150；画板浮条高 55 DIP；PPT 导航面板 40/42 缩放、透明度 0.4；触控目标最小 40 DIP。
14. **图标有两套来源**：`Glyphs`（Segoe Fluent/MDL2，含私用区码点）与 `ShapePreview` 的矢量预览。HarmonyOS 没有同名字体，必须建立“码点→系统 Symbol/自绘”映射表，不能依赖字体回退。
15. **强绑 Windows 的能力层**：分层透明窗口、WM_POINTER、Win2D 预乘 alpha 渲染、全局热键、DWM 圆角、GDI 截图、注册表自启/启动器、PPT/WPS COM、GitHub `.exe` 自动更新。这些在 HarmonyOS 上必须替换或放弃，不能直接翻译 API。
16. **明确未移植**：WPF 通知条 `MW_Notification`、循环进度条 `CycleProcessBar`、窗口位置记忆、旧 `.isf/.icstk/.icart` 读取、屏幕批注层的通用图片/元素对象层。画板层已实现图片/视频，不等于屏幕层元素已移植。
17. **设置页有“如实展示的未接线项”**，故意不做假开关：`DeferredRow` 显示保存值并标注缺口。移植时把这些字段标 `adapted/not-ported`，不要误记为已实现。
18. **验证声明要分层引用**：3.4.0 声称真机 Windows Server 2025/125% DPI 验证了窗口关闭/拖动/模式往返/弹窗锚定/菜单内容/工具栏顺序/半透回退与 124 项单测；3.3.0 及更早的菜单/白板/PPT 行为验证被用户明确跳过。本次研究没有复现任何一项，详见第 7 节。

---

## 1. 基线事实与统计

### 1.1 仓库与构建

| 项 | 值 | 出处 |
|---|---|---|
| 解决方案 | `InkCanvas.slnx` | 根目录 |
| 目标框架 | `net10.0-windows10.0.26100.0` | `src/InkCanvas.App/InkCanvas.App.csproj` |
| 最低平台 | `10.0.17763.0` | App/Test csproj |
| 平台/RID | x64 / win-x64，非打包部署 | `Directory.Build.props`、README |
| Windows App SDK | 1.8.260921001（分包子包） | `Directory.Build.props` |
| Win2D | 1.4.0 | `Directory.Build.props` |
| 版本 | 3.4.0 | `InkCanvas.App.csproj:19` |
| 许可 | GPL-3.0 | README / LICENSE |
| 测试框架 | xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.12.0 | Test csproj |

### 1.2 分层与文件规模（仅源码，不含 bin/obj）

| 层 | 源文件数 | 关键职责 |
|---|---:|---|
| `InkCanvas.Core` | 22 | 纯域模型：墨迹、笔锋、几何、擦除、撤销、图形生成/识别、选区、回放、设置、主题、版本/更新策略、像素合成、日志 |
| `InkCanvas.Platform` | 8 | Win32：分层窗口、指针输入、手掌拒绝、热键、边缘手势、系统/前台/自启、软件启动器、P/Invoke |
| `InkCanvas.Ink` | 6 | InkController、双指手势、Win2D 渲染、覆盖层呈现 |
| `InkCanvas.Controls` | 9 + 1 XAML | 浮动栏、调色盘、图形预览、触控按钮、计时环、主题 token、动画 |
| `InkCanvas.Features` | 8 | 画板、持久化、PPT 模型/会话/文件/COM、计时/抽奖/截图、更新 |
| `InkCanvas.App` | 20 | 启动、服务装配、窗口 chrome、13 个窗口/视图、自检 |

### 1.3 测试统计

| 文件 | `[Fact]` | `[Theory]` | `[InlineData]` | 可执行用例 |
|---|---:|---:|---:|---:|
| `DomainTests.cs` | 16 | 0 | 0 | 16 |
| `PalmRejectorTests.cs` | 9 | 0 | 0 | 9 |
| `PixelCompositingTests.cs` | 7 | 0 | 0 | 7 |
| `ReplayTests.cs` | 13 | 0 | 0 | 13 |
| `SelectionGeometryTests.cs` | 9 | 1 | 4 | 13 |
| `ShapeDrawingSemanticsTests.cs` | 23 | 1 | 2 | 25 |
| `StrokeRegionTests.cs` | 3 | 0 | 0 | 3 |
| `TaperTests.cs` | 6 | 2 | 6 | 12 |
| `UpdatePolicyTests.cs` | 7 | 0 | 0 | 7 |
| `UpdateTests.cs` | 7 | 3 | 12 | 19 |
| **合计** | **100** | **7** | **24** | **124** |

测试项目同时引用 `InkCanvas.Core` 与 `InkCanvas.Platform`，因此 `PalmRejectorTests` 覆盖 Platform 层的纯逻辑类。

### 1.4 公开类型与消费方（关键映射）

| 层 | 公开类型 | 实际消费方 |
|---|---|---|
| Core | `InkStroke`, `InkPoint`, `StrokeAttributes`, `InkColor`, `InkDocument`, `Rect` | `InkController`、`BoardService`、`InkFileStore`、`PptInkSession`、`InkReplaySession`、`ShapeRecognizer`、渲染器 |
| Core | `TaperProfile` / `NoneTaper` / `TailTaper` / `VelocityTaper`, `TaperKind` | `InkStroke` 构造、`RebuildWidths`、`AppServices.ApplySettings` |
| Core | `StrokeGeometry` | `OverlayInkPresenter`、`StrokeRenderer`、`SelfTest`、`TaperTests`、`StrokeRegionTests` |
| Core | `StrokeEraser` | `InkController.EraseAlong`（面积擦） |
| Core | `UndoStack`, `IUndoableAction`, `DelegateAction` | `InkController.History`、`BoardService`（图片/视频/变换） |
| Core | `SelectionGeometry`, `SelectionHandle` | `InkController`（选区命中/拖拽） |
| Core | `ShapeGenerator`, `ShapeKind` | `InkController`（两步图形）、`ShapePreview`（菜单图标与选择）、`ShellController`（Alt+L） |
| Core | `ShapeRecognizer`, `RecognitionResult`, `RecognizedShape` | `InkController.TryRecognize` |
| Core | `TouchContactClassifier`, `TouchDecision`, `TouchIntent` | `PalmRejector.ClassifyContact` |
| Core | `PixelCompositing` | `OverlayInkPresenter`（局部回读合成） |
| Core | `ReplayClock`, `InkReplaySession` | `ReplayWindow`、`SelfTest` |
| Core | `AppSettings` 及 9 个设置段、`OptionalOperation`、`MatrixTransformCenterPointOptions` | `AppServices`、`SettingsWindow`、`ShellController`、`PptInkSession`、`PalmRejector` |
| Core | `OriginalMenuCatalog` | `PenPalette`、`SettingsWindow` |
| Core | `ThemeResolver`, `AppTheme` | `AppServices`、`ShellController.EffectiveTheme` |
| Core | `MatrixTransformCenter` | `InkController.ResolvePivot` |
| Core | `ReleaseFeed`, `ReleaseInfo`, `UpdatePolicy`, `AppVersion` | `UpdateService`、`UpdateCoordinator`、单测 |
| Core | `FileLog`, `LogType` | `ShellController`、`App`、`WindowChrome`、`PptInkSession` 失败回调 |
| Platform | `LayeredInkWindow`, `OverlayPointer`, `OverlayPointerKind` | `OverlayHost`、`OverlayInkPresenter`、`InkController`、`PalmRejector` |
| Platform | `PalmRejector` | `InkController.Palm` |
| Platform | `HotkeyService`, `HotkeyModifiers` | `OverlayHost.RegisterHotkeys` |
| Platform | `EdgeGestureService`, `WindowEnumerator`, `ScreenService`, `ForegroundWindowService`, `AutoStartService`, `SoftwareLauncher` | `ShellController`、`SettingsWindow`、`BoardWindow`、`CountdownWindow`、`LotteryWindow` |
| Platform | `NativeMethods` + `POINT`/`RECT`/`BITMAPINFO`/pointer structs | 几乎所有平台层与 `SelfTest` |
| Ink | `InkController`, `InkTool`, `InkEraserMode` | `ShellController`、`FloatingBar`、`BoardWindow`、`PickerWindows`、`ReplayWindow`、`SelfTest`、`PptInkSession` 回调 |
| Ink | `OverlayInkPresenter`, `InkSceneRenderer`, `StrokeRenderer` | `OverlayHost`、`App` 诊断 |
| Ink | `TwoFingerManipulator` | `InkController` |
| Controls | `FloatingBar`, `IShellActions`, `BarPanelKind` | `FloatingBarWindow`、`ShellController`、所有窗口（动作回调） |
| Controls | `PenPalette` | `PenPaletteWindow` |
| Controls | `ShapePreview`, `ShapeMenuCatalog`, `ShapeMenuItem` | `ShapePickerWindow`、`Shared` 菜单 |
| Controls | `TouchIconButton`, `InkSurface`, `CountdownRing`, `Motion`, `PopupBrushes`, `Glyphs` | 浮条、画板、计时器、所有弹窗 |
| Features | `BoardService`, `BoardBackground`, `BoardImageAsset`, `BoardMediaAsset`, `BoardElementMath`, `BoardImageGeometry`, `BoardMediaGeometry` | `AppServices`、`BoardWindow`、`BoardImageLayer`、`BoardMediaLayer`、`ShellController` |
| Features | `SettingsStore`, `InkFileStore`, `AutoSaveService` | `AppServices`、`ShellController`、`BoardService`、`PptInkFileStore` |
| Features | `PptInteropService`, `PptInkSession`, `PptInkFileStore`, `PptModels` 全部记录/枚举、`IPptInkStore` | `AppServices`、`ShellController`、`PptNavigationWindow`、`SettingsWindow` |
| Features | `CountdownTimer`, `LotteryService`, `ScreenshotService` | 计时器窗口（实际自有逻辑）、抽奖窗口、Shell/PPT 截图 |
| Features | `UpdateService` | `UpdateCoordinator` |
| App | `AppServices`, `ShellController`, `OverlayHost`, `UpdateCoordinator`, `WindowChrome`, `SelfTest`, 13 个 Window | `App.OnLaunched`、`FloatingBar` 回调、启动参数 |

### 1.5 文档与测试口径差异（需要显式记录）

| 文档声明 | 当前工作树 | 处理 |
|---|---|---|
| 3.3.0：全量 96 项单测未运行，仅 3 项几何测试 3/3 通过 | 当前 124 用例 | 96 是 3.3.0 快照，124 是 3.4.0 |
| `PORTING-NOTES.md`：更新 26 项单测 | `UpdateTests.cs` 当前 19 用例 | 以源码 19 为准 |
| `PORTING-NOTES.md`：图形 21 项新几何单测 | `ShapeDrawingSemanticsTests.cs` 当前 25 用例 | 以源码 25 为准 |
| `PORTING-NOTES.md`：选区 13、回放 13、更新 26 | 选区 13、回放 13、更新 19 | 只有更新数不一致 |
| 3.4.0：真机 124 项单测通过 | 本次未运行 | 引用为项目声明 |

---

## 2. 权威功能清单（三态 + 移植分类）

分类定义：
- **pure-logic**：无平台 API 的纯算法/状态机，可 1:1 移植并用共享向量钉住。
- **adapt**：业务逻辑可复用，但 UI/IO/输入需要平台适配。
- **platform-bound**：强绑 Windows/WinUI/Win32/COM/Win2D，HarmonyOS 必须替换实现或放弃。
- **drop-candidate**：建议明确不移植并写明原因。

### 2.1 墨迹与输入（Core / Platform / Ink）

| ID | 功能 | 状态 | 分类 | 关键行为与精确常量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `ink-stroke-model` | 笔画/点/文档数据模型 | 已实现 | pure-logic | `InkPoint(x,y,pressure,width,timeMs)`；默认 pressure 0.5；`Rect` UI 无关；`Bounds` 按最大半宽外扩；`Transform` 宽度按 √|det(M)| 缩放；`DistanceToSegment`；文档 add/insert/remove/clear/hit-test/erase | `Core/Ink/InkStroke.cs`、`InkPoint.cs`、`StrokeAttributes.cs`、`InkColor.cs`、`InkDocument.cs` | DomainTests 的 `TransformMovesStrokesAndScalesWidth`、`EraseRemovesOnlyTouchedStrokes` |
| `ink-taper-causal` | 因果增量笔锋 | 已实现 | pure-logic | `None=0 / Tail=1 / Velocity=2`；Tail：窗口 10 点、入口 4 点、TipRatio 0.2、宽度只增不减；Velocity：`speed=(d1+d2)/20`，speed≥0.25 时 `0.5-0.3*(min(speed,1.5)-0.3)/1.2`，speed<0.05 时 `0.5+0.4*(0.05-speed)/0.05`，ratio 夹 0.15..1.8；抬笔不重算；荧光笔强制 None | `Core/Ink/TaperProfile.cs`、`InkStroke.cs` | TaperTests 12 例；SelfTest `widthsIdentical/outlineIdentical/taperPresent` |
| `ink-stroke-region` | 同向 capsule 轮廓 | 已实现 | pure-logic | 每段生成独立 capsule，圆弧 12 步、单点圆 24 段、半径 `max(width,0.5)/2`；统一正绕向；Winding 合并而非 even-odd；`BuildUnionContours` 返回 `Count-1` 个区域（单点 1 个） | `Core/Ink/StrokeGeometry.cs` | StrokeRegionTests 3 例；TaperTests `OutlineIsDeterministic`、`SinglePointProducesAClosedDot` |
| `ink-erase` | 面积擦 / 线条擦 | 已实现（未验证） | pure-logic | 面积擦 `StrokeEraser.EraseDisc`：切割半径 = 橡皮半径 + 最大半宽，二次方程求交，保留未接触片段并继承属性；线条擦 `InkDocument.EraseAt` 删整笔；`EraserRadius` 默认 16 | `Core/Ink/StrokeEraser.cs`、`InkDocument.cs`、`Ink/InkController.cs` | 无单测；代码注释明说只编译检查 |
| `ink-undo-history` | 撤销/重做 | 已实现 | pure-logic | `UndoStack(capacity=100)`，`InkController.History = new(200)`；Push 清 redo；容量满丢最旧；`DelegateAction` 闭包 | `Core/History/UndoStack.cs`、`Ink/InkController.cs` | DomainTests 3 例；SelfTest 选区缩放/删除撤销 |
| `ink-selection` | 选区命中与变换 | 已实现 | pure-logic | HandleRadius=12；RotateOffset=28；缩放夹 0.05..20；角点绕对角、旋转绕中心、Move 平移；keepAspect 取 max(sx,sy)；退化拖拽不产生 NaN；克隆模式 | `Core/Selection/SelectionGeometry.cs`、`Ink/InkController.cs` | SelectionGeometryTests 13 例；SelfTest 选区缩放/删除 |
| `ink-shape-generator` | 24 预设图形 | 已实现 | pure-logic | 见第 5.2 节完整常量；`ShapeKind` 24 个值；两步长方体/双曲线；回笔、长按保持、克隆 | `Core/Shapes/ShapeGenerator.cs` | ShapeDrawingSemanticsTests 25 例；DomainTests 4 例 |
| `ink-shape-recognition` | 墨迹转图形 | 已实现（判定边界自研） | pure-logic | MinSize=40；点数<8 或弧长<40 → None；闭合距离 `<max(12, 0.18·arc)`；圆残差<0.10 且宽高比 0.82–1.22；椭圆残差<0.14；直线 spread/length≤0.035；RDP epsilon 6；转角>45° 且间距>20，3/4 角 → Triangle/Rectangle | `Core/Recognition/ShapeRecognizer.cs` | DomainTests 5 例 |
| `ink-touch-classifier` | 特殊屏触摸分档 | 已实现（未验证） | pure-logic | `MeasureContactWidth = IsQuadIR ? √(w·h) : w`；`BoundsWidth = nib ? NibModeBoundsWidth : FingerModeBoundsWidth`；`eraserEnabled = TouchMultiplier≠0 || !IsSpecialScreen`；`contact>bounds` 才进入橡皮；`>bounds·threshold` 面积擦，直径=`contact·EraserSize`（特殊屏再乘 TouchMultiplier）；否则 `pptGestureEligible ? GestureOnly : EraseByStroke`；否则 Draw | `Core/Input/TouchContactClassifier.cs` | 无单测；文档明确未验证 |
| `ink-palm-rejection` | 手掌拒绝 | 已实现 | pure-logic | `PalmContactThreshold=30`（取 max(contactWidth,contactHeight)）；`PenHasPriority=true`；手掌不画但可擦；抬笔/抬手/Reset 清状态；**不改墨迹层透明度** | `Platform/Input/PalmRejector.cs` | PalmRejectorTests 9 例 |
| `ink-gestures` | 双指缩放/平移/旋转 | 已实现 | adapt | `MultiTouchEnabled=true` 表示多指**书写**并禁手势（WPF 语义）；仅当 false 时第二指取消触摸墨迹并启动手势；`ConfigureGestures(zoom,translate,rotate)`；枢轴 `CanvasCenterPoint / SelectedElementsCenterPoint / GestureOperationCenterPoint`，未 Loaded 一律画布中心；可作用于选区 | `Ink/InkController.cs`、`Ink/Manipulation/TwoFingerManipulator.cs`、`Core/Ink/MatrixTransformCenter.cs` | 无单测；README 声称 1:1 未验证 |
| `ink-slide-flip` | 放映手指翻页 | 已实现 | adapt | 放映中 + 无墨迹 + 允许手指控制；横向位移 `|dx|>60` 触发上一页/下一页（60 为移植自定，原实现用 iniP 比较） | `Ink/InkController.cs`、`App/Services/ShellController.cs` | 无单测 |
| `ink-overlay-render` | 覆盖层渲染与局部回读 | 已实现（部分真机验证） | platform-bound | 提交层缓存为 `CanvasRenderTarget`；仅回读活动笔迹包围盒并 `PixelCompositing.BlendOver`；整层重建时才 `GetPixelBytes`；`GpuReadbackBytes` 统计；1080p 全帧 8,294,400 B；项目证据：书写 205,778 B/帧 ≈ 2.48%，整轮 10.42% | `Ink/Rendering/OverlayInkPresenter.cs`、`StrokeRenderer.cs`、`InkSceneRenderer.cs` | SelfTest；`evidence/runtime-selftest.json` |
| `ink-layered-window` | 透明分层窗口/指针输入 | 已实现（部分真机验证） | platform-bound | `WS_EX_LAYERED|TOOLWINDOW|TOPMOST|NOACTIVATE`；点击穿透加 `WS_EX_TRANSPARENT`；`UpdateLayeredWindow`；`RegisterTouchWindow` + `SetWindowFeedbackSetting` 抑制接触视觉；`WM_POINTER*` 优先，鼠标可走 `WM_POINTER` 或回退 `WM_MOUSE*` | `Platform/Windows/LayeredInkWindow.cs`、`NativeMethods.cs` | README 五种透明手法实测；未在目标触屏验证 |
| `ink-pixel-compositing` | 预乘 alpha 合成 | 已实现 | pure-logic | `out = src + dst·(1−srcA)` 四通道；alpha=0 跳过、alpha=255 直拷；越界/负偏移裁剪；空源 no-op | `Core/Imaging/PixelCompositing.cs` | PixelCompositingTests 7 例 |
| `ink-replay-logic` | 回放时钟/会话 | 已实现 | pure-logic | `ReplayClock`：默认暂停在 0，Speed 默认 1，Advance 按 Speed 缩放并到端点自动停，Play 完成后重头，Seek 夹取；`InkReplaySession`：默认 12 ms/点，`VisibleAt` 按累计时间揭示，末笔可部分，保留存储宽度 | `Core/Replay/ReplayClock.cs`、`InkReplaySession.cs` | ReplayTests 13 例；SelfTest 单调揭示/完成 |

### 2.2 屏幕批注外壳、浮条、菜单、设置、主题、窗口

| ID | 功能 | 状态 | 分类 | 关键行为与精确度量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `ui-floating-bar` | WPF 1:1 浮动工具栏 | 已实现（部分真机验证） | adapt | 内容 36 DIP，`WpfScale=50/36≈1.3889`；PartH=50、BtnW=41.7、BtnH=44.4、Icon=22.2、Label=11.1、圆角 6.94、边 1.39、间隙 2.78、More 按钮 62.5/图标 30.6/标签 13.9、PPT 退出牌 47.2；笑脸/主栏/手势/PPT 退出牌为独立圆角矩形；主行按钮顺序、glyph、选中 wash、悬停/按下压色逐一对上游；更多/手势为独立弹窗；折叠为侧边把手（露出 ~14 DIP）；设置门控显隐/文字/彩色 | `Controls/Controls/FloatingBar.cs`、`App/Views/FloatingBarWindow.cs`、`FoldedBarHandleWindow.cs`、`BarPanelWindow.cs` | SelfTest 布局/位置/透明/展开/弹窗顺序；3.4.0 真机声明 |
| `ui-pen-palette` | 笔/荧光笔独立调色盘 | 已实现（部分验证） | adapt | 笔 9 色（黑/白/红/粉/橙/黄/绿/青/蓝，深浅主题 RGB 见 5.3）；荧光笔 6 色（红/橙/黄/青/蓝/紫）；笔锋 3 项“点集/速率/关闭”；菜单宽度 1–30 步长 1，持久化值=菜单/2（0.5–15 步长 0.5）；透明度 20–240 步长 20，默认 140；色块 40、格 44、行高 32、设计宽 248/208、圆角 5、标题带 27 | `Controls/Controls/PenPalette.cs`、`Core/Settings/OriginalMenuCatalog.cs`、`App/Views/PickerWindows.cs` | SelfTest palette 适配；headless 声明 |
| `ui-shape-picker` | 所见即所得图形菜单 | 已实现（部分验证） | adapt | 24 个可见图形（椭圆/四面体在原版 Collapsed，不计）；5 行 5/5/5/5/4；行高 50；行距 14/15/10/10/10；纵向堆叠 −8；标题“图形(第一行支持长按保持选中)”；第一行 500ms 长按保持选中；图钉跨开合持久；宽 200（浮条）/240（画板），回退高 234/286；预览为 1:1 矢量坐标 | `Controls/Controls/ShapePreview.cs`、`App/Views/PickerWindows.cs`、`Ink/InkController.cs` | SelfTest 弹窗适配；3.4.0 headless 目视声明 |
| `ui-touch-button` | 触控图标按钮 | 已实现 | adapt | 圆角 8；图标区固定 22 DIP；caption 不位移；UIA 名称；40/48 触控目标 token | `Controls/Controls/TouchIconButton.cs`、`Themes/Tokens.xaml` | SelfTest 按钮适配；3.4.0 真机声明 |
| `ui-theme-tokens` | 主题 token 与弹窗色板 | 已实现 | adapt | Light/Dark 各 9 个颜色 + 刷；Accent #0067C0/#4CC2FF；板黑 #1F3B2C/#16281E；板白 #FAFAFA/#1B1B1B；触控 40/48；图标 22；圆角 8/14；内边距 8,6,8,6；卡片 16,12；弹窗 surface #F0F3F9@0.95 / #1F1F1F@0.95；浮条底白@0.75 / 黑@0.6；标题带 #588BFC@0.25 / #2156C9@0.5；选中 #1296DB@0.5；彩色渐变 #9580B0FF→#95C0FFC0 | `Controls/Themes/Tokens.xaml`、`Controls/Theming/PopupBrushes.cs`、`Core/Theming/ThemeResolver.cs` | 3.4.0 真机/headless 声明 |
| `ui-motion` | 入场/按压动效 | 已实现 | adapt | 系统关闭动画时全部时长归零；Fast 140ms / Normal 240ms / Slow 380ms；入场淡入+上移；按压弹簧阻尼 0.55/周期 60ms，回弹 0.7/80ms 延迟 120ms | `Controls/Animation/Motion.cs` | 无单测 |
| `ui-window-chrome` | 无边框窗口/DWM/DPI/拖动 | 已实现（部分真机验证） | platform-bound | 全辅助窗 Topmost、无边框、不可调整（除计时/抽奖）；DWM 圆角 `DWMWCP_ROUND=2`、非客户区禁用 `DWMNCRP_DISABLED=1`、边框色 `0xFFFFFFFE`；构造期 DPI=0 → 首次 Activated 后延迟一 tick 重放 DIP 记录；1px 往返 resync；拖动 16ms 轮询光标、8 DIP 阈值、fromAnywhere 抢捕获抑制 Click；按圆角区域 `SetWindowRgn` 裁剪；900ms 巡检重压 Z | `App/Services/WindowChrome.cs`、`ChromeBackdrop.cs`、`Platform/Interop/NativeMethods.cs` | 3.4.0 真机声明；Server 2025 DWM 死锁后移除 Acrylic 合成 |
| `ui-hotkeys` | 全局热键 | 已实现（行为未验证） | platform-bound | `RegisterHotKey` + `NoRepeat=0x4000`，id 从 0xB100 递增；Ctrl+Z 撤销、Ctrl+Y 重做、Ctrl+E 清屏、Alt+E 橡皮、Alt+S 选择、Alt+L 直线、Alt+B 画板、Alt+V 折叠、Alt+C 截图、Alt+D 批注/调色盘、Alt+Q 鼠标模式、PageUp/PageDown 翻页、Shift+Esc 退出放映 | `Platform/Input/HotkeyService.cs`、`App/Services/OverlayHost.cs` | 无单测；README 声称 WPF 键位 |
| `ui-settings` | 440 DIP 设置面板 | 已实现（部分验证） | adapt | 宽 440、上下各 150 DIP、最小高 240；分组：启动/画板/手势/墨迹识别/外观/PPT/高级/自动；详细设置头 40、底部 50；重置为推荐值 1:1；静默时间档 00:00–23:40 每 20 分钟；未接线项 DeferredRow 如实显示；3s 通知；0.5s 入场 | `App/Views/SettingsWindow.cs`、`Core/Settings/AppSettings.cs`、`OriginalMenuCatalog.cs` | 3.4.0 headless 目视；无行为单测 |
| `ui-guide-changelog` | 操作指南/更新日志 | 已实现 | adapt | 指南 700×600、最小宽 500、12 行键帽（Ctrl Z/Y/E；Alt C/V/D/Q/B/S/E/L；Shift Esc）；更新日志 600×390、只读窗口 | `App/Views/InfoWindow.cs` | headless 声明 |
| `ui-yes-no` | 是/否通知窗 | 已实现 | adapt | 450×160、Topmost、NoResize、X 关闭不触发动作；900ms keep-above；PPT 三条确认文案逐字保留 | `App/Views/YesOrNoWindow.cs`、`ShellController.cs` | 3.4.0 真机声明 |
| `ui-mouse-mode` | 鼠标穿透模式 | 已实现 | adapt | 切换 `WS_EX_TRANSPARENT`；进入前按设置保存快照/截图；墨迹可见性由 `HideStrokeWhenSelecting` 或 PPT `ShowStrokeOnSelect` 决定；切回绘制不清墨迹 | `App/Services/ShellController.cs`、`OverlayHost.cs`、`LayeredInkWindow.cs` | 3.4.0 真机声明 |
| `ui-startup-args` | 启动参数/自检钩子 | 已实现 | adapt | `--board`、`--passthrough`、`--diag=<path>`（700ms 写输入计数/延迟）、`--open=a,b,c`（1100ms 间隔）、`--shot=<dir>`（窗口激活后 700ms RenderTargetBitmap PNG）、`--selftest=<dir>`（1200ms 后跑自检） | `App/App.xaml.cs`、`Diagnostics/SelfTest.cs` | headless 声明；自检输出见 evidence |
| `ui-selftest` | 运行时自检 | 已实现 | platform-bound | 检查：浮条适配、工具栏位置稳定、backdrop 透光、调色盘/弹窗适配、点按落笔、边缘把手展开、候选弹窗 Z 顺序、提交几何=最后活帧、荧光笔等宽、3D 三轴、选区缩放+撤销、删除+撤销、回放单调完成、边缘手势属性、GPU 回读比例；输出 JSON | `App/Diagnostics/SelfTest.cs` | `evidence/runtime-selftest.json`：159 帧、6 笔、全关键项通过、回读 2.481% |

### 2.3 画板

| ID | 功能 | 状态 | 分类 | 关键行为与精确常量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `board-service` | 分页画板模型 | 已实现 | pure-logic | `MaximumPages=99`；每页独立 `InkController`/文档/撤销/图片/视频；Next 在末页新增；Previous 仅访问已有页；删除最后一页清空该页；GoTo 夹取；页事件 `PageChanged`；`HasContent` | `Features/Board/BoardService.cs` | 无单测；SelfTest `--board` 启动存活 |
| `board-ui` | 三浮条/选区面板/工具 | 已实现（部分验证） | adapt | 全屏；三条 55 DIP 半透浮条（左页导航/中工具/右页镜像）；单元格 60×55，图标 26/28、标签 12；页码指示 58×55；选区面板高 104，含克隆/衍至新页、逆旋/顺旋、水平/垂直翻转、粗细三键（×1.25/×0.8/恢复）、9 色、存为图片、删除；长按 550ms 弹尺寸/纸型/间距；更多 3×3；手势弹窗；BlackboardScale live | `App/Views/BoardWindow.cs`、`BoardImageLayer.cs`、`BoardMediaLayer.cs` | 3.4.0 真机/headless 声明；非逐像素 |
| `board-images` | 图片对象 | 已实现 | adapt | 单图上限 32 MiB；`Data` base64、`ContentType`、DIP 几何、`Transform` 6 浮点 row-major；移动/缩放/旋转/翻转/双指手势合为一个撤销单元；插入居中缩放；删除/克隆 | `BoardService.cs`、`BoardWindow.cs`、`BoardImageLayer.cs` | 无单测 |
| `board-video` | 视频对象 | 已实现（未验证） | adapt | 上限 512 MiB；`ContentType` 必须 `video/*`；默认 360×240；`MediaPlayerElement` + 传输控件；刷新保留播放位置；参与旋转/翻转/手势与页历史 | `BoardService.cs`、`BoardMediaLayer.cs` | 无编解码/实机验证 |
| `board-persistence` | 画板/页保存加载 | 已实现 | pure-logic | 单表面 `.icink`（无图片时）或画板包 `Format/Version/Background/GridSpacing/Pages`；页保存仅当前页；**只有视频没有图片的页会丢 media**；打开页/整板带替换确认；加载 >99 页抛错；`GridSpacing` 夹 12..160；`Background` 全板共享 | `BoardService.cs`、`Features/Persistence/Stores.cs` | 无单测 |
| `board-tools` | 更多功能/外部工具 | 已实现（部分未验证） | adapt | 设置、EasiCamera、Desmos、倒计时、抽奖、回放、截图、指南、插入视频、清屏、删除本页、全屏/窗口；EasiCamera 走 HKLM 卸载注册表，Desmos 打开 `https://www.desmos.com/calculator?lang=zh-CN` | `BoardWindow.cs`、`ShellController.cs`、`Platform/System/SoftwareLauncher.cs` | 外部启动未实机运行 |
| `board-screen-elements` | 屏幕批注层通用图片/元素 | 未移植 | drop-candidate | 原 `MW_ElementsControls.cs` / `Helpers/InkCanvasElementsHelper.cs` 的屏幕层图片/元素插入与变换未实现；画板层图片/视频不能替代 | — | `PORTING-NOTES.md` 明列 |

### 2.4 PPT / WPS

| ID | 功能 | 状态 | 分类 | 关键行为与精确常量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `ppt-com-interop` | PowerPoint/WPS COM 适配 | 已实现（未验证） | platform-bound | `ProgIds = {PowerPoint.Application, KWPP.Application}`；`CLSIDFromProgID` + `GetActiveObject` 晚绑定；必须 STA；`AllowWps=false` 时若 `wpp.exe` 存在拒绝附着；快照含 presentation identity/Name/FullName/HasSavedPath/SlideCount/尺寸/ProgId、SlideId/1-based Index/Name/Hidden/AdvanceOnTime/AdvanceTime、窗口句柄、ShowPosition、ShowState（1 运行/2 暂停/3 黑/4 白/5 结束）；导航 Next/Previous/Exit/GotoSlide(1-based)/ShowSlideNavigationPane；放映启动三模式；隐藏页恢复；计时禁用；断开错误释放 COM | `Features/Ppt/PptInteropService.cs`、`PptModels.cs` | 全部未实机验证 |
| `ppt-ink-session` | PPT 墨迹状态机 | 已实现（未验证） | pure-logic | 以 SlideID 缓存每页墨迹；切换时克隆发出 `InkTransition`；轮询建议 200/1000ms；5s 截图抑制；通知上次页/隐藏页/计时；截图原因 BeforeNavigation/ExternalSlideChange/Explicit；`MinimumScreenshotStrokeNumber` 严格大于才截；不拥有 UI/控制器 | `Features/Ppt/PptInkSession.cs` | 无单测 |
| `ppt-file-store` | 每演示文稿墨迹存档 | 已实现（未验证） | pure-logic | 目录 `Auto Saved - Presentations/<安全名>_<页数>`；manifest `InkCanvas-Ppt.json`（Version=1、LastSlideIndex、LastShowPosition、Slides[{SlideId,SlideIndex,File}]）；每页 `slide-<SlideID>.icink`；旧 `Position` 文件兼容读取；`.icstk/.icart` 只检测不导入不删除；保存时清理旧页文件 | `Features/Ppt/PptInkFileStore.cs` | 无单测 |
| `ppt-navigation-ui` | 四个 PPT 导航面板 | 已实现（headless 部分验证） | adapt | 缩放 S=40/42；单元格 40·S；页码格 38·S；图标盒 26·S、字号 20·S；文字 9·S；分隔线 25·S/线厚 1·S；圆角 6·S；透明度 0.4；glyph EDD5/EDD6（底）与 EDD7/EDD8（侧）；`WS_EX_NOACTIVATE|TOOLWINDOW`；折叠/画板时隐藏 | `App/Views/PptNavigationWindow.cs`、`ShellController.cs` | 3.4.0 headless 目视 |
| `ppt-shell-integration` | 放映模式接线 | 已实现（未验证） | adapt | 放映开始退画板、按设置显示画布、切红笔；结束退画板；翻页先退画板；页码点击进鼠标模式并打开导航窗格；四个可见性设置分别控制箭头面板与页码按钮；使能手指翻页门控；WPS wpp 守卫；PPT 截图写 PNG + 可选 `.icink` | `ShellController.cs`、`FloatingBar.cs`、`PptInkSession.cs` | Office/WPS 实机未验证 |

### 2.5 工具

| ID | 功能 | 状态 | 分类 | 关键行为与精确常量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `tool-countdown` | 倒计时窗口 | 已实现（headless 部分验证） | adapt | 默认 00:01:00；窗口 1100×700，最小 300×200；环 150×150；数字 88.5 高、字号 56；编辑 ±1/±5；结束时间 pill 18 高/圆角 9；紧凑/全屏；结束音 `Assets/TimerDownNotice.wav`；自适应 tick：总时长≤10s→20ms、≤60→30ms、≤120→50ms、否则 100ms；进度=1−已用/总；到 0 停并响铃 | `App/Views/CountdownWindow.cs`、`Controls/Controls/CountdownRing.cs`、`Features/Tools/Tools.cs` | 3.4.0 headless 目视；Features `CountdownTimer` 无消费方 |
| `tool-lottery` | 抽奖窗口 | 已实现（部分验证） | adapt | 窗口 900×500；默认人数 60；`Names.txt` / `Replace.txt`（规则 `旧-->新`）；滚动 5 次、每次 150ms、闪烁集内不重复；最终抽取在本次内不重复，`NoRepeat` 还跨次记忆；结果 ≤5 一列、≤10 两列、>10 三列；单次模式自动滚动后 1.5s 关闭；池空显示“已抽完” | `App/Views/LotteryWindow.cs`、`Features/Tools/Tools.cs` | headless 目视；无单测 |
| `tool-replay-ui` | 回放窗口 | 已实现（部分验证） | adapt | 进度条 0–1000、宽 320；时间标签最小宽 96；速度下拉宽 96、默认索引 1（约 1×）；33ms 帧定时器；播放/暂停/进度拖动；0.5–4 倍速 | `App/Views/ReplayWindow.cs`、`Core/Replay/*` | 3.4.0 headless；逻辑有 13 单测 |
| `tool-screenshot` | 截图 | 已实现（未验证） | platform-bound | GDI `GetDC/CreateCompatibleDC/CreateCompatibleBitmap/BitBlt(SRCCOPY)/GetDIBits`；BGRA→Win2D PNG；文件 `Screenshot-HHmmss.png`；可选 `yyyy-MM-dd` 子目录；截图时可选自动存墨迹快照 | `Features/Tools/Tools.cs`、`ShellController.cs` | 断开会话下 CopyFromScreen/PrintWindow 不可靠 |
| `tool-autosave` | 自动快照/清理 | 已实现 | pure-logic | 文件名 `{HHmmss}-{reason}.icink`；根目录 `AutoSavedStrokesLocation`（默认 `D:\Ink Canvas`）；可选日期子目录；前提 `count ≥ MinimumAutomationStrokeNumber`；清理按 `LastWriteTime < now - max(1, days)` 递归删 `.icink`；`AutoDelSavedFiles` 关闭则 0 | `Features/Persistence/Stores.cs` | 无单测 |
| `tool-launchers` | EasiCamera/Desmos 启动 | 已实现（未验证） | platform-bound | HKLM 64/32 位卸载表，DisplayName 含“希沃视频展台”，找 `InstallLocation\sweclauncher.exe` 或 `UninstallString\..\sweclauncher\sweclauncher.exe`；Desmos 固定 URL；失败返回 false | `Platform/System/SoftwareLauncher.cs` | 未实机运行 |

### 2.6 持久化、系统与更新

| ID | 功能 | 状态 | 分类 | 关键行为与精确常量 | 源码 | 钉住/验证 |
|---|---|---|---|---|---|---|
| `system-settings-store` | Settings.json 读写 | 已实现 | pure-logic | `%APPDATA%\InkCanvas\Settings.json`；大小写不敏感；损坏/缺失回默认；写 `.tmp` 后原子替换；失败静默 | `Features/Persistence/Stores.cs`、`Core/Settings/AppSettings.cs` | DomainTests 2 例（往返 + 旧 WPF 文件） |
| `system-icink` | `.icink` 持久化 | 已实现 | pure-logic | 单表面 `Version=1 + Strokes`；每笔 `Color(uint ARGB), Width, Highlighter, HighlighterAlpha(默认96), Taper, Shape, Points[[x,y,pressure,width,timeMs]]`；加载 `RestorePoints` 保留宽度不再跑笔锋 | `Features/Persistence/Stores.cs` | 无单测；PPT 文件复用 |
| `system-filelog` | 文件日志 | 已实现 | pure-logic | `Log.txt`；行 `{ISO-8601} [{Type}] {message}`；类型 Info/Event/Trace/Error；默认 `%APPDATA%\InkCanvas`；失败即忽略；`IsLogEnabled` 总开关 | `Core/Diagnostics/FileLog.cs` | 无单测 |
| `system-update` | 自动更新 | 已实现（未验证） | platform-bound | 默认仓库 `QiXinia/Ink-Canvas-HEHS`；HttpClient 30s 超时、UA `InkCanvas-Updater`、`api.github.com/repos/{repo}/releases?per_page=20`；版本解析接受 `v/V` 前缀、2–4 段；安装包优先名含 `setup` 的 `.exe`，否则任意 `.exe`；下载后校验字节长度（与 `InstallerSize` 不等则删除）；用户确认后 `Process.Start`；静默策略见下；更新日志截断 600 字 | `Core/Update/ReleaseFeed.cs`、`UpdatePolicy.cs`、`Core/Versioning/AppVersion.cs`、`Features/Update/UpdateService.cs`、`App/Services/UpdateCoordinator.cs` | UpdatePolicy 7 + Update 19 例 |
| `system-edge-gesture` | 边缘手势抑制 | 已实现（属性写入已验证） | platform-bound | `SHGetPropertyStoreForWindow` → `IPropertyStore`，键 `System.EdgeGesture.DisableTouchWhenFullscreen`，`VT_BOOL=11`、PropertyId=2，必须 `Commit()` | `Platform/System/EdgeGestureService.cs` | SelfTest `edge gesture property applied` |
| `system-autostart` | 开机自启 | 已实现（未验证） | platform-bound | HKCU `Software\Microsoft\Windows\CurrentVersion\Run`，值名 `InkCanvasWinUI`，写入带引号 exe 路径 | `Platform/System/SystemServices.cs` | 无单测 |
| `system-foreground-autofold` | 前台识别/自动收纳 | 部分实现 | platform-bound | 取前台进程名；已接希沃白板/视频展台/品课、鸿合 HiteTouch、微软 Whiteboard、PPT 放映；未接轻白板/鸿合相机/文香/中原旧白板/忽略桌面 EN 批注；折叠前先切鼠标模式；手动覆盖 | `SystemServices.cs`、`ShellController.cs`、`SettingsWindow.cs` | 无单测；设置页部分 Deferred |
| `system-app-services` | 依赖装配与设置应用 | 已实现 | adapt | `ApplySettings`：笔宽夹 0.5–60、InkStyle→Taper、alpha 夹 20–240、橡皮半径 `45×{0.5,0.8,1,1.25,1.8}`、识别开关、多触/手势、palm 阈值 `max(8,FingerModeBoundsWidth)`、WPS、抽奖人数/不重复、日志开关 | `App/Services/AppServices.cs`、`ShellController.cs` | 无单测 |
| `platform-desktop-acrylic` | 半透背景 | 已实现（平台受限） | platform-bound | `DesktopAcrylicBackdrop`；Server 2025 回退不透明；永久移除 `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)`（DWM 死锁） | `App/Services/ChromeBackdrop.cs`、`FloatingBarWindow.cs` | 3.4.0 真机声明 |

### 2.7 明确未移植 / 部分实现清单（三态中的第三态与灰区）

| ID | 项 | 状态 | 原因/现状 | 出处 |
|---|---|---|---|---|
| `not-ported-notification-bar` | WPF 通知条 `MW_Notification` / `YesOrNoNotificationWindow.xaml` | 未移植 | 改用 `ContentDialog` / WinUI `YesOrNoWindow`；原通知条 UI 未复刻 | `PORTING-NOTES.md` 仍未移植表 |
| `not-ported-cycle-processbar` | `Windows/CycleProcessBar.xaml` | 未移植 | 辅助进度 UI | 同上 |
| `not-ported-window-memory` | `Helpers/WindowMemoryHelper.cs` 窗口位置记忆 | 未移植 | 无持久化位置；每次按默认/DPI 重新落位 | 同上 |
| `not-ported-legacy-isf` | 旧 `.isf/.icstk/.icart` 读取 | 未移植 | 依赖 `System.Windows.Ink`；仅检测并保留，不导入/覆盖/删除 | README、PPT notes |
| `not-ported-settings-gaps` | 设置历史缺口 | 部分实现 | `IsCompressPicturesUploaded`、`IsShowCursor`、`HideStrokeWhenSelecting` 全状态、`AutoSwitchTwoFingerGesture`、工具栏文字/彩色/透明/按钮显隐、笔尖切换器、画板独立缩放、静默更新时间 UI、开机收纳恢复、PPT no-clear 全状态、自动查杀、退出自动清屏、旧存档清理调度、关闭前确认、目录浏览器、触摸尺寸测量 UI、ZY SmartBoard 识别 | `menu-porting-notes.md` |
| `meta-drop-desktop-only` | 桌面专有附加合并项（元条目，不计入 62 个功能单元） | 建议不移植 | 通知条/循环进度/窗口位置记忆/旧 ISF/屏幕元素层合并为 1 个分母单元，原因逐项写 `notes` | 本报告建议 |

---

## 3. 全部测试清单（124 用例，逐个说明钉住的行为）

> 本节按测试类列出。`[Theory]` 的每个 `[InlineData]` 单独计数。断言中的数值即测试钉住的契约。

### 3.1 `DomainTests.cs`（16 用例）

#### `ShapeRecognizerTests`（5）

| 用例 | 输入 | 钉住的行为 |
|---|---|---|
| `RecognizesCircle` | `EllipsePoints(center(200,200), 90, 90, 0, 2π, 80)` | 识别为 `Circle`；`RadiusX∈[85,95]`；`Center.X∈[195,205]` |
| `RecognizesEllipse` | 中心 (300,200)，rx=140，ry=70，90 段 | 识别为 `Ellipse`；`RadiusX∈[130,150]`、`RadiusY∈[62,78]` |
| `RecognizesStraightLine` | 60 点，`(50+4i, 100+1.2i)` | 识别为 `Line`；`Corners.Length==2` |
| `RejectsTinyMarks` | 半径 6 的小圆（<MinSize 40） | 识别为 `None` |
| `RejectsOpenScribble` | 70 点正弦开放曲线 | 只能为 `None` 或 `Line`（不能误判为闭合图形） |

#### `ShapeGeneratorTests`（4）

| 用例 | 钉住的行为 |
|---|---|
| `EveryShapeKindProducesGeometry` | 遍历所有 `ShapeKind`：每个至少 1 笔；每笔至少 1 点；全部 `IsCompleted` |
| `Coordinate3DHasThreeAxes` | `Coordinate3D` 恰好 3 笔 |
| `DashedLineIsBrokenIntoSegments` | 实线 1 笔；虚线 >3 笔 |
| `CircleIsRoundRegardlessOfDragAspect` | 从 (0,0) 拖到 (200,80) 仍为圆，包围盒宽高比 0.9–1.1（半径=拖拽距离） |

#### `UndoStackTests`（3）

| 用例 | 钉住的行为 |
|---|---|
| `UndoAndRedoRoundTrip` | Push 已应用动作；Undo 后文档计数 1→0；Redo 后 0→1 |
| `PushClearsRedoBranch` | Undo 后 `CanRedo=true`；再 Push 新动作后 `CanRedo=false`；再 Undo 撤销新动作 |
| `CapacityDropsOldest` | `UndoStack(3)` 推 10 次后 `UndoCount==3` |

#### `InkDocumentTests`（2）

| 用例 | 钉住的行为 |
|---|---|
| `EraseRemovesOnlyTouchedStrokes` | 在 (120,100) 半径 12 擦除：只删近笔，远笔保留，`Count==1` |
| `TransformMovesStrokesAndScalesWidth` | `Scale(2)*Translate(5,7)`：点 (10,10)→(25,27)；宽度 ×2 |

#### `SettingsTests`（2）

| 用例 | 钉住的行为 |
|---|---|
| `RoundTripsThroughJson` | `InkWidth=7.5`、`Theme=2`、`AutoSavedStrokesLocation="E:\somewhere"` 序列化/反序列化一致 |
| `ReadsLegacyWpfSettingsJson` | 旧 Newtonsoft 形状小写键可读：`inkWidth=4.5`、`inkStyle=1`、`eraserType=2`、`theme=1`、`floatingBarBottomMargin=42.0`、`autoDelSavedFiles=true`、`autoDelSavedFilesDaysThreshold=30`、`peopleCount=45`；未出现的 `PowerPointSupport` 保持默认 `true` |

### 3.2 `PalmRejectorTests.cs`（9 用例）

| 用例 | 钉住的行为 |
|---|---|
| `SmallContactIsNotAPalm` | 接触 10 < 阈值 30 → 非手掌 |
| `LargeContactIsAPalm` | 接触 45 ≥ 30 → 手掌 |
| `PalmDoesNotDrawButDoesErase` | 手掌 `ShouldDraw=false`、`ShouldErase=true` |
| `FingertipDrawsAndDoesNotErase` | 指尖 8 → 画、不擦 |
| `ActivePenSuppressesTouchDrawing` | 笔按下后 `IsPenActive=true`；触摸不画；笔可画 |
| `PenPriorityCanBeDisabled` | `PenHasPriority=false` 时笔按下仍允许触摸画 |
| `LiftingPenReleasesTouchSuppression` | 抬笔后 `IsPenActive=false`，触摸恢复可画 |
| `LiftingPalmClearsIt` | 抬手后 `ActivePalms` 空，不再擦 |
| `ResetClearsAllState` | Reset 后无活笔、无手掌 |

### 3.3 `PixelCompositingTests.cs`（7 用例）

| 用例 | 钉住的行为 |
|---|---|
| `OpaqueSourceReplacesDestination` | alpha=255 → 目标字节等于源 |
| `FullyTransparentSourceLeavesDestinationUntouched` | alpha=0 → 目标完全不变 |
| `HalfAlphaSourceOverOpaqueBlackIsHalfSource` | 预乘源 (100,100,100,128) 覆盖不透明黑 → 结果 100，alpha 255 |
| `WritesAtRequestedOffsetOnly` | 在 (2,1) 写 1×1，只有该像素变白；左邻不变 |
| `ClipsSourceThatOverflowsDestination` | 4×4 源写入 2×2 目标右下角不抛异常，角像素变白 |
| `NegativeOffsetClips` | 偏移 (-1,-1) 裁剪；目标 (0,0) 白、(0,3) 不变 |
| `EmptySourceIsNoOp` | 0×0 源不改目标 |

### 3.4 `ReplayTests.cs`（13 用例）

#### `ReplayClockTests`（9）

| 用例 | 钉住的行为 |
|---|---|
| `StartsPausedAtZero` | 初始 `IsPlaying=false`、`ElapsedMs=0` |
| `AdvanceDoesNothingWhilePaused` | 暂停时 Advance 500 仍为 0 |
| `AdvanceAccumulatesWhenPlaying` | 播放后 100+150=250ms |
| `SpeedScalesAdvance` | Speed=2，100ms→200ms |
| `StopsAtEndAndReportsComplete` | 时长 300，Advance 1000 → 300、停、`IsComplete=true` |
| `SeekClampsToRange` | Seek(-50)→0；Seek(9999)→时长 |
| `ProgressIsFractionOfDuration` | 400ms 中 100ms → 0.25 |
| `ToggleFlipsPlaying` | 两次 Toggle 开→关 |
| `PlayingAfterCompletionRestartsFromBeginning` | 完成后 Play 从 0 开始并播放 |

#### `InkReplaySessionTests`（4）

| 用例 | 钉住的行为 |
|---|---|
| `NothingVisibleBeforeStart` | `VisibleAt(-1)` 为空 |
| `EverythingVisibleAtDuration` | 两笔各 10 点在总时长处全部可见 |
| `VisibleCountNeverDecreases` | t=0..400 步长 10，可见点数单调不减 |
| `ReplayPreservesStoredWidths` | 50ms 处部分笔画的点宽仍为 7 |

### 3.5 `SelectionGeometryTests.cs`（13 用例）

| 用例 | 钉住的行为 |
|---|---|
| `HitTestFindsCornerHandles` ×4 | (100,100)→TopLeft；(200,100)→TopRight；(200,200)→BottomRight；(100,200)→BottomLeft |
| `HitTestInsideBoundsIsMove` | (150,150)→Move |
| `HitTestFarOutsideIsNone` | (400,400)→None |
| `HitTestFindsRotateHandleAboveTopEdge` | 旋转柄命中 Rotate，且 `Y < Top`（RotateOffset=28） |
| `CornerDragScalesAboutOppositeCorner` | 拖 BR (200,200)→(300,300)：左上角 (100,100) 不动，BR 到 (300,300) |
| `MoveDragTranslates` | (150,150)→(170,160) |
| `RotateDragRotatesAboutCenter` | (200,150)→(150,200)（绕中心 (150,150) 转 90°） |
| `KeepAspectUsesUniformScale` | 拖到 (300,250) 且 keepAspect：两轴增长量相等 |
| `DegenerateDragDoesNotProduceNaN` | 起止同点：M11/M22 非 NaN |
| `NoneHandleIsIdentity` | None → `Matrix3x2.Identity` |

### 3.6 `ShapeDrawingSemanticsTests.cs`（25 用例）

| 用例 | 钉住的行为与常量 |
|---|---|
| `CircleUsesPointerDownAsCenterAndDragDistanceAsRadius` | 起点 (100,100) 为中心，拖 (30,40) → R=50；所有点距中心 50；首点 (150,100) |
| `DashedCircleUsesCenterAndRadiusWithAngularDashes` | R=50；角向 dash 0.05π / 0.083π → 共 26 笔 |
| `RectangleFromCenterUsesFullDeltaAndNoCenterDot` | 中心 (100,100) 拖 (140,130)：单笔 5 点；四角 (60,70)-(60,130)-(140,130)-(140,70)-(60,70)；无中心点 |
| `EllipseFromCenterUsesFullDeltaAndNoCenterDot` | 中心 (100,100) 拖 (160,120)：x∈[40,160]、y∈[80,120]；首点 (160,100) |
| `EllipseWithFociPlacesPressureOneDotsOnTheMajorAxis` | 宽：3 笔；焦点 x=100±√(60²−20²)；pressure=1；宽度=BaseWidth×2；高：焦点在 y=100±√(60²−20²)；a==b 时只 1 笔 |
| `ParallelLineDrawsFourLinesAtOffsets25And75` | 4 笔，偏移 −75/−25/+25/+75；每笔 2 点 |
| `ParallelLineSnapsNear30Degrees` | tan 0.6 → 30°；端点 (start.X+d·0.866, start.Y−d·0.5)；法向 (0.5,0.866) 偏移四条线 |
| `ParallelLineSnapsNearVertical` | 近垂直：x=100±偏移；y 从 100 到 300 |
| `SnapParallelEndRespectsBands` ×2 | tan 0.8 不吸附；tan 0.05（<1/12）→ 压平 y |
| `ParallelLineSnaps45And60` | 45° 用 0.707/0.707；60° 用 0.5/0.866，保持拖拽距离 |
| `CuboidFirstStepIsFourSeparateEdges` | 4 笔 ×2 点；边序：左竖、下横、右竖、上横 |
| `CuboidDepthHasFiveSolidEdgesAndThreeDashedHiddenEdges` | 5 条实边坐标精确；隐藏边 3 条；18 段 dash；总 23 笔 |
| `HyperbolaFirstStepIsTwoDashedAsymptotesThroughTheCenter` | 两条穿过中心的虚线；11 段/条 → 22 笔；所有点在两条线上 |
| `HyperbolaBranchesLockToKWithFociOnXAxis` | k=1，拖 (100,30)：4 支；顶点 (a,0)，a=√(100²−30²)；每点满足 `y²=k²x²−b²` |
| `HyperbolaBranchesPutFociOnYAxisWhenDragIsSteeperThanK` | 拖 (30,100)：6 笔；顶点 (0,a)；焦点 (0,±c)，c=√(a²+a²)；pressure=1 |
| `CylinderHasSolidTopAndFrontSolidBackDashedBottom` | 上拖 (100,300)→(200,100)：17 笔；上椭圆全实、短半轴=宽/2.646；下底前半实（r 0..π，y≥300）、后半 13 段角向 dash；侧线连接椭圆中心 |
| `ConeHasApexAtTopCenterAndSplitBottom` | 16 笔；前底半实；顶点 (150,100)；两条斜线到 (100,300)/(200,300) |
| `DashedLineUsesDash5AndPeriod13Point8` | 100 长 → 8 段；首段 0..5；第二段 13.8..18.8；末段裁到 100 |
| `DotLinePlacesPressure0Point8DotsEvery8Point28` | 80 长 → 10 点；每点 pressure=0.8；宽度=BaseWidth×1.6 |
| `ArrowHeadIsFixed30By10` | 单笔 5 点：起点、终点、终点后 30/−10、终点、终点后 30/+10（按方向旋转） |
| `CoordinateAxisArrowHeadIsFixed20By7` | 第一象限：x 轴尾部 −25；y 轴尾部 +25；箭头条 20×7 |
| `ParabolaUpPassesExactlyThroughTheDragEnd` | 2 支；顶点=pointer-down；右支末点=拖拽终点；左支对称；向下拖仍端点精确 |
| `ParabolaRightOpensTowardTheDragAndIsEndpointExact` | 2 支；末点=拖拽终点；镜像支 (50,−100) |
| `ParabolaWithFociPlacesFocusAtMinusPHalf` | 3 笔；p=dy²/(2·(ini.X−end.X))=−100；焦点 (50,0)；pressure=1 |

### 3.7 `StrokeRegionTests.cs`（3 用例）

| 用例 | 钉住的行为 |
|---|---|
| `SelfCrossingStrokeProducesOverlappingFilledRegionsNotOneEvenOddFigure` | 自交 4 点笔（宽 20、荧光笔）→ `BuildUnionContours` 返回 `Count−1=3` 个区域；每区有向面积 >0（正绕向） |
| `ReversingSegmentDirectionKeepsSameFillWinding` | 正/反向线段轮廓的有向面积都 >0 |
| `SinglePointStillHasOneFilledRegion` | 单点笔返回 1 个正面积区域 |

### 3.8 `TaperTests.cs`（12 用例）

| 用例 | 钉住的行为 |
|---|---|
| `LastLiveFrameEqualsCommittedGeometry` ×3（None/Tail/Velocity） | 60 个样本的 `Points[].Width` 与 `BuildOutline` 在 `Complete()` 前后逐元素完全相同 |
| `NoPointEverGetsNarrower` ×3（None/Tail/Velocity） | 每来一个新样本，所有已存在点的宽度不得下降（容差 1e-4） |
| `TailIsAlreadyTaperedWhileStillDrawing` | 40 样本时笔尖宽度 < 中点宽度；中点 >9.9（BaseWidth=10） |
| `VelocityTaperDoesNotRewriteEarlierPoints` | 前 30 点快照；后续更快 30 点不改变前 30 点宽度 |
| `NoneTaperIsUniform` | 所有点宽 = BaseWidth=6 |
| `HighlighterNeverTapers` | 荧光笔即使请求 Tail，`TaperKind` 变 None，所有点宽=24 |
| `OutlineIsDeterministic` | 同一输入两次构建轮廓完全相等 |
| `SinglePointProducesAClosedDot` | 单点轮廓 ≥8 点；所有点距中心 0.1..8 |

### 3.9 `UpdatePolicyTests.cs`（7 用例）

| 用例 | 钉住的行为 |
|---|---|
| `NeverSilentWhenSilenceDisabled` | `IsAutoUpdateWithSilence=false` → 永不静默 |
| `NeverSilentWhenAutoUpdateDisabled` | `IsAutoUpdate=false` → 永不静默 |
| `SilentInsideSameDayWindow` | 01:00–05:00：03:00 可静默；06:00、00:00 不可 |
| `HandlesWindowCrossingMidnight` | 22:00–06:00：23:00、05:00 可；12:00 不可 |
| `EqualStartAndEndMeansNoRestriction` | 00:00–00:00 → 任意时刻可静默 |
| `WindowBoundariesAreInclusiveAtStartExclusiveAtEnd` | 02:00–04:00：02:00 可；04:00 不可 |
| `MalformedTimesFallBackToAskingTheUser` | 非法时间字符串 → 不静默（询问用户） |

### 3.10 `UpdateTests.cs`（19 用例）

#### `AppVersionTests`（11）

| 用例 | 钉住的行为 |
|---|---|
| `ParsesCommonTagForms` ×5 | `"3.0.0"`→3.0.0.0；`"v3.1.2"`→3.1.2.0；`"V26.10.7"`→26.10.7.0；`"1.2.3.4"`→1.2.3.4；`"  v2.5  "`→2.5.0.0 |
| `RejectsNonVersionTags` ×4 | `""`、`"latest"`、`"release"`、`null` 均解析失败 |
| `OrdersByComponents` | 3.0.0 < 3.0.1 < 3.1.0 < 4.0.0 |
| `BuildNumberBreaksTies` | 3.0.0.1 < 3.0.0.2 |

#### `ReleaseFeedTests`（8）

| 用例 | 钉住的行为 |
|---|---|
| `ParsesLatestRelease` | `tag_name="v3.2.0"` → `Tag="v3.2.0"`、`Version=3.2.0.0`、`Notes="修复若干问题"` |
| `PrefersInstallerExeAsset` | 优先名含 `Setup` 的 `.exe`；URL 与 size 取该资产（987654） |
| `ReturnsNullWhenNoInstallerAsset` | 只有 `notes.txt` → null |
| `SkipsDraftsAndPrereleasesInList` | 数组里 draft/prerelease 跳过，选 `v3.2.0` |
| `MalformedFeedReturnsNull` ×3 | 空串、非 JSON、`{}` → `ParseLatest` 与 `ParseNewestStable` 都 null |
| `IsNewerThanComparesAgainstCurrent` | 3.2.0 新于 3.0.0；不新于 3.2.0/4.0.0 |

---

## 4. 数据格式与 JSON 字段名（移植必须逐字保持）

### 4.1 `Settings.json`

路径：`%APPDATA%\InkCanvas\Settings.json`（可在 `SettingsStore` 构造时覆盖目录）。
序列化：System.Text.Json，`WriteIndented=true`，读取 `PropertyNameCaseInsensitive=true`，写入 `.tmp` 后 `File.Move(overwrite)`；损坏返回默认。

顶层键：
`advanced`, `appearance`, `automation`, `behavior`（PowerPointSettings）, `canvas`, `gesture`, `inkToShape`, `startup`, `randSettings`。

| 段 | 字段（JSON 名 = 属性名） | 默认值 |
|---|---|---|
| `advanced` | `isSpecialScreen`, `isQuadIR`, `touchMultiplier=0.25`, `nibModeBoundsWidth=10`, `fingerModeBoundsWidth=30`, `nibModeBoundsWidthThresholdValue=2.5`, `fingerModeBoundsWidthThresholdValue=2.5`, `nibModeBoundsWidthEraserSize=0.8`, `fingerModeBoundsWidthEraserSize=0.8`, `isEnableEdgeGestureUtil=false`, `isLogEnabled=true`, `isSecondConfimeWhenShutdownApp=false` | 见左 |
| `appearance` | `isEnableDisPlayFloatBarText=false`, `isEnableDisPlayNibModeToggler=true`, `isColorfulViewboxFloatingBar=false`, `enableViewboxFloatingBarScaleTransform=100.0`, `enableViewboxBlackBoardScaleTransform=100.0`, `floatingBarBottomMargin=100.0`, `isTransparentButtonBackground=true`, `isShowExitButton=true`, `isShowEraserButton=true`, `isShowHideControlButton=false`, `isShowLRSwitchButton=false`, `isShowModeFingerToggleSwitch=true`, `theme=0`（0 跟随/1 浅/2 深） | 见左 |
| `canvas` | `inkWidth=2.5`, `inkAlpha=140`, `isCompressPicturesUploaded=false`, `isShowCursor=false`, `inkStyle=0`, `eraserSize=2`, `eraserType=0`, `hideStrokeWhenSelecting=true`, `usingWhiteboard=false`, `hyperbolaAsymptoteOption=Ask`（Yes=0/No=1/Ask=2） | 见左 |
| `gesture` | `isEnableMultiTouchMode=true`, `isEnableTwoFingerZoom=true`, `isEnableTwoFingerTranslate=true`, `AutoSwitchTwoFingerGesture=true`（注意大写 A）, `isEnableTwoFingerRotation=false`, `isEnableTwoFingerRotationOnSelection=false`, `matrixTransformCenterPoint=CanvasCenterPoint`（0/1/2） | 见左 |
| `behavior` | `isShowPPTNavigationBottom=true`, `isShowPPTNavigationSides=true`, `isShowBottomPPTNavigationPanel=true`, `isShowSidePPTNavigationPanel=true`, `powerPointSupport=true`, `isShowCanvasAtNewSlideShow=true`, `isNoClearStrokeOnSelectWhenInPowerPoint=true`, `isShowStrokeOnSelectInPowerPoint=false`, `isAutoSaveStrokesInPowerPoint=true`, `isAutoSaveScreenShotInPowerPoint=false`, `isNotifyPreviousPage=false`, `isNotifyHiddenPage=true`, `isNotifyAutoPlayPresentation=true`, `isEnableTwoFingerGestureInPresentationMode=false`, `isEnableFingerGestureSlideShowControl=true`, `isSupportWPS=true` | 见左 |
| `automation` | 10 个 `isAutoFoldIn*`（EasiNote/EasiNoteIgnoreDesktopAnno/EasiCamera/EasiNote3C/SeewoPincoTeacher/HiteTouchPro/HiteCamera/WxBoardMain/OldZyBoard/MSWhiteboard/PPTSlideShow）、`isAutoKillPptService`, `isAutoKillEasiNote`, `isSaveScreenshotsInDateFolders`, `isAutoSaveStrokesAtScreenshot`, `isAutoSaveStrokesAtClear`, `isAutoClearWhenExitingWritingMode`, `minimumAutomationStrokeNumber=0`, `autoSavedStrokesLocation="D:\Ink Canvas"`, `autoDelSavedFiles=false`, `autoDelSavedFilesDaysThreshold=15` | 见左 |
| `inkToShape` | `isInkToShapeEnabled=true` | true |
| `startup` | `isAutoUpdate=true`, `isAutoUpdateWithSilence=false`, `isAutoUpdateWithSilenceStartTime="00:00"`, `isAutoUpdateWithSilenceEndTime="00:00"`, `isEnableNibMode=false`, `isFoldAtStartup=false` | 见左 |
| `randSettings` | `peopleCount=60`, `isNotRepeatName=false` | 见左 |

推荐重置 `ResetToRecommendation()` 的差异：`TouchMultiplier=0.3`、`InkAlpha=80`、`EraserSize=1`、`InkStyle=0`、`FloatingBarBottomMargin=100`、`AutoSavedStrokesLocation="D:\Ink Canvas"`、`AutoDelSavedFiles`/阈值保持不变、`Theme=0`、`HyperbolaAsymptoteOption=Yes` 等；未列字段回构造默认。

### 4.2 单表面 `.icink`（`InkFileStore`）

```json
{
  "Version": 1,
  "Strokes": [
    {
      "Color": 4278190080,
      "Width": 2.5,
      "Highlighter": false,
      "HighlighterAlpha": 96,
      "Taper": 1,
      "Shape": false,
      "Points": [[x, y, pressure, width, timeMs], "..."]
    }
  ]
}
```
- `Color`：packed ARGB uint。
- `Points` 行至少 4 列；第 5 列 timeMs 可缺省为 0。
- 加载后 `RestorePoints` 直接使用存储宽度，不再跑笔锋。
- 扩展名 `.icink`。

### 4.3 画板包 / 页面包（`BoardService`）

```json
{
  "Format": "inkcanvas-board",
  "Version": 1,
  "Background": "Black|White|Grid|Lined",
  "GridSpacing": 40.0,
  "Pages": [
    {
      "Strokes": [ { "Color": 0, "Width": 2.5, "Highlighter": false,
                     "Taper": 1, "Shape": false, "Points": [[...],[...]] } ],
      "Images": [ { "Id": "...", "Name": "...", "Data": "base64",
                    "ContentType": "image/png", "Left": 0, "Top": 0,
                    "Width": 0, "Height": 0, "Transform": [m11,m12,m21,m22,m31,m32] } ],
      "Media":  [ { "Id": "...", "Name": "...", "Data": "base64",
                    "ContentType": "video/mp4", "Left": 0, "Top": 0,
                    "Width": 360, "Height": 240, "Transform": null } ]
    }
  ]
}
```
- 嗅探 `Format` 使用 `JsonDocument.GetProperty("Format")`，**大小写敏感**；反序列化大小写不敏感。
- `Version != 1` 抛 `InvalidDataException`。
- 画板 Stroke 子格式**没有 `HighlighterAlpha`**（见硬结论 5）。
- `SavePage` 仅当当前页**没有图片**时写单表面 `.icink`；有图片时写画板包。因此“只有视频、没有图片”的页执行页保存会**丢失媒体**；这是当前真实行为，移植时必须显式处理。
- 画板包的 `Images`/`Media` 资产字段使用默认 PascalCase；`Data` 为 base64。

### 4.4 PPT 墨迹存档（`PptInkFileStore`）

目录：`<AutoSavedStrokesLocation>/Auto Saved - Presentations/<安全文件名>_<SlideCount>/`

`InkCanvas-Ppt.json`：
```json
{
  "Version": 1,
  "LastSlideIndex": 3,
  "LastShowPosition": 2,
  "Slides": [ { "SlideId": 256, "SlideIndex": 1, "File": "slide-256.icink" } ]
}
```
- 每页文件 `slide-<SlideID>.icink`，内容为 4.2 单表面格式。
- 另写纯文本 `Position`（旧兼容），内容为 `LastSlideIndex`。
- `.icstk/.icart` 只做存在性检测，`LegacyInkFilesDetected=true`，不导入、不覆盖、不删除。
- 保存时删除 manifest 中已不存在的旧页文件。

### 4.5 其他文件

| 文件 | 规则 |
|---|---|
| `Log.txt` | `%APPDATA%\InkCanvas\Log.txt`；行 `{DateTime.Now:O} [{LogType}] {message}` |
| 自动快照 | `<AutoSavedStrokesLocation>[/yyyy-MM-dd]/{HHmmss}-{reason}.icink` |
| PPT 截图 | `<root>/Auto Saved - PPT Screenshots[/yyyy-MM-dd]/<演示文稿名>/`；`Explicit` 文件名 `<ShowPosition>_<HH-mm-ss>.png`；导航/外部切换用 `<filePosition>.png`（外部切换优先上一页位置） |
| `Names.txt` | 每行一个姓名；空行/空白忽略 |
| `Replace.txt` | 每行 `旧-->新`，加载时替换显示名 |
| `TimerDownNotice.wav` | 随包 `Assets/TimerDownNotice.wav` |
| `SegoeFluentIcons.ttf` / `SegoeMDL2Assets.ttf` | 随包 `Assets/`，WinUI 通过 `ms-appx:///Assets/...` 引用 |

---

## 5. UI 度量与图标码点

### 5.1 窗口与面板

| 窗口/元素 | 度量 |
|---|---|
| 设置面板 | 宽 440 DIP；上下 inset 150；高 `max(240, workH−300)`；标题头 40；底部 50；内容边距 20；圆角 5；通知 3s；入场 0.5s |
| 浮动栏 | 内容 36 DIP → 50 DIP 视觉；用户缩放 50–200%；底部边距默认 100 DIP |
| 主按钮 | 30×32 → 41.7×44.4；图标 22.2；标签 11.1；圆角 6.94；边框 1.39；间隙 2.78 |
| 更多按钮 | 45×45 → 62.5×62.5；图标 30.6；标签 13.9 |
| PPT 退出牌 | 宽 34 → 47.2 |
| 折叠把手 | 露出约 14 DIP |
| 笔/荧光笔调色盘 | 设计宽 248/208；色块 40、格 44、行高 32；圆角 5；标题带 27 |
| 图形菜单 | 5 行 5/5/5/5/4；行高 50；行距 14/15/10/10/10；纵向 −8；宽 200/240；回退高 234/286；长按 500ms |
| 画板 | 三条 55 DIP 浮条；单元格 60×55；页码指示 58×55；选区面板高 104；粗细框 155×33；提示条高 20、宽 200 |
| 画板网格 | 默认 40 DIP；加载夹 12..160；预设 24/32/40/56/72 |
| 计时器 | 1100×700；最小 300×200；环 150×150；数字 88.5 高、56 号；结束 pill 18 高、圆角 9；紧凑模式 400×250（缩放后） |
| 抽奖 | 900×500；结果按钮 200×70；抽奖按钮 80 宽、55 号 |
| 指南 | 700×600；最小宽 500；键帽 50×50 圆形、字号 20 |
| 更新日志 | 600×390 |
| 是/否窗 | 450×160；按钮宽 100；底部行 35 |
| PPT 导航 | S=40/42；单元格 38.1；页码格 36.2；图标盒 24.8、字号 19.0；文字 8.57；分隔线 23.8；圆角 5.71；透明度 0.4 |
| 触控 token | `IcTouchTargetMin=40`, `IcTouchTargetComfortable=48`, `IcFloatingBarIcon=22`, `IcControlCorner=8`, `IcBarCorner=14`, `IcBarPadding=8,6,8,6`, `IcCardPadding=16,12` |

### 5.2 图形常量（`ShapeGenerator`）

| 常量 | 值 |
|---|---|
| `FullEllipseSegments` | 72 |
| `DashLength` | 5 |
| `DashPeriod` | 5×2.76 = 13.8 |
| `DotPeriod` | 3×2.76 = 8.28 |
| `DotPressure` | 0.8 |
| `EllipseDashStep` | 0.05（×π） |
| `EllipseDashPeriod` | 0.05×1.66 = 0.083（×π） |
| `SolidEllipseRatio` | 2.646（短轴=宽/2.646） |
| 平行线偏移 | ±25、±75（`x=25`，`k∈{−3,−1,1,3}`） |
| 角度吸附带 | \(\tan<1/12\) 水平；0.52–0.63→30°；0.92–1.08→45°；1.63–1.95→60°；\(|\cot|<1/12\) 垂直 |
| 箭头 | 头 30×10 |
| 坐标轴箭头 | 头 20×7，尾部 25 |
| 双曲线采样 | 0.5 px |
| 抛物线采样 | 0.5 px |
| 长方体深度 | 45° 上右，d=|frontTop.Y−dragEnd.Y|；5 实 3 虚 |
| 圆/中心矩形/中心椭圆 | pointer-down=中心，drag delta=全尺寸 |
| 圆焦点椭圆 | c=√|a²−b²|，焦点为 pressure 1 单点，宽度=2×BaseWidth |
| 抛物线上开口 | a=(ini.Y−end.Y)/dx²，顶点在 pointer-down，端点精确 |
| 抛物线右开口 | a=(ini.X−end.X)/dy² |
| 抛物线含焦点 | p=dy²/(2·(ini.X−end.X))，a=0.5/p，焦点 (ini.X−p/2, ini.Y)，pressure 1 |

24 个可见图形（`ShapeKind` 25 个枚举值，`HyperbolaWithFoci` 与 `Hyperbola` 共用第一段、`Ellipse` 边界框版在菜单中 Collapsed 不计）：Line、DashedLine、DotLine、Arrow、ParallelLine、CoordinateBoth、CoordinateXOneWay、CoordinateYOneWay、CoordinateFirstQuadrant、Coordinate3D、Rectangle、RectangleFromCenter、Ellipse、Circle、EllipseFromCenter、EllipseWithFoci、DashedCircle、Hyperbola、HyperbolaWithFoci、ParabolaRight、ParabolaWithFoci、ParabolaUp、Cylinder、Cone、Cuboid。菜单显示 24 个（5+5+5+5+4）。

### 5.3 调色板颜色（`OriginalMenuCatalog`）

普通笔（深色主题 / 浅色主题）：
| 名称 | Dark | Light |
|---|---|---|
| 黑 | (0,0,0) | (0,0,0) |
| 白 | (255,255,255) | (255,255,255) |
| 红 | (220,38,38) | (239,68,68) |
| 粉 | (147,51,234) | (236,72,153) |
| 橙 | (234,88,12) | (249,115,22) |
| 黄 | (234,179,8) | (250,204,21) |
| 绿 | (22,163,74) | (34,197,94) |
| 青 | (13,148,136) | (20,184,166) |
| 蓝 | (37,99,235) | (59,130,246) |

荧光笔（两主题同色）：红 (220,38,38)、橙 (234,88,12)、黄 (234,179,8)、青 (13,148,136)、蓝 (37,99,235)、紫 (147,51,234)。
画板选区 9 色：黑 (0,0,0)、白 (255,255,255)、红 (220,38,38)、橙 (234,88,12)、黄 (234,179,8)、绿 (22,163,74)、青 (13,148,136)、蓝 (37,99,235)、粉 (199,46,199)。

### 5.4 图标码点（`Glyphs` / `BoardWindow` / `PptNavigationWindow`）

| 名称 | 码点 | 字体/用途 |
|---|---|---|
| Pen | U+E70F | Segoe Fluent |
| Highlight | U+E7E6 | |
| Eraser | U+E75C | |
| Select | U+E8B3 | |
| Shapes | U+25B3 | Segoe UI Symbol |
| Undo / Redo | U+E7A7 / U+E7A6 | |
| Delete / Add | U+E74D / U+E710 | |
| Settings / Camera | U+E713 / U+E722 | |
| Stopwatch / People / Board | U+E916 / U+E716 / U+E7F4 | |
| ChevronUp / Down / Left / Right | U+E70E / U+E70D / U+E76B / U+E76C | |
| Lock / Unlock | U+E72E / U+E785 | |
| Close / Exit | U+E711 / U+E8BB | |
| Play / Pause / Restart | U+E768 / U+E769 / U+E72C | |
| Replay / Help / Info / Download | U+E823 / U+E897 / U+E946 / U+E896 | |
| Cursor | U+E8B0 | |
| PenWpf | U+EE56 | |
| HighlightWpf | U+E7E6 | |
| EraserOverlayCircle / Stroke | U+F0B6 / U+F128 | |
| SelectWpf / SelectOverlay | U+E958 / U+F8AF | |
| ShapesWpf | U+F158 | |
| InsertMedia / BoardWpf / Tools | U+EE71 / U+E929 / U+E8A9 | |
| FoldWpf | U+ED1A | |
| Emoji / EmojiDrag | U+E76E / U+E899 | |
| Gesture | U+F271 | |
| TimerWpf / Rand | U+EA8F / U+E8B1 | |
| SaveStrokes / OpenStrokes | U+E74E / U+ED25 | |
| ReplayWpf / GuideWpf / ExitWpf | U+EC57 / U+E82D / U+E7E8 | |
| HideControl / LRSwitch | U+E7B3 / U+E8AB | |
| GestureTranslate / Zoom / Rotate | U+E7C2 / U+E740 / U+E7AD | |
| 画板 prev/next | U+E760 / U+E761 | |
| 画板 add page | U+ECC8 | |
| 画板 exit | U+E944 | |
| 画板 canvas | U+EF1F | |
| PPT bottom prev/next | U+EDD5 / U+EDD6 | |
| PPT side prev/next | U+EDD7 / U+EDD8 | |

---

## 6. 平台依赖清单（HarmonyOS 替换点）

### 6.1 Win32 / User32 / GDI32

| 能力 | API | 使用点 |
|---|---|---|
| 分层透明窗口 | `CreateWindowExW`, `WS_EX_LAYERED`, `UpdateLayeredWindow`, `ShowWindow(SW_SHOWNOACTIVATE/SW_HIDE)` | `LayeredInkWindow` |
| 点击穿透 | `WS_EX_TRANSPARENT` 增删 | `LayeredInkWindow.ClickThrough` |
| 指针输入 | `WM_POINTERDOWN/UPDATE/UP/CAPTURECHANGED`, `GetPointerInfo`, `GetPointerPenInfo`, `GetPointerTouchInfo`, `POINTER_INPUT_TYPE_PEN/TOUCH`, `PeekMessageW` | `LayeredInkWindow` |
| 触摸反馈抑制 | `RegisterTouchWindow`, `SetWindowFeedbackSetting` | `LayeredInkWindow` |
| 鼠标回退 | `WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP`, `WM_CAPTURECHANGED`, `WM_CANCELMODE` | `LayeredInkWindow` |
| 全局热键 | `RegisterHotKey`, `UnregisterHotKey`, `WM_HOTKEY`, `MOD_ALT=1/CONTROL=2/SHIFT=4/WIN=8`, `MOD_NOREPEAT=0x4000` | `HotkeyService` |
| 窗口 chrome | `GetDpiForWindow`, `SetWindowPos(HWND_TOPMOST)`, `SetWindowRgn`, `CreateRectRgn`, `ClientToScreen`, `GetWindowRect`, `GetCursorPos` | `WindowChrome` |
| DWM | `DwmSetWindowAttribute` 33 (`DWMWA_WINDOW_CORNER_PREFERENCE`=2)、2 (`DWMWA_NCRENDERING_POLICY`=1)、34 (`DWMWA_BORDER_COLOR`=0xFFFFFFFE) | `WindowChrome` |
| 屏幕/工作区 | `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN/SM_XVIRTUALSCREEN/...)`, `SystemParametersInfoW(SPI_GETWORKAREA)` | `ScreenService` |
| 截图 | `GetDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject`, `BitBlt(SRCCOPY=0x00CC0020)`, `GetDIBits`, `DeleteObject/DeleteDC/ReleaseDC` | `ScreenshotService` |
| 窗口枚举/前台 | `EnumWindows`, `GetWindowTextW`, `IsWindowVisible`, `IsIconic`, `GetForegroundWindow`, `GetWindowThreadProcessId` | `WindowEnumerator`, `ForegroundWindowService` |
| 属性存储 | `SHGetPropertyStoreForWindow` + `IPropertyStore`，`System.EdgeGesture.DisableTouchWhenFullscreen`，`VT_BOOL=11`，`Commit()` | `EdgeGestureService` |

### 6.2 COM

| 能力 | API/对象 | 使用点 |
|---|---|---|
| PPT/WPS 附着 | `CLSIDFromProgID`（ole32）、`GetActiveObject`（oleaut32）、`Marshal.GetObjectForIUnknown/ReleaseComObject`、`dynamic` 后期绑定 | `PptInteropService` |
| Office 对象 | `PowerPoint.Application` / `KWPP.Application`、`Presentations`、`Slides`、`Slide.SlideID`、`SlideShowTransition.Hidden/AdvanceOnTime/AdvanceTime`、`SlideShowWindow.View`（`Next/Previous/Exit/GotoSlide`）、`SlideShowSettings`、`CommandBars.ExecuteMso("SlideShowFromCurrent")` | 同上 |
| WPS 守卫 | `Process.GetProcessesByName("wpp")` | 同上 |
| 线程 | 必须在创建 UI 的 STA；每 API 校验 `CurrentManagedThreadId` | 同上 |

### 6.3 Win2D / WinUI 3

| 能力 | API | 使用点 |
|---|---|---|
| 2D 渲染 | `CanvasControl`, `CanvasDevice`, `CanvasRenderTarget`, `CanvasDrawingSession`, `CanvasGeometry`/`CanvasPathBuilder` | `InkSceneRenderer`, `StrokeRenderer`, `OverlayInkPresenter`, `ScreenshotService`, `CountdownRing` |
| PNG 编码 | `CanvasBitmap.CreateFromBytes`, `CanvasBitmapFileFormat.Png`, `SaveAsync` | `ScreenshotService` |
| 窗口 | `Window`, `AppWindow`, `AppWindow.MoveAndResize/ResizeClient/Resize/Move`, `OverlappedPresenter.SetBorderAndTitleBar/IsAlwaysOnTop`, `DisplayArea.GetFromRect` | `WindowChrome`, 全部窗口 |
| 背景 | `DesktopAcrylicBackdrop` | `ChromeBackdrop`, `FloatingBarWindow` |
| 调度 | `DispatcherQueue.CreateTimer`, `TryEnqueue`, `XamlRoot.Changed`, `FrameworkElement.Loaded/Activated` | 全局 |
| 主题 | `FrameworkElement.RequestedTheme`, `Application.Current.RequestedTheme` | `WindowChrome.ApplyTheme` |
| 弹窗/对话框 | `ContentDialog`, `Flyout`, `MenuFlyout`, `YesOrNoWindow` | `ShellController`, `BoardWindow` |
| 文件选择 | `FileOpenPicker`, `FileSavePicker`, `InitializeWithWindow` | `ShellController`, `BoardWindow` |
| 媒体 | `MediaPlayerElement`, `MediaPlayer`, `MediaSource` | `BoardMediaLayer`, `CountdownWindow` |
| 自检截图 | `RenderTargetBitmap`, `BitmapEncoder`, `DataReader` | `App.xaml.cs` |
| 动效 | `ElementCompositionPreview`, `Compositor`, keyframe/spring/implicit animations, `UISettings.AnimationsEnabled` | `Motion` |
| 无障碍 | `AutomationProperties` | pickers/palette |
| 字体 | `ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons`、`Segoe MDL2 Assets` | 全局图标 |

### 6.4 .NET / 系统

- `System.Text.Json`（设置、`.icink`、画板、PPT manifest、日志对象、更新 JSON）
- `System.Numerics`（`Vector2`, `Matrix3x2`）
- `Microsoft.Win32.Registry`（自启、EasiCamera 卸载表）
- `System.Diagnostics.Process`（前台进程名、WPS 守卫、启动器、安装包）
- `HttpClient`（GitHub API/下载，30s）
- `System.Threading.Timer`（Features `CountdownTimer`，无消费方）

---

## 7. 验证边界（逐轮、逐项）

### 7.1 项目文档声明

| 轮次 | 声明 | 来源 |
|---|---|---|
| 3.0.0 | WinUI3 迁移、Win2D 重写、托管图形识别、COM 后期绑定、Fluent UI | CHANGELOG |
| 3.1.0 | 选区变换、删除按钮、双指手势作用选区、回放、自动更新、指南/更新日志、边缘手势、窗口枚举、区域回读 | CHANGELOG |
| 3.3.0 | 菜单/白板/PPT 适配补齐；**未运行单测、未启动应用、未接真实触摸/压感/Office/WPS**；仅 Release 编译 0 警告 0 错误；例外：3 项纯几何测试 3/3 | CHANGELOG、menu-verification-status |
| 3.4.0 | **真机 Windows Server 2025、125% DPI**：窗口关闭/拖动/模式往返/弹窗锚定/菜单内容完整/工具栏顺序/半透回退/**124 项单测通过**；**headless（RDP 断开）**：七窗 RenderTargetBitmap 目视、色选即关、pen toggle、画板 `--board` 启动存活 | CHANGELOG |
| 3.4.0 仍待 | 触摸/压感硬件、Win11 亚克力真半透、Office/WPS 实机、视频帧导出、克隆上限提示与确认对话框（移植期安全 additions） | CHANGELOG、PORTING-NOTES |
| README | 覆盖层五次透明手法实测（输出与不透明组逐像素一致）；背景保留率 97.65%；回读 205 KB/帧 = 2.5% | README |
| runtime-selftest.json | 159 帧、6 笔、`allCriticalChecksPassed=true`；`fullFrameBytes=8294400`；`drawingReadbackBytesPerFrame=205778`；`drawingRatioVsFullScreen=0.02481`；`ratioWholeRun=0.1042` | evidence |

### 7.2 本次研究未验证（必须如实标注）

- 未运行 `.\build.ps1 build|test|run|selftest`，未确认当前工作树可编译。
- 未运行 124 项单测，不能声称“通过”；只能声称“源码中存在 124 个用例且文档声明 3.4.0 已通过”。
- 未启动应用，未验证窗口、DPI、拖动、Z 序、菜单、画板、PPT、计时、抽奖、回放。
- 未接触触摸屏/压感笔/红外屏，`TouchContactClassifier` 全部阈值未验证。
- 未接 Office/WPS，COM 附着、放映、截图、隐藏页/计时提示未验证。
- 未在 HarmonyOS SDK/真机运行；本报告不包含任何 ArkTS 验证。
- 视频编解码、PNG 截图、音频播放未验证。
- 更新下载/安装、注册表自启、EasiCamera 启动未验证。

### 7.3 文档内部矛盾与处理

| 矛盾 | 处理 |
|---|---|
| 3.3.0 说“96 项未运行”，3.4.0 说“124 项通过” | 以 3.4.0/当前源码为准；96 是历史 |
| `PORTING-NOTES` 说更新 26 项、图形 21 项 | 源码为 19、25；配置/测试在 3.4.0 有增删，按源码 |
| CLAUDE.md 说“本机无 HarmonyOS SDK” | `00-harness-notes.md` 记录 E 盘有 26.0.1.290 beta 工具链且探针构建成功；两者是不同时间点，移植验证前需重新确认环境 |
| README 说“本轮未运行测试”，CHANGELOG 3.4.0 说真机 124 通过 | README 的“本轮”指 3.3.0 轮；3.4.0 已更新验证声明 |
| `PORTING-NOTES` 说“图片等元素未移植”，`board-porting-notes` 说画板图片/视频已实现 | 两者不冲突：屏幕批注层的通用元素未移植，画板层媒体对象已实现 |

---

## 8. 移植分类汇总（给 manifest 的初始 `status/portClass`）

### 8.1 可 1:1 纯逻辑（优先做共享黄金向量）

`ink-stroke-model`, `ink-taper-causal`, `ink-stroke-region`, `ink-erase`, `ink-undo-history`, `ink-selection`, `ink-shape-generator`, `ink-shape-recognition`, `ink-touch-classifier`, `ink-palm-rejection`, `ink-pixel-compositing`, `ink-replay-logic`, `board-service`, `board-persistence`, `ppt-ink-session`, `ppt-file-store`, `tool-autosave`, `system-settings-store`, `system-icink`, `system-filelog`, `system-update` 的版本/发布/策略部分。

向量建议（与设计文档 G4 对齐）：笔锋三型全序列、24 图形全量点坐标、识别判定集、选区矩阵、撤销序列、设置往返、回放时钟、触摸分档、像素合成、`.icink`/画板/PPT manifest 往返。

### 8.2 需要平台适配（业务可复用，UI/IO 重写）

`ink-gestures`, `ink-slide-flip`, `ui-floating-bar`, `ui-pen-palette`, `ui-shape-picker`, `ui-touch-button`, `ui-theme-tokens`, `ui-motion`, `ui-settings`, `ui-guide-changelog`, `ui-yes-no`, `ui-mouse-mode`, `ui-startup-args`, `board-ui`, `board-images`, `board-video`, `board-tools`, `ppt-navigation-ui`, `ppt-shell-integration`, `tool-countdown`, `tool-lottery`, `tool-replay-ui`, `system-app-services`。

### 8.3 强绑 Windows（必须替换或明确放弃）

`ink-overlay-render`, `ink-layered-window`, `ui-window-chrome`, `ui-hotkeys`, `ui-selftest`, `ppt-com-interop`, `tool-screenshot`, `tool-launchers`, `system-update` 的下载/安装部分, `system-edge-gesture`, `system-autostart`, `system-foreground-autofold`, `platform-desktop-acrylic`。

### 8.4 建议明确不移植（drop）

`not-ported-notification-bar`, `not-ported-cycle-processbar`, `not-ported-window-memory`, `not-ported-legacy-isf`, `board-screen-elements`（屏幕层通用元素）。
另外以下 Windows 专有能力应写“不适用”而非“移植”：Win32 全局热键、DWM 圆角、GDI 截图、注册表自启、`.exe` 自动更新、PowerPoint/WPS COM。

---

## 9. 覆盖率核算与开放问题

### 9.1 建议的 manifest 计法

1. 以第 2 节的 62 个功能单元为顶层 `features`；每个单元可再拆子项（本报告列出的可核查行为约 240 项）。
2. 状态取值：`ported`（已实现且行为等价）、`adapted`（已实现但平台适配/有差异）、`not-ported`（明确不移植 + `notes` 原因）。
3. 覆盖率 = `(ported + adapted) / total`，目标 ≥98%。若按 62 等权，5 个 drop 为 8.1%，不达标；建议：
   - 合并 5 个 drop 为 1 个“桌面专有附加”单元 → 58 单元，1 个 drop = 1.72%，达标；或
   - 以 ~240 行为子项计，5 个 drop 行为 = 2.08%，接近但仍略超；需再细分子项或把“旧格式读取”标为兼容性缺口而非功能。
4. 每个 `not-ported` 必须在 `notes` 写明原因与用户影响；不得把“DeferredRow 显示但未接线”标成 `ported`。

### 9.2 开放问题（需在动手移植前定）

1. **覆盖率分母口径**：62 顶层 / 240 行为 / 合并 drop，三者选一；这直接决定 98% 是否可达。
2. **画板 `HighlighterAlpha` 缺失**：保留 WPF/3.4.0 兼容 bug，还是升 `Version` 修复？两种选择会影响双向文件互认。
3. **PPT 轮询值**：契约取 200/1000ms（会话建议）还是 200/900ms（Shell 实际）？两侧必须同值。
4. **`CountdownTimer` 死代码**：Features 的 `CountdownTimer` 无消费方，实际行为在 `CountdownWindow`（自适应 20/30/50/100ms）。移植时以窗口为准还是把类改成共享契约？
5. **Windows 专有功能处置**：全局热键、DWM 圆角、注册表自启、`.exe` 更新、PPT COM、EasiCamera 启动器，HarmonyOS 上是“替代实现”还是“not-ported”？需产品确认。
6. **屏幕层通用元素**：是否列入 98% 分母？当前明确未移植；若上游用户依赖，需补 ArkTS 元素层。
7. **手机档功能面**：设计 G3 说手机“初步”，需明确手机端裁剪哪些功能（预计浮动栏/画板/PPT 导航/多窗口），并在 `parity/api-matrix.md` 写清。
8. **图标映射**：Segoe 私用区码点在 HarmonyOS 无对应字体，需产出完整“码点→Symbol/自绘”表；本报告 5.4 是输入，不是最终映射。
9. **多窗口/悬浮窗能力**：WinUI 的多个 Topmost 无边框窗口在 HarmonyOS 需要 `window`/`subWindow`/`overlay` 能力；API 24 与 26 行为差异需实测。
10. **触控/笔压输入**：`OH_PointerEvent` 是否提供接触矩形（palm/接触宽度）？若不提供，`PalmRejector` 与 `TouchContactClassifier` 需要降级策略，必须列为 adapt/未验证。
11. **视频/媒体**：`MediaPlayerElement` → HarmonyOS `Video`/AVPlayer；大文件（512 MiB）内嵌 base64 在 HarmonyOS 的文件/内存策略需重新设计。
12. **截图**：`Display`/`windowSnapshot` 或 `image` 能力能否覆盖 PPT 截图与全屏截图；权限与安全窗口限制需实测。
13. **设置文件兼容**：HarmonyOS 沙箱路径与 `%APPDATA%` 不同，但键名保持；`AutoSavedStrokesLocation` 默认 `D:\Ink Canvas` 在 HarmonyOS 无意义，需迁移策略（保持键名、改默认值，或首次导入时重写）。
14. **`CountdownRing`/`Motion`**：Win2D/Composition 动画需改为 ArkUI Canvas/属性动画；入场/弹簧参数需重标定。
15. **验证环境**：CLAUDE.md 与 harness notes 对 SDK 是否就位说法不一致；移植前重新确认 `E:\EcoStudioCLI` 与 HAP 构建可用性。

---

## 10. 参考

- `docs/PORTING-NOTES.md`：逐轮移植对照、刻意未移植项、仍未移植表。
- `docs/menu-porting-notes.md`：调色盘色序/范围、设置字段接线状态、PPT 菜单语义、验证声明。
- `docs/board-porting-notes.md`：白板源文件行号、三栏布局、分页/99 页、媒体、保存/加载、差异。
- `docs/ppt-porting-notes.md`：PPT/WPS 适配层、快照、会话、文件存档、宿主接线、验证缺口。
- `docs/menu-verification-status.md`：3.3.0 轮跳过行为验证、3 项几何测试例外、96 项未运行。
- `CHANGELOG.md`：3.0.0–3.4.0 功能与验证声明。
- `README.md`：架构、透明层取舍、笔锋修复、已知兼容性中断、覆盖层性能。
- `docs/superpowers/evidence/runtime-selftest.json`：自检结果与回读数据。
- `docs/superpowers/research/harmony/01-core-ink.md` … `06-features.md`：核心/域/平台/Ink/控件/功能层的深度常量盘点，本报告与之互补（本报告是清单与分母，01–06 是实现细节）。
