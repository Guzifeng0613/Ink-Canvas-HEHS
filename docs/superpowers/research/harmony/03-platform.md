# 03 — Platform 层源码盘点（Win32 → HarmonyOS ArkTS）

> 日期：2026-11-18
> 仓库：`D:\InkCanvas`，分支 `feat/winui3-migration`，基线 commit `0d26d63` + **工作树未提交的 3.4.0 改动**（本文以工作树为准）。
> 范围：`src/InkCanvas.Platform/` 全部 7 个源文件 + 其直接依赖的 Core 类型与全部消费方。
> 性质：**只读盘点**。本轮未构建、未启动应用、未接触摸屏/压感笔/红外屏，未运行任何测试。
> 记号：`DIP` = 逻辑像素（Windows DIP ↔ HarmonyOS vp，按 1:1 设计）；`px` = 物理像素；`>`、`>=` 严格区分（这些边界决定移植正确性）。

---

## 0. 一句话结论

Platform 层是**纯 Win32/GDI/COM P/Invoke + 纯数据模型**，不引用 WinUI/XAML，也不引用 Win2D（`InkCanvas.Platform.csproj` 只引用 `InkCanvas.Core`）。它对外只暴露五类能力：

1. 一块**逐像素透明、置顶、全屏、可接收笔/触/鼠标**的分层窗口 + 可写预乘 BGRA 缓冲 + `Present()`；
2. 指针事件归一化（`OverlayPointer`）与生命周期；
3. 手掌/触控分档纯逻辑（`PalmRejector`、`TouchContactClassifier`）；
4. 系统集成（全局热键、边缘手势抑制、屏幕度量、前台进程、自启、外部程序启动）；
5. 底层 P/Invoke 常量表（`NativeMethods`）。

其中 **手掌判定与触控分档是唯一可 1:1 纯逻辑移植的部分**；分层窗口与“覆盖其他应用”在 HarmonyOS 三方应用模型下是根本性平台约束（**未核实官方文档**，见 §2.12、§27 开放问题 1）。

---

## 1. 分层位置、依赖与消费方

```
Core (纯域模型)
  ↑
Platform (本项目：Win32 P/Invoke + Core 数据)
  ↑
Ink (InkController / OverlayInkPresenter)
  ↑
Controls → Features → App (Shell/Views)
```

- `InkCanvas.Platform.csproj`：`net10.0-windows10.0.26100.0`，`TargetPlatformMinVersion 10.0.17763.0`，唯一 `ProjectReference` 是 `InkCanvas.Core`。
- 因此 Platform **没有** WinUI/Win2D 依赖；`OverlayInkPresenter`（Ink 层）才是 Win2D 消费者。移植时 Platform 的接缝可以保持为“像素缓冲 + 指针事件”。

### 1.1 公开类型与消费方总表

| 类型 | 关键成员 | 直接消费方 | 分类 |
|---|---|---|---|
| `OverlayPointerKind` | `Pen=0, Touch=1, Mouse=2`（枚举顺序，勿改） | Ink/Presenter/SelfTest | pure-logic（数据类型） |
| `OverlayPointer` (readonly record struct) | `Id, X, Y, Pressure, ContactWidth, ContactHeight, Kind, TimeMs` | InkController、Presenter、PalmRejector、SelfTest | pure-logic（数据类型） |
| `LayeredInkWindow` | 构造、`PixelBuffer`、`Present`、`Show/Hide`、`ClickThrough`、`Topmost`、`DpiScale`、`HasQueuedPointerMessage`、`PointerDown/Move/Up`、`InputBegan`、`HotkeyPressed`、诊断计数 | `OverlayHost`、`OverlayInkPresenter`、`ShellController`、`SelfTest`、`App --diag` | platform-bound |
| `PalmRejector` | `PalmContactThreshold=30`、`PenHasPriority=true`、`OnDown/OnUp`、`IsPalmContact`、`ShouldDraw`、`ShouldErase`、`ClassifyContact`、`Reset` | `InkController`（`Palm` 属性）、`AppServices.ApplySettings`、`ShellController` | pure-logic（输入映射 adapt） |
| `HotkeyModifiers` | `None=0, Alt=1, Control=2, Shift=4, Win=8, NoRepeat=0x4000` | `OverlayHost`、`HotkeyService` | pure-logic（数据）+ platform-bound（注册） |
| `HotkeyService` | `Register/UnregisterAll/Dispatch`、id 基址 `0xB100` | `OverlayHost` | platform-bound |
| `ScreenRect` (readonly record struct) | `X, Y, Width, Height` | 各 View、`WindowChrome`、`ShellController` | pure-logic（数据） |
| `ScreenService` | `Primary`、`Virtual`、`WorkArea` | `OverlayHost`、`ShellController`、`FloatingBarWindow`、`BoardWindow`、`Countdown/Lottery/Info/Replay/Settings/PptNavigation`、`WindowChrome.CenterOnPrimary` | adapt |
| `ForegroundWindowService` | `GetForegroundWindow()`、`GetForegroundProcessName()` | `ShellController.ApplyAutoFold`、`BarPanelWindow`、`PickerWindows`（popup 外部点击关闭轮询） | platform-bound |
| `AutoStartService` | `IsEnabled/SetEnabled` | `SettingsWindow` | drop-candidate |
| `SoftwareLauncher` | `LaunchEasiCamera/FindEasiCameraExecutablePath/LaunchDesmos` | `BoardWindow` 启动器行 | platform-bound（注册表）+ adapt（URL） |
| `EdgeGestureService` | `SetEdgeGesturesDisabled(IntPtr,bool)` | `ShellController.ApplyEdgeGestureSetting`、`SelfTest` | platform-bound |
| `WindowEnumerator` | `VisibleWindowTitles/WindowExists/IsWindowMinimised` | **无消费方（死代码）** | drop-candidate |
| `NativeMethods` | 约 98 个常量/结构/P/Invoke | Platform 自身、`WindowChrome`、`SelfTest`、`Features/Tools.ScreenshotService` | platform-bound |

---

## 2. 分层透明覆盖窗口（`LayeredInkWindow`）

文件：`src/InkCanvas.Platform/Windows/LayeredInkWindow.cs`（444 行；3.4.0 工作树大改，见 §2.9）。

### 2.1 为什么不是 WinUI 窗口（行为语义）

类注释记录了实测结论：WinUI 3 XAML 窗口**总会绘制不透明背景**；尝试过 5 种方案（null backdrop + transparent root、`WS_EX_NOREDIRECTIONBITMAP` 在 XAML 初始化前后各一次、DWM blur-behind、两种 accent policy），输出与不透明控制窗口**逐像素相同**。只有“Win32 分层窗口 + 预乘 alpha 像素”能做到逐像素透明（含部分 alpha）。因此：**墨迹面是 Win32 窗口，所有 chrome 是 WinUI 3 窗口**。

移植含义：HarmonyOS 的 ArkUI 窗口默认也是不透明页面；如需“真透明 + 覆盖”，必须用系统级悬浮窗能力（三方应用通常没有），或改为应用内全屏 XComponent（见 §11）。

### 2.2 窗口创建常量（精确值）

| 项 | 值 | 说明 |
|---|---|---|
| 窗口类名 | `"InkCanvasOverlayWindow"` | `LayeredInkWindow.cs:34` |
| 窗口标题 | `"Ink Canvas Overlay"` | 仅调试可见（TOOLWINDOW 无任务栏） |
| 样式 | `WS_POPUP = 0x80000000` | 无边框、无标题 |
| 扩展样式 | `WS_EX_LAYERED 0x00080000 \| WS_EX_TOOLWINDOW 0x00000080 \| WS_EX_TOPMOST 0x00000008 \| WS_EX_NOACTIVATE 0x08000000` | 初始**没有** `WS_EX_TRANSPARENT`（书写时必须接收输入） |
| 位置/尺寸 | 由 `OverlayHost` 传入 `ScreenService.Primary`：`(0,0,SM_CXSCREEN,SM_CYSCREEN)` | 只覆盖主屏，不是虚拟桌面 |
| 鼠标指针 | `hCursor = IntPtr.Zero`（不设光标） | |
| 背景刷 | `hbrBackground = IntPtr.Zero` | |
| 窗口过程委托 | 静态字段持有，防止 GC 回收原生函数指针 | |

创建顺序（不可乱）：
1. `EnableMouseInPointer(true)`（**进程级、只调用一次**，用静态锁保护）。成功后 `IsMouseInPointerEnabled()` 记录 `_mouseAsPointer`；否则鼠标只发 `WM_MOUSE*`，走回退路径。
2. `RegisterClassW`（进程内一次）。
3. `CreateWindowExW`。
4. `GetDpiForWindow` → `DpiScale = dpi == 0 ? 1.0f : dpi / 96.0f`（**构造期一次性固定**，不随显示器迁移更新）。
5. `RegisterTouchWindow(hwnd, TWF_FINETOUCH | TWF_WANTPALM)`：`0x1 | 0x2 = 3`。
6. `SuppressSystemInputFeedback()`（10 种反馈全部设 0，见 §5）。
7. 建 DIB：`GetDC(NULL)` 缓存为 `_screenDc`；`CreateCompatibleDC`；`BITMAPINFO{ biWidth=width, biHeight=-height（top-down）, biPlanes=1, biBitCount=32, biCompression=BI_RGB(0) }`；`CreateDIBSection(..., usage=0)` 得到 `_bits`；`SelectObject` 进内存 DC。

### 2.3 像素缓冲契约

- `PixelBuffer`：`Width * Height * 4` 字节，**top-down、BGRA、预乘 alpha**。
- 隐藏（鼠标模式）帧：每个字节写 `1`，即 BGRA=`(1,1,1,1)`、alpha=1。原因：分层窗口**完全透明（alpha=0）的像素不参与命中测试**；alpha=1 既视觉透明又保持可点击。该模式只构建/上传一次，不随失效重复。
- 提交层清屏色：`Windows.UI.Color.FromArgb(1, 1, 1, 1)`（ARGB）。
- 活动层清屏色：`FromArgb(0,0,0,0)`。

### 2.4 `Present()` 的精确参数

```
POINT dst = { X, Y }          // 窗口位置（物理像素）
POINT src = { 0, 0 }
SIZE  size = { Width, Height }
BLENDFUNCTION {
  BlendOp = AC_SRC_OVER = 0,
  BlendFlags = 0,
  SourceConstantAlpha = 255,
  AlphaFormat = AC_SRC_ALPHA = 1
}
UpdateLayeredWindow(hwnd, _screenDc, ref dst, ref size, _memDc, ref src,
                    crKey = 0, ref blend, ULW_ALPHA = 0x02)
if (!IsVisible) Show();       // SW_SHOWNOACTIVATE = 4
```

- `_screenDc` 为窗口生命周期缓存（3.4.0 优化），不再每帧 `GetDC/ReleaseDC`。
- `Present` 不做节流；节流/合并在 Presenter（§2.7）。

### 2.5 显示/隐藏、点击穿透、置顶

| 成员 | 行为 | 底层常量 |
|---|---|---|
| `Show()` | 幂等；`ShowWindow(hwnd, SW_SHOWNOACTIVATE=4)`；**不抢焦点** | `SW_SHOWNOACTIVATE=4` |
| `Hide()` | 幂等；`ShowWindow(hwnd, SW_HIDE=0)` | `SW_HIDE=0` |
| `ClickThrough` get | `(GetWindowLongW(GWL_EXSTYLE=-20) & 0x20) != 0` | `WS_EX_TRANSPARENT=0x20` |
| `ClickThrough` set true | `AddExStyle(WS_EX_TRANSPARENT)` | 点击穿透到下层 |
| `Topmost` set | `SetWindowPos(HWND_TOPMOST(-1) 或 HWND_NOTOPMOST(-2), 0,0,0,0, SWP_NOMOVE(0x2)\|SWP_NOSIZE(0x1)\|SWP_NOACTIVATE(0x10))` | |

语义注意：
- `ClickThrough` 与墨迹可见性**分离**：鼠标模式 = `Window.ClickThrough=true` + `Presenter.InkVisible = showInk`（`OverlayHost.SetInteractionMode`）。`ShellController.ToggleClickThrough` 决定 `showInk`：放映中取 `IsShowStrokeOnSelectInPowerPoint`，否则取 `!HideStrokeWhenSelecting`。
- 覆盖层全屏且置顶，本身会挡住 chrome。任何输入落到覆盖层会把一个 topmost 窗口抬到 topmost 带最上；因此 `InputBegan` 事件让 Shell 立即把所有 chrome 重新抬到覆盖层之上（§9）。

### 2.6 指针→`OverlayPointer` 的精确换算

`TryReadPointer(id)`：

```
GetPointerInfo(id, out info)                     // 失败则丢弃该样本
kind = Mouse(默认)
pressure = 0.5f; cw = 0; ch = 0

if info.pointerType == POINTER_INPUT_TYPE_PEN(3):
    kind = Pen
    if GetPointerPenInfo(id, out pen):
        pressure = pen.pressure == 0 ? 0.5f : Clamp(pen.pressure / 1024f, 0, 1)

else if info.pointerType == POINTER_INPUT_TYPE_TOUCH(2):
    kind = Touch
    if GetPointerTouchInfo(id, out touch):
        cw = (touch.rcContact.Right - touch.rcContact.Left) / DpiScale
        ch = (touch.rcContact.Bottom - touch.rcContact.Top) / DpiScale
        pressure = touch.pressure == 0 ? 0.5f : Clamp(touch.pressure / 1024f, 0, 1)

pt = info.ptPixelLocation
sample = OverlayPointer(
    id,
    (pt.X - X) / DpiScale,          // 相对覆盖层左上角，DIP
    (pt.Y - Y) / DpiScale,
    pressure, cw, ch, kind,
    info.dwTime)                    // GetTickCount 纪元
```

关键语义：
- 笔/触压力原始范围 **0..1024**；`0` 被解释为“无压力数据”→ `0.5`；否则归一化并夹取 `[0,1]`。
- 接触矩形取 **`rcContact`（已按系统 DPI 校准）**，不是 `rcContactRaw`；除以 `DpiScale` 后单位是 **DIP**。手掌阈值因此是 DIP 值。
- 鼠标（pointer 模式）没有接触矩形：`cw=ch=0`，压力 `0.5`。
- 时间戳：笔/触用 `POINTER_INFO.dwTime`；鼠标回退用 `Environment.TickCount`。仅用于延迟统计，不参与手掌判定。

### 2.7 指针生命周期状态机（3.4.0 重写，务必照搬语义）

内部状态：`Dictionary<uint, OverlayPointer> _activePointers`、`OverlayPointer? _fallbackMouse`。

| 消息 | 精确行为 |
|---|---|
| `WM_POINTERDOWN = 0x0246` | `PointerMessageCount++`、`PointerDownCount++` → `BeginPointer(wParam)` |
| `WM_POINTERUPDATE = 0x0245` | 计数 → `MovePointer(wParam)` |
| `WM_POINTERUP = 0x0247` | `PointerMessageCount++` → `EndPointer(id, readCurrent: true)`（用抬起瞬间的最终样本） |
| `WM_POINTERCAPTURECHANGED = 0x024F` | `EndPointer(id, readCurrent: false)`（capture 变化后 `GetPointerInfo` 可能失败，用最后有效样本收笔） |
| `WM_CAPTURECHANGED = 0x0215` | 只结束 `Kind == Mouse` 的活动指针；若 `_fallbackMouse` 存在则清空并补发 `PointerUp` |
| `WM_CANCELMODE = 0x001F` | 结束**所有**活动指针（鼠标/笔/触）；补发 fallback 鼠标 up |
| `WM_HOTKEY = 0x0312` | `HotkeyPressed(w.ToInt32())` |
| `WM_DESTROY = 0x0002` | 直接返回，不 `DefWindowProc` |
| 其他 | `DefWindowProcW` |

`BeginPointer` 顺序（**对下游有语义意义**）：
1. 解析样本；失败返回。
2. 若同 id 已在活动表 → 先 `EndPointer(readCurrent:false)`（去重，避免“悬空笔画”）。
3. 存入活动表。
4. `PointerDown?.Invoke(sample)`。
5. `InputBegan?.Invoke()`。
6. 若该 id 仍在表中且 `Kind == Mouse` → `SetCapture(hwnd)`。
   - 笔/触有隐式捕获；鼠标-in-pointer 需要显式捕获，保证拖过工具栏 HWND 后仍收到 up。

> 注释明确：先建立控制器状态，再触发可能同步改变焦点/捕获的回调（关闭弹窗、呈现墨迹、抬升其他窗口）。

`MovePointer`：
- 若 id 不在活动表 → 忽略（**不会**凭空产生 move）。
- 重新读样本并更新表。
- 再次 `GetPointerInfo` 检查 `pointerFlags`：
  - `CANCELED = 0x8000` 置位，或
  - `INCONTACT = 0x0004` 未置位
  → `EndPointer(id, readCurrent:false)` 并返回；
- 否则 `PointerMove?.Invoke(sample)`。

`EndPointer(id, readCurrent)`：
- 不在表中 → 返回（保证 up 不重复触发）。
- `readCurrent=true` 时用当前样本替换。
- `PointerUpCount++` → `PointerUp`。
- 若 `Kind == Mouse` 且 `GetCapture()==hwnd` → `ReleaseCapture()`。

### 2.8 鼠标回退路径（`_mouseAsPointer == false` 时）

| 消息 | 行为 |
|---|---|
| `WM_LBUTTONDOWN = 0x0201` | **先** `InputBegan`，再 `RaiseMouse(PointerDown, mouseDown:true)` |
| `WM_MOUSEMOVE = 0x0200` | 仅当 `_fallbackMouse.HasValue` 时 `RaiseMouse(PointerMove)` |
| `WM_LBUTTONUP = 0x0202` | `RaiseMouse(PointerUp, mouseUp:true)` |

`RaiseMouse` 细节：
- `MouseMessageCount++`；无订阅者则返回。
- `lParam` 低 16 位 = x（short），高 16 位 = y（short），**客户区坐标**。
- 构造样本：`OverlayPointer(1, x/DpiScale, y/DpiScale, 0.5f, 0, 0, Mouse, Environment.TickCount)`。
- Down：记录 `_fallbackMouse`、`SetCapture`、回调。
- Up：若没有 `_fallbackMouse` 则拒绝（防止无 down 的 up）；清空、回调、若捕获在本窗口则释放。

> 顺序差异（移植时注意）：pointer 路径是 `PointerDown → InputBegan`；鼠标回退路径是 `InputBegan → PointerDown`。

### 2.9 3.4.0 工作树改动（以工作树为准）

相对 `0d26d63`：
- 指针处理从“每条消息直接 `Raise`”重写为上述活动表 + 捕获 + 取消 + 回退鼠标状态机。
- `_screenDc` 生命周期缓存；`Dispose` 补 `ReleaseDC`。
- 新增 `RegisterTouchWindow`、`SuppressSystemInputFeedback`、`HasQueuedPointerMessage`、`InputBegan`。
- `NativeMethods` 增加 `GetCapture`、`GetCursorPos`、`GetWindow`、`IsWindowVisible`。
- `PalmRejector.DefaultAdvanced` 改为 `static readonly`（避免每次接触分配）。
- `ForegroundWindowService.GetForegroundWindow()` 公开（弹窗关闭轮询使用）。

### 2.10 诊断计数

`PointerMessageCount`、`PointerDownCount`、`PointerMoveCount`、`PointerUpCount`、`MouseMessageCount`、`static MouseAsPointer`。`--diag=<path>` 每 700 ms 写一行（见 `App.xaml.cs:56-60`）：

```
pointerMessages=… (down=… move=… up=…); mouseMessages=…; mouseAsPointer=…;
clickThrough=…; visible=…; strokes=…; live=…; tool=…
```

自检中 `SelfTest` 还读取 `Presenter.PresentedFrames/GpuReadbackBytes/LatencySamples/...`（见 §10）。

### 2.11 消费方与测试

- 消费方：`OverlayHost`（创建/Show/Hide/ClickThrough）、`OverlayInkPresenter`（PixelBuffer/Present/事件/HasQueuedPointerMessage）、`ShellController`（Handle 用于边缘手势与 Z 序）、`SelfTest`、`App --diag`。
- 测试：**无单元测试**（Win32 资源无法在纯逻辑测试中构造）。运行时由 `--selftest`/`--diag` 与人工实机验证覆盖；文档记录过真机（Windows Server 2025、125% DPI）验证窗口/拖动/模式往返，但本轮未运行。

### 2.12 分类与 HarmonyOS 映射

**platform-bound**。可保留的语义契约（建议做成平台接口）：
```
OverlaySurface {
  width, height, dpiScale
  pixelBuffer: ArrayBuffer (BGRA premultiplied, top-down)
  present()
  show()/hide()
  clickThrough: boolean
  topmost: boolean
  pointerDown/Move/Up events
}
```
HarmonyOS 候选（**均需在 API 26/24 文档与真机核实**）：
- 应用内全屏 `XComponent` + `Drawing`/`Canvas` 渲染；GPU 直接合成，无需 CPU readback。
- `window.WindowStage.createSubWindow` / `setWindowLayoutFullScreen` / `setWindowSystemBarEnable` 可用于应用内多窗口与沉浸式。
- **三方应用跨应用悬浮（覆盖 PPT/浏览器/其他 App）在 HarmonyOS 上没有公开等价能力**（截至 API 26，**未在官方文档核实**）；“透明覆盖桌面”很可能必须放弃或改为系统/企业签名能力。这是移植最大风险，见 §27。

---

## 3. 指针输入归一化（`OverlayPointer`）

### 3.1 数据模型（pure-logic）

```csharp
public enum OverlayPointerKind { Pen, Touch, Mouse }

public readonly record struct OverlayPointer(
    uint Id,            // Windows pointer id（低 16 位）；鼠标回退恒为 1
    float X,            // DIP，相对覆盖层原点
    float Y,            // DIP
    float Pressure,     // [0,1]，0.5 = 无压力数据
    float ContactWidth, // DIP；笔/鼠为 0
    float ContactHeight,// DIP；笔/鼠为 0
    OverlayPointerKind Kind,
    uint TimeMs);       // 设备时间戳（ms）
```

- `InkController.OnPointerDown/Move/Up` 直接消费；`InkPoint.Pressure` 原样进入笔锋/宽度管线（`InkController.cs:330,383`）。
- 测试：`SelfTest.CheckTapInk` 用 `OverlayPointer` 构造 Pen/Mouse/Touch 三种样本，钉住“tap 提交可见点 + undo/redo”，但它是运行时自检，不是单元测试。

### 3.2 分类与映射

数据模型 **pure-logic**；采集 **adapt**。HarmonyOS 候选：
- ArkUI `onPointer` / `onTouch` 的 `PointerEvent`/`TouchEvent`：`sourceTool`（Pen/Finger/Mouse）、`pressure`、`tiltX/tiltY`、`getHistoricalPoints()`（未合并历史点）。
- 接触尺寸：`TouchObject.width/height`（或 `TouchEvent` 接触区域）是否可用、单位是 px 还是 vp，**必须真机核实**；若只有压力没有接触矩形，`PalmRejector` 的几何阈值需要替代方案（如压力/面积/笔优先）。
- 坐标需从屏幕 px 转为 vp（`px2vp`），保持 `DpiScale` 等价物。

---

## 4. 手掌判定（`PalmRejector`）— 精确阈值 / 状态机 / 时序

文件：`src/InkCanvas.Platform/Input/PalmRejector.cs`（95 行）。
测试：`tests/InkCanvas.Core.Tests/PalmRejectorTests.cs`（9 个 `[Fact]`，**本轮未运行**）。

### 4.1 常量与默认值

| 字段 | 默认 | 实际接线 | 单位 |
|---|---|---|---|
| `PalmContactThreshold` | `30f` | `AppServices.ApplySettings`: `Math.Max(8, Settings.Advanced.FingerModeBoundsWidth)` → **8..50 DIP**（默认配置 30，推荐重置 20） | DIP |
| `PenHasPriority` | `true` | 未改动 | bool |
| `ForceEraser` | `false` | 未改动 | bool |
| `ForcePointEraser` | `true` | 未改动 | bool |
| `NibMode` | `false` | `Settings.Startup.IsEnableNibMode`（默认 false，重置 false） | bool |
| `Advanced` | `null` | 指向**活的** `Settings.Advanced` 实例（非拷贝） | — |
| `PptGestureEligible` | `false` | 每次 Shell 轮询：放映中 && 文档笔画数==0 && `IsEnableFingerGestureSlideShowControl` | bool |

### 4.2 状态机（无任何时间阈值/去抖/超时）

内部状态只有两个：
- `uint? _activePenId`（**单个**活动笔 id，后 down 的笔覆盖先前的）
- `HashSet<uint> _palms`（活动手掌 id 集合）

```
OnDown(p):
    if p.Kind == Pen:                       _activePenId = p.Id
    else if p.Kind == Touch && IsPalmContact(p): _palms.Add(p.Id)

OnUp(p):
    if _activePenId == p.Id:                _activePenId = null
    _palms.Remove(p.Id)                     // 无条件移除

IsPalmContact(p) = max(p.ContactWidth, p.ContactHeight) >= PalmContactThreshold   // 含等号

ShouldDraw(p):
    if p.Kind == Pen: return true
    if _palms.Contains(p.Id): return false
    if PenHasPriority && IsPenActive: return false
    return true

ShouldErase(p) = p.Kind == Touch && _palms.Contains(p.Id)

Reset(): _activePenId = null; _palms.Clear()
```

关键语义（移植必须保留）：
1. **判定只在 down 时做一次**；活动期间接触矩形变化不会重新分类。`OnUp` 才清除。
2. **没有时间窗口/超时/连续帧确认**；不存在“N ms 内判定为手掌”之类逻辑。`TimeMs` 不参与。
3. 阈值比较是 `>=`，不是 `>`。
4. 只统计 Touch；Pen/Mouse 的 `ContactWidth/Height` 为 0，永不成为手掌。
5. `PalmRejector` 刻意**没有视觉副作用**：WPF 原版在检测到手掌时把整层墨迹压到 `Opacity=0.1`，造成闪屏；本移植只影响路由，不改透明度（源码注释与 PORTING-NOTES 均强调）。
6. 笔优先只抑制 **Touch 且工具为 Pen/Highlighter** 的绘制（`InkController.cs:244`）；Eraser/Select/Shape 工具下掌/指仍会进入工具逻辑。手掌擦除（`ShouldErase`）在笔优先之前判断，因此**手掌可优先触发擦除**。
7. `OnDown` 先于 `ClassifyContact`（`InkController.cs:196,222`）；`PalmContactThreshold` 与 `Advanced.*BoundsWidth` 是两套独立阈值，会同时生效。

### 4.3 测试钉住的行为（未运行）

| 测试 | 钉住的语义 |
|---|---|
| `SmallContactIsNotAPalm` | 10 DIP，阈值 30 → false |
| `LargeContactIsAPalm` | 45 DIP → true |
| `PalmDoesNotDrawButDoesErase` | 50 DIP：`ShouldDraw=false`、`ShouldErase=true` |
| `FingertipDrawsAndDoesNotErase` | 8 DIP：可画、不可擦 |
| `ActivePenSuppressesTouchDrawing` | 笔 down 后触摸不可画；笔自身可画 |
| `PenPriorityCanBeDisabled` | `PenHasPriority=false` 时触摸可画 |
| `LiftingPenReleasesTouchSuppression` | 笔 up 后恢复触摸绘制 |
| `LiftingPalmClearsIt` | 掌 up 后集合清空、不再可擦 |
| `ResetClearsAllState` | 一次清空笔与掌 |

### 4.4 分类与 HarmonyOS 映射

逻辑部分 **pure-logic，可 1:1**（建议放进 `harmony/core`，用同一套黄金向量）。前置条件 **adapt**：上游必须提供 Touch 的接触宽高（DIP/vp）。若无接触矩形，需要明确降级策略（只做笔优先 + 压力/面积启发式），并标注未验证。

---

## 5. 触控分档（`TouchContactClassifier`）

文件：`src/InkCanvas.Core/Input/TouchContactClassifier.cs`（不属于 Platform 目录，但由 `PalmRejector.ClassifyContact` 直接调用，是手掌/触控判定的核心）。**无单元测试**。

### 5.1 枚举与结果

```csharp
public enum TouchIntent { Draw, EraseByStroke, EraseByPoint, GestureOnly, Ignore }
public readonly record struct TouchDecision(TouchIntent Intent, double EraserDiameter);
```

### 5.2 配置默认值与推荐值

| JSON 键 | 属性 | 构造默认 | 「重置设置」推荐 | 设置 UI 范围/步长 |
|---|---|---|---|---|
| `isSpecialScreen` | IsSpecialScreen | false | **true** | 开关 |
| `isQuadIR` | IsQuadIR | false | false | 开关 |
| `touchMultiplier` | TouchMultiplier | **0.25** | **0.3** | 0..5，步长 0.01 |
| `nibModeBoundsWidth` | NibModeBoundsWidth | 10 | **5** | 1..50，步长 1（int） |
| `fingerModeBoundsWidth` | FingerModeBoundsWidth | **30** | **20** | 1..50，步长 1（int） |
| `nibModeBoundsWidthThresholdValue` | Nib…ThresholdValue | 2.5 | 2.5 | 0..10，步长 0.01 |
| `fingerModeBoundsWidthThresholdValue` | Finger…ThresholdValue | 2.5 | 2.5 | 0..10，步长 0.01 |
| `nibModeBoundsWidthEraserSize` | Nib…EraserSize | 0.8 | 0.8 | 0.01..10，步长 0.01 |
| `fingerModeBoundsWidthEraserSize` | Finger…EraserSize | 0.8 | 0.8 | 0.01..10，步长 0.01 |
| `isEnableEdgeGestureUtil` | IsEnableEdgeGestureUtil | false | false | 开关 |
| `isLogEnabled` | IsLogEnabled | true | true | 开关 |
| `isSecondConfimeWhenShutdownApp` | … | false | false | 开关 |
| `isEnableNibMode`（Startup） | NibMode | false | false | 开关「启用笔尖模式」 |

### 5.3 精确算法

```
MeasureContactWidth(width, height, isQuadIr):
    return isQuadIr ? sqrt(width * height) : width

BoundsWidth(advanced, nibMode):
    return nibMode ? advanced.NibModeBoundsWidth : advanced.FingerModeBoundsWidth

Classify(contactWidth, advanced, nibMode,
         forceEraser=false, isDrawingShape=false,
         forcePointEraser=true, pptGestureEligible=false):

    if advanced == null: return (Draw, 5)

    boundsWidth = BoundsWidth(advanced, nibMode)

    // 特殊屏 + 触摸倍数为 0 => 禁用橡皮
    eraserEnabled = (advanced.TouchMultiplier != 0) || !advanced.IsSpecialScreen

    if eraserEnabled && contactWidth > boundsWidth:          // 严格 >
        if !isDrawingShape && forceEraser:
            return (Ignore, 0)

        threshold = nibMode ? advanced.NibModeBoundsWidthThresholdValue
                            : advanced.FingerModeBoundsWidthThresholdValue

        if contactWidth > boundsWidth * threshold:            // 严格 >
            diameter = contactWidth * (nibMode ? advanced.NibModeBoundsWidthEraserSize
                                               : advanced.FingerModeBoundsWidthEraserSize)
            if advanced.IsSpecialScreen:
                diameter *= advanced.TouchMultiplier
            return (EraseByPoint, diameter)

        if pptGestureEligible:
            return (GestureOnly, 5)
        return (EraseByStroke, 5)

    return (Draw, forcePointEraser ? 50 : 5)
```

### 5.4 当前接线语义（`PalmRejector.ClassifyContact`）

```
advanced = Advanced ?? DefaultAdvanced (static readonly new AdvancedSettings())
width = MeasureContactWidth(p.ContactWidth, p.ContactHeight, advanced.IsQuadIR)
return Classify(width, advanced, NibMode, ForceEraser, isDrawingShape:false,
                ForcePointEraser, PptGestureEligible)
```

- `isDrawingShape` **恒为 false**（图形工具不走这里）。
- `ForcePointEraser` 只影响 `Draw` 的直径（50 vs 5），而 `InkController` 对 `Draw` 不使用直径 → **当前接线中该字段无效**。
- `ClassifyContact` **不使用** `PalmContactThreshold`；两套阈值并行。

### 5.5 `InkController` 对决策的消费（精确后果）

| 决策 | InkController 行为 |
|---|---|
| `Ignore` | 直接 return，不画不擦 |
| `GestureOnly` | 记录 `_slideGestureStartX = p.X`，`_slideGestureActive = true`，down 不画 |
| `EraseByPoint`（且 `PalmErases=true`） | `_gestureEraserRadius = diameter / 2`（float），`BeginErase(world, partialStroke:true)` |
| `EraseByStroke`（且 `PalmErases=true`） | `_gestureEraserRadius = 2.5f`，`BeginErase(world, partialStroke:false)` |
| `Draw` | 继续走 `ShouldDraw`/工具逻辑 |

补充：
- `PalmErases` 默认 `true`（`InkController.cs:129`）。
- `MultiTouchEnabled` 默认 `true`（`InkController.cs:133`）；为 false 时双指才进入手势/取消触摸墨迹。
- `GestureOnly` 后抬指：`|p.X - startX| > 60f`（**严格 >，60 DIP**）触发 `SlideFlipRequested(dx < 0)`，即左滑→下一页。该 60 是移植自定阈值（PORTING-NOTES 已注明原实现由 `iniP` 比较）。
- `EraseByPoint` 直径可为 0（当 eraserSize 或 multiplier 为 0）；半径 0 需下游擦除器能处理。

### 5.6 分类与 HarmonyOS 映射

**pure-logic，1:1**。建议原样搬到 `harmony/core`，并建立黄金向量（§12）。唯一平台依赖是接触宽高/压力输入。

---

## 6. 触摸时延与系统反馈抑制（platform-bound）

### 6.1 `RegisterTouchWindow(hwnd, 3)`

| 标志 | 值 | 语义 |
|---|---|---|
| `TWF_FINETOUCH` | 0x1 | 优先非合并触摸（不把多个接触点合并成一个） |
| `TWF_WANTPALM` | 0x2 | **关闭系统的手掌检测缓冲**，让手掌包也立即上报；因为应用自己做分类，手掌接触必须到达 |

行为目的：首笔落墨延迟最小；笔输入不受影响。

### 6.2 `SetWindowFeedbackSetting(hwnd, kind, 0, sizeof(int), 0)`

关闭的 `FEEDBACK_TYPE`（全部设 `0`，即 `BOOL FALSE`）：

| kind | 含义 |
|---|---|
| 1 | `FEEDBACK_TOUCH_CONTACTVISUALIZATION`（接触点/水波纹） |
| 3 | `FEEDBACK_PEN_TAP` |
| 4 | `FEEDBACK_PEN_DOUBLETAP` |
| 5 | `FEEDBACK_PEN_PRESSANDHOLD`（按住右键圆环） |
| 6 | `FEEDBACK_PEN_RIGHTTAP` |
| 7 | `FEEDBACK_TOUCH_TAP` |
| 8 | `FEEDBACK_TOUCH_DOUBLETAP` |
| 9 | `FEEDBACK_TOUCH_PRESSANDHOLD` |
| 10 | `FEEDBACK_TOUCH_RIGHTTAP` |
| 11 | `FEEDBACK_GESTURE_PRESSANDHOLD` |

同一组 kind 也用于 `WindowChrome.SuppressTouchFeedback(Window)`，在 `BoardWindow` 创建/重建 surface 时调用（`BoardWindow.cs:194,2041,2065`）。

### 6.3 分类与映射

两者都是 **platform-bound**；HarmonyOS 没有“系统接触视觉/按住圆环”这一层（自绘组件默认无系统反馈），因此**建议 drop**（不是功能缺失，而是不再需要）。`TWF_WANTPALM` 的等价物是按需使用 ArkUI 历史点/原始指针；需确认是否默认合并。

---

## 7. 全局热键（`HotkeyService`）

文件：`src/InkCanvas.Platform/Input/HotkeyService.cs`（54 行）。

### 7.1 机制

```csharp
[Flags] enum HotkeyModifiers : uint { None=0, Alt=1, Control=2, Shift=4, Win=8, NoRepeat=0x4000 }

Register(modifiers, virtualKey, action):
    id = _nextId++                     // 初值 0xB100 = 45312
    ok = RegisterHotKey(hwnd, id, (uint)modifiers | 0x4000, virtualKey)
    if (!ok) return false              // 失败非致命（键可能被占用）
    _actions[id] = action; return true

Dispatch(id): 命中则执行 action() 并返回 true；否则 false

UnregisterAll(): 对每个 id 调 UnregisterHotKey；清空字典
```

- `NoRepeat (0x4000)` **总是**被 OR 进去（按住不重复触发）。
- 注册在覆盖层窗口的 `WM_HOTKEY` 上：`Window.HotkeyPressed += id => Hotkeys.Dispatch(id)`（`OverlayHost.cs:25`）。
- 系统级全局热键（应用不在前台也能触发），不是应用内快捷键。

### 7.2 实际键位表（`OverlayHost.RegisterHotkeys`，来自 WPF 操作指南）

| 修饰符 | VK | 动作 | 备注 |
|---|---|---|---|
| Ctrl | `0x5A` Z | Undo | |
| Ctrl | `0x59` Y | Redo | |
| Ctrl | `0x45` E | Clear | |
| Alt | `0x45` E | 切换橡皮 | |
| Alt | `0x53` S | 选择工具 | 非空才注册 |
| Alt | `0x4C` L | 直线/图形 | |
| Alt | `0x42` B | 切换画板 | |
| Alt | `0x56` V | 折叠浮动栏 | |
| Alt | `0x43` C | 截图 | |
| Alt | `0x44` D | 画笔/调色板 | |
| Alt | `0x51` Q | 切换鼠标模式（逃生口） | 覆盖层全屏时即使浮动栏不可达也能退出 |
| None | `0x22` PageDown | PPT 下一页 | 仅放映中执行（Shell 内 guard） |
| None | `0x21` PageUp | PPT 上一页 | 同上 |
| Shift | `0x1B` Escape | 退出放映 | 同上 |

### 7.3 消费方与测试

- `OverlayHost.RegisterHotkeys` 由 `ShellController.Start` 接线（`ShellController.cs:152-176`）。
- 无单元测试；`HotkeyModifiers` 数值可做纯数据向量。
- 分类：**platform-bound**。HarmonyOS 三方应用通常不能在后台监听全局按键；候选是 `@ohos.multimodalInput.inputMonitor`（InputKit，见 `.claude/skills/harmony-os/system/references/input-multimodal-kit.md`）与 `keyEvent`，但权限/可用性**未核实**，且可能仅系统应用可用。可降级为应用内 `onKeyEvent`（前台窗口），或平板外接键盘的快捷键；产品需决定是否保留“全局”语义。

---

## 8. 边缘手势抑制（`EdgeGestureService`）

文件：`src/InkCanvas.Platform/System/EdgeGestureService.cs`（前半 76 行）。

### 8.1 精确 COM 常量与流程

| 项 | 值 |
|---|---|
| 属性 fmtid | `32CE38B2-2C9A-41B1-9BC5-B3784394AA44` |
| 属性 pid | `2` |
| 属性语义 | `System.EdgeGesture.DisableTouchWhenFullscreen` |
| `IPropertyStore` IID | `886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99` |
| VARTYPE | `VT_BOOL = 11` |
| true | `-1`（VARIANT_TRUE；**不是 1**） |
| false | `0` |
| 调用 | `SHGetPropertyStoreForWindow(hwnd, ref iid, out store)` |

流程：
1. `hwnd == 0` → false。
2. 取属性仓库；失败/null → false。
3. 组装 `PropertyKey{fmtid, pid=2}` 与 `PropVariant{vt=11, boolVal = disabled ? -1 : 0}`。
4. `store.SetValue(key, value)` → `store.Commit()`（**上游原实现漏了 Commit，本移植补上**）。
5. 任何异常吞掉返回 false；`finally` 中 `Marshal.FinalReleaseComObject(store)`。

### 8.2 实际接线

- `ShellController.ApplyEdgeGestureSetting()`：
  `SetEdgeGesturesDisabled(Overlay.Window.Handle, Settings.Advanced.IsEnableEdgeGestureUtil)`。
- 调用时机：`Start()` 末尾（`ShellController.cs:187`）与设置变更后（`:557`）。
- 设置 UI：`SettingsWindow.cs:576` 开关「阻止 Win 10/11 的边缘手势」。
- 只作用于**覆盖层窗口**；切到画板（覆盖层隐藏）时属性仍在隐藏窗口上，是否继续抑制取决于 Windows 对该窗口全屏状态的判定 → **行为未验证**。
- 自检：`SelfTest.cs:191-194` 对覆盖层设置 true 再 false，报告 `edge gesture property applied`。
- 文档记录：PORTING-NOTES 称“已验证属性写入成功；补上 Commit”。

### 8.3 分类与映射

**platform-bound**。语义是“全屏时禁用系统边缘滑动，防止课堂误触切走应用”。HarmonyOS 候选：沉浸式全屏 + 系统栏/导航条控制（`setWindowLayoutFullScreen`、`setWindowSystemBarEnable`、`setSpecificSystemBarEnabled` 等，**需核实 API 26/24 与设备差异**）；没有逐窗口的“禁用边缘手势”公开属性。若无法屏蔽系统返回/多任务手势，则**建议 drop**，并在产品层接受误触风险或提供应用内手势替代。

---

## 9. 系统度量（`ScreenService`）

文件：`src/InkCanvas.Platform/System/SystemServices.cs:7-35`。

### 9.1 精确常量与公式

| 成员 | Win32 | 常量值 | 返回 |
|---|---|---|---|
| `Primary` | `GetSystemMetrics(SM_CXSCREEN=0)`, `SM_CYSCREEN=1` | 0/1 | `(0, 0, cx, cy)` 物理像素 |
| `Virtual` | `SM_XVIRTUALSCREEN=76`, `SM_YVIRTUALSCREEN=77`, `SM_CXVIRTUALSCREEN=78`, `SM_CYVIRTUALSCREEN=79` | 76..79 | 虚拟桌面矩形，物理像素 |
| `WorkArea` | `SystemParametersInfoW(SPI_GETWORKAREA=0x0030, 0, ref RECT, 0)` | 0x0030 | `(rect.Left, rect.Top, right-left, bottom-top)`；失败回退 `Primary` |

注意：
- `WorkArea` 是**主显示器**工作区（扣任务栏/停靠栏），不是当前窗口所在显示器。多屏时 `WindowChrome.GetWorkArea(window)` 另用 `DisplayArea`（WinUI API）。
- `Primary` 原点假定 `(0,0)`（Windows 主屏约定）。
- 单位全部物理像素；使用方自行除以 DPI 得 DIP。

### 9.2 消费方（精确用途）

| 消费方 | 用途 |
|---|---|
| `OverlayHost` | 覆盖层全屏尺寸；`ViewCenter = (W/DpiScale/2, H/DpiScale/2)` DIP |
| `ShellController.OnPptScreenshotRequested` / `ScreenshotAsync` | 主屏截图区域 |
| `FloatingBarWindow.PlaceBottomCentre` | 工作区底部居中，`y = work.Y + work.Height - height - ceil(max(0,bottomMargin)*dpi)` |
| `FloatingBarWindow.SetFolded` | 折叠把手垂直居中；左/右把手 30×50 DIP，露出 14 DIP |
| `BoardWindow.CenterAndScaleElement` | 主屏 DIP 尺寸的一半作为元素最大缩放框 |
| `Countdown/Lottery/Info/Replay/Settings/PptNavigation` | 居中/夹取到工作区 |
| `WindowChrome.CenterOnPrimary` | DIP 中心放置 |

### 9.3 分类与映射

**adapt**。HarmonyOS 候选（需核实）：
- `display.getDefaultDisplaySync()` 的 `width/height`（px）、`densityDPI`、`densityPixels`。
- 工作区/安全区：`window.getWindowAvoidArea`（系统栏、导航条、刘海等），把避让区从 px 换算为 vp 后得到等价 `WorkArea`。
- 多设备：平板/2in1 可能分屏/自由窗口，`Primary` 的“全屏主屏”假设需要改为“当前窗口可用区域”。

---

## 10. 前台应用识别与自动折叠（`ForegroundWindowService`）

文件：`src/InkCanvas.Platform/System/SystemServices.cs:37-59`。

### 10.1 精确行为

```csharp
GetForegroundWindow() => GetForegroundWindow()   // HWND，3.4.0 新增公开

GetForegroundProcessName():
    hwnd = GetForegroundWindow()
    if hwnd == 0: return ""
    GetWindowThreadProcessId(hwnd, out uint pid)
    if pid == 0: return ""
    using proc = Process.GetProcessById((int)pid)
    return proc.ProcessName           // 不带 .exe
    // 任何异常 → ""（前台窗口可能在两次调用之间消失）
```

### 10.2 自动折叠规则（`ShellController.ApplyAutoFold`，`ShellController.cs:446-479`）

```
foreground = GetForegroundProcessName()
a = Settings.Automation
fold = foreground != "" && a.IsEnableAutoFold && (
    (a.IsAutoFoldInEasiNote        && foreground.Contains("EasiNote",   OrdinalIgnoreCase)) ||
    (a.IsAutoFoldInEasiCamera      && foreground.Contains("EasiCamera", OrdinalIgnoreCase)) ||
    (a.IsAutoFoldInSeewoPincoTeacher && foreground.Contains("Pinco",    OrdinalIgnoreCase)) ||
    (a.IsAutoFoldInHiteTouchPro    && foreground.Contains("HiteTouch",  OrdinalIgnoreCase)) ||
    (a.IsAutoFoldInMSWhiteboard    && foreground.Contains("Whiteboard", OrdinalIgnoreCase)) ||
    (a.IsAutoFoldInPPTSlideShow    && _lastSlideShowState))
```

行为：
- 折叠/展开有手动覆盖 `_manualFoldOverride`；自动状态变化时清空覆盖。
- 折叠时先 `ToggleClickThrough()`（若尚未鼠标模式）再 `SetFolded(true)`；展开只恢复浮动栏，覆盖层仍留鼠标模式，直到用户选笔。
- 折叠后强制 `Overlay.ClickThrough = true`。
- 轮询：Shell 定时器空闲 **900 ms**；放映中改为 `PptInkSession.RecommendedPollInterval`（放映 **200 ms**，否则 1000 ms；宿主对空闲轮询固定 900）。即自动折叠判定最坏约 900 ms 延迟。

### 10.3 保真度缺口（重要）

`ApplyAutoFold` **只检查 6 个条件**，以下设置项存在但**未参与判定**：
- `isAutoFoldInEasiNote3C`
- `isAutoFoldInHiteCamera`
- `isAutoFoldInWxBoardMain`
- `isAutoFoldInOldZyBoard`
- `isAutoFoldInEasiNoteIgnoreDesktopAnno`（`IsEnableAutoFold` 派生属性包含部分项，但判定未用派生值）

此外：
- `EasiNote3C` 会被 `Contains("EasiNote")` 顺带匹配；`HiteCamera` 不会被 `Contains("HiteTouch")` 匹配。
- `IsEnableAutoFold`（`AppSettings.cs:259-263`）包含上述全部项（含 HiteCamera/WxBoard/OldZy/PPT/MSWhiteboard），但 `ApplyAutoFold` 没有用它，而是逐项手写 → 两处会漂移。
- 移植建议：在 harmony 侧抽成“规则表 + 进程名匹配器”的纯逻辑，避免继续手写 if。

### 10.4 其他消费方

- `BarPanelWindow` 与 `PickerWindows`：每 **300 ms** 轮询 `GetForegroundWindow()`，当前前台既不是弹窗本身也不是 owner 时关闭弹窗（WPF `HideSubPanels` 的外部点击关闭替代方案）。`PickerWindows` 还要求弹窗至少曾获得过一次前台（`_everForeground`），防止打开时 `Activate()` 失败导致闪退式关闭。
- 覆盖层是 `WS_EX_NOACTIVATE`，点击不会改变前台 → 覆盖层输入通过 `InputBegan` 直接关闭面板，不依赖前台轮询。

### 10.5 分类与映射

**platform-bound**。HarmonyOS 三方应用**没有**公开的“获取其他应用前台进程名”的等价 API（`getRunningProcessInformation` 等通常受权限/系统应用限制，且隐私策略更严）。PPT/WPS 互操作本身也是 Windows COM 专有 → 建议**自动折叠整体 drop**（或仅保留“本应用内页面切换时折叠”）。弹窗外部点击关闭应改用 ArkUI 的焦点/触摸外点事件（`onBlur`/全屏拦截层），不要移植进程名轮询。

---

## 11. 开机自启（`AutoStartService`）

文件：`src/InkCanvas.Platform/System/SystemServices.cs:61-87`。

### 11.1 精确行为

| 项 | 值 |
|---|---|
| 注册表根 | `HKEY_CURRENT_USER`（无需提权） |
| 键路径 | `Software\Microsoft\Windows\CurrentVersion\Run` |
| 值名 | `InkCanvasWinUI` |
| 写入值 | `"<executablePath>"`（**带双引号**） |
| 启用 | `CreateSubKey` + `SetValue` |
| 禁用 | `DeleteValue(valueName, throwOnMissingValue:false)` |
| 查询 | `OpenSubKey` + `GetValue(ValueName) is string`（**只判断存在且为字符串，不校验路径/文件是否存在**） |
| 异常 | 全部吞掉（策略锁定注册表时保持原状态） |

### 11.2 消费方

- `SettingsWindow.cs:389`：某处强制 `SetEnabled(true, Environment.ProcessPath ?? "InkCanvas.exe")`（启动/安装路径逻辑）。
- `SettingsWindow.cs:429-430`：开关「开机时运行」读 `IsEnabled()`、写 `SetEnabled(v, Environment.ProcessPath ?? "InkCanvas.exe")`。

### 11.3 分类与映射

**drop-candidate**。HarmonyOS 三方应用没有“用户级开机自启”公开能力（**需在 API 26 文档核实**）；系统级自启需要企业 MDM/系统签名或特定后台任务，语义不等价。建议在 harmony 侧移除该开关，并在移植说明中记录“平台不支持”。

> 注意：即使保留，HarmonyOS 的 `Environment.ProcessPath` 概念也不存在，需应用包名/Ability 名，不能 1:1。

---

## 12. 外部程序启动（`SoftwareLauncher`）

文件：`src/InkCanvas.Platform/System/SoftwareLauncher.cs`（70 行，**未跟踪新文件**）。

### 12.1 `LaunchEasiCamera(softwareName = "希沃视频展台")`

1. `FindEasiCameraExecutablePath(name)`；为空或文件不存在 → `false`。
2. `Process.Start(new ProcessStartInfo(exe){ UseShellExecute = true })`；异常 → `false`。
3. 返回是否成功。

### 12.2 `FindEasiCameraExecutablePath` 精确搜索顺序

```
UninstallPath = Software\Microsoft\Windows\CurrentVersion\Uninstall
for view in [Registry64, Registry32]:
    root = RegistryKey.OpenBaseKey(LocalMachine, view)
    uninstall = root.OpenSubKey(UninstallPath)
    if null: continue
    for name in uninstall.GetSubKeyNames():
        item = uninstall.OpenSubKey(name)
        display = item.GetValue("DisplayName") as string
        if display == null || !display.Contains(softwareName, OrdinalIgnoreCase): continue

        // 首选：InstallLocation 目录下的 sweclauncher.exe
        install = item.GetValue("InstallLocation") as string
        if install 非空:
            candidate = Path.Combine(install, "sweclauncher.exe")
            if File.Exists(candidate): return candidate

        // 回退：UninstallString 最后一段目录下的 sweclauncher\sweclauncher.exe
        uninstallString = item.GetValue("UninstallString") as string
        if 空: continue
        slash = uninstallString.LastIndexOf('\\')
        if slash < 0: continue
        folder = uninstallString[..slash]
        fallback = Path.Combine(folder, "sweclauncher", "sweclauncher.exe")
        if File.Exists(fallback): return fallback
    // 每个 view 的异常吞掉
return null
```

要点：
- 同时查 **64 位与 32 位**注册表视图（厂商安装器两种都出现过）。
- 匹配是 `DisplayName` 的**不分大小写包含**，不是精确相等。
- 两种候选路径的差异要保留：安装目录直下 vs `sweclauncher\` 子目录。
- 只读注册表 + `File.Exists`；**不启动/不杀进程**。

### 12.3 `LaunchDesmos()`

```
Process.Start(new ProcessStartInfo("https://www.desmos.com/calculator?lang=zh-CN")
              { UseShellExecute = true })
```
无返回值、无 try/catch（调用方 BoardWindow 直接调用）。

### 12.4 消费方

`BoardWindow` 底部启动器行（`BoardWindow.cs:674-688`）：
- 「视频展台」按钮：先 `ToggleBoard()` 退出画板，`await Task.Delay(120)` 后启动；失败显示「未找到希沃视频展台安装路径，或启动器无法运行。」。
- 「Desmos」按钮：退出画板后 `LaunchDesmos()`。
- 图标码点（Segoe Fluent Icons）：视频展台 `U+E714`，Desmos `U+E8EF`。
- 分隔线 1×25 DIP、左右 margin 4 DIP、透明度 0.6。

### 12.5 分类与映射

- 注册表发现 + 启动 Win32 exe：**platform-bound**；HarmonyOS 无注册表、无希沃 Windows 版展台 → **建议 drop**。
- Desmos：**adapt**；HarmonyOS 用 `startAbility` 打开浏览器（Want action `ohos.want.action.viewData` + URI），或应用内 Web 组件。
- 若产品需要“视频展台”，HarmonyOS 侧应替换为系统相机/扫码 Kit 的等价业务，不是移植此启动器。

---

## 13. 窗口枚举（`WindowEnumerator`）— 死代码

文件：`src/InkCanvas.Platform/System/EdgeGestureService.cs:78-134`。

### 13.1 行为

| 方法 | 精确语义 |
|---|---|
| `VisibleWindowTitles()` | `EnumWindows`；仅 `IsWindowVisible`；`GetWindowTextW` 到 512 字符缓冲；非空标题加入列表 |
| `WindowExists(title, matchFullName=true)` | 空标题→false；`matchFullName` 时 `t == title`（**区分大小写**）；否则 `t.Contains(title, OrdinalIgnoreCase)` |
| `IsWindowMinimised(title)` | 枚举；标题非空且 `Contains(title, OrdinalIgnoreCase)` 且 `IsIconic` → true 并提前停止；**不检查可见性** |

### 13.2 消费方与分类

- **全仓库无调用方**（`grep WindowEnumerator` 仅命中自身）。它是上游 `Helpers/WinTabWindowsChecker.cs`（WPF UIAutomation）的替代占位。
- **drop-candidate**：无使用 + HarmonyOS 三方应用无法枚举其他应用窗口。若未来需要“检测某应用是否运行”，应使用 HarmonyOS 的公开能力（若有）或产品级集成，而不是移植此 API。

---

## 14. DPI 语义（PerMonitorV2）

### 14.1 精确事实

| 项 | 值/行为 |
|---|---|
| 进程 DPI 感知 | `app.manifest`：`PerMonitorV2` + 兼容 `dpiAware=true` |
| `GetDpiScale(window)` | `GetDpiForWindow(hwnd)`；`0 → 1.0`；否则 `dpi / 96.0` |
| 覆盖层 `DpiScale` | 构造期一次计算并只读；不随窗口移动/显示变化 |
| 坐标 | 覆盖层：物理屏幕 px − 窗口原点，再 ÷ DpiScale → DIP |
| 窗口放置 | `AppWindow` 期望物理像素；`PlaceDip` 用该窗口自己的 DPI 换算 |
| 构造期 DPI=0 问题 | HWND 未关联显示器时 `GetDpiForWindow=0`；DIP 当物理像素用导致 125%/150% 裁切 |
| 修复 | 记录 DIP 放置；首次 `Activated`（非 Deactivated）后延迟一个 dispatcher tick 重放；`MoveAndResize` 在 Activated 回调内会抛异常 |
| island 对齐 | `PlaceCore` 做 `(x+1,y,w,h)` → `(x,y,w,h)` 的 1px 往返 resync |
| 拖动阈值 | **8 DIP**（`threshold = 8 * GetDpiScale`；平方距离比较），定时器 **16 ms** 轮询 `GetCursorPos` |
| 标题栏 | 高 **32 DIP**，标题字号 **12**，标题左右 margin 12/8 |
| caption 按钮 | 最小 **46×32 DIP**，padding 0，无圆角；图标字号 10（最小化/最大化）、12（关闭） |
| 关闭按钮 hover | 背景 `#E81123`（232,17,35），前景白 |
| 圆角 | `DWMWA_WINDOW_CORNER_PREFERENCE=33` → `DWMWCP_ROUND=2`；`DWMWA_NCRENDERING_POLICY=2` → `DWMNCRP_DISABLED=1`；`DWMWA_BORDER_COLOR=34` → `0xFFFFFFFE`（DWMWA_COLOR_NONE，避免白圈） |
| 窗口裁剪 | 按 XAML 卡片矩形 `CreateRoundRectRgn`，右/下各 +1 px，圆角半径 = `CornerRadius * 视图缩放 * dpi` |
| 弹出定位 | 水平居中于锚点并夹到工作区；优先锚点上方 `y = anchor.Y - height - gap`，放不下则下方；gap 调用方传 `ceil(8 * dpi)` |
| 中心放置 | `WorkArea` 居中，尺寸 `ceil(DIP * scale)` |

### 14.2 分类与映射

**adapt**。HarmonyOS 用 vp/vp2px 与窗口 density；建议统一“逻辑坐标 = vp”，避免再现 Win32 的物理/DIP 双坐标。注意：
- HarmonyOS 的 `densityPixels` 与 Windows 125%/150% 不是同一概念，不能直接套 `dpi/96`。
- 多设备（平板/2in1/手机）与分屏窗口需要按当前窗口的可用区域计算，而不是主屏。
- 覆盖层若改为应用内 XComponent，其尺寸/密度由 ArkUI 布局给出，天然是 vp。

---

## 15. GDI 截屏（`NativeMethods` 的消费方）

文件：`src/InkCanvas.Features/Tools/Tools.cs:126-176`（不在 Platform 目录，但完全依赖 `NativeMethods`）。

### 15.1 `ScreenshotService.Capture(x,y,w,h)`

```
if w<=0 || h<=0: return empty
screenDc = GetDC(NULL)
memDc    = CreateCompatibleDC(screenDc)
bmp      = CreateCompatibleBitmap(screenDc, w, h)
old      = SelectObject(memDc, bmp)
BitBlt(memDc, 0,0,w,h, screenDc, x,y, SRCCOPY = 0x00CC0020)
BITMAPINFO{ width=w, height=-h（top-down）, planes=1, bitCount=32, compression=0 }
pixels = new byte[w*h*4]
GetDIBits(memDc, bmp, 0, h, pixels, ref bi, 0)
SelectObject(memDc, old); DeleteObject(bmp); DeleteDC(memDc); ReleaseDC(NULL, screenDc)
return (pixels /* BGRA */, w, h)
```

- `SavePngAsync` 用 Win2D `CanvasBitmap.CreateFromBytes(B8G8R8A8UIntNormalized)` + `SaveAsync(Png)`。
- `CaptureToFileAsync` 文件名为 `Screenshot-{HHmmss}.png`，可选 `yyyy-MM-dd` 日期子目录。
- 消费方：截图热键 Alt+C、鼠标模式自动截图、PPT 截图请求。

### 15.2 分类与映射

**platform-bound**（GDI）。HarmonyOS 候选（需核实）：应用自身内容的 `componentSnapshot`/`window.snapshot` 可行；**全屏（含其他应用）截图**受隐私限制，可能需要 `@ohos.screenshot` 系统权限或不可用。移植时应把“截图”降级为“当前画布/窗口截图”。

---

## 16. 窗口 Z 序契约（App 层，但由 Platform 窗口驱动）

文件：`src/InkCanvas.App/Services/WindowChrome.cs:479-504`。

### 16.1 `EnsureAbove(window, overlay)`

```
hwnd = GetHwnd(window)
if hwnd==0 || !IsWindowVisible(hwnd): return
if overlay != 0:
    if !IsWindowVisible(overlay): return
    // 只有覆盖层实际压在该窗口之上时才修 Z 序，避免每轮都抬升
    belowOverlay = false
    for (above = GetWindow(hwnd, GW_HWNDPREV=3); above != 0; above = GetWindow(above, 3)):
        if above == overlay: belowOverlay = true; break
    if !belowOverlay: return
SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0,
             SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)
```

- 不激活、不改尺寸/位置；只修 topmost 带内顺序。
- 保留 IME 候选等更高窗口（因为它们会出现在 `GW_HWNDPREV` 链里，但只在覆盖层压住本窗口时修）。
- 调用时机：每次覆盖层输入（`InputBegan` → `RaiseChromeAboveOverlay`）、窗口创建/放置后、维护轮询（900/200 ms）。
- `SelfTest.CheckPopupOrder` 钉住“维护轮询不破坏候选弹窗 Z 序”。

### 16.2 分类与映射

**adapt**。HarmonyOS 应用内窗口（subWindow）天然有层级；`createSubWindow` 的子窗在主窗之上，焦点/Z 序由窗口管理器管理。若保留“全屏画布 + 多个工具窗”，需要验证 subWindow 的置顶与失焦行为；跨应用置顶不可用。建议把“Z 序重挂”做成窗口管理适配层，而不是照搬 `GetWindow(GW_HWNDPREV)`。

---

## 17. 输入诊断与自检

### 17.1 延迟统计（`OverlayInkPresenter`）

- `NoteInput(p)` 在每次 Down/Move/Up 记录 `_lastInputTimeMs = p.TimeMs`、`_lastInputId/_lastInputKind`。
- `SampleLatency()`：
  `delta = unchecked((uint)Environment.TickCount - _lastInputTimeMs)`；
  `delta > 2000` 丢弃（合成时间戳/回绕）；
  `Last/Max/Total/Samples` 更新；`LatencyLogEnabled` 时写 `FileLog`：
  `ink latency: pointer {id} {kind} dwTime->present {delta} ms`。
- `--diag=<path>`：700 ms 周期写覆盖层计数器与工具状态（`App.xaml.cs:44-65`）。

### 17.2 `--selftest` 中与 Platform 相关的检查

| 检查 | 内容 |
|---|---|
| `edge gesture property applied` | `SetEdgeGesturesDisabled(handle,true)` 返回值，再设回 false |
| `gpu readback stays well below full-screen-per-frame` | 依赖 `Presenter.PresentedFrames/GpuReadbackBytes`（渲染层，非 Platform） |
| `maintenance preserves candidate popup Z order` | `GetWindow(bar, GW_HWNDPREV=3)` 链中是否出现测试弹窗 |
| `edge button unfolds across maintenance poll` | `IsWindowVisible(bar hwnd)`（NativeMethods） |
| `floating bar buttons fit client area` | 纯 UI 布局 |

SelfTest 的笔迹检查使用**合成 `OverlayPointer`** 直接调用 `InkController`，不经过真实 `LayeredInkWindow` 消息泵；因此它不能证明 Win32 指针采集本身正确。

### 17.3 测试资产现状

| 测试 | 数量 | 状态 |
|---|---|---|
| `PalmRejectorTests` | 9 Fact | 存在，本轮未运行 |
| `PixelCompositingTests` | 7 Fact | 存在，本轮未运行；钉住预乘合成公式 |
| `TouchContactClassifier` | 0 | **无测试**（源码注释明确“未验证”） |
| Platform 窗口/热键/屏幕/边缘手势 | 0 | 无单元测试 |
| `SelfTest` 运行时 | — | 需启动应用；本轮未运行 |

---

## 18. 预乘 alpha 管线（Platform 的像素契约 + Core 的合成公式）

虽然分层窗口本身只负责“上传预乘 BGRA”，完整管线跨越 Presenter 与 Core。移植时必须保持公式与像素格式。

### 18.1 Presenter 的精确流程（`OverlayInkPresenter`）

1. `CanvasDevice.GetSharedDevice()`；提交层 `CanvasRenderTarget`：`B8G8R8A8UIntNormalized` + `CanvasAlphaMode.Premultiplied`，DIP 96。
2. `OverlayTransform = _controller.ViewTransform * Matrix3x2.CreateScale(_window.DpiScale)`（笔迹坐标是 DIP，目标是物理像素）。
3. 提交层失效时：
   `session.Clear(ARGB(1,1,1,1))` → `Scene.DrawCommitted(...)` → `GetPixelBytes` 到 CPU 缓存（`GpuReadbackBytes += W*H*4`）。
4. 活动层（live）：只渲染**活动笔画包围盒**：
   - 包围盒 = 所有 live/shape preview 的 world bounds 并集 → 4 个角变换到屏幕 → 取 min/max → 四周各减/加 **4 px** → 夹到 `[0, Width/Height]`。
   - 容量按 **64 px** 粒度增长（`(n + 63) & ~63`），避免每帧建 GPU 资源。
   - live target 清 `ARGB(0,0,0,0)`，变换 = `OverlayTransform * Translate(-region.X, -region.Y)`。
   - 只回读 region 大小（`GetPixelBytes(..., region.Width, region.Height)`），`GpuReadbackBytes += region.W*region.H*4`。
5. 合成：`PixelCompositing.BlendOver(frame, W,H, region.X, region.Y, livePixels, region.W, region.H)`。
6. 只把“上一活动框 ∪ 当前活动框”写回 DIB（`WriteRegionToDib`，整行时用一次 `Marshal.Copy`）。
7. 隐藏墨迹（鼠标模式）：`_frame` 每 4 字节写 `1`（BGRA=1,1,1,1），整帧拷进 DIB，只做一次。
8. `PresentFrame()`：
   - 若 `_inputDriven && _window.HasQueuedPointerMessage()` → **跳过本次上传**（更新的指针消息会呈现最新状态；不增加延迟，只在消息泵落后于数字化仪时丢陈旧帧）。
   - 否则 `_window.Present()`；`PresentedFrames++`；采样延迟。
9. 重入合并：`RequestRender` 用 `_rendering/_pending` 双循环；渲染期间再次失效只标记 pending，循环一次，不递归。

### 18.2 预乘“source over”公式（`PixelCompositing.BlendOver`，已钉住）

```
out = src + dst * (1 - srcA)        // 对 B,G,R,A 四个通道都适用
逐字节整数实现：
  if srcA == 0:   continue（不动 dst）
  if srcA == 255: dst = src（含 alpha=255）
  else:
    inv = 255 - srcA
    dst.b = src.b + dst.b * inv / 255
    dst.g = src.g + dst.g * inv / 255
    dst.r = src.r + dst.r * inv / 255
    dst.a = src.a + dst.a * inv / 255
```

边界：源/目标尺寸 ≤0 或缓冲长度不足 → no-op；源超出目标/负偏移会裁剪。

### 18.3 分类与映射

- 合成公式 **pure-logic**（已有 7 个测试钉住）；像素格式契约 **adapt**。
- 分层上传 **platform-bound**。HarmonyOS 侧若用 GPU 直接渲染到 XComponent surface，可完全去掉 CPU readback 与手工合成；若必须 CPU 合成，沿用上述公式与 BGRA 预乘约定。

---

## 19. `NativeMethods` 全量清单（Windows API 依赖）

文件：`src/InkCanvas.Platform/Interop/NativeMethods.cs`（215 行）。

### 19.1 常量/结构

- 窗口样式：`GWL_STYLE=-16`、`GWL_EXSTYLE=-20`、`WS_POPUP=0x80000000`、`WS_VISIBLE=0x10000000`、`WS_EX_LAYERED=0x00080000`、`WS_EX_TRANSPARENT=0x20`、`WS_EX_TOOLWINDOW=0x80`、`WS_EX_TOPMOST=0x8`、`WS_EX_NOACTIVATE=0x08000000`。
- 窗口命令：`SW_SHOWNOACTIVATE=4`、`SW_HIDE=0`、`ULW_ALPHA=2`、`HWND_TOPMOST=-1`、`HWND_NOTOPMOST=-2`、`SWP_NOSIZE=1`、`SWP_NOMOVE=2`、`SWP_NOACTIVATE=0x10`、`SWP_SHOWWINDOW=0x40`。
- 消息：`WM_DESTROY=0x0002`、`WM_NCLBUTTONDOWN=0x00A1`、`HTCAPTION=2`、`WM_MOUSEMOVE=0x0200`、`WM_LBUTTONDOWN=0x0201`、`WM_LBUTTONUP=0x0202`、`WM_POINTERUPDATE=0x0245`、`WM_POINTERDOWN=0x0246`、`WM_POINTERUP=0x0247`、`WM_POINTERCAPTURECHANGED=0x024F`、`WM_HOTKEY=0x0312`、`WM_DISPLAYCHANGE=0x007E`。
- 结构：`POINT`、`SIZE`、`BLENDFUNCTION`、`BITMAPINFO`、`WNDCLASS`、`MSG`、`POINTER_INFO`、`POINTER_PEN_INFO`、`POINTER_TOUCH_INFO`、`RECT`。
- 指针类型：`POINTER_INPUT_TYPE_TOUCH=2`、`POINTER_INPUT_TYPE_PEN=3`。
- 系统度量：`SM_CXSCREEN=0`、`SM_CYSCREEN=1`、`SM_XVIRTUALSCREEN=76`、`SM_YVIRTUALSCREEN=77`、`SM_CXVIRTUALSCREEN=78`、`SM_CYVIRTUALSCREEN=79`、`SPI_GETWORKAREA=0x0030`。
- 辅助：`AddExStyle/RemoveExStyle/HasExStyle`（Get/SetWindowLongW ± 位或）。

### 19.2 P/Invoke 分组

| DLL | API |
|---|---|
| user32 | `GetPointerInfo`, `GetPointerPenInfo`, `GetPointerTouchInfo`, `EnableMouseInPointer`, `IsMouseInPointerEnabled`, `SetCapture`, `ReleaseCapture`, `GetCapture`, `GetCursorPos`, `RegisterClassW`, `CreateWindowExW`, `DefWindowProcW`, `DestroyWindow`, `ShowWindow`, `UpdateLayeredWindow`, `GetWindowLongW`, `SetWindowLongW`, `SetWindowPos`, `MoveWindow`, `GetForegroundWindow`, `GetWindow`, `IsWindowVisible`, `GetWindowThreadProcessId`, `SetForegroundWindow`, `GetDpiForWindow`, `SendMessageW`, `GetSystemMetrics`, `GetMessageW`, `TranslateMessage`, `DispatchMessageW`, `PostQuitMessage`, `RegisterHotKey`, `UnregisterHotKey`, `ScreenToClient`, `GetDC`, `ReleaseDC` |
| gdi32 | `CreateCompatibleDC`, `CreateDIBSection`, `SelectObject`, `DeleteObject`, `DeleteDC`, `BitBlt`, `GetDIBits`, `CreateCompatibleBitmap` |
| shell32 | `SHGetPropertyStoreForWindow`（`EdgeGestureService`） |
| dwmapi | `DwmSetWindowAttribute`（`WindowChrome`，App 层） |
| 注册表（托管） | `Microsoft.Win32.Registry`（自启、启动器） |
| 进程（托管） | `System.Diagnostics.Process`（前台进程名、启动外部程序） |

`LayeredInkWindow` 另有私有 DllImport：`RegisterTouchWindow`、`SetWindowFeedbackSetting`、`PeekMessageW`。
`WindowEnumerator` 另有私有 DllImport：`EnumWindows`、`GetWindowTextW`、`IsWindowVisible`、`IsIconic`。

### 19.3 未使用/遗留成员（可删，减少移植噪音）

静态分析（工作树源码，排除自身与注释）显示以下声明**没有实际调用**：
`DispatchMessageW`、`GetMessageW`、`TranslateMessage`、`PostQuitMessage`、`MoveWindow`、`ScreenToClient`、`SendMessageW`、`SetForegroundWindow`、`WM_DISPLAYCHANGE`、`SWP_SHOWWINDOW`、`WS_VISIBLE`、`GWL_STYLE`。
`WM_NCLBUTTONDOWN`/`HTCAPTION` 仅出现在注释中（见 §20 文档不一致）。

> 注意：`GetWindowLongW/SetWindowLongW` 虽无直接外部调用，但被 `AddExStyle/RemoveExStyle/HasExStyle` 内部使用，不能删。

### 19.4 分类

**platform-bound**。HarmonyOS 侧不存在 1:1 替代；应把“能力”而非“API”搬到 `harmony/platform` compat 层（按 CLAUDE.md 的契约），并在 `parity/api-matrix.md` 记录 API 24/26 差异。

---

## 20. 与现有文档的不一致（必须记录）

1. **拖动实现**：`docs/PORTING-NOTES.md:40` 仍写“浮动栏背景发 `WM_NCLBUTTONDOWN/HTCAPTION` 使用系统原生拖动循环”。当前工作树 `WindowChrome.EnableDragMove` 的注释明确说 WinUI island 的 WndProc 不跑原生 move loop，实际实现是 **16 ms 定时器 + `GetCursorPos` 物理像素增量 + 8 DIP 阈值**；`HTCAPTION` 只在注释里。移植应以代码为准，文档需更正。
2. **`WindowEnumerator` 状态**：PORTING-NOTES 把它列为已移植的 `WinTabWindowsChecker` 替代，但它**没有任何调用方**。若不需要“窗口存在检测”，应标为死代码/放弃。
3. **自动折叠覆盖项**：设置里有 `isAutoFoldInEasiNote3C/HiteCamera/WxBoardMain/OldZyBoard`，`ApplyAutoFold` 未使用（§10.3）。
4. **`ApplyAutoFold` 的 `IsEnableAutoFold`**：派生属性包含全部项，但判定手写 6 项，后续增项容易漏。

---

## 21. 跨切面常量速查（移植对照表）

| 类别 | 常量 | 值 | 出处 |
|---|---|---|---|
| 覆盖层 | 类名 | `InkCanvasOverlayWindow` | LayeredInkWindow:34 |
| 覆盖层 | 窗口大小 | 主屏 `SM_CXSCREEN × SM_CYSCREEN` | OverlayHost:17-18 |
| 覆盖层 | ex-style | `0x00080000\|0x80\|0x8\|0x08000000` | LayeredInkWindow:116-117 |
| 覆盖层 | DIB | 32bpp、top-down、BI_RGB | LayeredInkWindow:140-149 |
| 覆盖层 | blend | `AC_SRC_OVER=0, AC_SRC_ALPHA=1, SCA=255, ULW_ALPHA=2` | LayeredInkWindow:261-270 |
| 覆盖层 | 隐藏帧 | BGRA `(1,1,1,1)` | OverlayInkPresenter:215-218 |
| 输入 | 指针 id | `wParam & 0xFFFF` | LayeredInkWindow:291,357 |
| 输入 | 压力 | `p==0 → 0.5`，否则 `clamp(p/1024,0,1)` | LayeredInkWindow:410,420 |
| 输入 | 取消标志 | `CANCELED=0x8000`、`INCONTACT=0x0004` | LayeredInkWindow:377-378 |
| 输入 | 鼠标回退 id/压力 | `1 / 0.5` | LayeredInkWindow:338-339 |
| 触摸 | TWF | `FINETOUCH=1 \| WANTPALM=2` | LayeredInkWindow:58-59,135 |
| 触摸 | 反馈 kind | `1,3,4,5,6,7,8,9,10,11` 全部 0 | LayeredInkWindow:158-166 |
| 手掌 | 阈值 | `>= max(cw,ch)`；默认 30 DIP；接线 8..50 | PalmRejector:20,45; AppServices:69 |
| 手掌 | 笔优先 | `true` | PalmRejector:23 |
| 分档 | 默认 | special=false, quad=false, mult=0.25, nib=10, finger=30, thr=2.5, eraser=0.8 | AppSettings:266-280 |
| 分档 | 推荐 | special=true, mult=0.3, nib=5, finger=20 | AppSettings:40-47 |
| 分档 | 判定 | `cw > bounds`；`cw > bounds*thr`；直径 `= cw*eraser(*mult)` | TouchContactClassifier:81-100 |
| 分档 | 线条擦半径 | `2.5` DIP | InkController:238 |
| 分档 | 手指翻页 | `|dx| > 60` DIP，左滑→下一页 | InkController:347-348 |
| 热键 | id 基址 | `0xB100 = 45312` | HotkeyService:24 |
| 热键 | NoRepeat | `0x4000` 总是 OR | HotkeyService:34 |
| 边缘手势 | 属性 | fmtid `32CE38B2-…-B3784394AA44`, pid `2`, VT_BOOL=11, true=-1 | EdgeGestureService:18-21,62 |
| 屏幕 | SM_* | 0/1/76/77/78/79；SPI_GETWORKAREA=0x0030 | NativeMethods:195-202 |
| DPI | 基线 | `dpi/96`；PerMonitorV2 | WindowChrome:199-207; app.manifest:8 |
| DPI | 拖动阈值 | 8 DIP，16 ms 轮询 | WindowChrome:245-255 |
| 窗口 | 标题栏 | 32 DIP；字号 12；按钮 46×32 | WindowChrome:337-338,360-361 |
| 窗口 | 关闭 hover | `#E81123` | WindowChrome:430 |
| 窗口 | popup gap | 8 DIP（乘 DPI） | SelfTest:404 |
| 布局 | 浮动栏底边距 | 默认 100 DIP | AppSettings:58,202 |
| 布局 | 折叠把手 | 30×50 DIP，露出 14 DIP，垂直居中 | FloatingBarWindow:123-130 |
| 轮询 | Shell | 空闲 900 ms；放映 200 ms | ShellController:81,325-327 |
| 轮询 | 弹窗关闭 | 300 ms | BarPanelWindow:89 |
| 时延 | 丢弃阈值 | `delta > 2000 ms` | OverlayInkPresenter:298 |
| 时延 | live 边距 | 4 px；容量粒度 64 px | OverlayInkPresenter:354-356,402-403 |

---

## 22. JSON 字段名（平台相关设置，`.icink` 不涉及 Platform）

Platform 层**不直接序列化**任何文件。与其行为相关的 `Settings.json` 键名（两侧必须一致）：

`advanced.isSpecialScreen`、`advanced.isQuadIR`、`advanced.touchMultiplier`、`advanced.nibModeBoundsWidth`、`advanced.fingerModeBoundsWidth`、`advanced.nibModeBoundsWidthThresholdValue`、`advanced.fingerModeBoundsWidthThresholdValue`、`advanced.nibModeBoundsWidthEraserSize`、`advanced.fingerModeBoundsWidthEraserSize`、`advanced.isEnableEdgeGestureUtil`、`advanced.isLogEnabled`、`advanced.isSecondConfimeWhenShutdownApp`、
`startup.isEnableNibMode`、
`behavior.isEnableFingerGestureSlideShowControl`、`behavior.isSupportWPS`、
`automation.isAutoFoldInEasiNote`、`isAutoFoldInEasiNoteIgnoreDesktopAnno`、`isAutoFoldInEasiCamera`、`isAutoFoldInEasiNote3C`、`isAutoFoldInSeewoPincoTeacher`、`isAutoFoldInHiteTouchPro`、`isAutoFoldInHiteCamera`、`isAutoFoldInWxBoardMain`、`isAutoFoldInOldZyBoard`、`isAutoFoldInMSWhiteboard`、`isAutoFoldInPPTSlideShow`、`isEnableAutoFold`（派生，不序列化）、`minimumAutomationStrokeNumber`、
`appearance.floatingBarBottomMargin`、`appearance.enableViewboxFloatingBarScaleTransform`、`appearance.isEnableDisPlayFloatBarText`。

> `isEnableAutoFold` 是 `[JsonIgnore]` 派生属性，不要写入 JSON。

---

## 23. UI 度量（平台/窗口相关，DIP）

| 项 | 值 |
|---|---|
| 覆盖层 | 主屏全幅；无边框；无任务栏按钮（TOOLWINDOW） |
| 浮动栏底边距 | 100 DIP（可设置） |
| 浮动栏折叠把手 | 30×50 DIP，露出 14 DIP，工作区垂直居中 |
| 标题栏 | 32 DIP 高；标题 12 pt；左 margin 12、右 8 |
| caption 按钮 | 46×32 DIP；图标 10（最小/最大）、12（关闭） |
| 关闭 hover | 背景 `#E81123`，前景白 |
| 拖动启动 | 8 DIP（欧氏距离平方比较） |
| 拖动轮询 | 16 ms |
| 弹窗锚点间隙 | 8 DIP × DPI（调用方取整） |
| 弹窗关闭轮询 | 300 ms |
| 折叠/自动折叠轮询 | 900 ms（放映 200 ms） |
| 截图文件名 | `Screenshot-HHmmss.png`；可选日期目录 |
| 图标码点（画板启动器） | 视频展台 `U+E714`；Desmos `U+E8EF`（Segoe Fluent Icons） |

> 触摸目标 ≥40 DIP、浮条 55 DIP、设置面板 440 DIP 等属于 Controls/App 度量，不在 Platform 文件内，但 CLAUDE.md 要求两侧同值；移植 Platform 时不要引入新的窗口边距常量。

---

## 24. 测试与验证边界（诚实声明）

**已存在、本轮未运行：**
- `PalmRejectorTests`：9 Fact（§4.3）。
- `PixelCompositingTests`：7 Fact（§18.2）。
- `SelfTest`（`--selftest`）：运行时检查，含边缘手势属性、Z 序、窗口可见性；本轮未启动。
- `--diag`：运行时延迟/计数器输出；本轮未启动。

**无测试：**
- `TouchContactClassifier`（源码明确“未验证”，需真实红外屏复核）。
- `LayeredInkWindow` 指针生命周期（3.4.0 新状态机）。
- `HotkeyService`、`ScreenService`、`ForegroundWindowService`、`AutoStartService`、`SoftwareLauncher`、`EdgeGestureService`、`WindowEnumerator`。

**源码注释/文档声称的验证（未在本轮复现）：**
- 边缘手势属性可写入（PORTING-NOTES:74）。
- 真机（Windows Server 2025、125% DPI）窗口/拖动/模式往返/菜单/124 项单测通过（CHANGELOG，较早轮次）。
- 触摸硬件、压感笔、Win11 亚克力、Office/WPS 实机仍待复核（CHANGELOG:33）。

**未验证（HarmonyOS）：** 一切。本轮无 SDK、无设备、无 HAP 构建。

---

## 25. 移植分类汇总

| 功能 id | 功能 | 分类 | 理由 / 下游动作 |
|---|---|---|---|
| `overlay-layered-window` | 分层透明覆盖窗、全屏、置顶、显示/隐藏、诊断计数 | **platform-bound** | Win32 分层窗口；HarmonyOS 无跨应用覆盖。需产品决策：应用内 XComponent 或放弃覆盖。 |
| `overlay-pointer-input` | WM_POINTER → OverlayPointer（id/坐标/压力/接触/时间） | **adapt** | 公式可 1:1；输入源换成 ArkUI PointerEvent，需核实接触尺寸单位。 |
| `pointer-lifecycle-capture` | down/move/up/cancel/capture 状态机、去重、鼠标回退 | **adapt** | 状态机语义可保留；捕获/取消映射到 ArkUI 指针生命周期。 |
| `click-through-topmost` | `WS_EX_TRANSPARENT` / `WS_EX_TOPMOST` 切换 | **platform-bound** | 跨应用穿透/置顶无等价。 |
| `premultiplied-alpha-presenter` | 预乘 BGRA 合成、脏区回写、隐藏帧、present 合并 | **platform-bound**（合成公式 pure-logic） | `BlendOver` 可 1:1；上传层改为 GPU/XComponent。 |
| `present-coalescing` | 有新指针消息时跳过陈旧 present | **adapt**（规则 pure-logic） | 规则可保留；队列查询接口由平台实现。 |
| `palm-rejection` | 手掌阈值/状态机/笔优先/掌擦 | **pure-logic** | 可 1:1；前置：接触宽高输入。 |
| `touch-contact-classification` | QuadIR/特殊屏/笔尖-手指分档 | **pure-logic** | 可 1:1；建议黄金向量。 |
| `touch-input-buffering` | TWF_FINETOUCH/TWF_WANTPALM | **platform-bound** | 建议 drop（系统层概念）。 |
| `system-input-feedback` | 接触视觉/按住圆环抑制 | **platform-bound** | 建议 drop（自绘 UI 无此系统反馈）。 |
| `global-hotkeys` | RegisterHotKey 全局键位表 | **platform-bound** | 三方全局热键不可得；降级应用内快捷键。 |
| `edge-gesture-suppression` | 全屏禁用边缘手势 | **platform-bound** | 无逐窗口等价；候选 immersive/系统栏控制，需核实；否则 drop。 |
| `screen-metrics` | 主屏/虚拟屏/工作区 | **adapt** | display + avoidArea；统一 vp。 |
| `foreground-app-query` | 前台进程名、自动折叠、弹窗外点关闭 | **platform-bound** | 三方不可得；自动折叠 drop；弹窗改失焦事件。 |
| `auto-start-registration` | HKCU Run 自启 | **drop-candidate** | HarmonyOS 无用户级自启；移除开关。 |
| `external-app-launcher` | 希沃展台注册表发现/启动；Desmos URL | **platform-bound**（注册表）/ **adapt**（URL） | 展台 drop；Desmos 用浏览器 Want。 |
| `window-enumeration` | EnumWindows 标题/最小化检测 | **drop-candidate** | 无调用方 + 不可实现。 |
| `dpi-scaling` | PerMonitorV2、GetDpiForWindow、DIP/px 换算、首激活重放 | **adapt** | 用 vp/density，避免双坐标；多设备按窗口可用区。 |
| `gdi-screen-capture` | BitBlt/GetDIBits 截屏 → PNG | **platform-bound**（可 adapt 为画布截图） | 全屏截图受隐私限制；降级当前窗口/画布。 |
| `window-zorder-contract` | EnsureAbove / InputBegan 重挂 / 弹窗保留 | **adapt** | 应用内 subWindow 层级；跨应用不可用。 |
| `input-diagnostics-selftest` | 计数器、延迟统计、--diag/--selftest | **adapt** | 统计逻辑可保留；时间源/日志接口替换。 |
| `native-interop-surface` | `NativeMethods` 98 项 | **platform-bound** | 不迁移；按能力重建 compat 层，删死成员。 |

---

## 26. 黄金向量建议（parity/vectors）

按 CLAUDE.md 契约，纯逻辑迁移必须两侧消费同一 JSON。当前仓库**没有 `parity/` 目录**（`parity/manifest.json`、`parity/vectors/` 均不存在；需先建）。建议本轮优先为 Platform 的纯逻辑建立：

1. `palm-rejection.json`
   - 用例：触摸接触 8/10/29.99/30/30.01/45/50 DIP；笔优先开/关；掌 down→up；`Reset`；多笔 down/up 顺序；笔+掌同时。
   - 字段：`threshold`、`penHasPriority`、事件序列 `[{op:down/up, id, kind, cw, ch}]`、期望 `shouldDraw[]`/`shouldErase[]`/`isPenActive`。
2. `touch-classification.json`
   - 维度：`isSpecialScreen` × `isQuadIR` × `nibMode` × `forceEraser` × `pptGestureEligible`；接触 `w/h` 网格（含 `w != h` 验证几何平均）；`touchMultiplier` 0/0.25/0.3；阈值边界 `cw == bounds`、`cw == bounds*thr`。
   - 期望：`intent` + `eraserDiameter`（double，容差 1e-9）。
3. `hotkey-table.json`
   - 纯数据：`[{modifiers, vk, actionId}]`，钉住 Alt=1/Control=2/Shift=4/Win=8/NoRepeat=0x4000 与键位表；ArkTS 侧断言映射一致。
4. `premultiplied-blend.json`（可复用现有 PixelCompositing 测试的用例）
   - 输入/输出 BGRA 缓冲、偏移、裁剪；期望逐字节相等。
5. `screen-workarea.json`（可选）
   - 只钉“工作区 = 主屏 − 任务栏/避让区”的**语义**，不钉具体数值（平台相关）。

> 建议把 `TouchContactClassifier` 从 Core 保留为两侧共享纯逻辑；`PalmRejector` 的手掌状态机也下沉为纯逻辑（输入只是 `{id, kind, cw, ch}`），ArkTS 侧直接复用。

---

## 27. 开放问题（需要产品/架构决策）

1. **跨应用覆盖是否必须？** HarmonyOS 三方应用没有“覆盖任意前台应用/PPT”的公开能力。若课堂场景必须覆盖 PPT/WPS/浏览器，需要系统签名/企业 MDM/专有通道；否则整个“屏幕画笔覆盖层”应改为“本应用内全屏画布 + 分屏/投屏协作”。这决定 `overlay-layered-window`、`click-through-topmost`、`window-zorder-contract` 的最终形态。
2. **全局热键的替代方案**：API 26/24 的 `@ohos.multimodalInput.inputMonitor` 是否对三方应用开放？是否需要系统权限？若不可用，接受“仅前台窗口生效”的快捷键吗？
3. **接触矩形可用性**：ArkUI 指针事件是否提供触摸接触宽高（`TouchObject.width/height`）？单位是 px 还是 vp？如果没有，30 vp 的手掌阈值如何替代（压力/面积/笔优先）？需要在平板/2in1 真机实测。
4. **自动折叠/前台应用**：HarmonyOS 无法获取其他应用前台进程名。是否接受删除自动折叠（以及 `isAutoFold*` 全部设置）？PPT/WPS 互操作在 HarmonyOS 侧本身是否还有对应产品能力？
5. **自启/窗口枚举 drop**：`AutoStartService` 与 `WindowEnumerator` 是否确认放弃（无等价公开 API）？自启开关从设置 UI 移除还是保留禁用态？
6. **屏幕与多窗口**：2in1/平板分屏、自由窗口下，覆盖/画布应按主屏还是当前窗口可用区？`WorkArea` 的 HarmonyOS 避让区换算与多显示器行为需要真机确认。
7. **DPI 模型**：是否统一以 vp 为唯一逻辑坐标（推荐），彻底移除 Windows 式 `DpiScale`？若保留，需明确 vp 与 px 的换算只在一处发生。
8. **黄金向量目录**：是否本轮就建立 `parity/` 全套（manifest、vectors、runner）并把 `PalmRejector`/`TouchContactClassifier`/`HotkeyModifiers`/`PixelCompositing` 纳入？当前仓库没有这些文件，CLAUDE.md 描述的契约尚未落地。
