# 02 — InkCanvas.Core 域模型源码盘点（WinUI 3 → HarmonyOS ArkTS 移植）

- 盘点对象：`D:/InkCanvas` 工作树（分支 `feat/winui3-migration`，含未提交的 3.4.0 改动；下文以工作树为准）。
- 盘点范围（任务指定）：`ShapeGenerator`、`ShapeRecognizer`、`SelectionGeometry`、`InkReplaySession` + `ReplayClock`、`PixelCompositing`、`TouchContactClassifier`、`ThemeResolver`、`AppSettings` + `OriginalMenuCatalog`、`ReleaseFeed` + `UpdatePolicy` + `AppVersion`、`FileLog`，以及钉住这些行为的测试。
- 方法：逐文件完整阅读 + 追消费方（InkController、PalmRejector、AppServices、SettingsWindow、ShapePreview、ReplayWindow、UpdateService/UpdateCoordinator）+ 读取全部相关测试。
- 只读：本文件是唯一新增文件，未改动任何源码。
- 重要现状：仓库里**尚不存在** `harmony/` 与 `parity/` 目录（`test -d` 失败）。CLAUDE.md 描述的 manifest / `@parity` / 黄金向量契约目前只有文字约定，没有任何已落地文件。因此本报告额外给出「建议转成黄金向量的输入/输出」。

---

## 0. 结论摘要（给移植者）

1. 这 14 个 Core 文件几乎没有 Win32/COM/Win2D 依赖，绝大多数是**可 1:1 重写的纯逻辑**；真正的平台摩擦集中在四类：浮点（C# `float` vs ArkTS `number`/double）、`System.Numerics` 的 `Vector2/Matrix3x2`、`System.Text.Json` 的序列化细节、以及文件/时间/触摸尺寸来源。
2. `ShapeGenerator` 是本次清单里最重、最容易移植错的部分：25 种 `ShapeKind`（任务文字写「24 个」，实际枚举有 **25** 项），包含 4 类语义（指针按下点=中心、指针按下点=顶点、拖拽框、两步式），以及 10 余个必须原样保留的常量（5 / 13.8 / 8.28 / 0.8 / 0.05π / 0.083π / 2.646 / 30×10 / 20×7 / 25 / 1.76 / 0.5 采样步长 / 72 段）。
3. `AppSettings` 的 JSON 键名是 `Settings.json` 兼容的硬契约。特别注意：`behavior` 键装的是 PPT 设置（不是 `powerPoint`）；`AutoSwitchTwoFingerGesture` 是唯一首字母大写的键；`isSecondConfimeWhenShutdownApp` 的拼写错误必须保留；枚举以**数字**序列化。
4. `TouchContactClassifier` 与 `ThemeResolver` 在源码注释里明确标注「未验证」，且 `TouchContactClassifier` **没有任何直接单元测试**（只有 `PalmRejector` 自身的阈值测试），移植时必须如实标注未验证。
5. `PixelCompositing` 是 premultiplied **BGRA** 整数合成；HarmonyOS `PixelMap` 常见格式是 RGBA_8888，通道顺序是整个功能能否复用的关键差异。
6. `SelectionGeometry` 的矩阵语义是 `System.Numerics` 的**行向量**约定，ArkTS 自实现 3×2 矩阵时必须复刻分量定义与乘法顺序，否则旋转/缩放会悄悄反向。

---

## 1. 基础类型与数值语义（所有功能共同前提）

这些类型虽然不在指定清单里，但清单中的每个算法都以它们为输入/输出。ArkTS 侧必须先有等价物。

### 1.1 `InkPoint`（`src/InkCanvas.Core/Ink/InkPoint.cs`）

| 字段 | 类型 | 含义 |
|---|---|---|
| `X`, `Y` | float | 世界坐标（DIP/px，由调用方决定） |
| `Pressure` | float | 原始压感 [0,1]，0.5 = 无压感信息 |
| `Width` | float | 最终渲染宽度（DIP），**存储值，不重算** |
| `TimeMs` | float | 距笔画开始的毫秒数 |

关键契约：`Width` 是冻结的存储值；`Complete()` 不重算宽度。这是「笔锋闪烁修复」的结构性保证。

### 1.2 `InkStroke`（`src/InkCanvas.Core/Ink/InkStroke.cs`）

| 成员 | 语义 |
|---|---|
| `Id` | `Guid.NewGuid()` |
| `Attributes` | 不可替换，仅可改字段 |
| `Points` / `Count` | 点列表 |
| `IsCompleted` | `Complete()` 置 true |
| `IsRecognizedShape` | 形状生成/识别产物标记；`.icink` 里存为 `shape` |
| `AddPoint(x,y,pressure=0.5,timeMs=0)` | 先以 `width=0` 加点，再 `_taper.Apply(...)` |
| `Bounds` | 惰性缓存；各点 X/Y 的 min/max 再向四周扩 `maxWidth/2` |
| `ArcLength()` | 相邻点欧氏距离之和（中心线长度） |
| `Transform(m, scaleWidth=true)` | `widthScale = sqrt(abs(M11*M22 - M12*M21))`；`<=0.0001` 时取 1；每点乘 `widthScale`，`Attributes.BaseWidth *= widthScale` |
| `DistanceTo(p)` / `DistanceToSegment` | 点到折线最短距离；段长平方 `<1e-6` 时退化为到端点距离，`t` 夹在 [0,1] |
| `Clone()` | 复用 `Id`、点列表逐值复制、`IsRecognizedShape` 保留 |
| `RestorePoints(points)` | 直接替换点列表，保留已算宽度（反序列化/回放用） |
| `TaperKind` setter | 若已 `IsCompleted` 会 `RebuildWidths()` |

`Rect`（同文件，`readonly record struct`）：

- 字段 `Left, Top, Right, Bottom`（float）。
- `Width = Right-Left`，`Height = Bottom-Top`。
- `IsEmpty => Width <= 0 || Height <= 0`。
- `Center = ((Left+Right)*0.5, (Top+Bottom)*0.5)`。
- `Contains` 为**闭区间**（含边界）。
- `Intersects` / `Union`（空矩形被忽略）/ `Inflate(d)`。

> 移植提示：ArkTS 中用 `class Rect` 或对象 + 纯函数均可，但 `IsEmpty` 的 `<= 0` 与 `Contains` 的闭区间必须一致，否则选区/命中判定会差一像素。

### 1.3 `StrokeAttributes`（`src/InkCanvas.Core/Ink/StrokeAttributes.cs`）

| 字段 | 默认 | 说明 |
|---|---|---|
| `Color` | `InkColor.Black` (A=255,R=G=B=0) | |
| `BaseWidth` | `3f` | 全宽（DIP） |
| `IsHighlighter` | `false` | |
| `HighlighterAlpha` | `96` | 仅荧光笔生效 |
| `TaperKind` | `TaperKind.Tail` (=1) | |
| `EffectiveTaper` | 计算 | 荧光笔强制 `None` |
| `EffectiveColor` | 计算 | 荧光笔为 `Color.WithAlpha(HighlighterAlpha)` |

`TaperKind`：`None=0`（等宽）、`Tail=1`（笔尖渐细）、`Velocity=2`（速度驱动）。`TailTaper` 参数：`TailPoints=10`、`HeadPoints=4`、`TipRatio=0.2`；`VelocityTaper` 的速度阈值 0.25/0.05、采样 `/20`、比例夹在 [0.15,1.8]。形状生成器在 App 层总是被喂 `TaperKind.None`（见 `InkController.ShapeAttributes()`），但 `ShapeGenerator.Stroke()` 本身不强制。

### 1.4 `InkColor`（`src/InkCanvas.Core/Ink/InkColor.cs`）

`readonly record struct InkColor(byte A, byte R, byte G, byte B)`：

- `Opaque(r,g,b) = (255,r,g,b)`。
- `PackedArgb = (A<<24)|(R<<16)|(G<<8)|B`（uint）。
- `FromPackedArgb(v)` 反向拆包。
- 常量：`Black=Opaque(0,0,0)`、`White`、`Red=Opaque(220,38,38)`、`Transparent=(0,0,0,0)`。

### 1.5 `System.Numerics` 语义（ArkTS 必须自实现）

- `Vector2` 是 **float32**；`Distance`、`Normalize`、`Dot` 均为 float。
- `Matrix3x2` 字段顺序/含义：`M11 M12 / M21 M22 / M31 M32`，**行向量**变换：
  - `x' = x*M11 + y*M21 + M31`
  - `y' = x*M12 + y*M22 + M32`
- `CreateScale(sx,sy)` → `M11=sx, M22=sy`，其余 0。
- `CreateRotation(θ)` → `M11=cosθ, M12=sinθ, M21=-sinθ, M22=cosθ`。
- `CreateTranslation(tx,ty)` → `M31=tx, M32=ty`。
- 矩阵乘法 `A*B` 表示先 A 后 B（行向量）。`About(pivot,m) = T(-pivot) * m * T(pivot)`。
- `Vector2.Transform(v,m)` 按上式展开。

> 移植建议：ArkTS 侧实现一个 6 分量 `Mat3x2` + `multiply(a,b)`，把「先 A 后 B」写死成同一顺序；所有黄金向量输出 6 个分量而不是只输出变换后的点。

---

## 2. 功能：ShapeGenerator（预设图形生成）

- 文件：`src/InkCanvas.Core/Shapes/ShapeGenerator.cs`（610 行，工作树 3.4.0 大改后版本）。
- 公开类型：`enum ShapeKind`、`static class ShapeGenerator`。
- 平台依赖：**无**（只依赖 `System.Numerics`、`InkStroke`/`InkPoint`/`StrokeAttributes`）。属于 `pure-logic`。
- 消费方：
  - `src/InkCanvas.Ink/InkController.cs`（主要）：`GenerateShapePreview`、`CommitShapeDrag`、`TryRecognize`、`_stepAnchor/_stepK/_stepPending` 两步状态机。
  - `src/InkCanvas.Controls/Controls/ShapePreview.cs`：形状菜单（`ShapeMenuCatalog`），标签/图标/排布。
  - `src/InkCanvas.Controls/Controls/FloatingBar.cs:915-942`：浮动条形状子菜单标签。
  - `src/InkCanvas.App/Diagnostics/SelfTest.cs:102,114`：自检使用 `Coordinate3D`/`Circle`。
  - `src/InkCanvas.App/Services/ShellController.cs:168`：默认 `ShapeKind.Line`。
- 测试：`tests/InkCanvas.Core.Tests/ShapeDrawingSemanticsTests.cs`（24 个用例）+ `DomainTests.cs` 的 `ShapeGeneratorTests`（4 个）。

### 2.1 枚举与数值（隐式 int，声明顺序即值）

| 值 | 成员 | 值 | 成员 |
|---|---|---|---|
| 0 | Line | 13 | Circle |
| 1 | DashedLine | 14 | EllipseFromCenter |
| 2 | DotLine | 15 | EllipseWithFoci |
| 3 | Arrow | 16 | DashedCircle |
| 4 | ParallelLine | 17 | Hyperbola |
| 5 | CoordinateBoth | 18 | HyperbolaWithFoci |
| 6 | CoordinateXOneWay | 19 | ParabolaRight |
| 7 | CoordinateYOneWay | 20 | ParabolaWithFoci |
| 8 | CoordinateFirstQuadrant | 21 | ParabolaUp |
| 9 | Coordinate3D | 22 | Cylinder |
| 10 | Rectangle | 23 | Cone |
| 11 | RectangleFromCenter | 24 | Cuboid |
| 12 | Ellipse | | |

> 注意：任务文字说「24 个图形」，代码实际 **25** 个（`docs/PORTING-NOTES.md:76` 又写「26 种」——文档数字不可靠，以枚举为准）。

### 2.2 公开常量（必须逐字节一致）

| 常量 | 值 | 含义 / 上游引用 |
|---|---|---|
| `FullEllipseSegments`（private） | `72` | 整椭圆采样段数 |
| `DashLength` | `5f` | 虚线 dash 长度（px） |
| `DashPeriod` | `5f * 2.76f` = `13.8f` | 虚线周期 |
| `DotPeriod` | `3f * 2.76f` = `8.28f` | 点线点距 |
| `DotPressure` | `0.8f` | 点线点的压感 |
| `EllipseDashStep` | `0.05f` | 椭圆角向 dash 宽度（单位 π） |
| `EllipseDashPeriod` | `0.05f * 1.66f` = `0.083f` | 椭圆角向 dash 周期（单位 π） |
| `SolidEllipseRatio` | `2.646f` | 柱/锥底面椭圆短轴 = 宽度 / 2.646 |

其余散落常量（非 public，但同样是契约）：

- 平行线偏移：`x = 25f`，倍数 `{-3,-1,1,3}` → ±75、±25。
- 箭头头：`30f × 10f`；坐标轴箭头头：`20f × 7f`。
- 坐标轴尾长：`25f`；双向轴镜像尾偏移：`20f`。
- 空间坐标 z 轴除数：`1.76f`。
- 抛物线/双曲线采样步长：`0.5f`。
- 退化阈值：`0.01f`（多数图形的宽/高）、`1e-4f`（箭头方向向量长度平方）。
- 形状点时间戳步长：`8f` ms（`Stroke()` 每点 `t += 8f`）。

### 2.3 总入口算法

```
Generate(kind, start, end, attrs):
  a = attrs ?? new StrokeAttributes()
  w = end.X - start.X; h = end.Y - start.Y
  center = (start + end) * 0.5
  rx = |w| * 0.5; ry = |h| * 0.5
  switch kind -> 追加若干 InkStroke
  对 result 每个 s:
    s.IsRecognizedShape = true
    s.Complete()
  return result
```

`Generate` 的返回顺序即渲染顺序、也是下游 `IsNewerThan`/撤销单元的组成部分，必须保留。

### 2.4 逐图形算法

#### 2.4.1 线条类

- **Line(0)**：`[Stroke(a, start, end)]`，2 点。
- **DashedLine(1)**：`Dashed(a, start, end, dash=5, gap=DashPeriod-DashLength=8.8)`。
- **DotLine(2)**：`Dots(a, start, end)`。
- **Arrow(3)**：`Arrow(a, start, end)` = `ArrowWithHead(a,start,end,30,10)`。
- **ParallelLine(4)**：`ParallelLines(a, start, end)`，见 2.4.6。

`Dashed` 精确算法：

```
delta = end - start; len = |delta|
if len < 0.5: return []
dir = delta / len
pos = 0
while pos < len:
    stop = min(pos + dash, len)
    result.Add(Stroke(a, start + dir*pos, start + dir*stop))
    pos = stop + gap
```

注意：即使最后一段被 `len` 截断，仍然 `pos = stop + gap` 后结束。长度 100、dash 5/gap 8.8 → 8 段（pos=0,13.8,27.6,41.4,55.2,69,82.8,96.6），最后一段从 96.6 到 100（被裁剪）。测试 `DashedLineUsesDash5AndPeriod13Point8` 钉住该 8 段与端点 (0,0)-(5,0)、(13.8,0)-(18.8,0)。

`Dots`：

```
delta = end - start; len = |delta|
if len <= 0: return []
dir = delta / len
for (i = 0; i < len; i += 8.28):
    result.Add(Dot(a, start + dir*i, 0.8))
```

长度 80 → i=0,8.28,...,74.52 共 10 个点（下一个 82.8 不 < 80）。测试钉住 10 个点、每点 `Pressure=0.8`、`Width=BaseWidth*1.6`。

#### 2.4.2 坐标系类

全部由 `AxisArrow`（头 20×7）构成。

- **CoordinateBoth(5)**（模式 11）：
  1. x 轴：`AxisArrow(a, (2*start.X - (end.X - 20), start.Y), (end.X, start.Y))`
  2. y 轴：`AxisArrow(a, (start.X, 2*start.Y - (end.Y + 20)), (start.X, end.Y))`
  两条都画，指针按下点是原点。
- **CoordinateXOneWay(6)**（模式 12）：
  - 守卫：`|w| < 0.01` → 直接 break（**x、y 两条都不画**）。
  - x 轴：`AxisArrow(a, (start.X + sign(-w)*25, start.Y), (end.X, start.Y))`
  - y 轴：同 CoordinateBoth 的 y 轴。
- **CoordinateYOneWay(7)**（模式 13）：
  - 守卫：`|h| < 0.01` → break（两条都不画）。
  - x 轴：同 CoordinateBoth 的 x 轴。
  - y 轴：`AxisArrow(a, (start.X, start.Y + sign(-h)*25), (start.X, end.Y))`
- **CoordinateFirstQuadrant(8)**（模式 14）：
  - 守卫：`|w| < 0.01 || |h| < 0.01` → break。
  - x：`(start.X + sign(-w)*25, start.Y) → (end.X, start.Y)`
  - y：`(start.X, start.Y + sign(-h)*25) → (start.X, end.Y)`
- **Coordinate3D(9)**（模式 17）：三条，全部从 `start` 出发：
  1. x：`start → (start.X + |w|, start.Y)`
  2. y：`start → (start.X, start.Y - |h|)`
  3. z：`d = (|w| + |h|) * 0.5`；`start → (start.X - d/1.76, start.Y + d/1.76)`
  注意 y 轴永远朝**屏幕上方**（`-|h|`），与拖拽方向无关；x 永远向右。

`sign(-w)` 的含义：`MathF.Sign(-w)`，`w>0`（向右拖）时结果为 -1，尾段落在原点左侧 25px，箭头指向右。`sign(0)=0` 会导致尾段与原点重合（实际由 `Scientific` 的 0.01 守卫基本排除，但 CoordinateBoth 无守卫）。

坐标轴箭头测试（`CoordinateAxisArrowHeadIsFixed20By7`）：`start=(0,0), end=(100,-80)`，FirstQuadrant 两条：
- x 轴首点 `(-25,0)`，倒刺 `(80,-7)`、`(80,7)`；
- y 轴首点 `(0,25)`，倒刺 `(-7,-60)`、`(7,-60)`。

#### 2.4.3 矩形/圆/椭圆类

- **Rectangle(10)**：单条闭合 5 点 `start → (start.X,end.Y) → end → (end.X,start.Y) → start`。
- **RectangleFromCenter(11)**：单条闭合 5 点，中心=指针按下点，半宽高=**完整拖拽量**（不是一半）：
  - `p0=(2sx-ex, 2sy-ey)`, `p1=(2sx-ex, ey)`, `p2=end`, `p3=(ex, 2sy-ey)`, `p4=p0`。
  - 上游位置：无中心点（菜单图标上的点是装饰）。
  - 测试：start(100,100), end(140,130) → (60,70),(60,130),(140,130),(140,70),(60,70)。
- **Ellipse(12)**：中心=两点中点，`rx=|w|/2, ry=|h|/2` → `EllipseStroke`。
- **Circle(13)**：中心=指针按下点 `start`，半径=`Vector2.Distance(start,end)`（欧氏距离，不是 |w| 或 |h|）；`EllipseStroke(a, start, r, r)`。
  - 测试：start(100,100), end(130,140) → R=50，所有点到 start 距离 50，首点 (150,100)。
- **EllipseFromCenter(14)**：中心=`start`，`rx=|w|, ry=|h|`（完整拖拽量），无中心点。
- **EllipseWithFoci(15)**：
  ```
  aa=|w|; bb=|h|
  result.Add(EllipseStroke(a, start, aa, bb))
  c = sqrt(|aa² - bb²|)
  if aa > bb: 焦点 (start.X ± c, start.Y)，各一个压力 1 点
  else if aa < bb: 焦点 (start.X, start.Y ± c)
  ```
  - 焦点顺序：先 `+c` 后 `-c`（x 轴）；先 `-c` 后 `+c`（y 轴，代码为 `start.Y - c` 再 `start.Y + c`）。
  - 压力 1 点的宽度 = `BaseWidth * (1/0.5)` = `2*BaseWidth`（`Dot` 直接写 Width，不过 taper 管线）。
  - `aa == bb`（正圆）时**不画焦点**，只返回 1 条椭圆。
  - 测试：start(100,100), end(160,120) → c=sqrt(3600-400)=56.5685，焦点 (156.5685,100)/(43.4315,100)。
- **DashedCircle(16)**：中心=`start`，`r=distance`，`DashedEllipseArcs(a, start, r, r, frontHalf:true, backHalf:true)`。
  - `r=50` 时严格 26 条（每半 13 条），测试钉住。

`EllipseStroke`：
```
Stroke(a, EllipsePoints(center, max(rx,0.5), max(ry,0.5), 0, 2π, 72))
```
`EllipsePoints(center,rx,ry,from,to,segments)`：
```
pts = new Vector2[segments+1]
for i in 0..segments:
  ang = from + (to-from)*i/segments
  pts[i] = center + (cos(ang)*rx, sin(ang)*ry)
```
- 整椭圆 73 个点；首点 = 最右点 `(cx+rx, cy)`；由于屏幕 Y 向下，`sin` 正方向向下，视觉上顺时针。
- `SegmentsFor(from,to) = max(2, ceil(72*(to-from)/(2π)))`；半圆 = 36 段 → 37 点。

`DashedEllipseArcs` 精确算法：
```
rx=max(rx,0.5); ry=max(ry,0.5)
if frontHalf: AddHalf(0,1)
if backHalf:  AddHalf(1,2)

AddHalf(from, to):
  for (i = from; i < to; i += 0.083f):
      a0 = π*i
      a1 = π*(i + 0.05f)
      segments = max(2, ceil((a1-a0)/0.01f))   // 0.05π/0.01 = 15.708 → 16
      result.Add(Stroke(a, EllipsePoints(center, rx, ry, a0, a1, segments)))
```
- 每半 13 段（i=0..0.996，`0.996+0.083 > 1`）。
- 每段 17 点。
- frontHalf 对应 r∈[0,π]（屏幕下半，上游 `isDrawBottom`）；backHalf 对应 r∈[π,2π]（屏幕上半，`isDrawTop`）。
- 最后一段会越过 π（0.996π→1.046π），与另一半在 π 附近重叠约 0.046π；这是原实现行为，移植时不要"修正"。

#### 2.4.4 圆锥曲线类

- **Hyperbola(17) / HyperbolaWithFoci(18)**：第一步只生成**两条虚线渐近线**（曲线本身在 `GenerateHyperbolaBranches`）。
  ```
  if |w| < 0.01 || |h| < 0.01: break
  Dashed(a, (2sx-ex, 2sy-ey), end)              // 过 start 的直线 1
  Dashed(a, (2sx-ex, ey), (ex, 2sy-ey))         // 过 start 的直线 2
  ```
  两条都过指针按下点 `start`（=双曲线中心）。dash=5、gap=8.8。
  测试：start(100,100), end(160,140) → 每条长度 144.222，`ceil(144.222/13.8)=11` 段 → 共 22 条；所有点落在两条渐近线上。
- **ParabolaUp(21)**（y = a·x²，顶点=指针按下点）：
  ```
  守卫 |w|,|h| >= 0.01
  a = (start.Y - end.Y) / (w*w)
  right = []; left = []
  for (i = 0; i <= |w|; i += 0.5):
      right.Add((start.X + i, start.Y - a*i*i))
      left.Add( (start.X - i, start.Y - a*i*i))
  result = [Stroke(right), Stroke(left)]      // 先右后左
  ```
  - 端点精确性：仅当 `|w|` 是 0.5 的整数倍时最后一点才精确落在 `end`；否则最多差 0.5px。上游声称"精确穿过 end"，实现是采样量化后的精确。
  - `w<0` 时右支终点是镜像点 `(2sx-ex, ey)`，左支终点才是 `end`；测试只覆盖 `w>0`。
  - 向下拖（h>0）时 a<0，开口向下。
- **ParabolaRight(19)**（y² = a·x，顶点=指针按下点）：
  ```
  a = (start.X - end.X) / (h*h)
  ParabolaBranches(a, start, a, |h|)
  ```
- **ParabolaWithFoci(20)**：
  ```
  p = h*h / (2*(start.X - end.X))
  a = 0.5 / p
  ParabolaBranches(a, start, a, |h|)
  焦点 Dot(a, (start.X - p/2, start.Y), pressure=1)
  ```
  测试：start(0,0), end(50,100) → p=-100，焦点 (50,0)。

`ParabolaBranches`：
```
down=[]; up=[]
for (i = 0; i <= extent; i += 0.5):
    down.Add((vertex.X - a*i*i, vertex.Y + i))
    up.Add(  (vertex.X - a*i*i, vertex.Y - i))
return [Stroke(down), Stroke(up)]      // 先下后上
```
- 向 `+y`（屏幕下）的支先返回。
- 采样点数 = `floor(extent/0.5)+1`。

#### 2.4.5 立体图形类

**Cylinder(22)**：

```
top    = start.Y > end.Y ? (start.X, end.Y) : start
bottom = start.Y > end.Y ? (end.X, start.Y) : end
halfW  = |top.X - bottom.X| * 0.5
halfH  = halfW / 2.646
midX   = (top.X + bottom.X) * 0.5

1) 顶面整椭圆：EllipseStroke(a, (midX, top.Y), halfW, halfH)     // 实线
2) 底面下半（r∈[0,π]）：Stroke(a, EllipsePoints((midX,bottom.Y), halfW, halfH, 0, π, 36))  // 实线
3) 底面上半（r∈[π,2π]）：DashedEllipseArcs(frontHalf=false, backHalf=true)  // 13 条虚线
4) 左边：Stroke(a, (top.X, top.Y), (top.X, bottom.Y))
5) 右边：Stroke(a, (bottom.X, top.Y), (bottom.X, bottom.Y))
```

- 归一化只交换 Y：上端点取 `start.X`、`end.Y`；下端点取 `end.X`、`start.Y`。X 不交换，因此 `halfW` 用绝对值，左右边仍分别落在 `start.X`/`end.X`。
- 顺序 = [顶椭圆, 前实线半椭圆, 13 条虚线段, 左, 右] 共 **17** 条（测试钉住）。
- 顶椭圆包围盒：宽 `2*halfW`、高 `2*halfH`；测试用 `1` 的容差。
- 底面虚线段每条都应满足 `((x-cx)/halfW)² + ((y-cy)/halfH)² = 1`（测试容差 2 位）。
- 侧线连接的是椭圆**中心**的 y，不是椭圆边缘。

**Cone(23)**：

```
同样的 top/bottom/halfW/halfH/midX
apex = (midX, top.Y)
1) 底面下半实线：EllipsePoints((midX,bottom.Y), halfW, halfH, 0, π, 36)
2) 底面上半 13 条虚线
3) 斜线：apex → (top.X, bottom.Y)
4) 斜线：apex → (bottom.X, bottom.Y)
```

共 **16** 条（1 + 13 + 2），测试钉住。注意 Cone **没有**顶椭圆。

**Cuboid(24)** 第一步：4 条**独立**边（便于后续单独擦除某条棱）：

```
Stroke(start, (start.X, end.Y))          // 左竖
Stroke((start.X, end.Y), end)            // 下横
Stroke(end, (end.X, start.Y))            // 右竖
Stroke(start, (end.X, start.Y))          // 上横
```

第二步 `GenerateCuboidDepth(frontTop, frontBottom, depth, attrs)`：

```
d = |depth|
off = (d, -d)
ft  = frontTop + off                                  // 后上左
fb  = (frontBottom.X + d, frontBottom.Y - d)          // 后下右
ftr = (frontBottom.X, frontTop.Y)                     // 前上右

实线 1: ft → (fb.X, ft.Y)                              // 后上横
虚线 1: (ft.X, fb.Y) → fb                              // 后下横
实线 2: frontTop → ft                                  // 左上连接
实线 3: ftr → (ftr.X + d, ftr.Y - d)                   // 右上连接
虚线 2: (frontTop.X, frontBottom.Y) → (ft.X, fb.Y)     // 左下连接
实线 4: frontBottom → fb                               // 右下连接
虚线 3: ft → (ft.X, fb.Y)                              // 后左竖
实线 5: (fb.X, ft.Y) → fb                              // 后右竖
```

- 输出顺序即上表顺序，共 8 条逻辑棱（虚线会展开成多条 dash）。
- 前置条件：`frontTop`/`frontBottom` 已被调用方 min/max 归一化（`InkController.CommitShapeDrag` 里 `_stepAnchor=(min x,min y)`、`_stepSecond=(max x,max y)`）。
- 测试 `CuboidDepthHasFiveSolidEdgesAndThreeDashedHiddenEdges`：`frontTop=(100,100), frontBottom=(200,160), d=40` → 5 条实线（弧长 >10）+ 18 条虚线（弧长 ≤10；8+5+5），总 23 条；实线端点精确列出；所有虚线点都在三条隐藏棱上。
  - 隐藏棱：(140,120)-(240,120)、(100,160)-(140,120)、(140,60)-(140,120)。
- 虚线段数：后下横长 100 → 8 段；左下连接长 `sqrt(40²+40²)=56.568` → 5 段；后左竖长 60 → 5 段。

**双曲线第二步** `GenerateHyperbolaBranches(center, k, end, withFoci, attrs)`：

```
dx = end.X - center.X; dy = end.Y - center.Y
if |dx| < 0.01 || |dy| < 0.01 || k <= 0: return []
fociOnX = |dy/dx| < k
arcs[4] = [[],[],[],[]]

if fociOnX:
    a = sqrt(|dx² - dy²/k²|)
    b = a*k
    steps = max(0, floor((|dx| - a) / 0.5))
    for j in 0..steps:
        i = a + j*0.5
        rY = sqrt(|k²*i² - b²|)
        arcs[0].Add((cx + i, cy - rY))   // 右上
        arcs[1].Add((cx + i, cy + rY))   // 右下
        arcs[2].Add((cx - i, cy - rY))   // 左上
        arcs[3].Add((cx - i, cy + rY))   // 左下
else:
    a = sqrt(|dy² - dx²*k²|)
    b = a/k
    steps = max(0, floor((|dy| - a)/0.5))
    for j in 0..steps:
        i = a + j*0.5
        rX = sqrt(|i²/k/k - b²|)
        arcs[0].Add((cx - rX, cy + i))   // 左下
        arcs[1].Add((cx + rX, cy + i))   // 右下
        arcs[2].Add((cx - rX, cy - i))   // 左上
        arcs[3].Add((cx + rX, cy - i))   // 右上

foreach arc 非空: Stroke(arc)
if withFoci:
    c = sqrt(a² + b²)
    fociOnX: Dot(cx+c, cy, 1), Dot(cx-c, cy, 1)
    else:    Dot(cx, cy+c, 1), Dot(cx, cy-c, 1)
```

- 分支顺序（0..3）固定；`arcs` 为空时跳过（`steps` 至少 0，故 j=0 总会生成顶点，除非 `a` 为 NaN）。
- `steps` 是 `floor`，可能让终点落在 `|dx|` 之前最多 0.5px。
- 第二步的 `center` 是第一步的 `start`（`_stepAnchor`），`k` 是 `|dy/dx|`（`InkController` 中 `_stepK = |(end.Y-start.Y)/(end.X-start.X)|`）。
- 第二步拖拽时 `InkController` 把 `iniP` 锁在中心（`_shapeDragStart = _stepAnchor`），所以 `end` 是相对中心的当前拖拽点。
- 测试：center(0,0)、k=1、end(100,30) → a=sqrt(10000-900)=95.3939，4 条弧，弧 0 首点 (a,0)，所有点满足 `y² = k²x² - b²`；end(30,100) → foci 在 y 轴，a 同上，6 条（4 弧 + 2 焦点），焦点 `(0,±sqrt(2)a)`。

#### 2.4.6 平行线与角度吸附

`SnapParallel(start,end)` 返回 `(snappedEnd, sin, cos)`：

```
d = distance(start,end)
if d <= 0: return (end, 0, 0)
sin = (start.Y - end.Y) / d
cos = (end.X - start.X) / d
tan = |sin / cos|

if tan < 1/12:                       // ≈0.08333 水平
    sin = 0; cos = 1
    end = (end.X, start.Y)

if tan > 0.52 && tan < 0.63:         // 30°
    sin = sign(sin)*0.5;  cos = sign(cos)*0.866
    end = (start.X + d*cos, start.Y - d*sin)

if tan > 0.92 && tan < 1.08:         // 45°
    sin = sign(sin)*0.707; cos = sign(cos)*0.707
    end = (start.X + d*cos, start.Y - d*sin)

if tan > 1.63 && tan < 1.95:         // 60°
    sin = sign(sin)*0.866; cos = sign(cos)*0.5
    end = (start.X + d*cos, start.Y - d*sin)

if |cos / sin| < 1/12:               // 垂直
    end = (start.X, end.Y)
    sin = 1; cos = 0                 // 注意：sin 强制 +1，与拖拽方向无关

return (end, sin, cos)
```

- 各 `if` 是独立判断而非 `else-if`；但区间互斥，实际效果等同。
- 垂直分支把 `sin` 固定为 +1、`cos=0`，法向量 `n=(sin,cos)=(1,0)`，因此四条平行线沿 x 偏移、线段方向由 `end` 的 y 决定（`(start.X, end.Y)`），而不是由 `(cos,-sin)=(0,-1)` 推出。这是上游的一个怪癖，必须保留。
- `tan = |sin/cos|` 在 `cos=0` 时为 `+∞`，不会进入前四个区间；`|cos/sin|` 在 `sin=0` 时为 `+∞`。

`ParallelLines(attrs,start,end)`：
```
(snapped, sin, cos) = SnapParallel(start,end)
if distance(start,snapped) <= 0: return []
n = (sin, cos)                    // 与拖拽方向 (cos,-sin) 垂直
x = 25
for k in [-3,-1,1,3]:
    Stroke(attrs, start + n*(x*k), snapped + n*(x*k))
```
- 输出顺序：-75、-25、+25、+75。
- 每条 2 点。
- 测试：
  - 水平拖 (100,100)→(300,100)：四条 y=100+{-75,-25,25,75}。
  - 30° 拖 (100,100)→(300,-20)：d=320.623，终点 `(100+0.866d, 100-0.5d)`；`n=(0.5,0.866)`，四条偏移。
  - 垂直拖 (100,100)→(102,300)：四条 `x=100+offset`，从 y=100 到 300。
  - 45°：`end=(100,-100)` → `(70.7107,-70.7107)`。
  - 60°：`end=(100,-180)` → `(d*0.5, -d*0.866)`，`d=sqrt(100²+180²)=205.913`。

#### 2.4.7 三个基础构造器

`Stroke(attrs, params Vector2[] pts)`：
```
s = new InkStroke(attrs.Clone())
t = 0
foreach p: s.AddPoint(p.X, p.Y, 0.5f, t); t += 8f
return s
```
- 压感固定 0.5（= 名义全宽）；时间戳 0,8,16,... ms。
- 宽度由 `TaperProfile` 按 `attrs.TaperKind` 写入；App 层传入 `TaperKind.None` 时每点宽度 = `BaseWidth`。

`Dot(attrs, center, pressure)`：
```
s = new InkStroke(attrs.Clone())
width = attrs.BaseWidth * (pressure / 0.5f)
s.RestorePoints([ new InkPoint(center.X, center.Y, pressure, width, 0) ])
```
- 绕过 taper，宽度直接写入。pressure=1 → 2×BaseWidth；0.8 → 1.6×BaseWidth。

`ArrowWithHead(attrs,start,end,w,h)`：
```
dir = end - start
if |dir|² < 1e-4: return Stroke(attrs, start, end)     // 退化：普通 2 点线
theta = atan2(start.Y - end.Y, start.X - end.X)         // 从箭尖指回起点
sint = sin(theta); cost = cos(theta)
barb1 = (end.X + (w*cost - h*sint), end.Y + (w*sint + h*cost))
barb2 = (end.X + (w*cost + h*sint), end.Y - (h*cost - w*sint))
return Stroke(attrs, start, end, barb1, end, barb2)     // 5 点
```
- `Arrow` 用 `w=30,h=10`；`AxisArrow` 用 `w=20,h=7`。
- 测试：`(0,0)→(100,0)` → 点序列 (0,0),(100,0),(70,-10),(100,0),(70,10)。

### 2.5 两步式形状的调用方状态机（行为契约，属 `InkController`，不在本文件内但形状移植必须同步）

`src/InkCanvas.Ink/InkController.cs`：

- 字段：`_shapeStep`（0/1）、`_stepAnchor`、`_stepSecond`、`_stepK`、`_stepPending`、`_shapePreview`。
- `ShapeKind` setter：`ResetShapeState()` + `StickyShape=false`。
- `Tool` 离开 Shape：`ResetShapeState()` + `StickyShape=false`。
- `SelectShape(kind, sticky)`：设置 kind、sticky、`Tool=Shape`。
- `OnPointerDown` 且 `Tool==Shape`：`_shapeDragStart = (_shapeStep==1 && 双曲线) ? _stepAnchor : world`。
- `GenerateShapePreview`：
  - `_shapeStep==1 && Cuboid`：`depth=|_stepAnchor.Y - end.Y|`，`_stepPending + GenerateCuboidDepth(_stepAnchor,_stepSecond,depth)`。
  - `_shapeStep==1 && 双曲线`：`_stepPending + GenerateHyperbolaBranches(_stepAnchor,_stepK,end,withFoci)`。
  - 否则 `Generate(kind,start,end)`。
- `CommitShapeDrag`：
  - Cuboid step0：`_stepPending = Generate(...)`；`_stepAnchor=(minX,minY)`、`_stepSecond=(maxX,maxY)`；`_shapeStep=1`；**不提交**。
  - Cuboid step1：depth=`|_stepAnchor.Y-end.Y|`；`solid=_stepPending + Depth(...)`；一次提交一个撤销单元；`Tool=Pen`。
  - 双曲线 step0：渐近线；若为空则重试；`_stepK=|dy/dx|`；`_stepAnchor=start`；`_shapeStep=1`。
  - 双曲线 step1：`branches=GenerateHyperbolaBranches(...)`；先 `Tool=Pen`；再 `FinishHyperbola`。
  - 其他：`Generate` 后立即提交；`StickyShape` 为 false 时 `Tool=Pen`。
- `FinishHyperbola`：
  - `OptionalOperation.Ask` 且有 `AskRemoveAsymptotes` → 弹窗；无弹窗时**保留渐近线**。
  - `removeAsymptotes = option == OptionalOperation.No`（`Yes=0, No=1, Ask=2`）。即 "Yes"=保留渐近线，"No"=移除。
- `ShapeAttributes()`：`CurrentAttributes.Clone()` 后强制 `TaperKind=None`（形状恒等宽）。
- `CommitStrokes`：`Complete()` → `Document.AddRange` → 一个 `DelegateAction` 撤销单元；`name=="ink"` 时才触发识别。

### 2.6 形状菜单 UI（消费方事实，供两侧 UI 一致性）

`src/InkCanvas.Controls/Controls/ShapePreview.cs` + `ShapeMenuCatalog`：

- 标题：`"图形(第一行支持长按保持选中)"`。
- 5 行，可见单元数 5/5/5/5/4；行序：
  1. Line 直线 / DashedLine 虚线 / DotLine 点线 / Arrow 箭头 / ParallelLine 平行线（该行支持长按）。
  2. CoordinateBoth 坐标系（双向）/ XOneWay（x 单向）/ YOneWay（y 单向）/ FirstQuadrant（第一象限）/ Coordinate3D 空间坐标系。
  3. RectangleFromCenter 中心矩形 / Circle 圆 / DashedCircle 虚线圆 / EllipseFromCenter 中心椭圆 / EllipseWithFoci 椭圆（含焦点）。
  4. Hyperbola 双曲线 / HyperbolaWithFoci 双曲线（含焦点）/ ParabolaUp 抛物线（上开口）/ ParabolaWithFoci 抛物线（含焦点，预览旋转 90°）/ ParabolaRight 抛物线（右开口，预览旋转 90°）。
  5. Rectangle 矩形 / Cylinder 圆柱 / Cone 圆锥 / Cuboid 长方体。
- 行高 `50`；行间距 `{14,15,10,10,10}`；行栈 `Spacing=-8`。
- 弹窗主体宽度：白板 240、屏幕 200（DIP）。
- 每格 `Margin` 见 `ShapeMenuItem` 定义（如 16/12/0/12、0/10 等；`SlotWidth` 26 或 30）。
- 标题带：`Padding=(15,5,0,5)`、圆角 `5,5,0,0`、边框 1；深色标题底色 `ARGB(128,33,86,201)`、浅色 `ARGB(64,88,139,252)`；band 边框深色 `ARGB(153,255,255,255)`、浅色 `ARGB(204,0,0,0)`。
- 标题字号：白板 14、屏幕 10，Bold；pin 图标字号 18/16，按钮 28×24，`Margin` 白板 (16,0,0,0)、屏幕 (22,-1,0,-1)。
- pin 图标码点：`U+E718`（自动隐藏）/ `U+E77A`（固定）；`Segoe Fluent Icons`。
- 长按：`500ms` 定时器；命中后 `100ms` 淡到 `0.3`，并进入 sticky。
- 预览图标是矢量 Path（`ShapeIcons`），画布约 600–870 单位；不属于 Core，但移植 UI 时必须逐 Path 复制（`StrokeDashArray` 在 WinUI 与 ArkUI 都按线宽倍数解释）。

---
## 3. 功能：ShapeRecognizer（墨迹转图形）

- 文件：`src/InkCanvas.Core/Recognition/ShapeRecognizer.cs`（285 行）。
- 公开类型：
  - `enum RecognizedShape { None, Line, Circle, Ellipse, Triangle, Rectangle }`（隐式值 0..5）。
  - `readonly record struct RecognitionResult(Shape, Center, RadiusX, RadiusY, Confidence, Corners)`；静态 `None = (None, 0,0,0,0, [])`。
  - `static class ShapeRecognizer`。
- 平台依赖：**无**（纯几何）。`pure-logic`。
- 消费方：`src/InkCanvas.Ink/InkController.cs` 的 `TryRecognize`（仅最近最多 4 条笔画；`Triangle` 被忽略；替换动作作为一个撤销单元）。
- 测试：`tests/InkCanvas.Core.Tests/DomainTests.cs` 的 `ShapeRecognizerTests`（5 个）。

### 3.1 常量与总流程

- 唯一公开常量：`MinSize = 40f`（px，世界坐标）。
- `Recognize(strokes)`：

```
if strokes null/empty: None
pts = 拼接所有 stroke.Points.Position
arc = 所有 stroke.ArcLength() 之和
if pts.Count < 8 || arc < 40: None
bounds = pts 的 min/max
if bounds.Width < 40 && bounds.Height < 40: None      // 注意是 &&
centroid = 所有点算术平均
if TryLine(pts): 返回 Line
closed = distance(pts[0], pts[^1]) < max(12, arc*0.18)
if !closed: None
if TryCircle(...) && residual < 0.10 && aspect ∈ (0.82,1.22): Circle
if TryEllipse(...) && residual < 0.14: Ellipse
if TryPolygon(...):
    corners==3 -> Triangle (confidence 0.8)
    corners==4 -> Rectangle (confidence 0.8)
None
```

关键点：
- 直线判定在闭合判定**之前**，所以一条不闭合但仍很"直"的涂鸦会返回 Line。`RejectsOpenScribble` 允许 `None or Line`，正是这个原因。
- `Triangle` 有结果但 `InkController` 不替换（`if result.Shape is None or Triangle return`）。
- 所有阈值单位是笔画坐标（世界坐标），不是屏幕像素；HarmonyOS 若用 vp 存墨迹，阈值仍是 40vp 对应值，不能误乘 DPI。

### 3.2 TryLine（协方差主轴）

```
c = centroid
sxx=Σdx²; syy=Σdy²; sxy=Σdxdy
trace = sxx+syy; if trace < 1e-6: false
det = sxx*syy - sxy²
disc = sqrt(max(0, trace²/4 - det))
major = trace/2 + disc; minor = trace/2 - disc
if major < 1e-6: false
spread = sqrt(max(0,minor)/n)
length = 2*sqrt(major/n)
if length < 40: false
if spread/length > 0.035: false
axis = normalize((sxy, major-sxx))
if NaN(axis.X) || |axis|² < 0.5: axis = (1,0)
min/max = 各点在 axis 上投影的最小/最大
corners = [c + axis*min, c + axis*max]
confidence = 1 - spread/length
```

- 注意：`axis` 的 y 分量是 `major - sxx`（不是 `sxy` 的比值），这是 2×2 主轴公式的一个分支；`sxy=0` 且 `major>sxx` 时轴为 (0,1)。
- `corners` 只有 2 个点；`InkController` 用它们画 `ShapeKind.Line`。

### 3.3 TryCircle（Kåsa 代数拟合）

```
（所有坐标先减去 guess/centroid）
sxx=Σx²; sxy=Σxy; syy=Σy²; sxz=Σx·z; syz=Σy·z; sx=Σx; sy=Σy; sz=Σz; z=x²+y²
解 [[sxx,sxy,sx],[sxy,syy,sy],[sx,sy,n]] · [a,b,c] = -[sxz,syz,sz]
cx=-a/2; cy=-b/2; r2=cx²+cy²-c
if r2 <= 1: false
r=sqrt(r2); center = guess + (cx,cy)
err=Σ(distance(p,center)-r)²
residual = sqrt(err/n)/r
radius=r; return radius>1
```

- 3×3 高斯消元 `Solve3`：按列部分主元；`|pivot| < 1e-12` 判奇异；回代；任一分量 NaN 则失败（`Solve3` 返回 false 时 `TryCircle` false）。
- 圆接受条件：`residual < 0.10` 且 `aspect = bounds.W/bounds.H` 满足 `0.82 < aspect < 1.22`；`bounds.Height <= 0.01` 时 aspect 视为 1。
- confidence = `1 - residual`。

### 3.4 TryEllipse

```
rx = max(bounds.Width/2, 1)
ry = max(bounds.Height/2, 1)
residual = sqrt( mean( (sqrt(((x-cx)/rx)² + ((y-cy)/ry)²) - 1)² ) )
return true
```

- 拟合本身一定返回 true，接受与否由调用方的 `residual < 0.14` 决定。
- 半径直接取包围盒半宽/半高，不做最小二乘。

### 3.5 TryPolygon + RDP

- 先 `Simplify(pts, epsilon=6f)`（Ramer–Douglas–Peucker，迭代栈实现）。
  - `pts.Count < 3` 原样返回。
  - 保留首尾；对每个区间找距首尾连线最远的点（`InkStroke.DistanceToSegment`）；`maxDist > 6` 且 `index` 在开区间内 → 保留并拆栈。
- 对简化后折线逐内点计算转角：
  ```
  v1 = normalize(p[i]-p[i-1]); v2 = normalize(p[i+1]-p[i])
  dot = clamp(dot(v1,v2), -1, 1)
  angle = acos(dot) * 180/π
  if angle > 45:
      if found 为空 || distance(found[^1], p[i]) > 20: found.Add(p[i])
  ```
- 只有 `found.Count` 恰好为 3 或 4 才接受；返回这些角点（不额外闭合）。
- 注释说"缝合 last/first 也是角点候选"，但代码**没有**实现该补充，不要自行添加。
- confidence 固定 `0.8`。

---

## 4. 功能：SelectionGeometry（选区命中与拖拽矩阵）

- 文件：`src/InkCanvas.Core/Selection/SelectionGeometry.cs`（99 行）。
- 公开类型：`enum SelectionHandle { None, Move, TopLeft, TopRight, BottomRight, BottomLeft, Rotate }`、`static class SelectionGeometry`。
- 平台依赖：无（`Vector2`/`Matrix3x2`）。`pure-logic`。
- 消费方：
  - `src/InkCanvas.Ink/InkController.cs`：`BeginSelectionInteraction`（`HitTest`）、`UpdateSelectionDrag`（`DragMatrix`）、`_activeHandle`。
  - `src/InkCanvas.Ink/Rendering/InkSceneRenderer.cs:84`：`RotateHandlePosition(bounds)` 画旋转手柄。
- 测试：`tests/InkCanvas.Core.Tests/SelectionGeometryTests.cs`（10 个）。

### 4.1 常量

| 常量 | 值 | 说明 |
|---|---|---|
| `HandleRadius` | `12f` | 手柄命中半径（view px/DIP） |
| `RotateOffset` | `28f` | 旋转手柄在顶边上方的距离 |
| `MinScale`（private） | `0.05f` | 缩放比下限 |
| `MaxScale`（private） | `20f` | 缩放比上限 |

### 4.2 命中测试

```
if bounds.IsEmpty: None
if distance(point, RotateHandlePosition(bounds)) <= 12: Rotate
if distance(point, (Left,Top))       <= 12: TopLeft
if distance(point, (Right,Top))      <= 12: TopRight
if distance(point, (Right,Bottom))   <= 12: BottomRight
if distance(point, (Left,Bottom))    <= 12: BottomLeft
if bounds.Contains(point): Move
else None
```

- 顺序固定：旋转优先，其次四角（TL→TR→BR→BL），最后内部 Move。
- `RotateHandlePosition = (bounds.Center.X, bounds.Top - 28)`。
- 角点命中半径是圆形（欧氏距离），不是方形热区。

### 4.3 拖拽矩阵

```
DragMatrix(handle, bounds, from, to, keepAspect):
  None -> Identity
  Move -> CreateTranslation(to - from)
  Rotate:
      center = bounds.Center
      a = from - center; b = to - center
      if |a|² < 1e-4 || |b|² < 1e-4: Identity
      angle = atan2(b.Y,b.X) - atan2(a.Y,a.X)
      return About(center, CreateRotation(angle))
  default（四角）:
      anchor = OppositeCorner(handle, bounds)
      sx = Ratio(from.X-anchor.X, to.X-anchor.X)
      sy = Ratio(from.Y-anchor.Y, to.Y-anchor.Y)
      if keepAspect: sx = sy = max(sx,sy)
      return About(anchor, CreateScale(sx,sy))

Ratio(from,to):
  if |from| < 1e-3: 1
  else clamp(to/from, 0.05, 20)

About(pivot,m) = CreateTranslation(-pivot) * m * CreateTranslation(pivot)
```

`OppositeCorner`：

| handle | anchor |
|---|---|
| TopLeft | (Right, Bottom) |
| TopRight | (Left, Bottom) |
| BottomRight | (Left, Top) |
| BottomLeft | (Right, Top) |
| 其他 | bounds.Center |

### 4.4 行为测试与语义

- 四角命中、内部 Move、远处 None、旋转手柄在顶边上方。
- `BottomRight` 拖 (200,200)→(300,300)：anchor (100,100)，sx=sy=2 → (100,100) 不动、(200,200)→(300,300)。
- `Move` (150,150)→(170,160) 平移 (20,10)。
- `Rotate` (200,150)→(150,200)：中心 (150,150)，角度 π/2，(200,150)→(150,200)。
- `keepAspect`：sx=(300-100)/(200-100)=2，sy=(250-100)/(200-100)=1.5 → 统一 2；测试只断言两轴增量相等。
- 退化拖拽 `from=(100,100)` 正好在 anchor 上 → `Ratio` 返回 1，不产生 NaN。
- `None` 恒等矩阵。

### 4.5 调用方坐标语义（移植时必须同步）

`InkController.UpdateSelectionDrag`：
- `HitTest` 用 **view** 坐标（`p.X,p.Y`）与 `ViewBoundsOf(SelectionBounds)`。
- `DragMatrix` 在 view 空间算 `viewDelta`，再共轭回 world：
  ```
  Matrix3x2.Invert(_viewAtDragStart, out inverse)
  worldDelta = _viewAtDragStart * viewDelta * inverse
  ```
- 即 `w' = w · V · D · V⁻¹`。ArkTS 若矩阵顺序不同，必须复刻同一共轭公式。
- 拖拽从快照 `_dragSnapshot` 绝对重建，避免累计误差。
- 克隆模式只对 `Move` 生效（`CloneMode && handle==Move`）；新点选会清除 `CloneMode`。

---

## 5. 功能：墨迹回放（InkReplaySession + ReplayClock）

- 文件：`src/InkCanvas.Core/Replay/InkReplaySession.cs`（58 行）、`ReplayClock.cs`（52 行）。
- 公开类型：`sealed class InkReplaySession`、`sealed class ReplayClock`。
- 平台依赖：无（纯时间/列表）。`pure-logic`。
- 消费方：
  - `src/InkCanvas.App/Views/ReplayWindow.cs`：每 `33ms` tick 调 `Advance(delta)` + `VisibleAt`；`ReplayClock(session.Duration)`。
  - `src/InkCanvas.App/Diagnostics/SelfTest.cs:170-190`：单调性/完成性自检。
- 测试：`tests/InkCanvas.Core.Tests/ReplayTests.cs`（`ReplayClockTests` 9 + `InkReplaySessionTests` 4 = 13 个）。

### 5.1 InkReplaySession 精确算法

构造：`InkReplaySession(strokes, msPerPoint = 12f)`

```
_source = strokes.ToList()
acc = 0
for each s:
    acc += max(1, s.Count) * msPerPoint
    _cumulative.Add(acc)
_totalMs = acc
```

- 每个笔画的持续时间只与其**点数**有关，与几何长度/时间戳无关；空笔画按 1 点计。
- 默认 `12ms/点`；一个 10 点笔画 = 120ms。

属性：
- `Duration = TimeSpan.FromMilliseconds(_totalMs)`。
- `StrokeCount = _source.Count`。
- `IsComplete(elapsedMs) => elapsedMs >= _totalMs`（注意是 `>=`，空文档 total=0 时 `elapsed=0` 即完成）。

`VisibleAt(elapsedMs)`：

```
result = []
for i in 0.._source.Count-1:
    start = (i == 0) ? 0 : _cumulative[i-1]
    if elapsedMs < start: break
    end = _cumulative[i]
    src = _source[i]
    if elapsedMs >= end:
        result.Add(src)                       // 完整笔画，原对象引用
        continue
    fraction = (elapsedMs - start) / max(1, end - start)
    take = max(1, (int)(src.Count * fraction))   // C# 向零截断
    partial = new InkStroke(src.Attributes.Clone())
    partial.RestorePoints(src.Points.Take(take))
    result.Add(partial)
```

- 部分笔画**没有** `Complete()`，`IsCompleted=false`；宽度逐点复制，因此外观与原始绘制一致。
- `elapsedMs` 恰等于某笔画 `start` 时 `fraction=0`，`take=1`，至少显示 1 点。
- `elapsedMs < 0`（任何负数）→ 第一条 `start=0` 就 `break`，返回空。
- 返回顺序即绘制顺序；完整笔画返回**原引用**（不是克隆），回放窗口只读不修改。

### 5.2 ReplayClock 精确算法

构造：`_durationMs = max(0, (float)duration.TotalMilliseconds)`。

状态：
- `Speed` 默认 `1f`（可写）。
- `IsPlaying` 默认 false。
- `ElapsedMs` 默认 0。
- `Progress => _durationMs <= 0 ? 1f : ElapsedMs / _durationMs`（不夹取，但 `ElapsedMs` 本身不超范围）。

方法：
```
Play():
    if IsComplete: ElapsedMs = 0
    IsPlaying = true

Pause(): IsPlaying = false

Toggle(): IsPlaying ? Pause() : Play()

Seek(ms): ElapsedMs = clamp(ms, 0, _durationMs)

Advance(realDeltaMs):
    if !IsPlaying: return
    ElapsedMs = min(_durationMs, ElapsedMs + realDeltaMs * max(0, Speed))
    if ElapsedMs >= _durationMs: IsPlaying = false
```

- `duration=0` 的边界：构造后 `IsComplete=true`、`Progress=1`；`Play()` 把它重置到 0 并置 playing，但下一次 `Advance` 立即因 `ElapsedMs>=0` 停回 false。
- 测试钉住：初始暂停 0；暂停时 Advance 无效；累加；Speed 2×；到末尾夹取并暂停；Seek 夹取；Progress=0.25；Toggle；完成后 Play 从头开始。

### 5.3 回放窗口 UI（消费方）

`ReplayWindow`：
- 标题 `"墨迹回放"`。
- 设计尺寸 `900×680` DIP，按 DPI 夹到工作区（`wDip=min(900, work.Width/s)`，`hDip=min(680, work.Height/s)`，其中 `s` 为 DPI 缩放）。
- 计时器间隔 `33ms`；用 `DateTime.UtcNow` 差值作为 `realDeltaMs`。
- 进度条 `Minimum=0, Maximum=1000, Width=320`；`Progress*1000` 回填，`_suppressSeek` 防递归。
- 速度下拉 `Width=96`，选项 `0.5x/1x/2x/4x`，默认索引 1（1x）；映射 0→0.5、2→2、3→4、其余 1。
- 时间标签 `MinWidth=96`，格式 `mm:ss / mm:ss`。
- 工具栏 `Padding=(12,8,12,8)`、顶部边框 1、`Viewbox` 设计宽约 700 DIP（`Stretch=Uniform, DownOnly`）。
- 宿主背景 `ARGB(255,250,250,250)`；工具栏底色 `IcSurfaceBrush`、边框 `IcStrokeBrush`。
- 打开时若有笔画自动 `Play()`。

---

## 6. 功能：PixelCompositing（预乘 BGRA 合成）

- 文件：`src/InkCanvas.Core/Imaging/PixelCompositing.cs`（56 行）。
- 公开类型：`static class PixelCompositing`，唯一方法 `BlendOver`。
- 平台依赖：`Span<byte>` / `ReadOnlySpan<byte>`（ArkTS → `Uint8Array`）；**无**图形 API。`pure-logic`。
- 消费方：`src/InkCanvas.Ink/Rendering/OverlayInkPresenter.cs:427`（把小块活动墨迹区域叠到已提交层，避免每帧全屏 GPU 回读）。
- 测试：`tests/InkCanvas.Core.Tests/PixelCompositingTests.cs`（7 个）。

### 6.1 精确算法

```
BlendOver(dst, dstWidth, dstHeight, dstX, dstY, src, srcWidth, srcHeight):
  if srcWidth<=0 || srcHeight<=0 || dstWidth<=0 || dstHeight<=0: return
  if src.Length < srcWidth*srcHeight*4 || dst.Length < dstWidth*dstHeight*4: return

  srcStartX = max(0, -dstX)
  srcStartY = max(0, -dstY)
  dstOriginX = max(0, dstX)
  dstOriginY = max(0, dstY)
  copyWidth = min(srcWidth - srcStartX, dstWidth - dstOriginX)
  copyHeight = min(srcHeight - srcStartY, dstHeight - dstOriginY)
  if copyWidth<=0 || copyHeight<=0: return

  for y in 0..copyHeight-1:
    srcRow = ((srcStartY+y)*srcWidth + srcStartX)*4
    dstRow = ((dstOriginY+y)*dstWidth + dstOriginX)*4
    for x in 0..copyWidth-1:
      si = srcRow + x*4; di = dstRow + x*4
      a = src[si+3]
      if a == 0: continue
      if a == 255:
          dst[di..di+3] = src[si..si+3]（包括 alpha=255）
          continue
      inv = 255 - a
      dst[di+0] = src[si+0] + dst[di+0]*inv/255    // 整数除法，向零截断
      dst[di+1] = src[si+1] + dst[di+1]*inv/255
      dst[di+2] = src[si+2] + dst[di+2]*inv/255
      dst[di+3] = a        + dst[di+3]*inv/255
```

- 通道顺序 **BGRA**（索引 0=B、1=G、2=R、3=A），预乘 alpha：颜色通道已乘过 alpha。
- 整数运算的截断顺序会影响 1 LSB；黄金向量应固定为整数结果。
- 测试：
  - 不透明源完全替换（逐字节相等）。
  - 全透明源不动 dst。
  - 半透明源 `{100,100,100,128}` over 不透明黑 → B=100、A=255（`255*127/255=127`，`128+127=255`）。
  - 偏移只写目标像素。
  - 源溢出右下边缘裁剪。
  - 负偏移裁剪（只写重叠区）。
  - 空源 no-op。
- HarmonyOS 注意：若用 `PixelMap`/`ImageData` 的 `RGBA_8888`，必须换通道；若用 GPU 合成，则非预乘纹理的混合公式不同（`out = src*srcA + dst*(1-srcA)`），不能直接复用。

---

## 7. 功能：TouchContactClassifier（触摸接触分档 / 掌拒）

- 文件：`src/InkCanvas.Core/Input/TouchContactClassifier.cs`（103 行）。
- 公开类型：
  - `enum TouchIntent { Draw, EraseByStroke, EraseByPoint, GestureOnly, Ignore }`。
  - `readonly record struct TouchDecision(TouchIntent Intent, double EraserDiameter)`。
  - `static class TouchContactClassifier`。
- 平台依赖：本身无 Win32，但输入是**数字化仪接触矩形宽高**；Windows 侧由 `OverlayPointer.ContactWidth/ContactHeight` 提供（来自 `PointerPointProperties.ContactRect`，见 `src/InkCanvas.Platform/...`）。`adapt`。
- 消费方：
  - `src/InkCanvas.Platform/Input/PalmRejector.ClassifyContact`（`MeasureContactWidth` + `Classify`）。
  - `src/InkCanvas.Ink/InkController.cs:224-240`（Intent 分派；`EraseByPoint` 半径 = `EraserDiameter/2`；`EraseByStroke` 固定半径 `2.5f`）。
- 测试：**无直接测试**。`tests/InkCanvas.Core.Tests/PalmRejectorTests.cs`（9 个）只钉住 `PalmRejector` 的简单阈值/笔优先逻辑，不覆盖本类。源码注释明确写「未验证」。

### 7.1 两个公开辅助

```
MeasureContactWidth(width, height, isQuadIr):
    return isQuadIr ? sqrt(width * height) : width

BoundsWidth(advanced, nibMode):
    return nibMode ? advanced.NibModeBoundsWidth : advanced.FingerModeBoundsWidth
```

- 四边红外（QuadIR）用几何平均，因为单看宽度会误导；`width`/`height` 都是 double。

### 7.2 `Classify` 精确分支

参数：`contactWidth`（已过 `MeasureContactWidth`）、`advanced`、`nibMode`、`forceEraser=false`、`isDrawingShape=false`、`forcePointEraser=true`、`pptGestureEligible=false`。

```
if advanced == null: return (Draw, 5)

boundsWidth = nibMode ? NibModeBoundsWidth : FingerModeBoundsWidth
eraserEnabled = (TouchMultiplier != 0) || (!IsSpecialScreen)

if eraserEnabled && contactWidth > boundsWidth:
    if (!isDrawingShape && forceEraser): return (Ignore, 0)

    threshold = nibMode ? NibModeBoundsWidthThresholdValue
                        : FingerModeBoundsWidthThresholdValue

    if contactWidth > boundsWidth * threshold:
        diameter = contactWidth * (nibMode ? NibModeBoundsWidthEraserSize
                                           : FingerModeBoundsWidthEraserSize)
        if IsSpecialScreen: diameter *= TouchMultiplier
        return (EraseByPoint, diameter)

    if pptGestureEligible: return (GestureOnly, 5)
    return (EraseByStroke, 5)

return (Draw, forcePointEraser ? 50 : 5)
```

要点：
- `TouchMultiplier == 0 && IsSpecialScreen` → `eraserEnabled=false`，**完全绕过橡皮候选**（直接落到 Draw）。
- `forceEraser` 只影响 `Ignore` 分支，不阻止后续面积擦/线擦。
- `isDrawingShape` 为 true 时不会 `Ignore`，接触仍可能进入橡皮分支（上游行为）。
- 面积擦直径 = `contactWidth * eraserSize`，特殊屏再乘 `TouchMultiplier`；`EraseByPoint` 的直径在下游除以 2 作为半径。
- `GestureOnly` 和 `EraseByStroke` 的 `EraserDiameter` 恒为 `5`；下游 `EraseByStroke` 实际用固定 `2.5`，所以 5 只是占位。
- `Draw` 的 50/5 只是"名义橡皮直径"，绘制路径不使用。
- 返回的 `double` 精度链：contact→sqrt→×threshold/×size/×multiplier；ArkTS 用 Number 即可，但阈值比较的边界（`>` 严格）必须一致。

### 7.3 默认参数（来自 `AdvancedSettings`）

| 字段 | 默认 | 重置推荐 |
|---|---|---|
| `IsSpecialScreen` | false | **true** |
| `IsQuadIR` | false | false |
| `TouchMultiplier` | 0.25 | **0.3** |
| `NibModeBoundsWidth` | 10 | **5** |
| `FingerModeBoundsWidth` | 30 | **20** |
| `NibModeBoundsWidthThresholdValue` | 2.5 | 2.5 |
| `FingerModeBoundsWidthThresholdValue` | 2.5 | 2.5 |
| `NibModeBoundsWidthEraserSize` | 0.8 | 0.8 |
| `FingerModeBoundsWidthEraserSize` | 0.8 | 0.8 |

### 7.4 周边 `PalmRejector`（平台适配层，port 时必须一并考虑）

`src/InkCanvas.Platform/Input/PalmRejector.cs`：
- `PalmContactThreshold = 30f`；`IsPalmContact` 用 `max(ContactWidth, ContactHeight) >= 30`（与 `MeasureContactWidth` 的 QuadIR 几何平均是两套逻辑）。
- `PenHasPriority = true`：有笔在范围时触摸不画。
- `ForceEraser = false`、`ForcePointEraser = true`、`NibMode = false`、`Advanced = null`（null 时用 `Classify` 的兜底 `new AdvancedSettings()`）。
- `ShouldDraw`：Pen 恒 true；palm false；笔活跃且 PenHasPriority false；否则 true。
- `ShouldErase`：只有被标记为 palm 的 Touch。
- `AppServices.ApplySettings` 设置：
  - `PalmContactThreshold = max(8, FingerModeBoundsWidth)`
  - `Palm.Advanced = Settings.Advanced`
  - `Palm.NibMode = Settings.Startup.IsEnableNibMode`
  - 未设置 `ForceEraser`（保持 false）。

> HarmonyOS 映射：ArkUI `TouchEvent`/`PointerEvent` 提供的是触点 `size`/`pressure`/`tiltX` 等；`size` 的物理含义（半径 vs 直径、px vs vp）与 Windows `ContactRect` 不同，必须先做实测，再决定 `MeasureContactWidth` 的输入换算。红外/电容屏没有 `ContactRect` 时该分类器退化为简单阈值。

---
## 8. 功能：ThemeResolver（明暗主题判定）

- 文件：`src/InkCanvas.Core/Theming/ThemeResolver.cs`（34 行）。
- 公开类型：`enum AppTheme { Light, Dark }`、`static class ThemeResolver`。
- 平台依赖：判定逻辑无；真正切主题在 WinUI 用 `FrameworkElement.RequestedTheme`（`WindowChrome.ApplyTheme`，`theme` 1=Light、2=Dark、其他=Default），在 HarmonyOS 要用资源/`colorMode`。`adapt`。
- 消费方：
  - `src/InkCanvas.App/Services/AppServices.cs:23`：`GetBoardTheme(Settings.Canvas.UsingWhiteboard)` 决定画板白/黑底。
  - `src/InkCanvas.App/Services/ShellController.cs:229-231`：`EffectiveTheme`（有画板时 `UsingWhiteboard ? 1 : 2`，否则 `Appearance.Theme`）。
  - `src/InkCanvas.App/Services/WindowChrome.cs:486-495`：`RequestedTheme` 映射。
  - `src/InkCanvas.Controls/Controls/PenPalette.cs` 通过 `LightColorScheme` 选浅色色板。
- 测试：**无**。源码注释标注「未验证」。

### 8.1 精确算法

```
GetMainWindowTheme(currentMode, usingWhiteboard, requestedThemeIsLight):
    if currentMode != 0:
        return usingWhiteboard ? Light : Dark
    return requestedThemeIsLight ? Light : Dark

GetBoardTheme(usingWhiteboard):
    return usingWhiteboard ? Light : Dark
```

- `currentMode`：0 = 屏幕画笔（注解），非 0 = 画板/放映。
- 判定优先级：画板/放映模式下**白板开关压过**应用主题；屏幕画笔模式才跟随应用主题。
- `requestedThemeIsLight` 是调用方解析后的布尔（跟随系统时由 `Application.Current.RequestedTheme` 决定）。
- 注意：`usingWhiteboard=false` 时画板是 **Dark**（黑底），不是"跟随"。

### 8.2 主题枚举与菜单的映射（UI 契约）

- `Appearance.Theme` 存储值：`0 = 跟随系统`、`1 = 浅色`、`2 = 深色`（见 `AppSettings` 注释与 `WindowChrome.ApplyTheme`）。
- `OriginalMenuCatalog.ThemeOptions` 的**显示顺序是 浅色 / 深色 / 跟随系统**，所以 `SettingsWindow.cs:482-484` 用 `themeMap = {1,2,0}` 做索引映射；不要拿 ComboBox 索引直接当存储值。
- 浮动条/其它窗口的默认主题（`FloatingBar.cs:845`、`SettingsWindow.cs:128`）在 `Theme==0` 时根据系统 `RequestedTheme` 解析成 1 或 2。

---

## 9. 功能：AppSettings（Settings.json 模型与重置）

- 文件：`src/InkCanvas.Core/Settings/AppSettings.cs`（290 行）。
- 公开类型：`AppSettings`、`OptionalOperation`、`CanvasSettings`、`MatrixTransformCenterPointOptions`、`GestureSettings`、`StartupSettings`、`AppearanceSettings`、`PowerPointSettings`、`AutomationSettings`、`AdvancedSettings`、`InkToShapeSettings`、`RandSettings`。
- 平台依赖：模型本身无；文件读写与路径见 `SettingsStore`（`src/InkCanvas.Features/Persistence/Stores.cs`）。模型 `pure-logic`，存储 `adapt`。
- 消费方：`SettingsStore`、`AppServices.ApplySettings`、`SettingsWindow`、`AutoSaveService`、`PalmRejector`、`UpdatePolicy`、`ShellController` 等。
- 测试：`tests/InkCanvas.Core.Tests/DomainTests.cs` 的 `SettingsTests`（2 个）：JSON 往返；读取 legacy WPF 片段（`canvas.inkWidth/inkStyle/eraserType`、`appearance.theme/floatingBarBottomMargin`、`automation.autoDelSavedFiles/autoDelSavedFilesDaysThreshold`、`randSettings.peopleCount`，未出现的段保持默认，`PowerPoint.PowerPointSupport` 为 true）。

### 9.1 序列化契约（决定 Settings.json 能否互读）

- 序列化器：`System.Text.Json`。
- 读取选项（`SettingsStore.Options`）：`WriteIndented = true`、`PropertyNameCaseInsensitive = true`。
- **枚举以数字序列化**（未加 `JsonStringEnumConverter`）：`hyperbolaAsymptoteOption`、`matrixTransformCenterPoint` 都是整数。
- 所有需要持久化的属性都显式标了 `[JsonPropertyName]`；`[JsonIgnore]` 的计算属性不落盘。
- 未知键被忽略；缺失键用属性初始值（即下表默认值）。
- 写入方式：先写 `Settings.json.tmp`，再 `File.Move(overwrite:true)`；读写异常一律吞掉（损坏文件 → 全新默认设置）。
- 文件位置：`Environment.SpecialFolder.ApplicationData` + `/InkCanvas/Settings.json`（Windows 即 `%APPDATA%/InkCanvas/Settings.json`）。HarmonyOS 应改为应用沙箱 `filesDir`/`preferences`，但**文件名与键名必须保持不变**。
- `Clone()` 用「序列化→反序列化」实现，语义上等于深拷贝。
- `AppSettings` 顶层键：`advanced`、`appearance`、`automation`、`behavior`（PPT）、`canvas`、`gesture`、`inkToShape`、`startup`、`randSettings`。

### 9.2 完整键表（键名 / 类型 / 默认值）

> 类型为 C# 声明类型；double/int/byte 在 JSON 中都是数字。默认值即"键缺失时"的结果。`ResetToRecommendation` 的差异单列在 9.3。

#### canvas（`CanvasSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `inkWidth` | `InkWidth` | double | 2.5 |
| `inkAlpha` | `InkAlpha` | int | 140 |
| `isCompressPicturesUploaded` | `IsCompressPicturesUploaded` | bool | false |
| `isShowCursor` | `IsShowCursor` | bool | false |
| `inkStyle` | `InkStyle` | int | 0（0 笔尖渐细 / 1 速度 / 2 关闭） |
| `eraserSize` | `EraserSize` | int | 2（索引 0..4） |
| `eraserType` | `EraserType` | int | 0（0 图标切换 / 1 面积擦 / 2 线条擦） |
| `hideStrokeWhenSelecting` | `HideStrokeWhenSelecting` | bool | true |
| `usingWhiteboard` | `UsingWhiteboard` | bool | false |
| `hyperbolaAsymptoteOption` | `HyperbolaAsymptoteOption` | enum→数字 | 2（`Ask`；`Yes=0, No=1, Ask=2`） |

#### gesture（`GestureSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isEnableMultiTouchMode` | `IsEnableMultiTouchMode` | bool | true |
| `isEnableTwoFingerZoom` | `IsEnableTwoFingerZoom` | bool | true |
| `isEnableTwoFingerTranslate` | `IsEnableTwoFingerTranslate` | bool | true |
| `AutoSwitchTwoFingerGesture` | `AutoSwitchTwoFingerGesture` | bool | true |
| `isEnableTwoFingerRotation` | `IsEnableTwoFingerRotation` | bool | false |
| `isEnableTwoFingerRotationOnSelection` | `IsEnableTwoFingerRotationOnSelection` | bool | false |
| `matrixTransformCenterPoint` | `MatrixTransformCenterPoint` | enum→数字 | 0（`CanvasCenterPoint`；`SelectedElementsCenterPoint=1`、`GestureOperationCenterPoint=2`） |

计算属性（不落盘）：

- `IsEnableTwoFingerGesture = Zoom || Translate || Rotation`。
- `IsEnableTwoFingerGestureTranslateOrRotation = Translate || Rotation`。

> `AutoSwitchTwoFingerGesture` 是大小写例外，必须保留大写 A。

#### startup（`StartupSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isAutoUpdate` | `IsAutoUpdate` | bool | true |
| `isAutoUpdateWithSilence` | `IsAutoUpdateWithSilence` | bool | false |
| `isAutoUpdateWithSilenceStartTime` | `AutoUpdateWithSilenceStartTime` | string | `"00:00"` |
| `isAutoUpdateWithSilenceEndTime` | `AutoUpdateWithSilenceEndTime` | string | `"00:00"` |
| `isEnableNibMode` | `IsEnableNibMode` | bool | false |
| `isFoldAtStartup` | `IsFoldAtStartup` | bool | false |

#### appearance（`AppearanceSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isEnableDisPlayFloatBarText` | `IsEnableDisPlayFloatBarText` | bool | false |
| `isEnableDisPlayNibModeToggler` | `IsEnableDisPlayNibModeToggler` | bool | true |
| `isColorfulViewboxFloatingBar` | `IsColorfulViewboxFloatingBar` | bool | false |
| `enableViewboxFloatingBarScaleTransform` | `FloatingBarScale` | double | 100.0 |
| `enableViewboxBlackBoardScaleTransform` | `BlackboardScale` | double | 100.0 |
| `floatingBarBottomMargin` | `FloatingBarBottomMargin` | double | 100.0 |
| `isTransparentButtonBackground` | `IsTransparentButtonBackground` | bool | true |
| `isShowExitButton` | `IsShowExitButton` | bool | true |
| `isShowEraserButton` | `IsShowEraserButton` | bool | true |
| `isShowHideControlButton` | `IsShowHideControlButton` | bool | false |
| `isShowLRSwitchButton` | `IsShowLRSwitchButton` | bool | false |
| `isShowModeFingerToggleSwitch` | `IsShowModeFingerToggleSwitch` | bool | true |
| `theme` | `Theme` | int | 0（0 跟随系统 / 1 浅 / 2 深） |

> 注意两个 `enableViewbox...` 键是 WPF 历史名，属性名却是 `FloatingBarScale` / `BlackboardScale`，不能"改名改正"。

#### behavior（`PowerPointSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isShowPPTNavigationBottom` | `IsShowPPTNavigationBottom` | bool | true |
| `isShowPPTNavigationSides` | `IsShowPPTNavigationSides` | bool | true |
| `isShowBottomPPTNavigationPanel` | `IsShowBottomPPTNavigationPanel` | bool | true |
| `isShowSidePPTNavigationPanel` | `IsShowSidePPTNavigationPanel` | bool | true |
| `powerPointSupport` | `PowerPointSupport` | bool | true |
| `isShowCanvasAtNewSlideShow` | `IsShowCanvasAtNewSlideShow` | bool | true |
| `isNoClearStrokeOnSelectWhenInPowerPoint` | `IsNoClearStrokeOnSelectWhenInPowerPoint` | bool | true |
| `isShowStrokeOnSelectInPowerPoint` | `IsShowStrokeOnSelectInPowerPoint` | bool | false |
| `isAutoSaveStrokesInPowerPoint` | `IsAutoSaveStrokesInPowerPoint` | bool | true |
| `isAutoSaveScreenShotInPowerPoint` | `IsAutoSaveScreenShotInPowerPoint` | bool | false |
| `isNotifyPreviousPage` | `IsNotifyPreviousPage` | bool | false |
| `isNotifyHiddenPage` | `IsNotifyHiddenPage` | bool | true |
| `isNotifyAutoPlayPresentation` | `IsNotifyAutoPlayPresentation` | bool | true |
| `isEnableTwoFingerGestureInPresentationMode` | `IsEnableTwoFingerGestureInPresentationMode` | bool | false |
| `isEnableFingerGestureSlideShowControl` | `IsEnableFingerGestureSlideShowControl` | bool | true |
| `isSupportWPS` | `IsSupportWPS` | bool | true |

#### automation（`AutomationSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isAutoFoldInEasiNote` | `IsAutoFoldInEasiNote` | bool | false |
| `isAutoFoldInEasiNoteIgnoreDesktopAnno` | `IsAutoFoldInEasiNoteIgnoreDesktopAnno` | bool | false |
| `isAutoFoldInEasiCamera` | `IsAutoFoldInEasiCamera` | bool | false |
| `isAutoFoldInEasiNote3C` | `IsAutoFoldInEasiNote3C` | bool | false |
| `isAutoFoldInSeewoPincoTeacher` | `IsAutoFoldInSeewoPincoTeacher` | bool | false |
| `isAutoFoldInHiteTouchPro` | `IsAutoFoldInHiteTouchPro` | bool | false |
| `isAutoFoldInHiteCamera` | `IsAutoFoldInHiteCamera` | bool | false |
| `isAutoFoldInWxBoardMain` | `IsAutoFoldInWxBoardMain` | bool | false |
| `isAutoFoldInOldZyBoard` | `IsAutoFoldInOldZyBoard` | bool | false |
| `isAutoFoldInMSWhiteboard` | `IsAutoFoldInMSWhiteboard` | bool | false |
| `isAutoFoldInPPTSlideShow` | `IsAutoFoldInPPTSlideShow` | bool | false |
| `isAutoKillPptService` | `IsAutoKillPptService` | bool | false |
| `isAutoKillEasiNote` | `IsAutoKillEasiNote` | bool | false |
| `isSaveScreenshotsInDateFolders` | `IsSaveScreenshotsInDateFolders` | bool | false |
| `isAutoSaveStrokesAtScreenshot` | `IsAutoSaveStrokesAtScreenshot` | bool | false |
| `isAutoSaveStrokesAtClear` | `IsAutoSaveStrokesAtClear` | bool | false |
| `isAutoClearWhenExitingWritingMode` | `IsAutoClearWhenExitingWritingMode` | bool | false |
| `minimumAutomationStrokeNumber` | `MinimumAutomationStrokeNumber` | int | 0 |
| `autoSavedStrokesLocation` | `AutoSavedStrokesLocation` | string | 原值为盘符 `D:` + 反斜杠 + `Ink Canvas`（即 D 盘根下的 "Ink Canvas" 文件夹） |
| `autoDelSavedFiles` | `AutoDelSavedFiles` | bool | false |
| `autoDelSavedFilesDaysThreshold` | `AutoDelSavedFilesDaysThreshold` | int | 15 |

计算属性（不落盘）：`IsEnableAutoFold = EasiNote || EasiCamera || EasiNote3C || SeewoPincoTeacher || HiteTouchPro || HiteCamera || WxBoardMain || OldZyBoard || PPTSlideShow || MSWhiteboard`。

#### advanced（`AdvancedSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isSpecialScreen` | `IsSpecialScreen` | bool | false |
| `isQuadIR` | `IsQuadIR` | bool | false |
| `touchMultiplier` | `TouchMultiplier` | double | 0.25 |
| `nibModeBoundsWidth` | `NibModeBoundsWidth` | int | 10 |
| `fingerModeBoundsWidth` | `FingerModeBoundsWidth` | int | 30 |
| `nibModeBoundsWidthThresholdValue` | `NibModeBoundsWidthThresholdValue` | double | 2.5 |
| `fingerModeBoundsWidthThresholdValue` | `FingerModeBoundsWidthThresholdValue` | double | 2.5 |
| `nibModeBoundsWidthEraserSize` | `NibModeBoundsWidthEraserSize` | double | 0.8 |
| `fingerModeBoundsWidthEraserSize` | `FingerModeBoundsWidthEraserSize` | double | 0.8 |
| `isEnableEdgeGestureUtil` | `IsEnableEdgeGestureUtil` | bool | false |
| `isLogEnabled` | `IsLogEnabled` | bool | true |
| `isSecondConfimeWhenShutdownApp` | `IsSecondConfimeWhenShutdownApp` | bool | false |

> `isSecondConfimeWhenShutdownApp` 的拼写错误（Confime）必须原样保留。

#### inkToShape（`InkToShapeSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `isInkToShapeEnabled` | `IsInkToShapeEnabled` | bool | true |

#### randSettings（`RandSettings`）

| JSON 键 | 属性 | 类型 | 默认 |
|---|---|---|---|
| `peopleCount` | `PeopleCount` | int | 60 |
| `isNotRepeatName` | `IsNotRepeatName` | bool | false |

### 9.3 `ResetToRecommendation()`（"重置设置"）

1:1 上游 `SetSettingsToRecommendation()`（MW_Settings.cs）。**原地修改**，不替换实例（服务持有的 `Automation` / `Advanced` 引用保持有效）；调用方随后 `ApplySettings()` + `SaveSettings()`。上游在重置时**保留**自动删除配置（`AutoDelSavedFiles` / `AutoDelSavedFilesDaysThreshold`），本实现同样先保存再写回。

与默认值不同的项（其余恢复默认）：

| 段.键 | 重置值 |
|---|---|
| `advanced.isSpecialScreen` | true |
| `advanced.touchMultiplier` | 0.3 |
| `advanced.nibModeBoundsWidth` | 5 |
| `advanced.fingerModeBoundsWidth` | 20 |
| `appearance.isEnableDisPlayNibModeToggler` | false |
| `automation.isAutoFoldInEasiNote` | true |
| `automation.isAutoFoldInEasiNoteIgnoreDesktopAnno` | true |
| `automation.isAutoFoldInEasiCamera` | true |
| `automation.isAutoSaveStrokesAtScreenshot` | true |
| `automation.isAutoSaveStrokesAtClear` | true |
| `powerPoint.isShowPPTNavigationBottom` | false |
| `powerPoint.isAutoSaveScreenShotInPowerPoint` | true |
| `powerPoint.isNotifyHiddenPage` | false |
| `powerPoint.isEnableFingerGestureSlideShowControl` | false |
| `canvas.inkAlpha` | 80 |
| `canvas.eraserSize` | 1 |
| `canvas.hideStrokeWhenSelecting` | false |
| `canvas.hyperbolaAsymptoteOption` | `Yes`(0)（= 保留渐近线） |
| `gesture.isEnableTwoFingerZoom` | false |
| `gesture.isEnableTwoFingerRotationOnSelection` | true |
| `startup.isAutoUpdateWithSilence` | true |
| `startup.isAutoUpdateWithSilenceStartTime` | `"18:20"` |
| `startup.isAutoUpdateWithSilenceEndTime` | `"07:40"` |

其余显式赋值的项与默认相同（如 `isQuadIR=false`、`theme=0`、`inkWidth=2.5`、`powerPointSupport=true`、`isSupportWPS=true`、`peopleCount=60`、`usingWhiteboard=false`、`matrixTransformCenterPoint=CanvasCenterPoint`、`isEnableMultiTouchMode=true`）。`Appearance.Theme` 重置为 0（跟随系统）。

重置按钮还会（UI 层）强制开启开机自启：`AutoStartService.SetEnabled(true, ...)`（`SettingsWindow.cs:385-393`）。

### 9.4 `ApplySettings` 的运行时映射（消费方关键常量）

`src/InkCanvas.App/Services/AppServices.cs:41-80`：

- `InkStyle`：0→`TaperKind.Tail`，1→`Velocity`，2→`None`。
- `BaseWidth = clamp(InkWidth, 0.5, 60)`。
- `HighlighterAlpha = (byte)clamp(InkAlpha, 20, 240)`。
- 橡皮半径：`45f * [0.5, 0.8, 1.0, 1.25, 1.8][clamp(EraserSize, 0, 4)]` → `[22.5, 36, 45, 56.25, 81]`。
- `RecognizeShapes = InkToShape.IsInkToShapeEnabled`。
- `HyperbolaAsymptoteOption` 下传控制器。
- `MultiTouchEnabled = Gesture.IsEnableMultiTouchMode`。
- 手势三开关 → `ConfigureGestures(Zoom, Translate, Rotation)`。
- `Palm.PalmContactThreshold = max(8, Advanced.FingerModeBoundsWidth)`。
- `GesturesTargetSelection = Gesture.IsEnableTwoFingerRotationOnSelection`。
- `Palm.Advanced = Advanced`、`Palm.NibMode = Startup.IsEnableNibMode`。
- `TransformCenterOption = Gesture.MatrixTransformCenterPoint`。
- `Ppt.AllowWps = PowerPoint.IsSupportWPS`；`Lottery.PeopleCount/NoRepeat = Rand.*`。
- `FileLog.Enabled = Advanced.IsLogEnabled`。

---

## 10. 功能：OriginalMenuCatalog（菜单文案与色板）

- 文件：`src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs`（71 行）。
- 公开类型：`static class OriginalMenuCatalog` + 嵌套 `readonly record struct Swatch(string Name, InkColor DarkTheme, InkColor LightTheme)`。
- 平台依赖：无。`pure-logic`（静态数据）。
- 消费方：`PenPalette`（色板/笔宽/透明度）、`SettingsWindow`（下拉项）、`BoardWindow`（橡皮大小）、`FloatingBar`（形状/工具标签）。
- 测试：无直接测试（颜色/文案由 UI 使用；`SettingsTests` 不覆盖）。

### 10.1 色板（名称 / 深色主题 RGB / 浅色主题 RGB）

`PenColors`（9 个，顺序固定）：

| 名称 | Dark | Light |
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

`HighlighterColors`（6 个，Dark/Light 同值）：荧光笔红色 (220,38,38)、橙色 (234,88,12)、黄色 (234,179,8)、青色 (13,148,136)、蓝色 (37,99,235)、紫色 (147,51,234)。全部 alpha=255。

### 10.2 文案列表（索引即存储值）

- `PenStyles`：`["点集笔锋", "速率笔锋", "关闭笔锋"]` → `InkStyle` 0/1/2。
- `EraserSizes`：`["小", "较小", "中（默认）", "较大", "大"]` → `EraserSize` 0..4。
- `EraserModes`：`["图标切换", "面积擦", "线条擦"]` → `EraserType` 0..2。
- `AutoDeleteDays`：`[1, 3, 5, 7, 15, 30, 60, 100, 365]`。
- `ThemeOptions`：`["浅色主题", "深色主题", "跟随系统"]` → 显示顺序≠存储值；映射 `{1,2,0}`。

### 10.3 笔宽/透明度与存储换算

| 常量 | 值 |
|---|---|
| `PenWidthMinimum` | 1 |
| `PenWidthMaximum` | 30 |
| `PenWidthStep` | 1 |
| `HighlighterAlphaMinimum` | 20 |
| `HighlighterAlphaMaximum` | 240 |
| `HighlighterAlphaStep` | 20 |

```
ToStoredPenWidth(menuWidth) = menuWidth / 2      // 菜单 1..30 → 存储 0.5..15
ToMenuPenWidth(storedWidth) = storedWidth * 2
```

- 菜单滑块用「菜单宽度」；`Canvas.InkWidth` 存「一半」。
- `PenPalette` 读取时 `ToMenuPenWidth(BaseWidth)` 再夹到 [1,30]。
- 高亮透明度只对荧光笔生效（`StrokeAttributes.EffectiveColor`）。

---

## 11. 功能：版本与更新（AppVersion / ReleaseFeed / UpdatePolicy）

### 11.1 AppVersion

- 文件：`src/InkCanvas.Core/Versioning/AppVersion.cs`（50 行）。
- 公开类型：`readonly record struct AppVersion(int Major, int Minor, int Patch, int Build) : IComparable<AppVersion>`。
- 平台依赖：无。`pure-logic`。
- 消费方：`ReleaseInfo`、`UpdateService.CurrentVersion`、`UpdateCoordinator`、`ShellController`。
- 测试：`tests/InkCanvas.Core.Tests/UpdateTests.cs` 的 `AppVersionTests`（4 个方法/多组 InlineData）。

`TryParse` 精确规则：

```
if null/whitespace: false
span = text.Trim()
if span[0] == 'v' || 'V': span = span[1..]
parts = span.Split('.')
if parts.Length < 2 || > 4: false
numbers[4] = {0,0,0,0}
for i in 0..parts.Length-1:
    if !int.TryParse(parts[i], out n) || n < 0: false
    numbers[i] = n
version = (numbers[0..3])
```

- 接受：`3.0.0`、`v3.1.2`、`V26.10.7`、`1.2.3.4`、`  v2.5  `（两段 → patch=build=0）。
- 拒绝：空串、`latest`、`release`、null、段数 1 或 5、负号、非数字。
- 注意 `int.TryParse` 默认允许前后空白与正负号；负号被 `n < 0` 拒绝。极大数溢出返回 false。
- 比较：Major→Minor→Patch→Build 逐级 `CompareTo`。
- `ToString()`：`Build == 0` 时 `"M.m.p"`，否则 `"M.m.p.b"`。
- 运算符 `< > <= >=` 全部基于 `CompareTo`。

### 11.2 ReleaseFeed

- 文件：`src/InkCanvas.Core/Update/ReleaseFeed.cs`（106 行）。
- 公开类型：`sealed record ReleaseInfo(Tag, Version, Name, Notes, PageUrl, InstallerUrl, InstallerSize)`；`static class ReleaseFeed`。
- 平台依赖：`System.Text.Json`；无网络代码（HTTP 在 Features 层）。`adapt`（解析逻辑可复用，`.exe` 资产规则是 Windows 专属）。
- 消费方：`src/InkCanvas.Features/Update/UpdateService.cs`（GitHub API）、`UpdateCoordinator`。
- 测试：`UpdateTests.cs` 的 `ReleaseFeedTests`（6 个方法/多组数据）。

解析契约：

```
ParseLatest(json):
    root = 解析 JSON；非 Object → null
    return FromElement(root)

ParseNewestStable(json):
    root 必须是 Array，否则 null
    best = null
    for item in array:
        if item["draft"] 为 true 或 item["prerelease"] 为 true: continue
        info = FromElement(item); if null: continue
        if best == null || info.Version > best.Version: best = info
    return best

FromElement(e):
    tag = Str(e, "tag_name")
    if !AppVersion.TryParse(tag): return null
    (url,size) = PickInstaller(e)
    if url == null: return null
    return ReleaseInfo(tag, version, Str("name"), Str("body"), Str("html_url"), url, size)
```

JSON 字段名（GitHub）：

| 字段 | 用途 |
|---|---|
| `tag_name` | 版本来源（必须可解析） |
| `name` | `ReleaseInfo.Name` |
| `body` | `ReleaseInfo.Notes` |
| `html_url` | `ReleaseInfo.PageUrl` |
| `draft` / `prerelease` | 列表过滤（只认 `true`） |
| `assets[].name` | 必须 `EndsWith(".exe", OrdinalIgnoreCase)` |
| `assets[].browser_download_url` | 下载地址 |
| `assets[].size` | 必须是整数（`TryGetInt64`），否则 0 |

`PickInstaller`：

```
fallbackUrl = null; fallbackSize = 0
for a in assets:
    name = Str(a,"name")
    if empty || !name endsWith ".exe" (忽略大小写): continue
    url = Str(a,"browser_download_url"); if empty: continue
    size = a["size"] 可读 int64 ? 值 : 0
    if name 包含 "setup"（忽略大小写）: return (url,size)     // 优先 setup
    if fallbackUrl == null: fallbackUrl = url
    if fallbackSize == 0: fallbackSize = size
return (fallbackUrl, fallbackSize)
```

- 注意：非 setup 的 exe 只取**第一个** url；size 只在首次为 0 时补。
- 全部失败 → null。
- `TryRead`：空白 → null；`JsonException` → null；`JsonDocument.Parse` 根元素克隆后返回。
- `Str`：仅当值为 `String` 时返回，否则空串；`Bool`：仅当值为 `True` 时 true。
- `ReleaseInfo.IsNewerThan(current) => Version > current`。
- 测试：`v3.2.0`、优先 `...Setup.exe`、无 exe → null、跳过 draft/prerelease 取最高稳定、非法 JSON/`{}` → null、`IsNewerThan` 真值表。

消费方网络参数（`UpdateService`）：
- `Repository = "QiXinia/Ink-Canvas-HEHS"`。
- URL `https://api.github.com/repos/{repo}/releases?per_page=20`。
- `User-Agent: InkCanvas-Updater`；`Accept: application/vnd.github+json`；超时 30s。
- 下载目录 `Path.GetTempPath()/InkCanvas/update`；缓冲 `81920` 字节；若 `InstallerSize > 0` 且实际字节不等 → 删除并失败；`Process.Start(UseShellExecute=true)` 启动安装器。
- `CurrentVersion` 来自 `Assembly.GetEntryAssembly().GetName().Version`，负的 Build/Revision 归 0。

### 11.3 UpdatePolicy

- 文件：`src/InkCanvas.Core/Update/UpdatePolicy.cs`（40 行）。
- 公开类型：`static class UpdatePolicy`，唯一方法 `MayInstallSilently(StartupSettings, DateTime)`。
- 平台依赖：`DateTime` 本地时间。`pure-logic`（ArkTS 用本地时间的 hour/minute 即可）。
- 消费方：`src/InkCanvas.App/Services/UpdateCoordinator.cs:54`（决定是否弹确认框）。
- 测试：`UpdatePolicyTests.cs`（7 个方法）。

精确算法：

```
if settings == null: false
if !settings.IsAutoUpdate || !settings.IsAutoUpdateWithSilence: false
if !TryMinutes(startTime, out start): false
if !TryMinutes(endTime, out end): false
if start == end: true
minutes = now.Hour*60 + now.Minute
if start < end: return minutes >= start && minutes < end      // 同日窗口，起点含、终点不含
else:           return minutes >= start || minutes < end      // 跨午夜
```

`TryMinutes(text)`：

```
if null/whitespace: false
parts = text.Trim().Split(':')
if parts.Length != 2: false
if !int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m): false
if h < 0 || h > 23 || m < 0 || m > 59: false
minutes = h*60 + m
```

- `"00:00"–"00:00"` → `start==end` → 恒 true（默认配置即"无限制"，但默认 `IsAutoUpdateWithSilence=false`，所以实际不会静默）。
- 非补零也接受（`"9:5"` 合法）。
- 任何解析失败 → false（"询问用户"）。
- 测试钉住：静默关/自动更新关 → false；同日窗口 01:00–05:00（3 点 true，6/0 点 false）；跨午夜 22:00–06:00（23/5 true，12 false）；00:00–00:00 true；02:00–04:00 起点含终点不含；乱码 false。

---

## 12. 功能：FileLog（日志）

- 文件：`src/InkCanvas.Core/Diagnostics/FileLog.cs`（60 行）。
- 公开类型：`enum LogType { Info, Event, Trace, Error }`、`static class FileLog`。
- 平台依赖：`File.AppendAllText` / `Directory.CreateDirectory` / `Path.Combine` / `Environment.SpecialFolder.ApplicationData` / `DateTime.Now` / `System.Text.Json`。`adapt`。
- 消费方：`App.xaml.cs`（未处理异常）、`ShellController`（启动/保存/截图/打开）、`WindowChrome`、`OverlayInkPresenter`、`AppServices.ApplySettings`（`Enabled`）。
- 测试：**无**。源码标注「未验证」。

### 12.1 契约

- 静态属性：
  - `LogFile = "Log.txt"`（可写）。
  - `RootPath = %APPDATA%/InkCanvas`（可写）。
  - `Enabled = true`（由 `Advanced.IsLogEnabled` 驱动）。
- `NewLog(string)` = `WriteLogToFile(message, Info)`。
- `NewLog(Exception)` 是**空方法**（上游如此），不要顺手实现。
- `WriteLogToFile(message, type = Info)`：
  ```
  if !Enabled: return
  try:
      if !Directory.Exists(RootPath): Directory.CreateDirectory(RootPath)
      line = "{DateTime.Now:O} [{type}] {message}"     // ISO-8601 round-trip，含本地时区偏移
      File.AppendAllText(RootPath/LogFile, line + Environment.NewLine)
  catch: 忽略
  ```
- `WriteObjectLogToFile(value, type)`：序列化失败也忽略；成功后调用 `WriteLogToFile`。
- 行格式：`2026-11-18T01:23:45.1234567+08:00 [Info] 消息`（C# `:O` 的 7 位小数 + 偏移；ArkTS 需自行拼到同等精度或以兼容格式标注差异）。默认编码 UTF-8（无 BOM，.NET Core 行为）。
- 与原实现的差异：原版写到 exe 旁（`App.RootPath`），本移植改到用户 AppData（只读安装目录也能写）。HarmonyOS 应改为应用沙箱日志目录，保留文件名 `Log.txt` 与行格式以便日志分析工具兼容。

---
## 13. 钉住行为的测试矩阵

> 说明：以下为**静态阅读**得到的测试清单；本次盘点没有运行 `dotnet test`（只读任务，且 Windows 构建需先结束 InkCanvas.App 进程），因此不声称测试通过。

| 测试文件 | 用例数（方法） | 覆盖的功能与关键断言 |
|---|---|---|
| `tests/InkCanvas.Core.Tests/ShapeDrawingSemanticsTests.cs` | 24（Theory 展开 25） | ShapeGenerator 的全部语义：中心+半径、26 段虚线圆、中心矩形/椭圆、焦点、平行线四偏移与四档吸附、长方体两步（5 实 3 虚、23 条）、双曲线两步（22 条渐近线、k 锁定的 4 弧与焦点）、圆柱 17 条/圆锥 16 条、dash 5/13.8、dot 8.28/0.8/1.6×、箭头 30×10、坐标轴 20×7 与 25 尾、抛物线端点精确与焦点 |
| `tests/InkCanvas.Core.Tests/DomainTests.cs` → `ShapeRecognizerTests` | 5 | 圆（R 85..95、中心 195..205）、椭圆（rx 130..150、ry 62..78）、直线（2 角点）、拒绝小图、拒绝开放涂鸦（None 或 Line） |
| `DomainTests.cs` → `ShapeGeneratorTests` | 4 | 每个 ShapeKind 都产出非空/已 Complete、Coordinate3D 恰 3 条、虚线 >3 段、圆在非等比拖拽下仍近似圆形（Bounds W/H 0.9..1.1） |
| `DomainTests.cs` → `SettingsTests` | 2 | Settings JSON 往返（inkWidth 7.5、theme 2、路径）、legacy WPF 片段读取与未出现段保持默认 |
| `tests/InkCanvas.Core.Tests/SelectionGeometryTests.cs` | 10 | 四角/内部/远处命中、旋转手柄位置、角点缩放绕对角、Move 平移、旋转绕中心、keepAspect、退化不 NaN、None 恒等 |
| `tests/InkCanvas.Core.Tests/ReplayTests.cs` → `ReplayClockTests` | 9 | 初始暂停、暂停时 Advance 无效、累加、Speed 2×、末尾夹取并暂停、Seek 夹取、Progress、Toggle、完成后重播 |
| `ReplayTests.cs` → `InkReplaySessionTests` | 4 | 负时间无可见、Duration 时全可见（点数 10/10）、可见点数单调不减、部分笔画保留存储宽度 7 |
| `tests/InkCanvas.Core.Tests/PixelCompositingTests.cs` | 7 | 不透明替换、全透明 no-op、半透明 100/255、偏移只写目标、右下溢出裁剪、负偏移裁剪、空源 no-op |
| `tests/InkCanvas.Core.Tests/UpdatePolicyTests.cs` | 7 | 静默关/自动更新关、同日窗口、跨午夜、00:00–00:00 无限制、起点含终点不含、乱码回退询问 |
| `tests/InkCanvas.Core.Tests/UpdateTests.cs` → `AppVersionTests` | 4（Theory 展开 9） | 5 种标签形式、4 种拒绝、逐级排序、Build 破平 |
| `UpdateTests.cs` → `ReleaseFeedTests` | 6 | latest 解析、Setup.exe 优先、无 exe 返回 null、列表跳过 draft/prerelease、非法 JSON、IsNewerThan |
| `tests/InkCanvas.Core.Tests/PalmRejectorTests.cs` | 9 | 小/大接触、palm 不画但擦、指尖画不擦、笔优先、可关闭、抬笔释放、抬 palm 清除、Reset |

**未覆盖/未验证**（必须在 HarmonyOS 移植说明里如实标注）：

- `TouchContactClassifier.Classify` / `MeasureContactWidth` / `BoundsWidth`：0 个直接测试；源码注释写明未在真实红外屏/压感笔上验证。
- `ThemeResolver`：0 个测试。
- `FileLog`：0 个测试。
- `AppSettings.ResetToRecommendation`：没有逐键断言（只有默认值/往返测试）。
- `OriginalMenuCatalog`：没有测试。
- 两步式形状的 `InkController` 状态机：`ShapeDrawingSemanticsTests` 只测纯生成函数，未测拖拽状态（有 `SelfTest` 的运行路径，但不是单元测试）。
- 墨迹转图形替换（`TryRecognize`）的撤销单元：没有单元测试。

---

## 14. 逐功能移植分类

> `pure-logic` = 可 1:1 重写为 ArkTS 纯函数/类；`adapt` = 逻辑可复用但输入/输出或存储/主题/设备能力要平台适配；`platform-bound` = 强绑 Windows API，不能直接移植；`drop-candidate` = 建议放弃并说明。

| id（kebab-case） | 功能 | 分类 | 依据 / 适配点 |
|---|---|---|---|
| `shape-generator` | 25 种预设图形生成 | **pure-logic** | 零平台 API；只依赖 Vector2/InkStroke 模型。浮点用 double 时需 `Math.fround` 或在黄金向量上给 1e-4 容差 |
| `shape-two-step` | 长方体/双曲线两步拖拽状态机 | **adapt** | 状态机本身是纯逻辑，但绑定 `InkController` 的指针事件与 Tool 切换；ArkTS 需接 `onTouch`/`onPointer` 并复刻 `_stepAnchor/_stepK` 语义 |
| `shape-recognizer` | 墨迹转图形几何拟合 | **pure-logic** | 纯数学；注意阈值单位是世界坐标，不能按 DPI 缩放 |
| `ink-to-shape-integration` | 识别结果替换与撤销 | **adapt** | 逻辑简单（圆用 Center + max 半径、椭圆用中心±半径、矩形闭合 4 角），但依赖文档/历史栈；ArkTS 需保证一个撤销单元 |
| `selection-geometry` | 选区命中 + 拖拽矩阵 | **pure-logic** | 需自实现 Matrix3x2 行向量语义；`HitTest` 用 view 坐标 |
| `selection-drag-conjugation` | view→world 矩阵共轭 | **adapt** | 依赖视图变换 `ViewTransform`；ArkTS 的画布缩放/平移表达不同，但公式 `V·D·V⁻¹` 必须保留 |
| `ink-replay` | 回放会话 + 时钟 | **pure-logic** | 默认 12ms/点、33ms 帧；ArkTS 定时器精度更高，`Advance` 仍按真实 delta |
| `pixel-compositing` | 预乘 BGRA 合成 | **pure-logic** | 纯整数缓冲；HarmonyOS 若改 RGBA/非预乘合成，需在调用层转换，函数本身可 1:1（配合 Uint8Array） |
| `touch-contact-classifier` | 触摸接触分档 / 特殊屏 | **adapt** | 依赖数字化仪 ContactRect；HarmonyOS 触摸 size 语义/单位不同，必须实测并重标定；红外屏专属分档可能不适用 |
| `palm-rejector` | 掌拒与笔优先 | **adapt** | 与上一条同源；ArkUI 的 `sourceTool` 可区分 pen/touch，但 `ContactWidth/Height` 未必可得 |
| `theme-resolver` | 明暗主题判定 | **adapt** | 判定是纯逻辑；资源切换（`RequestedTheme`）要映射到 HarmonyOS `colorMode`/资源限定词 |
| `app-settings-schema` | Settings.json 键/类型/默认值 | **pure-logic** | 纯数据模型；枚举按数字、键名逐字一致 |
| `settings-store` | Settings.json 读写 | **adapt** | 路径 %APPDATA% → 应用沙箱；原子写 tmp+rename；损坏回退默认 |
| `settings-reset` | 重置为推荐设置 | **pure-logic** | 逐键赋值；保留自动删除配置；随后重应用并保存 |
| `original-menu-catalog` | 色板/文案/笔宽换算 | **pure-logic** | 静态数据；颜色为 RGB 常量 |
| `app-version` | 版本解析与比较 | **pure-logic** | 注意 int 解析/溢出与 ToString 规则 |
| `release-feed-parser` | GitHub Release JSON 解析 | **adapt** | 解析逻辑可复用；资产筛选规则（`.exe`、`setup`）是 Windows 专属，HarmonyOS 应改为 HAP/应用市场资产 |
| `update-policy` | 静默更新时间窗 | **pure-logic** | 本地时间 hour/minute；跨午夜/等值无限制 |
| `update-install-flow` | 下载并运行安装器 | **platform-bound / drop-candidate** | `Process.Start(.exe)`、临时目录、GitHub 下载；HarmonyOS 不能运行 Windows 安装器。建议放弃"自安装"，改为跳转应用市场或下载 HAP 后交给系统安装器（需权限与签名校验），并在移植说明中明确 |
| `file-log` | 文本日志 | **adapt** | 文件追加/路径/时间格式；保留 `Log.txt` 与行格式 |

---

## 15. 建议的黄金向量（`parity/vectors/*.json`）

仓库当前没有 `parity/`；若按 CLAUDE.md 的契约补建，下面是能"钉死"本清单行为的最小集合。所有数值建议以 **float32 的十六进制位**或 `%.9g` 输出，ArkTS 侧用 `Math.fround` 对齐；比较容差 `1e-4`（整数合成除外）。

1. `shapes.json`
   - 输入：`kind`（0..24）、`start`、`end`、`baseWidth`、`color`。
   - 输出：每条 stroke 的 `points[{x,y,pressure,width,timeMs}]`、`isRecognizedShape`、`isCompleted`，按返回顺序。
   - 至少覆盖：Circle（中心/半径）、RectangleFromCenter、EllipseWithFoci（x/y 轴/正圆）、DashedCircle（26 条）、ParallelLine（水平/30/45/60/垂直）、Cuboid 两步、Hyperbola 两步、Cylinder/Cone、ParabolaUp/Right/WithFoci、Coordinate 五种。
2. `shape-depth.json` / `shape-hyperbola.json`：两步函数独立输入输出（frontTop/frontBottom/depth；center/k/end/withFoci）。
3. `shape-recognizer.json`：点序列 → `{shape,center,radiusX,radiusY,confidence,corners}`；包含圆/椭圆/线/矩形/拒绝样例。
4. `selection.json`：`{handle,bounds,from,to,keepAspect}` → 6 个矩阵分量 + `hitTest` 结果。
5. `replay.json`：每笔画点数 + `msPerPoint` + 采样时间 → 每个时刻可见的 `(strokeIndex, pointCount, widths[])`。
6. `pixel-composite.json`：dst/src 小缓冲 + 偏移 → 逐字节输出。
7. `touch-classify.json`：`{contactWidth, advanced 全字段, nibMode, forceEraser, isDrawingShape, forcePointEraser, pptGestureEligible}` → `{intent, eraserDiameter}`；必须包含特殊屏 `touchMultiplier=0` 与 QuadIR 的 `sqrt(w*h)` 样例。
8. `settings.json`：默认实例序列化后的**完整 JSON**（键序可不定，但键名/类型/值必须一致）；另加 legacy 片段读取断言。
9. `version.json`：标签 → 解析结果/拒绝；`policy.json`：`(silent,start,end,now)` → bool。
10. `release-feed.json`：GitHub payload → `{tag,version,name,notes,pageUrl,installerUrl,installerSize}` 或 null。

---

## 16. 移植检查清单（下游可直接执行）

1. 先落基础类型：`Vector2`（float32）、`Mat3x2`（行向量 + `multiply(a,b)` 先 A 后 B）、`Rect`（`IsEmpty<=0`、`Contains` 闭区间）、`InkPoint/InkStroke/StrokeAttributes/InkColor`。
2. 形状：先复制常量表（2.2），再逐 kind 实现；用 `shapes.json` 对拍。特别注意：Circle/RectangleFromCenter/EllipseFromCenter/EllipseWithFoci 的中心=指针按下点；Coordinate3D 的 y 轴永远向上；平行线垂直档 `sin` 强制 +1。
3. 两步式：先实现纯函数，再接 ArkTS 指针状态机；长方体第一步结束后把 front rect min/max 归一化；双曲线第二步把 `iniP` 锁在中心；`Yes` 表示保留渐近线。
4. 识别：按 3.2–3.5 原样实现；不要额外补角点/平滑；`Triangle` 不替换。
5. 选区：先对拍 `DragMatrix` 的 6 个分量，再接视图共轭；命中用 view 坐标。
6. 回放：12ms/点 + 33ms 帧；部分笔画不 Complete；宽度逐点复制。
7. 像素：先确认目标 PixelMap 的通道与预乘状态；整数公式不要改成浮点。
8. 触摸：先实测 HarmonyOS 的 size/pressure 单位，再决定是否保留 QuadIR/倍数分档；无 ContactRect 时退回简单阈值并标注。
9. 设置：JSON 键名逐字复制（9.2 表）；枚举写数字；`behavior` 是 PPT；`AutoSwitchTwoFingerGesture` 大写 A；`isSecondConfime...` 拼写错误保留；文件路径改沙箱。
10. 更新：只保留版本解析/时间窗/Release JSON 展示；安装动作改为平台方案或明确放弃。
11. 日志：保留 `Log.txt` 与 `时间 [类型] 消息` 格式；路径改沙箱；异常吞掉。
12. 所有"未验证"项写入 `docs/` 的 HarmonyOS porting notes 与 CHANGELOG（不声称通过）。

---

## 17. 未验证、风险与开放问题

1. **未运行任何测试/应用**：本报告是静态源码盘点；`ShapeDrawingSemanticsTests` 等测试是否在当前工作树通过，未验证。
2. **`harmony/` 与 `parity/` 不存在**：CLAUDE.md 要求的 manifest、`@parity` 锚点、黄金向量均未落地；移植前需要先决定由哪一步创建，否则"两侧互认"没有可校验载体。
3. **图形数量口径不一致**：任务写 24，代码 25，`docs/PORTING-NOTES.md` 写 26。以 `ShapeKind` 枚举（0..24）为准。
4. **ArkTS 数值语义**：C# 全链路是 float32（`Vector2`/`MathF`），ArkTS `number` 是 double。若直接双精度计算，形状采样点与边界判定会有 1e-6 级差；黄金向量需明确用 `Math.fround` 逐步对齐，或接受 `1e-4` 容差。
5. **整数除法**：`PixelCompositing` 的 `dst*inv/255` 是整数截断；ArkTS 必须 `Math.trunc` 或 `|0`，不能用浮点后再取整的等价式（会有 1 LSB 差）。
6. **触摸尺寸来源**：HarmonyOS 的触摸 `size` 是半径还是直径、单位是 px 还是 vp、是否随 DPI/缩放变化，未验证；`TouchContactClassifier` 的 30/5/20/0.25 等默认值来自希沃红外屏经验，不能假设适用。
7. **特殊屏分档**：`IsQuadIR` 的 `sqrt(w*h)` 与 `TouchMultiplier` 是为四边红外/大屏定制；平板/手机可能应直接跳过该分类器（用 `sourceTool` + 半径阈值）。
8. **主题枚举顺序**：`ThemeOptions` 显示顺序是 [浅,深,跟随]，存储是 [跟随=0,浅=1,深=2]；ArkTS 若直接用数组索引会错。
9. **`OptionalOperation` 语义**：`Yes=0` 表示保留渐近线，`No=1` 表示移除；命名与直觉相反，UI 文案需核对上游。
10. **更新流程目标**：HarmonyOS 不能运行 `.exe`；是走 AppGallery 自动更新、企业 MDM，还是 HAP 侧载，未定。`ReleaseFeed` 的资产筛选与 `UpdateService` 的下载/校验/启动需要重新设计。
11. **`AutoSavedStrokesLocation` 默认是 Windows 盘符路径**：鸿蒙上不可用；保留键但默认值应改为沙箱路径，或读取到无效路径时回退。是否改默认值会影响 Settings.json 互读，需要产品决策。
12. **PPT/WPS/自动化设置**：大量 `PowerPointSettings`/`AutomationSettings` 键在 HarmonyOS 无对应实现；为 Settings.json 兼容应保留键与默认值，但 UI 上必须显示"未实现"而不是假开关（`docs/menu-porting-notes.md` 的要求）。
13. **`FileLog` 时间格式**：C# `:O` 是 7 位小数 + 时区偏移；ArkTS 的 ISO 字符串通常 3 位毫秒 + `Z` 或偏移。若下游日志工具按格式解析，需要自定义格式化。
14. **`FileLog.NewLog(Exception)` 是空实现**：移植时不要"顺手修好"，否则行为与上游不一致（且可能引入异常路径）。
15. **`ShapeRecognizer` 与 InkAnalyzer 不相等**：源码明确说判定边界是自研的，不能拿原版 InkAnalyzer 结果做等价性验收；只能用本项目测试/向量。
16. **形状 UI 的图标路径**：`ShapeIcons` 是逐 Path 转录的 WPF 资源（约 600–870 单位画布），若 ArkTS 用 SVG/Path 重画，需要逐条对照；这部分不在 Core 清单内，但影响"UI 一致"验收。
17. **回放的部分笔画**：`VisibleAt` 创建的部分笔画没有 `Complete()`；若 ArkTS 的渲染器要求 completed 才绘制，会少显示最后一帧，需要在适配层处理。
18. **`AppSettings.Clone()` 的 JSON 往返**：依赖枚举数字与默认值；ArkTS 若实现 `clone` 用 `structuredClone`，语义等价，但要注意不可序列化字段（无）。
19. **`SettingsStore` 的 `PropertyNameCaseInsensitive=true`**：ArkTS 的 `JSON.parse` 是大小写敏感的；若要兼容旧文件，需要手写大小写不敏感键匹配（至少对已知键）。
20. **`ReleaseFeed.ParseNewestStable` 的版本比较**：只在同数组内取最高，不按发布时间；若 GitHub 返回乱序，仍按版本号选，符合上游。

---

## 18. 附：本次阅读的文件清单（绝对路径）

- `D:/InkCanvas/src/InkCanvas.Core/Shapes/ShapeGenerator.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Recognition/ShapeRecognizer.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Selection/SelectionGeometry.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Replay/InkReplaySession.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Replay/ReplayClock.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Imaging/PixelCompositing.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Input/TouchContactClassifier.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Theming/ThemeResolver.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Settings/AppSettings.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Update/ReleaseFeed.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Update/UpdatePolicy.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Versioning/AppVersion.cs`
- `D:/InkCanvas/src/InkCanvas.Core/Diagnostics/FileLog.cs`
- 基础类型：`Ink/InkStroke.cs`、`Ink/InkPoint.cs`、`Ink/StrokeAttributes.cs`、`Ink/InkColor.cs`、`Ink/TaperProfile.cs`、`Ink/InkDocument.cs`、`Ink/MatrixTransformCenter.cs`
- 消费方：`src/InkCanvas.Ink/InkController.cs`、`src/InkCanvas.Platform/Input/PalmRejector.cs`、`src/InkCanvas.App/Services/AppServices.cs`、`src/InkCanvas.App/Services/ShellController.cs`、`src/InkCanvas.App/Services/WindowChrome.cs`、`src/InkCanvas.App/Services/UpdateCoordinator.cs`、`src/InkCanvas.App/Views/SettingsWindow.cs`、`src/InkCanvas.App/Views/ReplayWindow.cs`、`src/InkCanvas.Controls/Controls/ShapePreview.cs`、`src/InkCanvas.Controls/Controls/PenPalette.cs`、`src/InkCanvas.Controls/Controls/FloatingBar.cs`、`src/InkCanvas.Features/Update/UpdateService.cs`、`src/InkCanvas.Features/Persistence/Stores.cs`
- 测试：`tests/InkCanvas.Core.Tests/ShapeDrawingSemanticsTests.cs`、`DomainTests.cs`、`SelectionGeometryTests.cs`、`ReplayTests.cs`、`PixelCompositingTests.cs`、`UpdatePolicyTests.cs`、`UpdateTests.cs`、`PalmRejectorTests.cs`



