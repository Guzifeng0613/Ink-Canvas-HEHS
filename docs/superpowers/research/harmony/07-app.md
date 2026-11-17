# 07 — App 层源码盘点（WinUI 3 外壳 → HarmonyOS ArkTS）

> 日期：2026-11-18
> 仓库：`D:\InkCanvas`，分支 `feat/winui3-migration`，基线 commit `0d26d63` + **工作树未提交的 3.4.0 改动**（本文以工作树为准）。
> 范围：`src/InkCanvas.App/` 全部源码（27 个 `.cs`、`App.xaml`、`App.xaml.cs`、`InkCanvas.App.csproj`、`app.manifest`；不含 `bin/`、`obj/`）。
> 性质：**只读盘点**。本轮没有构建、没有启动应用、没有接触摸屏/压感笔/PowerPoint/投影，也没有运行 `--selftest` / `--shot` / `--diag`。文中“未验证”均指本轮未运行验证。
> 记号：`DIP` = WinUI 逻辑像素（与 HarmonyOS `vp` 按 1:1 设计）；`px` = 物理像素；`S` 为各窗口自带的 WPF Viewbox 缩放系数（定义见对应小节）。

---

## 0. 一句话结论

App 层不是业务逻辑层，而是**组合根 + Windows 窗口编排器**：

1. `AppServices` 装配 Core/Features 服务并把 `Settings.json` 翻译成控制器参数；
2. `ShellController` 拥有全部窗口、全局热键、900 ms 维护轮询，并实现「屏幕画笔 / 画板 / PPT 放映 / 鼠标穿透 / 收纳」五种运行态的组合状态机；
3. `Views/` 是 14 个独立 WinUI 3 顶层窗口类（浮动栏、画板、计时器、抽奖、回放、设置、指南、是否确认、PPT 导航、笔/荧光笔调色盘、图形选择器，以及 internal 的更多/手势面板、收纳把手、名单导入），全部依赖 Win32 置顶/Z 序/窗口区域裁剪/DPI 落位；
4. `Diagnostics/SelfTest.cs` 是唯一的应用内运行时自检，不在单元测试体系内。

对 HarmonyOS 的关键结论：**窗口与输入模型是最大移植风险**。HarmonyOS 三方应用没有“覆盖全桌面的置顶透明输入窗 + 多个独立置顶子窗 + 全局热键 + 全屏截屏”这一整套能力；App 层的纯逻辑只占少数（设置映射、模式判定、计时/抽奖/回放算法、选区矩阵与撤销），其余需要重新设计为单 UIAbility 内的子窗/浮层或降级。

---

## 1. 分层位置、依赖与文件清单

```
Core（纯域模型：Ink/Document/Shapes/Settings/Replay/Update）
  ↑
Platform（Win32 P/Invoke：LayeredInkWindow / Hotkey / Screen / Foreground / AutoStart / Screenshot）
  ↑
Ink（InkController / OverlayInkPresenter / InkSurface / InkSceneRenderer）
  ↑
Controls（FloatingBar / PenPalette / ShapePreview / CountdownRing / TouchIconButton / PopupBrushes / Motion / Glyphs）
  ↑
Features（BoardService / PptInteropService+PptInkSession / AutoSave / LotteryService / UpdateService / InkFileStore）
  ↑
App（本文：组合根 + ShellController + 全部窗口 + 自检）
```

- `InkCanvas.App.csproj`：`net10.0-windows10.0.26100.0`，`TargetPlatformMinVersion 10.0.17763.0`，`AssemblyName InkCanvas`，`Version 3.4.0`，**非 MSIX 打包**（`EnableMsixTooling=false`、`WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`、`SelfContained=false`）。
- 包依赖：Win2D `Microsoft.Graphics.Win2D`、Windows App SDK WinUI/Runtime/Foundation/InteractiveExperiences/DWrite 1.8 系列。
- 直接项目引用：Core / Platform / Ink / Controls / Features。
- `App.xaml` 只做两件事：合并 `XamlControlsResources` 与 `ms-appx:///InkCanvas.Controls/Themes/Tokens.xaml`（设计 token 的单一来源）。
- `app.manifest`：`PerMonitorV2` + legacy `dpiAware`、`longPathAware=true`，Windows 10/11 supportedOS GUID；没有 UAC/COM 清单项。
- 资源：`Assets/InkCanvas.ico`、`Assets/SegoeFluentIcons.ttf`、`Assets/SegoeMDL2Assets.ttf`、`Assets/TimerDownNotice.wav`、根目录 `CHANGELOG.md`（复制到输出目录）。**图标字体随包发布，不依赖系统字体**。

### 1.1 文件与职责

| 文件 | 行数 | 职责 |
|---|---:|---|
| `App.xaml.cs` | 186 | 进程启动、异常兜底、命令行自检钩子 |
| `Services/AppServices.cs` | 103 | 组合根、`ApplySettings` 唯一翻译点 |
| `Services/ShellController.cs` | 860 | 窗口编排、模式状态机、PPT 轮询、热键动作、退出 |
| `Services/OverlayHost.cs` | 97 | 透明覆盖层窗口/Presenter/热键生命周期 |
| `Services/UpdateCoordinator.cs` | 96 | 检查更新→确认→下载→安装的唯一决策点 |
| `Services/WindowChrome.cs` | 613 | 所有窗口共用的无边框/DPI/落位/裁剪/Z 序/拖动/标题栏 |
| `Services/ChromeBackdrop.cs` | 62 | 桌面亚克力（DesktopAcrylic）背板 |
| `Diagnostics/SelfTest.cs` | 617 | `--selftest` 运行时自检管线与 JSON 报告 |
| `Diagnostics/EraserRegression.cs` | 66 | 橡皮回归（区域擦/线条擦/其他触点/撤销） |
| `Diagnostics/RenderingRegression.cs` | 92 | 墨迹栅格化与几何缓存回归（Win2D） |
| `Diagnostics/SelectionRegression.cs` | 204 | 框选/克隆/旋转/混合元素/双指选择回归，并输出 `selection-ui.png` |
| `Diagnostics/SelectionWindowRegression.cs` | 112 | 真实 BoardWindow + Win32 鼠标注入的 XAML 选择路由回归 |
| `Diagnostics/PerformanceCheck.cs` | 93 | `--perfcheck` 内存/渲染工作负载采样 |
| `Views/FloatingBarWindow.cs` | 304 | 浮动栏宿主窗口、收纳把手、两个弹出面板 |
| `Views/BarPanelWindow.cs` | 204 | 更多设置 / 手势选项独立弹窗 |
| `Views/FoldedBarHandleWindow.cs` | 66 | 收纳后左右边缘把手 |
| `Views/BoardWindow.cs` | 2166 | 画板全屏窗口：三浮条、选区面板、控制器统一选择、文件 IO |
| `Views/BoardImageLayer.cs` | 189 | 每页图片层（几何/选中视觉，输入由控制器统一处理） |
| `Views/BoardMediaLayer.cs` | 224 | 每页视频层（MediaPlayerElement/几何/选中视觉，输入由控制器统一处理） |
| `Views/CountdownWindow.cs` | 894 | 计时器窗口 + `MinSizeHook` |
| `Views/LotteryWindow.cs` | 630 | 抽奖窗口 + 名单导入窗口 |
| `Views/ReplayWindow.cs` | 174 | 墨迹回放窗口 |
| `Views/SettingsWindow.cs` | 927 | 440 DIP 设置面板（全部设置分区） |
| `Views/PickerWindows.cs` | 501 | 笔/荧光笔调色盘窗口 + 图形选择窗口 |
| `Views/PptNavigationWindow.cs` | 276 | PPT 底部/侧边导航面板（四种放置） |
| `Views/InfoWindow.cs` | 364 | 使用指南（键位表）/ 更新日志（Markdown）窗口 |
| `Views/YesOrNoWindow.cs` | 151 | 450×160 置顶是否确认窗 |

### 1.2 公开类型与消费方（App 层）

| 类型 | 关键公开成员 | 直接消费方 | 分类 |
|---|---|---|---|
| `App` | `OnLaunched`、`OnUnhandledException` | WinUI 运行时 | adapt |
| `AppServices` | `Store/Settings/OverlayInk/Board/Ppt/AutoSave/Lottery`、`ApplySettings`、`AllControllers`、`SaveSettings`、`Reload`、`Dispose` | `App`、`ShellController`、`SettingsWindow`、`BoardWindow`、`SelfTest` | pure-logic（映射）/ adapt（装配） |
| `OverlayHost` | `Window/Presenter/Hotkeys/Controller`、`ClickThrough`、`SetInteractionMode`、`Show/Hide`、`RegisterHotkeys` | `ShellController`、`SelfTest` | platform-bound |
| `ShellController` | 实现 `IShellActions` 全部 20 个动作；`Start`、`Services`、`Overlay`、`Updater`、`OpenWindows`、`WindowOpened`、`BarDiagnostics`、`Exit`、`Dispose` | `App`、`FloatingBar`（经由 `IShellActions`）、`SelfTest` | adapt |
| `UpdateCoordinator` | `Confirm/Notify/ExitForInstall`、`CurrentVersion`、`CheckAsync(bool)` | `ShellController`、`SettingsWindow` | adapt |
| `WindowChrome`（static） | `GetHwnd`、`MakeBorderless`、`Place/PlaceClient/PlaceDip`、`GetClientPosition/GetScreenBounds/GetWorkArea/GetPopupPosition`、`TrackAnchor`、`ClipToSurfaces`、`EnableDragMove`、`AddTitleBar`、`SuppressTouchFeedback`、`ApplyTheme`、`EnsureAbove`、`CenterOnPrimary`、`SetRoundedCorners`、`GetDpiScale` | 全部窗口、`SelfTest` | platform-bound |
| `ChromeBackdrop`（internal） | `State`、`OnTargetConnected` | `FloatingBarWindow.EnableTransparency` | platform-bound |
| `FloatingBarWindow` | `Bar`、`GetToolAnchor`、`IsFolded`、`SetFolded`、`Configure`、`Reposition`、`GetToolAnchor`、`UserScale`、`FoldHandles`、`EnsureHandlesAbove`、`ClosePanels`、`OnPanelClosed` | `ShellController`、`BarPanelWindow`、`FoldedBarHandleWindow`、`SelfTest` | adapt |
| `BarPanelWindow`（internal） | `IsMore`、`ShowAboveBar`、`PlaceAboveBar` | `FloatingBarWindow`、`SelfTest` | adapt |
| `FoldedBarHandleWindow`（internal） | `ShowHandle` | `FloatingBarWindow` | adapt |
| `BoardWindow` | `CurrentController`、`CurrentPageChanged`、`RefreshUiScale`、`GetToolAnchor`、`Close`（继承） | `ShellController`、`SelfTest` | adapt |
| `BoardImageLayer`（internal） | `SelectedImage`、`SetSelection`、`SelectionChanged`、`SetPage`、`Refresh`、`UpdateViewTransform`、`UpdateElement`、`ClearSelection` | `BoardWindow` | adapt |
| `BoardMediaLayer`（internal） | `SelectedMedia`、`SetSelection`、`SelectionChanged`、`SetPage`、`Refresh`、`UpdateViewTransform`、`UpdateElement`、`GetViewElement`、`ClearSelection`、`Dispose` | `BoardWindow` | platform-bound（MediaPlayer） |
| `CountdownWindow` | 构造(theme)、`Dispose` | `ShellController` | adapt |
| `MinSizeHook`（internal） | 构造(window,minW,minH)、`TryGetMonitorBounds`、`Dispose` | `CountdownWindow`、`InfoWindow` | platform-bound |
| `LotteryWindow` | 构造(lottery, autoClose, theme)、静态 `DataDir/NamesFilePath/ReplaceFilePath` | `ShellController` | adapt |
| `NamesInputWindow`（internal） | 构造(theme) | `LotteryWindow` | adapt |
| `ReplayWindow` | 构造(strokes, theme) | `ShellController` | adapt |
| `SettingsWindow` | 构造(services, updater, shell)、`SettingsChanged` | `ShellController` | adapt |
| `PptNavigationWindow` + `PptNavigationPlacement` | `Update(snapshot, panelVisible, counterVisible)` | `ShellController` | adapt |
| `InfoWindow` | `OpenGuide(theme)`、`OpenChangeLog(theme)` | `ShellController`、`SettingsWindow` | adapt |
| `YesOrNoWindow` | 静态 `AskAsync(owner,text)` | `ShellController`、`BoardWindow`、`LotteryWindow` 等 | adapt |
| `PenPaletteWindow` | 构造(controller,highlighter,theme,scale)、`PlaceAboveBar`、`ApplyScale`、`DismissOnOverlayInput`、`RefreshFromController` | `ShellController`、`SelfTest` | adapt |
| `ShapePickerWindow` | 构造(controller,onPicked,theme,scale,boardMode)、`PlaceAboveBar`、`ApplyScale`、`DismissOnOverlayInput`、`AutoHide` | `ShellController`、`SelfTest` | adapt |
| `SelfTest`（static） | `Run(shell, outputDirectory)` | `App --selftest` | platform-bound |

---

## 2. 启动、组合根与命令行钩子

### 2.1 进程启动顺序（`App.xaml.cs:10-168`）

1. `App()`：`InitializeComponent()`；订阅 `UnhandledException`；订阅 `DispatcherQueue.ShutdownCompleted`、`AppDomain.ProcessExit`、`AppDomain.UnhandledException`，全部写 `Core.Diagnostics.FileLog`。
2. `OnLaunched`：
   - `var services = new AppServices();`
   - `_shell = new ShellController(services); _shell.Start();`
   - 然后依次解析命令行：`--board`、`--passthrough`、`--diag=`、`--open=`、`--shot=`、`--perfcheck=`、`--selftest=`。**所有开关都在 shell 完整启动之后才执行**，所以 `--selftest` 看到的是已经显示浮动栏、overlay、轮询已启动的状态。
3. `AppServices` 构造顺序（`AppServices.cs:17-29`）：
   `SettingsStore` → `Settings = Store.Load()` → `BoardService` → 用 `ThemeResolver.GetBoardTheme(UsingWhiteboard)` 决定 `Board.Background`（白板→White，否则 Black）→ `PptInteropService` → `AutoSaveService(Automation)` → `LotteryService` → `ApplySettings()`。
   字段 `OverlayInk = new InkController()` 在构造体之前初始化。
4. `ShellController` 构造（`ShellController.cs:40-83`）：
   - `Overlay = new OverlayHost(services.OverlayInk)`；
   - 创建 `PptInkSession`（`PptInkFileStore(AutoSavedStrokesLocation)`，注意**路径在构造时固定**）；
   - 接好 `CaptureCurrentInk/ApplyCurrentInk/AutoSave.../MinimumScreenshotStrokeCount` 回调；
   - 订阅 `PresentationNotice`、`ScreenshotRequested`、`Failed`；
   - 立即 `_pptSession.Poll()`（构造期即可能触碰 COM）；
   - `Updater = new UpdateCoordinator(...)`；
   - 创建 900 ms `_pollTimer`，但 `Start()` 才 `Start()`。
5. `Start()`（`ShellController.cs:128-191`）的顺序（不可随意调换）：
   创建 `FloatingBarWindow` → `NotifyWindowOpened` → `Bar.Settings = Services.Settings` → `Configure(OverlayInk, this)`（内部 `Build()`）→ `LayoutChanged += OnBarLayoutChanged` → `Reposition(BottomMargin, FloatingBarScale)` → `WindowChrome.ApplyTheme(bar, Theme)` → `bar.Activate()`；
   `Overlay.Show()`；
   订阅 `Overlay.Window.InputBegan`：`RaiseChromeAboveOverlay()`、`bar.ClosePanels()`、`palette/shapePicker.DismissOnOverlayInput()`；
   若 `Startup.IsFoldAtStartup` → `bar.SetFolded(true)`；
   `Overlay.RegisterHotkeys(...)`（见 §3.2）；
   `_pollTimer.Start()`；
   订阅 `OverlayInk.SlideFlipRequested`、`ToolChanged`；设置 `OverlayInk.AskRemoveAsymptotes`；
   写启动日志 `Ink Canvas 启动，版本 {CurrentVersion}`；
   `ApplyEdgeGestureSetting()`；
   若 `Startup.IsAutoUpdate` → 后台 `Updater.CheckAsync(interactive:false)`。

### 2.2 命令行开关（精确语义）

| 开关 | 行为 | 关键常量 |
|---|---|---|
| `--board` | 对命令行中每个匹配项调用一次 `_shell.ToggleBoard()`（若出现两次会进出各一次） | 无 |
| `--passthrough` | `_shell.StartInClickThrough()`：若已穿透则返回，否则 `ToggleClickThrough()`（会先执行 `SaveBeforeMouseMode`） | 无 |
| `--diag=<path>` | 打开 `OverlayInkPresenter.LatencyLogEnabled=true`；创建**700 ms 重复** DispatcherQueueTimer，每 tick 覆盖写一行诊断 | 700 ms |
| `--open=<k1,k2,...>` | 逗号拆分入队；创建 **1100 ms 重复** timer，每 tick 取一个 kind 调用对应 shell 方法；队列空时停止 | 1100 ms |
| `--shot=<dir>` | 创建目录；对当前 `_shell.OpenWindows` 每个窗口挂 `Activated` 钩子，并对之后 `WindowOpened` 的窗口挂同一钩子；窗口激活后延迟 **700 ms** 自渲染 PNG | 700 ms |
| `--perfcheck=<dir>` | 创建目录；**2 s 单次** timer 后 `await Diagnostics.PerformanceCheck.Run(_shell, dir)`，输出 `performance.json` | 2000 ms |
| `--selftest=<dir>` | 创建目录；**1200 ms 单次** timer 后 `await Diagnostics.SelfTest.Run(_shell, dir)` | 1200 ms |

`--open` 的 kind 映射（`App.xaml.cs:81-90`）：`settings`→`OpenSettings`、`countdown`→`OpenTimer`、`lottery`→`OpenLottery`、`guide`→`OpenGuide`、`replay`→`OpenReplay`、`pen`→`OpenPenPalette(false)`、`highlighter`→`OpenPenPalette(true)`、`shapes`→`OpenShapePicker`、`board`→`ToggleBoard`；其余字符串忽略。kind 前后 `Trim()`。

`--diag` 输出**精确格式**（`App.xaml.cs:53-60`，每行末尾 `\n`）：

```
pointerMessages={PointerMessageCount} (down={PointerDownCount} move={PointerMoveCount} up={PointerUpCount}); mouseMessages={MouseMessageCount}; mouseAsPointer={LayeredInkWindow.MouseAsPointer}; clickThrough={ClickThrough}; visible={IsVisible}; strokes={OverlayInk.Document.Count}; live={OverlayInk.LiveStrokes.Count}; tool={OverlayInk.Tool}
```

`--shot` 实现细节（`App.xaml.cs:104-133`）：
- 只对 `w.Content is FrameworkElement fe && fe.XamlRoot is not null` 生效；
- `RenderTargetBitmap.RenderAsync(fe)` → `GetPixelsAsync` → `BitmapEncoder.PngEncoderId`；
- 像素格式 `Bgra8`，alpha `Premultiplied`，DPI 96×96；
- 文件名 = `w.Title ?? "window"`，按 `Path.GetInvalidFileNameChars()` 拆分后用 `_` 连接（`string.Join("_", title.Split(invalidChars))`），扩展 `.png`；
- 所有异常静默；`--shot` 与 `--open` 同时使用时，shot 钩子在 open timer 第一次 tick 之前完成注册。
- 注意：`OpenWindows` 不包含 `PptNavigationWindow`、`YesOrNoWindow`、`NamesInputWindow`、`OpenLotteryQuick()` 的临时窗；这些窗口也不会触发 `WindowOpened`。

### 2.3 `AppServices.ApplySettings()` 精确映射（`AppServices.cs:47-82`）

对 `AllControllers()`（`OverlayInk` + `Board.Pages` 中每一页）逐个执行：

| 目标 | 公式/映射 |
|---|---|
| `CurrentAttributes.BaseWidth` | `Math.Clamp(Canvas.InkWidth, 0.5, 60)`（float） |
| `CurrentAttributes.TaperKind` | `Canvas.InkStyle`：`1 → Velocity`，`2 → None`，其它（含 0）→ `Tail` |
| `CurrentAttributes.HighlighterAlpha` | `(byte)Math.Clamp(Canvas.InkAlpha, 20, 240)` |
| `EraserRadius` | `45f * {0.5,0.8,1,1.25,1.8}[Math.Clamp(Canvas.EraserSize, 0, 4)]` |
| `RecognizeShapes` | `InkToShape.IsInkToShapeEnabled` |
| `HyperbolaAsymptoteOption` | `Canvas.HyperbolaAsymptoteOption`（枚举 `Yes=0,No=1,Ask=2`） |
| `MultiTouchEnabled` | `Gesture.IsEnableMultiTouchMode` |
| 手势开关 | `ConfigureGestures(Gesture.IsEnableTwoFingerZoom, Gesture.IsEnableTwoFingerTranslate, Gesture.IsEnableTwoFingerRotation)` |
| `Palm.PalmContactThreshold` | `Math.Max(8, Advanced.FingerModeBoundsWidth)` |
| `GesturesTargetSelection` | `Gesture.IsEnableTwoFingerRotationOnSelection` |
| `Palm.Advanced` | 整个 `Settings.Advanced` 对象引用 |
| `Palm.NibMode` | `Startup.IsEnableNibMode` |
| `TransformCenterOption` | `Gesture.MatrixTransformCenterPoint`（`CanvasCenterPoint=0, SelectedElementsCenterPoint=1, GestureOperationCenterPoint=2`） |

控制器之外：
- `Ppt.AllowWps = PowerPoint.IsSupportWPS`
- `Lottery.PeopleCount = Rand.PeopleCount`
- `Lottery.NoRepeat = Rand.IsNoRepeatName`
- `FileLog.Enabled = Advanced.IsLogEnabled`

注意：
- `ApplySettings` **不会**把 `Canvas.UsingWhiteboard` 重新应用到 `Board.Background`；该值只在 `AppServices` 构造时决定初始背景，运行中修改 `UsingWhiteboard` 不会立即换板（`SettingsWindow` 也把它标成 Deferred）。
- `Advanced.TouchMultiplier` / `IsSpecialScreen` / `IsQuadIR` 等通过 `Palm.Advanced` 引用间接生效，App 层不再逐项复制。
- `AllControllers()` 是 `yield return` 序列：先 overlay，再逐页。
- `Reload()` 会重新 `Store.Load()` 后 `ApplySettings()`；**全仓库没有任何调用方**（死代码），`SettingsWindow` 走的是 `ApplySettings + SaveSettings`。

### 2.4 异常兜底与日志

- `OnUnhandledException`：写 `FileLog`（Error 级）；再向 `%APPDATA%\InkCanvas\error.log` 追加 `{DateTime.Now:O} {exception}\n\n`；最后 `e.Handled = true`（课堂工具“尽量不消失”策略）。
- 进程退出路径均只写一行日志：`Application dispatcher shutdown completed`、`ProcessExit`、`AppDomain unhandled: {exception}`。
- `FileLog` 的目录/文件名/行格式由 Core 决定（`%APPDATA%\InkCanvas\Log.txt`，`{ISO} [{Type}] {msg}`），App 只负责开/关（`Advanced.IsLogEnabled`）。

---

## 3. 透明覆盖层宿主 `OverlayHost`

### 3.1 构造与生命周期（`OverlayHost.cs:15-56`）

1. `var screen = ScreenService.Primary;`
2. `Window = new LayeredInkWindow(screen.X, screen.Y, screen.Width, screen.Height)`——全屏、逐像素 alpha 的 Win32 分层窗口（Platform 层）。
3. `Presenter = new OverlayInkPresenter(Window, controller)`。
4. `Hotkeys = new HotkeyService(Window.Handle)`。
5. `controller.ViewCenter = new Vector2(screen.Width / Window.DpiScale / 2f, screen.Height / Window.DpiScale / 2f)`（DIP 中心）。
6. `Window.HotkeyPressed += id => Hotkeys.Dispatch(id)`；`Presenter.Attach()`。
7. `ClickThrough` getter/setter：setter 调 `SetInteractionMode(value, inkVisible: !value)`，即**开启穿透就隐藏墨迹**；`Show()` 会 `Window.Show()` 后再次 `Presenter.Attach()`；`Hide()` 先 `Presenter.Suspend()` 再 `Window.Hide()`（隐藏时释放/暂停呈现，避免后台空转）。
8. `Dispose()` 顺序：`Hotkeys.Dispose()` → `Presenter.Dispose()` → `Window.Dispose()`。

### 3.2 全局热键注册表（`OverlayHost.RegisterHotkeys`，`OverlayHost.cs:64-85`）

| 修饰键 | 虚拟键 | 动作 | 是否可选 |
|---|---|---|---|
| `Control` | `0x5A` Z | undo | 必填 |
| `Control` | `0x59` Y | redo | 必填 |
| `Control` | `0x45` E | clear | 必填 |
| `Alt` | `0x45` E | toggleEraser | 必填 |
| `Alt` | `0x53` S | select | 可选 |
| `Alt` | `0x4C` L | line | 可选 |
| `Alt` | `0x42` B | toggleBoard | 可选 |
| `Alt` | `0x56` V | fold | 可选 |
| `Alt` | `0x43` C | screenshot | 可选 |
| `Alt` | `0x44` D | draw / palette | 可选 |
| `Alt` | `0x51` Q | togglePassThrough | 可选 |
| `None` | `0x22` PageDown | pptNext | 可选 |
| `None` | `0x21` PageUp | pptPrevious | 可选 |
| `Shift` | `0x1B` Escape | pptExit | 可选 |

`HotkeyService.Register` 的失败不致命（键可能被其他程序占用）。`HotkeyModifiers` 数值：`None=0, Alt=1, Control=2, Shift=4, Win=8, NoRepeat=0x4000`。

热键回调在 `ShellController.Start` 中闭包绑定（`ShellController.cs:152-176`）：PageUp/Down/Shift+Esc 均先判断 `_lastSlideShowState`；`draw` 回调是“穿透中→切回标注；已开调色盘→关闭；否则打开笔调色盘”的三态逻辑。

## 4. `ShellController`：窗口编排与模式状态机

### 4.1 拥有的窗口与打开策略

字段（`ShellController.cs:23-38`）：`_bar`、`_board`、`_settings`、`_countdown`、`_lottery`、`_replay`、`_guide`、`_palette`、`_shapePicker`、`_pptBottomLeft/_pptBottomRight/_pptSideLeft/_pptSideRight`；状态：`_lastSlideShowState`、`_lastAutoFoldRequested`、`_manualFoldOverride`（`bool?`）、`_exiting`、`_paletteHighlighter`、`_boundBoardController`。

打开规则：

| 方法 | 单例策略 | 备注 |
|---|---|---|
| `OpenSettings` | 已开则 `Activate()` | 构造后订阅 `SettingsChanged`；关闭置 null；`WindowChrome.EnsureAbove(_settings)` |
| `OpenTimer` | 已开则 Activate | `CountdownWindow(EffectiveTheme)` + `ApplyTheme` |
| `OpenLottery` | 已开则 Activate | `LotteryWindow(Lottery, autoClose:false, EffectiveTheme)` |
| `OpenLotteryQuick` | **不登记、不做单例** | `autoClose:true`；无 Closed 处理，窗口自行关闭 |
| `OpenReplay` | 已开则 Activate | 打开时快照当前 surface：board 存在取 `Board.Current.Document.Strokes`，否则取 `OverlayInk.Document.Strokes` |
| `OpenGuide` | 已开则 Activate | `InfoWindow.OpenGuide(EffectiveTheme)` |
| `OpenPenPalette(highlighter)` | **同类型再点一次即关闭**（WPF 721-731） | 不同类型先关旧再开；`_paletteHighlighter` 记录 |
| `OpenShapePicker` | 每次先关闭旧窗再新建 | 无“同键切换关闭” |
| `ToggleBoard` | `_board == null` 则新建，否则 `_board.Close()` | 见 §4.3 |
| PPT 导航 ×4 | 按设置懒创建；条件不满足全部 Close | 见 §4.5 |

`OpenWindows` 只枚举：bar、board、settings、countdown、lottery、replay、guide、palette、shapePicker；**不含** PPT 导航、YesOrNo、NamesInput、quick lottery。

`WindowOpened` 事件只在上述登记窗口创建时触发（`NotifyWindowOpened`），供 `--shot` 挂钩。

### 4.2 `EffectiveTheme`

`EffectiveTheme`（`ShellController.cs:228-230`）：

- 画板打开时：`Settings.Canvas.UsingWhiteboard ? 1 : 2`（白板→浅色，黑板→深色，**不跟随应用主题**）；
- 否则：`Settings.Appearance.Theme`（0 跟随系统 / 1 浅 / 2 深）。

消费方：倒计时、抽奖、回放、指南的构造与 `ApplyTheme`。注意：`OpenPenPalette` 的 `LightColorScheme` 用的是 `Appearance.Theme == 1`（不是 `EffectiveTheme`），与画板主题可能不一致；`YesOrNoWindow.AskAsync` 则从 owner 的 `ActualTheme` 推导（Dark→2，否则 1）。

### 4.3 模式状态机

App 没有单一 `Mode` 枚举，运行态由四个正交布尔/引用组合而成：

| 维度 | 状态来源 | 进入/退出副作用 |
|---|---|---|
| 标注 vs 鼠标穿透 | `Overlay.ClickThrough` | 进入鼠标：先 `SaveBeforeMouseMode()`，再 `SetInteractionMode(true, showInk)`；退出：`SetInteractionMode(false, true)`；同步 `bar.ClickThroughActive` + `RefreshSelection` |
| 展开 vs 收纳 | `FloatingBarWindow._isFolded` | 收纳：关面板/把手、`AppWindow.Hide`、创建左右把手；展开：关把手、显示、`Reposition`、`Activate` |
| 屏幕画笔 vs 画板 | `_board != null` | 进画板：创建 `BoardWindow`、绑定 controller、`Overlay.Hide()`、非收纳时隐藏 bar 窗口；出画板：恢复 overlay、bar 显示/重排、controller 回 overlay |
| 非放映 vs PPT 放映 | `_lastSlideShowState` | 进入：显示 PPT 模式按钮、重排 bar、退出画板、按设置显示 overlay 并置红笔；退出：退出画板 |

组合语义：

1. **进入画板**（`ToggleBoard`，`ShellController.cs:483-527`）：
   - `new BoardWindow(Services, this)`；订阅 `CurrentPageChanged`；`BindBoardController(CurrentController)`；`bar.Bar.Controller = board.CurrentController`；订阅 `Closed`；`Activate()`；`Overlay.Hide()`；若 bar 未收纳则 `bar.AppWindow.Hide()`（**收纳时 bar 窗口本来就隐藏，把手保留**）。
   - 画板是**不透明全屏**窗口，覆盖层隐藏，避免墨迹画到画板上。
   - 退出：`_board.Close()` → `OnBoardClosed`：解绑、`_board=null`、`bar.Controller=OverlayInk`、`Overlay.Show()`、bar 未收纳时 `AppWindow.Show()` + `Reposition(..., recenter:false)`。
   - 换页 `OnBoardPageChanged`：bar 控制器重绑到新页；`_palette` 关闭并置 null（避免调色盘指向旧页）。
2. **鼠标穿透**（`ToggleClickThrough`，`ShellController.cs:765-787`）：
   - `enableMouseMode = !Overlay.ClickThrough`；
   - 进入时：
     - `SaveBeforeMouseMode()`：取 `ActiveController.Document.Strokes`；若 `Count <= Automation.MinimumAutomationStrokeNumber` 直接返回；否则若正在放映 → `_pptSession.RequestScreenshot()`，否则 `ScreenshotAsync()`；再 `AutoSave.SaveSnapshot(strokes, 放映 ? "ppt-mouse-mode" : "mouse-mode")`。
     - `showInk = 放映 ? PowerPoint.IsShowStrokeOnSelectInPowerPoint : !Canvas.HideStrokeWhenSelecting`；
     - `Overlay.SetInteractionMode(clickThrough:true, inkVisible:showInk)`。
   - 退出时：`SetInteractionMode(false, inkVisible:true)`（WPF `CursorIcon_Click` 恢复书写且不清墨迹）。
   - 注意阈值判定是 `<=`，而 `AutoSaveService.SaveSnapshot` 内部是 `< Math.Max(0, Minimum)`；两者在“等于阈值”时不一致（App 更严格）。
3. **收纳**（`ToggleFold`，`ShellController.cs:748-763`）：
   - 每次手动切换都把 `_manualFoldOverride = fold`，防止 `Poll` 用旧的自动判定把栏立刻重新收起/展开；
   - 收起的顺序：若当前不是穿透 → `ToggleClickThrough()`（WPF `FoldFloatingBar_Click` 先 `CursorIcon_Click`），再 `bar.SetFolded(true)`；展开只 `SetFolded(false)`（**overlay 仍是鼠标模式**，直到用户选笔）。
4. **自动收纳**（`ApplyAutoFold`，`ShellController.cs:446-479`）：
   - 取 `ForegroundWindowService.GetForegroundProcessName()`；
   - `a = Settings.Automation`；`fold = !string.IsNullOrEmpty(foreground) && a.IsEnableAutoFold && ( ... )`；
   - 匹配项（全部 `OrdinalIgnoreCase` `Contains`）：
     `IsAutoFoldInEasiNote`→`"EasiNote"`，
     `IsAutoFoldInEasiCamera`→`"EasiCamera"`，
     `IsAutoFoldInSeewoPincoTeacher`→`"Pinco"`，
     `IsAutoFoldInHiteTouchPro`→`"HiteTouch"`，
     `IsAutoFoldInMSWhiteboard`→`"Whiteboard"`，
     `IsAutoFoldInPPTSlideShow && _lastSlideShowState`。
   - `a.IsEnableAutoFold` 是 **Core 计算属性**：十个 auto-fold 开关的 OR，包括 `EasiNote3C / HiteCamera / WxBoardMain / OldZyBoard`；但 App 的匹配串**没有**这四项，所以仅开启这四项时主开关为 true 却永远不触发。
   - 自动折叠状态变化（`fold != _lastAutoFoldRequested`）时清空 `_manualFoldOverride`；`desiredFold = _manualFoldOverride ?? fold`。
   - `desiredFold && !IsFolded`：若未穿透先 `ToggleClickThrough()`，再 `SetFolded(true)`；`!desiredFold && IsFolded`：`SetFolded(false)`。
   - 最后 `if (desiredFold) Overlay.ClickThrough = true;`——**该 setter 会同时把 `InkVisible` 设为 false**，因此自动收纳生效期间每次 Poll 都会强制隐藏墨迹（即使进入鼠标模式时按设置本应显示墨迹）。这是需要移植时明确取舍的现存行为。
5. **PPT 放映**：见 §4.5；进入/退出都会 `ToggleBoard()`（若画板打开），即“放映开始离开画板、放映结束离开画板、翻页前离开画板”。

`ActiveController`（`ShellController.cs:199-201`）：`_board != null ? Board.Current : OverlayInk`。用于保存/打开墨迹、鼠标模式保存、选区虚线等。

### 4.4 900 ms 维护轮询 `Poll()`（`ShellController.cs:319-378`）

每 tick：

1. `_exiting` 直接返回。
2. 动态设置下一次间隔：
   `_pollTimer.Interval = _pptSession.Snapshot?.IsSlideShowRunning == true ? _pptSession.RecommendedPollInterval : TimeSpan.FromMilliseconds(900);`
   `PptInkSession.RecommendedPollInterval` 本身是 `show?200ms:1000ms`，因此**非放映时实际是 900 ms，而不是 1000 ms**。
3. 若 `PowerPoint.PowerPointSupport`：
   - `previous = _pptSession.Snapshot; current = _pptSession.Poll();`
   - `isShow = current.IsSlideShowRunning`；
   - 状态翻转时：
     - `_lastSlideShowState = isShow`；`bar.Bar.SetPptModeVisible(isShow)`；`bar.Reposition(..., recenter 默认 true)`；
     - 进入放映：若画板打开 `ToggleBoard()`；若 `IsShowCanvasAtNewSlideShow` → `Overlay.Show()` 并把笔色设为 `InkColor.Red`（WPF `lastDesktopInkColor=1`），刷新 bar 选择；
     - 退出放映：若画板打开 `ToggleBoard()`。
   - `UpdatePptNavigation(current)`。
   否则 `UpdatePptNavigation(PptSnapshot.Detached)`。
4. `ApplyAutoFold()`（无论 PPT 支持是否关闭都执行）。
5. `OverlayInk.Palm.PptGestureEligible = _lastSlideShowState && OverlayInk.Document.Count == 0 && PowerPoint.IsEnableFingerGestureSlideShowControl;`
6. `RaiseChromeAboveOverlay()`。

`RaiseChromeAboveOverlay`（`ShellController.cs:385-401`）：
- 画板不存在时 `WindowChrome.EnsureAbove(bar, Overlay.Handle)`；始终 `bar.EnsureHandlesAbove(Overlay.Handle)`；
- 画板、设置、计时、抽奖、回放、指南、四个 PPT 导航、调色盘、图形选择器逐个 `EnsureAbove(x, Overlay.Handle)`；
- 依赖 `EnsureAbove` 的“只在 overlay 实际压住本窗时才重排”逻辑（见 §5.3），避免抢焦点。

### 4.5 PPT 集成（App 侧接线）

1. **通知**（`ShowPptPresentationNoticeAsync`，`ShellController.cs:269-288`）：逐项 `YesOrNoWindow.AskAsync(owner, text)`，owner 为 `(Window)_board ?? _bar`。精确文案：
   - `上次播放到了第 {PreviousSlideIndex} 页, 是否立即跳转`（注意“页”后是**半角逗号加空格**）；选“是” → `_pptSession.GoToSlide(index)`；
   - `检测到此演示文档中包含隐藏的幻灯片，是否取消隐藏？` → `RestoreHiddenSlides()`;
   - `检测到此演示文档中自动播放或排练计时已经启用，可能导致幻灯片自动翻页，是否取消？` → `DisableSlideTimings()`;
   - `LegacyInkFilesDetected` → `NotifyAsync("发现旧版墨迹存档", "发现 .icstk/.icart 存档。该 WPF ISF 格式无法在 WinUI 3 直接读取；当前仅保留文件，不会覆盖或删除。")`。
2. **截图请求**（`OnPptScreenshotRequested`，`ShellController.cs:290-317`）：
   - 同步 `ScreenshotService.Capture(ScreenService.Primary)`（在 COM 导航改变幻灯片前抓屏）；
   - `path = request.SuggestedPath`；为空时：
     `Path.Combine(Automation.AutoSavedStrokesLocation, "Auto Saved - PPT Screenshots", DateTime.Now.ToString("yyyy-MM-dd"), $"{request.Snapshot.ShowPosition}_{DateTime.Now:HH-mm-ss}.png")`；
   - `Directory.CreateDirectory(dir)`；`ScreenshotService.SavePngAsync(path, pixels, w, h)`（fire-and-forget）；
   - 若 `request.SaveInkAlongside`：同名 `.icink`，`InkFileStore.Save(ink, request.Strokes)`。
3. **导航面板**（`UpdatePptNavigation`，`ShellController.cs:403-430`）：
   - 条件：`inShow && !bar.IsFolded && _board == null`，否则 `ClosePptNavigationWindows()` 并返回；
   - `IsShowBottomPPTNavigationPanel` → 懒创建 `BottomLeft` + `BottomRight`；
   - `IsShowSidePPTNavigationPanel` → 懒创建 `SideLeft` + `SideRight`；
   - 每个窗口 `Update(snapshot, panelEnabled, counterEnabled)`；
   - 创建时用 `Settings.Appearance.Theme`，不是 `EffectiveTheme`。
4. **翻页/退出**：
   - `PptNext()` / `PptPrevious()`：若画板打开先 `ToggleBoard()`，再 `_pptSession.Next()/Previous()`；
   - `PptExit()`：`_pptSession.ExitSlideShow()`，然后 `bar.ClosePanels()`、`_palette?.Close()`、`_shapePicker?.Close()`；
   - `PptOpenNavigation()`：非放映直接返回；若未穿透先 `ToggleClickThrough()`；再 `_pptSession.ShowSlideNavigationPane()`。
5. **会话构造回调**（`ShellController.cs:45-67`）：
   - `CaptureCurrentInk = () => OverlayInk.Document.Strokes`
   - `ApplyCurrentInk = strokes => { OverlayInk.Document.Clear(); AddRange(strokes); Selection.Clear(); History.Clear(); bar.RefreshSelection(); }`（**换页恢复墨迹不进撤销栈**）
   - `AutoSavePresentationInk / AutoSavePptScreenshots / NotifyPreviousPage / NotifyHiddenSlides / NotifySlideTimings` 全部直接读设置；
   - `AutoSaveRoot = Automation.AutoSavedStrokesLocation`；
   - `UseScreenshotDateFolders = Automation.IsSaveScreenshotsInDateFolders`；
   - `SaveInkWithScreenshot = Automation.IsAutoSaveStrokesAtScreenshot`；
   - `MinimumScreenshotStrokeCount = Automation.MinimumAutomationStrokeNumber`。
6. 失败回调只写 `FileLog`：`PPT/{failure.Operation}: {failure.Message} (0x{failure.ErrorCode:X8})`。

### 4.6 设置变更后的重应用 `OnSettingsChanged()`

`SettingsWindow.SettingsChanged` 触发（`ShellController.cs:543-558`）：

1. `bar.Reposition(BottomMargin, FloatingBarScale)`——**默认 `recenter:true`**，所以任何设置提交都会把浮动栏重新居中到底部（即使用户手动拖过）。
2. `WindowChrome.ApplyTheme(bar, Appearance.Theme)`。
3. 菜单缩放：`menuScale = _board != null ? BlackboardScale : FloatingBarScale`；`_palette?.ApplyScale(menuScale)`、`_shapePicker?.ApplyScale(menuScale)`。
4. `_board?.RefreshUiScale()`。
5. `bar.Bar.RefreshSettingsVisuals()`。
6. 若正在放映：`UpdatePptNavigation(_pptSession.Snapshot)`。
7. `ApplyEdgeGestureSetting()`。

### 4.7 墨迹保存/打开（`ShellController.cs:591-641`）

- **保存**：`FileSavePicker`，`SuggestedFileName = $"墨迹 {DateTime.Now:yyyy-MM-dd HH-mm-ss}"`，类型名 `"Ink Canvas 墨迹"`，扩展 `.icink`；`InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd((Window)_board ?? _bar))`；保存 `ActiveController.Document.Strokes`。
- **打开**：`FileOpenPicker` 过滤 `.icink`；若 `ActiveController.Document.Count > 0` 先问 `"打开将替换当前墨迹，是否继续？"`；然后 `Clear + AddRange + Selection.Clear + History.Clear + bar.RefreshSelection`。
- 异常写 `save strokes:` / `open strokes:` 日志。

### 4.8 截图与自动保存

- `ScreenshotAsync()`（`ShellController.cs:729-746`）：
  - 目录 `Path.Combine(Environment.SpecialFolder.MyPictures, "InkCanvas")`；
  - `ScreenshotService.CaptureToFileAsync(dir, primary.X/Y/W/H, IsSaveScreenshotsInDateFolders)`；
  - 输出名 `Screenshot-{HHmmss}.png`（可选 `yyyy-MM-dd` 子目录）；
  - 若 `IsAutoSaveStrokesAtScreenshot` → `AutoSave.SaveSnapshot(OverlayInk.Document.Strokes, "screenshot")`；
  - 全部异常吞掉。
- 自动保存服务（Features）：`root` 为空返回 null；目录可选日期；文件名 `{HHmmss}-{reason}.icink`；`MinimumAutomationStrokeNumber` 下限；`Prune()` 仅在 `AutoDelSavedFiles` 时递归删除超过 `max(1, days)` 天的 `.icink`。

### 4.9 退出 `Exit()`（`ShellController.cs:833-858`）

1. `_exiting` 幂等；停轮询。
2. 若 `Automation.IsAutoSaveStrokesAtClear` → `AutoSave.SaveSnapshot(OverlayInk.Document.Strokes, "exit")`（只存 overlay，不存画板页）。
3. `AutoSave.Prune()`。
4. `SaveSettings()`。
5. `Dispose()`：停 timer、`_countdown.Dispose()`、关 PPT 导航、`_pptSession.Dispose()`、`Services.Ppt.Dispose()`、`Updater.Dispose()`、`Overlay.Dispose()`、`Services.Dispose()`（overlay controller + 每页 controller）。
6. `Application.Current.Exit()`。
7. **没有二次确认**（`Advanced.IsSecondConfimeWhenShutdownApp` 在设置里是 Deferred）。

### 4.10 `BarDiagnostics`

`no bar` 或：

```
window={W}x{H}; client={w}x{h}; dpi={scale:0.##}; desired={w:0.#}x{h:0.#}; primaryChildren={n}; computedRow={w:0.#}
```

用于 `selftest.json` 的 `barDiagnostics` 字段。

---

## 5. 窗口基础设施

### 5.1 `WindowChrome`：无边框、DPI、落位

`MakeBorderless(Window, topmost=true, resizable=false)`（`WindowChrome.cs:28-71`）：

- `OverlappedPresenter.SetBorderAndTitleBar(false,false)`；`IsResizable/IsMaximizable/IsMinimizable = resizable`；`IsAlwaysOnTop = topmost`；
- `window.SystemBackdrop = null`（这些窗口自己画 token 化表面；只有浮动栏族显式装 `ChromeBackdrop`）；
- `SetRoundedCorners(window)`；
- 记录 `Closed` 日志 `window Closed: {Title}`；
- 首次 `Activated`（且非 Deactivated）：若 `Placements` 有记录且未应用，则延迟一个 dispatcher tick 重放：
  - `Kind=1` → `PlaceDip(x,y,w,h)`；
  - `Kind=2` → `CenterOnPrimary(w,h)`；
  - `Kind=0` → 不重放（调用方已按物理像素放过）。
  这里的设计原因是构造期 HWND 未建、`GetDpiForWindow` 返回 0，DIP 会被当作 scale=1 落位，125%/150% 下裁切。

放置 API：

| API | 行为 |
|---|---|
| `Place(window,x,y,w,h)` | 记录 `Kind=0` 物理矩形，`PlaceCore` |
| `PlaceClient(window,x,y,w,h)` | `AppWindow.ResizeClient(w,h)` 得到外框尺寸，再用 `ClientToScreen - GetWindowRect` 修正左上角，最后 `Place` |
| `PlaceDip(window,xDip,yDip,wDip,hDip)` | `scale=GetDpiScale`；`x/y=Math.Round(xDip*scale)`、`w/h=Math.Ceiling(wDip*scale)`；`PlaceClient`；再记录 `Kind=1` |
| `CenterOnPrimary(window,wDip,hDip)` | 记录 `Kind=2`；工作区居中 |
| `PlaceCore` | `MoveAndResize(x+1,y,w,h)` 后 `MoveAndResize(x,y,w,h)`（1 px 往返，修复 XAML island 与 HWND 错位） |
| `GetDpiScale` | `GetDpiForWindow(hwnd)/96.0`；0→1；异常→1 |
| `GetClientPosition` | `ClientToScreen` 客户区原点（物理 px） |
| `GetWorkArea(window)` / `GetWorkArea(anchor)` | `DisplayArea.GetFromWindowId / GetFromRect`，`DisplayAreaFallback.Nearest` |
| `GetScreenBounds(window, element)` | `TransformToVisual(root)` 得到 DIP 边界；`left=origin.X+floor(left*dpi)`，`top` 同理，宽=ceil(right*dpi)+origin.X-left，高同理 |
| `GetPopupPosition(anchor,w,h,gap)` | 水平居中并夹在工作区；`y=anchor.Y-h-gap`；若 `y<work.Y` 改为 `anchor.Y+anchor.Height+gap`；再夹在工作区 |
| `TrackAnchor(popup, ownerHwnd, reanchor)` | 订阅 owner 的 `AppWindow.Changed`，仅 `DidPositionChange || DidSizeChange`，Low 优先级合并，返回 detach Action |

### 5.2 `ClipToSurfaces`：把窗口裁成“卡片”形状

`WindowChrome.cs:156-257`：

- 输入 `Func<IEnumerable<Border>> getSurfaces`（例如浮动栏返回 `ChromeSurfaces` 四张卡片）；
- 监听 `root.LayoutUpdated`（Low 优先级合并），只处理 `XamlRoot != null`；
- 对每个可见且 `ActualWidth/Height>0` 的 Border：
  - `bounds=TransformToVisual(root)`；`scale=bounds.Width/ActualWidth`；
  - 物理边界保留**小数**：`x + bounds.Left*dpi` 等；四个角半径分别乘 `scale*dpi`（支持非对称圆角）；
  - 由 `SurfaceStrips` 逐扫描线生成 1 像素高的 GDI 矩形条：先按该行在圆角区域的位置计算左右 inset（`radius - sqrt(radius²-dy²)`），再把连续相同 `x/width` 的行合并成一条矩形；
  - 每个矩形 `CreateRectRgn(left, y, right, y+h)`，`CombineRgn(region, region, part, RGN_OR=2)`；
  - 之所以不用 `CreateRoundRectRgn`：GDI 对圆角半径的取整与 XAML 分数 DPI/Viewbox 缩放不一致，会在卡片边缘留下 1 px 亚克力毛边；
- 全部合并后 `SetWindowRgn(hwnd, region, true)`；成功后 region 所有权归系统，置 `IntPtr.Zero`，不再 Delete；
- 用拼接 key 缓存，布局不变不重设；
- 目的：XAML 背景透明仍会在圆角/卡片间隙留下亚克力或白底，必须同时裁剪**渲染与命中测试**。
- 所有窗口 `Closed` 时都会停止拖动 timer（`EnableDragMove` 也订阅了 `Closed`）。

### 5.3 Z 序：`EnsureAbove`

`WindowChrome.cs:554-575`：

- `hwnd==0` 或不可见直接返回；
- 传入 overlay 时：overlay 不可见也返回；然后沿 `GetWindow(hwnd, GW_HWNDPREV=3)` 向上走，**只有发现 overlay 在本窗之上才修复**；否则直接返回（避免每次轮询把 IME 候选窗等正常弹窗压下去）；
- 修复：`SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`。

### 5.4 拖动：`EnableDragMove`

`WindowChrome.cs:304-365`：

- 创建 **16 ms** DispatcherQueueTimer；按下时记录 `GetCursorPos` 与 `AppWindow.Position`；
- 未进入拖动态时，位移平方 `< (8*DpiScale)^2` 不动作（**8 DIP 阈值**）；
- 超过阈值设 `dragging=true`；`fromAnywhere` 模式会 `CapturePointer` 抢走按钮的 Click；每次 tick `AppWindow.Move(originPx + cursorDelta)`；
- `fromAnywhere=false`（标题栏/标签）时，若按下点在 `ButtonBase/TextBox/ComboBox/Slider/ToggleSwitch` 祖先内则忽略；
- `PointerReleased/Canceled/CaptureLost` 停止 timer；拖动结束时 `e.Handled=true`；
- 原因注释：WinUI 3 island 不跑原生 HTCAPTION move loop，且 `AppWindow.Move` 后 `PointerMoved` 停止投递，所以必须轮询原生光标。

### 5.5 标题栏：`AddTitleBar`

返回一个 2 行 Grid（`Auto` + `*`），`Background = Application.Current.Resources["IcSurfaceBrush"]`（不设会在透明子内容后露黑）：

- 标题文字：`FontSize=12`，`Margin=(12,0,8,0)`，VerticalCenter；
- header：`MinHeight=32`，背景 `IcSurfaceBrush`，列 `* + Auto`；
- 标题按钮 `CaptionButton(glyph,tooltip)`：`MinWidth=46`、`MinHeight=32`、`Padding=0`、透明背景、无边框、圆角 0；图标字体 `ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons`，字号 10；
- `minMax=true`：最小化 ``；最大化 ``，变化时图标切 ``，tooltip 在“最大化/还原”间切换；标题栏双击在非交互区域切换最大化；
- 关闭按钮（始终可配）：``，字号 12；hover 背景 `#FFE81123`（232,17,35），前景白；
- 对 header 调用 `EnableDragMove`。

### 5.6 触摸反馈抑制

`SuppressTouchFeedback(window)`：对 HWND 逐个 `SetWindowFeedbackSetting(hwnd, kind, 0, sizeof(int), ref 0)`，kind 集合 `{1,3,4,5,6,7,8,9,10,11}`；用于 overlay 与画板，去除触摸接触圈/长按右键反馈。

### 5.7 DWM 圆角

`SetRoundedCorners`（`WindowChrome.cs:593-603`）：`DwmSetWindowAttribute`：

- `DWMWA_WINDOW_CORNER_PREFERENCE(33) = DWMWCP_ROUND(2)`；
- `DWMWA_NCRENDERING_POLICY(2) = DWMNCRP_DISABLED(1)`（XAML 自绘整个无边框客户区）；
- `DWMWA_BORDER_COLOR(34) = 0xFFFFFFFE`（`DWMWA_COLOR_NONE`，避免白圈）。

### 5.8 `ChromeBackdrop`

`ChromeBackdrop.cs:13-62`：

- 继承 `SystemBackdrop`；`OnTargetConnected` 时保存 `GetDefaultSystemBackdropConfiguration`；
- 若 `DesktopAcrylicController.IsSupported()`：创建 `DesktopAcrylicKind.Thin`，`TintOpacity=0`、`LuminosityOpacity=0`、`TintColor=Transparent`、`FallbackColor=Transparent`；
- 配置：`Theme/IsHighContrast/HighContrastBackgroundColor` 复制默认值，`IsInputActive=true`；
- `State` 返回 controller 状态字符串（未连接为 `"Disconnected"`）；
- 由 `FloatingBarWindow.EnableTransparency` 安装到浮动栏、面板、调色盘、图形选择器、把手；`UpdateAcrylicTint` 是空操作（tint 由 XAML 画）。

### 5.9 `MinSizeHook`（`CountdownWindow.cs:752-895`）

- WinUI `OverlappedPresenter` 没有最小尺寸 API，用 `SetWindowLongPtrW(GWLP_WNDPROC)` 子类化 HWND 处理 `WM_GETMINMAXINFO(0x0024)`；
- 先 `CallWindowProcW` 拿默认值，再把 `ptMinTrackSize` 设为 `ceil(minDip*scale)`；
- 同时用 `MonitorFromWindow(MONITOR_DEFAULTTONEAREST=2)` + `GetMonitorInfoW` 修正最大化矩形到 `rcWork`；
- `TryGetMonitorBounds` 暴露 `rcMonitor`；
- `Dispose` 恢复旧 WndProc；跨原生边界全部 try/catch。

---

## 6. 浮动栏窗口族

### 6.1 `FloatingBarWindow`（`FloatingBarWindow.cs`）

**结构**：`Window.Content = Border(transparent, Padding=0) → Viewbox(Stretch=Uniform, 左上对齐) → FloatingBar`。标题 `"Ink Canvas 工具栏"`。

**尺寸公式**（`GetPhysicalSize`，`FloatingBarWindow.cs:163-174`）：

- `userScale = Math.Clamp(_scalePercent, 50, 200) / 100.0`；
- `(widthDip,heightDip) = _bar.ComputeLayoutSize()`；
- `_scaledBar.Width = widthDip * userScale; _scaledBar.Height = heightDip * userScale;`（DIP）
- 物理：`widthPx = ceil(widthDip*userScale*dpi)`、`heightPx = ceil(heightDip*userScale*dpi)`。

`FloatingBar.ComputeLayoutSize()`（Controls，供参考）：

```
primary  = MeasureRow(_primary)
mainW    = primary + 2*(MainPadH + PartBorder)
rowW     = PartH                                   // 笑脸拖动手柄始终存在
if mainHost visible:    rowW += PartGap + mainW
if gesture visible:     rowW += PartGap + PartH
if pptExit visible:     rowW += PartGap + PptExitW
return (rowW, PartH)
```

常量：`PartH=36*S=50`、`PartGap=2*S=2.777…`、`PartBorder=1*S=1.388…`、`MainPadH=4*S=5.555…`、`PptExitW=34*S=47.222…`，其中 `S=50/36`。

**落位**（`PlaceBottomCentre`）：

```
keep = keepPosition || _userPlaced || (_isFolded && _foldHandles.Count > 0)
x = keep ? currentX : work.X + (work.Width - width)/2
y = keep ? currentY : work.Y + work.Height - height - ceil(max(0, bottomMargin)*dpi)
PlaceClient(x,y,width,height)
```

`Reposition(bottomMargin, scalePercent, recenter=true)` 会把参数缓存为 `_bottomMargin/_scalePercent`；`recenter` 时清 `_userPlaced`；随后 `EnsureAbove` + `ReanchorPanels`。

**重排监视**（`OnBarLayoutUpdated`）：

- 未完成首次 DPI 落位前不动作；
- 比较 `_bar.CountVisibleItems()` 的 `(Buttons,Separators)` 与 `IsExpanded/IsMainCollapsed`；
- 变化时按**当前左上角不变**重设窗口尺寸（“笑脸收起只从右侧缩”），再 `EnsureAbove` + `ReanchorPanels`。

**DPI 跟踪**：`_root.Loaded` 后订阅 `XamlRoot.Changed`；只有 `GetDpiScale` 变化超过 0.001 才按当前客户区位置重设尺寸。

**收纳**（`SetFolded`，`FloatingBarWindow.cs:106-135`）：

- 已处于目标态直接返回；
- 收起：
  1. `ClosePanels()`、`CloseFoldHandles()`；
  2. `AppWindow.Hide()`；
  3. `work = ScreenService.WorkArea; dpi = GetDpiScale`；
  4. 把手尺寸 `width=ceil(30*dpi)`、`height=ceil(50*dpi)`，露出量 `protrude=ceil(14*dpi)`；`y = work.Y + (work.Height-height)/2`；
  5. 左把手 `x = work.X - (width-protrude)`；右把手 `x = work.X + work.Width - protrude`；
  6. `ShowHandle()`；`_bar.SetFoldedVisual(true)`。
- 展开：关把手、`SetFoldedVisual(false)`、`AppWindow.Show()`、`Reposition(_bottomMargin,_scalePercent)`、`Activate()`。

**两个弹窗**（`OnPanelRequested`）：More/Gesture 互斥；打开时关另一个；用 `AppWindow.Position/Size.Width` 传给 `BarPanelWindow.ShowAboveBar`；关闭时置 null。

**点击路由**（`AttachTapRouting`）：按下记录 `OriginalSource` 与位置；抬起时若 `|dx|<=10 && |dy|<=10`（DIP）则 `_bar.TryHandleTap(source)`，用于笑脸收起/PPT 退出牌等“按下与抬起都在同一元素”的点击。

**背景**：`EnableTransparency` 装 `ChromeBackdrop`；`UpdateAcrylicTint` 空实现。

**初始激活**：第一次 Activated 且非 Deactivated 时 `_initialDpiPlacementDone=true`，`Reposition` + `UpdateAcrylicTint`；构造期落位只是占位。

### 6.2 `BarPanelWindow`（更多设置 / 手势选项，`BarPanelWindow.cs`）

- 标题：`isMore ? "更多设置" : "手势选项"`；
- `S = FloatingBar.WpfScale = 50/36 ≈ 1.3889`；
- 内容来源：`bar.CreateMorePanelContent()` / `bar.CreateGesturePanelContent()`（行元素由浮动栏共享，窗口关闭时会从父 Panel 移除，便于下次重新挂载）；
- 标题带：
  - `FontSize = (isMore ? 12 : 10) * S`，Bold；
  - `Margin = (-S,-S,-S,0)`，`Padding = (15S,5S,15S,5S)`，`BorderThickness=S`，上方圆角 `5S`；
- 外框 `_host`：`BorderThickness=S`、`CornerRadius=5S`，子元素为标题+内容；
- `Viewbox` 包裹 `_host`，左上对齐；
- 设计尺寸：未 rooted 时 `Measure(∞,∞)`；若 `DesiredSize < 50` 用回退值 **More 256×236 / Gesture 180×165**；
- 落位（`PlaceAboveBar`）：
  - 锚点 = `WindowChrome.GetScreenBounds(_owner, _bar.GetPanelButton(_isMore))`；
  - `fit = min(1, min(work.Width/(designW*user*dpi), work.Height/(designH*user*dpi)))`；`user *= fit`（小屏高缩放下整体缩小）；
  - `_scaledHost.Width=designW*user`，物理 `ceil(designW*user*dpi)`；
  - `gap=ceil(4*S*dpi)`；`GetPopupPosition` 居中在上方/下方；
  - `PlaceClient` + `EnsureAbove`；
- 关闭条件：
  - 前景窗口每 **300 ms** 轮询：若 `fg != 自己 && fg != owner` → `Close()`；
  - `Escape`；
  - overlay 落笔由 shell 调 `ClosePanels`；
- `Closed` 时注销 `ThemeRefreshed`、退订 `XamlRoot.Changed`、停 dismiss timer、把共享行从父 Panel 移除、`_host.Child=null`、通知 `_owner.OnPanelClosed(isMore)`。

### 6.3 `FoldedBarHandleWindow`（`FoldedBarHandleWindow.cs`）

- 仅收纳时创建；标题无（不显示标题栏）；
- 根 `Border`：
  - 背景 `ARGB(185,235,238,240)`（约 72.5% 不透明浅灰）；
  - 边框 `ARGB(100,80,95,100)`；
  - `BorderThickness=1`；
  - 圆角：左侧 `(0,8,8,0)`，右侧 `(8,0,0,8)`；
- 图标 ``，字号 16；左把手右对齐、右边距 3，右把手左对齐、左边距 3（露出的 14 DIP 正好放图标）；
- 真正命中控件是透明 `Button`（MinWidth/Height=0、全拉伸、无边框），UIA 名称 `"展开工具栏"`；`Click` 调 `_owner.Bar.Shell?.ToggleFold()`（必须走 shell，才能更新 `_manualFoldOverride`，否则 Poll 会立刻再收起）；
- 落位由 `FloatingBarWindow` 用物理 `Place(x,y,width,height)` 完成；`ShowHandle()` 只 `Activate + EnsureAbove`。

---

## 7. 画板 `BoardWindow`（2311 行，App 层最大文件）

### 7.1 窗口与图层结构

- 标题 `"Ink Canvas 画板"`；`MakeBorderless(topmost:true, resizable:true)`；`SuppressTouchFeedback`；
- **不透明全屏**：构造末尾 `SetFullscreen(true)`；
- 图层（从下到上）：
  1. `_background`（Border，纯色板底）；
  2. `_paper`（Win2D `CanvasControl`，只画网格/横线，`ClearColor = ARGB(0,0,0,0)`）；
  3. `_images`（`BoardImageLayer : Canvas`）；
  4. `_media`（`BoardMediaLayer : Canvas`）；
  5. `_surface`（`InkSurface`，墨迹在最上，**墨迹永远压在图片/视频之上**）；
  6. `dock`（底部三浮条，`VerticalAlignment=Bottom`, `Margin=(3,0,3,0)`）；
  7. `_selectionPanel`（左上选区操作面板，默认 Collapsed）。
- `_surfaceHost = Grid { ManipulationMode=None }`；对 Pressed/Moved/Released/Canceled 用 `AddHandler(..., handledEventsToo:true)`，并监听 `PointerWheelChanged`；
- `_contentRoot = Grid { Background=Black }`，`SizeChanged` 时 `_selectionPanel.MaxWidth = max(240, ActualWidth-16)`；
- `_windowedContent = WindowChrome.AddTitleBar(this, Title, _contentRoot, closeButton:true, minMax:true)`，全屏时移出该结构。
- 构造后订阅 `_board.PageChanged / BackgroundChanged / ImagesChanged / ImagePlacementChanged / MediaPlacementChanged`（后两者直接把单个元素几何刷新到图片/视频层）、`Closed`、`Activated`；随后 `BindPage(); RefreshBackground(); UpdatePageNavigation(); ApplyBoardScale(); SetFullscreen(true);`

### 7.2 底部三浮条（`BuildStrips`，`BoardWindow.cs:213-294`）

每个 `ToolCell` = Button（宽×高 55 DIP，`Padding=0`，边框/圆角按位置），内容 Grid：`FontIcon` 在上、`TextBlock` 标签（12 pt）在下，内容 `Margin=5`。无徽标时图标 `26×26` 居中；有徽标时图标 `28×22` 左上、徽标右对齐/下对齐，徽标 `Margin=(0,0,-2,-3)`，宿主 `Grid 28×28`。按钮 tooltip=标签。

**左条**（`Opacity=0.75`，左对齐）从左到右：

| 元素 | 宽度 | 图标 | 标签 | 图标字号 | 边框/圆角 |
|---|---:|---|---|---:|---|
| `_prevL` | 60 | `` | 上一页 | 24 | `Thickness(1,1,0.25,1)`，`(5,0,0,5)` |
| 页码指示器 | 58×55 | — | 页码 | 15/12 | `Thickness(0,1,0.25,1)` |
| `_nextL` | 60 | ``（末页变 ``） | 下一页/加页 | 24 | `Thickness(0,1,1,1)`，`(0,5,5,0)` |
| 退出 | 55 | `` | 退出 | 24 | `Thickness(1)`，圆角 5，`Margin=(5,0,0,0)` |

**中条**（居中）从左到右：

| 元素 | 宽度 | 图标 | 标签 | 图标字号 | 徽标 | 边框/圆角 |
|---|---:|---|---|---:|---|---|
| `_gestureCell` | 60 | `` | 手势 | 28 | — | `(1,1,0.25,1)`，`(5,0,0,5)` |
| 画布 | 60 | `` | 画布 | 22 | — | `(0,1,0.25,1)` |
| `_selectCell` | 60 | `` | 选择 | 22 | `` 14 | 同上 |
| `_penCell` | 60 | `` | 墨迹 | 22 | — | 同上 |
| `_highlighterCell` | 60 | `` | 荧光笔 | 22 | — | 同上 |
| `_areaCell` | 60 | `` | 面积擦 | 22 | `` 16 | 同上 |
| `_strokeCell` | 60 | `` | 墨迹擦 | 22 | `` 12 | 同上 |
| `_shapeCell` | 60 | `` | 图形 | 22 | — | 同上 |
| 插入图片 | 60 | `` | 插入图片 | 22 | — | 同上 |
| `_undoCell` | 60 | `` | 撤销 | 22 | — | 同上 |
| `_redoCell` | 60 | `` | 重做 | 22 | — | 同上 |
| `_moreCell` | 60 | `` | 更多 | 24 | — | 同上 |
| 退出 | 60 | `` | 退出 | 24 | — | `(0,1,1,1)`，`(0,5,5,0)` |

**右条**（镜像，`Opacity=0.75`，右对齐）：

| 元素 | 宽度 | 图标 | 标签 | 图标字号 | 边框/圆角 |
|---|---:|---|---|---:|---|
| `_addR` | 55 | `` | 加页 | 24 | `Thickness(1)`，圆角 3，`Margin=(0,0,5,0)` |
| `_prevR` | 60 | `` | 上一页 | 24 | `(1,1,0.25,1)`，`(3,0,0,3)` |
| 页码指示器 | 58×55 | — | 页码 | 15/12 | `(0,1,0.25,1)` |
| `_nextR` | 60 | ``/`` | 下一页/加页 | 24 | `(0,1,1,1)`，`(0,5,5,0)` |

页码指示器：`TextBlock` 页码 15 pt（`Margin=(0,4,0,2)`），下方 `"页码"` 12 pt（`Margin=(0,3,0,0)`），整体 `Border 58×55`。

**按钮主题**（`ApplyButtonTheme`）：将 `_barBg/_barBorder/_barFg` 写入 `Background/BorderBrush/Foreground` 及 `ButtonBackground* / ButtonForeground* / ButtonBorderBrush*` 资源槽（每个槽用新的 brush 实例，避免 WinUI `0x800F0902`）。禁用态由 `SetCellEnabled` 设 `IsEnabled=false` 且内容 `Opacity=0.5`。

### 7.3 画板配色（`RefreshBackground`，`BoardWindow.cs:1578-1613`）

板底与三浮条**完全不跟随应用主题**：

| 项 | 黑板（Dark） | 白板（Light，含 Grid/Lined） |
|---|---|---|
| `_background.Background` | `ARGB(255,31,31,31)` | `ARGB(255,242,242,242)` |
| `_barBg` | `ARGB(191,0,0,0)` | `ARGB(191,255,255,255)` |
| `_barBorder` | `ARGB(153,255,255,255)` | `ARGB(204,0,0,0)` |
| `_barFg` | White | Black |
| `_barTitleBg` | `ARGB(128,33,86,201)` | `ARGB(64,88,139,252)` |
| `_hoverBg` | `Blend(_barBg, ARGB(54,0,205,205))` | `Blend(_barBg, ARGB(34,0,205,205))` |
| `_pressedBg` | `Blend(_barBg, ARGB(85,0,205,205))` | `Blend(_barBg, ARGB(68,0,205,205))` |
| `_selectedBg` | `Blend(_barBg, ARGB(90,0,102,191))` | 同（色值同） |
| 笔色自动修正 | 当前色 `R,G,B<=32` → `ARGB(255,245,245,245)` | 当前色 `R,G,B>=240` → `ARGB(255,20,20,20)` |

`Blend(base, tint)` 的 alpha 合成公式（`BoardWindow.cs:425-436`）：

```
ba = base.A/255; ta = tint.A/255; outA = ba + ta*(1-ba)
Mix(bc,tc) = clamp(round((bc*ba + tc*ta*(1-ba)) / outA), 0, 255)
out = ARGB(round(outA*255), Mix(R), Mix(G), Mix(B))
```

`SetBackground(background)`：`_board.SetBackground`；`UsingWhiteboard = background != Black`（Grid/Lined 也算白板）；`SaveSettings()`。**不调用 `ApplySettings`**。

### 7.4 页面与导航行为

- 页码文本：`$"{CurrentIndex+1} / {PageCount}"`（前后条一致）。
- 末页时 `_nextL/_nextR` 图标换 ``、标签换 `"加页"`、tooltip 同步；`canNext = !atLast || PageCount < BoardService.MaximumPages`。
- 启用规则：上一页 `CurrentIndex > 0`；下一页 `canNext`；`_addR` `PageCount < 99`。`MaximumPages = 99`。
- `NavigateNext()`：先 `AutoSaveCurrentPage("board-next")`，再 `Board.NextPage()`。`NextPage` 在末页会**自动追加一页**（未满 99），所以“下一页”在末页等价“加页”。
- `NavigatePrevious()`：直接 `PreviousPage()`（首页不动）。
- `InsertPage()`：`AutoSaveCurrentPage("board-add")` + `InsertPageAfterCurrent()`。
- `DeleteCurrentPage()` → 确认文案 `"删除当前页及其墨迹、图片？此操作无法从其他页恢复。"` → `AutoSaveCurrentPage("board-delete")` + `RemoveCurrentPage()`。
- `ConfirmClearCurrentPageAsync()`：文案 `"清除当前页上的全部墨迹和图片？"`；确认后 `AutoSaveCurrentPage("board-clear")`；**先 `SetTool(InkTool.Pen)`**（WPF 先回笔），再 `Current.Clear()`，再删除当前页所有 image/media。
- `AutoSaveCurrentPage(reason)`：仅在 `Automation.IsAutoSaveStrokesAtClear` 且 `Current.Document.Count > MinimumAutomationStrokeNumber` 时 `AutoSave.SaveSnapshot(..., reason)`；调用点 reason 为 `board-next / board-add / board-delete / board-clear / board-clone / board-exit`。
- `UpdateUndoRedo()`：`_undoCell`/`_redoCell` 按 `History.CanUndo/CanRedo` 启用。
- `OnClosed`：`AutoSaveCurrentPage("board-exit")`；退订 board 事件（含 `ImagePlacementChanged/MediaPlacementChanged`）；`_images.SelectionChanged`/`_media.SelectionChanged` 退订；`_media.Dispose()`；`_boundController.GetSelectableElements = null`；`_surface.Dispose()`；`_paper.Draw -= OnDrawPaper`；`_paper.RemoveFromVisualTree()`；退订当前 controller 的 Selection/History/Invalidated 事件。

### 7.5 工具与长按

`SetTool(tool)`（`BoardWindow.cs:465-490`）：若 `Current.Tool == tool` 只调 `OnToolChanged()`；否则赋 `Current.Tool = tool`，由 `InkController.ToolChanged` 触发 `OnToolChanged()`：

- `select = tool == Select`；`_surface.IsHitTestVisible = !select`；`_images.IsHitTestVisible = _media.IsHitTestVisible = select`；
- 离开 Select：`_images.ClearSelection()`、`_media.ClearSelection()`、`Board.Current.ClearSelection()`、`FinishSelectionPointers()`；
- `UpdateToolHighlight()`：活动工具按钮背景 `_selectedBg`，否则 `_barBg`；
- `UpdateSelectionActions()`。

活动判定：`Select`、`Pen`、`Highlighter`、`Eraser && Area`、`Eraser && WholeStroke`、`Shape`。

**长按（550 ms）**（`AttachClickAndHold`）：PointerPressed 起 timer；550 ms 时打开 flyout（Placement=Top）且 `opened=true`；Click 若 `opened` 则只清标志不执行单击，否则执行 `onClick`。用于“画布”（单击切黑白，长按纸型/缩放/间距）与“面积擦”（单击启用/再次清屏，长按橡皮大小）。

**面积擦**（`ActivateAreaEraser`）：

- 若已是 `Eraser && Area` → `ShowClearScreenFlyout`，返回；
- 否则 `EraserMode=Area`、`ApplyEraserSize()`、`SetTool(Eraser)`。

**墨迹擦**（`ActivateStrokeEraser`）：

- 若已是 `Eraser && WholeStroke` → 清屏 flyout；
- 否则 `EraserMode=WholeStroke`、`EraserRadius=2.5f`、`SetTool(Eraser)`。

**橡皮尺寸**（`ApplyEraserSize`）：`index = clamp(Canvas.EraserSize,0,4)`；`radius = 45f * {0.5,0.8,1,1.25,1.8}[index]`；对每一页：`page.EraserRadius = page.EraserMode == WholeStroke ? 2.5f : radius`。注意这与 `AppServices.ApplySettings` 的公式相同，但后者会覆盖所有页（包括 WholeStroke 页）；`BindPage` 末尾再调用 `ApplyEraserSize` 恢复 WPF 规则。

**清屏 flyout**：内容为 `FontIcon ` 22 + `"点击清屏"` 14；透明 Button；Flyout 内 Border 使用 `_barBg/_barBorder`、边框 1、圆角 5；点击先 `Hide()` 再确认清屏。

### 7.6 画布背景、纸型、网格、视图缩放

- `BuildCanvasFlyout()`（画布长按）：
  - 纸型：`黑板`→Black、`白板`→White、`网格纸`→Grid、`横线纸`→Lined；
  - 视图：`缩小 · 80%`(0.8)、`实际大小 · 100%`(1)、`放大 · 125%`(1.25)、`放大 · 150%`(1.5)；
  - `网格间距` 子菜单：24/32/40/56/72 DIP，点击 `_board.GridSpacing=value; _paper.Invalidate();`。
- `BuildEraserSizeFlyout()`：`OriginalMenuCatalog.EraserSizes` 五项（`小/较小/中（默认）/较大/大`），RadioMenuFlyoutItem 组名 `"BoardEraserSize"`；点击写 `Canvas.EraserSize=index`，`SaveSettings`，`ApplyEraserSize`，`EraserMode=Area`，`SetTool(Eraser)`。
- `OnDrawPaper`：仅 `Grid/Lined` 画线；`spacing = clamp(GridSpacing, 12, 160)`；线色 `ARGB(55,92,113,121)`，线宽 1；从 `y=spacing` 到 height 画横线；Grid 再从 `x=spacing` 到 width 画竖线。
- `SetViewScale(scale)`：`scale=clamp(0.5,2)`；若 surface 未布局则只记 `_viewScale`；否则 `current=ExtractScale(ViewTransform)`（`sqrt(M11²+M12²)` 再 clamp 0.5..2），`factor=scale/current`，以 surface 中心为轴 `T(-c)*Scale(factor)*T(c)` 左乘 `ViewTransform *= aroundCenter`；`Document.Touch()`；同步 images/media/paper。
- `ApplyBoardScale()`：`scale=clamp(Appearance.BlackboardScale,50,150)/100`；对左/中/右三浮条分别 `RenderTransform=ScaleTransform(scale)`，`RenderTransformOrigin` 为 `(0,1)/(0.5,1)/(1,1)`；`RefreshUiScale()` 公开给 shell 设置变更时调用。
- `OnControllerInvalidated`：若 `ViewTransform` 变了，同步 `_images/_media/_paper` 并更新 `_viewScale`；若 `IsMarqueeSelecting || IsRotatingSelection` 则隐藏选区面板，否则若面板可见则重新 `PlaceSelectionPanel()`。

### 7.7 页面绑定 `BindPage()`（`BoardWindow.cs:1668-1706`）

1. `FinishSelectionPointers()`；
2. 旧 controller：`GetSelectableElements=null`，退订 `SelectionChanged/History.Changed/Invalidated/ToolChanged`；
3. `_boundController = _board.Current`；清 `_selectionElements` 与 `SelectedElements`；
4. 订阅 `SelectionChanged/History.Changed/Invalidated/ToolChanged`；设置 `GetSelectableElements = GetSelectionElements`；
5. `_surface.Controller = _boundController`；
6. `_images.SetPage(_board)` + `UpdateViewTransform(ViewTransform)`；`_media` 同理；
7. `_paper.RenderTransform = MatrixTransform(ViewTransform)`；`_lastLayerTransform=ViewTransform`；`_viewScale=ExtractScale`；
8. `ApplyEraserSize()`；
9. `SetTool(_boundController.Tool)`；
10. `UpdatePageNavigation()`、`UpdateSelectionActions()`、`UpdateUndoRedo()`、`_paper.Invalidate()`。

`OnPageChanged()`：`Services.ApplySettings()`（注意会覆盖页橡皮半径）→ `BindPage()` → `RefreshBackground()` → `CurrentPageChanged?.Invoke()`（shell 据此重绑 bar controller 并关闭调色盘）。

### 7.8 全屏/窗口模式

`SetFullscreen(bool)`（`BoardWindow.cs:1895-1919`）：

- 进入：从 `_windowedContent` 移除 `_contentRoot`；`AppWindow.SetPresenter(FullScreen)`；`Content=_contentRoot`；
- 退出：`SetPresenter(Overlapped)`；`MakeBorderless(topmost:true,resizable:true)`；`SuppressTouchFeedback`；把 `_contentRoot` 放回 `_windowedContent` 第 1 行；`Content=_windowedContent`；`Place(work area)`；
- 最后 `ApplyTheme(_boardTheme)`、`UpdatePageNavigation()`。
- 默认构造末尾进入全屏；全屏时没有标题栏/关闭按钮，退出画板依赖三浮条“退出”或 Alt+B。

### 7.9 更多功能 flyout（`BuildMoreFlyout`）

标题带：背景 `_barTitleBg`，下边框 1，圆角 `(5,5,0,0)`，`Padding=(15,5,15,5)`，文字 `"更多功能"` 16 Bold。

3×3 网格（`ColumnSpacing/RowSpacing=6`，每格 Button `56×56`、圆角 5、透明底、hover `_hoverBg`、pressed `_pressedBg`，图标 `40×40` 宿主内 24 pt）：

| glyph | 标签 | 动作 |
|---|---|---|
| `` | 倒计时 | `OpenTimer` |
| `` | 随机抽选 | `OpenLottery` |
| `` | 单次抽选（右上角 12×12 accent 徽标，文字 `"1"` 8 pt） | `OpenLotteryQuick` |
| `` | 保存墨迹 | `SavePageAsync` |
| `` | 打开墨迹 | `OpenPageAsync` |
| `` | 播放墨迹 | `OpenReplay` |
| `` | 截屏 | `_shell.ScreenshotAsync()` |
| `` | 指南 | `OpenGuide` |
| `` | 设置 | `OpenSettings` |

启动器行（水平居中，`Margin=(0,6,0,6)`）：
- `视频展台`：glyph ``，20 pt，文字 12.5，行高 35，`Margin=(10,0,10,0)`；点击先 `ToggleBoard()` 退出画板，等 **120 ms**，`SoftwareLauncher.LaunchEasiCamera("希沃视频展台")`；失败弹 `"未找到希沃视频展台安装路径，或启动器无法运行。"`；
- 分隔线：`1×25`，`Margin=(4,0,4,0)`，`_barFg`，`Opacity=0.6`；
- `Desmos`：glyph ``；点击先 `ToggleBoard()`，再 `LaunchDesmos()`。

移植附加两行：
- row1：`插入视频`(``)、`文件`(``，打开文件子菜单)、`清屏`(``)；
- row2：`删除本页`(``)、`全屏/窗口`(``，文字随 `_isFullscreen`)、`画布纸型`(``)；
- ExtraButton：高 30、`Padding=(8,0,8,0)`、图标 13、文字 12、圆角 4。

### 7.10 手势 flyout（`BuildGestureFlyout`）

标题带 `"手势功能"` 11 pt，`Padding=(10,4,10,4)`，圆角 `(5,5,0,0)`；面板 `Spacing=6`，`Margin=(12,8,12,10)`。

行（label 行：图标 16 pt/20×20 + 文字 12；右侧 `ToggleSwitch Width=44, MinWidth=0`，On/OffContent 为空）：
1. `` 多指书写 → `Gesture.IsEnableMultiTouchMode`
2. `` 双指移动 → `IsEnableTwoFingerTranslate`
3. `` 双指缩放 → `IsEnableTwoFingerZoom`
4. `` 双指旋转 → `IsEnableTwoFingerRotation`
5. 分隔线（高 1，`_barBorder`）
6. `` 双指手势作用于选区 → `IsEnableTwoFingerRotationOnSelection`

每个 Toggle 变化即 `set(v); SaveSettings(); ApplySettings();`。
下方 `变换中心点` ComboBox（`FontSize=12, Width=132`，三项 `画布中心 / 选中内容中心 / 双指中心`），索引写 `Gesture.MatrixTransformCenterPoint` + 保存/应用。

### 7.11 文件 flyout 与画板文件格式

`BuildFileFlyout`：`保存当前页…`、`保存整个画板…`、分隔线、`打开到当前页…`、`打开整个画板…`。

`BoardService` 的持久化（Features，App 直接调用）：

- 扩展 `.icink`；`SavePage(path)`：若当前页 **没有图片** → 调 `InkFileStore.Save` 写“纯笔画” `.icink`；否则写单页 board 包。**注意：只含视频而没有图片的页会走纯笔画分支，视频数据不会写入。**
- `SaveBoard(path)`：写全部页的 board 包。
- Board 包 JSON（默认 PascalCase，`WriteIndented`）：

```json
{
  "Format": "inkcanvas-board",
  "Version": 1,
  "Background": "Black|White|Grid|Lined",
  "GridSpacing": 40.0,
  "Pages": [
    {
      "Strokes": [ { "Color": 4278190080, "Width": 2.5, "Highlighter": false, "Taper": 0, "Shape": false, "Points": [[x,y,pressure,width,timeMs]] } ],
      "Images": [ { "Id": "guid", "Name": "...", "Data": "<base64>", "ContentType": "image/png", "Left": 0, "Top": 0, "Width": 0, "Height": 0, "Transform": [6 floats] } ],
      "Media":  [ { "Id": "guid", "Name": "...", "Data": "<base64>", "ContentType": "video/mp4", "Left": 0, "Top": 0, "Width": 360, "Height": 240, "Transform": [6 floats] } ]
    }
  ]
}
```

- **Board 包的 `StrokeFile` 没有 `HighlighterAlpha` 字段**（与 `InkFileStore` 的 `.icink` 不同）；读回时属性默认 96。移植时必须保持这个差异或统一升级格式。
- 写入是原子的：先写 `path + ".tmp"`，再 `File.Move(tmp, path, overwrite:true)`。
- `LoadPage`：若文件是 board 包取 `Pages.FirstOrDefault()` 替换当前页；否则按纯 `.icink` 读笔画。`ReplacePage` 会清空 history/selection，并把 `ViewTransform` 重置为 Identity。
- `LoadBoard`：board 包页数 `>99` 抛 `InvalidDataException`；`Background` 用 `Enum.TryParse(ignoreCase:true)`；`GridSpacing` load 后 clamp 12..160；`CurrentIndex=0`；页/图片/媒体全部替换。
- `RemoveCurrentPage`：只有一页时**不删页**，改为清空该页墨迹/选区/历史/图片/媒体并触发 `ImagesChanged + PageChanged`。

### 7.12 选区面板（`BuildSelectionPanelContent` / `PlaceSelectionPanel`）

**外观**：`_selectionPanel = Border { HorizontalAlignment=Left, VerticalAlignment=Top, Margin=(0,50,0,0), Height=104, Padding=4, BorderThickness=1, CornerRadius=5, Visibility=Collapsed }`；背景/边框在 `RefreshBackground` 中设为 `_barBg/_barBorder`；`BuildSelectionPanelContent()` 每次重建（主题切换时）。

Panel 内容 = `Viewbox(Stretch=Uniform)` 包一个 `StackPanel`：
- `row1`：`StackPanel Horizontal, Spacing=6, Height=80`；
- `hints`：`StackPanel Horizontal, Height=20, Margin=(0,0,0,4), Opacity=0.75, Spacing=6`。

**row1 四列**：

1. **克隆列**（`StackPanel Spacing=2, Margin=(4,0,0,0)`）：
   - `克隆模式`：图标 `` 16 pt/18×18 + 文字 14；Button 90×35，圆角 5；点击 `CurrentController.CloneMode = !CloneMode`，`SyncCloneButton` 在开启时把图标/文字前景设为 `_barBg`（“反白”），关闭恢复 `_barFg`；
   - `衍至新页`：图标 `` + 文字 14，Button 90×35。
2. **旋转列**（`StackPanel Spacing=4`）：
   - 两行 `逆旋` / `顺旋`，每行：图标 `` 14 pt/18×18；`逆旋` 变换 `Rotate(90) + Scale(1,-1)`，`顺旋` 为 `Rotate(-90)`；文字 14；三个角度按钮 `15°/45°/90°`（30×30，字号 14），角度 = `sign * degrees`，调用 `RotateSelection(angle)`。
3. **翻转列**（`StackPanel Spacing=2`）：两个 Button 90×35：
   - 水平：文字 `"水平翻转"`；竖直：`"竖直翻转"`；
   - 复合图标：`` 16（底）、`` 16（覆盖）、`` 12（箭头），`StackPanel Width/Height=18`；
   - 水平：覆盖层 `Margin=(0,-18,-1,0)`、箭头 `Margin=(0,-17,0,0)`、箭头变换 `Rotate(90)+Scale(-1,-1)`；
   - 竖直：覆盖层 `Margin=(0,-19,0,0)` 且 origin 中心、变换 `Rotate(90)+Scale(-1,-1)`；箭头 `Margin=(0,-18,0,0)`。
4. **动作列**（`StackPanel Spacing=4, Margin=(0,0,4,0)`）：
   - 第一行 `StackPanel Horizontal, Spacing=4`：
     - `粗细` Border `155×33`, `Padding=(5,0,5,0)`, 背景 `_barBg`, 圆角 3；内部 `StackPanel Horizontal, Spacing=1.5`：图标 `` 14/18×18、文字 `"粗细"` 14、三个 30×30 按钮：``（×1.25）、``（×0.8）、``（恢复）；
     - `存为图片`：图标 `` + 文字 14，90×35；
     - `删除`：图标 `` + 文字 14，90×35。
   - 第二行 `颜色` Border（高 33，`Padding=(5,0,5,0)`，`_barBg`，圆角 3）：
     - 图标 `` 14/18×18、文字 `"颜色"` 14；
     - 9 个色块按钮（30×30，圆角 5，内部 Border 16×16、圆角 8、边框 1.5 `_barBorder`）：
       `黑色(0,0,0)`、`白色(255,255,255)`、`红色(220,38,38)`、`橙色(234,88,12)`、`黄色(234,179,8)`、`绿色(22,163,74)`、`青色(13,148,136)`、`蓝色(37,99,235)`、`粉色(199,46,199)`。

**hints**：两个 `Border Width=200`、边框 0.75、圆角 2、文字 12 居中：
- `"鼠标：拖拽移动，滚轮缩放"`；
- `"触屏：单指移动，两指缩放/旋转"`，`Margin=(2,0,-2,0)`。

**放置**（`PlaceSelectionPanel`，`BoardWindow.cs:1279-1301`）：

```
panelW = ActualWidth > 0 ? ActualWidth : 695
panelH = ActualHeight > 0 ? ActualHeight : 104
left = (view.Left + view.Right - panelW) / 2
top  = view.Bottom + 15
left = clamp(left, 0, max(0, rootW - panelW))
top  = clamp(top, 0, max(0, rootH - panelH - 60))
Margin = (left, top, 0, 0)
```

其中 `view = ViewBoundsOfWorld(CombinedSelectionBounds())`：把世界坐标矩形四角经 `ViewTransform` 变换后取轴对齐包围盒（`ViewBoundsOfWorld`）。

**组合包围盒**（`CombinedSelectionBounds`）：**只返回控制器的 `SelectionBounds`**；控制器的 `SelectionBounds` 本身已对 `Selection` 笔画和 `SelectedElements`（图片/视频）取并集（`InkController.cs:191-198`）。

**元素进入选区的路径**（3.4.0 工作树重构）：
- `BindPage()` 先清旧控制器的 `GetSelectableElements = null`，再清空新控制器的 `SelectedElements` 与 `_selectionElements`，并设置 `GetSelectableElements = GetSelectionElements`；
- `GetSelectionElements()` 按当前页 `CurrentImages`/`CurrentMedia` 逐个创建/复用 `BoardSelectionElement`（`Features/Board/BoardSelectionElement.cs`），它是 `ISelectionElement` 适配器：`Bounds` 用 `BoardElementMath.TransformedBounds`，`Transform(delta)` 追加 `asset.Transform`，`Capture/Restore` 保存 `Left/Top/Width/Height/Transform`，`SetPresent` 调 `BoardService.SetImagePresent/SetMediaPresent`；构造为 `new BoardSelectionElement(_board, image/media)`，元素几何变化通过 `BoardService.NotifyImagePlacement/NotifyMediaPlacement` 通知图层；
- 控制器的选择逻辑（点选/框选/拖动/双指/删除）因此**同时覆盖笔画与元素**；
- `OnStrokeSelectionChanged` 再把控制器的 `SelectedElements` 同步给图层：`_images.SetSelection(...Image...)`、`_media.SetSelection(...Media...)`，然后 `UpdateSelectionActions()`。

**可见性**（`UpdateSelectionActions`）：`any = Current.HasSelection`；仅当 `any && !IsMarqueeSelecting && !IsRotatingSelection` 时显示选区面板；可见时 `SyncCloneButton()` + `PlaceSelectionPanel()`。

### 7.13 选区变换与撤销

- `RotateSelection(deg)`：`Matrix3x2.CreateRotation(deg*π/180)`，`scaleWidth:false`，名称 `"rotate selection"`。
- `FlipSelection(horizontal)`：`CreateScale(-1,1)` 或 `(1,-1)`，名称 `"flip horizontal"/"flip vertical"`。
- `OnSurfaceHostPointerWheelChanged`（Select 模式且有选区）：滚轮 delta>0 → 1.1，否则 0.9；`CreateScale(scale)` 绕选区中心，`scaleWidth:true`，名称 `"zoom selection"`。
- `TransformSelectionMatrix(local,name,scaleWidth)`（`BoardWindow.cs:1211-1226`）：
  1. `controller = _board.Current`；`if (!controller.HasSelection) return;`
  2. `center = CombinedSelectionBounds().Center`；`matrix = T(-center) * local * T(center)`；
  3. **全部委托给 `controller.TransformSelectionUndoable(matrix, name, scaleWidth)`**；
  4. `PlaceSelectionPanel()`。
  控制器内部（`InkController.cs:1022-1046`）：
  - `strokes = Selection.ToList()`、`elements = SelectedElements.ToList()`；记录 `beforeFrame = SelectionFrame` 与 before 快照；
  - 对每个 stroke `Transform(matrix, scaleWidth)`；对每个 element `Transform(matrix)`（内部追加 `asset.Transform` 并 `Refresh()`）；
  - 记录 after；`afterFrame = beforeFrame with { Transform = beforeFrame.Transform * matrix }`；`RememberFrame(afterFrame)`；
  - `History.Push(DelegateAction(name, apply: 恢复 after 笔画+元素+frame, revert: 恢复 before ...))`；
  - `Document.Touch(); SelectionChanged?.Invoke(); Invalidated?.Invoke();`
- `SelectionChange(name,mutate)`：仍只针对**笔画**（粗细/颜色），不处理元素；`before=clone`，`mutate`，`after=clone`，`Document.Touch()`，push `DelegateAction(apply=after, revert=before)`。
- **控制器的选择手势 API**（BoardWindow 的 Select 路由最终都落到这里）：
  - `BeginSelectionGesture()`：仅在 `HasSelection` 且未处于手势时；快照 `Selection` 笔画、`SelectedElements` 元素与 `SelectionFrame`；`_cloneDrag=false; _dragMoved=false`；`_selectionGesture=true`；
  - `ApplySelectionGesture(delta)`：若 `CloneMode && !_cloneDrag`，先把笔画 `Clone(preserveId:false)` 加入文档、元素 `Duplicate()` 并 `SetPresent(true)`，然后对克隆目标应用 delta；否则对原目标应用；虚线 frame 只在不克隆时 `Transform`；`Document.Touch()+Invalidated`；
  - `EndSelectionGesture()`：结束手势、`Reset` manipulator、调用 `EndSelectionDrag()` 记录撤销（笔画+元素+frame 一个单元）；
  - `TransformSelectionUndoable(matrix,name,scaleWidth)`：见上，非手势路径（旋转/翻转/滚轮/选区面板按钮）的通用入口。
- `SelectionFrame`/`ISelectionElement.Frame`/`InkStroke.ObjectFrame`：选择框是“对象框 + 累积矩阵”的组合；控制器在 `SelectionFrame` getter 里按“单笔画取 ObjectFrame、单元素取 Frame、多对象取 SelectionBounds+Identity”决定初始 frame，并用 `RememberFrame` 缓存。
- `RestoreStrokes(targets,snapshot)`：逐点 `RestorePoints`，并恢复 `BaseWidth/Color/IsHighlighter/HighlighterAlpha/TaperKind`。
- `ScaleSelectionWidth(factor)`：`BaseWidth = clamp(old*factor, 0.5, 60)`。
- `RestoreSelectionWidth()`：恢复到 `CurrentAttributes.BaseWidth`。
- `RecolorSelection(color)`：设置 `Attributes.Color`。
- `DeleteSelection()`（`BoardWindow.cs:1497`）：**只有 `_board.Current.DeleteSelection()`**。控制器删除笔画 + 把 `SelectedElements` 逐个 `SetPresent(false)`（元素从页集合移除），并把两者放进**同一个** `DelegateAction("delete")` 撤销单元；`ClearSelection()` 后触发 SelectionChanged。

### 7.14 `衍至新页` 与 `存为图片`

**`CloneSelectionToNewPage()`**（板内“衍至新页”）：

1. 克隆选中 strokes/image/media；三者皆空返回；
2. 若 `PageCount >= 99` → 提示 `"已达到 99 页上限，无法衍至新页。"` 并返回；
3. `AutoSaveCurrentPage("board-clone")`；
4. `InsertPageAfterCurrent()`；取新 `Current`；
5. strokes 加到新页，并 push `DelegateAction("clone to page", apply=AddRange(strokes), revert=RemoveAll(strokes))`；
6. `AddImage(image)` / `AddMedia(media)`；
7. 清空原 controller 选区，`Document.Touch()`，`UpdateSelectionActions()`。

**`SaveSelectionAsImageAsync()`**（存为图片）：

1. 选区/元素皆空返回；`bounds=CombinedSelectionBounds()`；空返回；
2. `FileSavePicker`：类型 `"PNG 图片"`、扩展 `.png`、建议名 `DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff")`；
3. 目标尺寸 `width=clamp(ceil(bounds.Width+10),1,8192)`、height 同；
4. `CanvasRenderTarget(device,width,height,96)`；`Clear(Transparent)`；
5. 笔画：`offset = T(-bounds.Left,-bounds.Top)`；新 `InkController`，`Document.AddRange(strokes.Clone())`，`ViewTransform=offset`，`InkSceneRenderer{ShowSelectionOutline=false}.DrawCommitted`；
6. 图片：`CanvasBitmap` decode 后 `session.Transform = ElementExportTransform(left,top,transform,offset)`，`DrawImage(bitmap, Rect(0,0,width,height))`；
7. 视频：`RenderTargetBitmap.RenderAsync(_media.GetViewElement(media))` 抓当前帧，`CanvasBitmap.CreateFromBytes(..., B8G8R8A8UIntNormalized)`，同样 transform；失败只跳过视频；
8. `target.SaveAsync(file, CanvasBitmapFileFormat.Png)`；
9. 异常 → `"存为图片失败：{ex.Message}"`。

`ElementExportTransform(left,top,transform,offset) = T(left,top) * Read(transform) * offset`。

### 7.15 元素层：图片（`BoardImageLayer`，189 行）

- 每个 asset 一个 `Grid`（`Tag=asset`），子元素：`Image(Stretch=Uniform)`、选中边框 `Border(BorderThickness=2, 透明)`、右下缩放手柄 `Border(12×12, accent, 默认 Collapsed)`；
- **输入完全由控制器统一处理**：`AddViewAsync` 只创建/挂载 view，不订阅任何指针事件；拖动/缩放/旋转/删除都在 `InkController` 的 `SelectedElements`（`BoardSelectionElement`）路径里完成。
- 选中同步：`SetSelection(IEnumerable<BoardImageAsset>)` 由 `BoardWindow.OnStrokeSelectionChanged` 调用，取 `FirstOrDefault()` 写入 `_selected`；**当前实现只把 `_selected` 赋值并把所有 view 的选中视觉清空，没有再点亮被选中的 view**（工作树现存缺陷，见 §18.1）。
- `ApplyGeometry`：`Width/Height = asset.Width/Height`；`Canvas.SetLeft/Top`；`RenderTransform = MatrixTransform(BoardElementMath.RenderMatrix(left,top,Transform))`；origin `(0,0)`；
- `Refresh()` 按 `CurrentImages` 对账：移除不存在的 view、对已有 view 重设几何、新 asset 异步解码；`version` 防过期异步回调；在 `_refreshVersion` 变化或 asset 已不在页中时丢弃；
- 解码：bytes → `InMemoryRandomAccessStream` → `BitmapImage.SetSourceAsync`；失败静默（坏图不影响其他元素）；
- `SelectedImage` 仍被 `BoardWindow.SaveSelectionAsImageAsync` 读取（作为控制器选区的缓存视图）；
- `ClearSelection()` 只清 `_selected` 与视觉并触发 `SelectionChanged`；没有 `DeleteSelected/CancelDrag`（3.4.0 已删除）。

### 7.16 元素层：视频（`BoardMediaLayer`，224 行）

- 每个 asset：`MediaPlayerElement`（`AreTransportControlsEnabled=true`, `AutoPlay=false`, `Stretch=Uniform`, `MinWidth=96`, `MinHeight=72`）+ 透明 `outline Border`（2 px，`IsHitTestVisible=false`）+ 右下 `14×14` 手柄（`IsHitTestVisible=false`）；
- 播放器：`new MediaPlayer { AutoPlay=false, IsLoopingEnabled=false }`，`Source = MediaSource.CreateFromStream(InMemoryRandomAccessStream, asset.ContentType ?? "video/mp4")`；每 asset 保留 `(Player, Stream)`，页面切换/移除时暂停、置空、Dispose；
- **输入完全由控制器统一处理**：`AddViewAsync` 不订阅指针事件；旧的 `OnPressed/OnMoved/OnReleased`、`IsWithinTransportControls`、22 DIP resize、`"transform media"` 已在 3.4.0 删除。运输控件的播放/暂停仍由 `MediaPlayerElement` 自己接收（但当前无自定义拖拽路径）。
- `SetSelection(IEnumerable<BoardMediaAsset>)` 与图片层同样只赋值 `_selected` 并清空视觉；`ClearSelection()` 同上；
- `GetViewElement(asset)` 暴露给“存为图片”的当前帧抓取；
- 行为/编解码未验证（源码注释明确写了 per user instruction）。

### 7.17 Select 模式输入路由（`BoardWindow.cs:1789-1850`）

只在 `Board.Current.Tool == Select` 时处理：

- `PointerPressed`（handledEventsToo）：若 `e.Handled` 直接返回；`p=ToOverlayPointer(e)`；`CapturePointer`；`_selectionPointers[p.Id]=p`；`Board.Current.OnPointerDown(p)`；`Handled=true`。
- `PointerMoved`：仅当该 `PointerId` 在 `_selectionPointers` 中；更新 map；`OnPointerMove`；`Handled=true`。
- `PointerReleased`：若 map 中存在 → `OnPointerUp`、`ReleasePointerCapture`、`Handled=true`。
- `PointerCanceled`：从 map 移除并取最后样本 → `OnPointerUp`。
- `FinishSelectionPointers()`：把所有仍按下的样本逐个 `OnPointerUp`，清空 map，`ReleasePointerCaptures()`；`SetTool` 离开 Select、`BindPage`、`OnClosed` 都调用，避免跨页/跨工具留下按下的触点。
- 旧的双指元素手势、`_selectTouches`、`_elementManipulator`、`RestoreImageElement/RestoreMediaElement` 已在最新工作树中删除；元素双指/拖动统一由 `InkController` 的 `SelectedElements` + `GetSelectableElements` + `GesturesTargetSelection` 处理。
- `OnSurfaceHostPointerWheelChanged`：Select 且有选区（笔画或图层缓存选中元素）时，滚轮 1.1/0.9，绕选区中心，`TransformSelectionMatrix(..., scaleWidth:true)`。
- `OnToolChanged()`：`SetTool` 只在工具变化时赋值，所有副作用集中在 ToolChanged 处理器：`_surface.IsHitTestVisible = !select`、元素层可命中=select；离开 Select 时清元素/控制器选择并 `FinishSelectionPointers()`；更新工具高亮与选区面板。
- `ToOverlayPointer`：`Pressure <= 0 ? 0.5f : Pressure`；`ContactRect.Width/Height`；`TimeMs=(uint)point.Timestamp/1000`；设备类型 Pen/Touch/其它→Mouse；`Shift/Control` 取 `e.KeyModifiers` 的对应位（`OverlayPointer` 现有 10 个字段：Id/X/Y/Pressure/ContactW/ContactH/Kind/TimeMs/Shift/Control，后两个默认 false）。

### 7.18 插入图片 / 视频

**插入图片**（`InsertImageAsync`）：

- `FileOpenPicker` 过滤 `.png/.jpg/.jpeg/.bmp/.gif/.tif/.tiff`；
- `GetBasicPropertiesAsync`，`Size > 32*1024*1024` → `"图片文件超过 32 MiB，未插入。"`；
- 读 bytes → `BitmapDecoder` 得 `PixelWidth/PixelHeight`；
- 若 `Canvas.IsCompressPicturesUploaded && (w>1920 || h>1080)`：`k=min(1920/w,1080/h)`，`w*=k; h*=k`；
- `CenterAndScaleElement(baseW,baseH,out w,out h,out left,out top)`；
- `BoardImageAsset { Name=file.Name, Data=bytes, ContentType=file.ContentType, Width,Height,Left,Top }`；`_board.AddImage`；
- 异常 `"图片插入失败：{ex.Message}"`。

**`CenterAndScaleElement`**（`BoardWindow.cs:2158-2168`）：

```
dpi = GetDpiScale
screenW = Primary.Width/dpi; screenH = Primary.Height/dpi
scale = min(screenW/2 / max(1,baseW), screenH/2 / max(1,baseH))
width = max(1, baseW*scale); height = max(1, baseH*scale)
left = (surface.ActualWidth - width)/2
top  = (surface.ActualHeight - height)/2
```

注意没有 `min(1, scale)` 上限——小图也会放大（与 WPF `ScaleTransform` 一致）。

**插入视频**（`InsertMediaAsync`）：

- `FileOpenPicker` 起始 `VideosLibrary`，过滤 `.mp4/.avi/.wmv/.mov/.m4v`；
- `Size > 512*1024*1024` → `"视频大于 512 MiB；为避免将大文件整体复制进画板文档，未插入。可先使用常规播放器打开该视频。 "`（末尾有一个空格）；
- `CenterAndScaleElement(256,256,...)`（WPF 以 256×256 为基准）；
- `BoardMediaAsset { ContentType = 空 ? "video/mp4" : file.ContentType, ... }`；`AddMedia` 失败 → `"视频格式或内容无效，未插入。"`；
- 异常 `"视频插入失败：{ex.Message}"`。

**保存/打开页面与画板**：

- `SavePageAsync`：`FileSavePicker` 类型 `"画板页面"`、`.icink`、建议名 `Page-{index+1}.icink`；`_board.SavePage(path)`；异常 `"页面保存失败：…"`。
- `SaveBoardAsync`：类型 `"画板文件"`、`.icink`、建议名 `InkCanvas-Board.icink`；异常 `"画板保存失败：…"`。
- `OpenPageAsync`：若当前页有墨迹或图片 → 确认 `"打开文件会替换当前页上的墨迹和图片。继续吗？"`；`OpenBoardAsync`：若 `HasContent` → 确认 `"打开文件会替换当前画板的所有页面、墨迹和图片。继续吗？"`。
- 文件选择器都 `SuggestedStartLocation=DocumentsLibrary`；`PickInkFileAsync` 只过滤 `.icink`。
- `ShowMessageAsync`：`ContentDialog { Content=TextBlock(Wrap), CloseButtonText="知道了", XamlRoot }`。
- `ConfirmAsync`：`Title`、`PrimaryButtonText="确认"`、`CloseButtonText="取消"`、`DefaultButton=Close`；只有 Primary 返回 true。

`GetToolAnchor(InkTool)`：`Highlighter→_highlighterCell`、`Shape→_shapeCell`、其它→`_penCell`，返回 `WindowChrome.GetScreenBounds(this, cell.Button)`（供调色盘/图形选择器锚定）。

### 7.19 画板关闭与主题/激活

- `OnActivated`：`ApplyTheme(_boardTheme)`；再次 `SuppressTouchFeedback`；`ApplyBoardScale()`；首次激活时对三浮条各 `Motion.PlayEntrance(..., 20f)`。
- `OnClosed`：自动保存 + 退订（见 §7.4）。
- `OnImagesChanged`：`_images.Refresh()`、`_media.Refresh()`、同步 ViewTransform、`UpdateSelectionActions()`。
- `OnImageSelectionChanged/OnMediaSelectionChanged/OnStrokeSelectionChanged` → `UpdateSelectionActions()`。

### 7.20 画板移植要点（App 侧）

- 三浮条是“屏幕底边工具条”而非窗口；进入 HarmonyOS 可做成 `Stack` 覆盖在画布容器底部，`RenderTransform` 缩放改成 `scale()`。
- 选区/元素变换现在统一在 `InkController`：`SelectedElements` + `GetSelectableElements` + `TransformSelectionUndoable`。移植时优先抽这套接口（`ISelectionElement` 适配图片/视频），而不是照搬 BoardWindow 里残存的旧元素拖拽/双指代码。
- `Canvas` 坐标 + `RenderTransform` 矩阵仍是元素层的核心；ArkTS 用 `Canvas` 或 `Stack` 的 `translate/scale/rotate` 组合，但**矩阵累积语义**（`Append` = `Read(existing) * m`，行向量 `p' = p*M`）必须保持，否则旋转后拖动方向会错。
- 图片/视频拖动使用“画布空间平移追加到 Transform”，不是改 `Left/Top`；移植时不要换成绝对位置，否则旋转/翻转后会漂移。
- `MediaPlayerElement` 在 HarmonyOS 对应 `Video`/AVPlayer；`MediaTransportControls` 没有等价物，需要自绘播放控件或放弃“播放中拖拽”边界判断。
- 全屏 `AppWindow.SetPresenter(FullScreen)` 对应 `window.setWindowLayoutFullScreen(true)` + 隐藏系统栏；需要保留窗口模式切换。
- `BoardService` 的页/元素模型（99 页、DIP、6 浮点 Transform）应作为共享逻辑，不绑定 UI。
- 选区面板 `Height=104`、`PanelW` 回退 695 是 WPF 几何；HarmonyOS 上若字体度量不同，`ActualWidth` 会变化，但回退值保持一致。

---

## 8. 计时器 `CountdownWindow`

### 8.1 窗口与几何

- 标题 `"计时器 - Ink Canvas Artistry"`；`MakeBorderless(topmost:true,resizable:true)`；`CenterOnPrimary(1100,700)`；`MinSizeHook(300,200)`；
- 首次 Activated 延迟一个 tick，把 1100×700 DIP 夹到工作区：`wDip=min(1100, work.Width/s)`、`hDip=min(700, work.Height/s)`，居中 `Place`；
- 内容：`Content = AddTitleBar(this, Title, _contentBorder, closeButton:true, minMax:true)`；
- `_contentBorder = Border { Margin=20, CornerRadius=10, BorderThickness=1, 背景/边框来自 PopupBrushes.Surface/Border }`；
- 默认时间 `_hour=0,_minute=1,_second=0`，`_totalSeconds=60`；初始 `_ring.Value=0`。

### 8.2 大视图布局（`BuildUi`）

- `_bigViewbox = Viewbox { Margin=20, Child=bigGrid }`；`bigGrid` 固定 `200×180`，叠放：`_ring`、`_digitRow`、`_adjustLayer`、`_checkCircle`、`_stopPill`、`_controlsRow`。
- 环形进度 `CountdownRing`：`Width=150, Height=150`，顶部居中。绘制规则（Controls）：
  - `scale=min(w,h)/34`；`radius=14*scale`；`thickness=1.5*scale`；
  - 轨道 `#EAEAEA`；进度 `#0067C1`；暂停 `#1A71C8`；
  - 弧从 12 点方向顺时针，`sweep=value*360`，`value>=1` 时取 `359.99`；`value<=0` 只画轨道。
- 数字行 `_digitRow`：`Height=28.5`，居中，`Margin=(0,0,0,25)`；列宽 `28,8,28,8,28`；每个单位两个 `14` 宽单元；数字 `FontSize=26, SemiBold`；冒号 26 SemiBold，`Margin=(0,-2,0,0)`。
- 调整层 `_adjustLayer`：`Height=88.5`（数字上下各 ±30），`Margin=(0,0,0,25)`，默认 Collapsed；每单位一个 Grid，上 `+5/+1`、下 `-1/-5`；`Stepper` 按钮 `29×13`、字号 8、圆角 2、边框 0.5、内容单行。
- 对勾圆 `_checkCircle`：直径 20，accent 底，`\uE74E` 10 pt 白，`Margin=(0,113,0,0)`，默认 Collapsed；点击 `ConfirmEdit()`。
- 结束时间胶囊 `_stopPill`：`Height=18`、圆角 9、边框 0.5、`Margin=(0,23,0,0)`、默认 Collapsed；内部 `StackPanel Horizontal, Spacing=2, Margin=(6,0,6,0)`：时钟图标 `\uEDAC` 10 + 文本 9。
- 控制行 `_controlsRow`：`Height=30`、`Spacing=10`、底部居中：
  - 开始/暂停：圆 20，accent 底，图标 `\uEDB5`（播放）/`\uEDB4`（暂停）10 pt 白；
  - 重置：圆 20，透明底 + 0.5 边框，图标 `\uE72C` 10；
  - 两个灰色禁用覆盖 `_startCover/_resetCover`：圆 20，`#BFBFBF`，同一图标 10 pt 白；`_resetCover` 初始 Visible（未开始前重置禁用）。
- 右列 `_rightColumn = Viewbox { Margin=20, HorizontalAlignment=Right }`，内部 `StackPanel Horizontal, Height=180`，三个圆按钮（20，`Margin=5`，底部对齐）：
  - 紧凑 `\uE8C5` 9，默认 Collapsed（仅结束时间胶囊可见时显示）；
  - 最大化/还原 `\uE740` 9 / `\uE73F` 9；
  - 关闭 `\uE711` 9，`#E32A34` 底，白字。
- 紧凑模式 `_compactTime`：`FontSize=56`、Black、居中、默认 Collapsed、文本 `"00:01:00"`；点击 `ToggleCompact`。
- 底部拖动条 `_dragPill`：`64×15`、圆角 8、灰色、`Margin=(0,0,0,5)`、底部居中、默认 Collapsed；`EnableDragMove`。
- `_contentBorder` 的 `inner` 叠放顺序：`_bigViewbox`、`_rightColumn`、`_compactTime`、`_dragPill`。

### 8.3 编辑规则

`ToggleEdit()`：运行中不允许进入编辑；编辑中再点数字则 `ConfirmEdit`。

`ConfirmEdit()`：若 `0:0:0` 则 `_second=1`；恢复环形/隐藏调整层与对勾/显示控制行；`SetDigitsFromConfigured()`。

`Adjust(unit,delta)` 的环绕（不是取模）：

| 单位 | 范围 | 越界规则 |
|---|---|---|
| 小时 | 0..99 | `>=100 → 0`；`<0 → 99` |
| 分 | 0..59 | `>=60 → 0`；`<0 → 59` |
| 秒 | 0..59 | 同上 |

`SetDigits(TimeSpan)`：`h=totalHours, m=Minutes, s=Seconds`；逐位 `h/10,h%10,m/10,m%10,s/10,s%10`；`_compactTime = $"{h:00}:{m:00}:{s:00}"`。

### 8.4 计时引擎（WPF `Timer_Elapsed` 1:1）

`OnTick()`：

```
elapsed = DateTime.Now - _startTime
spentPercent = elapsed.TotalSeconds / _totalSeconds
left = max(0, totalSeconds - elapsed)
display = ceil-to-next-second(left)         // (left.Ticks + TicksPerSecond-1)/TicksPerSecond*TicksPerSecond
_ring.Value = max(0, 1 - spentPercent)
SetDigits(display)
if spentPercent >= 1:
    _ring.Value=0; SetDigits(0)
    _timer.Stop(); _isRunning=false; _isPaused=false
    glyphs = Play; _startCover.Visible
    SetPillVisible(false); PlayFinishSound()
```

`StartPause()`：

- 已在跑且暂停 → 恢复：`_startTime += DateTime.Now - _pauseTime`；`_ring.IsPaused=false`；glyph=Pause；`_isPaused=false`；`_timer.Start()`；`UpdateStopTime()`；显示结束胶囊。
- 已在跑未暂停 → 暂停：`_pauseTime=Now`；`_ring.IsPaused=true`；glyph=Play；隐藏胶囊；`_isPaused=true`；停 timer。
- 全新开始：
  - `_startTime=Now`；`_totalSeconds=(h*60+m)*60+s`；若 `<=0` 返回；
  - `_ring.IsPaused=false; _ring.Value=1`；
  - glyph=Pause；隐藏 start/reset 灰色覆盖；
  - 自适应 interval：`total<=10 ? 20 : total<=60 ? 30 : total<=120 ? 50 : 100`（ms）；
  - `_isPaused=false; _isRunning=true; timer.Start()`；`UpdateStopTime`；显示胶囊。

`Reset()`：

- 未运行：恢复配置数字；`resetCover` 显示、`startCover` 隐藏；隐藏胶囊；返回。
- 已暂停：完整停止回配置时间；显示 reset 覆盖、隐藏 start 覆盖；隐藏胶囊；glyph=Play；`_isRunning=false`；停 timer；`_isPaused=false`；`ring.Value=0`；`ring.IsPaused=false`。
- 运行未暂停：`_startTime=Now`；`UpdateStopTime()`；`OnTick()`（即重新计时）。

`UpdateStopTime()`：`(_startTime + TimeSpan.FromSeconds(_totalSeconds)).ToString("t", CultureInfo.CurrentCulture)`。

`SetPillVisible(v)`：同时控制 `_stopPill` 与 `_minimalBtn` 的 Visibility。

`PlayFinishSound()`：`Path.Combine(AppContext.BaseDirectory,"Assets","TimerDownNotice.wav")`；不存在则静默；`_player ??= new MediaPlayer()`，`Source=MediaSource.CreateFromUri(new Uri(path))`，`Play()`；异常吞。

### 8.5 紧凑 / 最大化

- `ToggleCompact()`：
  - 退出紧凑：若最大化先 `Restore` 并恢复全屏 glyph；目标 1100×700；用 `MinSizeHook.TryGetMonitorBounds` 夹到监视器并居中（失败则 `AppWindow.Resize`）；显示大视图/右列，隐藏紧凑文字/拖动条。
  - 进入紧凑：若最大化先 Restore；`AppWindow.Resize(ceil(400*scale), ceil(250*scale))`；隐藏大视图/右列；显示紧凑文字/拖动条。
  - `_isCompact = !_isCompact`。
- `ToggleFullscreen()`：只切换 `OverlappedPresenter.Maximize()/Restore()`，glyph 相应切换；不是真正的全屏 presenter。
- `AppWindow.Changed` 在 `DidSizeChange` 时同步 `_fullscreenGlyph`。

### 8.6 生命周期

`Closed` → `Cleanup()`：`_disposed` 幂等；停 timer；释放 `MinSizeHook`；Dispose MediaPlayer。`Dispose()=>Cleanup()`。

### 8.7 `MinSizeHook`（详见 §5.9）

- 最小尺寸 300×200 DIP；计时器与指南窗口共用。

---

## 9. 抽奖 `LotteryWindow` + 名单导入

### 9.1 窗口与数据路径

- 标题 `"抽奖 - Ink Canvas Artistry"`；`MakeBorderless(topmost:true,resizable:true)`；`CenterOnPrimary(900,500)`；首次激活夹到工作区；
- 数据目录：`%APPDATA%\InkCanvas`；`Names.txt`、`Replace.txt`；
- 常量：`EffectivePool = _lottery.Names.Count > 0 ? Names.Count : max(1, PeopleCount)`；
- `_totalCount` 默认 1（**不读取 `Rand.PeopleCount` 作为抽选人数**；`PeopleCount` 仅在无名单时作为号码池大小）；
- `_autoClose`：单次抽选模式；`Loaded` 后 100 ms 自动 `Roll()`；出结果后 1500 ms 自动 `Close()`；不播放入场动画。

### 9.2 布局度量

- 根 Grid 两列：`1.8*` / `1*`；
- 左列 `Viewbox`：`Margin=(20,0,20,0)`，居中；内容 `StackPanel Horizontal, MinHeight=470, Spacing=50`，三个结果 `TextBlock FontSize=70, 居中`（默认只显示 `_out1`，`_out2/_out3` Collapsed）；
- 右列 `Viewbox`（`Stretch=Uniform, StretchDirection=DownOnly, 左对齐, 垂直居中`）包 `StackPanel Spacing=40`：
  - 步进器 `StackPanel Horizontal`：减号圆按钮 70（`\uECC9`，字号 `size*0.45=31.5`）、数字 `TextBlock Width=80, Margin=30, FontSize=55, 居中, "1"`、加号圆按钮 70（`\uECC8`）；
    - 减号在 `_totalCount<2` 时无效；加号在 `_totalCount >= EffectivePool` 时无效；
  - 抽奖按钮 `Border 200×70, CornerRadius=10, 背景 Accent()`：内部 `StackPanel Horizontal, Spacing=10, Margin=(3,0,3,0)`：图标 `\uE77B` 30 白 + 文字 `"抽奖"` 35 白；
  - 灰色滚动覆盖 `_randCover` 同尺寸，`DisabledCover()`，默认 Collapsed；
  - `randGrid` 叠放按钮与覆盖，`Tapped → Roll()`；
- 导入胶囊 `pill`：`Height=40`，`Margin=(10,10,60,10)`，右/下对齐，圆角 20，边框 1；内部 `StackPanel Horizontal, Spacing=6, Margin=(15,0,15,0)`：people 图标 `\uE716` 18 + 文本 16（默认 `"点击此处以导入名单"`）；
- 关闭圆按钮 `CircleButton(40, "\uF78A", ..., Danger(), White)`，`Margin=10`，右/下对齐；
- 内容卡 `_contentBorder = Border { Margin=10, CornerRadius=10, BorderThickness=1, Surface/Border }`。

### 9.3 名单加载 `ReloadNames()`

1. 若 `Names.txt` 存在：
   - 读全部行；`Replace.txt` 存在则读全部行；
   - 对每一行 `s`，按顺序应用替换规则：`idx = rule.IndexOf("-->")`；若 `idx>=0 && s == rule[..idx]` → `s = rule[(idx+3)..]`（**整行精确匹配**，不是子串替换）；
   - `s.Length>0` 才加入；
   - `_lottery.LoadNames(names)`（内部再 `Trim` 并跳过空）；`_lottery.PeopleCount = names.Count`；
   - 若 `names.Count==0` → `PeopleCount=60`，文本恢复占位；
   - 否则 `_peopleText.Text = names.Count.ToString()`。
2. 若文件不存在：保持数字模式，`_peopleText` 占位，`PeopleCount` 用设置值。
3. 异常（锁定/不可读）：回落占位。
4. 最后若 `_totalCount > EffectivePool` → `_totalCount = max(1, EffectivePool)`；更新数字标签。

### 9.4 抽奖 `Roll()`

- `_rolling` 防重入；显示 `_randCover`；隐藏 `_out2/_out3`；
- `pool = EffectivePool`；`Random` 新建；
- **闪烁阶段**：5 次，每次：
  - `r = random.Next(1, pool+1)`；若 `pool>1` 且 `flicker` 已含 r，则重抽直到唯一；
  - `flicker.Add(r)`；若 `flicker.Count >= pool` → `flicker.Clear()`（池小于 5 时避免死循环）；
  - `_out1.Text = DisplayName(r)`；`await Task.Delay(150)`。
- **最终抽取**：
  - `NoRepeat`：循环 `_totalCount` 次 `_lottery.Pick()`；返回 null 则 break（池耗尽）；`Pick()` 内部 `Random.Next(poolSize)`，`NoRepeat && _used.Count>=poolSize` 返回 null；`_used` 是**服务级持久集合**（跨多次开窗/抽奖），只有 `LoadNames` 时清空；无 UI 重置。
  - 否则：`used = HashSet<int>`；循环 `_totalCount` 次取 `r=Next(1,pool+1)`；若 `pool>1` 且 used 含 r 重抽；加入 outputs。
- **输出分组**（注意分支用 `_totalCount`，不是 `outputs.Count`）：
  - `outputs.Count==0` → `_out1.Text="已抽完"`；
  - `_totalCount<=5`：`_out1 = string.Join("\n", outputs)`；
  - `<=10`：`split=(outputs.Count+1)/2`；`_out1=Take(split)`；`_out2=Skip(split)`；显示 `_out2`；
  - `>10`：`a=(outputs.Count+1)/3`；`b=(outputs.Count+1)*2/3`；`_out1=Take(a)`；`_out2=Skip(a).Take(b-a)`；`_out3=Skip(b)`；显示 `_out2/_out3`。
- 隐藏覆盖；`_rolling=false`；若 `_autoClose` → `await Task.Delay(1500); Close();`。
- `DisplayName(number)`：有名单且 `number<=Names.Count` → `Names[number-1]`，否则数字字符串。

### 9.5 名单导入窗口 `NamesInputWindow`

- 标题 `"名单导入 - 抽奖 - Ink Canvas Artistry"`；`400×500`，顶层可调尺寸，`CenterOnPrimary`，首次激活夹工作区；
- 标签：`"请在下方输入名单，每行一人（建议直接粘贴表格姓名列）"`，`Margin=10`，Wrap；
- `TextBox`：`Margin=(10,40,10,50)`，`AcceptsReturn=true`，`TextWrapping=NoWrap`，双向滚动条 Auto；
- 右下 `关闭` Button：100 宽，`Margin=10`；
- `Loaded` 时若 `Names.txt` 存在则读入；记录 `_origin`；
- `AppWindow.Closing`：若 `_box.Text == _origin` 直接允许；否则 `args.Cancel=true`，`ContentDialog`（Title `"名单导入"`，Content `"是否保存？"`，Primary `"是"`，Close `"否"`，默认 Primary）：
  - 是 → 创建 `DataDir` 并 `File.WriteAllText(NamesFilePath, _box.Text)`；异常吞；
  - 然后 `_allowClose=true; Close()`。
- 自身 900 ms `EnsureAbove`，避免被 overlay 压住；
- 打开时 owner `_root.IsHitTestVisible=false`，关闭恢复并 `ReloadNames()`（WPF ShowDialog 模态模拟）。

---

## 10. 回放 `ReplayWindow`

- 标题 `"墨迹回放"`；`MakeBorderless(topmost:true,resizable:true)`；`CenterOnPrimary(900,680)`；首次激活夹工作区；`AddTitleBar(..., closeButton:true, minMax:true)`；
- 构造时 `strokes = source.ToList()`；`InkReplaySession(strokes)`（默认 **12 ms/点**）；`ReplayClock(session.Duration)`；
- 专用 `_replayController`：`RecognizeShapes=false`、`MultiTouchEnabled=false`、`Tool=Select`；`_surface.Controller=...`；
- 布局：
  - `host = Grid { Background=#FAFAFA }` 放 `_surface`；
  - 根 Grid 两行：`*`（host） + `Auto`（toolbar）；
  - toolbar `Border`：背景 `IcSurfaceBrush`、上边框 1 `IcStrokeBrush`、`Padding=(12,8,12,8)`；内容 `Viewbox(Stretch=Uniform, DownOnly, 左对齐)` 包 `StackPanel Horizontal, Spacing=8`；
  - 控件：播放/暂停 `TouchIconButton`（Glyph `Glyphs.Play`/`Glyphs.Pause`）、从头 `Glyphs.Restart`、进度 `Slider 0..1000 Width=320`、时间 `TextBlock MinWidth=96`、速度 `ComboBox Width=96`（`0.5x/1x/2x/4x`，默认 SelectedIndex=1）、关闭 `Glyphs.Close`；
- 速度映射：索引 0→0.5、2→2、3→4、其它→1；
- 进度 `ValueChanged`（非 `_suppressSeek`）：`Seek(e.NewValue/1000 * DurationMs)` + `RenderFrame()`；
- `_timer` **33 ms**；`OnTick`：`delta = now - _lastTick`；`_clock.Advance(delta)`；`RenderFrame()`；`SyncPlayGlyph()`；
- `RenderFrame()`：
  - `visible = session.VisibleAt(_clock.ElapsedMs)`；
  - `replayController.Document.Clear(); AddRange(visible)`（无历史）；
  - `_progress.Value = _clock.Progress * 1000`（期间 `_suppressSeek=true`）；
  - `_timeLabel.Text = $"{TimeSpan.FromMilliseconds(ElapsedMs):mm\\:ss} / {session.Duration:mm\\:ss}"`；
- `Restart`：`Seek(0)`、`Play()`、SyncGlyph、RenderFrame；
- 播放/暂停：`_clock.Toggle()`；
- 构造末尾 `RenderFrame()`；若有笔画则自动 `Play()`；
- `Closed`：停 timer、`_surface.Dispose()`、`_replayController.Dispose()`；首次 Activated `Motion.PlayEntrance(toolbar, 16f)`；
- `ReplayClock` 语义：`Speed` 倍率；`Advance(delta)` 后到末尾自动 `IsPlaying=false`；`Seek` clamp 0..duration；`Progress` 在 duration=0 时为 1。

---

## 11. 设置 `SettingsWindow`

### 11.1 窗口几何与骨架

- 标题 `"Ink Canvas 设置"`；`MakeBorderless(topmost:true,resizable:false)`；**无标题栏按钮、不可拖动**；
- `PanelWidthDip=440`，`PanelInsetDip=150`；
- `PlacePanel()`：
  ```
  scale = GetDpiScale
  widthDip  = min(440, work.Width/scale)
  heightDip = min(max(240, work.Height/scale - 2*150), work.Height/scale)
  w=ceil(widthDip*scale); h=ceil(heightDip*scale)
  居中 Place(work.X+(work.Width-w)/2, work.Y+(work.Height-h)/2, w,h)
  ```
  构造期 DPI 可能为 1，首次 Activated 延迟一个 tick 再 `PlacePanel()` + `AttachDpiTracking()`；
- 面板结构：`_panel Border`（`Surface/Border`，边框 1，圆角 5）包 `inner Grid`：
  - `scroll`：`ScrollViewer { Margin=(0,60,0,50), VerticalScrollBar=Auto, Horizontal=Disabled, HorizontalContentAlignment=Stretch }`，内容 `_content StackPanel { Spacing=10, Margin=20 }`；
  - `header`：`Grid { Height=40, Margin=20, 顶部 }`，内容 `StackPanel Horizontal`：`FontIcon \uE713` 26 pt/28×28 + 文字 `"详细设置"` 26 Bold `Margin=(10,-2,0,0)`；
  - `footer`：`Grid { Height=50, 底部 }`，右对齐 `关闭` Button `120×30, Margin=10`；点击 `Close()`；UIA 名称 `"关闭设置面板"`；
  - `_notification`：`Border`（`BadgeBackground/BadgeForeground`，圆角 5，`Padding=(14,8,14,8)`，`Margin=(20,0,20,58)`，居中/底部，默认 Collapsed）；文本 14；3 秒单次 timer 后隐藏；
- 根 `Grid` 背景：深色 `#FF1F1F1F`，浅色 `#FFF0F3F9`（`PopupBrushes.IsDark ? (31,31,31) : (240,243,249)`）；
- 入场：`_panel` opacity 0.5→1、`Offset Y 10→0`，**500 ms**；只播一次；
- 主题解析：`BrushTheme = _theme == 0 ? (Application.Current.RequestedTheme == Dark ? 2 : 1) : _theme`；`_theme` 来自 `Settings.Appearance.Theme`。
- 所有卡片 `Card()`：深色背景 `#FF2B2C2F`、边框 `#24FFFFFF`；浅色背景 `#FFFFFFFF`、边框 `#1F000000`；边框 1、圆角 10、`Padding=(16,12,16,12)`、横向拉伸。
- `Section(title)`：26 Bold，`Margin=(0,16,0,2)`。
- `ToggleRow`：ToggleSwitch `OnContent="开"`、`OffContent="关"`、左对齐；`Toggled` → set + Commit。
- `SliderRow`：Slider 拉伸 + 右侧读数 `MinWidth=48`、右对齐、格式 `0.##`；`ValueChanged` → 读数 + set + Commit；`StepFrequency` 默认 1。
- `ComboRow`：ComboBox 拉伸；先加 items 再设 `SelectedIndex=clamp(selected,0,count-1)`，再订阅 `SelectionChanged`（避免初始 -1 强制回写）。
- `TextRow`：TextBox 拉伸；**只在 `LostFocus`** 写值并 Commit。
- `ButtonRow`：标题 + 12 pt 描述 + 全宽按钮（MinHeight=40）。
- `DeferredRow`：显示 `当前保存值：{value}` + 原因；**不提供可交互控件**。
- `InfoRow`：12 pt、Wrap、次要前景色。
- `SecondaryText()`：深色 `#A8FFFFFF`，浅色 `#9E000000`。
- `Accessible(control,name,helpText)`：设置 `AutomationProperties.Name/HelpText`。

### 11.2 提交 / 重建 / 重置 / 重启

- `Commit()`：`ApplySettings()` → `SaveSettings()` → 若 `Appearance.Theme != _theme` 则下一调度帧 `RebuildContent()`，否则 `WindowChrome.ApplyTheme(this, Theme)` → `SettingsChanged?.Invoke()`。
- `RebuildContent()`：重读 `_theme`、清空 `_content`、`BuildSections()`、重建 scroll/header/footer/notification/panel/root、`ApplyTheme`。
- `RestartApp()`：`Process.Start(new ProcessStartInfo(Environment.ProcessPath, "-m") { UseShellExecute=true })`；失败提示 `"重启失败：无法启动新的程序实例。"`；成功 `ExitApp()`。`-m` 在移植版无互斥用途，仅为对齐原版。
- `ResetSettings()`：`Settings.ResetToRecommendation()` → `AutoStartService.SetEnabled(true, Environment.ProcessPath ?? "InkCanvas.exe")` → `ApplySettings()` → `SaveSettings()` → `SettingsChanged` → 下一调度帧 `RebuildContent()` + 提示 `"设置已重置为默认推荐设置~"`。
- `ExitApp()`：优先 `_shell.Exit()`；否则 `_updater.ExitForInstall?.Invoke()`；否则 `Close()` + `Application.Current.Exit()`。
- `SilenceTimes`：72 项，`i=0..71`，`"{i*20/60:00}:{i*20%60:00}"`，即 `00:00` 到 `23:40` 每 20 分钟一档。

### 11.3 分区与逐行清单（原 GroupBox 顺序）

**顶部卡片**（`BuildTopCard`）：
- 文字 `"设置更改将自动保存，部分设置需要重启软件后生效"`，15 pt，居中，`Margin=(0,0,0,10)`；
- 三个按钮 `116×45`，`Spacing=5`：
  - `重启软件` glyph `\uE72C` 16；
  - `重置设置` glyph `\uE8F7` 18；
  - `关闭软件` glyph `\uE7E8` 18；
  - 文字 16 Bold；容器 `Border Height=100, Margin=(0,0,0,10), 边框 1, 圆角 5`。

**启动选项**：

| 控件 | 标题 | 设置/JSON | 备注 |
|---|---|---|---|
| Toggle | 自动检查更新 | `startup.isAutoUpdate` | |
| Toggle | 静默更新 | `startup.isAutoUpdateWithSilence` | note：静默时间段内直接下载安装，否则弹窗 |
| Combo | 静默更新起始时间 | `startup.isAutoUpdateWithSilenceStartTime` | 72 档 |
| Combo | 静默更新终止时间 | `startup.isAutoUpdateWithSilenceEndTime` | 72 档 |
| Info | `# 若终止时间小于起始时间，即将终止时间视为第二天的时间。若起始时间与终止时间相同，即视为全天候时间。` | — | |
| Toggle | 开机时运行 | 不存 Settings；`AutoStartService` | 注册表 Run |
| Toggle | 开机运行后收纳到侧边栏 | `startup.isFoldAtStartup` | 下次启动生效 |
| Toggle | 启用笔尖模式 | `startup.isEnableNibMode` | |

**画板选项**：

| 控件 | 标题 | 设置/JSON | 状态/范围 |
|---|---|---|---|
| Deferred | 上传图片时将画质压缩至 1080P 及以下 | `canvas.isCompressPicturesUploaded` | 原因：图片上传压缩尚未接入 |
| Deferred | 显示画笔光标 | `canvas.isShowCursor` | 尚未接入 |
| Combo | 橡皮大小 | `canvas.eraserSize` | 5 档 `小/较小/中（默认）/较大/大` |
| Toggle | 退出画板模式后隐藏墨迹 | `canvas.hideStrokeWhenSelecting` | note：切换鼠标模式时是否仍显示墨迹由此决定 |
| Deferred | 保留双曲线渐近线 | `canvas.hyperbolaAsymptoteOption` | 展示 `是/否/每次询问`；图形生成尚未读取 |
| Combo | 笔迹风格 | `canvas.inkStyle` | `点集笔锋/速率笔锋/关闭笔锋` |
| Slider | 画笔粗细（原菜单 1–30；设置值为菜单值的一半） | `canvas.inkWidth` | 0.5..15，step 0.5 |
| Deferred | 橡皮模式 | `canvas.eraserType` | `图标切换/面积擦/线条擦`；只有一种行为 |
| Deferred | 白板背景配色 | `canvas.usingWhiteboard` | 仅启动初始化；运行中切换未接入 |
| Info | `荧光笔透明度：{InkAlpha}（原菜单范围 20–240，步长 20；在荧光笔调色盘中调整，按笔迹保存）` | | |

**手势选项**：

| 控件 | 标题 | 设置/JSON |
|---|---|---|
| Combo | 缩放/旋转变换中心点（`画布中心点（默认）/全部内容的中心点/手势操作的中心点`） | `gesture.matrixTransformCenterPoint` |
| Deferred | 自动开关两指缩放功能 | `gesture.AutoSwitchTwoFingerGesture` |
| Toggle | 允许双指旋转选中的墨迹 | `gesture.isEnableTwoFingerRotationOnSelection` |
| Toggle | 启用多点触控模式 | `gesture.isEnableMultiTouchMode` |
| Toggle | 双指缩放 | `gesture.isEnableTwoFingerZoom` |
| Toggle | 双指平移 | `gesture.isEnableTwoFingerTranslate` |
| Toggle | 双指旋转 | `gesture.isEnableTwoFingerRotation` |

**墨迹识别选项**：`启用墨迹识别` Toggle → `inkToShape.isInkToShapeEnabled`。

**外观选项**：

| 控件 | 标题 | 设置/JSON | 范围/备注 |
|---|---|---|---|
| Combo | 颜色主题（`浅色主题/深色主题/跟随系统`） | `appearance.theme` | 显示索引→存储值映射 `{1,2,0}` |
| Deferred | 浮动工具栏显示文字注释 | `appearance.isEnableDisPlayFloatBarText` | 尚未接入 |
| Deferred | 浮动工具栏背景色 | `appearance.isColorfulViewboxFloatingBar` | 尚未接入 |
| Slider | 浮动工具栏底部边距 (像素) | `appearance.floatingBarBottomMargin` | 0..200 |
| Quick | 底部边距快捷值 `[100]` | 同上 | 文案 `重置为 100` |
| Slider | 浮动工具栏 UI 缩放 (%) | `appearance.enableViewboxFloatingBarScaleTransform` | 50..150 |
| Quick | 工具栏缩放快捷值 `[80,90,100,110,125]` | 同上 | |
| Slider | 画板 UI 缩放 (%) | `appearance.enableViewboxBlackBoardScaleTransform` | 50..150 |
| Quick | 画板缩放快捷值 `[80,90,100,110,125]` | 同上 | |
| Deferred | 按钮背景透明 | `appearance.isTransparentButtonBackground` | |
| Deferred | 显示退出按钮 | `appearance.isShowExitButton` | |
| Deferred | 显示橡皮按钮 | `appearance.isShowEraserButton` | |
| Deferred | 显示隐藏控制按钮 | `appearance.isShowHideControlButton` | |
| Deferred | 显示左右切换按钮 | `appearance.isShowLRSwitchButton` | |
| Deferred | 显示手指模式切换按钮 | `appearance.isShowModeFingerToggleSwitch` | |

**PPT 选项**：

| 控件 | 标题 | 设置/JSON |
|---|---|---|
| Toggle | 显示 PPT 下方翻页箭头按钮 | `behavior.isShowBottomPPTNavigationPanel` |
| Toggle | 显示 PPT 下方页码按钮 | `behavior.isShowPPTNavigationBottom` |
| Toggle | 显示 PPT 侧边翻页箭头按钮 | `behavior.isShowSidePPTNavigationPanel` |
| Toggle | 显示 PPT 侧边页码按钮 | `behavior.isShowPPTNavigationSides` |
| Toggle | Microsoft PowerPoint 支持 | `behavior.powerPointSupport` |
| Toggle | WPS 支持 | `behavior.isSupportWPS`，同时写 `Ppt.AllowWps` |
| Toggle | 进入 PPT 放映时自动进入批注模式 | `behavior.isShowCanvasAtNewSlideShow` |
| Deferred | 允许幻灯片模式下的双指手势 | `behavior.isEnableTwoFingerGestureInPresentationMode` |
| Toggle | 允许使用手指手势进行幻灯片翻页 | `behavior.isEnableFingerGestureSlideShowControl` |
| Toggle | 自动幻灯片截屏 | `behavior.isAutoSaveScreenShotInPowerPoint` |
| Toggle | 自动保存幻灯片墨迹 | `behavior.isAutoSaveStrokesInPowerPoint` |
| Toggle | 记忆并提示上次播放位置 | `behavior.isNotifyPreviousPage` |
| Toggle | 提示隐藏幻灯片 | `behavior.isNotifyHiddenPage` |
| Toggle | 提示是否已启用自动播放 | `behavior.isNotifyAutoPlayPresentation` |
| Deferred | PPT 选择时不清除墨迹 | `behavior.isNoClearStrokeOnSelectWhenInPowerPoint` |
| Toggle | 鼠标模式时在 PPT 上显示墨迹 | `behavior.isShowStrokeOnSelectInPowerPoint` |

**高级选项**：

| 控件 | 标题 | 设置/JSON | 范围/备注 |
|---|---|---|---|
| Info | `# 启用特殊屏幕模式以调节触控屏触摸大小倍数。# 触控屏触摸大小倍数调整为 0 即为禁用手势橡皮。` | | |
| Toggle | 特殊屏幕模式 | `advanced.isSpecialScreen` | |
| Slider | 特殊屏幕触摸大小倍数 | `advanced.touchMultiplier` | 0..5，step 0.01 |
| Deferred | 触摸大小倍数校准 | `—` | 原校准垫未移植 |
| Deferred | 在调色盘窗口中显示 启用笔尖模式 切换按钮 | `appearance.isEnableDisPlayNibModeToggler` | |
| Slider | 笔尖模式 BoundsWidth | `advanced.nibModeBoundsWidth` | 1..50 |
| Slider | 手指模式 BoundsWidth | `advanced.fingerModeBoundsWidth` | 1..50 |
| Slider | 笔尖模式 ThresholdValue | `advanced.nibModeBoundsWidthThresholdValue` | 0..10，step 0.01 |
| Slider | 手指模式 ThresholdValue | `advanced.fingerModeBoundsWidthThresholdValue` | 0..10，step 0.01 |
| Slider | 笔尖模式 EraserSize | `advanced.nibModeBoundsWidthEraserSize` | 0.01..10，step 0.01 |
| Slider | 手指模式 EraserSize | `advanced.fingerModeBoundsWidthEraserSize` | 0.01..10，step 0.01 |
| Toggle | 四边红外模式 | `advanced.isQuadIR` | note：宽高几何平均 |
| Toggle | 记录日志 | `advanced.isLogEnabled` | |
| Deferred | 关闭软件时二次弹窗确认 | `advanced.isSecondConfimeWhenShutdownApp` | 退出确认未接入 |
| Toggle | 阻止 Win 10/11 的边缘手势 | `advanced.isEnableEdgeGestureUtil` | |
| Info | `以上特殊屏幕与触摸参数按原 WPF 算法移植；按要求未做行为验证，请在实际触控屏与压感笔上复核。` | | |

**自动选项**：

| 控件 | 标题 | 设置/JSON |
|---|---|---|
| Toggle | 进入“希沃白板”时自动收纳 | `automation.isAutoFoldInEasiNote` |
| Deferred | 自动收纳忽略桌面EN批注窗口 | `automation.isAutoFoldInEasiNoteIgnoreDesktopAnno` |
| Toggle | 进入“希沃视频展台”时自动收纳 | `automation.isAutoFoldInEasiCamera` |
| Deferred | 进入“希沃轻白板”时自动收纳 | `automation.isAutoFoldInEasiNote3C` |
| Toggle | 进入“希沃品课”时自动收纳 | `automation.isAutoFoldInSeewoPincoTeacher` |
| Toggle | 进入“鸿合白板”时自动收纳 | `automation.isAutoFoldInHiteTouchPro` |
| Deferred | 进入“鸿合视频展台”时自动收纳 | `automation.isAutoFoldInHiteCamera` |
| Deferred | 进入“文香白板”时自动收纳 | `automation.isAutoFoldInWxBoardMain` |
| Deferred | 进入“中原旧白板”时自动收纳 | `automation.isAutoFoldInOldZyBoard` |
| Toggle | 进入“微软 Whiteboard”时自动收纳 | `automation.isAutoFoldInMSWhiteboard` |
| Toggle | 进入PPT 放映时自动收纳至侧边栏 | `automation.isAutoFoldInPPTSlideShow` |
| Deferred | 自动查杀希沃“PPT 小工具” | `automation.isAutoKillPptService` |
| Deferred | 自动查杀 希沃白板5 | `automation.isAutoKillEasiNote` |
| Toggle | 清屏时自动截图 | `automation.isAutoSaveStrokesAtClear` |
| Toggle | 截图分日期文件夹保存 | `automation.isSaveScreenshotsInDateFolders` |
| Toggle | 截图时自动保存墨迹 | `automation.isAutoSaveStrokesAtScreenshot` |
| Deferred | 切换到鼠标模式后自动清屏 | `automation.isAutoClearWhenExitingWritingMode` |
| Slider | 自动保存所需最少墨迹数（0 表示不限制） | `automation.minimumAutomationStrokeNumber` 0..10 |
| Text | 墨迹、截图自动保存位置 | `automation.autoSavedStrokesLocation` |
| 按钮组 | `设置为 D:\Ink Canvas` / `设置为文档目录` | 前者写 `D:\Ink Canvas`，后者写 `Environment.SpecialFolder.MyDocuments` |
| Info | `* 请注意检查保存文件夹是否有写入权限。` | |
| Deferred | 定期自动删除超过保存时间的墨迹、截图文件 | `automation.autoDelSavedFiles` |
| Deferred | 保存时间设置（Days） | `automation.autoDelSavedFilesDaysThreshold` + `OriginalMenuCatalog.AutoDeleteDays`（展示原选项列表） |

**关于 Ink Canvas Artistry**：
- Info `当前版本：{Updater.CurrentVersion}`（无 updater 时 `"Ink Canvas · WinUI 3"`）；
- Info 开源协议说明；
- LinkRow `https://github.com/InkCanvas/Ink-Canvas-Artistry` / credit `本项目开发者: CJ Wu (ChangSakura)`；
- LinkRow `https://github.com/WXRIW/Ink-Canvas` / credit `原项目开发者: XY Wang (WXRIW)`；
- Info 贡献者列表；
- ButtonRow `检查更新` / 描述 `从 GitHub 发行版检查新版本` → `Updater.CheckAsync(interactive:true)`；
- ButtonRow `操作指南` / `模式、工具、手势与快捷键` → `InfoWindow.OpenGuide(Theme)`；
- ButtonRow `更新日志` / `查看版本变更记录` → `InfoWindow.OpenChangeLog(Theme)`。

**抽奖（移植附加）**：
- Slider `抽取人数` → `randSettings.peopleCount` 1..200；
- Toggle `不重复抽取` → `randSettings.isNotRepeatName`，note 说明移植版按此持久化。

### 11.4 设置面板移植要点

- 440 DIP 宽、上下 150 inset、内容滚动 60/50 是 WPF 精确几何；若 ArkTS 字体度量不同，`TextBlock` 自动高度会变化，但滚动容器能吸收。
- 35 个 `DeferredRow` 是**刻意保留**的“未接线”选项：不要为了实现看起来完整而伪造开关；移植时应按同样策略标注或删除。
- `Appearance.Theme` 的显示索引与存储值不是同一个数（`{1,2,0}`），移植时保留映射，避免 Settings.json 兼容性破坏。
- `Startup.AutoUpdateWithSilenceStartTime/EndTime` 是字符串 `"HH:mm"`，不是枚举/分钟数。
- `AutoSavedStrokesLocation` 默认硬编码 `D:\Ink Canvas`（Windows 路径）；HarmonyOS 必须迁移到沙箱目录，但**键名与字符串值格式保持**以便双向读取（读到不存在的盘符时保存应失败/提示）。
- 设置提交会触发 `ShellController.OnSettingsChanged`，其中浮动栏 `Reposition` 默认 recenter；移植时可选择保留（WPF 行为）或改成保持用户位置。

---

## 12. 辅助窗口

### 12.1 PPT 导航 `PptNavigationWindow`

**几何常量**（`PptNavigationWindow.cs:27-43`）：

```
S = 40/42 ≈ 0.95238                 // WPF Viewbox Height/Width=40 over 42 DIP content
Cell = 40*S      CounterH = 38*S
IconBox = 26*S   IconSz = 20*S     TextSz = 9*S
LineLen = 25*S   LineThick = 1*S   BorderThick = 1*S   Corner = 6*S
GlyphPrevBottom = \uEDD5   GlyphNextBottom = \uEDD6
GlyphPrevSide   = \uEDD7   GlyphNextSide   = \uEDD8
```

**结构**：
- 根 `Border`：`Background=FloatBarBackground(theme)`、`BorderBrush=FloatBarBorder(theme)`、边框 `BorderThick`、圆角 `Corner`、`Opacity=0.4`；
- `StackPanel`：底部为 Horizontal，侧边为 Vertical；子项 `prev / counter / next`；
- `NavCell`：`Border Cell×Cell` 透明，内部 `FontIcon`（图标尺寸 `IconSz`，`Width/Height=IconBox`，居中）；
- `CounterCell`：`Border Cell×CounterH` 透明，内部 Grid：
  - 垂直：三行 `LineThick / 36S / LineThick`；两条分隔线 `LineLen×LineThick` 水平居中；
  - 水平：三列 `LineThick / 36S / LineThick`；两条分隔线 `LineThick×LineLen` 垂直居中；
  - 中间 `_counterText`：`"0/0"`，`TextSz`，居中；
- 计数器点击 → `shell.PptOpenNavigation()`（切鼠标模式 + 打开 PowerPoint 导航窗格）。

**交互**：`AttachClick`：
- `PointerPressed`：`pressed=true`、Capture、`Motion.Pulse(cell,0.92f)`、`e.Handled=true`（不抢放映焦点）；
- `PointerReleased`：若仍在 cell 边界内（0..ActualWidth/Height）→ invoke；`Handled=true`；
- Canceled/CaptureLost 清 pressed。WPF 没有 hover/pressed 视觉，脉冲是全局触摸反馈。

**放置**（`PlaceOnScreen`）：

```
screen = ScreenService.Primary; dpi
vertical = SideLeft || SideRight
counter = _counter.Visibility == Visible
widthDip  = vertical ? 42*S : (counter ? 122 : 82)*S
heightDip = vertical ? (counter ? 120 : 82)*S : 42*S
width=ceil(widthDip*dpi); height=ceil(heightDip*dpi)
marginH = side ? 6 : (BottomLeft ? 2 : 0)      // BottomRight=0
marginV = vertical ? 0 : 2
x = right-side placement ? screen.X+screen.Width-width-marginH : screen.X+marginH
y = vertical ? screen.Y+(screen.Height-height)/2 : screen.Y+screen.Height-height-marginV
```

- 窗口样式：`AddExStyle(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW)`；不抢焦点、不进 Alt-Tab；
- 显隐用 `ShowWindow(SW_SHOWNOACTIVATE)` / `SW_HIDE`，不是 `Window.Activate`；
- 构造末尾 `PlaceOnScreen()` + `SetVisible(false)`；`Update` 每次重排并按 `panelVisible` 显隐；
- `Update(snapshot,panelVisible,counterVisible)`：
  - `_counter.Visibility = counterVisible`;
  - `position = snapshot?.ShowPosition > 0 ? ShowPosition : snapshot?.Slide?.Index ?? 0`;
  - `count = snapshot?.Presentation?.SlideCount ?? 0`;
  - 文本 `$"{position}/{count}"`（无空格）；文本未变不写；
  - `PlaceOnScreen()` + `SetVisible(panelVisible)`。

### 12.2 指南 / 更新日志 `InfoWindow`

**指南模式**（`private InfoWindow(int theme)`）：
- 标题 `"Ink Canvas Artistry 使用指南"`；`700×600`，可调尺寸，最小宽度 500（`MinSizeHook`），标题栏有 min/max/close；
- 头部：图标 `\uEDA7` 24 pt，`40×40`，`Margin=(6,0,6,0)`；文字 `"软件快捷键"` 22，Wrap；`StackPanel Horizontal, Margin=(20,20,20,0)`；
- 12 行键位（`AddRow(key, second, global, description, extraTop)`）；所有 `description` 实参都以 `" ——  "` 开头（em dash 后是**两个**空格，下表省略该前缀）；行高 20，`Margin=(20, extraTop?10:2, 20,2)`，左对齐；键帽 `Border MinWidth=30, Padding=(3,0,3,0), CornerRadius=3, BorderThickness=1`，文字 10（Ctrl 左右 Margin 4，其它 6）；`+ second` 文本 10；全局徽标 `Border Height=18, Margin=(2,0,2,0), Padding=(6,0,6,0), CornerRadius=3`，文字 `"全局快捷键"` 10；
- 行内容（精确文案）：
  1. `Ctrl + Z —— 撤销`（非全局）
  2. `Ctrl + Y —— 重做`（非全局）
  3. `Ctrl + E —— 清屏`（全局）
  4. `Alt + C —— 截屏（Capture）`（全局，`extraTop=true`）
  5. `Alt + V —— 显示/隐藏 笑脸右侧工具栏（Visibility）`（全局）
  6. `Alt + D —— 切换至批注模式 / 显示或关闭调色板（Draw）`（全局）
  7. `Alt + Q —— 退出批注模式（Quit）`（全局）
  8. `Alt + B —— 进入/退出 画板（Board）`（全局）
  9. `Alt + S —— 切换至选择模式（Select）`（非全局）
  10. `Alt + E —— 切换至面积擦/墨迹擦功能（Eraser）`（非全局）
  11. `Alt + L —— 切换至单次直线绘制功能（Line）`（非全局）
  12. `Shift + Esc —— 退出 PPT 放映`（全局）
- 键位块 `Viewbox { Width=500, HorizontalAlignment=Left }`，外层再套 `Stretch=Uniform, StretchDirection=DownOnly, Left`，窗口缩到 500 时按比例缩小不裁切；
- 滚动区 `Margin=(0,0,0,30)`，横向 Disabled；
- 右下红色关闭圆：`Border 50×50, CornerRadius=25, Background=Danger(), BorderThickness=0.5, Margin=(0,0,10,10)`，图标 `\uE711` 20 白；
- 底部拖动胶囊：`64×15, CornerRadius=8, BorderThickness=1, Margin=(0,0,0,5)`，居中/底部；`EnableDragMove(pill)`；
- 卡片：`Border Margin=10, Padding=(20,20,0,0), CornerRadius=10, BorderThickness=1`。

**更新日志模式**（`public InfoWindow(string title, string markdown, int theme)`）：
- 标题由调用方传入（`OpenChangeLog` 用 `"版本更新 - Ink Canvas Artistry"`）；
- `600×390`，不可调整大小，无 min/max；
- Markdown 逐行渲染：
  - `"### "` → 15 pt SemiBold；
  - `"## "` → 18 pt SemiBold；
  - `"# "` → 22 pt Bold；
  - `"- "` / `"* "` → `"•  " + 内容` 14 pt；
  - 空行 → `Border Height=6`；
  - 其它 → 14 pt Wrap；
  - 标题 `Margin=(0,8,0,2)`；正文 `Spacing=8, Padding=24`；
- 卡片 `Margin=10, CornerRadius=10, BorderThickness=1`，内 `ScrollViewer` 纵向 Auto、横向 Disabled。
- `OpenChangeLog` 读 `Path.Combine(AppContext.BaseDirectory,"CHANGELOG.md")`；不存在 `"未找到更新日志文件。"`；读失败 `"无法读取更新日志。"`。

**通用 chrome**（`FinishChrome`）：
- `Content = AddTitleBar(this, Title, content, closeButton:true, minMax)`；`MakeBorderless(topmost:true,resizable)`；`CenterOnPrimary(widthDip,heightDip)`；
- 首次 Activated 延迟 tick，把设计尺寸夹到工作区并居中（`Kind=0` 记录，重激活不弹回）；
- `ApplyTheme(theme)`；`ApplyPalette(theme)`；`ActualThemeChanged` 重新取 1/2；首次 `Loaded` `Motion.PlayEntrance(content, 10f)`；
- `Closed` 释放 `MinSizeHook`。

### 12.3 是否确认 `YesOrNoWindow`

- 标题固定 `"演示文档设置 - Ink Canvas Artistry"`；`450×160`；`MakeBorderless(topmost:true,resizable:false)`；`CenterOnPrimary(450,160)`；close-only 标题栏；
- 表面色：深色 `ARGB(255,43,44,47)`，浅色 White；
- 图标 `\uE786` 20，`Viewbox Height=30, Margin=10`；
- 文案 `TextBlock`：Wrap，`Margin=(60,0,10,0)`，垂直居中；
- 内部 Grid `Margin=10`，两行 `*` / `35`：
  - 第 0 行图标+文案；
  - 第 1 行按钮 `StackPanel Horizontal, Spacing=10`，右/下对齐；
- `是` Button：`Width=100`，资源覆盖：
  - `ButtonBackground=#15803D`、hover `#166534`、pressed `#306534`；
  - 前景白色（三态）；边框透明（三态）；
  - 点击 → `_yesAction?.Invoke(); Close();`
- `否` Button：`Width=100`，默认主题；点击 → `_noAction?.Invoke(); Close();`
- 标题栏 X 关闭不调用任何 action；
- 入场 `Motion.PlayEntrance(surfaceGrid, 10f)`；自身 900 ms `EnsureAbove`（因为它不在 Shell 的 RaiseChrome 清单里）；
- `AskAsync(owner,text)`：
  - 用 owner 的 `DispatcherQueue`（无则当前线程，再无可返回 false 的 Task）；
  - 入队失败返回 false；
  - 主题由 owner 内容 `ActualTheme == Dark ? 2 : 1`；
  - `YesAction` → `tcs.TrySetResult(true)`；`NoAction` → false；`Closed` → false（防止 X 关闭永久挂起）；
  - `RunContinuationsAsynchronously`。

### 12.4 笔/荧光笔调色盘 `PenPaletteWindow`

- 标题：`highlighter ? "荧光笔" : "墨迹选项"`；
- 宿主：`ScrollViewer → Viewbox(Uniform，左上) → PenPalette`；`AutomationProperties.Name = Title`；
- 设计尺寸：优先用 `PenPalette.CardSurface.ActualWidth/Height`；否则 `Measure(∞,∞)`；仍 `<50` 用 `DesignWidthDip`/`DesignHeightDip`（**248 / 208**）与控件计算的 `DesignHeightDip`；
- 缩放：`_scalePercent = clamp(scalePercent,50,200)`；`_host.Width/Height = design*scale`；
- 构造：`MakeBorderless(topmost:true)`；`FloatingBarWindow.EnableTransparency`；`ClipToSurfaces(CardSurface)`；`ApplyTheme(theme)`；初始 `PlaceDip(100,100,design*scale,design*scale)`；`RefreshFromController()`；
- `PlaceAboveBar(getAnchor, ownerHwnd)`：记录锚点函数与 owner；`TrackAnchor` 在 owner 移动/改尺寸时重锚；`Reanchor()`：
  - `PhysicalSize` 按锚点工作区夹取；`w=min(ceil(design*scale*dpi),work.Width)`，h 同理；
  - `gap=ceil(8*dpi)`；`GetPopupPosition`；`PlaceFitted`：`_host.Width=w/dpi`、`PlaceClient`、`EnsureAbove`；
- 关闭条件：
  - `Escape`；
  - 前景窗口每 **300 ms** 轮询：只有 `_everForeground` 为 true 后才允许外部点击关闭；`fg != self && fg != owner` → Close；
  - overlay 落笔：shell `DismissOnOverlayInput()` **无条件关**（WPF HideSubPanels 对 PenPalette）；
- `ApplyScale`：更新缩放；已锚定→重锚；否则按当前位置重设；未 Loaded 时 `PlaceDip`；
- 暴露 `InkWidthChanged/InkStyleChanged/InkAlphaChanged/AttributesChanged/HighlighterAlpha/LightColorScheme` 转发到 `PenPalette`；
- `InkAlphaChanged` setter 会 `RefreshDesignSize()`（接线后“未接 alpha”提示消失，高度变化）。

### 12.5 图形选择器 `ShapePickerWindow`

- 标题 `"图形"`；宿主同调色盘：`ScrollViewer → Viewbox → ShapePreview`；
- `ShapePreview.AutoHide` 有**跨开窗静态状态** `_lastAutoHide`（默认 true）；`AutoHideChanged` 与 `Closed` 都写回；
- 设计尺寸：`Measure(∞,∞)`；`<50` 时回退 **屏幕 208×234 / 画板 254×286**；
- `SelectionMade(kind, sticky)`：`controller.StickyShape = sticky`；`onPicked()`；若 `AutoHide` → `Close()`；
- 关闭条件：
  - `Escape` 无条件；
  - 外部点击轮询：只有 `_everForeground && _preview.AutoHide` 才关；**图钉固定时外部点击不关**；
  - overlay 落笔：`DismissOnOverlayInput()` **仅 AutoHide 时关**；
- 锚点/缩放/DPI/`PlaceAboveBar`/`ApplyScale` 与调色盘同一套（`gap=8*dip`）；
- `ShellController.OpenShapePicker` 每次都先关旧窗再新建；`boardMode` 只影响回退尺寸与预览主题。

---

## 13. 自检管线 `Diagnostics/SelfTest.cs`

### 13.1 入口与输出

- 仅由 `--selftest=<dir>` 触发：`App` 在 1200 ms 单次 timer 后 `SelfTest.Run(_shell, dir)`；
- 另有 `--perfcheck=<dir>`：2 s 后 `PerformanceCheck.Run`，输出 `performance.json`（不是自检的一部分）；
- `Run` 先 `Directory.CreateDirectory(outputDirectory)`；
- **先**执行三项无窗口回归：`RenderingRegression.Run(checks)`（Win2D 栅格/几何缓存）、`EraserRegression.Run(checks)`（区域擦/线条擦/多触点/撤销）、`SelectionRegression.Run(checks)`（框选/克隆/旋转/混合元素/双指选择）；并 `await SelectionRegression.SavePreview(outputDirectory)` 生成 `selection-ui.png`；
- 再执行真实窗口注入回归 `SelectionWindowRegression.Run(checks)`（可返回 `null` 表示当前会话无法注入鼠标）；
- 再做 UI 前置检查，最后做墨迹行为检查；
- 所有检查结果装进 `List<object> checks`，最后写 `Path.Combine(outputDirectory,"selftest.json")`，`WriteIndented=true`；
- 自检**不抛断言**；失败的布尔值进入 `allCriticalChecksPassed`；
- 自检结束会把自由笔画重新选中（`Tool=Select` + down/up），让截图显示缩放/旋转手柄与删除按钮。

### 13.2 前置 UI 检查

| 方法 | 内容 | 关键常量 |
|---|---|---|
| `RenderingRegression.Run` | 三组点集（单点；自交 X；重合点+轻微移动），用 `StrokeRenderer` 绘制到 128×128 目标，和 `StrokeGeometry.BuildUnionContours` + Winding 填充的参考图逐字节比较（允许 ±1）；再验证几何缓存：未变笔画复用、编辑后重建、删除后逐出、缓存上限 2048 | 128×128@96 DPI；±1；2048 |
| `EraserRegression.Run` | `kind∈{Mouse,Pen,Touch} × mode∈{Area,WholeStroke}`：控制器 `ViewTransform=Scale(2)`、`EraserRadius=10`；两条水平线 y=50/y=90；在 (200,100) 按下/抬起；区域擦期望 3 笔（原笔被切成两段+另一条），线条擦期望只剩另一条；undo/redo 计数与几何还原；第二组验证快速面积擦的最终 up、其他触点不干扰、整个手势一个撤销 | 半径 10；ViewTransform×2 |
| `SelectionRegression.Run` | `kind∈{Mouse,Pen,Touch} × zoom∈{0.5,1,2}`：矩形笔画 (100,100)-(200,160)；框选后应为 1 笔且无历史；CloneMode 下点击不产生克隆/历史；两次拖拽克隆出 2 份（共 3 笔、Id 唯一、原笔不动、选区仍为原笔）；undo/redo 数量；Shift 旋转拖拽吸附到 **30°**（旋转 23° 输入，`SelectionRotationDegrees≈30`）；旋转 undo 还原点、redo 恢复 30°；清空选择后重新点选仍保持角度；再用 `BoardService` + `BoardSelectionElement` 验证“墨迹+图片+视频”混合框选、克隆 2 份、单一撤销单元 | 9 组；矩形 (100,100)-(200,160)；旋转 23°→吸附 30° |
| `SelectionRegression.SavePreview` | 生成 1020×330 PNG：三列 `拖动框选 / 旋转与角度提示 / 克隆：保留原对象`，用 `InkSceneRenderer.Draw` 渲染 | 1020×330@96 DPI |
| `SelectionWindowRegression.Run` | 新建真实 `BoardWindow`（独立 `AppServices`，关闭自动保存），用 `SetCursorPos` + `mouse_event` 注入原生鼠标：拖框选、点“克隆模式”Button（UIA Invoke）、拖克隆、旋转拖拽、切回笔；校验 `InkSurface.IsHitTestVisible` 模式同步、选择/克隆/撤销/旋转/切笔清选区；返回 `null` 表示 `WindowFromPoint/GetAncestor` 不通过（无法注入） | 350 ms 初次等待；每次 drag 50/40/6×30/100 ms |
| `CheckFloatingBarLayout` | 对 `labels∈{false,true} × scale∈{50,100,200}`：写 `Appearance`、`RefreshSettingsVisuals`、`Reposition`、等 100 ms、`UpdateLayout`；递归检查每个可见 `TouchIconButton` 的变换后边界在 root `±1` 内；`CheckWindowRegion` 检查窗口区域 | 6 组；容差 1 DIP |
| `CheckToolbarPosition` | 连续 6 次 `ToggleClickThrough`，每次等 100 ms，比较 `WindowChrome.GetClientPosition(bar)` 与 `GetScreenBounds(bar, ChromeSurfaces.First())` 相对初始值**完全相等**；再开/关笔与荧光笔调色盘（150/100 ms），检查客户区原点与绘制原点都不变 | 6 次 |
| `CheckBackdropTransparency` | 在浮动栏第一张卡片位置盖一个红/蓝切换的置顶窗口，等 250 ms 后用 `GetPixel` 读屏幕采样点；红→蓝各读一次，要求两次都成功且 R/B 通道变化 >10；返回 `null` 表示当前桌面/会话无法读取（例如锁屏/远程） | 250 ms×2；通道差 >10 |
| `CheckPaletteLayout` | `theme∈{1,2} × highlighter∈{false,true} × scale∈{50,100,200}`；新建 `PenPaletteWindow`、`PlaceAboveBar`、等 200 ms；卡片边界在 root ±1；每个色块 Button 的内容 Border 在按钮内 ±0.5 且 `ActualWidth+Margin.Left+Margin.Right <= 44.5`；`CheckWindowRegion`；期望锚点用 `GetPopupPosition(anchor, paintedW,paintedH, ceil(8*dpi))`，位置误差 ≤1 px | 12 组；gap 8 DIP |
| `CheckPopupLayout` | `scale∈{50,100,200} × kind∈{more,gesture,shapes}`；`BarPanelWindow` 用 `ShowAboveBar(AppWindow.Position.X, Y, Size.Width)`；`ShapePickerWindow` 直接 Activate（不锚定）；等 200 ms；取 `(Border)((Viewbox)root).Child` 或 `ShapePreview` 内容 Border；边界在 root ±1 且窗口区域匹配 | 9 组 |
| `CheckTapInk` | `tool∈{Pen,Highlighter} × kind∈{Mouse,Pen,Touch}`；用 `Sample(9900,50,50,0,0.5,1,1,kind,0)` 按下；取 live outline；抬起 TimeMs=10；再重复一次抬起（模拟 capture-lost）；要求 `Document.Count==1 && 无 live && completed && outline 相同`；然后 undo/redo 计数 | 6 组 |
| `CheckUnfoldHandles` | 对两侧：若未收纳先 `ToggleFold`；等 100 ms；取 `(Button)bar.FoldHandles[side].Content`，用 `ButtonAutomationPeer` + `IInvokeProvider.Invoke()`；等 **1200 ms**（覆盖一次维护轮询）；要求已展开且 HWND 可见 | 2 组 |
| `CheckPopupOrder` | 新建一个 topmost 测试窗（TextBox），`PlaceDip(50,50,180,40)`；等 1200 ms；沿 `GetWindow(bar,GW_HWNDPREV)` 走，找到测试窗即通过 | 1 组 |

`CheckWindowRegion`（`SelfTest.cs:564-604`）：
- 取 `GetWindowRgn`（0 失败）；
- 对每个可见 surface：中心、左/右/上/下边中点都必须 `PtInRegion`；边坐标用 `ceil(dip*dpi - 0.5)` 对齐像素中心（右/下再减 1）；
- 再以 **2 px 步长**扫描整个窗口外框，凡不在任何卡片 `±1 DIP` 内的点都不得在 region 内；
- 用于证明“卡片间隙/最小尺寸 padding 不渲染也不命中”。

### 13.3 墨迹行为检查

1. **自由笔画提交几何不变**：
   - 设置笔色 `InkColor.Opaque(220,38,38)`；`BaseWidth=14`；`TaperKind=Tail`；`Tool=Pen`；
   - `penId=9001`；70 个点：`x=220+i*9`，`y=300+sin(i*0.16)*70`；
   - 逐点 `OnPointerDown/OnPointerMove`（TimeMs=i*8）；
   - 记录 live 的 `Widths`、`StrokeGeometry.BuildOutline(live)`；
   - `tipTaperedLive = live.Points[^1].Width < live.Points[10].Width`；
   - 抬起后取 committed，要求 widths/outline 序列完全一致；
   - 记录 `tipWidth`（末点宽）与 `settledWidth`（第 10 点宽）。
2. **荧光笔等宽**：`Tool=Highlighter`，色 `Opaque(234,179,8)`，`BaseWidth=34`；50 个点、步长 12、y=460；要求所有点 `|Width-34|<0.01`。
3. **预设图形**：
   - `Tool=Shape`，色 `Opaque(37,99,235)`，`BaseWidth=4`，`RecognizeShapes=false`；
   - `ShapeKind=Coordinate3D`：down(1150,620) → move(1420,400) → up；要求新增 3 笔（三条轴）；
   - `ShapeKind=Circle`：down(300,700) → move(520,900) → up；
   - 恢复 `RecognizeShapes = Settings.InkToShape.IsInkToShapeEnabled`。
4. **选区缩放与撤销**：
   - `Tool=Select`；`selectId=9004`；目标 `Document.Strokes[0]`；在 `path[30]` 按下/抬起；
   - `selected = Selection.Contains(target)`；
   - 手柄 = `beforeBounds.Right/Bottom`；down 手柄 → move +120,+90 → up；
   - `scaledUp = afterBounds.Width > beforeBounds.Width + 10`；
   - `Undo()`；`undoRestored = |width-before|<0.5 && |left-before|<0.5`。
5. **删除选区与撤销**：再点选 `path[30]`；`DeleteSelection()`；要求移除 1 笔；`Undo()` 后数量还原。
6. **回放单调**：`new InkReplaySession(Document.Strokes)`、`new ReplayClock(session.Duration)`、`Play()`；40 次 `Advance(duration/20)`；每次 `VisibleAt(ElapsedMs).Sum(s=>s.Count)` 不下降；要求最终 `clock.IsComplete`。
7. **边缘手势属性**：`EdgeGestureService.SetEdgeGesturesDisabled(overlay.Handle,true)` 返回 true；再设 false。
8. **GPU 回读**：
   - `fullFrameBytes = Window.Width*Window.Height*4`；
   - `frames=PresentedFrames`，`readback=GpuReadbackBytes`；
   - 输出 `naiveBytes`、`ratioWholeRun`、`drawingFrames`、`drawingReadbackBytesPerFrame`、`drawingRatioVsFullScreen`；
   - 判据只进入报告，不硬失败。

`Sample(id,p,timeMs) = new OverlayPointer(id, p.X, p.Y, 0.5f, 0, 0, OverlayPointerKind.Pen, timeMs)`。

### 13.4 `selftest.json` 字段（精确）

顶层：

| 字段 | 来源 |
|---|---|
| `presentedFrames` | `Presenter.PresentedFrames` |
| `latencySamples` | `Presenter.LatencySamples` |
| `lastInputToPresentMs` | 同上 |
| `maxInputToPresentMs` | 同上 |
| `averageInputToPresentMs` | 同上 |
| `barDiagnostics` | `ShellController.BarDiagnostics` 字符串 |
| `overlayTransparentCapable` | 常量 `true` |
| `strokeCount` | `OverlayInk.Document.Count` |
| `allCriticalChecksPassed` | 见下 |
| `desktopTransparencyCheckAvailable` | `CheckBackdropTransparency` 返回非 `null` |
| `desktopTransparencyVerified` | 返回 `true`（读屏可用且颜色确实透过） |
| `checks` | 对象数组，字段随检查类型变化（`check` 为描述字符串） |

`allCriticalChecksPassed` 的与条件：`renderingMatches && erasingWorks && selectionWorks && boardSelectionWorks != false && chromeFits && positionStable && translucent != false && palettesFit && panelsFit && tapsWork && unfoldingWorks && popupOrder && widthsIdentical && outlineIdentical && tipTaperedLive && selected && scaledUp && undoRestored && monotonic`。`boardSelectionWorks=null`（无法注入鼠标）不判失败，明确失败才判失败。
注意：**不包含** `highlighter.uniform`、`strokesAdded==3`、删除/撤销、回放 `completed`、GPU 比例、边缘手势等；这些只记录不参与总判定。桌面透明确认“不可用”（`null`）不判失败，只有明确读到颜色但不透（`false`）才失败。

典型 check 对象字段：
- `compact stroke geometry matches original raster`：`points,identical`
- `cache retains unchanged ink and evicts edited/deleted ink`：`reused,rebuilt,evicted`
- `native geometry cache has a fixed entry bound`：`bounded`
- `eraser preserves outside ink and supports undo/redo`：`kind,mode,erased,undo,redo`
- `fast area erase handles final up and ignores other contacts`：`otherContactIgnored,swept,undoWholeGesture`
- `marquee, repeated clone and snapped rotation`：`kind,zoom,marquee,noClickClone,clones,undo,redo,rotated,rotationUndo,rotationRedo,reselectKeepsAngle`
- `mixed ink/image/video clone is one undo step`：`selectedTogether,mixedClone,mixedUndo,mixedRedo`
- `mixed object gesture records one undo step`：`gestureMoved,gestureUndo,gestureRedo`
- `two-finger selection and tool switch finish cleanly`：`touchMove,touchUndo,switchFinishes`
- `undo of another object preserves current selection frame`：`otherFrameUnchanged,clearDropsFrame`
- `board XAML selection with native mouse input`：`available,modeSynced,marquee,cloneToggled,cloned,cloneUndo,rotated,penRestored,passed`
- `floating bar buttons fit client area`：`scale,labels,fits,regionFits,clientWidth,clientHeight`
- `mode switch keeps toolbar origin`：`mouseMode,unchanged,x,y`
- `popup activation keeps toolbar origin`：`highlighter,unchanged`
- `toolbar background transmits desktop colors while inactive`：`available,visible,red,blue,backdrop`
- `palette card and swatches fit without backdrop margins`：`theme,highlighter,scale,fits,swatchesFit,regionFits,anchored,paintedX,paintedY,expectedX,expectedY`
- `popup content fits without backdrop margins`：`kind,scale,fits,regionFits`
- `tap commits visible dot with undo/redo`：`tool,kind,committed,undo,redo`
- `edge button unfolds across maintenance poll`：`side,unfolded`
- `maintenance preserves candidate popup Z order`：`above`
- `committed geometry equals last live frame`：`widthsIdentical,outlineIdentical,taperPresentWhileDrawing,tipWidth,settledWidth`
- `highlighter is uniform width`：`uniform`
- `3D coordinate system committed as three axes`：`strokesAdded`
- `selection corner drag scales and undo restores`：`selected,scaledUp,undoRestored,widthBefore,widthAfter,widthUndone`
- `delete selection removes and undo restores`：`removed,restoredAfterUndo`
- `replay reveals strokes monotonically and completes`：`monotonic,completed,strokes`
- `edge gesture property applied`：`applied`
- `gpu readback stays well below full-screen-per-frame`：`fullFrameBytes,frames,readbackBytes,naiveBytes,ratioWholeRun,drawingFrames,drawingReadbackBytesPerFrame,drawingRatioVsFullScreen`

### 13.5 证据边界

- 仓库 `tests/` 只有 `InkCanvas.Core.Tests`，**没有任何测试引用 `InkCanvas.App`**；`SelfTest`、`EraserRegression`、`RenderingRegression`、`PerformanceCheck` 都是应用内运行时检查，不是单元测试。
- `docs/superpowers/evidence/README.md` 记录过历史 `runtime-selftest.json` 数值（如 `tipWidth=2.8/settledWidth=14`、连续书写每帧回读 205,778 B ≈ 2.48% 整屏），但**本轮未重新运行**。
- `--selftest` 还会生成 `selection-ui.png`（1020×330，三列示意）；它不是断言，只是视觉证据。
- `SelfTest` 仍是 `public static`，但 3.4.0 工作树把三项回归拆到同命名空间的 `internal static` 类：
  - `RenderingRegression.Run(List<object>)`：Win2D 128×128 栅格/几何缓存；
  - `EraserRegression.Run(List<object>)`：区域擦/线条擦/多触点/撤销；
  - `PerformanceCheck.Run(ShellController,string)`：`--perfcheck`，写 `performance.json`。
- `--shot`/`--diag`/`--selftest`/`--perfcheck` 都依赖真实 WinUI 窗口与 Win32；HarmonyOS 上不能原样复用，需要改用 `hdc shell uitest`/截图工具或应用内自渲染。

### 13.6 `--perfcheck` 性能检查 `PerformanceCheck`

入口：`--perfcheck=<dir>`，2 s 后运行；输出 `performance.json`，内容为 `samples` 数组。

采样阶段：

1. `idle`；
2. `render 240 strokes x 100 points`：240 条 × 100 点，点坐标 `x=20+j*5`、`y=20+(i%32)*20+sin(j*0.2)*12`；记录 `elapsedMs` 与 `GC.GetAllocatedBytesForCurrentThread` 差值；
3. `document rendered`；
4. `append 8 strokes`：克隆前 8 条追加，记录耗时/分配；
5. `document cleared`（先 `Document.Clear()` + `History.Clear()`，等 100 ms）；
6. `160 live pointer updates`：从 (10,10) 每次 `x+7,y+4`，TimeMs=i*8，最后抬到 (1130,650)，记录耗时/分配；
7. `large live stroke finished`；
8. 6 次画板开/关循环（每次 `ToggleBoard` + 120 ms，再 `ToggleBoard` + 120 ms）；
9. `after diagnostic GC`（`GC.Collect` + `WaitForPendingFinalizers` + `GC.Collect` + 200 ms）；
10. `resource checks`：`emptyBuffersReleased`（`Presenter.ManagedBufferBytes==0 && RenderTargetBytes==0 && Scene.Strokes.CachedGeometryCount==0`）与 `!LiveStrokes.Any()`。

每个 phase 采样的字段（`Sample(phase)`）：`phase, workingSetBytes, privateBytes, managedBytes, handles, cpuMs, overlayManagedBufferBytes, overlayRenderTargetBytes, cachedGeometries, geometryBuilds`。

移植时：这是 Windows 进程/GC/Win2D 指标，HarmonyOS 只能保留“负载生成 + 时间/内存”思路，具体字段需替换为 ArkTS/ohos 性能 API。

---

## 14. 文件/JSON 格式汇总（App 直接读写的部分）

### 14.1 `Settings.json`

- 位置：`%APPDATA%\InkCanvas\Settings.json`；`SettingsStore` 缺失/损坏时返回 `new AppSettings()`；保存是 `path + ".tmp"` 再 `File.Move` 原子替换；所有异常吞掉（磁盘满/锁定继续用内存值）。
- 分区键：`advanced / appearance / automation / behavior / canvas / gesture / inkToShape / startup / randSettings`。
- 大小写不敏感读取（`PropertyNameCaseInsensitive=true`），枚举按**数字**序列化（未配置 `JsonStringEnumConverter`）。
- App 直接改写的键（节选，均在 §11.3 表中）：`canvas.inkWidth/inkAlpha/inkStyle/eraserSize/usingWhiteboard/hideStrokeWhenSelecting/...`、`appearance.theme/enableViewboxFloatingBarScaleTransform/enableViewboxBlackBoardScaleTransform/floatingBarBottomMargin/...`、`gesture.*`、`behavior.*`、`startup.*`、`automation.*`、`advanced.*`、`randSettings.*`。
- 默认值需与 Core 一致，App 侧特别依赖的：`InkWidth=2.5`、`InkAlpha=140`、`EraserSize=2`、`UsingWhiteboard=false`、`HideStrokeWhenSelecting=true`、`Theme=0`、`FloatingBarScale=100`、`BlackboardScale=100`、`FloatingBarBottomMargin=100`、`MinimumAutomationStrokeNumber=0`、`AutoSavedStrokesLocation="D:\\Ink Canvas"`、`Rand.PeopleCount=60`、`Rand.IsNotRepeatName=false`。

### 14.2 `.icink` 纯墨迹

`InkFileStore`（Features）格式：

```json
{
  "Version": 1,
  "Strokes": [
    { "Color": 4278190080, "Width": 2.5, "Highlighter": false, "HighlighterAlpha": 96,
      "Taper": 0, "Shape": false,
      "Points": [[x,y,pressure,width,timeMs]] }
  ]
}
```

- 扩展 `.icink`；`Color` 是 packed ARGB（uint）；`Points` 每行 4 或 5 个 float（第 5 个缺省 0）。
- App 的 `SaveStrokes/OpenStrokes`、画板 `SavePage`（无图片时）、PPT 截图 `SaveInkAlongside`、`AutoSaveService` 都走此格式。
- 自动保存文件名：`{HHmmss}-{reason}.icink`；可选 `yyyy-MM-dd` 子目录。

### 14.3 Board 包

见 §7.11；与 `.icink` 共用扩展名，但根对象有 `Format: "inkcanvas-board"` 用于区分。**Board 包笔画缺 `HighlighterAlpha`** 是已知格式差异。

### 14.4 抽奖名单

- `%APPDATA%\InkCanvas\Names.txt`：每行一个名字；空行被跳过（窗口层先判 `Length>0`，服务层再 `Trim`）。
- `%APPDATA%\InkCanvas\Replace.txt`：每行 `旧名-->新名`；**整行精确匹配**，按文件顺序应用；没有 `-->` 的行忽略。
- `NamesInputWindow` 保存时写 `Names.txt`，不写 `Replace.txt`。

### 14.5 `selftest.json`

见 §13.4；只有 `--selftest` 生成。

### 14.6 错误日志

- `%APPDATA%\InkCanvas\error.log`，追加 `{DateTime.Now:O} {exception}\n\n`。
- `FileLog` 主日志由 Core 写 `Log.txt`（App 只控制开关与启动/退出行）。

### 14.7 `performance.json`

`--perfcheck` 输出，字段见 §13.6：`phase, workingSetBytes, privateBytes, managedBytes, handles, cpuMs, overlayManagedBufferBytes, overlayRenderTargetBytes, cachedGeometries, geometryBuilds`；另有渲染/追加/实时笔画阶段的 `elapsedMs/allocatedBytes`，以及 `resource checks` 的 `emptyBuffersReleased/hiddenSurfaceHasNoLiveInk`。

### 14.8 `selection-ui.png`

`--selftest` 输出，1020×330@96 DPI；三列：`拖动框选`、`旋转与角度提示`、`克隆：保留原对象`。仅视觉证据，不参与 `allCriticalChecksPassed`。

---

## 15. 平台依赖清单（App 侧）

### 15.1 Win32 / COM / 系统

| 能力 | 具体 API | 使用位置 | 移植分类 |
|---|---|---|---|
| 窗口句柄/客户区/屏幕坐标 | `WindowNative.GetWindowHandle`、`GetWindowRect`、`ClientToScreen`、`GetCursorPos` | `WindowChrome`、`SelfTest` | platform-bound |
| 窗口置顶/Z 序 | `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`、`GetWindow(GW_HWNDPREV=3)`、`IsWindowVisible` | `WindowChrome.EnsureAbove`、`ShellController` | platform-bound |
| 窗口区域裁剪 | `CreateRectRgn`（逐扫描线矩形）、`CombineRgn(RGN_OR=2)`、`SetWindowRgn`、`GetWindowRgn`、`PtInRegion` | `WindowChrome.ClipToSurfaces`、`SelfTest` | platform-bound |
| 屏幕像素读取（透明度回归） | `GetDC(0)`、`GetPixel`、`ReleaseDC` | `SelfTest.CheckBackdropTransparency` | platform-bound |
| DPI | `GetDpiForWindow`、`XamlRoot.RasterizationScale` | `WindowChrome`、全部窗口、`SelfTest` | platform-bound（HarmonyOS `vp` 自动换算，逻辑应删除） |
| 触摸反馈 | `SetWindowFeedbackSetting`（feedback 1,3..11） | `WindowChrome.SuppressTouchFeedback` | platform-bound |
| 窗口子类化/最小尺寸/最大化区域 | `SetWindowLongPtrW(GWLP_WNDPROC=-4)`、`CallWindowProcW`、`WM_GETMINMAXINFO=0x24`、`MonitorFromWindow(2)`、`GetMonitorInfoW` | `MinSizeHook` | platform-bound |
| DWM 圆角 | `DwmSetWindowAttribute` 33/2/34 | `WindowChrome.SetRoundedCorners` | platform-bound |
| 显隐窗口不激活 | `ShowWindow(SW_SHOWNOACTIVATE=4/SW_HIDE=0)`、`AddExStyle(WS_EX_NOACTIVATE=0x08000000, WS_EX_TOOLWINDOW=0x00000080)` | `PptNavigationWindow` | platform-bound |
| 桌面截屏 | `GetDC(0)`、`CreateCompatibleDC/Bitmap`、`BitBlt(SRCCOPY=0x00CC0020)`、`GetDIBits` | `ScreenshotService`（Features）经 `ShellController` | platform-bound |
| 前台进程名 | `GetForegroundWindow`、进程枚举 | `ForegroundWindowService` 经 `ApplyAutoFold`/弹窗轮询 | platform-bound |
| 全局热键 | `RegisterHotKey` + 消息循环 | `HotkeyService` 经 `OverlayHost` | platform-bound |
| 边缘手势抑制 | `SetWindowFeedbackSetting`/系统属性 | `EdgeGestureService` 经 `ShellController` | platform-bound |
| 开机自启 | 注册表 Run | `AutoStartService` 经 `SettingsWindow` | drop-candidate |
| 进程重启 | `Process.Start(exe,"-m")` | `SettingsWindow.RestartApp` | platform-bound |
| 外部程序启动 | `SoftwareLauncher.LaunchEasiCamera/LaunchDesmos` | `BoardWindow` | platform-bound（HarmonyOS 可改 want/URL） |
| 全屏 presenter | `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` | `BoardWindow.SetFullscreen` | adapt |
| 最大化/还原/最小化 | `OverlappedPresenter.Maximize/Restore/Minimize`、`AppWindow.Changed` | 全部有标题栏窗口 | adapt |
| 显示器工作区 | `DisplayArea.GetFromWindowId/GetFromRect` | `WindowChrome`、`FloatingBarWindow`、`Countdown/Lottery` | adapt |
| 文件选择器 | `Windows.Storage.Pickers.FileOpenPicker/FileSavePicker` + `InitializeWithWindow` | `ShellController`、`BoardWindow` | platform-bound |
| 文件 IO | `System.IO.File/Directory`、`Path` | 全部 | adapt |
| 媒体 | `Windows.Media.Playback.MediaPlayer`、`MediaPlayerElement`、`MediaSource`、`Windows.Media.Core` | `BoardMediaLayer`、`CountdownWindow` | platform-bound |
| 2D 渲染 | Win2D `CanvasControl/CanvasDevice/CanvasRenderTarget/CanvasBitmap/CanvasDrawingSession` | `BoardWindow._paper`、`SaveSelectionAsImage`、`ScreenshotService` | platform-bound |
| 光栅截图 | `RenderTargetBitmap.RenderAsync/GetPixelsAsync` | `--shot`、`SaveSelectionAsImage` 的视频帧 | platform-bound |
| 图像编解码 | `BitmapEncoder`、`BitmapDecoder`、`SoftwareBitmap`、`DataReader/DataWriter`、`InMemoryRandomAccessStream` | `--shot`、`BoardWindow.LoadCanvasBitmapAsync`、`BoardImageLayer` | adapt |
| 亚克力 | `DesktopAcrylicController`、`SystemBackdrop`、`SystemBackdropConfiguration` | `ChromeBackdrop` | platform-bound |
| UI 自动化 | `ButtonAutomationPeer`、`IInvokeProvider` | `SelfTest.CheckUnfoldHandles` | platform-bound |
| 输入注入/真实指针 | `InkController.OnPointerDown/Move/Up`（不经 XAML） | `SelfTest` | pure-logic（数据） |
| 应用退出 | `Application.Current.Exit()` | `ShellController.Exit`、`SettingsWindow.ExitApp` | adapt |

### 15.2 WinUI 3 控件/框架

- `Window`、`Application`、`DispatcherQueue`/`DispatcherQueueTimer`、`XamlRoot`、`ElementCompositionPreview`。
- `Grid/StackPanel/Viewbox/ScrollViewer/Border/TextBlock/FontIcon/Button/ToggleSwitch/Slider/ComboBox/TextBox/HyperlinkButton/ContentDialog/Flyout/MenuFlyout/RadioMenuFlyoutItem`。
- `MediaPlayerElement`、`MediaTransportControls`。
- `SystemBackdrop`、`DesktopAcrylicController`、`Composition`（`Motion`/`SelfTest`/`SettingsWindow`）。
- `RenderTargetBitmap`、`BitmapEncoder`、`BitmapDecoder`。

### 15.3 App 依赖的 Core/Features 类型（跨层契约）

- `InkController`：`CurrentAttributes`、`Tool`、`ShapeKind`、`EraserMode`、`EraserRadius`、`RecognizeShapes`、`HyperbolaAsymptoteOption`、`MultiTouchEnabled`、`ConfigureGestures`、`Palm`、`GesturesTargetSelection`、`TransformCenterOption`、`ViewTransform`、`ViewCenter`、`Document`、`Selection`、`SelectedElements`、`GetSelectableElements`、`HasSelection`、`SelectionBounds`、`IsMarqueeSelecting`、`IsRotatingSelection`、`History`、`LiveStrokes`、`OnPointerDown/Move/Up`、`Undo/Redo/Clear/DeleteSelection`、`TransformSelectionUndoable`、`AskRemoveAsymptotes`、`SlideFlipRequested`、`ToolChanged`、`SelectionChanged`、`Invalidated`、`ToWorld`、`CloneMode`、`StickyShape`、`Dispose`。
- `BoardSelectionElement`（Features/Board）：`Image/Media`、`Bounds`、`Capture/Restore`、`Transform`、`Duplicate`、`SetPresent`；由 `BoardWindow.GetSelectionElements` 创建并交给 `InkController.GetSelectableElements`。
- `BoardService`：见 §7.11/7.4；`MaximumPages=99`、`GridSpacing=40`、`Background`、`Current/Pages/PageCount/CurrentIndex/HasContent/CurrentImages/CurrentMedia`、`PageChanged/BackgroundChanged/ImagesChanged`、`SetBackground/NextPage/PreviousPage/InsertPageAfterCurrent/RemoveCurrentPage/AddImage/RemoveImage/AddMedia/RemoveMedia/RecordImageChange/RecordMediaChange/SavePage/SaveBoard/LoadPage/LoadBoard`。
- `PptInkSession`：`Poll/Snapshot/RecommendedPollInterval/Next/Previous/ExitSlideShow/GoToSlide/RestoreHiddenSlides/DisableSlideTimings/ShowSlideNavigationPane/RequestScreenshot`；`PptSnapshot` 字段 `IsSlideShowRunning/ShowPosition/Slide.Index/Presentation.SlideCount`；`PptSnapshot.Detached`。
- `AutoSaveService`；`LotteryService`；`SettingsStore`；`InkFileStore`；`ScreenshotService`；`UpdateService`/`UpdatePolicy`/`AppVersion`。

---

## 16. 测试与验证现状

- `tests/InkCanvas.Core.Tests/DomainTests.cs` 覆盖：形状识别、几何、撤销/重做、橡皮、变换、`Settings.json` 往返与遗留 WPF JSON 读取；**没有任何 App 层测试**。
- App 的行为验证只能通过运行时：
  - `--selftest`（最全面，含 `RenderingRegression`、`EraserRegression`、`SelectionRegression`、`SelectionWindowRegression`、窗口/区域/DPI/透明度断言，依赖真实窗口与输入注入；另产出 `selection-ui.png`）；
  - `--perfcheck`（240×100 点渲染、追加、实时笔画、画板开关循环的内存/句柄/缓存采样）；
  - `--diag`（覆盖层计数/工具状态）；
  - `--shot`（各窗 PNG）；
  - `--open`（逐窗打开做人工目视）。
- 本轮未构建、未启动、未运行任何上述钩子；所有“行为”描述来自源码阅读。
- 历史证据（`docs/superpowers/evidence/README.md`）显示过往版本曾在 Windows Server 2025 / 125% DPI 下跑通 `--selftest` 并产出 `runtime-selftest.json`；但该证据不代表 3.4.0 工作树当前状态。
- 未验证的硬件/外部系统：Win11 亚克力真半透、Office/WPS COM、触摸屏/压感笔、视频编解码、真实投影分辨率下的窗口夹取。

---

## 17. 逐功能移植分类

> 说明：`pure-logic` = 可 1:1 抽成 ArkTS 纯函数/类；`adapt` = 逻辑保留但 UI/窗口/存储需替换；`platform-bound` = 强绑 Windows，HarmonyOS 无等价能力或需完全重写；`drop-candidate` = 建议放弃并说明。

| 功能 | 分类 | 依据/移植建议 |
|---|---|---|
| `app-boot-composition`（AppServices 装配/初始主题） | adapt | 组合根保留；`%APPDATA%`→`filesDir`；WinUI Application→UIAbility |
| `app-commandline-harness`（--board/--passthrough/--diag/--open/--shot/--selftest） | platform-bound | HarmonyOS 无命令行启动参数/全局桌面；改为 hdc/uitest 或调试菜单 |
| `app-error-logging` | adapt | `FileLog` 可复用；`error.log` 路径改为沙箱 |
| `app-settings-application`（ApplySettings 公式） | pure-logic | 所有 clamp/映射公式原样迁移；控制器接口按 ArkTS 重写 |
| `overlay-host`（全屏透明置顶输入窗） | platform-bound | 三方应用无跨应用覆盖层；需改为应用内画布/系统级 API（未核实） |
| `global-hotkeys`（Alt/Ctrl 组合） | platform-bound | HarmonyOS 仅前台按键；全局热键无 API（未核实） |
| `shell-orchestration`（窗口单例/打开器） | adapt | 单 UIAbility + 页面/子窗管理；删除 Topmost 语义 |
| `shell-mode-state-machine`（标注/鼠标/画板/PPT/收纳） | adapt | 状态与转换保留；穿透/收纳改为应用内浮层与可见性 |
| `shell-auto-fold`（前台进程名匹配） | adapt | 判定逻辑可保留；HarmonyOS 无“前台其他应用进程名”可见性，建议 drop 或改为应用内事件 |
| `shell-ppt-integration`（COM 轮询/通知/截图） | adapt | 状态机/通知文案/导航几何可保留；COM/进程/截屏需替换或 drop |
| `shell-settings-reapply` | adapt | 逻辑保留；`Reposition` recenter 行为可选 |
| `shell-ink-file-actions`（.icink 保存/打开） | adapt | 文件选择器/路径替换；JSON 格式保持 |
| `shell-screenshot-autosave` | platform-bound | HarmonyOS 无法静默截全屏/其他应用；改为 `photoAccessHelper` 或应用内截图 |
| `shell-exit-lifecycle` | adapt | 保存/prune/Dispose 保留；`Application.Exit`→`UIAbilityContext.terminateSelf` |
| `update-coordinator`（检查/下载/安装） | adapt | 网络/版本比较/`UpdatePolicy` 可保留；安装器/`Process.Start` platform-bound，HarmonyOS 应走应用市场/自定义下载 |
| `window-chrome`（无边框/DPI/裁剪/Z 序/标题栏） | platform-bound | Win32 专有；HarmonyOS 用 `window` 模块 + 自定义标题栏，裁剪/Z 序语义不存在 |
| `chrome-backdrop`（DesktopAcrylic） | platform-bound | HarmonyOS 用 `backgroundBlurStyle`/半透明，无 1:1 |
| `min-size-hook`（WM_GETMINMAXINFO 子类化） | platform-bound | HarmonyOS `window.setWindowLimits` 等价（需按 API 版本确认） |
| `floating-bar-window` | adapt | 布局/尺寸/收纳几何可保留；多窗口/置顶/裁剪重写 |
| `bar-panel-window` | adapt | 弹出面板内容/尺寸可保留；独立置顶窗改为应用内浮层 |
| `folded-bar-handle` | adapt | 30×50、露出 14、图标/颜色可保留；窗口位置改为应用内边缘吸附 |
| `board-window`（三浮条/页面/全屏） | adapt | 布局/配色/工具/页面模型可 1:1；Win2D/窗口全屏替换 |
| `board-strips-navigation` | adapt | 单元格宽度/图标/页码/启用规则原样 |
| `board-tool-flyouts` | adapt | Flyout 语义用 ArkUI 弹层；长按 550 ms 可保留 |
| `board-canvas-background`（Grid/Lined 绘制） | adapt | 网格间距/线色/线宽原样；Canvas 绘制替换 Win2D |
| `board-selection-panel`（克隆/旋转/翻转/粗细/颜色） | adapt | 矩阵/撤销/按钮集原样；面板定位用组件坐标 |
| `board-element-gestures`（图片/视频双指矩阵） | adapt | 纯矩阵逻辑 pure-logic；Manipulator 用 ArkUI 手势重写 |
| `board-image-layer` | adapt | 选中/拖动/缩放/20 DIP 手柄可保留；Bitmap 解码替换 |
| `board-media-layer` | platform-bound | `MediaPlayerElement`/TransportControls 无等价；`Video` 组件可部分替代，先降级为“静态占位+播放” |
| `board-file-pickers` | platform-bound | FilePicker/StorageFile 替换为 `picker`/`fs`；32 MiB/512 MiB 限制保留 |
| `countdown-window` | adapt | 全部计时公式/编辑规则/布局可 1:1；音频/Win2D 环替换 |
| `countdown-min-size-hook` | platform-bound | `setWindowLimits` 替代 |
| `lottery-window` | adapt | 抽奖算法/分组/名单解析 pure-logic；窗口布局可保留 |
| `lottery-names-input` | adapt | 文本编辑/保存提示可保留；文件路径替换 |
| `replay-window` | adapt | ReplayClock/Session 纯逻辑；Slider/ComboBox 替换 |
| `settings-window` | adapt | 440 DIP/分区/35 个 Deferred 原样；控件替换；JSON 键保持 |
| `ppt-navigation-window` | adapt | 40/42 几何/文案/0.4 透明度可保留；无激活/置顶窗口需应用内浮层 |
| `info-window`（指南/日志） | adapt | 700×600/12 行键位/Markdown 渲染保留；文件路径替换 |
| `yes-no-window` | adapt | 450×160/按钮宽度/关闭语义保留；ContentDialog/自定义对话框 |
| `pen-palette-window` | adapt | 248/208 设计尺寸/关闭规则保留；独立窗改浮层 |
| `shape-picker-window` | adapt | 208×234/254×286、图钉持久、关闭规则保留 |
| `selftest-pipeline` | platform-bound | 依赖真实窗口/区域/输入/桌面像素；HarmonyOS 改为应用内自动化测试 + uitest |
| `diagnostics-rendering-regression`（`RenderingRegression`） | platform-bound | Win2D 128×128 像素比较与几何缓存；ArkTS 需用 Drawing/Canvas 重建等价像素测试 |
| `diagnostics-eraser-regression`（`EraserRegression`） | pure-logic | 只走 `InkController` 指针路径与文档/历史，可在共享逻辑测试中 1:1 复现（几何断言需随实现调整） |
| `diagnostics-selection-regression`（`SelectionRegression`） | adapt | 框选/克隆/旋转/混合元素逻辑可共享；`InkSceneRenderer` 预览 PNG 是 Win2D，需替换 |
| `diagnostics-selection-window-regression`（`SelectionWindowRegression`） | platform-bound | 依赖真实 BoardWindow + Win32 `mouse_event`/`WindowFromPoint`/`SetCursorPos`；HarmonyOS 改用 uitest 触摸注入 |
| `diagnostics-performance-check`（`PerformanceCheck`） | platform-bound | 240×100 点负载、进程/GC/句柄/Win2D 缓存指标；HarmonyOS 只保留负载生成思路 |
| `diagnostics-backdrop-transparency`（`CheckBackdropTransparency`） | platform-bound | 依赖 `GetPixel` 读桌面，证明 Win32 半透；HarmonyOS 用窗口背景/模糊层测试替代 |
| `auto-start`（开机运行） | drop-candidate | HarmonyOS 三方应用无注册表 Run；说明：删除或改为“上次会话恢复” |
| `restart-process`（重启软件） | drop-candidate | `Process.Start(exe)` 不可用；用 `terminateSelf` + 用户手动启动 |
| `edge-gesture-block`（阻止 Win10/11 边缘手势） | drop-candidate | Windows 专有；HarmonyOS 无对应系统手势 |
| `global-hotkey`（系统级） | drop-candidate | 无系统级热键 API；建议保留应用内快捷键 |
| `desktop-screenshot`（Alt+C/PPT 自动截屏） | drop-candidate | 沙箱不可静默截其他应用；只能截应用自身或走系统授权 |
| `background-process-kill`（自动查杀希沃等） | drop-candidate | HarmonyOS 无跨进程查杀；设置项仍保留为 Deferred |
| `EasiCamera/Desmos` 外链 | adapt | 改为 `want`/URL Scheme；找不到应用时提示 |

---

## 18. 移植风险与开放问题

### 18.1 已确认的代码行为风险（不是“未验证”，是源码事实）

1. **自动收纳会强制隐藏墨迹**：`ApplyAutoFold` 末尾 `Overlay.ClickThrough = true` 经 setter 把 `InkVisible=false`；每次轮询都重置。若目标是“收纳后仍显示墨迹”，需改 setter 或改为 `SetInteractionMode(true, true)`。
2. **`IsEnableAutoFold` 主开关包含 App 不处理的分支**：只开 `EasiNote3C/HiteCamera/WxBoardMain/OldZyBoard` 时主开关 true 但永不自动收纳。
3. **PPT 支持关闭时 `_lastSlideShowState` 不会复位**：`Poll` 在 `PowerPointSupport=false` 时只 `UpdatePptNavigation(Detached)`，不更新 `_lastSlideShowState`；若之前为 true，热键/自动收纳仍按放映处理。
4. **`AppServices.Reload()` 无调用方**：设置窗口走 `ApplySettings + SaveSettings`，不会重新从磁盘读（外部编辑 Settings.json 后不生效，直到重启）。
5. **`PptInkFileStore` 路径在 Shell 构造时固定**：之后修改 `AutoSavedStrokesLocation` 不影响 PPT 墨迹存档根目录。
6. **`ScreenshotAsync` 自动保存的是 overlay 墨迹**（`Services.OverlayInk`），即使画板打开；画板页有自己的 `AutoSaveCurrentPage`。
7. **画板 `SavePage` 只检查图片**：只有视频的页会写成纯笔画 `.icink`，视频丢失。
8. **Board 包丢失 `HighlighterAlpha`**：与 `.icink` 不一致，跨格式往返会变 96。
9. **`OpenShapePicker` 每次新建窗口**：若旧窗未关闭，会被强制 Close，可能丢失图钉状态（`_lastAutoHide` 静态仍保留）。
10. **`OpenPenPalette` 的 `LightColorScheme` 用 `Appearance.Theme==1`**，而画板模式的有效主题是 `UsingWhiteboard`；黑白板切换后调色盘可能配色不符。
11. **`OpenSettings` 的 `EnsureAbove` 不带 overlay 参数**：设置窗口不会被“overlay 压住时”修复，但因为 overlay 用 `InputBegan` 抬升 chrome，通常仍可见；在画板模式下 overlay 隐藏。
12. **`--shot` 只捕获 `OpenWindows`/之后 WindowOpened 的窗口**：PPT 导航、Yes/No、名单导入、单次抽奖不会自动截图。
13. **`BarPanelWindow` 的共享行在关闭时从父 Panel 移除**：如果 shell 未及时重建，下一次打开需要 `CreateMorePanelContent()` 重新挂载；这是刻意设计，不要改成缓存控件树。
14. **`PptNavigationWindow` 计数器隐藏时窗口尺寸变小**：`122→82` / `120→82`，布局时必须按 `counter.VISIBLE` 重算。
15. **图片/视频层 `SetSelection` 只赋值不清空/点亮**：`BoardImageLayer/BoardMediaLayer.SetSelection` 当前只 `_selected = assets.FirstOrDefault()` 并对所有 view `SetSelectedVisual(false)`，没有再给选中的 view 设 true；控制器的 `SelectedElements` 是真实选择源，但图层选中框可能不显示。移植时应以控制器选择为唯一真源并补上视觉同步。
16. **元素双指/拖动已统一到控制器**：最新工作树删除了 BoardWindow 的旧 `BeginElementGesture/ApplyElementGestureDelta/EndElementGesture`、`_selectTouches`、`_elementManipulator` 与元素层的指针处理；移植时以 `InkController.SelectedElements + GetSelectableElements + TransformSelectionUndoable` 为准，避免从旧版本文档/历史代码里把双套实现搬回来。
17. **`BoardWindow.BindPage` 现在会清旧控制器的 `GetSelectableElements=null` 并退订 `ToolChanged`**；切页/关闭时元素选择代理不会泄漏到旧页。

### 18.2 需要决策的开放问题

1. **窗口模型**：HarmonyOS 上是否允许“一个 UIAbility + 多个子窗/浮层”，还是必须合成一个窗口？App 层 12 个顶层窗口 + 4 个 PPT 导航 + 若干临时弹窗，若逐个 `window.createWindow`，置顶/不抢焦点/跨应用覆盖都难以复现。
2. **跨应用覆盖层**：屏幕画笔的本质是覆盖其他应用（PPT/希沃白板）。HarmonyOS 三方应用是否具备系统级悬浮窗/无障碍注入权限？若没有，整个“屏幕画笔”模式需要重新定义（可能只能在自有应用内批注）。
3. **全局热键**：Alt+Q/B/D/C/V/S/E/L、Ctrl+Z/Y/E、PageUp/Down、Shift+Esc 在 HarmonyOS 无系统级注册能力；是否降级为应用内快捷键？还是放弃？
4. **截屏/PPT 自动化**：`ScreenshotService` + PowerPoint COM + `PptInkSession` 在 HarmonyOS 上没有对应生态；是否保留“PPT 模式”UI 与设置项，仅作为未接线 Deferred？
5. **文件路径/沙箱**：`AutoSavedStrokesLocation` 默认 `D:\Ink Canvas`、`%APPDATA%\InkCanvas`、`MyPictures\InkCanvas` 在 HarmonyOS 必须映射到 `filesDir`/`cacheDir`/相册；`Settings.json` 的旧值读到后如何处理（改写默认还是保留只读）？
6. **`Names.txt/Replace.txt` 位置**：抽奖名单在 `%APPDATA%\InkCanvas`；HarmonyOS 上是否改为应用沙箱 + 文件选择器导入？跨设备迁移如何同步？
7. **视频播放**：`MediaPlayerElement` 的 transport controls、`MediaSource.CreateFromStream`、512 MiB 上限在 HarmonyOS `Video`/AVPlayer 上的等效行为未验证；是否先只支持本地文件 URI？
8. **亚克力/半透明**：WinUI 的 DesktopAcrylic + `SetWindowRgn` 卡片裁剪在 HarmonyOS 上如何表达？是否统一改成不透明圆角卡片 + 阴影？
9. **DPI/多显示器**：WinUI `Primary/WorkArea/DisplayArea` 的“主屏工作区”语义在 HarmonyOS（平板/PC/手机）不同；窗口夹取、浮动栏底部定位、PPT 导航四角定位需要重新定义基准。
10. **`parity/` 与 `harmony/` 目录当前不存在**：CLAUDE.md 描述的 `parity/manifest.json`、`harmony/` HAR 工程在工作树中尚未创建；本报告无法给出 App 文件到 ArkTS 文件的最终映射，只能给出功能清单。下一轮应先落 `parity/manifest.json` 骨架。
11. **自检替代**：`--selftest` 的窗口区域/DPI 断言在 HarmonyOS 无意义；需要设计新的黄金测试（纯逻辑：设置映射、抽奖、计时、回放、选区矩阵）与 UI 截图测试（uitest）。
12. **退出确认**：`Advanced.IsSecondConfimeWhenShutdownApp` 在 Windows 版是 Deferred；HarmonyOS 是否要实现？若实现，需要决定 `Exit()` 的调用链（`ShellController.Exit` 目前无确认）。

---

## 19. 附：App 层常量速查

| 常量 | 值 | 位置 |
|---|---:|---|
| 轮询间隔（空闲） | 900 ms | `ShellController` |
| 轮询间隔（放映） | 200 ms（`PptInkSession.RecommendedPollInterval`） | Features |
| PPT 通知/截图轮询上限 | 200 ms | Features |
| 浮动栏用户缩放 | 50..200（UI 滑块 50..150） | `FloatingBarWindow.UserScale` |
| 浮动栏底部边距默认 | 100 DIP | Settings |
| 收纳把手 | 30×50 DIP，露出 14 DIP | `FloatingBarWindow.SetFolded` |
| 弹窗锚点间距 | 4×S DIP（面板）/ 8 DIP（调色盘/图形） | `BarPanelWindow`/`PickerWindows` |
| 长按阈值 | 550 ms | `BoardWindow.AttachClickAndHold` |
| 拖动阈值 | 8 DIP / 16 ms | `WindowChrome.EnableDragMove` |
| 点击容差（浮动栏 tap 路由） | 10 DIP | `FloatingBarWindow.AttachTapRouting` |
| 画板最大页数 | 99 | `BoardService.MaximumPages` |
| 画板网格默认/夹取 | 40 DIP / 12..160 | `BoardService`/`BoardWindow` |
| 网格线颜色 | ARGB(55,92,113,121)，1 px | `BoardWindow.OnDrawPaper` |
| 橡皮半径 | 45×{0.5,0.8,1,1.25,1.8}；墨迹擦 2.5 | `BoardWindow`/`AppServices` |
| 图片插入上限 | 32 MiB；超 1920×1080 可压缩 | `BoardWindow.InsertImageAsync` |
| 视频插入上限 | 512 MiB；基准 256×256 | `BoardWindow.InsertMediaAsync` |
| 选区存图 | bounds+10，clamp 1..8192，96 DPI | `BoardWindow.SaveSelectionAsImageAsync` |
| 计时器默认/最小 | 1100×700 DIP / 300×200 DIP；默认 00:01:00 | `CountdownWindow` |
| 计时器 tick | 20/30/50/100 ms（≤10/≤60/≤120/>120 s） | `CountdownWindow` |
| 计时器紧凑 | 400×250 DIP | `CountdownWindow` |
| 抽奖窗口 | 900×500 DIP；结果 70 pt；按钮 200×70 | `LotteryWindow` |
| 抽奖闪烁 | 5×150 ms；自动关闭 1500 ms；自动开抽 100 ms | `LotteryWindow` |
| 回放窗口 | 900×680 DIP；timer 33 ms；12 ms/点 | `ReplayWindow`/`InkReplaySession` |
| 设置面板 | 440 DIP 宽，上下 150 inset，高 = 工作区−300（≥240） | `SettingsWindow` |
| 设置静默时间档 | 72 档，20 分钟 | `SettingsWindow` |
| PPT 导航缩放 | S=40/42；Opacity 0.4 | `PptNavigationWindow` |
| 指南窗口 | 700×600，最小宽 500；日志 600×390 | `InfoWindow` |
| 是否确认窗 | 450×160；按钮 100 宽；行高 35 | `YesOrNoWindow` |
| 名单导入窗 | 400×500 DIP | `LotteryWindow.NamesInputWindow` |
| 托盘/标题栏 | header 32 DIP，按钮 46×32 | `WindowChrome.AddTitleBar` |
| 窗口关闭动画 | 设置 500 ms；其它 `Motion.Normal=240 ms` | `SettingsWindow`/`Motion` |
| DWM 圆角/边框 | 33=2、2=1、34=0xFFFFFFFE | `WindowChrome` |
| 触摸反馈 kind | 1,3,4,5,6,7,8,9,10,11 | `WindowChrome` |
| 自检延迟 | selftest 1200 ms；shot 700 ms；diag 700 ms；open 1100 ms；perfcheck 2000 ms | `App.xaml.cs` |

---

*报告结束。所有“精确常量/文案/字段名”均来自工作树源码；本轮未运行程序与测试。*
