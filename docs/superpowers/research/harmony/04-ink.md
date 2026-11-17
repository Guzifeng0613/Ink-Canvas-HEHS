# 04 — 墨迹层（Ink）源码盘点

日期：2026-11-18
基线：`D:\InkCanvas` 工作树，分支 `feat/winui3-migration`，含未提交的 3.4.0 改动（以工作树为准）
范围：`src/InkCanvas.Ink/` 全部 + `src/InkCanvas.Controls/Controls/InkSurface.cs`，并追读其直接依赖的 Core/Platform 类型（数据模型、笔锋、轮廓、擦除、选区、手势、触摸分档、持久化）。
性质：只读源码盘点。**本报告未运行测试、未启动应用、未在 HarmonyOS 实机/模拟器验证**；文中「已验证」均引用仓库既有文档的声明，并保留其边界。下游移植以本报告的常量与步骤为准。

---

## 0. 文件清单与职责

| 文件（相对仓库根） | 行数 | 职责 |
|---|---:|---|
| `src/InkCanvas.Ink/InkController.cs` | 927 | 对外唯一墨迹入口：工具状态机、笔/触摸/手掌路由、双指手势、选区编辑、图形工具（含两步体）、面积/整笔擦除、墨迹转形状、撤销 |
| `src/InkCanvas.Ink/Manipulation/TwoFingerManipulator.cs` | 74 | 双指平移/缩放/旋转的纯数学增量 |
| `src/InkCanvas.Ink/Rendering/StrokeRenderer.cs` | 95 | 单笔 Win2D 绘制；已完成笔迹几何缓存 |
| `src/InkCanvas.Ink/Rendering/InkSceneRenderer.cs` | 97 | 场景分层绘制：已提交层 / 活动层 / 选区框 |
| `src/InkCanvas.Ink/Rendering/OverlayInkPresenter.cs` | 442 | 透明覆盖层：已提交层 CPU 缓存、活动笔画包围盒回读、DIB 局部拷贝、`UpdateLayeredWindow`、延迟统计 |
| `src/InkCanvas.Ink/InkCanvas.Ink.csproj` | 18 | net10.0-windows10.0.26100.0；Win2D + WindowsAppSDK；依赖 Core、Platform |
| `src/InkCanvas.Controls/Controls/InkSurface.cs` | 155 | WinUI 3 不透明画布控件（白板/回放）：`CanvasControl` + 指针翻译 + 同控制器/渲染器 |

直接依赖的 Core/Platform 文件（本报告必须覆盖其常量，因为它们决定墨迹行为）：

| 文件 | 职责 |
|---|---|
| `src/InkCanvas.Core/Ink/InkPoint.cs` | 单个采样点（x/y/压力/宽度/时间） |
| `src/InkCanvas.Core/Ink/InkStroke.cs` | 笔画、包围盒、变换、到中心线距离、`Rect` |
| `src/InkCanvas.Core/Ink/StrokeAttributes.cs` | 颜色/宽度/荧光笔/透明度/笔锋种类 |
| `src/InkCanvas.Core/Ink/InkColor.cs` | 平台无关 ARGB 颜色 |
| `src/InkCanvas.Core/Ink/InkDocument.cs` | 笔画集合、`Changed`、命中、整笔擦除 |
| `src/InkCanvas.Core/Ink/TaperProfile.cs` | 因果增量笔锋（None/Tail/Velocity） |
| `src/InkCanvas.Core/Ink/StrokeGeometry.cs` | 每段 capsule + 正向绕组的并集轮廓 |
| `src/InkCanvas.Core/Ink/StrokeEraser.cs` | EraseByPoint：圆盘切断中心线，返回碎片 |
| `src/InkCanvas.Core/Ink/MatrixTransformCenter.cs` | 手势枢轴选项解析 |
| `src/InkCanvas.Core/Selection/SelectionGeometry.cs` | 选区句柄命中与拖拽矩阵 |
| `src/InkCanvas.Core/Recognition/ShapeRecognizer.cs` | 托管几何形状识别（替代 InkAnalyzer） |
| `src/InkCanvas.Core/Shapes/ShapeGenerator.cs` | 25 种预设图形几何 + 两步体第二段 |
| `src/InkCanvas.Core/History/UndoStack.cs` | 有界撤销/重做（`DelegateAction`） |
| `src/InkCanvas.Core/Imaging/PixelCompositing.cs` | 预乘 BGRA 的 source-over |
| `src/InkCanvas.Core/Input/TouchContactClassifier.cs` | 触摸接触宽度分档（特殊屏/QuadIR/笔尖模式） |
| `src/InkCanvas.Platform/Input/PalmRejector.cs` | 手掌判定 / 笔优先 |
| `src/InkCanvas.Platform/Windows/LayeredInkWindow.cs` | 分层透明窗口 + WM_POINTER 输入 + `OverlayPointer` |
| `src/InkCanvas.Features/Persistence/Stores.cs` | `.icink` 墨迹 JSON 格式 |

---

## 1. 结论摘要（移植者先读）

1. **唯一入口**：`InkController` 是 UI 层唯一墨迹 API；白板、回放、屏幕覆盖层、浮动栏/色板/图形预览全部共享同一控制器与同一 `InkSceneRenderer`。HarmonyOS 侧应保持这个「一个 Controller + 两个宿主（覆盖层/画布）」结构。
2. **提交语义**：`CommitStrokes` 先 `Complete()`、再 `Document.AddRange`、再 `History.Push`，最后发一次 `Invalidated`。笔画宽度在**到达时**由 `TaperProfile` 写入并冻结，`Complete()` 不重算几何，因此提交后的笔迹与最后一帧活动笔迹逐点一致（这是上游「笔锋闪烁」的修复点，测试钉住）。
3. **渲染分层**：已提交层（document + 选区框）与活动层（live strokes + shape preview）分离；覆盖层把已提交层缓存在 CPU，每帧只回读活动笔画的包围盒并局部合成。白板 `InkSurface` 则整场景重绘（Win2D `CanvasControl` 只在 `Invalidate()` 后绘制）。
4. **自交笔迹**：中心线每段生成一个同向 capsule 轮廓，渲染时用 **非零环绕（Winding）** 合并填充，使同一笔自交成为并集而不是奇偶镂空；半透明荧光笔在重叠处只合成一次。
5. **手势数学是纯逻辑**：`TwoFingerManipulator` 不依赖任何平台 API，可 1:1 移植并用黄金向量钉住；枢轴选择 `MatrixTransformCenter` 也是纯函数。
6. **多指语义与直觉相反**：`MultiTouchEnabled == true` 表示**多指书写、手势关闭**；只有为 `false` 时第二根手指才取消触摸墨迹并启动双指手势（WPF `isInMultiTouchMode` 原义）。设置默认值为 `true`（见 §10）。
7. **图形工具有两步体**：Cuboid（先前面，再深度）与 Hyperbola（先渐近线，再分支）各自维护 `_shapeStep` 状态；两步产生的所有笔画合并为**一个撤销单元**。单步图形提交后自动回笔模式，长按粘滞由 `StickyShape` 控制（两步体存在不回粘的差异，见 §8.10）。
8. **强平台绑定集中在两处**：`OverlayInkPresenter`（`UpdateLayeredWindow`、GPU 回读、DIB）与 `LayeredInkWindow`（Win32 分层窗口/WM_POINTER）。其余墨迹逻辑均为托管纯逻辑或仅更换渲染/事件 API 即可适配。

---

## 2. 依赖与数据流

```
输入源
  ├─ LayeredInkWindow (Win32 WM_POINTER*, 透明覆盖层)
  │     └─ OverlayPointer{Id,X,Y,Pressure,ContactWidth,ContactHeight,Kind,TimeMs}
  └─ InkSurface (WinUI PointerRoutedEventArgs, 白板/回放)
        └─ Translate() → OverlayPointer

InkController.OnPointerDown/Move/Up
  ├─ PalmRejector（笔优先/手掌）
  ├─ TouchContactClassifier（特殊屏分档）
  ├─ TwoFingerManipulator（双指）
  ├─ _live: Dictionary<pointerId, InkStroke>（活动笔迹）
  ├─ _shapeDragStart / _shapeStep / _stepPending（图形工具）
  ├─ _erasing / _eraseBeforeSnapshot / _erasedThisGesture（橡皮）
  ├─ Selection / _dragSnapshot / _dragTargets（选区）
  ├─ Document: InkDocument（已提交笔迹）
  └─ History: UndoStack(200)

事件
  ├─ Invalidated  → OverlayInkPresenter.RequestRender / InkSurface._canvas.Invalidate
  ├─ Document.Changed → Presenter/Scene 丢弃几何缓存、重建已提交层
  ├─ SelectionChanged → 覆盖层已提交层失效
  ├─ ToolChanged / SlideFlipRequested
  └─ History.Changed（UI 撤销按钮使能）

渲染
  OverlayInkPresenter: committed CanvasRenderTarget (CPU 缓存) + live CanvasRenderTarget(包围盒)
                       → 预乘 BGRA frame → LayeredInkWindow.PixelBuffer → UpdateLayeredWindow
  InkSurface:          CanvasControl.Draw → InkSceneRenderer.Draw(整场景)
```

---

## 3. 公开类型与消费方

### 3.1 `InkCanvas.Ink` 命名空间

| 类型 | 关键公开成员 | 消费方 |
|---|---|---|
| `enum InkTool { Pen, Highlighter, Eraser, Select, Shape }` | — | `FloatingBar`、`BoardWindow`、`PenPaletteWindow`、`ShapePickerWindow`、`ReplayWindow`、`SelfTest` |
| `enum InkEraserMode { Area, WholeStroke }` | — | `FloatingBar`、`BoardWindow`（面积擦/线条擦按钮高亮） |
| `sealed class InkController : IDisposable` | `Document`、`History`、`CurrentAttributes`、`Tool`、`ShapeKind`、`StickyShape`、`SelectShape(kind,sticky)`、`HyperbolaAsymptoteOption`、`AskRemoveAsymptotes`、`CloneMode`、`EraserRadius`、`EraserMode`、`PalmErases`、`RecognizeShapes`、`MultiTouchEnabled`、`ViewTransform`、`ViewCenter`、`TransformCenterOption`、`LiveStrokes`、`ShapePreview`、`Selection`、`GesturesTargetSelection`、`SelectionBounds`、`Palm`、`Invalidated`、`SelectionChanged`、`ToolChanged`、`SlideFlipRequested`、`ConfigureGestures`、`OnPointerDown/Move/Up`、`ToWorld`、`DeleteSelection`、`TransformSelection`、`Undo`、`Redo`、`Clear`、`Dispose` | `AppServices.OverlayInk`；`BoardService.Pages`；`BoardWindow`；`ReplayWindow`；`FloatingBar.Controller`；`PenPalette.Controller`；`ShapePreview`；`PenPaletteWindow`/`ShapePickerWindow`；`ShellController`；`SelfTest`；`PptInkSession`（通过 `CaptureCurrentInk`/`ApplyCurrentInk` 回调间接使用） |
| `sealed class TwoFingerManipulator` | `AllowZoom/AllowTranslate/AllowRotation`、`MinScale=0.2`、`MaxScale=8`、`Begin`、`Reset`、`Update(a,b,ref transform,center)` | `InkController`；`BoardWindow`（图片/视频元素手势） |
| `sealed class StrokeRenderer : IDisposable` | `Draw(session,stroke,transform)`、`Invalidate(stroke)`、`InvalidateAll()` | `InkSceneRenderer.Strokes` |
| `sealed class InkSceneRenderer : IDisposable` | `Strokes`、`ShowSelectionOutline`、`DrawCommitted`、`DrawLive`、`Draw`、`InvalidateGeometry` | `OverlayInkPresenter.Scene`；`InkSurface`；`BoardWindow`（导出图片时 `new InkSceneRenderer { ShowSelectionOutline=false }`） |
| `sealed class OverlayInkPresenter : IDisposable` | `Scene`、`PresentedFrames`、`GpuReadbackBytes`、`LatencyLogEnabled`、`LatencySamples`、`LastInputToPresentMs`、`MaxInputToPresentMs`、`AverageInputToPresentMs`、`InkVisible`、`Attach`、`Detach`、`Dispose` | `OverlayHost`；`SelfTest`/`App` 诊断 |
| `sealed class InkSurface : UserControl` | `Controller`、`ClearColor` | `BoardWindow`、`ReplayWindow` |

### 3.2 被 Ink 层消费的 Core/Platform 公开类型

| 类型 | 关键成员 | 消费方 |
|---|---|---|
| `InkStroke` | `Id`、`Attributes`、`Points`、`Count`、`IsCompleted`、`IsRecognizedShape`、`TaperKind`、`AddPoint`、`Complete`、`Bounds`、`ArcLength`、`Transform`、`DistanceTo`、`DistanceToSegment`、`Clone`、`RestorePoints` | `InkController`、`InkDocument`、`ShapeRecognizer`、`StrokeEraser`、`InkSceneRenderer`、`PptInkSession`、持久化 |
| `InkPoint`（struct） | `X/Y/Pressure/Width/TimeMs`、`Position` | 同上 |
| `StrokeAttributes` | `Color`、`BaseWidth`、`IsHighlighter`、`HighlighterAlpha`、`TaperKind`、`EffectiveTaper`、`EffectiveColor`、`Clone` | 同上 |
| `InkColor`（record struct） | `A/R/G/B`、`FromArgb`、`Opaque`、`PackedArgb`、`FromPackedArgb`、`WithAlpha`、`Black/White/Red/Transparent` | 同上 |
| `InkDocument` | `Strokes`、`Count`、`Changed`、`Add/AddRange/Remove/RemoveAll/Insert/Clear/Touch`、`Bounds`、`HitTest(point,radius)`、`HitTest(rect)`、`EraseAt`、`Transform` | `InkController`、`InkSurface`、`OverlayInkPresenter`、`PptInkSession`、`BoardService` |
| `Rect`（record struct） | `Left/Top/Right/Bottom`、`Empty`、`Width/Height`、`IsEmpty`、`Center`、`Contains`、`Intersects`、`Union`、`Inflate` | 全层 |
| `TaperProfile` 及 `NoneTaper/TailTaper/VelocityTaper` | `Apply(span, tipIndex, baseWidth)`、`Settle` | `InkStroke.AddPoint` |
| `StrokeGeometry` | `BuildUnionContours`、`BuildSegmentOutline`、`SignedArea`、`Circle`、`BuildOutline` | `StrokeRenderer`、测试、`SelfTest` |
| `StrokeEraser` | `EraseDisc(stroke,center,radius,out remaining)` | `InkController` |
| `SelectionGeometry` / `SelectionHandle` | `HandleRadius`、`RotateOffset`、`RotateHandlePosition`、`HitTest`、`DragMatrix` | `InkController`、`InkSceneRenderer`、测试 |
| `ShapeGenerator` / `ShapeKind` | `Generate`、`GenerateCuboidDepth`、`GenerateHyperbolaBranches`、`SnapParallelEnd`、`ParallelLines`、`Stroke`、`Dot`、`Arrow`、`AxisArrow`、`Dashed`、`Dots`、`EllipseStroke`、`EllipsePoints`、`DashedEllipseArcs`、常量 | `InkController`、`ShapePreview`、`ShapeRecognizer` 测试、`SelfTest` |
| `ShapeRecognizer` / `RecognizedShape` / `RecognitionResult` | `MinSize=40`、`Recognize` | `InkController.TryRecognize`、测试 |
| `UndoStack` / `IUndoableAction` / `DelegateAction` | `Capacity`、`Push/Undo/Redo/Clear`、`Changed` | `InkController.History`、`BoardWindow`、测试 |
| `TouchContactClassifier` / `TouchIntent` / `TouchDecision` | `MeasureContactWidth`、`BoundsWidth`、`Classify` | `PalmRejector` |
| `PalmRejector` | `PalmContactThreshold=30`、`PenHasPriority=true`、`Advanced`、`NibMode`、`ForceEraser`、`ForcePointEraser`、`PptGestureEligible`、`OnDown/OnUp`、`IsPalmContact`、`ClassifyContact`、`ShouldDraw`、`ShouldErase`、`Reset` | `InkController.Palm`、`AppServices`、测试 |
| `MatrixTransformCenter` | `Resolve(option,canvas,gesture,selection,isLoaded)` | `InkController.ResolvePivot` |
| `OverlayPointer` / `OverlayPointerKind` | `Id,X,Y,Pressure,ContactWidth,ContactHeight,Kind,TimeMs` | `InkController`、`InkSurface`、`PalmRejector`、测试 |
| `PixelCompositing` | `BlendOver(dst,dw,dh,dx,dy,src,sw,sh)` | `OverlayInkPresenter`、测试 |

---

## 4. 墨迹数据模型（精确语义）

### 4.1 `InkPoint`

```
struct InkPoint { float X, Y, Pressure, Width, TimeMs; }
Position = (X, Y)
```
- `Pressure`：原始设备压力 [0,1]；`0.5` 表示「无压力信息」。
- `Width`：**最终渲染宽度（DIP）**，不是实时推导；写入后除笔锋窗口内的 `Settle`（只能变宽）或整笔变换/恢复外不再改变。
- `TimeMs`：相对笔画开始的毫秒数（但 `InkController` 直接把 `OverlayPointer.TimeMs` 传入，覆盖层里那是绝对 `GetTickCount` 时间戳；见 §8.10）。

### 4.2 `InkStroke`

- 构造：`new InkStroke(attributes)`；`Id = Guid.NewGuid()`；`TaperProfile.Create(attributes.EffectiveTaper)`。
- `TaperKind` setter：值不同才重建 profile；若已 `IsCompleted`，调用 `RebuildWidths()`（清零所有宽度后依次重新 Apply）。
- `AddPoint(x,y,pressure=0.5,timeMs=0)`：
  1. 追加 `InkPoint(x,y,pressure,0,timeMs)`（宽度先置 0）；
  2. `_taper.Apply(points, Count-1, Attributes.BaseWidth)`；
  3. `_boundsDirty = true`。
- `Complete()`：仅 `IsCompleted = true`，**不碰几何**。
- `RebuildWidths()`：所有 `Width=0`，然后 `for i in 0..n-1 Apply(i)`。
- `Bounds`（惰性、脏标记）：
  - 空点 → `Rect.Empty`；
  - 遍历点取 minX/minY/maxX/maxY 与 `maxHalf = max(Width)`；
  - 返回 `FromLTRB(minX - maxHalf/2, minY - maxHalf/2, maxX + maxHalf/2, maxY + maxHalf/2)`。
  - 注意：只用点的 `Width`，不直接用 `Attributes.BaseWidth`。
- `ArcLength()`：相邻中心线欧氏距离之和。
- `Transform(Matrix3x2 m, bool scaleWidth=true)`：
  - `widthScale = sqrt(|m.M11*m.M22 - m.M12*m.M21|)`；若 `<= 0.0001` 则取 1；
  - 每个点位置 `Vector2.Transform`；`Width *= widthScale`；`Attributes.BaseWidth *= widthScale`；`_boundsDirty = true`。
  - **这会永久缩放笔画宽度**（撤销通过快照恢复）。
- `DistanceTo(p)`：单点 → 点到点距离；多点 → 到各线段的 `DistanceToSegment` 最小值。
- `DistanceToSegment(p,a,b)`：
  ```
  ab = b-a; len2 = |ab|²
  len2 < 1e-6        → |p-a|
  t = clamp(dot(p-a, ab)/len2, 0, 1)
  → |p - (a + ab*t)|
  ```
- `Clone()`：复制 Attributes、Id（相同）、`IsRecognizedShape`、所有点、`IsCompleted`；`_boundsDirty=true`。**引用相等性仍以对象实例为准**（`InkDocument.RemoveAll` 用 `HashSet<InkStroke>` 引用/默认相等）。
- `RestorePoints(points)`：清空后批量写入，保留传入点的已计算宽度；`_boundsDirty=true`。

### 4.3 `Rect`（Core 自定义，不是 WinUI `Rect`）

- `Empty = (0,0,0,0)`。
- `Width=Right-Left`，`Height=Bottom-Top`。
- `IsEmpty => Width <= 0 || Height <= 0`（注意：零宽/零高都算空）。
- `Center = ((L+R)/2,(T+B)/2)`。
- `Contains` 含边界（`>=`/`<=`）。
- `Intersects`：`!(o.Left > Right || o.Right < Left || o.Top > Bottom || o.Bottom < Top)`。
- `Union`：**任一方 IsEmpty 就返回另一方**（因此空矩形是并集单位元）。
- `Inflate(d)`：四边各外扩 d。

### 4.4 `StrokeAttributes`

| 字段 | 默认 | 说明 |
|---|---|---|
| `Color` | `InkColor.Black` = ARGB(255,0,0,0) | 普通笔迹颜色 |
| `BaseWidth` | `3f` DIP | 全宽（不是半径） |
| `IsHighlighter` | `false` | 荧光笔 |
| `HighlighterAlpha` | `96` | 仅荧光笔；原始 WPF `InkAlpha` 20–240、步进 20 |
| `TaperKind` | `Tail` | 见 §5 |
| `EffectiveTaper` | `IsHighlighter ? None : TaperKind` | 荧光笔永不笔锋 |
| `EffectiveColor` | `IsHighlighter ? Color.WithAlpha(HighlighterAlpha) : Color` | 荧光笔覆盖 alpha |
| `Clone()` | 深复制以上 5 字段 | 不含点 |

`InkController.CurrentAttributes` 初始为：`Color=Black`、`BaseWidth=2.5f`、`TaperKind=Tail`、`IsHighlighter=false`、`HighlighterAlpha=96`。
`AppServices.ApplySettings()` 会覆盖为（见 §10）：`BaseWidth=clamp(Settings.Canvas.InkWidth,0.5,60)`、`TaperKind` 由 `InkStyle` 映射、`HighlighterAlpha=clamp(InkAlpha,20,240)`。

### 4.5 `InkColor`

- `InkColor(byte A, byte R, byte G, byte B)`。
- `PackedArgb = (A<<24)|(R<<16)|(G<<8)|B`（uint）。
- `FromPackedArgb` 反向拆解。
- 预置：`Black(255,0,0,0)`、`White(255,255,255,255)`、`Red(255,220,38,38)`、`Transparent(0,0,0,0)`。

### 4.6 `InkDocument`

- 底层 `List<InkStroke>`；每次增删/插入/清空/`Touch()` 都触发 `Changed`（无参数）。
- `AddRange` 会跳过 null；只有实际加入至少一个才触发 `Changed`。
- `RemoveAll` 用 `HashSet<InkStroke>` 引用集合 + `List.RemoveAll(set.Contains)`。
- `Bounds`：对所有笔画 `Bounds` 做 `Rect.Union`。
- `HitTest(Vector2 point, float radius)`（选区/整笔擦除共用）：
  ```
  若 !stroke.Bounds.Inflate(radius).Contains(point) → 跳过
  若 stroke.DistanceTo(point) <= radius + stroke.Attributes.BaseWidth * 0.5f → 命中
  ```
  **注意用 BaseWidth，不用逐点 Width**。
- `HitTest(Rect rect)`：包围盒相接后再逐点判断 `rect.Contains(point)`，任一点在内即命中（为套索/框选预留，但当前 UI 未接框选）。
- `EraseAt(point,radius)`：`HitTest` 后 `RemoveAll`，返回被移除列表（整笔擦除）。
- `Transform(strokes,m)`：对每笔 `stroke.Transform(m)`（默认缩放宽度）。

### 4.7 `UndoStack` / `DelegateAction`

- `UndoStack(int capacity=100)`；`InkController` 用 **200**。
- `Push` 约定「动作**已经执行**」：加入 undo 栈；超过 capacity 时 `RemoveAt(0)` 丢最旧；清空 redo 分支；触发 `Changed`。
- `Undo`：取栈顶 → `Revert()` → 移入 redo；`Redo`：取 redo 栈顶 → `Apply()` → 移入 undo。
- `DelegateAction(name, apply, revert)`：两个闭包；`Name` 用于调试/UI（当前 UI 未显示名称）。
- 控制器中使用的撤销单元名称：`"ink"`、`"shape"`、`"area erase"`、`"erase"`、`"clone selection"`、`"transform selection"`、`"gesture selection"`、`"delete"`、`"clear"`、`"recognize"`。

---

## 5. 笔锋（`TaperProfile`）— 精确算法

> 设计契约：只读写 `index <= tipIndex`；宽度只增不减（`Settle`）；`Complete()` 不改变任何宽度。下游必须逐条保留，否则「提交几何 == 最后一帧」失效。

### 5.1 `TaperKind` 枚举

```
None = 0     均匀宽度
Tail = 1     笔尖细、尾部在滑动窗口内settle到全宽（默认）
Velocity = 2 速度驱动（快则细）
```

### 5.2 公共 `Settle`

```
Settle(ref slot, candidate): if (candidate > slot) slot = candidate;
```

### 5.3 `NoneTaper`

```
Apply(points, tip, baseWidth):
    Settle(points[tip].Width, baseWidth)
```

### 5.4 `TailTaper`（「点集笔锋」）

常量（`init` 属性，默认值）：

| 常量 | 值 | 含义 |
|---|---:|---|
| `TailPoints` | 10 | 尾部窗口采样数 |
| `HeadPoints` | 4 | 起笔窗口采样数 |
| `TipRatio` | 0.2 | 笔尖宽度 / 全宽 |

算法：

```
n = max(2, TailPoints)
start = max(0, tipIndex - n + 1)
for j in start..tipIndex:
    distance = tipIndex - j           // 笔尖处 0
    t = distance / (n - 1)            // 笔尖 0 → 窗口边缘 1
    ratio = TipRatio + (1 - TipRatio) * t
    Settle(points[j].Width, baseWidth * ratio)

if tipIndex < HeadPoints:
    t = (tipIndex + 1) / (HeadPoints + 1)
    ratio = TipRatio + (1 - TipRatio) * t
    Settle(points[tipIndex].Width, baseWidth * ratio)
```

数值：笔尖宽度 = `0.2 * baseWidth`；笔尖后第 9 个采样达到全宽；起笔前 4 点 ratio 依次 0.36、0.52、0.68、0.84（被 `Settle` 取最大后实际主导）。单点落笔（Tail）最终宽度 = `0.36 * baseWidth`（尾部 0.2 与起笔 0.36 取大）。

### 5.5 `VelocityTaper`（「速率笔锋」）

```
pressure = 0.5
if tipIndex >= 2:
    d1 = dist(points[tip-2], points[tip-1])
    d2 = dist(points[tip-1], points[tip])
    speed = (d1 + d2) / 20f                    // 与原 GetPointSpeed 同刻度

    if speed >= 0.25:
        pressure = 0.5 - 0.3 * (min(speed, 1.5) - 0.3) / 1.2
    else if speed < 0.05:
        pressure = 0.5 + 0.4 * (0.05 - speed) / 0.05

ratio = clamp(pressure / 0.5, 0.15, 1.8)
Settle(points[tip].Width, baseWidth * ratio)
```
- 只用**已经到达**的两段，所以每个宽度只决定一次，不回写历史点（测试钉住）。
- 极慢（speed→0）ratio→1.8；极快（speed≥1.5）pressure=0.2、ratio=0.4；ratio 下限 0.15。

### 5.6 `TaperProfile.Create`

```
None → NoneTaper
Velocity → VelocityTaper
其它（含 Tail）→ TailTaper
```

**边界差异**：构造函数用 `Attributes.EffectiveTaper`（荧光笔强制 None），但 `InkStroke.TaperKind` **setter 直接用传入值** `Create(value)`，没有再过 `EffectiveTaper`。因此对已存在的荧光笔笔画显式赋 `TaperKind=Tail` 会让它开始笔锋（当前 UI 只改 `CurrentAttributes`，未触发该路径）。移植时建议保持同一行为，或两侧统一修正并记录。

---

## 6. 笔画轮廓几何（`StrokeGeometry`）— 逐段 capsule + 正向绕组

### 6.1 `BuildUnionContours(stroke)`

- 0 点 → 空数组。
- 1 点 → 一个圆：半径 `max(point.Width, 0.5f) * 0.5f`，24 段。
- n 点 → `n-1` 个轮廓；第 i 段（i 从 1 到 n-1）：
  `BuildSegmentOutline(p[i-1], max(p[i-1].Width,0.5)*0.5, p[i], max(p[i].Width,0.5)*0.5)`。
- 返回 `Vector2[][]`（每个 capsule 一个独立闭合轮廓），渲染器再用 Winding 合并。

### 6.2 `BuildSegmentOutline(start, startRadius, end, endRadius)`

```
delta = end - start
length = |delta|
if length < 1e-4:
    return Circle(start, max(startRadius,endRadius), 24) 归一化绕组

tangent = delta / length
normal = (-tangent.Y, tangent.X)
a0 = atan2(normal.Y, normal.X)

outline:
  start + normal * startRadius
  end   + normal * endRadius
  AppendArc(end, endRadius, a0, deltaAngle=-π, steps=12)     // 加 11 个点
  end   - normal * endRadius
  start - normal * startRadius
  AppendArc(start, startRadius, a0+π, deltaAngle=-π, steps=12) // 加 11 个点
return NormalizeWinding(outline)
```

- `AppendArc(center,radius,startAngle,delta,steps)`：`radius <= 0` 直接返回；`for i=1..steps-1`，`angle = startAngle + delta*i/steps`，加 `center + (cos,sin)*radius`。
- 单段轮廓点数：2 + 11 + 2 + 11 = **26**（List 初始容量 28）。
- `SignedArea`：鞋带公式，`0.5 * Σ(x_i*y_{i+1} - x_{i+1}*y_i)`（double 计算后转 float）。
- `NormalizeWinding`：`SignedArea < 0` 时 `Array.Reverse`，保证所有 capsule 正向绕组。
- `Circle(center,radius,segments)`：`a = 2π*i/segments`，`center+(cos,sin)*r`。
- `BuildOutline(stroke)`：诊断/测试用；多轮廓时简单 `SelectMany` 展平（不能用于生产填充，会重新引入自交）。

### 6.3 渲染填充规则

`StrokeRenderer.BuildGeometry` 把每个轮廓建成 `CanvasPathBuilder`（`BeginFigure` → `AddLine` → `EndFigure(Closed)`），再 `CanvasGeometry.CreateGroup(..., CanvasFilledRegionDetermination.Winding)`。**必须非零环绕**：Alternate/EvenOdd 会让中心线自交处出现洞；一次 group 填充也保证半透明色只合成一次。

---

## 7. 渲染管线

### 7.1 分层与缓存不变量

| 层 | 内容 | 何时变化 | 缓存 |
|---|---|---|---|
| committed | `Document.Strokes` + 选区框（可选） | 文档变更、选区变更、`ViewTransform` 变更 | `StrokeRenderer._cache`（按 `InkStroke.Id`，仅已完成笔迹）；覆盖层另有一份 CPU 像素缓存 |
| live | `LiveStrokes` + `ShapePreview` | 每个指针移动 | 不缓存（几何每帧重建） |
| selection | 选区 AABB 四角 + 句柄 + 旋转柄 | 选区/变换 | 画在 committed 层 |

- `InkSceneRenderer.ShowSelectionOutline` 默认 `true`。
- `InkSurface` 与覆盖层共享同一 `InkSceneRenderer` 语义；覆盖层用 `transformOverride` 支持子区域渲染。

### 7.2 `StrokeRenderer`

```
Draw(session, stroke, transform):
  if stroke == null || stroke.Count == 0: return
  color = stroke.Attributes.EffectiveColor
  winColor = Windows.UI.Color.FromArgb(A,R,G,B)
  fromCache = stroke.IsCompleted && _cache.TryGetValue(stroke.Id, out cached)
  geometry = fromCache ? cached : BuildGeometry(session, stroke)
  if geometry == null: return
  if !fromCache && stroke.IsCompleted: _cache[Id] = geometry
  previous = session.Transform
  session.Transform = transform
  session.FillGeometry(geometry, winColor)
  session.Transform = previous
  若几何未入缓存（未完成或未存储）→ Dispose
```

- 缓存键是 `Guid Id`；**克隆笔画复用同一 Id**（`Clone` 复制 Id），因此克隆后若几何不同而 Id 相同，可能命中错误缓存——调用方在文档变更时 `InvalidateAll()`（覆盖层/`InkSurface` 均监听 `Document.Changed`）。移植时需保留「文档变更即全量失效」或改为内容哈希。
- `Invalidate(stroke)` 移除单个缓存并 Dispose；`InvalidateAll` 清空。
- 只对 `IsCompleted` 缓存；活动笔画每帧重建几何。

### 7.3 `InkSceneRenderer`

- `DrawCommitted(session, controller, transformOverride?)`：
  - `view = override ?? controller.ViewTransform`；
  - 遍历 `controller.Document.Strokes` → `Strokes.Draw`；
  - 若 `ShowSelectionOutline && Selection.Count > 0` → `DrawSelection`。
- `DrawLive`：
  - 遍历 `controller.LiveStrokes`，再遍历 `controller.ShapePreview`，用同一 `view`。
- `Draw` = committed + live。
- `DrawSelection` 度量与颜色：
  - 世界坐标选区 AABB 四角经 `view` 变换成视图坐标；
  - 边线：`accent = ARGB(255, 0, 120, 212)`（#0078D4），宽 **1.5 DIP**，四条边；
  - 四角缩放柄：`FillCircle(c, 6f, white)` + `DrawCircle(c, 6f, accent, 1.5f)`；
  - 旋转柄位置：`SelectionGeometry.RotateHandlePosition(bounds)` = `(bounds.Center.X, bounds.Top - 28)`（世界坐标，再经 view）；
  - 连接线：上边中点 `(corners[0]+corners[1])*0.5` → 旋转柄，accent，1.5 DIP；
  - 旋转柄：`FillCircle(...,7f,white)` + `DrawCircle(...,7f,accent,1.5f)`。
  - 柄半径 6/7 是绘制尺寸；命中半径另为 12（§8.5）。

### 7.4 `OverlayInkPresenter`（透明覆盖层，平台强绑定）

#### 构造/资源

- `_device = CanvasDevice.GetSharedDevice()`；
- `_committedTarget = new CanvasRenderTarget(device, window.Width, window.Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied)`；
- `_committedPixels = byte[W*H*4]`；`_frame = byte[same]`；`AsBuffer()` 零拷贝包装；
- 所有像素为**预乘 BGRA**，顶向下（DIB `biHeight=-height`）。
- 尺寸限制：`_committedTarget`、`_committedPixels` 与 `_frame` 都在构造时按 `window.Width/Height` 固定；`LayeredInkWindow` 也没有 `WM_DISPLAYCHANGE`/DPI 变更处理（`WndProc` 只处理 `WM_POINTER*`、鼠标回退、热键、捕获）。因此当前实现不支持显示器分辨率/DPI 变化后的热重配，覆盖层按主屏尺寸一次性创建。HarmonyOS 侧若支持窗口尺寸/折叠/旋转变化，必须补上重建路径。

#### Attach/Detach

`Attach()` 订阅 `window.PointerDown/Move/Up`、`controller.Invalidated`、`controller.Document.Changed`、`controller.SelectionChanged`，然后 `RequestRender()`。
`Detach()` 对称退订。
`OnDocumentChanged`：`Scene.Strokes.InvalidateAll()` + `InvalidateCommitted()`。
`InvalidateCommitted`：`_committedValid=false` + `RequestRender()`。

#### 重入合并 `RequestRender`

```
if (_rendering) { _pending = true; return; }
_rendering = true;
try {
  do {
    do { _pending = false; UpdateFrame(); } while (_pending);
    PresentFrame();
  } while (_pending);
} finally { _rendering = false; _inputDriven = false; }
```
- 重入失效只置 `_pending`，不递归进入绘制会话。

#### `UpdateFrame`

1. 若 `!InkVisible`：
   - **一次性**把 `_frame` 每 4 字节填 `1,1,1,1`（alpha=1/255 的预乘白，保持分层窗口可命中但视觉透明）；
   - `Marshal.Copy` 全量到 `window.PixelBuffer`；
   - `_frameIsHidden=true`、`_liveW=_liveH=0`、`_frameDirty=true`，返回。
2. `EnsureCommittedCache()`。
3. `fullRefresh = _committedChanged || _frameIsHidden`；`_frameIsHidden=false`。
4. 若 `fullRefresh`：`Array.Copy(_committedPixels → _frame)`；清 live box；`_committedChanged=false`。
   否则若旧 live box 非空：`RestoreLiveRegion()` 从 committed 像素恢复旧区域。
5. `region = LiveRegion()`；非空则 `CompositeLiveRegion(region)`。
6. 写 DIB：
   - `fullRefresh`：全帧拷贝；
   - 否则：取「旧 live box ∪ 新 live box」矩形，`WriteRegionToDib`；
   - 设置 `_frameDirty=true`。
7. `_liveX/Y/W/H = region`。

#### `EnsureCommittedCache`

```
selectionBounds = controller.SelectionBounds
viewChanged = _lastView != controller.ViewTransform
selectionChanged = _lastSelectionCount != Selection.Count || _lastSelectionBounds != selectionBounds
if (_committedValid && !viewChanged && !selectionChanged) return
using session = _committedTarget.CreateDrawingSession():
    session.Clear(ARGB(1,1,1,1))
    Scene.DrawCommitted(session, controller, OverlayTransform())
_committedTarget.GetPixelBytes(_committedPixelsBuffer)   // 全屏回读
GpuReadbackBytes += _committedPixels.Length
_committedValid = true; _committedChanged = true
_lastView = ViewTransform; _lastSelectionBounds = selectionBounds; _lastSelectionCount = Selection.Count
```

- `OverlayTransform() = ViewTransform * Matrix3x2.CreateScale(_window.DpiScale)`：世界 DIP → 物理像素。
- 已提交层重绘条件还包含选区 AABB/数量变化（虽然选区框绘制在 committed 层）。

#### `LiveRegion()`（活动笔迹屏幕包围盒）

```
world = Union(LiveStrokes.Bounds, ShapePreview.Bounds)
if world.IsEmpty → (0,0,0,0)
view = OverlayTransform()
四角变换后取 AABB
x = max(0, floor(minX) - 4)
y = max(0, floor(minY) - 4)
right = min(window.Width,  ceil(maxX) + 4)
bottom= min(window.Height, ceil(maxY) + 4)
返回 (x,y,max(0,right-x),max(0,bottom-y))
```
- 4 物理像素余量；坐标是物理像素。

#### `CompositeLiveRegion(region)`

- live 渲染目标**只增不减**，容量按 **64 物理像素** 粒度向上取整：
  `capW = max(capW, (region.Width + 63) & ~63)`；高度同理；
- 目标像素格式同上，`Clear(ARGB(0,0,0,0))`；
- 变换：`OverlayTransform() * Matrix3x2.CreateTranslation(-region.X, -region.Y)`；
- `Scene.DrawLive(session, controller, transform)`；
- `_liveTarget.GetPixelBytes(_livePixelsBuffer, 0,0,region.Width, region.Height)`（只回读子矩形）；
- `GpuReadbackBytes += region.Width*region.Height*4`；
- `PixelCompositing.BlendOver(_frame, W, H, region.X, region.Y, _livePixels[0..liveBytes], region.Width, region.Height)`。

#### `RestoreLiveRegion` / `WriteRegionToDib`

- stride = `W*4`；
- 若 `x==0 && w==W` 走整行 `Buffer.BlockCopy`/`Marshal.Copy`，否则逐行拷贝；
- `_frame` 与 `PixelBuffer` 都是 `W*H*4` 顶向下。

#### `PresentFrame`

```
if (!_frameDirty) return
if (_inputDriven && _window.HasQueuedPointerMessage()) return
_window.Present()          // UpdateLayeredWindow
_frameDirty = false
PresentedFrames++
SampleLatency()
```
- 「输入驱动」且消息泵里已有更新的 `WM_POINTERUPDATE..WM_POINTERUP` 时跳过陈旧 present；不做额外等待，只丢过期工作。
- 跳过后 `_frameDirty` 仍为 true，下一次渲染会 present。

#### 延迟统计

- `NoteInput(p)` 记录 `_inputDriven=true`、`p.TimeMs`（绝对 `GetTickCount`）、id、kind；
- `SampleLatency`：`delta = unchecked((uint)Environment.TickCount - _lastInputTimeMs)`；`delta > 2000` 或 `_lastInputTimeMs==0` 视为无效；否则更新 `LastInputToPresentMs`、`MaxInputToPresentMs`、累计和 `LatencySamples`；`LatencyLogEnabled` 时写日志：
  `"ink latency: pointer {id} {kind} dwTime->present {delta} ms"`。
- `AverageInputToPresentMs = samples==0 ? 0 : total/samples`。

#### `InkVisible`

- setter 变化时 `RequestRender()`；隐藏态像素为 `1,1,1,1`，窗口因 `WS_EX_TRANSPARENT` 让鼠标穿透（该样式由 `LayeredInkWindow.ClickThrough` 控制）。

### 7.5 `PixelCompositing.BlendOver`（预乘 BGRA）

```
srcStartX = max(0, -dstX); srcStartY = max(0, -dstY)
dstOriginX = max(0, dstX); dstOriginY = max(0, dstY)
copyW = min(srcW - srcStartX, dstW - dstOriginX)
copyH = min(srcH - srcStartY, dstH - dstOriginY)
逐像素：
  a = src[3]
  a == 0   → 跳过
  a == 255 → 四通道直接复制（dst alpha=255）
  否则 inv = 255 - a
    dst[c] = src[c] + dst[c]*inv/255   (c = B,G,R)
    dst[3] = a     + dst[3]*inv/255
```
- 整数除法截断；与 Win2D 预乘合成语义一致；测试钉住偏移、裁剪、透明/不透明。

### 7.6 `InkSurface`（WinUI 3 不透明画布）

- 构造：`CanvasControl { ClearColor = ARGB(0,0,0,0) }`；`Draw += OnDraw`；`Content = _canvas`；
  `PointerPressed/Moved/Released/Canceled` + `PointerCaptureLost`；`ManipulationMode = None`（手势由控制器处理）。
- `Controller` setter（3.4.0 改动）：
  - 先对旧控制器把 `_activePointers` 中每个样本补发 `OnPointerUp`，清空；
  - 退订旧 `Invalidated`/`Document.Changed`；
  - 赋新控制器；`_scene?.Dispose()`；`new InkSceneRenderer()`；订阅新事件；`_canvas.Invalidate()`。
- `ClearColor` 透传 `CanvasControl.ClearColor`。
- `OnDocumentChanged`：`Scene.Strokes.InvalidateAll()` + `Invalidate()`。
- `OnDraw`：设置 `controller.ViewCenter = (ActualWidth/2, ActualHeight/2)`（DIP），然后 `Scene.Draw(session, controller)`（无 transform override）。
- 指针翻译 `Translate(e)`：
  - `point = e.GetCurrentPoint(this)`；
  - `kind`：`Pen→Pen`、`Touch→Touch`、其它→`Mouse`；
  - `pressure = props.Pressure <= 0 ? 0.5f : props.Pressure`；
  - `contact = props.ContactRect`，宽高直接转 float（DIP）；
  - `TimeMs = (uint)point.Timestamp / 1000`（`Timestamp` 单位微秒 → 毫秒）；
  - `Id = e.Pointer.PointerId`。
- `PointerPressed`：`CapturePointer` → 记录 active → `OnPointerDown` → `Handled=true`。
- `PointerMoved`：未知 id 直接忽略；否则更新并 `OnPointerMove`。
- `PointerReleased`：只有 active id 才 `OnPointerUp`；`ReleasePointerCapture`；`Handled=true`。
- `PointerCanceled` / `PointerCaptureLost`：只有 active id 才用**最后一个样本** `OnPointerUp`（捕获丢失后可能取不到当前点）。
- 延迟契约（注释）：XAML 在 UI 线程派发指针事件，处理内联跑控制器；`Invalidate()` 在下一合成帧触发 Win2D 绘制；Win2D 合并重复失效；无 dispatcher hop、无定时器；`CanvasControl` 只在失效时绘制，不自由跑循环。

---

## 8. `InkController` 状态机（核心）

### 8.1 字段与默认值

| 成员 | 默认 | 说明 |
|---|---|---|
| `Document` | `new InkDocument()` | 已提交笔迹 |
| `History` | `new UndoStack(200)` | 撤销栈 |
| `CurrentAttributes` | Black / 2.5 / Tail / 非荧光 / alpha 96 | 当前笔属性模板 |
| `Tool` | `Pen` | setter 见下 |
| `ShapeKind` | `Line` | 图形种类 |
| `StickyShape` | `false` | 长按粘滞 |
| `HyperbolaAsymptoteOption` | `Ask` | Yes 保留 / No 移除 / Ask 询问 |
| `AskRemoveAsymptotes` | `null` | 返回 true = 移除渐近线 |
| `CloneMode` | `false` | 选区拖拽克隆 |
| `EraserRadius` | `16f` DIP | 面积擦半径（AppServices 会按设置覆盖） |
| `EraserMode` | `Area` | 面积擦 / 整笔擦 |
| `PalmErases` | `true` | 手掌当面积擦 |
| `RecognizeShapes` | `true` | 落笔后墨迹转形状 |
| `MultiTouchEnabled` | `true` | **true = 多指书写、禁手势** |
| `ViewTransform` | `Identity` | 世界→视图（DIP）仿射 |
| `ViewCenter` | `(960,540)` | 枢轴 CanvasCenterPoint；宿主会覆盖 |
| `TransformCenterOption` | `CanvasCenterPoint`（枚举 0） | 手势枢轴选项 |
| `GesturesTargetSelection` | `false` | 双指手势作用于选区而非视图 |
| `Selection` | `new List<InkStroke>()` | 当前选区 |
| `Palm` | `new PalmRejector()` | 手掌/笔优先 |

`LiveStrokes` 的返回类型是 `Dictionary<uint, InkStroke>.ValueCollection`（不是 `IReadOnlyCollection`），`ShapePreview` 返回 `List<InkStroke>`，目的是渲染热路径用结构枚举器、避免装箱。ArkTS 侧可直接用 `Map.values()`/数组，无此优化必要。

`Tool` setter：
```
if (_tool == value) return;
previous = _tool; _tool = value;
if (previous == Shape) { ResetShapeState(); StickyShape = false; }
ToolChanged?.Invoke();
```
`ShapeKind` setter：**总是** `ResetShapeState()`；`_shapeKind = value`；`StickyShape = false`。
`SelectShape(kind, sticky=false)`：`ShapeKind = kind; StickyShape = sticky; Tool = Shape;`。

### 8.2 事件

| 事件 | 触发 |
|---|---|
| `Invalidated` | 任何需要重绘的逻辑变化；合并由宿主负责 |
| `SelectionChanged` | 选区重建/删除/清空 |
| `ToolChanged` | `Tool` 真正变化（含图形提交后自动回 Pen） |
| `SlideFlipRequested(bool next)` | 放映中手指横扫；`true=下一页`，`false=上一页` |

### 8.3 `OnPointerDown` 路由顺序（逐行对应）

```
_palm.OnDown(p)
world = ToWorld(p)

if (p.Kind == Touch):
    _touches[p.Id] = p

    // 1) 双指手势启动（且仅当 MultiTouchEnabled == false）
    if (_touches.Count == 2 && !MultiTouchEnabled):
        CancelTouchInk()
        if (TryGetTouchPair(out a, out b))
            _manipulator.Begin(ToWorld(a), ToWorld(b))
        Invalidated?.Invoke()
        return

    // 2) 手掌 = 面积擦（简单阈值路径）
    if (PalmErases && _palm.ShouldErase(p)):
        BeginErase(world, partialStroke: true)
        return

    // 3) 高级特殊屏分档（Advanced != null）
    if (_palm.Advanced != null):
        decision = _palm.ClassifyContact(p)
        switch decision.Intent:
            Ignore: return
            GestureOnly:
                _slideGestureStartX = p.X
                _slideGestureActive = true
                return
            EraseByPoint when PalmErases:
                _gestureEraserRadius = decision.EraserDiameter / 2
                BeginErase(world, partialStroke: true)
                return
            EraseByStroke when PalmErases:
                _gestureEraserRadius = 2.5f
                BeginErase(world, partialStroke: false)
                return

    // 4) 笔优先/手掌抑制（仅对 Pen/Highlighter 生效！）
    if (!_palm.ShouldDraw(p) && Tool is Pen or Highlighter):
        return

switch (Tool):
    Eraser:  BeginErase(world, partialStroke: EraserMode == Area)
    Select:  BeginSelectionInteraction(p, world)
    Shape:
        _shapeDragStart[p.Id] =
            (_shapeStep == 1 && ShapeKind in {Hyperbola, HyperbolaWithFoci})
                ? _stepAnchor          // 第二步锁定 iniP 为中心
                : world
    default: StartStroke(p, world)
```

关键点：
- 触摸一按下就进入 `_touches`，即使随后 `Ignore` 也参与后续手势计数。
- 高级分档对**所有触摸**生效（不只特殊屏）；`Advanced` 在应用装配后总是非 null。
- 手掌擦除优先于工具路由；`PalmErases=false` 时手掌会被 `ShouldDraw` 拒绝（仅 Pen/Highlighter），但对 Eraser/Select/Shape 工具仍会路由。
- 笔优先只抑制 `Pen`/`Highlighter` 的触摸绘制。

**`StartStroke(p, world)`**（Pen/Highlighter 的 default 分支）：

```
attrs = CurrentAttributes.Clone()
attrs.IsHighlighter = (Tool == Highlighter)
stroke = new InkStroke(attrs)
stroke.AddPoint(world.X, world.Y, p.Pressure, p.TimeMs)
_live[p.Id] = stroke
Invalidated?.Invoke()
```

- `CurrentAttributes` 本身不被修改（颜色/宽度/笔锋模板跨笔复用）。
- 后续每次 move 追加一个点，宽度当场结算；up 时若 `Count>=1` 提交（单点即圆点）。

**视图↔世界**：

```
ToWorld(p) = Vector2.Transform((p.X, p.Y), Inverse(ViewTransform))
```
- `Point` 的 X/Y 是**视图 DIP**；所有绘制/命中/几何使用世界 DIP。
- `ViewBoundsOf(world)` 用 `ViewTransform` 把世界框变换到视图后取 AABB。
- `ViewTransform` 是行向量矩阵，`Vector2.Transform` 语义。

### 8.4 `OnPointerMove`

```
world = ToWorld(p)

if (p.Kind == Touch && _touches.ContainsKey(p.Id)):
    _touches[p.Id] = p
    if (_touches.Count >= 2 && !MultiTouchEnabled):
        if (!TryGetTouchPair(out ta, out tb)) return
        if (GesturesTargetSelection && Selection.Count > 0):
            identity = Matrix3x2.Identity
            if (_manipulator.Update(ToWorld(ta), ToWorld(tb),
                                    ref identity,
                                    ResolvePivot((ToWorld(ta)+ToWorld(tb))*0.5f))):
                TransformSelectionByGesture(identity)
                Invalidated?.Invoke()
            return
        t = ViewTransform
        a = ToWorld(ta); b = ToWorld(tb)
        if (_manipulator.Update(a, b, ref t, ResolvePivot((a+b)*0.5f))):
            ViewTransform = t
            Invalidated?.Invoke()
        return

if (_activeHandle != None): UpdateSelectionDrag((p.X,p.Y)); return
if (_erasing): EraseAlong(_lastEraserPoint, world); _lastEraserPoint = world; return
if (Tool == Shape && _shapeDragStart.TryGetValue(p.Id, out start)):
    _shapePreview = GenerateShapePreview(start, world)
    Invalidated?.Invoke()
    return
if (_live.TryGetValue(p.Id, out stroke)):
    stroke.AddPoint(world.X, world.Y, p.Pressure, p.TimeMs)
    Invalidated?.Invoke()
```

### 8.5 `OnPointerUp`

```
_palm.OnUp(p)
_touches.Remove(p.Id)
if (_touches.Count < 2) _manipulator.Reset()

world = ToWorld(p)

if (_slideGestureActive):
    _slideGestureActive = false
    dx = p.X - _slideGestureStartX
    if (abs(dx) > 60f) SlideFlipRequested?.Invoke(dx < 0)
    return

if (_activeHandle != None): EndSelectionDrag(); return
if (_erasing): EndErase(); return
if (Tool == Shape && _shapeDragStart.Remove(p.Id, out start)):
    CommitShapeDrag(start, world); return
if (_live.Remove(p.Id, out stroke)):
    if (stroke.Count >= 1) CommitStrokes([stroke], "ink")
    Invalidated?.Invoke()
```

- 手指横扫阈值 **60 DIP**（移植自定，非上游原值；`docs/PORTING-NOTES.md` 有说明）。
- 重复 `OnPointerUp`（`PointerCaptureLost` 跟随正常 up）第二次 `_live.Remove` 返回 false，不会重复提交（`SelfTest.CheckTapInk` 钉住）。
- 单笔提交会连发两次 `Invalidated`（`CommitStrokes` 内一次、`OnPointerUp` 再一次），宿主合并。

### 8.6 笔/触摸/手掌三路分流

**PalmRejector（简化路径）**

| 常量/规则 | 值/行为 |
|---|---|
| `PalmContactThreshold` | 30 DIP（`AppServices` 覆盖为 `max(8, Advanced.FingerModeBoundsWidth)`，默认 30） |
| `PenHasPriority` | `true` |
| `IsPalmContact(p)` | `max(ContactWidth, ContactHeight) >= 30` |
| `OnDown` | Pen → `_activePenId = id`；Touch 且大接触 → `_palms.Add(id)` |
| `OnUp` | 清 `_activePenId`（匹配 id）；`_palms.Remove(id)` |
| `ShouldDraw` | Pen→true；手掌→false；`PenHasPriority && IsPenActive`→false；否则 true |
| `ShouldErase` | `Touch && _palms.Contains(id)` |
| `Reset` | 清空 pen id 与 palms |

**高级分档（`TouchContactClassifier`，`Advanced` 非 null 时始终启用）**

常量默认（`AdvancedSettings`）：

| 字段 | 默认 | 重置推荐 |
|---|---:|---:|
| `IsSpecialScreen` | false | true |
| `IsQuadIR` | false | false |
| `TouchMultiplier` | 0.25 | 0.3 |
| `NibModeBoundsWidth` | 10 | 5 |
| `FingerModeBoundsWidth` | 30 | 20 |
| `NibModeBoundsWidthThresholdValue` | 2.5 | 2.5 |
| `FingerModeBoundsWidthThresholdValue` | 2.5 | 2.5 |
| `NibModeBoundsWidthEraserSize` | 0.8 | 0.8 |
| `FingerModeBoundsWidthEraserSize` | 0.8 | 0.8 |

算法：

```
MeasureContactWidth(w,h,isQuadIr) = isQuadIr ? sqrt(w*h) : w
BoundsWidth(advanced,nibMode) = nibMode ? NibModeBoundsWidth : FingerModeBoundsWidth

eraserEnabled = (TouchMultiplier != 0) || (!IsSpecialScreen)

if eraserEnabled && contactWidth > boundsWidth:
    if (!isDrawingShape && forceEraser) → Ignore, diameter 0
    threshold = nibMode ? NibThreshold : FingerThreshold
    if contactWidth > boundsWidth * threshold:
        diameter = contactWidth * (nibMode ? NibEraserSize : FingerEraserSize)
        if IsSpecialScreen: diameter *= TouchMultiplier
        → EraseByPoint, diameter
    if pptGestureEligible → GestureOnly, 5
    → EraseByStroke, 5

→ Draw, diameter = forcePointEraser ? 50 : 5
```

- `PalmRejector.ClassifyContact` 固定传 `isDrawingShape=false`、`ForceEraser`（默认 false）、`ForcePointEraser`（默认 true）、`PptGestureEligible`。
- `PptGestureEligible` 由 `ShellController` 每次状态刷新：`放映中 && Document.Count == 0 && Settings.PowerPoint.IsEnableFingerGestureSlideShowControl`。
- **面积擦半径**：`EraseByPoint` → `decision.EraserDiameter / 2`；`EraseByStroke` → 固定 `2.5f` 并走整笔擦。
- 默认设置下 `eraserEnabled=true`，接触宽度 >30 DIP 即进入橡皮候选；>75 DIP 走面积擦。移植时要保证 HarmonyOS 能拿到等价的接触宽度，否则分档行为会退化（见 open questions）。

### 8.7 双指手势数学（`TwoFingerManipulator`）

默认：`AllowZoom=true`、`AllowTranslate=true`、`AllowRotation=false`、`MinScale=0.2`、`MaxScale=8`。
`Begin(a,b)` 记录 `_prevA/_prevB`；`Reset()` 清 `_hasPrevious`。

`Update(a,b,ref transform,center)`：

```
if (!_hasPrevious) { Begin(a,b); return false; }

prevMid = (_prevA + _prevB) * 0.5
mid     = (a + b) * 0.5
prevVec = _prevB - _prevA
vec     = b - a
prevLen = |prevVec|
len     = |vec|

delta = Identity
if AllowTranslate:
    delta *= Translation(mid - prevMid)

if AllowZoom && prevLen > 1f && len > 1f:
    scale = len / prevLen
    currentScale = sqrt(|transform.M11*transform.M22 - transform.M12*transform.M21|)
    scale = clamp(scale,
                  MinScale / max(currentScale, 1e-3),
                  MaxScale / max(currentScale, 1e-3))
    delta *= Translation(-center) * Scale(scale) * Translation(center)

if AllowRotation && prevLen > 1f && len > 1f:
    angle = atan2(vec.Y,vec.X) - atan2(prevVec.Y,prevVec.X)
    while angle >  π: angle -= 2π
    while angle < -π: angle += 2π
    delta *= Translation(-center) * Rotation(angle) * Translation(center)

_prevA = a; _prevB = b
if (delta == Identity) return false
transform = delta * transform
return true
```

- 系统为 **行向量** 约定（`System.Numerics`）：`p' = p * delta * transform`，所以 `delta * transform` 表示「先应用本帧增量，再应用既有变换」。
- 缩放围绕 `center`（枢轴）；旋转围绕 `center`；平移先做。
- 缩放钳制按**当前 transform 的行列式**归一化，视图路径是累计钳制（`transform` 为累计矩阵）。
- `prevLen/len <= 1` 时跳过缩放和旋转（避免退化）。
- 角度归一化到 `(-π, π]`。
- `delta == Identity` 用精确值比较。

**枢轴解析 `MatrixTransformCenter.Resolve`**：

```
if (!isLoaded) return canvasCenterPoint
switch option:
  GestureOperationCenterPoint → gestureOperationCenterPoint
  SelectedElementsCenterPoint → selectedElementsCenterPoint
  默认（CanvasCenterPoint） → canvasCenterPoint
```
`InkController.ResolvePivot` 固定传 `isLoaded:true`，参数为：
`ViewCenter`、`(worldA+worldB)*0.5`、`SelectionBounds.Center`。
因此：
- `CanvasCenterPoint` → `ViewCenter`（覆盖层为屏幕 DIP/2；白板在每次 `OnDraw` 设为 `ActualWidth/2, ActualHeight/2`）；
- `SelectedElementsCenterPoint` → 选区世界 AABB 中心（无选区时为 `(0,0)`）；
- `GestureOperationCenterPoint` → 双指中点（世界）。

**视图 vs 选区手势的差异（重要）**：

- 视图路径：`t = ViewTransform; Update(..., ref t, ...); ViewTransform = t`。缩放钳制累计生效（整体 0.2–8）。
- 选区路径：每次移动传 `identity`，`Update` 后 `identity` 变为**单帧 delta**，再 `TransformSelectionByGesture(identity)` 应用到选区。因此：
  - 缩放钳制只作用于单帧（每帧可在 [0.2,8] 内），**累计可超出 8 倍**；
  - **每个 pointer move 都压入一个撤销单元**（`"gesture selection"`），不是整个手势一个单元。
  - 这是当前实现的行为/缺陷，HarmonyOS 侧若要 1:1 需保留；若修正需两侧同步并加向量。

### 8.8 选区交互

#### 命中（`BeginSelectionInteraction`）

```
viewPoint = (p.X, p.Y)                        // 视图 DIP
handle = Selection.Count > 0
    ? SelectionGeometry.HitTest(ViewBoundsOf(SelectionBounds), viewPoint)
    : None

if (handle == None):
    hits = Document.HitTest(world, 8f)         // 世界坐标，半径 8 DIP
    Selection.Clear(); Selection.AddRange(hits)
    SelectionChanged?.Invoke()
    if (Selection.Count == 0) { Invalidated; return }
    CloneMode = false                          // 新选择自动关闭克隆
    handle = Move                              // 点中即允许拖动

_activeHandle = handle
_dragFromView = viewPoint
_viewAtDragStart = ViewTransform
_dragStartViewBounds = ViewBoundsOf(SelectionBounds)

_dragTargets = Selection
_cloneDrag = false
if (CloneMode && handle == Move):
    clones = Selection.Select(Clone).ToList()
    Document.AddRange(clones)                  // 副本立即进入文档（渲染为已提交）
    _dragTargets = clones
    _cloneDrag = true

_dragSnapshot = _dragTargets.Select(Clone).ToList()
Invalidated?.Invoke()
```

`ViewBoundsOf(worldBounds)`：把世界 AABB 四角变换到视图后取 AABB（视图轴对齐框，句柄画在这里）。
`SelectionGeometry.HitTest` 顺序：
1. 旋转柄（`(centerX, top-28)`）距离 ≤ **12** → `Rotate`；
2. 四角：TL → TR → BR → BL，距离 ≤ **12**；
3. `bounds.Contains(point)` → `Move`；
4. 否则 `None`。

#### 拖拽（`UpdateSelectionDrag`）

```
viewDelta = SelectionGeometry.DragMatrix(_activeHandle, _dragStartViewBounds,
                                          _dragFromView, viewPoint, keepAspect:false)
Matrix3x2.Invert(_viewAtDragStart, out inverse)
worldDelta = _viewAtDragStart * viewDelta * inverse     // 视图拖动共轭到世界
ApplySnapshot(_dragTargets, _dragSnapshot)              // 每帧从快照重置
foreach target: target.Transform(worldDelta)
Document.Touch()
Invalidated?.Invoke()
```
- 绝对计算（从按下时快照），长拖动无累积误差。
- `DragMatrix`：
  - `Move` → `Translation(to-from)`；
  - `Rotate` → 中心 `bounds.Center`；`a=from-center,b=to-center`；任一 `|a|²<1e-4` → Identity；`angle=atan2(b)-atan2(a)`；`T(-c)*R(angle)*T(c)`；
  - 角柄 → 对角锚点；`sx=Ratio(from.X-anchor.X, to.X-anchor.X)`、`sy` 同理；`keepAspect` 时 `sx=sy=max(sx,sy)`；`T(-anchor)*S(sx,sy)*T(anchor)`；
  - `Ratio(from,to)`：`|from| < 1e-3 → 1`；否则 `clamp(to/from, 0.05, 20)`；
  - `None` → Identity。
- `ApplySnapshot`：逐笔 `RestorePoints(snapshot.Points)` 并恢复 `Attributes.BaseWidth`。

#### 结束（`EndSelectionDrag`）

```
targets = _dragTargets ?? Selection
before = _dragSnapshot
cloneDrag = _cloneDrag
清 _activeHandle/_dragSnapshot/_dragTargets/_cloneDrag

if cloneDrag:
    History.Push(DelegateAction("clone selection",
        apply  = AddRange(clones); Touch(); Invalidated,
        revert = RemoveAll(clones); Touch(); Invalidated))
else if before != null && before.Count == targets.Count:
    after = targets.Select(Clone).ToList()
    History.Push(DelegateAction("transform selection",
        apply  = ApplySnapshot(targets, after); Touch(); Invalidated,
        revert = ApplySnapshot(targets, before); Touch(); Invalidated))

Document.Touch(); Invalidated
```
- 克隆拖拽：副本在**按下时**已加入文档；撤销移除副本，重做加回（位置由 `targets` 当前状态保留）。
- 普通拖拽：一次撤销单元；即使没有实际位移也会压入一个 `"transform selection"` 空操作（点击选中会留下 no-op 撤销项——移植时可选择修正并同步两侧）。
- `TransformSelectionByGesture` 见 §8.7，每 move 一个单元。
- `DeleteSelection`：移除选区 → `"delete"`（apply 移除、revert 加回）→ 清空选区 → `SelectionChanged` + `Invalidated`；撤销不恢复选区。
- `TransformSelection(Matrix3x2 m)`：直接 `InkDocument.Transform(Selection,m)`，**不记历史**，只 `Invalidated`（供调用方自行管理撤销）。
- 矩形/套索框选：`InkDocument.HitTest(Rect)` 已存在，但 `InkController` 未接入；只有点选。

### 8.9 图形工具（含两步体）

#### 公共

- `StartStroke` 只用于 Pen/Highlighter（default 分支）；Shape 工具不创建 live stroke，预览走 `ShapePreview`。
- `ShapeAttributes()` = `CurrentAttributes.Clone()` + `TaperKind = None`（WPF 图形等宽）。**保留 `IsHighlighter`**（通常为 false）。
- `GenerateShapePreview(start,end)`：
  - 步骤 1（Cuboid）：`_stepPending + GenerateCuboidDepth(_stepAnchor,_stepSecond, |_stepAnchor.Y-end.Y|, attrs)`；
  - 步骤 1（Hyperbola/HyperbolaWithFoci）：`_stepPending + GenerateHyperbolaBranches(_stepAnchor,_stepK,end,withFoci,attrs)`；
  - 否则 `ShapeGenerator.Generate(kind,start,end,attrs)`。
- `ResetShapeState()`：若 `_stepPending.Count>0`，先把它作为一个独立的 `"shape"` 撤销单元提交（上游会丢，这里刻意修正）；清 `_shapeStep/_stepK/_stepAnchor/_stepSecond/_shapePreview/_shapeDragStart`。

#### `CommitShapeDrag(start,end)`

**Cuboid**

步骤 0（第一次拖拽，前面）：
```
_stepPending = Generate(Cuboid, start, end, attrs)      // 4 条独立边
_stepAnchor = (min(start.X,end.X), min(start.Y,end.Y))
_stepSecond = (max(start.X,end.X), max(start.Y,end.Y))
_shapeStep = 1
_shapePreview = copy(_stepPending)
Invalidated
return   // 保持 Shape 工具，等待深度拖拽
```

步骤 1（第二次拖拽，深度）：
```
depth = |_stepAnchor.Y - end.Y|
solid = _stepPending + GenerateCuboidDepth(_stepAnchor,_stepSecond,depth,attrs)
_stepPending = new; _shapeStep = 0; _shapePreview = new
CommitStrokes(solid, "shape")
Tool = Pen          // 无条件回笔，StickyShape 对两步体不生效
```

**Hyperbola / HyperbolaWithFoci**

步骤 0（渐近线，锁定 k）：
```
asymptotes = Generate(kind,start,end,attrs)               // 两条虚线渐近线
if asymptotes.Count == 0: Invalidated; return             // 退化拖拽，可重试
_stepPending = asymptotes
_stepK = |(end.Y-start.Y)/(end.X-start.X)|
_stepAnchor = start
_shapeStep = 1
_shapePreview = copy(_stepPending)
Invalidated; return
```

步骤 1（分支）：
```
branches = GenerateHyperbolaBranches(_stepAnchor,_stepK,end,kind==WithFoci,attrs)
kept = _stepPending; _stepPending = new; _shapeStep = 0
Tool = Pen                       // 先回笔（上游 :1579），再决定渐近线
FinishHyperbola(kept, branches)
```

`FinishHyperbola`：
- 若 `HyperbolaAsymptoteOption == Ask` 且 `AskRemoveAsymptotes` 非空：
  - `_shapePreview = asymptotes + branches`，`Invalidated`；
  - 异步 `await ask()`；异常视为 `false`；完成后 `CommitHyperbola`。
- 否则：`CommitHyperbola(..., removeAsymptotes: option == No)`。
- Ask 但没有对话框：保留渐近线（最不破坏）。
- `CommitHyperbola`：`committed = remove ? branches : asymptotes+branches`；清预览；非空则 `CommitStrokes(committed,"shape")`；`Invalidated`。**整个双曲线一个撤销单元**（修正上游的分裂历史 bug）。

**单步图形**

```
shapes = Generate(kind,start,end,ShapeAttributes())
_shapePreview = new
if shapes.Count > 0: CommitStrokes(shapes,"shape")
Invalidated
if (!StickyShape): Tool = Pen
```

#### 图形生成常量（`ShapeGenerator`）

| 常量 | 值 | 用途 |
|---|---:|---|
| `FullEllipseSegments` | 72 | 整椭圆采样段数 |
| `DashLength` | 5f | 虚线实段 |
| `DashPeriod` | 13.8f（5×2.76） | 虚线周期；gap = 8.8 |
| `DotPeriod` | 8.28f（3×2.76） | 点线间距 |
| `DotPressure` | 0.8f | 点线单点压力 |
| `EllipseDashStep` | 0.05（×π） | 椭圆弧虚线角宽 |
| `EllipseDashPeriod` | 0.083（×π）= 0.05×1.66 | 椭圆弧虚线周期 |
| `SolidEllipseRatio` | 2.646 | 圆柱/圆锥底面椭圆短轴 = 宽/2.646 |
| `Arrow` 头 | 30×10 | 普通箭头 |
| `AxisArrow` 头 | 20×7 | 坐标轴/平行线箭头 |
| `Parallel` 偏移 | ±25、±75 | 四条平行线；`x=25` |
| `Parabola` 采样 | 0.5 | 抛物线点间距 |
| `Hyperbola` 采样 | 0.5 | 分支点间距 |

25 种 `ShapeKind`（`Generate`）语义：

| 种类 | 几何/公式 |
|---|---|
| `Line` | `Stroke(start,end)` |
| `DashedLine` | 沿方向每 13.8 一段，实段 5，末段裁剪到终点 |
| `DotLine` | 每 8.28 一个压力 0.8 的单点 |
| `Arrow` | 折线 `start→end→barb1→end→barb2`；θ=atan2(start-end)；barb1=(end + (w·cos − h·sin, w·sin + h·cos))，barb2=(end + (w·cos + h·sin, −h·cos + w·sin))，w=30,h=10 |
| `ParallelLine` | 先 `SnapParallel` 角度吸附；再沿法线 `n=(sin,cos)` 偏移 −75/−25/25/75 四条线 |
| `CoordinateBoth` | x 轴 `(2sx−(ex−20), sy)→(ex,sy)`；y 轴 `(sx,2sy−(ey+20))→(sx,ey)`；20×7 头 |
| `CoordinateXOneWay` | 需 `|w|≥0.01`；x 轴尾 `sx+sign(−w)*25`；y 轴同 Both |
| `CoordinateYOneWay` | 需 `|h|≥0.01`；x 轴同 Both；y 轴尾 `sy+sign(−h)*25` |
| `CoordinateFirstQuadrant` | 需 `|w|,|h|≥0.01`；两轴尾均 25 |
| `Coordinate3D` | 三条轴箭头：`(sx,sy)→(sx+|w|,sy)`、`(sx,sy)→(sx,sy−|h|)`、`(sx,sy)→(sx−d/1.76, sy+d/1.76)`，`d=(|w|+|h|)/2` |
| `Rectangle` | 闭合 5 点 `start,(sx,ey),end,(ex,sy),start` |
| `RectangleFromCenter` | 5 点：`(2sx−ex,2sy−ey),(2sx−ex,ey),end,(ex,2sy−ey),(2sx−ex,2sy−ey)` |
| `Ellipse` | 中心 `(start+end)/2`，`rx=|w|/2, ry=|h|/2`，72 段 |
| `Circle` | 中心=start，R=distance(start,end) |
| `EllipseFromCenter` | 中心=start，`rx=|w|, ry=|h|` |
| `EllipseWithFoci` | 中心=start，`a=|w|, b=|h|`；`c=sqrt(|a²−b²|)`；焦点为压力 1 单点，仅长轴；a==b 不加焦点 |
| `DashedCircle` | 中心=start，R=distance；角度虚线 0.05π/0.083π，前后半各 13 段（典型 26 条） |
| `Hyperbola` / `HyperbolaWithFoci` | 第一步两条虚线渐近线：`(2sx−ex,2sy−ey)→end` 与 `(2sx−ex,ey)→(ex,2sy−ey)`；需 `|w|,|h|≥0.01` |
| `ParabolaUp` | `a=(sy−ey)/w²`；`i=0..|w| step .5`，右/左支 `(sx±i, sy−a·i²)` |
| `ParabolaRight` | `a=(sx−ex)/h²`；`i=0..|h| step .5`，上下支 `(sx−a·i², sy±i)` |
| `ParabolaWithFoci` | `p=h²/(2(sx−ex))`，`a=.5/p`；分支同上；焦点 `(sx−p/2, sy)` 压力 1 |
| `Cylinder` | 先按 Y 交换归一化 top-first；`halfW=|top.x−bottom.x|/2`，`halfH=halfW/2.646`；顶椭圆实线；底面 0..π 实线、π..2π 角度虚线；两侧线连接椭圆中心 |
| `Cone` | 同上底面拆分；顶点 `(midX, top.Y)`；两条斜线到底面椭圆中心线两端 |
| `Cuboid` | 第一步 4 条独立前边：`start→(sx,ey)`、`(sx,ey)→end`、`end→(ex,sy)`、`start→(ex,sy)` |

`GenerateCuboidDepth(frontTop, frontBottom, depth, attrs)`：

```
d = |depth|
off = (d, -d)
ft  = frontTop + off
fb  = (frontBottom.X + d, frontBottom.Y - d)
ftr = (frontBottom.X, frontTop.Y)

实线：
  ft → (fb.X, ft.Y)                 // 横上
  frontTop → ft                     // 斜左上
  ftr → (ftr.X+d, ftr.Y-d)          // 斜右上
  frontBottom → fb                  // 斜右下
  (fb.X, ft.Y) → fb                 // 竖右
虚线：
  (ft.X, fb.Y) → fb                 // 横下
  (frontTop.X, frontBottom.Y) → (ft.X, fb.Y)  // 斜左下
  ft → (ft.X, fb.Y)                 // 竖左
```
共 8 条棱：5 实 3 虚；`ShapeDrawingSemanticsTests` 钉住 23 条 stroke（5 实 + 18 虚线段）。

`GenerateHyperbolaBranches(center, k, end, withFoci, attrs)`：

```
dx=end.X-center.X; dy=end.Y-center.Y
if |dx|<0.01 || |dy|<0.01 || k<=0 → 空
fociOnX = |dy/dx| < k
if fociOnX:
    a = sqrt(|dx² - dy²/k²|); b = a*k
    steps = max(0, floor((|dx|-a)/0.5))
    for j 0..steps:
        i = a + j*0.5
        rY = sqrt(|k²i² - b²|)
        四支： (cx+i, cy−rY), (cx+i, cy+rY), (cx−i, cy−rY), (cx−i, cy+rY)
else:
    a = sqrt(|dy² - dx²k²|); b = a/k
    steps = max(0, floor((|dy|-a)/0.5))
    for j 0..steps:
        i = a + j*0.5
        rX = sqrt(|i²/k² - b²|)
        四支： (cx−rX, cy+i), (cx+rX, cy+i), (cx−rX, cy−i), (cx+rX, cy−i)
if withFoci:
    c = sqrt(a²+b²)
    fociOnX → (cx±c, cy)；否则 (cx, cy±c)，压力 1 单点
```
每个分支是独立 stroke；`IsRecognizedShape=true`，`Complete()`。

`Stroke(attrs, pts)`：逐点 `AddPoint(x,y,0.5f,t)`，`t += 8f`（毫秒）；使用 clone 的 attrs，因此**若 attrs.TaperKind 非 None 会走笔锋**——`InkController.ShapeAttributes()` 强制 None，测试直接用 `Attrs` 时则是 Tail。
`Dot(attrs, center, pressure)`：单点，`width = BaseWidth * (pressure/0.5)`，压力存储；`RestorePoints` 保留宽度。
所有 `Generate*` 结束后统一 `IsRecognizedShape=true; Complete()`。

#### 墨迹转形状（`TryRecognize`，仅 `name=="ink"`）

- `RecognizeShapes` 为 true 且提交的是自由笔迹时：
  - `_recentForRecognition` 追加本次提交；超过 **4** 个时移除最旧；移除已不在 `Document` 中的；空则返回。
  - `ShapeRecognizer.Recognize(_recentForRecognition)`。
  - 若结果是 `None` 或 `Triangle` → 不替换（三角形被刻意忽略）。
  - 替换属性：`committed[0].Attributes.Clone()` + `TaperKind=None`。
  - 生成：
    - `Circle`：`Generate(Circle, result.Center, result.Center + (max(Rx,Ry),0), attrs)`（因为 Circle 以 start 为圆心）；
    - `Ellipse`：`Generate(Ellipse, Center−(Rx,Ry), Center+(Rx,Ry), attrs)`；
    - `Line`：`Generate(Line, Corners[0], Corners[1], attrs)`；
    - `Rectangle`：`Stroke(attrs, corners + corners[0])` 闭合。
  - 从文档移除最近笔迹，加入替换；压入 `"recognize"`（apply=移除原+加替换，revert=移除替换+加原）；清空最近列表。
- 注意：原 `"ink"` 历史项仍在栈中；撤销一次会先回到识别前的原始笔迹（因为 `"recognize"` 是后压的）。
- 失败/无替换时不动文档。

### 8.10 擦除

#### 整笔擦（`EraserMode == WholeStroke` 或高级分档 `EraseByStroke`）

```
BeginErase(world, partialStroke:false):
    _erasing=true; _partialEraseGesture=false; _lastEraserPoint=world
    _erasedThisGesture.Clear(); _eraseBeforeSnapshot=null
    EraseAlong(world, world)

EraseAlong(from,to):
    radius = _gestureEraserRadius ?? EraserRadius
    dist = |to-from|
    steps = max(1, (int)(dist / max(4f, radius*0.5f)))
    for i in 0..steps:
        p = Lerp(from,to, i/steps)
        hits = Document.EraseAt(p, radius)      // 整笔：命中即删
        _erasedThisGesture.AddRange(hits)
    if any: Invalidated
```
- 采样步长 = `max(4, radius/2)`，避免快速划动跳过笔画。
- `EndErase`：若有被擦除的笔迹，压入 `"erase"`（apply 移除、revert 加回）；清 `_erasedThisGesture`；`Invalidated`。

#### 面积擦（`EraserMode == Area` 或手掌/`EraseByPoint`）

```
BeginErase(world, partialStroke:true):
    _eraseBeforeSnapshot = Document.Strokes.Select(Clone).ToList()   // 全文档快照
    EraseAlong(...)

EraseAlong:
    for each sample p:
        if (_partialEraseGesture):
            currentStrokes = Document.Strokes.ToArray()
            for each stroke:
                if (StrokeEraser.EraseDisc(stroke,p,radius,out remaining)):
                    Document.Remove(stroke)
                    if remaining.Count>0: Document.AddRange(remaining)
```
- `StrokeEraser.EraseDisc`（圆盘切断中心线）：
  - 单点：距离 ≤ `radius + width/2` → 整笔移除（返回 true）；否则保留原笔（remaining 加原笔，返回 false）。
  - 多点：`maxHalfWidth = max(point.Width)/2`；`cutRadius = radius + maxHalfWidth`；`cutRadiusSquared`。
  - 对每段 `a→b` 解二次方程求与圆盘的交点参数 `t1,t2`，只在 `(0,1)` 加入 cuts；`cuts` 初始 `{0,1}` 排序。
  - 对每个子区间取中点；若中点到圆心的距离² ≤ `cutRadius²` → 整段被擦，`Flush(current)`（结束当前碎片）；否则把区间两端插值点加入当前碎片（去重距离² > 1e-4）。
  - 任一段被擦 → `touched=true`；返回 true，并为每个非空碎片创建 `new InkStroke(attrs.Clone()){IsRecognizedShape=...}`，`RestorePoints(fragment)`，`Complete()`。
  - 未触及 → remaining 返回原笔，false（调用方不替换）。
  - 插值保留 `x,y,pressure,width,time`。
- `EndErase`（面积擦）：
  - `after = Document.Strokes.Select(Clone).ToList()`；
  - 若 `before.Count != after.Count` 或 `!SameStrokeSnapshot(before,after)`（逐笔点数与逐点位置比较）→ 压入 `"area erase"`：apply=`RestoreDocument(after)`，revert=`RestoreDocument(before)`。
  - `RestoreDocument`：`Document.Clear()` + `AddRange(strokes.Select(Clone))` + `Selection.Clear()` + `SelectionChanged` + `Invalidated`。
  - 快照比较**只看点位置**，不看宽度/属性；宽度由克隆保留。
- 手掌擦除：`PalmErases && ShouldErase` → `BeginErase(..., partialStroke:true)`，半径用 `EraserRadius`（`_gestureEraserRadius` 为 null）。
- 高级分档 `EraseByPoint` → 半径 = `EraserDiameter/2`，面积擦；`EraseByStroke` → 半径 2.5，整笔擦。

### 8.11 取消触摸墨迹 / 触摸对

`CancelTouchInk()`：
```
_scratchIds.Clear()
foreach id in _live.Keys:
    if (_touches.ContainsKey(id)) _scratchIds.Add(id)
foreach id in _scratchIds: _live.Remove(id)
```
- 只取消触摸指针产生的 live stroke，不动笔/鼠标。
- `TryGetTouchPair(a,b)`：按 `_touches.Values` 枚举顺序取前两个；不足 2 返回 false（不分配数组/LINQ）。

### 8.12 命令

- `Undo()` / `Redo()`：调用 `History`，成功才 `Invalidated`。
- `Clear()`：空文档直接返回；否则取全部、清空、压入 `"clear"`（apply 移除、revert 加回）、`Invalidated`。
- `Dispose()`：清 `_live/_touches/Selection/_shapeDragStart/_stepPending/_shapePreview`（不 Dispose 事件）。

### 8.13 行为差异/坑（移植决策点）

1. `MultiTouchEnabled` 语义反转（true=多指书写）；默认 true → **默认手势关闭**。
2. `GesturesTargetSelection` 读的是 `IsEnableTwoFingerRotationOnSelection`（名字只提旋转，实际决定手势是否作用于选区）。
3. 选区手势每 move 一个撤销单元，且缩放钳制只按单帧。
4. 点击选中会留下一个 `"transform selection"` 空撤销项。
5. Cuboid/Hyperbola 第二步**无条件** `Tool=Pen`，忽略 `StickyShape`；单步图形尊重 `StickyShape`。
6. `AskRemoveAsymptotes` 异步返回后不再校验文档是否已变化（潜在竞态）。
7. `Tool` setter 同值短路；`ShapeKind` setter 总是重置两步状态。
8. `InkStroke.Clone` 复用 `Id`，与渲染缓存按 Id 命中存在耦合；文档变更时全量失效掩盖了问题。
9. 单笔提交发两次 `Invalidated`；`Document.Changed` 在 `History.Push` 之前触发，宿主可能同步重绘一次，但几何不变。
10. `OnPointerMove` 的 `Shape` 分支在 `_activeHandle`/`_erasing` 之后；同一指针若处于选区拖拽不会再生成预览。
11. `CommitShapeDrag` 的 `start=end` 退化拖拽：Line 产生两点重合笔画；Hyperbola/Cuboid 被保护为空。
12. `_shapeDragStart` 用指针 id 键；多指同时画图形时最后一个 down 覆盖状态（图形工具不支持并发多指）。
13. 手势进行中若有第 3 根手指，`Update` 始终取 `_touches` 枚举顺序的前两个；其中一根抬起后配对可能改变而 `_prevA/_prevB` 不重基，下一帧会产生跳变（`Reset` 只在剩余触摸 <2 时发生）。移植时应显式维护两根活动手指并在配对变化时重基。
14. `Tool` 切换不会清理 `_activeHandle`/`_erasing`；正常 up 会先走选区/擦除分支，但异常工具切换（热键）下的中间态需在 HarmonyOS 侧补状态机收尾。

---

## 9. 形状识别（`ShapeRecognizer`）— 精确判定

> 这是「墨迹转形状」的判定器；与上游 InkAnalyzer 的边界**刻意不同**（托管几何拟合）。

常量：`MinSize = 40f`（世界 DIP）。

流程：

```
1. 合并所有笔画的点，累计 ArcLength。
2. pts.Count < 8 或 arc < 40 → None
3. bounds = 点集 AABB
   若 bounds.Width < 40 且 bounds.Height < 40 → None
4. centroid = 点集均值
5. TryLine → 命中则返回 Line
6. closed = distance(pts[0], pts[^1]) < max(12, arc*0.18)
   不闭合 → None
7. TryCircle → 若 residual < 0.10 且 aspect=bounds.W/bounds.H（H<=0.01时取1）在 (0.82,1.22) → Circle
8. TryEllipse → residual < 0.14 → Ellipse
9. TryPolygon → 3 角 → Triangle（控制器忽略）；4 角 → Rectangle
10. 否则 None
```

`TryLine`：
```
协方差 sxx,syy,sxy（相对 centroid）
trace=sxx+syy；trace<1e-6 → false
det=sxx*syy-sxy²
disc=sqrt(max(0, trace²/4 - det))
major=trace/2+disc；minor=trace/2-disc
major<1e-6 → false
spread=sqrt(max(0,minor)/n)          // 垂直 RMS 散布
length=2*sqrt(major/n)
length<40 或 spread/length>0.035 → false
axis=normalize(sxy, major-sxx)；NaN 或 |axis|²<0.5 → (1,0)
投影 t=dot(p-centroid,axis)，取 min/max → corners = centroid+axis*{min,max}
confidence = 1 - spread/length
```

`TryCircle`（Kåsa 代数拟合）：
```
以 guess(centroid) 为原点：
x=p.X-guess.X, y=p.Y-guess.Y, z=x²+y²
累加 sxx,sxy,syy,sxz,syz,sx,sy,sz
解 3x3：
[[sxx,sxy,sx],[sxy,syy,sy],[sx,sy,n]]·[a,b,c] = [-sxz,-syz,-sz]
cx=-a/2, cy=-b/2, r²=cx²+cy²-c
r²<=1 → false
center=(cx+guess.X, cy+guess.Y)
residual = sqrt(Σ(dist(p,center)-r)² / n) / r
radius>1 才有效
```

`TryEllipse`：
```
rx=max(bounds.W/2,1), ry=max(bounds.H/2,1)
对每点 u=(x-cx)/rx, v=(y-cy)/ry
residual=sqrt(Σ(sqrt(u²+v²)-1)² / n)
总返回 true（由调用方阈值过滤）
```

`TryPolygon`：
```
simplified = RDP(pts, epsilon=6)
若 simplified.Count<4 → false
对 i=1..count-2:
    v1=normalize(s[i]-s[i-1]); v2=normalize(s[i+1]-s[i])
    angle=acos(clamp(dot(v1,v2),-1,1))*180/π
    if angle>45° 且 (found 为空 或 距上一个 found 点 >20):
        found.Add(s[i])
若 found.Count 为 3 或 4 → true，corners=found
```
RDP：首尾保留，栈迭代，最大距离 > epsilon 则分割。

`Solve3`：列主元高斯消元，主元绝对值 `< 1e-12` 失败；结果含 NaN 失败。

---

## 10. 设置 / JSON 字段名

### 10.1 `Settings.json` 中与墨迹直接相关的键

> 文件由 `System.Text.Json` 读写，`JsonPropertyName` 显式指定；键名必须两侧一致。路径：`%APPDATA%` 下的 `Settings.json`（见 `SettingsStore`）。

| JSON 路径 | 类型 | 默认 | 墨迹侧用途 |
|---|---|---:|---|
| `canvas.inkWidth` | double | 2.5 | `InkController.CurrentAttributes.BaseWidth = clamp(value,0.5,60)` |
| `canvas.inkAlpha` | int | 140 | `HighlighterAlpha = clamp(value,20,240)` |
| `canvas.inkStyle` | int | 0 | 0→Tail、1→Velocity、2→None |
| `canvas.eraserSize` | int | 2 | 索引 0..4 → 半径 `45×[0.5,0.8,1,1.25,1.8]` = 22.5/36/45/56.25/81 DIP |
| `canvas.eraserType` | int | 0 | 0=图标切换、1=面积擦、2=线条擦（菜单文案；控制器用 `EraserMode`） |
| `canvas.hyperbolaAsymptoteOption` | enum | Ask | `Yes=0/No=1/Ask=2`（`OptionalOperation`） |
| `canvas.usingWhiteboard` | bool | false | 白板主题（非墨迹算法，但影响颜色） |
| `gesture.isEnableMultiTouchMode` | bool | true | `MultiTouchEnabled`（true=多指书写/禁手势） |
| `gesture.isEnableTwoFingerZoom` | bool | true | `TwoFingerManipulator.AllowZoom` |
| `gesture.isEnableTwoFingerTranslate` | bool | true | `AllowTranslate` |
| `gesture.isEnableTwoFingerRotation` | bool | false | `AllowRotation`（视图手势） |
| `gesture.isEnableTwoFingerRotationOnSelection` | bool | false | `GesturesTargetSelection`（手势是否作用于选区） |
| `gesture.matrixTransformCenterPoint` | enum | CanvasCenterPoint(0) | 1=选区中心、2=手势中心、0=画布中心 |
| `gesture.AutoSwitchTwoFingerGesture` | bool | true | 墨迹侧未使用 |
| `advanced.isSpecialScreen` | bool | false | 特殊屏分档 |
| `advanced.isQuadIR` | bool | false | 接触宽度取几何平均 |
| `advanced.touchMultiplier` | double | 0.25 | 橡皮直径倍率；为 0 且特殊屏时禁用橡皮 |
| `advanced.nibModeBoundsWidth` | int | 10 | 笔尖模式接触宽度界 |
| `advanced.fingerModeBoundsWidth` | int | 30 | 手指模式接触宽度界；同时作为 `PalmContactThreshold` 下限 `max(8,值)` |
| `advanced.nibModeBoundsWidthThresholdValue` | double | 2.5 | 笔尖面积擦阈值倍率 |
| `advanced.fingerModeBoundsWidthThresholdValue` | double | 2.5 | 手指面积擦阈值倍率 |
| `advanced.nibModeBoundsWidthEraserSize` | double | 0.8 | 笔尖面积擦直径系数 |
| `advanced.fingerModeBoundsWidthEraserSize` | double | 0.8 | 手指面积擦直径系数 |
| `startup.isEnableNibMode` | bool | false | `PalmRejector.NibMode` |
| `inkToShape.isInkToShapeEnabled` | bool | true | `RecognizeShapes` |
| `appearance.theme` | int | 0 | 0=跟随系统、1=浅色、2=深色（非墨迹） |

补充：
- `PenPalette` 宽度滑杆刻度 **1–30、步进 1**，持久化值 = 刻度/2（`ToStoredPenWidth = w/2`，`ToMenuPenWidth = w*2`）；`AppServices` 再把读入值 clamp 到 0.5–60。
- `PenPalette` 塑料笔风格下拉索引 0..2 直接映射 `Tail/Velocity/None`。
- `ResetToRecommendation()` 的墨迹相关值：`inkWidth=2.5`、`inkAlpha=80`、`inkStyle=0`、`eraserSize=1`、`eraserType=0`、`hyperbolaAsymptoteOption=Yes`、`isEnableMultiTouchMode=true`、`isEnableTwoFingerZoom=false`、`isEnableTwoFingerTranslate=true`、`isEnableTwoFingerRotation=false`、`isEnableTwoFingerRotationOnSelection=true`、`matrixTransformCenterPoint=CanvasCenterPoint`、`isEnableNibMode=false`。

### 10.2 `.icink` 墨迹格式（`InkFileStore`）

- 扩展名 `.icink`；`FormatVersion = 1`；`System.Text.Json` **默认命名策略**（无 `JsonNamingPolicy`，因此是属性名原样 PascalCase）。
- 顶层：

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
      "Points": [[x, y, pressure, width, timeMs], ...]
    }
  ]
}
```

字段语义：

| 字段 | 类型 | 说明 |
|---|---|---|
| `Version` | int | 1 |
| `Strokes` | array | 笔画 |
| `Strokes[].Color` | uint | `InkColor.PackedArgb`（A<<24\|R<<16\|G<<8\|B） |
| `Strokes[].Width` | float | `Attributes.BaseWidth`（全宽） |
| `Strokes[].Highlighter` | bool | `IsHighlighter` |
| `Strokes[].HighlighterAlpha` | byte | 缺省 96 |
| `Strokes[].Taper` | int | `TaperKind`（0/1/2） |
| `Strokes[].Shape` | bool | `IsRecognizedShape` |
| `Strokes[].Points[]` | float[5] | `[x, y, pressure, width, timeMs]`；读取时 `Length>=4`，第 5 个缺省 0 |
- 加载时 `RestorePoints` **原样恢复宽度**，不重跑笔锋；`Complete()`。
- 注意：这是本移植的兼容断裂（上游 `.isf` 不支持）；HarmonyOS 侧必须同字段同语义，schema 放 `parity/schemas/`。

---

## 11. UI 度量 / 颜色 / 图标总表（本层内）

| 项 | 值 | 位置 |
|---|---:|---|
| 选区句柄命中半径 | 12 DIP | `SelectionGeometry.HandleRadius` |
| 旋转柄距上边 | 28 DIP | `SelectionGeometry.RotateOffset` |
| 角缩放柄绘制半径 | 6 DIP | `InkSceneRenderer.DrawSelection` |
| 旋转柄绘制半径 | 7 DIP | 同上 |
| 选区边线 / 柄描边 | 1.5 DIP | 同上 |
| 选区强调色 | #0078D4 = ARGB(255,0,120,212) | 同上 |
| 选区柄填充 | 白色 ARGB(255,255,255,255) | 同上 |
| 整笔/点选命中半径（世界） | 8 DIP | `InkController.BeginSelectionInteraction` |
| 面积擦默认半径 | 16 DIP（控制器默认；应用按设置覆盖） | `InkController.EraserRadius` |
| 面积擦半径档位 | 22.5 / 36 / 45 / 56.25 / 81 DIP | `45 × [0.5,0.8,1,1.25,1.8]` |
| 整笔擦固定半径 | 2.5 DIP | `BoardWindow` / 高级分档 |
| 手掌接触阈值 | 30 DIP（`max(8,FingerModeBoundsWidth)`） | `PalmRejector` |
| 手指翻页位移阈值 | 60 DIP | `InkController.OnPointerUp` |
| 覆盖层活动区余量 | 4 物理像素 | `OverlayInkPresenter.LiveRegion` |
| live target 容量粒度 | 64 物理像素 | `CompositeLiveRegion` |
| 隐藏态像素 | 预乘 `(1,1,1,1)` / ARGB(1,1,1,1) | `UpdateFrame` / committed clear |
| 覆盖层渲染目标 DPI | 96 | `CreateTarget` |
| 覆盖层像素格式 | `B8G8R8A8UIntNormalized` + `Premultiplied` | 同上 |
| 笔尖窗口 | 10 采样；起笔 4；笔尖比例 0.2 | `TailTaper` |
| 速度笔锋比例钳制 | 0.15–1.8 | `VelocityTaper` |
| 形状识别最小尺寸 | 40 DIP | `ShapeRecognizer.MinSize` |
| 形状等宽 | `TaperKind.None` | `InkController.ShapeAttributes` |
| 撤销容量 | 200 | `InkController.History` |
| 最近识别笔画数 | 4 | `_recentForRecognition` |
| 图形虚线 | 实 5 / 周期 13.8 | `ShapeGenerator` |
| 点线间距/压力 | 8.28 / 0.8 | `ShapeGenerator` |
| 椭圆弧虚线 | 0.05π / 0.083π | `ShapeGenerator` |
| 整椭圆段数 | 72 | `ShapeGenerator` |
| 圆柱/圆锥底面短轴比 | 1/2.646 | `ShapeGenerator` |

本层没有字体、图标码点或固定字号（选区框是矢量绘制；`InkSurface` 填满父容器）。

---

## 12. 平台依赖清单与 HarmonyOS 对应建议

### 12.1 Win32 / COM / Win2D / WinUI 具体 API

**Win2D（`Microsoft.Graphics.Canvas`）**
- `CanvasDevice.GetSharedDevice()`
- `CanvasRenderTarget(device,w,h,96,DirectXPixelFormat.B8G8R8A8UIntNormalized,CanvasAlphaMode.Premultiplied)`
- `CreateDrawingSession()`、`CanvasDrawingSession.Clear/FillGeometry/DrawLine/FillCircle/DrawCircle/Transform`
- `CanvasPathBuilder.BeginFigure/AddLine/EndFigure(CanvasFigureLoop.Closed)`
- `CanvasGeometry.CreatePath/CreateGroup(...,CanvasFilledRegionDetermination.Winding)`
- `CanvasRenderTarget.GetPixelBytes(IBuffer)` / `GetPixelBytes(buffer,x,y,w,h)`
- `Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl`、`CanvasDrawEventArgs`（`InkSurface`）

**Windows 运行时**
- `Windows.UI.Color.FromArgb`
- `Windows.Graphics.DirectX.DirectXPixelFormat`
- `Windows.Storage.Streams.IBuffer` + `System.Runtime.InteropServices.WindowsRuntime.AsBuffer()`

**WinUI 3**
- `Microsoft.UI.Xaml.Controls.UserControl`
- `Microsoft.UI.Xaml.Input.PointerRoutedEventArgs`、`PointerDeviceType`、`ContactRect`、`PointerId`、`Timestamp`
- `CapturePointer/ReleasePointerCapture`、`PointerCaptureLost`、`ManipulationModes.None`
- `CanvasControl.Invalidate()` / `Draw` 事件

**Win32（`LayeredInkWindow`，覆盖层输入/呈现）**
- 窗口：`RegisterClassW`、`CreateWindowExW`，样式 `WS_EX_LAYERED(0x00080000)|WS_EX_TOOLWINDOW(0x00000080)|WS_EX_TOPMOST(0x00000008)|WS_EX_NOACTIVATE(0x08000000)`，`WS_POPUP`
- 触摸：`RegisterTouchWindow(hwnd, TWF_FINETOUCH(0x1)|TWF_WANTPALM(0x2))`
- 反馈抑制：`SetWindowFeedbackSetting`，抑制 `FEEDBACK_TOUCH_CONTACTVISUALIZATION(1)`、`PEN_TAP(3)`、`PEN_DOUBLETAP(4)`、`PEN_PRESSANDHOLD(5)`、`PEN_RIGHTTAP(6)`、`TOUCH_TAP(7)`、`TOUCH_DOUBLETAP(8)`、`TOUCH_PRESSANDHOLD(9)`、`TOUCH_RIGHTTAP(10)`、`GESTURE_PRESSANDHOLD(11)`
- 指针：`GetPointerInfo`、`GetPointerPenInfo`、`GetPointerTouchInfo`、`EnableMouseInPointer`、`IsMouseInPointerEnabled`
- 消息：`WM_POINTERDOWN(0x0246)`、`WM_POINTERUPDATE(0x0245)`、`WM_POINTERUP(0x0247)`、`WM_POINTERCAPTURECHANGED(0x024F)`、`WM_CAPTURECHANGED(0x0215)`、`WM_CANCELMODE(0x001F)`、`WM_HOTKEY(0x0312)`
- `PeekMessageW(PM_NOREMOVE(0x0000)|PM_NOYIELD(0x0002))` 用于 `HasQueuedPointerMessage`
- 压力：笔/触摸 `pressure/1024`，0→0.5；接触矩形 `(right-left)/DpiScale`、`(bottom-top)/DpiScale`
- 呈现：`CreateDIBSection`（32bpp、`biHeight=-height` 顶向下）、`UpdateLayeredWindow`（`AC_SRC_OVER`、`AlphaFormat=1`、`SourceConstantAlpha=255`、`ULW_ALPHA=0x02`）
- DPI：`GetDpiForWindow(hwnd)/96`
- 捕获：`SetCapture/ReleaseCapture`
- 鼠标回退：`WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP`，坐标 `lParam` 低/高 16 位 `/DpiScale`，压力 0.5，接触 0，`Id=1`，`TimeMs=Environment.TickCount`

**无 COM**：本层没有 COM/Office/WPS 调用（PPT COM 在 Features）。

### 12.2 HarmonyOS 候选映射（需在 `/harmony-os` references 核对后再定稿）

| Windows 能力 | HarmonyOS 候选 | 注意 |
|---|---|---|
| Win2D `CanvasGeometry`/`CanvasDrawingSession` | ArkUI `Canvas` + `CanvasRenderingContext2D` + `Path2D`；离屏 `OffscreenCanvas` | 必须确认单次 `fill` 是否支持非零环绕多子路径；若只支持 evenodd，自交笔迹会镂空 |
| `CanvasRenderTarget` 已提交层 | `OffscreenCanvas` / `PixelMap` 缓存；或每次整场景重绘 | 平板/2in1 可使用离屏 Canvas 像素缓存 |
| `GetPixelBytes` 局部回读 | `getImageData(x,y,w,h)` / `PixelMap.readPixels` | 回读代价高，优先整帧重绘或分层组件 |
| `UpdateLayeredWindow` 透明覆盖 | 2in1 浮动窗口/系统覆盖层（`window.createWindow` TYPE_FLOAT 等，需权限） | **最大风险**：跨应用全屏透明批注可能不可行；手机明确不做全覆盖批注（设计文档 §7） |
| `WM_POINTER` / `PointerRoutedEventArgs` | `onTouch(event: TouchEvent)` / `onMouse` / `onHover`；Pen Kit（`@kit.Penkit`）预测/跟手 | 需确认 `TouchObject` 是否提供压力、接触面积、历史点、工具类型 |
| `ContactWidth/Height` | 可能无等价字段 | 手掌/高级分档退化风险；需以笔优先 + 工具类型 + 经验阈值兜底 |
| `ManipulationMode.None` | `gesture` 配置/`onTouch` 原始处理；避免系统手势抢占 | 双指手势与系统返回/缩放手势冲突需显式禁用/仲裁 |
| `DpiScale` | `vp`/`px` 转换、`display.getDefaultDisplaySync().densityPixels` | 设计 token 统一 DIP↔vp |
| `Matrix3x2`（行向量） | 自写 3x2 矩阵（或 `@ohos.matrix4`，语义需核对） | 必须保持行向量与乘法顺序，否则手势方向/枢轴错 |
| `IBuffer` 零拷贝 | `ArrayBuffer`/TypedArray | 预乘 BGRA 与 OHOS 像素格式需转换（RGBA/BGRA） |

---

## 13. 钉住行为的测试

> 以下为源码中存在的测试；**本次未运行**。括号内为断言要点。

### 13.1 `tests/InkCanvas.Core.Tests/TaperTests.cs`
- `LastLiveFrameEqualsCommittedGeometry`（None/Tail/Velocity 三型）：提交前后逐点宽度与 `BuildOutline` 完全相等。
- `NoPointEverGetsNarrower`（三型）：逐样本到达后所有已绘制点宽度不减（误差 1e-4）。
- `TailIsAlreadyTaperedWhileStillDrawing`：笔尖宽度 < 中间宽度，中间 ≈ 全宽。
- `VelocityTaperDoesNotRewriteEarlierPoints`：后续快速段不改变前 30 点宽度（精确相等）。
- `NoneTaperIsUniform`：所有点宽度 = BaseWidth。
- `HighlighterNeverTapers`：荧光笔 `TaperKind==None` 且所有点宽度=BaseWidth。
- `OutlineIsDeterministic`：两次相同采样得到相同轮廓。
- `SinglePointProducesAClosedDot`：单点轮廓 ≥8 点，距中心 0.1–8。

### 13.2 `tests/InkCanvas.Core.Tests/StrokeRegionTests.cs`
- 自交四段笔迹：`BuildUnionContours` 数量 = `Count-1`，所有轮廓 `SignedArea>0`。
- 正/反方向线段轮廓都为正面积。
- 单点仍只有一个填充区域且正面积。

### 13.3 `tests/InkCanvas.Core.Tests/SelectionGeometryTests.cs`
- 四角命中、内部 Move、远处 None、旋转柄命中且在上方。
- 角拖拽绕对角缩放（对角不动，右下到 300,300）。
- Move 平移；Rotate 绕中心；keepAspect 两轴同比；退化拖拽不产生 NaN；None 为 Identity。

### 13.4 `tests/InkCanvas.Core.Tests/PalmRejectorTests.cs`
- 小接触非手掌、大接触手掌；手掌不画但擦；指尖画不擦；笔优先抑制触摸；可关闭优先；抬笔解除抑制；抬掌清除；Reset 清空。

### 13.5 `tests/InkCanvas.Core.Tests/DomainTests.cs`
- 识别：圆、椭圆、直线、微小图形拒绝、开放涂鸦拒绝/直线。
- 图形：所有 `ShapeKind` 产出非空且完成；3D 坐标 3 轴；虚线 >3 段；圆在任意拖拽比例下接近正圆。
- 撤销：undo/redo 往返、Push 清 redo、容量丢最旧。
- 文档：`EraseAt` 只删被触及笔；`Transform` 移动并缩放宽度。
- 设置：JSON 往返；旧 WPF 风格 JSON 兼容。

### 13.6 `tests/InkCanvas.Core.Tests/ShapeDrawingSemanticsTests.cs`（工作树未跟踪）
- 中心+半径语义（圆/虚线圆/中心矩形/中心椭圆）；焦点椭圆压力 1 点；相等 a==b 无焦点。
- 平行线 4 条 ±25/±75；30°/45°/60°/垂直吸附；无吸附带保持原样。
- Cuboid 第一步 4 条独立边；第二步 5 实 3 虚（18 段虚线，共 23 条）。
- 双曲线第一步 22 条虚线渐近线；分支锁定 k、焦点在 X/Y 轴。
- 圆柱 17 条、圆锥 16 条、底面半实半虚、侧面连接椭圆中心。
- 虚线 8 段（100/13.8）、点线 10 个（80/8.28）、箭头 30×10、坐标轴箭头 20×7。
- 抛物线端点精确、焦点在 `sx-p/2`。

### 13.7 `tests/InkCanvas.Core.Tests/PixelCompositingTests.cs`
- 不透明源替换；全透明源不动；半透明预乘 over 不透明黑；偏移只写目标像素；越界裁剪；负偏移裁剪；空源 no-op。

### 13.8 `tests/InkCanvas.Core.Tests/ReplayTests.cs`
- 回放时钟：暂停/播放/速度/结束/Seek 钳制/进度/切换；回放会话：起点前空、结束全量、可见点数单调、保留存储宽度。

### 13.9 运行时自检 `src/InkCanvas.App/Diagnostics/SelfTest.cs`（`--selftest`）
- 笔/荧光笔 × 鼠标/笔/触摸的 tap：提交可见点、不重复提交、undo/redo。
- 「提交几何 == 最后一帧」逐点宽度与轮廓、活动期笔尖变细。
- 荧光笔等宽。
- 3D 坐标 3 轴、圆。
- 选区角拖拽缩放且 undo 恢复；删除选区 undo 恢复。
- 回放单调。
- GPU 回读远低于全屏每帧；报告 `drawingReadbackBytesPerFrame` 等。
- 结论写入 `selftest.json`；这是**应用内自检**，不是单元测试。

---

## 14. 逐功能移植分类

> `pure-logic`：可 1:1 移植为 ArkTS∩TS 严格子集，用黄金向量钉住；`adapt`：算法保留但需替换输入/渲染/窗口 API；`platform-bound`：强绑 Windows，需重写或降级；`drop-candidate`：建议不移植并说明。

| id（kebab） | 功能 | 文件 | 分类 | 说明 |
|---|---|---|---|---|
| `stroke-model` | 笔画/点/属性/颜色/包围盒 | `Core/Ink/InkPoint.cs`、`InkStroke.cs`、`StrokeAttributes.cs`、`InkColor.cs` | pure-logic | 无平台依赖；`System.Numerics` 需等价实现 |
| `ink-document` | 文档、命中、整笔擦 | `Core/Ink/InkDocument.cs` | pure-logic | `Rect` 一并移植 |
| `causal-taper` | 因果增量笔锋（None/Tail/Velocity） | `Core/Ink/TaperProfile.cs` | pure-logic | 黄金向量重点：全序列宽度 |
| `stroke-union-geometry` | 逐段 capsule + 正向绕组 | `Core/Ink/StrokeGeometry.cs` | pure-logic | 黄金向量：轮廓点序/面积 |
| `stroke-disc-eraser` | 圆盘切断中心线 | `Core/Ink/StrokeEraser.cs` | pure-logic | 黄金向量：碎片点/属性 |
| `undo-history` | 有界撤销栈 | `Core/History/UndoStack.cs` | pure-logic | 闭包在 ArkTS 用函数对象 |
| `selection-geometry` | 句柄命中/拖拽矩阵 | `Core/Selection/SelectionGeometry.cs` | pure-logic | 黄金向量：矩阵数值 |
| `two-finger-math` | 双指平移/缩放/旋转 | `Ink/Manipulation/TwoFingerManipulator.cs` | pure-logic | 注意行向量与累计钳制 |
| `gesture-pivot` | 手势枢轴选项 | `Core/Ink/MatrixTransformCenter.cs` | pure-logic | 纯枚举选择 |
| `touch-contact-classification` | 触摸分档 | `Core/Input/TouchContactClassifier.cs` | pure-logic | 输入 `contactWidth` 需平台提供 |
| `palm-rejection` | 手掌判定/笔优先 | `Platform/Input/PalmRejector.cs` | adapt | 依赖接触矩形；OHOS 可能无等价字段 |
| `ink-input-routing` | 输入状态机（down/move/up） | `Ink/InkController.cs` | adapt | 逻辑 1:1；Pointer 事件源替换 |
| `ink-commit-pipeline` | 活动层→文档→历史→失效 | `Ink/InkController.cs`、`Core/Ink/InkDocument.cs`、`UndoStack.cs` | adapt | 保留提交顺序与单次失效 |
| `ink-shape-tool` | 图形工具（含两步体/粘滞/渐近线询问） | `Ink/InkController.cs`、`Core/Shapes/ShapeGenerator.cs` | pure-logic | 异步询问在 ArkTS 用 Promise |
| `shape-catalogue` | 25 种预设图形几何 | `Core/Shapes/ShapeGenerator.cs` | pure-logic | 常量密集，黄金向量重点 |
| `shape-recognition` | 墨迹转形状 | `Core/Recognition/ShapeRecognizer.cs`、`Ink/InkController.cs` | pure-logic | 与 InkAnalyzer 结果不同是设计选择 |
| `ink-area-eraser` | 面积擦/快照撤销 | `Ink/InkController.cs`、`Core/Ink/StrokeEraser.cs`、`InkDocument.cs` | pure-logic | 快照内存开销需评估 |
| `ink-whole-stroke-eraser` | 整笔擦 | `Ink/InkController.cs`、`InkDocument.cs` | pure-logic | 命中用 BaseWidth |
| `ink-selection-editing` | 点选/拖拽/克隆/删除 | `Ink/InkController.cs`、`Core/Selection/SelectionGeometry.cs` | pure-logic | 视图↔世界共轭矩阵 |
| `stroke-renderer` | 单笔填充/缓存 | `Ink/Rendering/StrokeRenderer.cs` | adapt | 算法保留；Win2D → ArkTS Path2D/Canvas |
| `scene-renderer` | 分层场景/选区框 | `Ink/Rendering/InkSceneRenderer.cs` | adapt | 绘制调用替换，度量不变 |
| `overlay-presenter` | 透明覆盖层局部刷新/延迟 | `Ink/Rendering/OverlayInkPresenter.cs` | platform-bound | 依赖分层窗口/GPU 回读；OHOS 无等价路径则降级整帧或放弃覆盖层 |
| `layered-window-input` | Win32 分层窗口/WM_POINTER | `Platform/Windows/LayeredInkWindow.cs` | platform-bound | 需用 OHOS 窗口/触摸 API 重写；权限风险 |
| `ink-surface-control` | WinUI CanvasControl 宿主 | `Controls/Controls/InkSurface.cs` | adapt | ArkUI `Canvas` + `onTouch` |
| `pixel-compositing` | 预乘 BGRA over | `Core/Imaging/PixelCompositing.cs` | pure-logic | 若走高架 Canvas 合成可保留为工具/测试 |
| `icink-format` | `.icink` JSON | `Features/Persistence/Stores.cs` | pure-logic | 字段名/版本严格一致 |
| `ink-settings-wiring` | Settings→Controller 映射 | `App/Services/AppServices.cs`、`Core/Settings/AppSettings.cs` | adapt | 键名与钳制值必须一致 |
| `ink-slide-gesture` | 放映手指翻页 | `Ink/InkController.cs`、`App/Services/ShellController.cs` | adapt | 60 DIP 为移植自定 |
| `ink-latency-diagnostics` | 输入→呈现延迟统计 | `Ink/Rendering/OverlayInkPresenter.cs`、`SelfTest.cs` | adapt | 无 `UpdateLayeredWindow` 时改为帧时间/上屏回调统计 |

**建议的 ArkTS 分层落位**：
- `harmony/core`：`stroke-model`、`ink-document`、`causal-taper`、`stroke-union-geometry`、`stroke-disc-eraser`、`undo-history`、`selection-geometry`、`two-finger-math`、`gesture-pivot`、`touch-contact-classification`、`shape-catalogue`、`shape-recognition`、`pixel-compositing`、`icink-format`。
- `harmony/platform`：`palm-rejection`、`layered-window-input` 的 OHOS 替代（窗口/触摸/笔）、指针样本类型、DPI/vp 换算、能力层。
- `harmony/ink`：`ink-input-routing`、`ink-commit-pipeline`、`ink-shape-tool`、`ink-area-eraser`、`ink-whole-stroke-eraser`、`ink-selection-editing`、`stroke-renderer`、`scene-renderer`、`overlay-presenter`（若可行）。
- `harmony/controls`：`ink-surface-control`。
- `harmony/features`：`icink-format` 的存储服务、自动保存。
- `harmony/entry`：`ink-settings-wiring`、`ink-slide-gesture` 接线、延迟诊断。

**黄金向量建议**（与设计文档 §5.3 对齐）：
1. `taper-vectors.json`：三型笔锋，逐步采样序列 → 每点宽度（浮点容差 1e-5）。
2. `stroke-contour-vectors.json`：直线、曲线、单点、自交四段 → `BuildUnionContours` 点数/首末点/签名面积。
3. `eraser-vectors.json`：整笔命中集、面积擦碎片（点数/首末点/宽度）。
4. `selection-vectors.json`：命中枚举、DragMatrix 四个角 + 旋转 + keepAspect + 退化。
5. `gesture-vectors.json`：双指序列 → 每帧 delta 矩阵/最终 transform/钳制边界。
6. `shape-vectors.json`：25 种 `ShapeKind` 的 stroke 数量、每笔点数、首末点、ArcLength。
7. `recognition-vectors.json`：圆/椭圆/线/矩形/涂鸦/微小 → 枚举与中心/半径/角点。
8. `touch-classification-vectors.json`：QuadIR/特殊屏/笔尖/阈值边界/PPT 手势 → intent+diameter。
9. `icink-roundtrip-vectors.json`：字段与宽度往返。

---

## 15. 移植风险与待决问题

### 15.1 高风险

1. **全屏透明覆盖层**：`OverlayInkPresenter` + `LayeredInkWindow` 依赖 Win32 分层窗口与跨应用置顶；HarmonyOS 对跨应用覆盖/悬浮窗有权限与窗口类型限制。若不可行，「屏幕批注」需降级为「在自家画布/分屏内批注」，手机明确不做（设计文档 §7）。这也决定了 `overlay-presenter`/`layered-window-input` 是 platform-bound 而非直接移植。
2. **接触面积/手掌分档**：`PalmRejector` 和 `TouchContactClassifier` 的输入是 `ContactWidth/ContactHeight`；HarmonyOS `TouchObject` 是否有等价字段需核实。若没有，面积擦/手掌擦/特殊屏分档无法 1:1，需以笔优先 + 工具类型 + 固定阈值降级，并写入 `parity/api-matrix.md`。
3. **非零环绕填充**：ArkTS Canvas 的 `fill` 是否支持一次填充多子路径且非零环绕，是自交笔迹/半透明荧光笔正确性的关键。若不支持，需离线求并集（多边形布尔）或逐段填充到离屏再统一合成。
4. **矩阵约定**：`System.Numerics.Matrix3x2` 是行向量；ArkTS 侧若用列向量或 `matrix4`，手势/选区共轭会整体错位。必须以向量钉住 `M11..M32` 与乘法顺序。
5. **每帧局部回读**：OHOS `getImageData`/`readPixels` 代价可能高于 Win2D；实时笔迹应优先整帧重绘或使用独立 Canvas 图层，避免回读。
6. **事件频率/合帧**：WinUI/Win32 的指针消息逐条同步处理；OHOS 触摸事件可能批量/合帧。需保留「到达即写宽度」的因果性，并评估 `onTouch` 的 `getHistoricalPoints()` 以补采样。

### 15.2 行为一致性问题（需产品决策）

- 选区手势每 move 一个撤销单元 vs 整手势一个单元（建议两侧统一修正，并同步黄金向量）。
- `StickyShape` 对两步体无效（建议统一为尊重粘滞或明确保留差异）。
- 点击选中产生空撤销项（建议修正）。
- `MultiTouchEnabled` 命名反直觉（ArkTS 侧建议改名为 `multiTouchWriting` 并在设置层保留原 JSON 键）。
- 识别三角形被忽略（`TryRecognize` 显式跳过 `Triangle`），但识别器会返回 Triangle；是否在 OHOS 侧启用需产品决定。

### 15.3 未验证声明

- 本报告的所有代码结论来自静态阅读；**未运行 `dotnet test`、未启动 InkCanvas.App、未做 HarmonyOS 编译/实机**。
- 仓库既有文档声明：笔锋、选区手势、图形生成、回放、更新、设置兼容有单元测试/部分实机验证；`PalmRejector` 高级分档、`MatrixTransformCenter`、3.4.0 新多指语义/两步体/克隆等标注为「未验证」或「部分验证」。详细边界见 `docs/PORTING-NOTES.md`、`docs/board-porting-notes.md` 与 `CHANGELOG.md`。
- HarmonyOS 候选 API 名称与能力均未在本机核对（无 SDK/设备）；以 `.claude/skills/harmony-os/` references 为准。

---

## 16. 下游移植检查清单（按顺序）

1. 先移植 `stroke-model` + `ink-document` + `causal-taper` + `stroke-union-geometry`，用向量钉住。
2. 移植 `selection-geometry` + `two-finger-math` + `gesture-pivot`，用矩阵向量钉住。
3. 移植 `shape-catalogue` + `shape-recognition`，用 25 种图形与识别向量钉住。
4. 移植 `ink-input-routing`/`ink-commit-pipeline`，先接白板 `Canvas`（不接覆盖层），验证 tap/拖拽/撤销。
5. 接 `palm-rejection` 与 `touch-contact-classification`，用真机接触数据校准阈值；无接触面积时按 §15.1 降级。
6. 接 `stroke-renderer`/`scene-renderer`，确认非零环绕与半透明单次合成；对照选区框度量。
7. 接 `icink-format` 与设置映射，跑往返测试。
8. 最后评估覆盖层；若不可行，明确标注 `overlay-presenter`/`layered-window-input` 为 not-ported 并写入 manifest 与 API 矩阵。
9. 所有新增文件写双向 `@parity` 锚点；更新 `parity/manifest.json`；运行 `node tools/parity-check.mjs` 与 `parity/run-vectors.mjs`（工具就位后）。
10. 未编译/未实机项写入 `docs/harmony-porting-notes.md`，不得声称通过。
