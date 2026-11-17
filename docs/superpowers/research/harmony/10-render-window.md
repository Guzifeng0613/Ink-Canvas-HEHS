# 10 — 渲染与窗口能力（HarmonyOS 知识库提取）

- 日期：2026-11-18
- 角色：只读研究员（仅提取 `D:\InkCanvas\.claude\skills\harmony-os\` vendored 知识库事实）
- 版本基线：HarmonyOS 26.0.0 / API 26（Release，2026-08-29）为主；HarmonyOS 6.1.1 / API 24（Release，2026-05-26）向下兼容
- 指定范围：
  - `media/references/graphics2d.md`
  - `dev/references/ui-development.md`
  - `dev/references/app-framework.md`
  - `dev/references/desktop-extension-kit.md`
  - `dev/references/ui-design-kit.md`
  - `system/references/basic-services.md`
- 补充核对（同属 vendored KB，仅用于补齐上述文件未展开的交叉引用）：`samples/references/graphics.md`、`templates/references/arkui-components.md`、`dev/references/ndk-development.md`、`dev/references/pen-kit.md`、`dev/references/permissions.md`、`dev/references/multi-device.md`、`system/references/device-info.md`、`design/references/ux-best-practices.md`、`design/references/hmi-system-features.md`、根 `SKILL.md` / `dev/SKILL.md`
- 证据标记：
  - **【KB】** 知识库原文明确写明；
  - **【推断】** 基于 KB 事实的工程判断，不是 KB 承诺；
  - **【未知】** KB 未写明，必须查官方 API 参考或实机验证。
- 验证边界：本次为静态阅读，**未安装/未调用 HarmonyOS SDK，未编译 HAP，未实机运行**；文中所有性能结论均为待验证假设。

---

## 0. 结论摘要（先看这里）

1. **可变宽度笔画的高性能渲染**：【推断】首选 **XComponent（`type:'surface'`）+ NDK Drawing** 的原生渲染后端；ArkUI `Canvas` + `CanvasRenderingContext2D` 作为 API 24/手机/低复杂度场景的降级后端。依据是 KB 明确写了 XComponent 暴露 surface 与触屏事件接口、NDK Drawing 支持在 surface 上绘制，而 Canvas 只给了声明式组件的 `onReady` 全量绘制示例。**KB 没有任何性能数字、离屏/脏区/保留模式语义，选型前必须做 A/B spike。**
2. **离屏缓存/增量重绘**：【推断】采用「已提交层 + 活动层」双缓冲架构，并缓存每笔已完成笔迹的几何（Path/轮廓）。KB 没有 `OffscreenCanvas`、`clearRect`、脏区回读、surface 保留模式的任何记录，具体 API 全部 **【未知】**。
3. **第三方应用覆盖其他应用的全屏透明画笔**：**KB 中没有任何 API 或权限支持这种能力**。KB 记录的窗口 API 全部作用于本应用窗口（`getLastWindow`/`findWindow`/`resize`/沉浸式/系统栏），权限清单里也没有悬浮/覆盖类权限；`Desktop Extension Kit` 只做状态栏/快捷栏，且仅 PC/2in1、仅中国境内。**按现有文档依据，应按「不可行」规划**，并以官方窗口 API/权限文档 + 实机验证做最终确认。
4. **浮动工具栏载体**：
   - **API 26**：可用应用内 `OverlayManager` 的按层级浮层（26.0.0 Beta2 新增，KB 无签名）；`闪控窗`（26.0.0 Beta1 新增，悬浮在桌面/应用界面上的**小型窗口**）概念上接近，但 **API 名、第三方资格、尺寸、输入穿透、权限全部未知**，不能作为既定方案。
   - **API 24**：KB 未记录 `OverlayManager` 层级浮层与闪控窗；稳妥方案是主窗口内 `Stack` 浮层。
   - **子窗**：KB 只在 26.0.0 Beta2 提到「设置主窗或子窗支持的窗口模式」，**没有任何创建子窗的 API**，【未知】。
5. **PC/2in1 自由窗口/桌面模式**：【KB】设计规范要求平板及更宽设备适配自由窗口、左右分屏、分屏比例可调、键鼠；API 26 Release 新增「查询当前窗口是否处于指定窗口姿态模式（当前支持识别桌面模式）+ 姿态改变监听」，26.0.0 Beta2 新增「设置主窗或子窗支持的窗口模式」，但 **API 名/签名均未给出**。API 24 没有这些记录，只能按响应式布局适配。
6. **API 24 与 26 的关键差异**：闪控窗、闪控球销毁事件、OverlayManager 层级浮层、通用 `systemMaterial`、系统材质等级/ImmersiveMaterial、窗口姿态查询、窗口模式设置、PiP 自动启动 C API、窗口多模事件过滤 C API、Pen Kit 跟手性加速均为 **API 26 新增**；HDS `systemMaterialEffect`、`HdsMaterial`、平行视界 `easy_go.json` 在 **API 23 起可用（API 24 可用）**。`WindowFocusState` / `WindowPositionParams` 是 **26.0.1 Beta1**，不在 26.0.0 Release，且 Beta 版不能上架。

---

## 1. 知识库覆盖度与证据等级

| 能力域 | 指定 KB 文件 | KB 明确写到的内容 | 关键缺口 |
|---|---|---|---|
| ArkUI Canvas 2D | `dev/references/ui-development.md`；`samples/references/graphics.md` | `Canvas(this.context)`、`RenderingContextSettings(true)`、`CanvasRenderingContext2D`、`.onReady()`、`fillStyle/fillRect/beginPath/arc/fill/font/fillText` | 无 `clearRect`/`OffscreenCanvas`/`Path2D`/`moveTo`/`lineTo`/`fill(path)`/填充规则/变换/图层；无起始 API 版本 |
| XComponent | `templates/references/arkui-components.md`；`samples/references/graphics.md`；`dev/references/ndk-development.md` | `XComponent({id,type,controller})`；type 注释列出 `texture / surface / node`；`.onLoad((ctx)=>{})`；`XComponentController.create()`；NDK 提供 surface 与触屏事件接口 | 无 `onLoad(ctx)` 的参数类型/生命周期；无 surface 尺寸、缓冲、交换链、脏区、销毁；未给 `surface` 用法示例 |
| Drawing（ArkTS / NDK） | `media/references/graphics2d.md`；`dev/references/ndk-development.md` | `@ohos.graphics.drawing`；`Canvas/Pen/Brush/Path/Typeface/Image`；Pen 画轮廓、Brush 填充；NDK `Drawing` 支持「在 surface 进行绘制」 | 无具体方法签名、无 Path 构建/填充规则/抗锯齿/裁剪/图层/离屏 Surface API |
| 帧回调 / 可变帧率 | `media/references/graphics2d.md`；`samples/references/graphics.md` | `displaySync.createDisplaySyncScene()`、`setTargetFrameRateRange({min,max,expected})`、`start/stop`；同窗口多帧率、动态配置、录制回放、多渲染后端（亮点特征） | 无 `requestAnimationFrame`/vsync 回调；`createDisplaySyncScene` 可能是示例别名，正式 API 名与起始版本【未知】 |
| RenderNode / FrameNode | `samples/references/graphics.md` | `FrameNode`、`RenderNode`、`DrawingColor`；`setPosition`、`setBackgroundColor`、`appendChild`；`XComponent({type:'node', content: rootNode})` | 无绘制回调/失效/变换/图层语义，单靠它不足以承载墨迹 |
| Pen Kit | `dev/references/pen-kit.md` | `HandWriteComponent({canvasConfig:{width,height,backgroundColor}})`；API 23 禁用缩放/ScrollTo/长画布最大高度；API 26 跟手性加速 | 无擦除/多层/自定义渲染/笔迹数据导出；组件是黑盒 |
| 主窗口 / WindowStage | `system/references/basic-services.md`；`dev/references/app-framework.md` | `getLastWindow`、`findWindow('mainWindow')`、`resize`、`setWindowLayoutFullScreen`、`setWindowSystemBarProperties`、`setSpecificSystemBarEnabled`、`windowStage.loadContent`、`windowStageEvent` | 无 `setWindowTopmost`、无窗口透明背景/alpha、无窗口位置/尺寸约束、无子窗创建 |
| 子窗 / 闪控窗 | 根 `SKILL.md`；`dev/SKILL.md` | 26.0.0 Beta1：闪控窗「悬浮在桌面/应用界面上的小型窗口」；26.0.0 Beta2：设置主窗或子窗支持的窗口模式、闪控球销毁事件；26.0.1 Beta1：`WindowFocusState`、`WindowPositionParams` | 无 API 名/签名/权限/尺寸/第三方资格/输入语义 |
| 自由窗口 / 桌面模式 | `design/references/ux-best-practices.md`；根 `SKILL.md` | 平板及更宽设备适配自由窗口、左右分屏、比例可调；26.0.0 Release 窗口姿态模式查询与监听（识别桌面模式） | 无 API 名/签名；无进入/退出自由窗口的 API |
| 沉浸式 / 系统栏 | `system/references/basic-services.md`；`design/references/hmi-system-features.md` | `setWindowLayoutFullScreen(true)`、透明状态栏/导航栏、`setSpecificSystemBarEnabled`；沉浸页面超过 2 秒自动隐藏导航条 | 无刘海/挖孔避让 API；无全屏手势排除 API |
| OverlayManager | 根 `SKILL.md`；`dev/SKILL.md` | 26.0.0 Beta2：`UIContext` 的 `OverlayManager` 新增按层级配置浮层能力 | 无方法签名；API 24 是否可用【未知】 |
| 系统材质 | `dev/references/ui-design-kit.md`；`dev/references/ui-development.md`；根 `SKILL.md` | API 23：HDS `systemMaterialEffect:'AUTO'`、`hdsMaterial.getSystemMaterialTypes()`、`HdsMaterial`；API 26 Beta1：通用 `systemMaterial`；API 26 Release：材质等级枚举、全局等级查询、ImmersiveMaterial 判断 | 无枚举取值（除示例 EXQUISITE/GENTLE/SMOOTH）、无 API 签名、无 API 24 降级行为 |
| Desktop Extension Kit | `dev/references/desktop-extension-kit.md`；根 `SKILL.md` | 系统级统一入口，接入状态栏与快捷栏；仅 PC/2in1；仅中国境内；不支持模拟器；API 26 加快捷栏查询/图标/进度、状态栏图标悬浮回调 | 无具体 API 签名；不提供悬浮画布 |
| deviceInfo / display | `system/references/device-info.md`；`system/references/basic-services.md`；`dev/references/multi-device.md` | `deviceInfo.uuid/deviceType/osFullName`；`systemParameter.getSync('const.ohos.devicetype')`；`display.getDefaultDisplaySync()`、`getAllDisplays`、`on('add'/'change')`、`Display.id/width/height/densityDPI` | `2in1` 的 deviceType 值、width/height 单位、densityPixels、多屏坐标语义均【未知】 |

---

## 2. 渲染能力事实

### 2.1 ArkUI Canvas 2D（声明式画布组件）

**KB 明确事实**

```ts
private settings: RenderingContextSettings = new RenderingContextSettings(true)
private context: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.settings)

Canvas(this.context)
  .width(300)
  .height(300)
  .onReady(() => {
    this.context.fillStyle = '#007DFF'
    this.context.fillRect(10, 10, 200, 100)

    this.context.beginPath()
    this.context.arc(100, 50, 30, 0, Math.PI * 2)
    this.context.fillStyle = '#36D1A1'
    this.context.fill()

    this.context.font = '24px HarmonyOS Sans'
    this.context.fillStyle = '#000000'
    this.context.fillText('Hello HarmonyOS', 10, 80)
  })
```

来源：`samples/references/graphics.md:8-43`。`CanvasRenderingContext2D`、`RenderingContextSettings`、`Canvas`、`onReady` 均在此出现。

**要点**

- `RenderingContextSettings(true)` 的构造参数含义 KB 未解释；不要据此推断抗锯齿开关。【KB 未说明】
- KB 只示范了矩形、圆弧、文本和纯色填充。**没有** `clearRect`、`moveTo`、`lineTo`、`bezierCurveTo`、`quadraticCurveTo`、`closePath`、`fill(path)`、`stroke(path)`、填充规则、`save/restore`、`translate/scale/rotate`、`drawImage`、`Path2D`、`OffscreenCanvas`、`getImageData/putImageData`、`globalCompositeOperation`、`lineCap/lineJoin`、`setLineDash`、阴影。【未知，需查官方 Canvas API】
- 起始 API 版本 KB 未给出；`ui-development.md` 头部标注「适用 API 14+」，`samples/graphics.md` 头部也写「兼容 API 14+」。只能记录为「示例标注 API 14+」，不能断言 Canvas 本身从 API 14 开始。
- KB 未记录 Canvas 的重绘触发方式。示例是 `onReady` 一次性绘制；后续如何在状态变化时重绘、Canvas 内容是否跨帧保留、是否存在请求帧回调，均【未知】。
- 对 InkCanvas 的含义：Canvas 适合工具栏图标、圆环、缩略图、简单预览；用它绘制「每帧数千个 capsule 轮廓 + 非零环绕填充」是否可行，KB 无法回答。【推断】

### 2.2 XComponent + Drawing（原生渲染路径）

**KB 明确事实**

```ts
XComponent({
  id: 'canvas',
  type: 'texture',   // 注释列出 texture / surface / node
  controller: this.xComponentController
})
  .width('100%')
  .height('100%')
```

来源：`templates/references/arkui-components.md:174-184`。示例只使用 `texture`；`samples/references/graphics.md:18-22` 又出现：

```ts
XComponentController.create()
XComponent({ type: 'texture', id: 'canvas' })
  .onLoad((ctx) => {
    // ctx 是 XComponent 的 native Canvas
  })
```

以及 `XComponent({ type: 'node', content: rootNode })`（`samples/references/graphics.md:99`）。

- `type` 的三个取值来自代码注释：`texture / surface / node`。KB **没有**给出 `surface` 的完整用法示例。【KB 列出但未示范】
- `onLoad((ctx) => {})` 存在；注释声称 `ctx` 是 native Canvas。此说明与常见 XComponent 语义可能不符（通常为 context/surface id），**必须官方核对**，不要照抄注释。【未知】
- `XComponentController.create()` 在示例中没有赋值给任何变量，是片段式伪代码，不能作为可编译签名。【推断】
- NDK 文档明确：XComponent「提供 surface 与触屏事件等接口」；Drawing「系统提供的 2D 图形库，支持在 surface 进行绘制」；Native 代码受系统安全沙箱约束。来源：`dev/references/ndk-development.md:34-35,70`。
- ArkGraphics 2D 的 Drawing 模块提供 `drawing.Canvas`（承载绘制操作）、`drawing.Pen`（轮廓样式）、`drawing.Brush`（填充样式）、`drawing.Path`（几何形状）、`drawing.Typeface`、`drawing.Image`。来源：`media/references/graphics2d.md:88-100`。
- ArkGraphics 2D **不需要额外权限声明**。来源：`media/references/graphics2d.md:102-104`。
- 起始 API 版本仍未给出；`graphics2d.md` 无版本头，`samples/graphics.md` 标注兼容 API 14+。【未知】

**对 InkCanvas 的含义（推断）**

- XComponent 的 `surface` 模式 + NDK Drawing 是唯一在 KB 中被明确描述为「在 surface 上绘制」的高性能路径；适合把每笔已完成几何缓存为 `drawing.Path`，活动笔只重绘脏区。
- 但 KB 没有 surface 的创建/尺寸/交换链/局部刷新/销毁 API，也没有「如何把 ArkTS 的 Path 传给 NDK」「如何在 surface 上保留上一帧」的说明；落地前必须写一个最小探针。
- 若团队不想引入 C++，可先用 Canvas 双图层方案；但可变宽度笔画的每帧路径重建成本未知。

### 2.3 FrameNode / RenderNode

**KB 明确事实**

```ts
import { FrameNode, RenderNode, DrawingColor } from '@kit.ArkUI'

let rootNode = new FrameNode(getContext())
let renderNode = new RenderNode()
renderNode.setPosition(100, 100, 200, 200)
renderNode.setBackgroundColor(0xFF007DFF)
rootNode.appendChild(renderNode)
XComponent({ type: 'node', content: rootNode })
```

来源：`samples/references/graphics.md:80-100`。

- KB 只示范了位置与背景色；**没有**绘制回调、内容失效、变换、裁剪、图层、路径绘制 API。【未知】
- 同文件在模糊示例中写「配合 RenderNode 使用实现背景模糊」，但没有代码。来源：`samples/references/graphics.md:46-56`。
- 结论：RenderNode/FrameNode 可以作为自定义节点容器或背景效果，但不能单独作为墨迹渲染器。【推断】

### 2.4 帧回调 / 可变帧率（DisplaySync）

**KB 明确事实**

```ts
import displaySync from '@kit.ArkGraphics2D'

let displaySyncScene = displaySync.createDisplaySyncScene()
displaySyncScene.setTargetFrameRateRange({
  min: 30,
  max: 120,
  expected: 60
})
displaySyncScene.start()
// ...
displaySyncScene.stop()
```

来源：`samples/references/graphics.md:58-78`。

- `media/references/graphics2d.md:23-27,74-79` 说明可变帧率能力：同窗口支持多个帧率、帧率动态配置、录制回放机制（录制命令缓存并回放，提升跟手性）、多种渲染后端。
- KB 没有给出 `requestAnimationFrame`、vsync 回调、帧时间戳、掉帧统计等 API。【未知】
- `createDisplaySyncScene` / `setTargetFrameRateRange` 只在示例中出现，未在 API 表格中列出；正式 ArkTS 接口名与起始版本【未知/需官方核对】。
- 结论：把它作为「渲染循环节拍器」的候选，但必须先核对正式 API；不要在业务代码直接散落调用，应封装在平台层。【推断】

### 2.5 Pen Kit HandWriteComponent

**KB 明确事实**

```ts
import { HandWriteComponent } from '@kit.PenKit'

HandWriteComponent({
  canvasConfig: {
    width: 360,
    height: 600,
    backgroundColor: Color.White
  }
})
```

来源：`dev/references/pen-kit.md:15-27`。

- 定位：Pen Kit 的核心组件，提供手写绘制画布，面向笔记、绘画、签名。【KB】
- API 23 新增：禁用画布缩放、`ScrollTo` 滚动位置及长画布滚动监听、自定义长画布最大高度。来源：`dev/references/pen-kit.md:31-43`。
- API 26 新增：手写笔跟手性加速接口，提升书写时延。来源：根 `SKILL.md:450`。
- KB 没有擦除模式、多图层、笔迹导出、自定义渲染、笔锋/压力模型、手势与选择等 API。【未知】
- 对 InkCanvas：它更像「成品手写板」，不适合 InkCanvas 需要的活动层/已提交层、面积擦除、整笔擦除、选区和形状工具；除非只用于签名/便签。选用前必须确认能否拿到笔迹数据并叠加自定义渲染。【推断】

### 2.6 选型：Canvas 还是 XComponent + Drawing

| 评估项 | ArkUI Canvas 2D | XComponent + NDK Drawing |
|---|---|---|
| KB 依据 | `Canvas` + `CanvasRenderingContext2D` + `onReady`，示例全量绘制 | XComponent surface + 触屏接口；NDK Drawing 在 surface 绘制；Drawing Pen/Brush/Path |
| 可变宽度笔画（capsule 并集 + 非零环绕） | 需要每帧构建/填充大量路径；填充规则 API KB 未给出 | 可用原生 Path 缓存与填充；但 Path 填充规则/抗锯齿 API 未给出 |
| 已提交层缓存 | KB 无离屏/图层/缓存 API | 可用原生 surface/Path 缓存，具体 API 未给出 |
| 局部重绘/脏区 | KB 无 `clearRect`/脏区 API | KB 无脏区/交换链 API |
| 输入到呈现延迟 | KB 无数据 | NDK 触屏接口 + 原生绘制，理论更短；KB 无数据 |
| API 24 可用性 | 示例标注 API 14+，可用性高 | KB 未给起始版本；需确认 NDK 工具链与设备 |
| 工程复杂度 | 低（纯 ArkTS） | 高（C++/CMake/Node-API/生命周期） |
| 结论 | **降级/UI 层方案** | **主方案候选，先 spike** |

**推荐（推断）**

- 目标设备（平板、PC/2in1）与 API 26 基线：优先做 **XComponent `type:'surface'` + NDK Drawing** 的后端；用 `DisplaySync` 做节拍；每笔完成后缓存 Path，活动笔只重绘包围盒。
- API 24 / 手机 / 初期版本：先做 **Canvas 双图层** 后端，保证功能与 golden vectors 一致；性能不达标再切原生。
- 两个后端共享同一套纯几何（`StrokeGeometry` 的 capsule 轮廓、`TaperProfile` 的宽度序列、非零环绕语义），渲染器只消费轮廓数组；这样 `parity/vectors` 可以钉住几何而不依赖渲染 API。
- 选型不是「KB 已证明原生更快」，而是「KB 只对原生路径给出了 surface 绘制的能力描述」。必须用 2k 笔/10 万点/120Hz 的合成数据在实机测量输入到呈现 p95、掉帧、内存后再定。

---

## 3. 离屏缓存与增量重绘

### 3.1 InkCanvas 现有渲染契约（来自仓库源码盘点，非 KB）

`docs/superpowers/research/harmony/04-ink.md` 已记录：

- 已提交层：document 笔画 + 选区框；按 `InkStroke.Id` 缓存已完成笔迹的几何；文档变更时全量失效。
- 活动层：live strokes + shape preview；每个指针移动重建几何，不缓存。
- 覆盖层：`OverlayInkPresenter` 用 `CanvasRenderTarget` 做 committed 的 CPU 像素缓存，每帧只回读活动笔画包围盒，DIB 局部拷贝，`UpdateLayeredWindow` 呈现；预乘 BGRA、顶向下。
- 白板 `InkSurface`：Win2D `CanvasControl` 整场景重绘，`Invalidate()` 触发。
- 选区：`ShowSelectionOutline`，1.5 DIP 边线，6/7 半径手柄，命中半径 12。

这些是移植时要保持的**行为不变量**，不是 HarmonyOS API 事实。

### 3.2 HarmonyOS 可用的缓存积木（KB 事实）

| 积木 | KB 事实 | 能否直接做离屏缓存 |
|---|---|---|
| 两个 `Canvas` 组件叠放 | `Canvas(this.context)` 可各持一个 `CanvasRenderingContext2D`；有 `onReady` | 可分层，但清屏/重绘触发/内容保留未文档化 |
| `drawing.Path` | Path 表示几何形状；Pen 描边、Brush 填充 | 可缓存已完成笔的 Path 对象（推断），生命周期/复用/线程约束未知 |
| `drawing.Image` | 图片对象 | 是否可作为离屏目标、如何从 Canvas 导出/导入【未知】 |
| `image.PixelMap` | 解码、`scale`、`rotate`、读写像素、从 Surface 创建、打包 | 可做像素级缓存/合成，但「如何把 PixelMap 画到 Canvas/XComponent」KB 未写 |
| `RenderNode` | `setPosition`、`setBackgroundColor`、`appendChild` | 无绘制命令，不能单独做墨迹缓存 |
| NDK Drawing | 支持在 surface 绘制 | 是否支持离屏 Surface/保留上一帧/局部刷新【未知】 |
| `DisplaySync` | 可变帧率控制器 | 可做节拍，不是缓存 |

### 3.3 三种可行架构（按证据强度排序）

#### 方案 A：双 Canvas 图层（纯 ArkTS，API 24 友好）

```ts
Stack() {
  // 已提交层：只在文档/选区/视图变换变化时整层重绘
  Canvas(this.committedCtx)
    .width('100%')
    .height('100%')
    .onReady(() => {
      // 遍历 document.Strokes，绘制缓存轮廓
      // 平台 API 待核实：清屏、路径构建、填充规则
    })

  // 活动层：每个指针移动清空并重绘 live strokes + shape preview
  Canvas(this.liveCtx)
    .width('100%')
    .height('100%')
    .onReady(() => {
      // 绘制当前活动笔
    })
}
```

- 优点：只使用 KB 已出现的组件与上下文类型；API 24 可用性最高。
- 风险：KB 没有 `clearRect`/`fill(path)`/填充规则/Canvas 内容是否保留；如果只能 `onReady` 全量绘制，活动层每帧整屏重绘，性能不可接受。必须先验证「如何触发重绘、如何只清活动层」。
- 对可变宽度笔：每笔轮廓是一组 capsule 并集，需要非零环绕填充；Canvas 是否支持填充规则【未知】。若不支持，需要把 capsule 并集在 CPU 三角化/栅格化，成本高。

#### 方案 B：XComponent surface + NDK Drawing（目标性能方案）

```ts
XComponent({ id: 'ink', type: 'surface', controller: this.xc })
  .width('100%')
  .height('100%')
  .onLoad((ctx) => {
    // 平台 API 待核实：
    // 1. 从 XComponent 取 native window/surface
    // 2. 初始化 OH_Drawing_Canvas / Pen / Brush / Path
    // 3. 已完成笔迹绘制到后台缓冲，活动笔绘制到前景层
    // 4. 输入事件经 XComponent 的触屏接口进入 InkController
  })
```

- 优点：KB 明确 NDK Drawing 支持 surface 绘制；XComponent 提供 surface 与触屏事件；适合把「每笔 Path 只构建一次」和「脏区重绘」下沉到原生。
- 风险：KB 没有 surface 生命周期、尺寸变化、缓冲交换、脏区、线程模型、与 ArkTS 状态同步的 API；需要 C++ 侧自建渲染循环并处理 `DisplaySync` 或 vsync。
- 对可变宽度笔：原生 Path 填充规则/抗锯齿/半透明合成是否与 Win2D 的 Winding 语义一致，必须用 golden 图像对比。【未知】

#### 方案 C：PixelMap/RenderNode 缓存

- KB 的 `image` 模块支持 `PixelMap` 的缩放/旋转/像素读写/从 Surface 创建，但未提供「把 Canvas 内容导出为 PixelMap」或「把 PixelMap 绘到 XComponent」的 API。
- 可作为后续优化（把静态背景、已提交层栅格化为 PixelMap），但不能作为第一版主方案。

### 3.4 推荐的渲染器接口（平台无关）

```ts
// 纯接口，不绑定任何 KB 未证实的平台 API
interface IInkPresenter {
  attach(surfaceId: string): void
  resize(widthVp: number, heightVp: number, density: number): void
  drawCommitted(): void          // 文档/选区/变换变化时调用
  drawLive(dirty: Rect): void    // 指针移动时调用
  invalidate(rect: Rect): void   // 擦除/撤销/变换后的失效区域
  present(): void                // 提交到屏幕
  dispose(): void
}
```

- 已提交层：维护 `Map<strokeId, CachedGeometry>`；`Document.Changed` 时按 04-ink 的规则全量失效（注意克隆笔画复用同一 Id 的缓存风险，建议改为内容哈希或版本号）。
- 活动层：每帧重建；只重绘 `dirty` 包围盒；抬笔时把活动笔转为已提交并清空活动层。
- 选区/手柄：画在已提交层，变换时失效。
- 帧调度：`DisplaySync` 可用则请求 60/120；不可用则用平台默认刷新；抬笔强制 present 最后一帧。
- 尺寸/旋转/折叠/DPI 变化：必须重建缓冲并重算密度。Windows 现有实现按主屏尺寸一次性创建且不处理 DPI 变化（04-ink 已指出）；HarmonyOS 侧要补 `display.on('change')` / 窗口 resize 路径。KB 只记录 `display.on('change')`，没有窗口尺寸变化事件的细节。【未知】

### 3.5 增量重绘的边界

- 可增量：新增已提交笔、活动笔、单一新选区。
- 必须全量：撤销/重做、整页变换、擦除多笔、文档加载、主题/背景切换、窗口尺寸/密度变化。
- KB 不提供「图层合成」「脏区裁剪」「离屏 Surface」的明确 API；上述边界是工程约束，不是平台承诺。【推断】

---

## 4. 窗口能力

### 4.1 主窗口与 WindowStage

**KB 明确事实**

```ts
import { window } from '@kit.ArkUI'

window.getLastWindow(getContext(this), (err, data) => {
  if (err.code) { return }
  mainWindow = data
})
```

```ts
let mainWindow: window.Window = window.findWindow('mainWindow')
mainWindow.resize(720, 1280, (err) => { /* ... */ })
```

```ts
let sysBarProps: window.SystemBarProperties = {
  statusBarColor: '#00000000',
  statusBarContentColor: '#000000',
  navigationBarColor: '#00000000',
  navigationBarContentColor: '#000000'
}
mainWindow.setWindowSystemBarProperties(sysBarProps)
mainWindow.setWindowLayoutFullScreen(true)
mainWindow.setSpecificSystemBarEnabled('status', true)
mainWindow.setSpecificSystemBarEnabled('navigation', false)
mainWindow.on('systemBarTintChange', (data) => { /* ... */ })
```

来源：`system/references/basic-services.md:13-72`。

- WindowStage 生命周期：`onWindowStageCreate` → `windowStage.loadContent('pages/Index', cb)` → `windowStage.on('windowStageEvent', cb)`；`onWindowStageDestroy`。来源：`system/references/basic-services.md:74-101`、`dev/references/app-framework.md:25-58`。
- UIAbility 启动模式：singleton / multiton / specified。来源：`dev/references/app-framework.md:60-67`。
- `module.json5`：`name/type/srcEntry/description/mainElement/deviceTypes/deliveryWithInstall/installationFree/pages`；`abilities` 含 `name/srcEntry/icon/label/startWindowIcon/startWindowBackground/exported/launchType`。来源：`dev/references/app-framework.md:117-167`。
- API 14+ 若自建 `AbilityInfo`，需新增必填 `orientationId`。来源：`dev/references/app-framework.md:169-186`。
- 线程模型：主线程负责 UI 绘制、生命周期、ArkTS 引擎实例；TaskPool 用于计算；Worker 用于长任务。来源：`dev/references/app-framework.md:233-258`、`dev/SKILL.md:120`。
- **未记录**：`setWindowTopmost`、窗口置顶、窗口透明背景/alpha、窗口位置、窗口尺寸约束、窗口阴影/圆角、窗口焦点、窗口 Z 序（26.0.1 Beta1 才有 Z 序参数）、全屏 API（只有 layout full screen）。【未知】
- 起始 API 版本：`basic-services.md` 头部标注兼容 API 14+；单项 API 起始版本未给出。【未知】

**对 InkCanvas 的含义**

- 白板/全屏批注应使用「本应用主窗口 + 沉浸式」：`setWindowLayoutFullScreen(true)` + 透明系统栏 + 隐藏导航栏；设计规范还要求沉浸页面超过 2 秒自动隐藏导航条（`hmi-system-features.md:72-77`）。
- 需要保持屏幕常亮时，KB 记录了 `brightness.setKeepScreenOn(true, cb)`（`basic-services.md:166-182`），可用于课堂场景。
- 主窗口尺寸变化/旋转时，必须重建渲染缓冲；否则会重演 Windows 侧「固定尺寸不处理 DPI」的问题。

### 4.2 子窗、闪控窗、浮动工具栏

**KB 明确事实**

- 26.0.0 Beta1：「闪控窗：窗口管理新增闪控窗能力，悬浮在桌面/应用界面上的小型窗口。」来源：根 `SKILL.md:518`。
- 26.0.0 Beta2：「窗口管理新增支持设置主窗或子窗支持的窗口模式；闪控球新增销毁事件监听。」来源：根 `SKILL.md:407`。
- 26.0.0 Release：「窗口管理新增 C API，用于获取指定窗口注册的多模按键/鼠标/触摸事件的过滤函数」「画中画自动启动设置」「查询当前窗口是否处于指定的窗口姿态模式（当前支持识别桌面模式）+ 姿态改变事件监听」。来源：根 `SKILL.md:371-373`。
- 26.0.1 Beta1：「窗口管理新增窗口焦点状态 `WindowFocusState`、调整主窗 Z 序时使用的位置参数 `WindowPositionParams`。」来源：根 `SKILL.md:296`、`dev/SKILL.md:365`。**注意：这是 26.0.1 Beta1，不在 26.0.0 Release，且 Beta 版应用不能上架。**
- `OverlayManager`：26.0.0 Beta2「`UIContext` 的 `OverlayManager` 新增按层级配置浮层能力」。来源：根 `SKILL.md:404`。KB 没有方法签名、没有 API 24 记录。
- 设计规范只把「分屏、悬浮窗」列为系统多窗口交互（`hmi-system-features.md:65`），把「竖向悬浮窗、上下分屏、自由窗口、左右分屏」列为应用需要适配的形态（`ux-best-practices.md:43-48`），没有第三方创建悬浮窗的 API。

**回答「闪控窗/子窗能否承载浮动工具栏？」**

- **闪控窗**：概念上「悬浮在桌面/应用界面上的小型窗口」非常接近浮动工具栏，但：
  - 只有 API 26（26.0.0 Beta1 起）；
  - KB 没有创建/配置 API、没有尺寸限制、没有输入穿透/焦点语义、没有第三方应用资格说明、没有权限；
  - 它是「小型窗口」，不是全屏覆盖层；
  - 因此 **不能作为 API 24/26 的既定载体**；只能作为 API 26 的候选，需官方文档 + 实机验证。【未知】
- **子窗**：KB 只证明「主窗/子窗」概念存在（26.0.0 Beta2 的窗口模式设置），**没有创建子窗的 API**。不能据此设计浮动工具栏。【未知】
- **应用内浮层**：
  - API 26：`OverlayManager` 按层级配置浮层，适合工具栏、弹出菜单、更多面板；但 KB 无签名，需核对。
  - API 24：KB 未记录 OverlayManager；稳妥方案是页面 `Stack` 内绝对定位浮层，或用 `Popup`/`bindPopup` 等 ArkUI 弹窗组件（这些组件 KB 在 API 26 章节提到 `PopupV2`，但基础 Popup 用法未在本次范围展开）。
- **结论（推断）**：浮动工具栏第一版应做「主窗口内浮层」，跨版本一致；API 26 可选用 OverlayManager 提升层级管理；闪控窗/子窗留作后续实验，不作为验收依赖。

### 4.3 PC/2in1 自由窗口与桌面模式

**KB 明确事实**

- 设计规范：
  - 平板及更宽设备：适配自由窗口、左右分屏、分屏比例自由调节；平板及更宽设备需做基础鼠标、键盘适配。来源：`ux-best-practices.md:43-58`。
  - 手机/折叠屏折叠态：竖向悬浮窗、上下分屏；折叠屏展开态额外适配上下分屏。来源：`ux-best-practices.md:43-48`。
  - 宽屏导航用侧边页签/侧边栏；8 栅格以上支持分栏。来源：`ux-best-practices.md:27-33,99-104`。
- API 26 Release：窗口姿态模式查询与监听，当前支持识别「桌面模式」。来源：根 `SKILL.md:373`、`dev/SKILL.md:395`。
- API 26 Beta2：设置主窗或子窗支持的窗口模式。来源：根 `SKILL.md:407`。
- API 26 Release：窗口管理 C API 支持多模按键/鼠标/触摸事件过滤、画中画自动启动。来源：根 `SKILL.md:371-372`。
- API 26.0.1 Beta1：`WindowFocusState`、`WindowPositionParams`（主窗 Z 序位置参数），不在 26.0.0。【不可作为基线】
- Desktop Extension Kit：
  - 系统级统一入口，应用启动时或运行中可接入状态栏和快捷栏；
  - 仅支持中国境内（港澳台除外）；
  - 仅适用于 PC/2in1；
  - 不支持模拟器；
  - API 参考 `@ohos.desktopExtension`。来源：`dev/references/desktop-extension-kit.md:1-42`。
  - API 26 新增：快捷栏能力（查询接入、设置图标和进度条）、状态栏图标悬浮回调。来源：`dev/SKILL.md:413`。
- **未记录**：进入/退出自由窗口的 API、窗口拖拽/缩放事件、窗口位置/大小约束、桌面模式的具体判定 API 名与签名、外接显示器坐标语义。【未知】

**对 InkCanvas 的含义（推断）**

- 不要按设备类型写死布局；用窗口宽度断点（xs/sm/md/lg/xl）和 `ContainerReader`（API 26 新增，按容器尺寸而非窗口尺寸）做响应式。API 24 只能用断点 + 窗口尺寸。
- PC/2in1 上可使用 `HdsNavigation` 的 SPLIT 分栏、`HdsSidebar` 侧边栏承载设置/菜单；浮动工具栏仍应限制在主窗口内。
- 桌面模式可用于调整工具栏位置/面板形态（例如靠边停靠），但 API 24 没有姿态查询，需降级为「按窗口宽高判断」。
- `deviceTypes` 需包含 phone/tablet/2in1；KB 只示范 phone/tablet，2in1 的 `deviceType` 字符串值【未知】（项目探针 H9 已用 2in1 工程验证构建，但那是工程配置，不是运行时值）。
- Desktop Extension Kit 可作为 PC/2in1 的快捷入口/状态栏入口，但不能做覆盖画布；且中国境外不可用，不能作为核心功能。

### 4.4 沉浸式与系统栏

- KB 明确：`setWindowLayoutFullScreen(true)`、`setWindowSystemBarProperties`、`setSpecificSystemBarEnabled`、`systemBarTintChange`。
- 设计规范：
  - 沉浸式页面（全屏视频、游戏、阅读）超过 2 秒自动隐藏导航条；底部上滑/顶部下滑恢复。
  - 底部页签、工具栏、文本需自动抬高避让导航条；核心内容不要被挖孔遮挡；避免避让挖孔导致左右留白不一致。
  - 来源：`hmi-system-features.md:72-77`、`ux-best-practices.md:27-41`。
- **未记录**：如何捕获/排除系统返回手势、如何隐藏状态栏图标、如何设置窗口背景透明、如何做全屏手势区域。 【未知】
- 对白板：沉浸式全屏是可行的；但「覆盖其他应用」不是沉浸式能解决的，沉浸式只影响本应用窗口。

### 4.5 置顶与覆盖其他应用（重点否定结论）

**问题**：第三方应用能否做覆盖其他应用的全屏透明画笔？

**KB 证据链**

1. **窗口 API 全部是本应用窗口**：`getLastWindow` 获取当前应用窗口；`findWindow('mainWindow')` 按名称找本应用窗口；`resize`、`setWindowLayoutFullScreen`、`setWindowSystemBarProperties`、`setSpecificSystemBarEnabled` 都是本窗口属性。WindowStage 由本应用 UIAbility 创建并 `loadContent`。没有任何跨应用窗口的创建/枚举/挂载 API。来源：`system/references/basic-services.md:13-101`、`dev/references/app-framework.md:25-58`。
2. **权限清单没有覆盖/悬浮权限**：KB 权限表只列 normal 的 system_grant 与 user_grant；APL 表明确：normal 可直接申请，system_basic 需申请签名证书，system_core 仅系统应用可用。没有列出任何 `SYSTEM_ALERT_WINDOW` 类或悬浮窗类权限。来源：`dev/references/permissions.md:5-20,24-66`。
3. **唯一的系统级入口是 Desktop Extension Kit，且只做状态栏/快捷栏**：仅 PC/2in1、仅中国境内、不支持模拟器、API 为 `@ohos.desktopExtension`；没有全屏画布/覆盖层能力。来源：`dev/references/desktop-extension-kit.md:13-42`。
4. **闪控窗是「小型窗口」，不是全屏透明画布**：26.0.0 Beta1 新增，且 KB 无 API/权限/第三方资格。来源：根 `SKILL.md:518`。
5. **设计文档中的悬浮窗/分屏是系统多窗口形态**：`hmi-system-features.md:65` 把「分屏、悬浮窗」列为系统特性；`ux-best-practices.md:43-48` 要求应用适配这些形态，而不是让应用创建跨应用覆盖层。
6. **NDK 不提供绕过窗口权限的通道**：NDK 文档明确「Native 代码受系统安全沙箱约束」。来源：`dev/references/ndk-development.md:68-70`。

**结论**

- **按 KB 现有文档，第三方应用不能做覆盖其他应用的全屏透明画笔。** 这不是「没找到示例」，而是：
  - 窗口模型是应用私有窗口；
  - 权限模型没有给第三方覆盖权限；
  - 唯一系统级接入 Kit 明确限定为状态栏/快捷栏，且设备/地域受限；
  - 闪控窗是小窗口且资格未知。
- **仍需官方确认**：官方窗口 API 参考可能存在 KB 未收录的 `window` 子模块/系统应用接口；需查 `@ohos.window` 全量 API 与权限清单，并实机验证。即使存在，也可能要求 system_basic/system_core APL 或特殊签名，GPL 第三方分发无法满足。
- **对产品的直接影响**：
  - Windows 版的「透明置顶覆盖层 + PowerPoint/WPS 上批注」无法 1:1 移植；
  - HarmonyOS 没有 COM/`Process.Start`，PPT 互操作（`PptInteropService`）整体不可用（06-features 已指出 `.exe`/COM 不可移植）；
  - 需要重新设计为：应用内白板/课件渲染 + 批注；或分屏（系统多窗口）中并排使用 PPT 应用与本应用；或截屏/录屏后在本应用内批注。截屏权限与实时性 KB 未提供，需单独研究。

### 4.6 系统材质与浮层层级

**KB 明确事实**

- API 23（6.1.0）：
  - HDS 组件支持 `systemMaterialEffect`，系统根据设备算力自动适配材质效果；示例 `systemMaterialEffect: 'AUTO'`。
  - 手动控制：`hdsMaterial.getSystemMaterialTypes()`，`types.includes('IMMERSIVE')`，可选 `EXQUISITE / GENTLE / SMOOTH`。
  - `HdsMaterial` 组件：`materialType`（示例 `BLUR`）、`materialLevel`（示例 `LEVEL_2`）、`cornerRadius`。
  - 来源：`dev/references/ui-development.md:140-157`、`dev/references/ui-design-kit.md:286-312`。
- API 26（26.0.0）：
  - Beta1：所有支持通用属性的组件支持 `systemMaterial`；Chip 支持 `backgroundSystemMaterial` / `activatedBackgroundSystemMaterial`；Tips/Toast/对话框/操作菜单/自定义弹窗/半模态/Popup 支持系统材质。来源：根 `SKILL.md:508-510`。
  - Beta2：`ChipGroup/SelectionMenu/Navigation/DatePickerDialog` 新增 `systemMaterial`；C API 新增沉浸式材质类型与 API 声明。来源：根 `SKILL.md:402-403`。
  - Release：系统材质效果新增材质等级枚举（表示设备算力等级）；提供获取全局材质等级、判断设备是否支持沉浸式系统材质 `ImmersiveMaterial` 的接口。来源：根 `SKILL.md:370`、`dev/SKILL.md:395`。
  - HdsColorPicker 新增（API 26）。来源：`dev/references/ui-design-kit.md:314-342`。
- API 14+ 通用模糊：`.backdropBlur(20)`、`.foregroundBlurStyle(BlurStyle.Background)`。来源：`dev/references/ui-development.md:320-326`。
- **未记录**：材质等级枚举取值、`getGlobalMaterialLevel` 之类的方法名、`ImmersiveMaterial` 的判断方法名、API 24 上 `systemMaterial` 的降级行为。【未知】

**对 InkCanvas 的含义**

- API 24：使用 HDS 的 `systemMaterialEffect` / `HdsMaterial`（API 23+）或 `backdropBlur`/`foregroundBlurStyle` 做浮条与面板的毛玻璃。
- API 26：可升级到通用 `systemMaterial` 与材质等级判断，避免在低算力设备启用重材质。
- 这些都应封装在 `harmony/platform` 的 compat/theme 层，业务代码只拿「材质 token」，不直接判断版本。

---

## 5. API 24（6.1.1）与 API 26（26.0.0）差异表

> 说明：API 24 = HarmonyOS 6.1.1 Release（2026-05-26）；API 23 = 6.1.0（2026-04-20）。KB 中很多基础 API 没有逐项起始版本，下表只列 KB 明确写出新增版本的条目；未写版本的基础能力统一标「KB 未给版本边界」。

| 能力 | API 24（6.1.1） | API 26（26.0.0 Release） | 门控/证据 |
|---|---|---|---|
| ArkUI Canvas / CanvasRenderingContext2D | KB 未给起始版本；示例标注兼容 API 14+ | 同左，无版本差异记录 | 需官方 API 版本核对 |
| XComponent（texture/surface/node） | KB 未给起始版本；示例标注兼容 API 14+ | 同左；NDK Drawing surface 绘制无版本差异记录 | 需官方 API 版本核对 |
| Drawing（ArkTS Pen/Brush/Path） | KB 未给起始版本 | 同左；API 26 新增文字轮廓/字体回退、OHNativeWindow 3D 元数据 C API | 需官方 API 版本核对 |
| DisplaySync / 可变帧率 | KB 未给起始版本；示例可用 | 同左 | 示例 API 名待核对 |
| FrameNode / RenderNode | KB 未给起始版本 | 同左 | 需官方 API 版本核对 |
| 闪控窗（悬浮小窗） | **无**（KB 未记录） | **26.0.0 Beta1 新增**：悬浮在桌面/应用界面上的小型窗口；无 API 名/签名/权限/第三方资格 | 无 syscap 文档；需官方 API + 实机验证 |
| 闪控球销毁事件 | 无 | 26.0.0 Beta2 新增 | 同上 |
| 主窗/子窗窗口模式设置 | 无 | 26.0.0 Beta2 新增 | 同上 |
| OverlayManager 按层级浮层 | KB 未记录 | 26.0.0 Beta2：`UIContext.OverlayManager` 新增按层级配置浮层 | 无签名；API 24 可用性未知 |
| 通用 `systemMaterial` 属性 | **无**（仅 HDS `systemMaterialEffect`/`HdsMaterial`，API 23+） | 26.0.0 Beta1：所有通用属性组件支持 | 需 canIUse/版本门控 |
| 系统材质等级枚举 + ImmersiveMaterial 判断 | 无 | 26.0.0 Release 新增 | 无 syscap 文档 |
| HDS `systemMaterialEffect` / `HdsMaterial` | **有**（API 23+） | 有 | API 23 起 |
| HdsColorPicker | 无 | 有（API 26） | 26.0.1 Beta1 增加显示状态控制 |
| 窗口姿态模式查询/监听（桌面模式） | 无 | 26.0.0 Release 新增 | 无 API 名；需官方文档 |
| 窗口多模按键/鼠标/触摸事件过滤 C API | 无 | 26.0.0 Release 新增 | C API；ArkTS 是否暴露未知 |
| 画中画自动启动 C API | 无 | 26.0.0 Release 新增 | C API |
| `WindowFocusState` / `WindowPositionParams` | 无 | **26.0.0 Release 无**；仅 26.0.1 Beta1 | 不可作为基线；Beta 不能上架 |
| Pen Kit 跟手性加速 | 无 | 26.0.0 Release 新增 | 无 API 名/签名 |
| Pen Kit HandWriteComponent 基础能力 | 有 | 有；API 23 增加禁用缩放/ScrollTo/最大高度 | API 23 起 |
| 平行视界 `easy_go.json` | **有**（API 23+） | 有；API 24 增加分栏状态获取 | API 23 起 |
| HdsTabs 悬浮页签 + 迷你栏 | 有（API 23+） | 有 | API 23 起 |
| `backdropBlur` / `foregroundBlurStyle` | 有（API 14+） | 有 | API 14 起 |
| 沉浸式窗口/系统栏 | 有（basic-services 兼容 API 14+） | 有 | 单项起始版本未给 |
| 自由窗口/分屏 | 系统形态，应用需适配；无 API 记录 | 系统形态 + 姿态查询（26）+ 窗口模式设置（26） | 需官方窗口 API |
| Desktop Extension Kit 状态栏/快捷栏 | KB 未给起始版本 | API 26 加快捷栏查询/图标/进度、状态栏图标悬浮回调 | 仅 PC/2in1、中国境内、无模拟器 |
| deviceInfo/display | 有 | 有 | 无版本差异记录 |

---

## 6. 运行时门控方式

### 6.1 KB 里唯一的 canIUse 样例

- AR Engine：调用前需 `canIUse("SystemCapability.AREngine.Core")` 检查设备支持。来源：`media/references/ar-engine.md:82`。
- ArkGraphics 3D：需查询 `SystemCapability.Graphic.XEngine`。来源：`media/references/arkgraphics-3d.md:187`。
- **除此之外，KB 没有给出窗口、2D 绘图、XComponent、DisplaySync、Pen Kit、系统材质、闪控窗、OverlayManager 的 SystemCapability 字符串。** 不能猜；必须查官方 syscap 清单后写入 `parity/api-matrix.md`。

### 6.2 建议的 compat 层形态（推断）

业务代码禁止散落版本判断（CLAUDE.md）。在 `harmony/platform` 建统一能力层：

```ts
// 伪代码：实现方式待 syscap 确认
export class RenderCompat {
  static canUseNativeInkSurface(): boolean {
    // 优先 canIUse('SystemCapability.???')；未确认前返回 false
    return false
  }
  static canUseDisplaySync(): boolean {
    // 同上
    return false
  }
}

export class WindowCompat {
  static canUseLayeredOverlay(): boolean { return false }   // OverlayManager API 26
  static canUseFloatingWindow(): boolean { return false }   // 闪控窗 API 26，资格未知
  static canUseWindowPosture(): boolean { return false }    // API 26
  static canUseSubWindow(): boolean { return false }        // API 未知
}

export class MaterialCompat {
  static canUseSystemMaterial(): boolean { return false }       // API 26
  static canUseImmersiveMaterial(): boolean { return false }    // API 26
  static canUseHdsMaterial(): boolean { return true }           // API 23+
}
```

- 在 syscap 未确认前，建议「默认关闭 + 构建期 API 版本常量」双保险，但**只允许在 compat 层使用版本判断**，并记录到 `parity/api-matrix.md`。
- KB 没有记录 `deviceInfo.sdkApiVersion`；已知的 `deviceInfo.osFullName` / `systemParameter.getSync('const.ohos.fullname.version')` 是字符串，字符串比较脆弱，不建议作为唯一门控。【未知】
- 项目构建配置（来自 `00-harness-notes.md` H8/H9，不是 KB）：`compileSdkVersion` 省略以使用工具链内置 SDK；`compatibleSdkVersion: "6.1.1(24)"`；`targetSdkVersion: "26.0.0"`。`compileSdkVersion: "26.0.0"` 会被 26.0.1 工具链拒绝。
- 26.0.1 Beta1 的能力（`WindowFocusState` 等）不能作为基线：Beta 版 DevEco Studio 开发的应用不能上架（KB 多处强调，如 `graphics2d.md:61-65`、`SKILL.md:288`）。

---

## 7. 设备与屏幕信息

**KB 明确事实**

```ts
import { deviceInfo } from '@kit.BasicServicesKit'
let deviceId = deviceInfo.uuid
let deviceType = deviceInfo.deviceType
let osFullName = deviceInfo.osFullName
```

```ts
import { systemParameter } from '@kit.BasicServicesKit'
let deviceType = systemParameter.getSync('const.ohos.devicetype', 'default')
let osVersion = systemParameter.getSync('const.ohos.fullname.version', 'unknown')
```

```ts
import { display } from '@kit.ArkUI'
display.getDefaultDisplaySync()
display.getAllDisplays((err, data: Array<display.Display>) => {
  data.forEach(d => {
    console.info(`Display id: ${d.id}, width: ${d.width}, height: ${d.height}, densityDPI: ${d.densityDPI}`)
  })
})
display.on('add', (data: number) => {})
display.on('change', (data: number) => {})
display.off('add')
display.off('change')
```

来源：`system/references/device-info.md:5-13`、`system/references/basic-services.md:103-118,185-215`。

- `deviceType` 的已知取值：示例与多设备文档使用 `'phone'`、`'tablet'`。来源：`dev/references/multi-device.md:63-75`。
- **未记录**：`'2in1'` / `'pc'` 的精确取值；`Display.width/height` 的单位（px 还是 vp）；`densityPixels` / 缩放系数 API；多显示器坐标原点；折叠/旋转时的 display 变化语义。【未知】
- 对渲染：必须用 `densityDPI` 或官方密度 API 把 vp 设计尺寸换算为像素缓冲；KB 只给了 `densityDPI`，没有给出换算公式。窗口 resize 与 `display.on('change')` 都要触发渲染缓冲重建。
- 对布局：`deviceInfo.deviceType` 可作为粗分档，但主布局应基于窗口宽度断点，避免 PC 自由窗口下 deviceType 不变而窗口宽度变化导致的错配。

---

## 8. 针对 InkCanvas 的落地建议

### 8.1 分层映射（与项目六层同构）

| Windows 层 | HarmonyOS 层 | 渲染/窗口职责 |
|---|---|---|
| Core（纯域模型、笔锋、几何、撤销） | `harmony/core` | 保持纯 ArkTS/TS 子集；共享 `parity/vectors`；不依赖 Canvas/XComponent |
| Platform（Win32 窗口、指针、DPI） | `harmony/platform` | 窗口封装、compat 能力层、XComponent surface 生命周期、指针/触摸翻译、DPI/密度 |
| Ink（Win2D 渲染、OverlayInkPresenter） | `harmony/ink` | `IInkPresenter` 双后端（Canvas / XComponent+Drawing）、几何缓存、双图层、帧调度 |
| Controls（FloatingBar、InkSurface、PenPalette） | `harmony/controls` | InkSurface 组件、浮条、调色盘、图形菜单；浮层用 Stack/OverlayManager |
| Features（Board/PPT/计时/抽奖/回放） | `harmony/features` | 白板、回放、计时、抽奖；PPT 互操作需重设计 |
| App（外壳、窗口、服务） | `harmony/entry` | UIAbility、WindowStage、沉浸式、生命周期、服务装配 |

### 8.2 渲染后端策略

- 定义 `IInkPresenter`（见 §3.4），业务层只依赖接口。
- **后端 N（API 26，平板/PC）**：XComponent `surface` + NDK Drawing；已完成笔 Path 缓存；活动笔脏区重绘；`DisplaySync` 节拍；原生侧处理 surface 尺寸/旋转/重建。
- **后端 A（API 24/手机/回退）**：双 Canvas 图层；已提交层整层重绘、活动层按脏区重绘；如果清屏/填充规则 API 不可用，则退化为「活动层整屏重绘 + 限制活动笔点数/简化轮廓」。
- **后端选择**：由 `RenderCompat` 决定，不在 Features/Controls 里判断版本。
- **几何一致性**：活动笔与提交笔必须使用同一 `StrokeGeometry` 与 `TaperProfile`；`Complete()` 不改变宽度，保证提交后与最后一帧一致（04-ink 的硬约束）。渲染器只消费轮廓，不重新计算笔锋。

### 8.3 窗口/浮条策略

- **白板/全屏批注**：主窗口沉浸式（`setWindowLayoutFullScreen(true)` + 透明系统栏 + 隐藏导航栏），保持屏幕常亮；处理窗口 resize/旋转/折叠重建。
- **浮动工具栏**：
  - 第一版：主窗口内 `Stack` 浮层，绝对定位，支持拖拽/折叠；
  - API 26 可选 `OverlayManager` 分层管理（需核对签名）；
  - **不要**把「闪控窗/子窗」写入第一版验收；它们只有 API 26 且资格未知。
- **设置/取色/图形菜单**：
  - 同窗口弹窗/半模态/`Popup` 优先；避免依赖子窗；
  - 若必须独立窗口，先验证子窗 API；否则接受「菜单在主窗口内」的 UX 差异。
- **PC/2in1**：响应式断点 + `HdsNavigation` SPLIT / `HdsSidebar`；键鼠悬停/右键/滚轮要单独适配；桌面模式查询（API 26）用于工具栏停靠策略，API 24 按窗口比例降级。
- **手机**：初步版本只做单窗口白板/批注，不做跨应用覆盖；浮条可折叠为底部工具条。

### 8.4 PPT/课件批注的产品重设计（关键）

- Windows 侧依赖：PowerPoint/WPS COM 晚绑定、放映窗口句柄、截图、透明置顶覆盖层（06-features、04-ink 已盘点）。
- HarmonyOS 侧：
  - 没有 COM/`Process.Start`/`.exe`，`PptInteropService` 全部不可用；
  - 没有文档化的跨应用覆盖层（§4.5）；
  - 因此不能在「外部 PPT 放映窗口」上直接画。
- 可选路线（均需产品决策）：
  1. **应用内课件渲染**：把 PPT 导出为 PDF/图片，在本应用内翻页 + 批注；可使用 PDF Kit（KB 记录 API 26 自定义渲染/坐标转换，但本次未展开）或图片序列；墨迹与页面坐标绑定，最接近白板体验。
  2. **分屏协作**：系统左右分屏，一侧 PPT 应用、一侧 InkCanvas；用户在 InkCanvas 内独立批注，无法与 PPT 画面像素对齐。
  3. **截屏/录屏后批注**：需要屏幕捕获权限与低延迟；KB 本次范围未记录 `@ohos.screenCapture`，06-features 仅把它列为候选，**未验证**。
  4. **系统级合作**：若华为提供白板/批注系统能力或企业签名，另行评估；普通第三方分发不可依赖。
- 建议在渲染 spike 之前先做产品路线决策，否则窗口/渲染架构会返工。

### 8.5 建议的验证 spike（按优先级）

1. **S1 渲染 A/B**：同一组 2k 笔 / 10 万点 / 可变宽度数据，分别用 Canvas 双图层与 XComponent+NDK Drawing 在平板与 PC/2in1 实机跑；测输入到呈现 p95、掉帧率、内存、首帧。验收前不写「性能达标」。
2. **S2 跨应用覆盖可行性**：查官方 `@ohos.window` 全量 API 与权限清单，确认是否存在系统级 overlay/悬浮窗；在 2in1 实机尝试最小 Demo。若不可行，锁定 §8.4 路线。
3. **S3 浮层/浮条**：API 24 用 Stack 浮层，API 26 试 OverlayManager；验证拖拽、点击外部关闭、Escape、层级、输入不穿透。
4. **S4 窗口变化**：旋转、折叠、自由窗口缩放、外接显示器、DPI 变化时重建 surface/缓冲；验证不出现拉伸/模糊/坐标偏移。
5. **S5 材质降级**：API 24 HdsMaterial/backdropBlur 与 API 26 systemMaterial 的视觉一致性；低算力设备关闭重材质。
6. **S6 手写输入**：XComponent 触屏接口/ArkUI 触摸事件的压力、倾斜、历史点、预测点、笔/掌区分；与 `PalmRejector`/`TouchContactClassifier` 的字段映射。

### 8.6 建议的验收指标（提案，不是 KB）

- 几何正确性：与 `parity/vectors` 的轮廓/宽度逐点一致；自交笔非零环绕无洞；半透明荧光笔重叠只合成一次。
- 交互：抬笔到最终帧 ≤ 1 帧；连续划线无明显掉帧；双指缩放/旋转跟手。
- 稳定性：旋转/折叠/自由窗口缩放 100 次无崩溃、无缓冲泄漏；后台/前台切换后墨迹不丢。
- API 24 降级：所有 API 26 能力不可用时，白板/浮条/设置仍可用。

---

## 9. 第三方应用硬约束

1. **没有文档化的跨应用全屏透明覆盖窗 API/权限**。窗口 API 均操作本应用窗口；权限表无悬浮/覆盖类权限；`system_basic` 需签名证书，`system_core` 仅系统应用。
2. **Desktop Extension Kit 不是覆盖层**：只做状态栏/快捷栏，仅 PC/2in1，仅中国境内（港澳台除外），不支持模拟器。
3. **闪控窗/闪控球/子窗的第三方资格未文档化**：只有 API 26，不能作为 API 24 依赖；尺寸、输入穿透、权限均未知。
4. **26.0.1 Beta1 不能上架**：`WindowFocusState`/`WindowPositionParams` 等 26.0.1 能力不可作为基线；只能用 26.0.0 Release。
5. **无 .exe / COM 互操作**：PowerPoint/WPS 控制、安装器启动、外部进程注入均不可移植。
6. **NDK 受系统安全沙箱约束**：不能用 Native 代码绕过窗口/权限模型。
7. **系统材质/浮层层级/窗口姿态/窗口模式设置是 API 26 能力**：API 24 必须降级，且 API 24 无 `systemMaterial`/OverlayManager 层级浮层的 KB 记录。
8. **KB 未提供窗口/图形/手写笔的 SystemCapability 字符串**：在官方 syscap 清单确认前，不能写 `canIUse` 门控，不能声称设备支持。
9. **Canvas/XComponent/Drawing 的精确起始 API 版本与 surface 生命周期未文档化**：不能承诺「API 24 一定可用原生 surface 绘制」。
10. **设备能力不可假设**：手写笔、高刷、PC 模式、外接显示器、系统材质算力等级都可能缺失；必须能力检测 + 降级。

---

## 10. 未知 / 需实机验证清单

1. ArkUI Canvas 是否支持 `clearRect`、`Path2D`、`fill(path, fillRule)`、`save/restore`、`transform`、`drawImage`、`globalCompositeOperation`、`lineCap/lineJoin`；起始 API 版本。
2. Canvas 内容是否跨帧保留；如何在状态变化时只重绘活动层。
3. 是否存在 `OffscreenCanvas` 或等价的离屏绘制 API；起始版本与限制。
4. XComponent `onLoad(ctx)` 的参数类型、生命周期、surface 尺寸/销毁/重建语义。
5. XComponent `type:'surface'` 的完整用法；`texture` 与 `surface` 的差异、性能与输入延迟。
6. NDK Drawing 在 XComponent surface 上的初始化、缓冲交换、脏区重绘、双缓冲、线程模型。
7. NDK Drawing 的 Path 填充规则（非零环绕/奇偶）、抗锯齿、半透明合成是否与 Win2D 一致。
8. `DisplaySync` 的正式 ArkTS API 名、方法签名、起始版本；是否有 vsync/帧时间戳。
9. `drawing.Image` 能否作为离屏目标/缓存；如何与 Canvas/XComponent 互转。
10. `image.PixelMap` 如何绘制到 Canvas/XComponent；是否有硬件加速路径。
11. 第三方应用能否创建闪控窗/子窗；API 名、权限、APL 要求、尺寸/输入穿透/焦点语义。
12. 是否存在 `setWindowTopmost`/系统级 overlay/悬浮窗 API；官方 `@ohos.window` 全量能力。
13. 窗口/图形/手写笔/系统材质/OverlayManager/闪控窗的 SystemCapability 字符串。
14. 窗口姿态模式查询与监听的 API 名、签名、返回值；桌面模式的判定条件；API 24 行为。
15. 主窗/子窗窗口模式设置的 API 名与取值；API 24 行为。
16. `OverlayManager` 在 API 24 是否可用；按层级浮层的签名与层级语义。
17. 通用 `systemMaterial` 在 API 24 的降级/忽略行为；材质等级枚举取值与全局等级查询方法名。
18. `deviceInfo.deviceType` 的 PC/2in1 精确值；`display.width/height` 单位；`densityPixels`/缩放系数 API；多显示器坐标。
19. 窗口 resize/旋转/折叠/DPI 变化的通知 API 与时机；surface 重建契约。
20. XComponent/ArkUI 触摸事件的压力、倾斜、历史点、预测点、笔/掌区分、事件合并；与 `PalmRejector` 的映射。
21. Pen Kit `HandWriteComponent` 能否导出笔迹、擦除、多图层、自定义渲染；是否适合作为核心。
22. 屏幕捕获/截屏 API 的权限、延迟、是否可用于实时批注；KB 本次范围未展开。
23. 全屏沉浸下系统返回手势/导航条自动隐藏的精确行为与可配置性。
24. 自由窗口/分屏下应用窗口尺寸变化事件与最小尺寸约束。
25. 系统材质/模糊在大屏、低算力设备上的性能与降级策略。

---

## 11. 参考

### KB 文件

- `D:\InkCanvas\.claude\skills\harmony-os\media\references\graphics2d.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\ui-development.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\app-framework.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\desktop-extension-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\ui-design-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\system\references\basic-services.md`
- `D:\InkCanvas\.claude\skills\harmony-os\samples\references\graphics.md`
- `D:\InkCanvas\.claude\skills\harmony-os\templates\references\arkui-components.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\ndk-development.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\pen-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\permissions.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\multi-device.md`
- `D:\InkCanvas\.claude\skills\harmony-os\system\references\device-info.md`
- `D:\InkCanvas\.claude\skills\harmony-os\design\references\ux-best-practices.md`
- `D:\InkCanvas\.claude\skills\harmony-os\design\references\hmi-system-features.md`
- `D:\InkCanvas\.claude\skills\harmony-os\SKILL.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\SKILL.md`

### KB 内官方链接（未在本次运行中访问）

- ArkGraphics 2D 简介：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/arkgraphics2d-introduction
- 图形绘制与显示开发概述：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/graphic-drawing-overview
- Drawing 模块 API 参考：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/arkts-apis-graphics-drawing
- 可变帧率简介：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/displaysync-overview
- Canvas 开发：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/canvas-2d
- DisplaySync 指南：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/displaysync-guide
- 窗口管理开发指导：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/application-window-stage-V5
- Desktop Extension Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/statusbar-extension-introduction
- UI Design Kit 开发指南：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/ui-design-kit-overview-0000001820880929
- 多窗口交互设计：https://developer.huawei.com/consumer/cn/doc/design-guides/system-features-multi-window-interaction-0000001795392917
- 手写组件开发：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/pen-handwritecomponent

---

## 12. 验证边界声明

- 本文只做 vendored KB 的静态提取与工程推断；**未安装 HarmonyOS SDK、未编译 HAP、未实机运行、未做性能测量**。
- KB 是上游知识库的 vendored 副本，个别示例可能是片段/伪代码（例如 `XComponentController.create()` 未赋值、`displaySync.createDisplaySyncScene()` 未在 API 表出现）。这些均按原文标注为「待官方核对」，未当作可编译签名。
- 文中所有「不能/不可行」均限定为「KB 现有证据不支持」；最终结论需官方 API 参考与实机验证。尤其 §4.5 的跨应用覆盖结论，必须再做一次官方窗口 API/权限清单核对。
- 未修改除本文件外的任何文件。
