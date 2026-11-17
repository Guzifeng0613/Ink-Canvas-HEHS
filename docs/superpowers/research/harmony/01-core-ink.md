# 01 — Core Ink 源码盘点（墨迹模型 / 笔锋 / 轮廓几何 / 橡皮 / 撤销 / .icink）

日期：2026-11-18
范围：`src/InkCanvas.Core/Ink/` 全部、`src/InkCanvas.Core/History/UndoStack.cs`，
以及 `tests/InkCanvas.Core.Tests/{TaperTests,StrokeRegionTests,DomainTests}.cs` 与相关消费方。
基准：工作树（含未提交的 3.4.0 改动；`StrokeEraser.cs` 当前还是 untracked 文件）。
性质：只读盘点。**本次没有构建、没有运行测试、没有启动应用**（构建会写 `bin/obj`，超出只读约束）。
文中「已验证」只表示存在 pin 住行为的测试或文档记录；`StrokeEraser` 源码注释明确写的是
“compile-checked only; no behavior test was run”，`MatrixTransformCenter` 注释写「未验证」。

坐标/单位约定：所有 `X/Y/Width/Bounds` 都是 **DIP（WinUI 设备无关像素）**；HarmonyOS 侧用 **vp**
（两者在 160dpi 基准下数值口径一致）。坐标原点左上、Y 向下。ID 用 `Guid`（v4 字符串）。

---

## 1. 分层与纯逻辑边界（结论先行）

`InkCanvas.Core/Ink` 不引用 WinUI / Win2D / Win32。它只依赖：

- `System.Numerics.Vector2` / `Matrix3x2`（float32 运算）
- `System.Runtime.InteropServices.CollectionsMarshal.AsSpan(List<InkPoint>)`（把 `List<InkPoint>` 当可变
  struct 数组原地修改；ArkTS 无对应概念，见 §12）
- `MathF` / `Math.Clamp` / `Guid`

因此 **墨迹数据模型、两种笔锋、轮廓生成、命中/橡皮、撤销栈全部是可 1:1 复刻的纯逻辑**；
真正需要平台适配的是渲染（Win2D Winding 填充）、输入采样（PointerPoint 压感/时间戳）、
文件 IO 与 JSON 绑定。

---

## 2. 数据模型

### 2.1 `InkPoint`（`src/InkCanvas.Core/Ink/InkPoint.cs`）

可变 struct，5 个 public float 字段：

| 字段 | 类型 | 语义 | 关键不变量 |
|---|---|---|---|
| `X`, `Y` | float | 世界坐标（DIP） | 无 |
| `Pressure` | float | 设备原始压感 `[0,1]`；`0.5` 表示“无压感信息” | **只存储、不参与宽度计算**（两种笔锋都不用真实 Pressure） |
| `Width` | float | 最终渲染的**全宽**（不是半径！），DIP | 新点构造时为 0；`AddPoint` 期间只增不减；settle 后冻结 |
| `TimeMs` | float | 相对笔迹起点的毫秒 | **只存储，不参与宽度/几何**；`InkSurface` 传 `PointerPoint.Timestamp/1000` |

`Position => new Vector2(X, Y)`。

结构体语义很重要：
- `List<InkPoint>` 里是值类型；`TaperProfile.Apply(Span<InkPoint>)` 直接改元素。
- `StrokeEraser` 里 `InkPoint a = points[i-1]` 是**复制**，插值时不会别名。
- `InkStroke.Clone()` 复制点是逐值复制。
ArkTS 侧若用 class（引用），上述三处都必须显式深拷贝，否则会出现“改副本影响原笔迹”的错误。

### 2.2 `InkColor`（`InkColor.cs`）

`readonly record struct InkColor(byte A, byte R, byte G, byte B)`，值相等。

- `PackedArgb = (uint)(A << 24 | R << 16 | G << 8 | B)`。注意 C# 中 `A>=128` 时 `A<<24` 为负 int，
  再 cast 为 uint，结果例如黑 `(255,0,0,0)` → `4278190080`。
- `FromPackedArgb(uint v)`：`A=v>>24, R=v>>16, G=v>>8, B=v`，各自取低 8 位。
- `WithAlpha(a)` 只换 A。
- 常量：`Black = (255,0,0,0)`、`White = (255,255,255,255)`、`Red = (255,220,38,38)`、
  `Transparent = (0,0,0,0)`。
- ArkTS 打包时用 `>>> 0` 保持无符号（JS 的 `<<` 结果是 32 位有符号）。

### 2.3 `StrokeAttributes`（`StrokeAttributes.cs`）

| 字段 | 默认 | 说明 |
|---|---|---|
| `Color` | `InkColor.Black` | 基色，不含荧光笔透明度 |
| `BaseWidth` | `3f` | 全宽 DIP；构造 `InkController` 时实际先被设为 `2.5f`（见 §11） |
| `IsHighlighter` | `false` | |
| `HighlighterAlpha` | `96` | WPF `Canvas.InkAlpha` 语义：20–240，步长 20；**仅荧光笔生效**；Settings 默认实际是 140（见 §11） |
| `TaperKind` | `Tail` | 0/1/2，见 §4 |

计算属性：
- `EffectiveTaper => IsHighlighter ? None : TaperKind`（荧光笔永不笔锋）。
- `EffectiveColor => IsHighlighter ? Color.WithAlpha(HighlighterAlpha) : Color`。
- `Clone()` 逐字段复制（新对象，非引用共享）。

### 2.4 `Rect`（定义在 `InkStroke.cs` 末尾）

`readonly record struct Rect(float Left, float Top, float Right, float Bottom)`。

- `Empty = (0,0,0,0)`
- `Width = Right-Left`，`Height = Bottom-Top`
- `IsEmpty => Width <= 0 || Height <= 0`（**退化为线/点的矩形也算空**）
- `Center = ((L+R)/2, (T+B)/2)`
- `Contains(p)`：`p.X ∈ [L,R] && p.Y ∈ [T,B]`（闭区间）
- `Intersects(o)`：`!(o.L > R || o.R < L || o.T > B || o.B < T)`（贴边算相交）
- `Union(o)`：若 `this.IsEmpty` 返回 `o`；若 `o.IsEmpty` 返回 `this`；否则逐边 min/max
- `Inflate(d)`：四边各外扩 d

### 2.5 公开类型与主要消费方（总表）

| 公开类型 | 主要成员 | 主要消费方 |
|---|---|---|
| `InkPoint` | X/Y/Pressure/Width/TimeMs/Position | `InkStroke`、`StrokeEraser`、`ShapeGenerator.Dot`、`InkFileStore`/`BoardService`、`InkReplaySession` |
| `InkColor` | ARGB、PackedArgb、常量、WithAlpha | `StrokeAttributes`、`OriginalMenuCatalog`、`InkFileStore`、`BoardWindow.RecolorSelection` |
| `Rect` | LTRB、Empty、Contains/Intersects/Union/Inflate | `InkDocument`、`InkController`、`SelectionGeometry`、`InkSceneRenderer` |
| `StrokeAttributes` | Color/BaseWidth/IsHighlighter/HighlighterAlpha/TaperKind/Effective* | `InkStroke`、`InkController.CurrentAttributes`、`PenPalette`、`BoardWindow`、序列化 |
| `TaperKind` / `TaperProfile` / `NoneTaper` / `TailTaper` / `VelocityTaper` | Kind/Apply/Create/Settle | `InkStroke`（唯一生产消费方）；UI 只设置 Kind |
| `InkStroke` | 见 §3.1 | `InkController`、`InkDocument`、`BoardService`、`InkReplaySession`、`ShapeRecognizer`、`StrokeRenderer` |
| `InkDocument` | 见 §6 | `InkController.Document`、`InkSurface`/`OverlayInkPresenter`（订阅 `Changed`） |
| `StrokeGeometry` | BuildUnionContours/BuildSegmentOutline/SignedArea/Circle/BuildOutline | `StrokeRenderer`、`TaperTests`、`StrokeRegionTests` |
| `StrokeEraser` | EraseDisc | `InkController.EraseAlong`（面积擦） |
| `MatrixTransformCenter` | Resolve | `InkController.ResolvePivot`、`BoardWindow.ResolveElementPivot` |
| `IUndoableAction` / `DelegateAction` / `UndoStack` | 见 §9 | `InkController.History`（容量 200）、`BoardService`/`BoardWindow` 的页面历史、`FloatingBar` 按钮态 |

---

## 3. `InkStroke`（`InkStroke.cs`）

### 3.1 构造与公开成员

```text
InkStroke(StrokeAttributes attributes)
    Attributes = attributes ?? throw ArgumentNullException
    _taper = TaperProfile.Create(attributes.EffectiveTaper)
```
- `Id { get; init; } = Guid.NewGuid()` —— `Clone()` **复用同一个 Id**。
- `Points` 返回只读视图（底层仍是同一个 `List<InkPoint>`）。
- `Count`、`IsCompleted {get; private set;}`、`IsRecognizedShape {get;set;}`。
- `TaperKind { get => _taper.Kind; set ... }`（见 3.3）。
- `Bounds` 缓存，`_boundsDirty` 在 AddPoint/RestorePoints/Transform/RebuildWidths 时置脏。

### 3.2 `AddPoint`（逐样本增量核心）

```text
AddPoint(x, y, pressure = 0.5f, timeMs = 0f):
  _points.Add(new InkPoint(x, y, pressure, 0f, timeMs))     // Width 从 0 开始
  _taper.Apply(span(_points), _points.Count - 1, Attributes.BaseWidth)
  _boundsDirty = true
```
- 宽度在**点到达时**由笔锋写入，绝不回填未来点。
- `Complete()` 只有 `IsCompleted = true`，**几何零改动**。这是“提交几何 == 最后一帧”的结构保证，
  也是修复 WPF 抬笔重算导致笔锋闪烁的关键；对应测试见 §13。

### 3.3 `TaperKind` setter（两个易错点）

```text
set:
  if (_taper.Kind == value) return
  _taper = TaperProfile.Create(value)      // 注意：直接用 value，没有走 EffectiveTaper
  if (IsCompleted) RebuildWidths()
```
- 对荧光笔：构造时 profile 是 `None`；若在**已完成**的荧光笔笔迹上 `TaperKind = Tail`，
  会真的建立一个 Tail profile 并重建宽度（荧光笔被加上笔锋）。这是现有实现的行为/潜在 bug，
  移植时要明确是保留还是修（见 §15 Q3）。
- `TaperKind` getter 返回 **profile 的 Kind**，而不是 `Attributes.TaperKind`。
  序列化也存 `s.TaperKind`（§10）。运行中直接改 `Attributes.TaperKind` 不会改变 profile。
- 对未完成笔迹 setter 只换 profile，不重建旧点；后续点用新 profile。

`RebuildWidths`：

```text
所有点 Width = 0
for i = 0..n-1: _taper.Apply(span, i, Attributes.BaseWidth)
_boundsDirty = true
```
由于两个 profile 都是“按到达顺序逐点调用”的纯函数，这个顺序重放与实时增量结果完全一致
（`Settle` 的单调性保证）。

### 3.4 `Bounds` 公式

```text
空笔迹 -> Rect.Empty
否则:
  minX/minY/maxX/maxY = 点坐标极值
  maxHalf = max(所有点的 Width) * 0.5
  Rect.FromLTRB(minX-maxHalf, minY-maxHalf, maxX+maxHalf, maxY+maxHalf)
```
注意是**全局最大宽度**统一外扩，不做逐点局部包围；简单但偏保守。

### 3.5 几何辅助

- `ArcLength()`：相邻点欧氏距离之和（float 累加），不除时间。
- `Transform(Matrix3x2 m, bool scaleWidth = true)`：

```text
det = m.M11*m.M22 - m.M12*m.M21
widthScale = scaleWidth ? sqrt(abs(det)) : 1
if (widthScale <= 0.0001f) widthScale = 1
for each point:
    (X,Y) = Vector2.Transform((X,Y), m)      // row-vector: p' = p*M
    Width *= widthScale
Attributes.BaseWidth *= widthScale
_boundsDirty = true
```
非均匀缩放下宽度按面积比例的平方根（几何平均）缩放；旋转/镜像 `|det|=1`。
`Vector2.Transform(v, m)` 展开为 `(x*M11 + y*M21 + M31, x*M12 + y*M22 + M32)`。
注意：原地修改已完成笔迹后，渲染器按 `Id` 缓存几何，必须让宿主调用 `InkDocument.Touch()`
（`Changed` 会触发 `InvalidateAll`），否则屏幕不更新。

- `DistanceTo(Vector2 p)`：0 点 → `float.MaxValue`；1 点 → 点距；否则对所有相邻线段取最小值。
- `DistanceToSegment(p,a,b)`：`len2 = |b-a|²`；`len2 < 1e-6` 返回 `|p-a|`；
  `t = clamp(dot(p-a, b-a)/len2, 0, 1)`；返回 `|p - (a + t*(b-a))|`。
- `Clone()`：`new InkStroke(Attributes.Clone()) { Id = Id, IsRecognizedShape = ... }`，
  `AddRange` 复制点（值复制，保留已存 Width），复制 `IsCompleted`，bounds 置脏。
- `RestorePoints(IEnumerable<InkPoint>)`：清空并整体替换点；**不重算笔锋、不自动 Complete**。
  持久化、回放、图形点、橡皮碎片都依赖它保留宽度。

### 3.6 公开类型消费方

| 类型 | 消费方 |
|---|---|
| `InkStroke` | `InkController`、`InkDocument`、`BoardService`、`InkFileStore`、`InkReplaySession`、`ShapeRecognizer`、`ShapeGenerator`、`InkSceneRenderer`/`StrokeRenderer`、Selection 手势 |
| `InkPoint` | `StrokeEraser`、序列化、`ShapeGenerator.Dot`、回放 |
| `Rect` | `InkDocument`、`SelectionGeometry`、`InkController.SelectionBounds`、Board 元素 |

---

## 4. 笔锋算法（`TaperProfile.cs`）—— 移植 correctness 核心

### 4.1 三种 Kind 与契约

```text
enum TaperKind { None = 0, Tail = 1, Velocity = 2 }
TaperProfile.Create(kind):
  None -> NoneTaper
  Velocity -> VelocityTaper
  其它（含非法 int 走默认分支）-> TailTaper
```

抽象契约（注释原文语义）：
1. 只允许读写 `index <= tipIndex`，**绝不读未来点**。
2. 一个点的宽度在后续调用中**只能增大**（`Settle`），离开 profile 窗口后冻结。
3. `Complete()` 不改变任何几何，因此提交后的轮廓与最后一帧逐 float 相等。

```text
Settle(ref slot, candidate): if (candidate > slot) slot = candidate
```

### 4.2 `NoneTaper`

```text
Apply(points, tipIndex, baseWidth):
  Settle(points[tipIndex].Width, baseWidth)
```
单次调用即把该点设为全宽；等宽。

### 4.3 `TailTaper`（UI 名「点集笔锋」，`inkStyle=0`）

常量（init 属性默认值，**工程中没有任何设置会修改它们**）：

| 属性 | 默认 | 含义 |
|---|---|---|
| `TailPoints` | 10 | 尾部窗口采样数 |
| `HeadPoints` | 4 | 起笔增强采样数 |
| `TipRatio` | 0.2 | 笔尖宽度比例（原 WPF：压力 0.1 / 基线 0.5） |

精确算法：

```text
Apply(points, tip, baseWidth):
  n = max(2, TailPoints)                      // = 10
  start = max(0, tip - n + 1)
  for j = start .. tip:
      distance = tip - j                      // 尖端为 0
      t = distance / (n - 1)                  // 0..1（分母 9）
      ratio = TipRatio + (1 - TipRatio) * t   // = 0.2 + 0.8*t
      Settle(points[j].Width, baseWidth * ratio)

  if (tip < HeadPoints):                      // tip = 0..3
      t = (tip + 1) / (HeadPoints + 1)        // 分母 5
      ratio = TipRatio + (1 - TipRatio) * t
      Settle(points[tip].Width, baseWidth * ratio)
```

行为要点（必须在 ArkTS 里原样复刻）：
- 每一帧更新的是**滑窗内 10 个点**：距离 tip 0..9 的比例依次
  `0.2, 0.2889, 0.3778, 0.4667, 0.5556, 0.6444, 0.7333, 0.8222, 0.9111, 1.0`（分母 9）。
- 只有最后 9 个点最终保持锥形；更早的点在 tip 走过 9 个样本后达到全宽并冻结。
- 起笔增强只在 `tip<4` 时把当前点抬到 `0.36b, 0.52b, 0.68b, 0.84b`（b=baseWidth）。
  它只影响早期帧，最终会被滑窗/全宽覆盖。
- **短笔画永远不会到全宽**：若总点数 `m < 10`，任意点与最终 tip 的最大距离只有 `m-1 < 9`，
  故所有点 `ratio < 1`（起笔点最多到 `(m-1)/9` 的比例）。例如 2 点笔迹最终
  `p0=0.36b, p1=0.52b`。这是现有行为，无测试钉住（见 Q8）。
- 单点轻点（无移动）最终宽度 = `0.36 * baseWidth`（窗口先给 0.2b，起笔增强抬到 0.36b），
  半径 = `0.18b`；`TaperTests.SinglePointProducesAClosedDot` 只做了宽松范围检查。
- 最终曲线示例：base=10、40 个样本时，索引 0..30 全宽 10；索引 31..39 依次
  `9.111, 8.222, 7.333, 6.444, 5.556, 4.667, 3.778, 2.889, 2.0`。

### 4.4 `VelocityTaper`（UI 名「速率笔锋」，`inkStyle=1`）

不做压感映射，也**完全不使用 `InkPoint.Pressure` / `TimeMs`**，只用最近两段的距离：

```text
Apply(points, tip, baseWidth):
  pressure = 0.5
  if tip >= 2:
      d1 = dist(points[tip-2], points[tip-1])
      d2 = dist(points[tip-1], points[tip])
      speed = (d1 + d2) / 20.0
      if speed >= 0.25:
          pressure = 0.5 - 0.3 * (min(speed, 1.5) - 0.3) / 1.2
      else if speed < 0.05:
          pressure = 0.5 + 0.4 * (0.05 - speed) / 0.05
  ratio = clamp(pressure / 0.5, 0.15, 1.8)
  Settle(points[tip].Width, baseWidth * ratio)
```

精确分段（ratio 相对 baseWidth）：

| speed 区间 | 公式 | 端点值 |
|---|---|---|
| `speed = 0` | `0.5 + 0.4*(0.05-s)/0.05` → ratio | `1.8b` |
| `0 < s < 0.05` | 同上，线性 | `s=0.025 → 1.4b` |
| `0.05 <= s < 0.25` | `pressure=0.5` | 恒 `1.0b` |
| `0.25 <= s < 1.5` | `0.5 - 0.3*(s-0.3)/1.2` | `s=0.25 → 1.025b`；`s=0.5 → 0.9b`；`s=1 → 0.65b`；`s=1.5 → 0.4b` |
| `s >= 1.5` | 取 `min(s,1.5)` | 恒 `0.4b` |

- 在 `s=0.25` 处有约 +2.5% 的不连续跳变（`1.0b → 1.025b`），源码如此，勿“顺手修平”。
- `ratio` 的 clamp `[0.15,1.8]` 在上述可达区间内不会触发（实际范围 `[0.4,1.8]`）。
- 索引 0、1 没有足够历史，固定 `ratio=1`（全宽）。
- 每个索引**只被赋值一次**（`Settle` 实际是恒等），后续帧绝不改旧点；
  `TaperTests.VelocityTaperDoesNotRewriteEarlierPoints` 用精确 float 相等钉住。
- `speed` 是“每个采样间隔的 DIP 距离 / 20”，**不是按时间归一化的速度**；采样率/抖动会直接影响宽度。
  若要跨设备一致，需要改成按 `TimeMs` 归一，但那会破坏现有形状（见 Q12）。

### 4.5 宽度语义总表

- `InkPoint.Width` 是**全宽**；`StrokeGeometry` 里统一 `* 0.5` 变半径。
- 新点从 0 开始，`Settle` 只增；`Complete` 不重算；持久化保留逐点宽度，回放用保留值。
- 任何“改笔宽”的操作必须同时重算/缩放每个点的 `Width`，否则渲染（用逐点 Width）不会变。
- 现有 UI 的选区“增粗/变细/恢复笔宽”只改 `Attributes.BaseWidth`（§15 R5），不会改变已存 Width，
  因而**没有视觉反馈**；这是移植时需要修正或显式复刻的点。

### 4.6 与设置/UI 的映射

| 入口 | 值 |
|---|---|
| `OriginalMenuCatalog.PenStyles` | `["点集笔锋","速率笔锋","关闭笔锋"]`，索引 0/1/2 |
| `PenPalette` 笔锋下拉 → `CurrentAttributes.TaperKind` | 0→Tail，1→Velocity，2→None，默认 Tail |
| `Settings.Canvas.InkStyle` → `AppServices.ApplySettings` | 1→Velocity，2→None，**其它（含 0）→Tail** |
| `CanvasSettings.InkStyle` 默认 | `0`（即 Tail） |

---

## 5. 轮廓几何（`StrokeGeometry.cs`）—— 自交并集

### 5.1 `BuildUnionContours(InkStroke) -> Vector2[][]`

```text
points.Count == 0 -> []
points.Count == 1 -> [ NormalizeWinding(Circle(pos, max(Width, 0.5)*0.5, 24)) ]
否则 -> 长度 = Count-1 的数组，每段:
    BuildSegmentOutline(
        points[i-1].Position, max(points[i-1].Width, 0.5)*0.5,
        points[i].Position,   max(points[i].Width,   0.5)*0.5)
```
- 最小“全宽”被 clamp 为 **0.5 DIP**（半径最小 0.25），避免零宽段。
- 每段独立成闭合胶囊；相邻段在圆帽处重叠。
- 渲染时**整条笔迹的所有段一次性成组**用非零 Winding 填充：
  自交处是并集，不会像 even-odd 那样挖洞；半透明荧光笔在同一笔自交处只合成一次。

### 5.2 `BuildSegmentOutline(start, startRadius, end, endRadius)`

精确步骤（点序会影响 `BuildOutline` 相等性测试，必须一致）：

```text
delta = end - start
length = |delta|
if length < 1e-4:  return NormalizeWinding(Circle(start, max(startRadius, endRadius), 24))

tangent = delta / length
normal = (-tangent.Y, tangent.X)
a0 = atan2(normal.Y, normal.X)

outline = []
outline += start + normal * startRadius
outline += end   + normal * endRadius
AppendArc(outline, end,   endRadius,   a0,        -PI, 12)
outline += end   - normal * endRadius
outline += start - normal * startRadius
AppendArc(outline, start, startRadius, a0 + PI,   -PI, 12)
return NormalizeWinding(outline)

AppendArc(list, center, radius, startAngle, delta, steps):
    if radius <= 0: return
    for i = 1 .. steps-1:                    // 注意：不含 i=0 与 i=steps
        add center + (cos(startAngle + delta*i/steps), sin(...)) * radius
```

- `AppendArc(..., 12)` 实际向轮廓加 **11 个点**；两端端点由显式的 `±normal*radius` 提供。
- 正常段总点数 = `4 + 11 + 11 = 26`；半径 <=0 的退化段只有 4 点。
- 零长度段（<1e-4）返回以 **start** 为圆心、半径 `max(startRadius,endRadius)` 的 24 边形。
- 端点处半径不同 → 漏斗状变宽段。

### 5.3 绕向归一

```text
SignedArea(points):
  若 null 或 <3 点 -> 0
  area(double) = 0.5 * Σ (x_i*y_{i+1} - x_{i+1}*y_i)
  返回 (float)area

NormalizeWinding(points):
  if SignedArea(points) >= 0: keep
  else: Array.Reverse(points)
```
- `SignedArea` 用 **double** 累加再转 float。
- 面积恰为 0 时保持原序（`>=0`）。
- 所有 contour 最终都同向（正面积），Winding 叠加即并集。

### 5.4 兼容/诊断函数

- `Circle(center, radius, segments)`：`angle = 2π*i/segments`，`i=0..segments-1`。
- `BuildOutline(InkStroke)`：把 `BuildUnionContours` 的所有 contour 依次拼接成一个扁平数组。
  只用于诊断/测试/回放比较；**生产渲染不能用它**（连接后会重新引入自交）。
  `TaperTests.LastLiveFrameEqualsCommittedGeometry` / `OutlineIsDeterministic` 用它做逐点相等比较。

### 5.5 渲染消费方（平台相关，详见 §12）

`StrokeRenderer.BuildGeometry`：
- 每个 contour 建一个 `CanvasPathBuilder`：`BeginFigure(outline[0])` → `AddLine` 其余 →
  `EndFigure(CanvasFigureLoop.Closed)` → `CanvasGeometry.CreatePath`。
- 只有一个 contour 直接返回；多个则
  `CanvasGeometry.CreateGroup(session, regions, CanvasFilledRegionDetermination.Winding)`。
- 完成笔迹的几何按 `stroke.Id` 缓存；`Document.Changed` 时整体失效。
- 填充色用 `Attributes.EffectiveColor`；`FillGeometry` 一次。

---

## 6. `InkDocument`（`InkDocument.cs`）

一个表面（画板页或屏幕覆盖层）的笔迹集合；`List<InkStroke>` 顺序即 z-order（后加在上）。

| 成员 | 语义 |
|---|---|
| `Strokes` | 只读视图 |
| `Count` | |
| `event Action Changed` | 任何“需要重绘/失效缓存”的改动后触发一次 |
| `Add(s)` | null 直接忽略；否则追加并 `Changed` |
| `AddRange(xs)` | 跳过 null；**至少加入一个才触发一次** `Changed` |
| `Remove(s)` | 按引用删除（`InkStroke` 无 Equals 重写）；删掉才 `Changed` |
| `RemoveAll(xs)` | 用 `HashSet<InkStroke>`（引用相等）批量删；删掉 >0 才 `Changed` |
| `Insert(i,s)` | clamp 到 `[0,Count]`；**不做 null 检查**；总是 `Changed` |
| `Clear()` | 空集合不触发；否则清空 + `Changed` |
| `Touch()` | 手工通知“几何原地变了” |
| `Bounds` | 所有 `stroke.Bounds` 的 `Union`；无缓存 |
| `HitTest(point, radius)` | 见下 |
| `HitTest(rect)` | 先 `Bounds.Intersects(rect)`，再任一点 `rect.Contains`；命中返回整条 |
| `EraseAt(point, radius)` | `HitTest(point,radius)` → 有命中就 `RemoveAll` → 返回命中列表 |
| `static Transform(strokes, m)` | 对每条 `stroke.Transform(m)`（不改文档、不触发事件） |

点命中公式：

```text
if (!s.Bounds.Inflate(radius).Contains(point)) continue
if (s.DistanceTo(point) <= radius + s.Attributes.BaseWidth * 0.5f) hit
```
注意第二项用的是 **BaseWidth**，不是逐点 `Width`；对速率笔锋（实际点宽可能只有 0.4b）会偏宽，
对选区点选（`InkController` 用 `HitTest(world, 8f)`）和整笔橡皮都一样。Bounds 已按最大逐点宽度外扩，
所以预筛与最终判定大致一致。

`HitTest(Rect)` 当前在 App 里没有被调用（public 但未使用），移植时可保留为纯逻辑。

---

## 7. `StrokeEraser`（`StrokeEraser.cs`，untracked / 未行为验证）

WPF `EraseByPoint`（线条擦）的 1:1 思路：只切掉中心线上被橡皮圆盘扫到的部分，返回剩余碎片；
与 `InkDocument.EraseAt`（整笔擦）是两条独立路径。源码注释明确：**只做过编译检查，没有行为测试**。

### 7.1 精确算法

```text
EraseDisc(stroke, center, radius, out remaining):
  remaining = []
  if stroke == null || Count == 0 || radius <= 0: return false

  if Count == 1:
      if |point.Position - center| <= radius + point.Width*0.5:
          return true                    // 整条（一个点）删除，remaining 空
      remaining = [stroke]; return false

  maxHalfWidth = max(所有点 Width) * 0.5
  cutRadius = radius + maxHalfWidth
  cutRadiusSquared = cutRadius^2
  touched = false
  fragments = []; current = []

  for segment i = 1..Count-1:
      a = points[i-1]; b = points[i]
      p0 = a.Position; d = b.Position - p0; f = p0 - center
      aa = dot(d,d)
      cuts = [0, 1]
      if aa > 1e-8:
          bb = 2*dot(f,d)
          cc = dot(f,f) - cutRadiusSquared
          disc = bb*bb - 4*aa*cc
          if disc >= 0:
              root = sqrt(disc)
              t1 = (-bb-root)/(2*aa); t2 = (-bb+root)/(2*aa)
              if 0 < t1 < 1: cuts += t1
              if 0 < t2 < 1: cuts += t2
      sort(cuts)

      for k = 1..cuts.Count-1:
          t0 = cuts[k-1]; t1 = cuts[k]
          tm = (t0+t1)/2
          mid = lerp(p0, b.Position, tm)
          if |mid - center|^2 <= cutRadiusSquared:
              touched = true
              Flush(current -> fragments)        // 断开
              continue
          AppendDistinct(current, Interpolate(a,b,t0))
          AppendDistinct(current, Interpolate(a,b,t1))

  Flush(current)
  if !touched:
      remaining = [stroke]; return false

  for each fragment:
      if fragment empty: skip
      copy = new InkStroke(stroke.Attributes.Clone()) { IsRecognizedShape = stroke.IsRecognizedShape }
      copy.RestorePoints(fragment)
      copy.Complete()
      remaining += copy
  return true
```

辅助函数：

```text
Interpolate(a,b,t): 逐字段线性插值 X,Y,Pressure,Width,TimeMs
AppendDistinct(list, p): 若 list 非空且 |last - p|^2 <= 1e-4 则丢弃，否则追加
Flush(current, fragments): current 非空则加入 fragments 并清空
```
（`|Δ|² <= 1e-4` 即距离 <= 0.01 DIP 视为重复点。）

### 7.2 必须知道的语义

- 圆盘判定用**整条笔迹的最大宽度**（`cutRadius = radius + maxWidth/2`），局部细段也用宽切半径 → 偏保守。
- 区间分类用**中点**：由于区间端点都在圆上（0/1 或二次方程根），中点在内/外可代表整个区间。
- 只处理中心线段；不处理“笔迹自交区域”的特殊情况（按几何自然切割）。
- 碎片是**新 `InkStroke`**（新 Id），保留原属性与 `IsRecognizedShape`，不重算笔锋；
  插值后的宽度保留，`Complete()` 直接冻结。
- 相邻碎片互不连接：一次擦除后，切口左右是两条独立笔迹。
- 单点笔迹整体删除的条件是 `radius + Width/2`（局部宽度，不是全局最大）。
- 半径 <=0、空笔迹返回 false 且 `remaining` 为空（调用方须自行保留原笔迹）。

### 7.3 与 `InkController` 的接线

- `InkEraserMode.Area` → `BeginErase(world, partialStroke: true)` → 每个插值采样点对当前文档每条笔迹跑
  `StrokeEraser.EraseDisc`；命中则移除原件并把碎片加回。
- `InkEraserMode.WholeStroke` → `partialStroke: false` → `Document.EraseAt(p, radius)`。
- 橡皮半径在扫掠上按 `steps = max(1, int(distance / max(4, radius*0.5)))`、采样 `i=0..steps` 插值，
  避免快速划动漏擦。
- 面积擦的撤销：手势开始时克隆整个文档（`_eraseBeforeSnapshot`），结束时克隆“之后”状态；
  只有数量或点坐标不同才 push 一个 `DelegateAction`，Revert/Apply 都调用 `RestoreDocument`
  （`Clear` + 逐条 `Clone` 加回 + 清空选区）。副作用见 §15 R7。

---

## 8. `MatrixTransformCenter`（`MatrixTransformCenter.cs`）

双指缩放/旋转的枢轴选择，1:1 对应 WPF `MW_MatrixTransform.cs → GetMatrixTransformCenterPoint`。

```text
Resolve(option, canvasCenter, gestureCenter, selectedElementsCenter, isLoaded):
  if (!isLoaded) return canvasCenter
  switch option:
      GestureOperationCenterPoint   -> gestureCenter
      SelectedElementsCenterPoint   -> selectedElementsCenter
      default (CanvasCenterPoint)   -> canvasCenter
```

`MatrixTransformCenterPointOptions`（`AppSettings.cs`）：
`0 CanvasCenterPoint`、`1 SelectedElementsCenterPoint`、`2 GestureOperationCenterPoint`；
Settings 键 `gesture.matrixTransformCenterPoint`，默认 0。

消费方：
- `InkController.ResolvePivot` 固定 `isLoaded: true`（当前运行时 `!isLoaded` 分支不可达）；
  两指手势（视图变换或选区变换）调用它。
- `BoardWindow`/`SettingsWindow` 直接读写 `Gesture.MatrixTransformCenterPoint`。
- 源码注释「未验证」：无单测、无实机手势验证。

---

## 9. `UndoStack`（`History/UndoStack.cs`）

### 9.1 类型

```text
interface IUndoableAction { string Name; void Apply(); void Revert(); }

sealed class DelegateAction(name, apply, revert):
  两个委托都不能 null（ArgumentNullException）；Apply/Revert 直接调用
```

### 9.2 栈语义

```text
UndoStack(capacity = 100): Capacity = max(1, capacity)
CanUndo = _undo.Count > 0; CanRedo = _redo.Count > 0
UndoCount / RedoCount; event Action Changed

Push(a):
  a 不能 null
  _undo.Add(a)
  if (_undo.Count > Capacity) _undo.RemoveAt(0)     // 丢最旧
  _redo.Clear()                                     // 新操作截断 redo 分支
  Changed()

Undo():
  if !CanUndo return false
  a = _undo[^1]; _undo.RemoveAt(last)
  a.Revert()
  _redo.Add(a)
  Changed(); return true

Redo():
  if !CanRedo return false
  a = _redo[^1]; _redo.RemoveAt(last)
  a.Apply()
  _undo.Add(a)
  Changed(); return true

Clear():
  if both empty return
  clear both; Changed()
```

关键约定：
- `Push` 记录的是**已经执行过**的动作（墨迹输入时笔画已在屏幕上）。
- 没有 `Peek`、没有名称查询、没有合并/事务；容量只在 Push 时修剪。
- 若 `Revert/Apply` 抛异常，动作已经从来源栈弹出（不是原子回滚）；移植若要保持一致需照做或显式修。
- `InkController` 使用 `new UndoStack(200)`；其他页面/对象各自持有栈。
- 典型撤销单元（来自消费方）：add ink、erase、area erase（整文档快照）、recognize、clear、
  transform selection、gesture selection、delete、clone selection、board 图片/媒体操作。
- `BoardService`/`BoardWindow` 用 `Controller.History.Push(...)`；浮动栏只用 `CanUndo/CanRedo` 控制按钮透明度
  （1 / 0.35）。

---

## 10. `.icink` 与板包序列化（跨端数据契约）

### 10.1 扁平墨迹文件 `InkFileStore`（`Features/Persistence/Stores.cs`）

- 扩展名常量 `.icink`；`FormatVersion = 1`（private）。
- 保存：`JsonSerializer.Serialize(model)`——**无缩进、默认 PascalCase 属性名**。
- 精确字段（类型即 C# 序列化形状）：

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
      "Points": [
        [100.0, 120.0, 0.5, 2.5, 0.0],
        [103.0, 121.8, 0.5, 2.5, 8.0]
      ]
    }
  ]
}
```

| 字段 | 类型 | 写入来源 | 读取语义 |
|---|---|---|---|
| `Version` | int | 固定 1 | **完全不校验/不使用** |
| `Strokes` | array | 文档顺序 | null → 空 |
| `Color` | uint | `Attributes.Color.PackedArgb`（不含荧光笔 alpha） | `InkColor.FromPackedArgb` |
| `Width` | float | `Attributes.BaseWidth` | `BaseWidth` |
| `Highlighter` | bool | `IsHighlighter` | |
| `HighlighterAlpha` | byte | `Attributes.HighlighterAlpha` | 缺字段时用 `StrokeModel` 初始值 **96** |
| `Taper` | int | `stroke.TaperKind`（profile 的 Kind，0/1/2） | `(TaperKind)int`，**不做合法性校验**；非法值经 `TaperProfile.Create` 的 default 分支落 Tail（荧光笔因 `EffectiveTaper` 强制 None 不受影响） |
| `Shape` | bool | `IsRecognizedShape` | |
| `Points` | `float[][]` | 每行 `[X,Y,Pressure,Width,TimeMs]` | 仅接受 `row.Length >= 4`；第 5 项缺省 0 |

读取流程：
1. 文件不存在 → 空列表。
2. `JsonSerializer.Deserialize<FileModel>`（默认**大小写敏感**，即必须匹配上面的 PascalCase）。
3. 每条：构造 `StrokeAttributes`（含 `HighlighterAlpha`），`new InkStroke(attrs)`，
   `RestorePoints(rows)`，**逐点宽度原样恢复、绝不重跑笔锋**，然后 `Complete()`。
4. 不恢复 `Id`、`IsCompleted`（一律完成）、`Taper profile` 的运行态。
5. `Clone` 语义：每行 float 数组和每条 stroke 都是新建对象。

### 10.2 画板包（`BoardService.cs`）

`SaveBoard` / 有图片时的 `SavePage` 使用板包（同样 `.icink` 扩展）：

```json
{
  "Format": "inkcanvas-board",
  "Version": 1,
  "Background": "Black",
  "GridSpacing": 40.0,
  "Pages": [
    {
      "Strokes": [
        { "Color": 4278190080, "Width": 2.5, "Highlighter": false,
          "Taper": 1, "Shape": false, "Points": [[100.0,120.0,0.5,2.5,0.0]] }
      ],
      "Images": [ ... ],
      "Media": [ ... ]
    }
  ]
}
```

- 常量：`BoardFormat = "inkcanvas-board"`、`BoardFormatVersion = 1`、最大页数 99。
- 保存选项：`WriteIndented = true`、`PropertyNameCaseInsensitive = true`；写临时文件 `.tmp` 后
  `File.Move(overwrite: true)`。
- 读取：先解析 JSON，根上必须有 `Format` 且**精确等于** `"inkcanvas-board"`，否则返回 false
  （当作扁平 `.icink` 回退）；`Version != 1` 抛异常。
- `Background` 写 `BoardBackground.ToString()`（`Black/White/Grid/Lined`），读时 `ignoreCase` 解析；
  `GridSpacing` 读取时 clamp 到 `[12, 160]`，<=0 保留当前值。
- `StrokeFile` 字段：`Color/Width/Highlighter/Taper/Shape/Points`——**没有 `HighlighterAlpha`**。
  因此板包往返后荧光笔透明度一律变成 `StrokeAttributes` 默认 **96**（见 §15 R4）。
- `FromStrokeFile` 对 `Taper` 用 `Enum.IsDefined` 校验，非法 → `None`（与扁平 `.icink` 的“非法→Tail”不同）。
- `SavePage` 的分流：只有“当前页图片数 == 0”时才写扁平 `InkFileStore`；否则写板包。
  **媒体（视频）不计入该条件**，只有媒体没有图片时 `SavePage` 会丢媒体。
- `LoadPage`/`LoadBoard` 都支持“板包或旧扁平 `.icink` 单页”。
- `ToStrokeFile` 的 `Taper` 同样取 `Attributes.TaperKind`（注意不是 `stroke.TaperKind`；两者可能分叉）。

### 10.3 PPT 墨迹

`PptInkFileStore` 每个 slide 用 `InkFileStore.Save/Load` 写 `slide-<SlideId>.icink`，
并维护 `InkCanvas-Ppt.json` manifest（`Version/LastSlideIndex/LastShowPosition/Slides[{SlideId,SlideIndex,File}]`）。
字段形状与 §10.1 完全一致。

### 10.4 Settings.json 中与墨迹相关的键（`AppSettings.cs`）

`canvas` 段：

| JSON 键 | 属性 | 默认（属性初始值） | 备注 |
|---|---|---|---|
| `inkWidth` | `double InkWidth` | `2.5` | 存储宽度 DIP（菜单刻度的一半） |
| `inkAlpha` | `int InkAlpha` | `140` | 荧光笔透明度；应用时 clamp `[20,240]` |
| `inkStyle` | `int InkStyle` | `0` | 0 Tail / 1 Velocity / 2 None |
| `eraserSize` | `int EraserSize` | `2` | 0..4 索引 |
| `eraserType` | `int EraserType` | `0` | 0 图标切换 / 1 面积擦 / 2 线条擦 |
| `hideStrokeWhenSelecting` | bool | `true` | 非核心墨迹 |

`gesture` 段：`matrixTransformCenterPoint`（0/1/2，默认 0）。

「重置推荐设置」`ResetToRecommendation()` 会把 `Canvas.InkWidth=2.5`、`InkAlpha=80`、
`InkStyle=0`、`EraserSize=1`、`EraserType=0`、枢轴=0（这些是上游默认集，不是属性初始值）。

---

## 11. UI 度量 / 墨迹相关常量

| 项 | 数值 | 位置 |
|---|---|---|
| 笔宽菜单范围 | 1..30，步长 1（菜单显示值） | `OriginalMenuCatalog.PenWidth{Minimum=1,Maximum=30,Step=1}` |
| 存储笔宽 | 菜单值 / 2 → 0.5..15 DIP | `ToStoredPenWidth` / `ToMenuPenWidth = *2` |
| 运行时笔宽 clamp | `[0.5, 60]` DIP | `AppServices.ApplySettings`、选区粗细 clamp |
| `StrokeAttributes.BaseWidth` 默认 | 3 DIP（构造 `InkController` 时置 2.5） | |
| 荧光笔透明度 | 20..240，步长 20 | catalog |
| 透明度默认 | 属性 96；Settings 属性初始 140；重置推荐 80；Palette 字段 140 | |
| 橡皮尺寸索引 | 5 档 `{0.5, 0.8, 1.0, 1.25, 1.8}` × 45 DIP 半径 | `BoardWindow.EraserFactors`、`AppServices` |
| 面积擦半径 | `22.5 / 36 / 45 / 56.25 / 81` DIP | 同上 |
| 整笔擦半径 | 2.5 DIP | `BoardWindow.ActivateStrokeEraser` |
| 手掌线条擦半径 | 2.5 DIP | `InkController.OnPointerDown` |
| 特殊屏面积擦半径 | `EraserDiameter / 2`，`EraserDiameter = contactWidth * 0.8`（默认）× `TouchMultiplier`（属性默认 0.25，推荐设置 0.3，仅 `IsSpecialScreen` 时乘） | `TouchContactClassifier` |
| 选区点选命中半径 | 8 DIP | `InkController.BeginSelectionInteraction` |
| 默认输入无压感值 | 0.5 | `InkSurface.Translate`、`AddPoint` |
| 时间戳 | `PointerPoint.Timestamp(µs) / 1000` → ms | `InkSurface.Translate` |
| 笔画最小裁剪宽度 | 0.5 DIP 全宽（半径 0.25） | `StrokeGeometry` |
| 几何零长度阈值 | `1e-4` DIP | `StrokeGeometry`、线段距离 |
| 橡皮重复点阈值 | 距离平方 `1e-4`（0.01 DIP） | `StrokeEraser` |
| 笔菜单设计宽 | 248 DIP；荧光笔菜单 208 DIP | `PenPalette.DesignWidthDip` |
| 选区描边色 | `RGB(0,120,212)`，线宽 1.5 DIP；白填充手柄半径 6，旋转手柄 7 | `InkSceneRenderer` |

调色板颜色（`OriginalMenuCatalog`，`DarkTheme / LightTheme`，均为不透明）：

| 名称 | 暗色主题 | 亮色主题 |
|---|---|---|
| 黑色 | (0,0,0) | (0,0,0) |
| 白色 | (255,255,255) | (255,255,255) |
| 红色 | (220,38,38) | (239,68,68) |
| 粉色 | (147,51,234) | (236,72,153) |
| 橙色 | (234,88,12) | (249,115,22) |
| 黄色 | (234,179,8) | (250,204,21) |
| 绿色 | (22,163,74) | (34,197,94) |
| 青色 | (13,148,136) | (20,184,166) |
| 蓝色 | (37,99,235) | (59,130,246) |

荧光笔 6 色（两主题同值）：红 (220,38,38)、橙 (234,88,12)、黄 (234,179,8)、
青 (13,148,136)、蓝 (37,99,235)、紫 (147,51,234)。

图标码点：Core/Ink 范围内没有图标常量；墨水相关按钮的字形在 `Glyphs`/`BoardWindow`
（`GlyphEraser` 等），属于 UI 报告范围。

---

## 12. 平台依赖清单（WinUI → HarmonyOS 映射提示）

### 12.1 Core/Ink 内部

| 依赖 | 用途 | ArkTS 处理 |
|---|---|---|
| `System.Numerics.Vector2` / `Matrix3x2` | 点/矩阵；`Vector2.Distance/Dot/Transform` | 自写 `Vec2`/`Mat3x2`，保持 float32（`Math.fround`）与 row-vector 乘法 |
| `CollectionsMarshal.AsSpan(List<InkPoint>)` | 原地写 list 元素 Width | ArkTS 用 `InkPoint[]` 或可变 class 数组，按下标改 |
| `MathF` / `Math.Clamp` | 单精度数学 | `Math.fround` 包裹；`clamp` 自写 |
| `Guid` | 笔迹 Id | `util.generateRandomUUID()` 或自增 id（缓存/克隆语义要保持唯一） |

### 12.2 渲染（`src/InkCanvas.Ink/Rendering/`）

- `Microsoft.Graphics.Canvas.CanvasDrawingSession.FillGeometry`
- `Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder`（`BeginFigure`/`AddLine`/`EndFigure(CanvasFigureLoop.Closed)`）
- `CanvasGeometry.CreatePath`、`CanvasGeometry.CreateGroup(..., CanvasFilledRegionDetermination.Winding)`
- `Windows.UI.Color.FromArgb(A,R,G,B)`
- 覆盖层：`CanvasDevice.GetSharedDevice()`、`CanvasRenderTarget`（`B8G8R8A8UIntNormalized`、
  `CanvasAlphaMode.Premultiplied`）、Win32 `UpdateLayeredWindow`/DIB（强 Windows）。
- 板面：WinUI `CanvasControl`（`InkSurface`）。

HarmonyOS 对应：ArkUI `Canvas` + `CanvasRenderingContext2D`/`Path2D`。
`ctx.fill(path, "nonzero")` 对应 Winding；**需实测 ArkUI 是否支持非零填充规则与 `Path2D`**
（见 Q10）。若只支持 even-odd，则必须自己做多边形并集（复杂）或改用逐段 `fill` 且接受半透明重叠加深。

### 12.3 输入

- WinUI：`PointerRoutedEventArgs`、`PointerPoint.Properties.Pressure`（<=0 取 0.5）、
  `ContactRect.Width/Height`、`PointerPoint.Timestamp`（µs）、`PointerDeviceType`。
- Win32 覆盖层：`WM_POINTER*`/`GetPointerInfo` 经 `OverlayPointer` 传递同样字段。
- HarmonyOS：`TouchEvent`/`onTouch`、`HistoricalPoint` 取 coalesced 样本、笔压来源（Pen Kit 或
  `TouchObject` 压力字段）与时间戳单位需核实（Q9）。`Pressure`/`TimeMs` 目前不参与宽度，只影响
  持久化与回放；但仍应尽量保真。

### 12.4 持久化

- `System.Text.Json`（默认 PascalCase、紧凑、float 最短往返格式；板包 case-insensitive + 缩进）。
- `File`/`Directory`/`Path`；HarmonyOS 用 `@ohos.file.fs` + `JSON.parse/stringify`。
- 跨端约定：字段名、类型、数值口径完全一致；ArkTS 写出时应主动输出 PascalCase 键
  （或读时大小写不敏感、写时固定 PascalCase）。

---

## 13. 钉住行为的测试

本次未运行；以下为测试源码实际断言（xUnit）。

### `TaperTests.cs`

| 测试 | 钉住内容 |
|---|---|
| `LastLiveFrameEqualsCommittedGeometry`（None/Tail/Velocity） | 60 个样本逐点 `AddPoint` 后在 `Complete()` 前后比较：所有 `Width` 数组**精确相等**、`BuildOutline` 扁平数组**精确相等** |
| `NoPointEverGetsNarrower`（三型） | 80 个样本逐个到达，每个旧点宽度不得下降（容差 `1e-4`） |
| `TailIsAlreadyTaperedWhileStillDrawing` | Tail、base=10、40 样本：`Points[^1].Width < Points[10].Width` 且 `Points[10].Width > 9.9`（抬笔前已存在锥形） |
| `VelocityTaperDoesNotRewriteEarlierPoints` | 先 30 慢样本取快照，再 30 个快速样本；旧 30 个宽度 **`Assert.Equal` 精确相等** |
| `NoneTaperIsUniform` | base=6，所有点宽度恰为 6 |
| `HighlighterNeverTapers` | highlighter、base=24、请求 Tail：`stroke.TaperKind == None`，所有宽度 24 |
| `OutlineIsDeterministic` | 两条相同输入的 Tail 笔迹，`BuildOutline` 数组相等 |
| `SinglePointProducesAClosedDot` | 单点 Tail base=8：轮廓点数 >=8，所有顶点到圆心距离 ∈ [0.1, 8]（实际半径 1.44） |

### `StrokeRegionTests.cs`

| 测试 | 钉住内容 |
|---|---|
| `SelfCrossingStrokeProducesOverlappingFilledRegionsNotOneEvenOddFigure` | 4 点自交笔迹（base=20、None、highlighter）→ `BuildUnionContours.Length == Count-1 == 3`，每个 contour `SignedArea > 0` |
| `ReversingSegmentDirectionKeepsSameFillWinding` | 同段正/反向构造，两个轮廓 `SignedArea > 0` |
| `SinglePointStillHasOneFilledRegion` | 单点 → 1 个 contour 且面积为正 |

### `DomainTests.cs` 相关部分

| 测试 | 钉住内容 |
|---|---|
| `UndoAndRedoRoundTrip` | Add → Undo 后 doc.Count 0 → Redo 后 1 |
| `PushClearsRedoBranch` | Undo 后 Push 清空 redo |
| `CapacityDropsOldest` | 容量 3，Push 10 次后 `UndoCount == 3` |
| `EraseRemovesOnlyTouchedStrokes` | 近笔迹 (100,100)-(140,100) 在 (120,100) 半径 12 下被整笔擦除，远笔迹保留；只返回 1 条 |
| `TransformMovesStrokesAndScalesWidth` | `scale(2)*translate(5,7)` 后 p0 = (25,27)，宽度 = 之前 × 2（3 位小数） |

其他间接覆盖：
- `ReplayTests.ReplayPreservesStoredWidths`：回放的部分笔迹保留存储宽度（`RestorePoints` 不重算）。
- `ShapeDrawingSemanticsTests` 大量用 `ArcLength`、`DistanceToSegment`、`Points[0].Width`，
  依赖“形状不笔锋 / 点宽直接存储”的语义。
- `SelfTest`（运行时自检）会画 base=14 Tail、base=34 highlighter 等并检查宽度相等/统一。

文档中的验证声明：`docs/PORTING-NOTES.md` 称笔锋「重写并验证：单测 + 运行时自检」；
自交几何称 3 项几何回归测试通过，但 **Win2D 实际栅格化未验证**。`StrokeEraser` 与
`MatrixTransformCenter` 无行为验证。

---

## 14. 逐功能移植分类

| id | 功能 | 分类 | 说明 |
|---|---|---|---|
| `ink-point` | 采样点（含 Width 存储语义） | pure-logic | 注意 struct→class 的深拷贝 |
| `ink-color` | ARGB 打包色 | pure-logic | 用 `>>> 0` 保持 uint |
| `stroke-attributes` | 笔迹属性/荧光笔有效值 | pure-logic | |
| `taper-tail` | 点集笔锋（10/4/0.2） | pure-logic | 逐 float 复刻，含短笔画偏细行为 |
| `taper-velocity` | 速率笔锋（距离/20 分段） | pure-logic | 不读 Pressure/Time |
| `ink-stroke` | 笔迹聚合/边界/变换/距离/Clone | pure-logic | `AsSpan` 改写、Transform 宽度缩放 |
| `stroke-geometry-union` | 逐段胶囊 + 同向 Winding 并集 | pure-logic | 24 段圆、12 步半圆、26 点/段 |
| `ink-document` | 文档集合/命中/整笔擦 | pure-logic | 事件改回调；引用相等集合 |
| `stroke-eraser` | 线条擦二次方程切割 | pure-logic | 未行为验证；新 Id 碎片 |
| `matrix-transform-center` | 双指枢轴 | pure-logic | `isLoaded` 分支当前恒 true |
| `undo-stack` | 有界撤销/重做 | pure-logic | 异常非原子、Push 已应用约定 |
| `icink-serialization` | `.icink` 扁平/板包/PPT | adapt | JSON/文件 API 不同；字段必须一致 |
| `stroke-rendering-winding` | Win2D Winding 填充/缓存 | platform-bound | 几何生成可复用；填充 API 换 ArkUI |
| `pen-sample-input` | Pointer → InkPoint 采样 | adapt | 压感/contact/时间戳来源 |
| `eraser-mode-wiring` | 面积擦/整笔擦/橡皮尺寸 | adapt | 半径常量与手势采样逻辑 |
| `pen-palette-ink-bindings` | 笔锋/粗细/透明度 UI 接线 | adapt | 1–30 菜单↔0.5–15 存储、20–240 步 20 |

---

## 15. 风险 / 已知问题 / 决策点

**R1 浮点一致性**：C# 全程 `float`，但 `SignedArea` 用 `double` 累加、`VelocityTaper` 的
`pressure` 表达式是 double 再 cast float。ArkTS 只有 float64，必须用 `Math.fround` 在
每次赋值/运算后收窄，否则轮廓顶点会出现 ~1e-6 差异，`BuildOutline` 精确相等测试会失败。
建议共享向量设置绝对容差 `1e-4`，并另行保留一套 float32 严格的参考输出。

**R2 无黄金向量**：仓库当前**没有 `parity/` 目录**（`parity/manifest.json`、`parity/vectors/` 均不存在）。
移植前建议先在 C# 生成：Tail/Velocity 固定采样序列的逐点宽度、轮廓顶点数组、
StrokeEraser 碎片点数组、.icink 往返 JSON；ArkTS 消费同一文件。

**R3 高亮笔 TaperKind setter**：`TaperKind` setter 不走 `EffectiveTaper`，已完成荧光笔可被加笔锋。

**R4 板包丢透明度**：`BoardService.StrokeFile` 没有 `HighlighterAlpha`，板包往返后一律 96；
而扁平 `.icink` 能保留。若要求两侧/两种容器一致，需要加字段并升 `BoardFormatVersion`（破坏兼容）或在
端口层明确记录差异。

**R5 选区改笔宽无效**：`BoardWindow.ScaleSelectionWidth/RestoreSelectionWidth` 只改
`Attributes.BaseWidth`，已存 `InkPoint.Width` 不变（渲染以逐点 Width 为准）。需要公开
`RebuildWidths()` 或在改宽时按比例缩放每个点 Width；否则移植后按钮仍“没反应”。

**R6 命中用 BaseWidth**：`InkDocument.HitTest` 的最终判定用 `Attributes.BaseWidth*0.5`，
不是逐点宽度；速率笔锋/缩放过的笔迹可能命中偏宽或偏窄。整笔橡皮与 8 DIP 点选共用此逻辑。

**R7 面积擦撤销的快照语义**：撤销/重做时 `RestoreDocument` 用 `Clone` **替换所有笔迹对象**并清空选区；
更早的撤销条目若持有旧对象引用（如 transform 的 targets），后续撤销会作用在已脱离文档的对象上。
若 HarmonyOS 追求稳健，建议以稳定 Id + 文档级快照/命令重做，而不是保留对象引用。

**R8 图层顺序**：整笔擦的 undo 用 `Document.AddRange(removed)` 追加到末尾，会把擦掉的笔迹移到最上层；
多笔重叠场景撤销后 z-order 改变。面积擦的全区快照不受影响。

**R9 短笔画偏细 / 轻点变细**：Tail 模式下 <10 个采样的笔迹永远达不到全宽；单点只有 0.36×宽。
如果 HarmonyOS 的触摸采样率更高（同样手势产生更多点），Tail 的最终尾部形态会更接近 PC 笔，
但 Velocity 的“每采样距离/20”会因采样率不同而显著改变宽度——跨设备一致性需决策（见 Q12）。

**R10 板页保存分流**：`SavePage` 仅按“图片数==0”决定扁平/板包，媒体不参与；只有视频的页面会丢媒体。
移植时建议改为“有图片或媒体即写板包”。

**R11 `.icink` 版本不校验**：扁平文件 `Version` 完全不读；板包严格 ==1。未知版本策略需统一。

**R12 `InkDocument.Insert` 无 null 检查**，且当前 App 未调用；`HitTest(Rect)` 也未被调用。
可作为纯逻辑保留，或在端口裁剪并记录（不建议 drop，成本低）。

**R13 缓存失效耦合**：完成笔迹按 `Id` 缓存渲染几何；任何 `RestorePoints/Transform/BaseWidth`
原地修改后必须 `Document.Touch()`，否则画面旧。ArkTS 渲染器若同样缓存，需要同样的失效契约。

---

## 16. ArkTS 下游实现清单（按依赖顺序）

1. `InkPoint` / `InkColor` / `Rect` / `StrokeAttributes`（值语义或显式 copy）。
2. `TaperProfile`：`NoneTaper`、`TailTaper`、`VelocityTaper`；常量 10/4/0.2、1/20、0.05/0.25/1.5、
   0.3/1.2、0.4/0.05、clamp 0.15/1.8。所有 float 运算用 `Math.fround`。
3. `InkStroke`：`addPoint`（先压 0 宽再 apply）、`complete` 空操作、`rebuildWidths`、
   `bounds`、`arcLength`、`transform`、`distanceTo(Segment)`、`clone`、`restorePoints`。
4. `StrokeGeometry`：24 段圆、12 步半圆（含端点排除）、26 点胶囊、`signedArea` double 累加、
   同向归一、`buildOutline` 扁平诊断。
5. `InkDocument`：集合/事件/命中/整笔擦。
6. `StrokeEraser`：二次方程切割、全局最大宽度切半径、碎片插值/去重。
7. `MatrixTransformCenter` + 设置枚举。
8. `UndoStack` + `DelegateAction`。
9. `.icink` 读写（PascalCase、`Version/Strokes/Color/Width/Highlighter/HighlighterAlpha/Taper/Shape/Points`；
   板包 `Format:"inkcanvas-board"/Version:1/Background/GridSpacing/Pages`）。
10. 渲染（ArkUI Canvas Winding）+ 输入适配（笔压/时间戳/historical points）。
11. 共享黄金向量与双向测试（生成于 C#，ArkTS 消费）。

---

## 17. 开放问题（需产品/架构决策）

1. **Q1 黄金向量归属**：谁来生成并提交 `parity/vectors/ink-*.json`？用精确 float32 还是 1e-4 容差？
2. **Q2 浮点口径**：ArkTS 是否接受全程 `Math.fround` 的性能成本？轮廓逐顶点容差取多少？
3. **Q3 荧光笔 TaperKind bug**：保留“已完成荧光笔可被 set TaperKind 加笔锋”，还是修正为始终 EffectiveTaper？
4. **Q4 板包透明度**：`HighlighterAlpha` 丢失是既有缺陷还是可接受？是否升级板包版本以纳入？
5. **Q5 选区改笔宽**：移植时补 `RebuildWidths`/按比例重算，还是接受“无视觉效果”？
6. **Q6 命中宽度**：`HitTest` 是否应改用逐点 `Width`（更准）？改则需同步 C#。
7. **Q7 撤销对象身份**：面积擦撤销替换对象导致旧撤销条目失效；HarmonyOS 是否改为 Id 稳定模型？
8. **Q8 单点/短笔画**：Tail 下轻点 0.36×宽、短笔画不到全宽，是否符合课堂书写预期？是否需要额外单测钉住？
9. **Q9 ArkUI 输入**：笔压、接触面积、时间戳（ms/µs）在 HarmonyOS 27/API 26 上的获取方式；
   `HistoricalPoint` 是否提供每个合并样本的压力？
10. **Q10 ArkUI 填充规则**：`CanvasRenderingContext2D.fill(path, "nonzero")` 是否可用？
    Path2D 是否支持 26 点多边形与一次填充？
11. **Q11 速率定义**：`speed=(d1+d2)/20` 与采样率强相关，是否改为 `distance/TimeMs` 归一化以跨设备一致？
    改则现有笔迹外观变化，需要视觉验收。
12. **Q12 `.icink` 兼容**：ArkTS 是否必须能读 C# 现有文件（PascalCase、uint 颜色、float 数组）？
    写回是否必须保持字段顺序/大小写完全一致？
13. **Q13 橡皮默认值**：`InkController.EraserRadius` 属性默认 16 DIP，但启动即被 `AppServices`
    覆盖为 `45×factor`；HarmonyOS 是否保留这个“构造后覆盖”的两段默认？
14. **Q14 未验证功能**：`StrokeEraser` 与 `MatrixTransformCenter` 在 Windows 侧尚未做行为/实机验证，
    HarmonyOS 移植是否先补 Windows 单元测试再对齐？

---

## 18. 关键源码位置索引

| 内容 | 文件 |
|---|---|
| 点/宽度语义 | `src/InkCanvas.Core/Ink/InkPoint.cs` |
| 颜色打包 | `src/InkCanvas.Core/Ink/InkColor.cs` |
| 笔迹聚合/边界/变换 | `src/InkCanvas.Core/Ink/InkStroke.cs` |
| 笔锋（10/4/0.2；距离/20 分段） | `src/InkCanvas.Core/Ink/TaperProfile.cs` |
| 属性/荧光笔 | `src/InkCanvas.Core/Ink/StrokeAttributes.cs` |
| 胶囊/Winding 轮廓 | `src/InkCanvas.Core/Ink/StrokeGeometry.cs` |
| 线条擦 | `src/InkCanvas.Core/Ink/StrokeEraser.cs` |
| 手势枢轴 | `src/InkCanvas.Core/Ink/MatrixTransformCenter.cs` |
| 文档/命中/整笔擦 | `src/InkCanvas.Core/Ink/InkDocument.cs` |
| 撤销栈 | `src/InkCanvas.Core/History/UndoStack.cs` |
| `.icink` 扁平格式 | `src/InkCanvas.Features/Persistence/Stores.cs` |
| 板包格式/分流 | `src/InkCanvas.Features/Board/BoardService.cs` |
| Win2D 渲染 | `src/InkCanvas.Ink/Rendering/StrokeRenderer.cs` |
| 覆盖层合成 | `src/InkCanvas.Ink/Rendering/OverlayInkPresenter.cs` |
| 输入采样 | `src/InkCanvas.Controls/Controls/InkSurface.cs`、`src/InkCanvas.Platform/Windows/LayeredInkWindow.cs` |
| 橡皮模式/半径 | `src/InkCanvas.Ink/InkController.cs`、`src/InkCanvas.App/Services/AppServices.cs`、`src/InkCanvas.App/Views/BoardWindow.cs` |
| 笔锋/宽度/透明度 UI 映射 | `src/InkCanvas.Controls/Controls/PenPalette.cs`、`src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs` |
| 默认/键名 | `src/InkCanvas.Core/Settings/AppSettings.cs` |
