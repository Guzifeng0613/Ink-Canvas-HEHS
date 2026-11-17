# 09 — HarmonyOS 笔输入、触摸与多模输入能力研究

> 报告日期：2026-11-18
> 项目：InkCanvas（WinUI 3 → HarmonyOS ArkTS 移植）
> 基线：API 26.0.0 Release（HarmonyOS 26.0.0 / OpenHarmony 7.0）为主，API 24（HarmonyOS 6.1.1）向下兼容
> 设备优先级：平板（完整）> PC/2in1 桌面触摸（完整）> 手机（初步）
> 性质：只读研究；本报告未编译 ArkTS、未在 HarmonyOS 设备上运行。所有运行期行为均标注「需实机验证」。

---

## 0. 结论速览

1. **ArkUI 能拿到压感、倾斜和接触面积**：`onTouch` 的 `TouchEvent` 继承 `BaseEvent`，提供 `pressure`（归一化 [0,1]）、`tiltX/tiltY`（度）、`source/sourceTool`、`timestamp`（ns）；每个 `TouchObject` 还提供 `pressure`（[0,65535)）、`width/height`（vp，手指接触面积）、`pressedTime`（ns）。这些字段自 API 9/15 起可用，API 24 与 API 26 都具备。
2. **笔尖类型在 ArkUI TS 层是粗粒度的**：`SourceTool` 只有 `Finger / Pen / MOUSE / TOUCHPAD / JOYSTICK`，没有橡皮擦/铅笔/笔刷。要区分笔尾橡皮（`RUBBER`）、铅笔、笔刷等，必须走 **XComponent 原生触控接口** 的 `OH_NativeXComponent_GetTouchPointToolType()`。
3. **Pen Kit 的核心是成品手写画布 `HandwriteComponent`**（注意 SDK 实际拼写是 `HandwriteComponent`，不是知识库写的 `HandWriteComponent`）。它是带工具栏、画笔类型、保存/加载、缩放/长画布滚动的完整组件；**不提供逐点笔迹回调，也不导出公共笔迹数据模型**。它适合“开箱即用笔记”，不适合直接驱动 InkCanvas 自己的 `.icink` / 笔锋 / 撤销模型。
4. **Pen Kit 的可用算法接口**：`PointPredictor`（点预测，API 12+）、`InstantShapeGenerator`（图形识别，API 12+）、`stylusInteraction`（捏笔/双击，API 19+；笔身加速度计/陀螺仪，API 26+）。它们接受 ArkUI `TouchEvent`，可用于自绘管线。
5. **API 26 的“跟手性加速”有两套**：
   - ArkTS：`StylusFrameBoost.forceRefreshOneFrame(action: number): number`，`action` 0=down、2=move、1=up、3=cancel；权限 `ohos.permission.STYLUS_FRAME_BOOST`；`@since 26.0.0`。
   - Native：`HMS_HandWrite_SetRefreshDelayOff(const char* xcomponentId, const bool enable)`，面向自绘 XComponent；同权限；`@since 26.0.0`。
   - 另有 `HMS_HandWrite_GetPredictPoint(...)`，`@since 6.0.0(20)`。
6. **手掌误触抑制：平台只提供“笔优先时取消手指触点”的行为，没有公开的手掌分类/屏蔽 API**。`TouchType.Cancel` 的 SDK 注释明确写到“手写笔操作发生时，手指触摸收到 Cancel”。除此之外，按接触面积判定手掌、笔在范围内优先、手掌擦除等必须应用层自己实现。InkCanvas 已有 `PalmRejector` / `TouchContactClassifier` 纯逻辑，可 1:1 移植。
7. **接触面积可得**：ArkUI `TouchObject.width/height`（vp，API 15+）是最适合三方应用的来源；`HistoricalPoint.size`（API 10+，单位未注明）、XComponent 原生 `size`（API 8+）、Input Kit 系统 `touchEvent.width/height`（px，API 9+，但三方拿不到事件源）也存在。特殊屏分档移植可行，但单位与量纲必须实机标定。
8. **鼠标/键盘在 PC/2in1 可用**：ArkUI 有 `onMouse`、`onHover`、`onHoverMove`、`onKeyEvent`、`onKeyPreIme`、`onKeyEventDispatch`、`onAxisEvent`；API 26 新增窗口级 `UIContext.addLocalInputEventMonitor()`（仅当前窗口，拦 down/up/key，不含 move）。键事件要求组件获得焦点。
9. **普通三方应用拿不到全局输入**：`@ohos.multimodalInput.inputMonitor` 不在公开 SDK；`inputEventClient` 注入键盘/鼠标/触摸需要 `ohos.permission.CONTROL_DEVICE`（system_basic、manual_settings、仅 2in1、API 26）。回放/自动化不能依赖输入注入。
10. **最大的产品级硬约束：跨应用全屏透明覆盖层不可用**。`window.WindowType.TYPE_FLOAT`（全局悬浮窗）创建需要 `ohos.permission.SYSTEM_FLOAT_WINDOW`，该权限为 system_basic，普通三方应用默认不可获得。InkCanvas 在 Windows 上“覆盖在 PPT/其他应用上批注”的核心形态，在 HarmonyOS 三方应用上需要重新定义（应用内打开、分屏、系统签名/企业签名，或等待平台开放）。这是移植设计必须先决策的问题。

---

## 1. 证据分级与知识库覆盖度

### 1.1 证据分级

| 级别 | 来源 | 用法 |
|---|---|---|
| A | `D:\InkCanvas\.claude\skills\harmony-os\` 下 vendored 知识库原文 | 直接引用；标注文件名 |
| B | 本机 HarmonyOS SDK 26.0.1 Beta1（API 26，`E:\EcoStudioCLI\command-line-tools\sdk\default`）类型声明/权限定义 | 只读核对签名、`@since`、`@syscap`、权限等级；**不属于知识库**，需以 API 26.0.0 Release 文档与实机复核 |
| C | 由 A/B 事实导出的工程建议 | 明确写“建议/推断” |
| U | 知识库与 SDK 都未写明，或需要运行期确认 | 标注“未知/需实机验证”，不臆造 |

> 说明：任务要求“知识库未写明的一律标注未知/需实机验证”。本报告严格遵守；对知识库缺失但本机 SDK 可核对的事实，单独标为 B 级，并在每处注明“知识库未写明，SDK 核对结果如下”，不把它说成知识库原文。

### 1.2 指定 references 的实际覆盖

| 文件 | 行数 | 实际内容 | 对笔输入问题的覆盖 |
|---|---:|---|---|
| `dev/references/pen-kit.md` | 58 | Pen Kit 概述、`HandWriteComponent` 代码片段、API 23 三项新增、Kit 导入、官方链接 | 只有概念与少量属性；**没有压感/倾斜/笔尖/跟手性 API 签名，没有 SystemCapability** |
| `system/references/input-kit.md` | 53 | Input Kit 概述、基础事件、设备列表/光标、API 参考表、26.0.1 Beta1 两条 | 只有模块名与能力描述；**没有事件字段、权限、签名** |
| `system/references/input-multimodal-kit.md` | 84 | Input Kit + Multimodal Awareness 合并；列出 `@ohos.multimodalInput.*` 模块 | 只有模块清单；Multimodal Awareness 讲的是设备静止/活动识别，不是手写笔 |
| `dev/references/ui-development.md` | 394 | ArkUI 声明式开发、版本特性、布局、动画、路由、像素单位 | **完全没有 onTouch / TouchEvent / sourceType / 压感 / 倾斜 / 悬停 / 手势 API 细节**；VENDOR.md 声称它覆盖触摸事件，与实际不符 |
| `dev/references/arkts.md` | 94 | ArkTS 语言、状态管理、TaskPool/Worker/Sendable、API 26 并发变更 | 只有并发模型；没有输入管线、线程回调、事件频率说明 |

### 1.3 知识库中可用的旁证

| 文件 | 可用事实 |
|---|---|
| `dev/references/ndk-development.md` | “XComponent：ArkUI XComponent 组件，提供 surface 与触屏事件等接口”；“XComponent API：surface 与触屏事件接口” |
| `samples/references/graphics.md` | `XComponent({ type: 'texture', id: 'canvas' })`、`Canvas(this.context)`、`CanvasRenderingContext2D`、`onReady`；DisplaySync 示例（但函数名有误，见 1.4） |
| `design/references/hmi-system-features.md` | 输入方式分类：直接交互=触屏手指/手写笔；间接交互=鼠标/触摸板/键盘等；“交互事件归一”把不同设备归一到标准事件 |
| `design/references/multi-device.md` | 平板“支持手写笔压感”；二合一“触摸+键鼠”；键鼠适配=悬停状态、快捷键、右键菜单；触控热区 ≥44×44 dp |
| `system/references/test-kit.md` | UITest 支持“模拟触摸屏操作”“模拟键鼠操作”“模拟触摸板操作”“模拟手写笔操作”；但测试脚本需连硬件设备，UI 测试不支持并发 |
| `system/references/device-info.md` | `deviceInfo.uuid/deviceType/osFullName` |
| `dev/references/multi-device.md` | `deviceInfo.deviceType === 'phone' / 'tablet'` 的能力分流写法 |

### 1.4 知识库中的事实性瑕疵（移植时不要照抄）

1. **组件名大小写错误**：知识库写 `import { HandWriteComponent } from '@kit.PenKit'`；本机 SDK 实际声明是 `HandwriteComponent`（小写 w），Kit 描述文件为 `@kit.Penkit`（小写 k）。导入路径与符号名必须以编译验证为准。
2. **组件参数错误**：知识库示例 `HandWriteComponent({ canvasConfig: { width, height, backgroundColor } })` 与本机 SDK 不符。SDK 中 `HandwriteComponent` 的必填入口是 `handwriteController: HandwriteController`，另有 `onInit/onScale/onDidScroll/defaultPenType/defaultPenInfo/widthRatio/heightRatio/maxCanvasHeight/scaleDisabled/hiddenTools`；没有 `canvasConfig`。
3. **DisplaySync 示例函数名错误**：知识库 `samples/references/graphics.md` 用 `displaySync.createDisplaySyncScene()` 与 `setTargetFrameRateRange(...)`；本机 SDK 实际为 `displaySync.create(): DisplaySync` 与 `setExpectedFrameRateRange(ExpectedFrameRateRange)`（`@since 11`）。不要照抄。
4. **缺 SystemCapability**：知识库的 pen-kit/input-kit 均未给出 `SystemCapability.*` 字符串；本报告中的字符串来自本机 SDK（B 级）。

---

## 2. ArkUI 输入事件事实（B 级：本机 SDK 26.0.1 Beta1 核对；知识库未收录）

### 2.1 事件入口签名

`CommonMethod`（`E:\EcoStudioCLI\...\openharmony\ets\component\common.d.ts`）中与笔输入相关的声明：

```typescript
onTouch(event: (event: TouchEvent) => void): T;                 // @since 7
onHover(event: (isHover: boolean, event: HoverEvent) => void): T; // @since 8
onHoverMove(event: Callback<HoverEvent>): T;                    // @since 15，手写笔悬停
onMouse(event: (event: MouseEvent) => void): T;                 // @since 8
onKeyEvent(event: (event: KeyEvent) => void): T;                // @since 7
onKeyEvent(event: Callback<KeyEvent, boolean>): T;              // @since 15，返回 true 消费
onKeyPreIme(event: Callback<KeyEvent, boolean>): T;             // @since 12
onKeyEventDispatch(event: Callback<KeyEvent, boolean>): T;      // @since 15
onAxisEvent(event: Callback<AxisEvent>): T;                     // @since 17，滚轮/触控板
```

- `onTouch`：手指或手写笔触发；默认冒泡，可用 `TouchEvent.stopPropagation()` 阻止。**鼠标左键事件会被转换成触摸事件，同样触发 `onTouch`**（SDK 原文），所以不能只靠 `onTouch` 判断“这是手指”。
- `onKeyEvent`：**组件获得焦点后**才触发；`onKeyEvent` 返回 `true` 等价于消费（阻止冒泡）。浮动栏/覆盖层若没有焦点，收不到键事件（U：需要实测浮动窗口焦点策略）。
- `onHover`：鼠标指针或手写笔进入/离开组件；`onHoverMove` 专门用于手写笔悬停并给出位置（API 15+）。
- `onAxisEvent`：滚轮/触控板轴事件，`AxisEvent.action`、`scrollStep`、`axisHorizontal/axisVertical`、`axisPinch`（API 21+）。

### 2.2 `TouchEvent` / `BaseEvent` / `TouchObject` / `HistoricalPoint` 字段

#### `BaseEvent`（`@since 8`，部分字段更晚）

| 字段 | 类型/单位 | 起始 API | 说明 |
|---|---|---:|---|
| `target` | `EventTarget` | 8 | 触发元素 |
| `timestamp` | number，ns | 8 | 事件触发时间，距系统启动的纳秒数 |
| `source` | `SourceType` | 8 | 输入设备大类：`Unknown / Mouse / TouchScreen / KEY(22+) / JOYSTICK(22+)` |
| `sourceTool` | `SourceTool` | 9 | 工具类型：`Unknown / Finger / Pen / MOUSE(12+) / TOUCHPAD(12+) / JOYSTICK(12+)` |
| `pressure` | number，[0,1] | 9 | 按压压力；SDK 注明某些设备可能 >1 |
| `tiltX` | number，deg | 9 | 笔在设备平面投影与 x 轴夹角 |
| `tiltY` | number，deg | 9 | 笔在设备平面投影与 y 轴夹角 |
| `rollAngle` | number，deg，[-179,179] | 17 | 笔身绕长轴旋转；负值以 65357–65535 表示，需换算 |
| `axisHorizontal` / `axisVertical` | number | 12 | 滚轮/双指触控板滚动值；Shift+滚轮时水平为 0、值在垂直 |
| `axisPinch` | number | 21 | 双指捏合比例（触控板/Ctrl+滚轮） |
| `deviceId` | number | 12 | 输入设备 ID |
| `targetDisplayId` | number | 15 | 事件所在屏幕 ID |
| `getModifierKeyState?(keys)` | boolean | 12 | Ctrl/Alt/Shift 状态；**手写笔场景不支持** |

#### `TouchObject`（每个触点）

| 字段 | 类型/单位 | 起始 API | 说明 |
|---|---|---:|---|
| `type` | `TouchType` | 7 | `Down / Up / Move / Cancel`（另有 20+ 的无障碍 HOVER_*） |
| `id` | number | 7 | 触点唯一 ID（多指区分） |
| `x` / `y` | number，vp | 7 | 相对响应组件左上角 |
| `windowX` / `windowY` | number，vp | 10 | 相对应用窗口 |
| `displayX` / `displayY` | number，vp | 10 | 相对应用屏幕 |
| `globalDisplayX` / `globalDisplayY` | number，vp | 20 | 全局坐标系（多屏） |
| `screenX` / `screenY` | number，vp | 7（10 弃用） | 旧字段，改用 windowX/Y |
| `hand` | `InteractionHand` | 15 | 左手/右手/未指定 |
| `pressedTime` | number，ns | 15 | 按下时间 |
| `pressure` | number，[0,65535) | 15 | 触点压力原始值；与 `BaseEvent.pressure` 的量纲不同 |
| `width` / `height` | number，vp | 15 | **手指接触区域宽/高**；可选，可能 undefined 或 0 |
| `getCurrentLocalPosition?()` | `Coordinate2D` | 26.0.0 | 组件实时位置下的本地坐标；API 24 没有 |

#### `HistoricalPoint`（同一帧内的历史采样）

| 字段 | 类型/单位 | 起始 API | 说明 |
|---|---|---:|---|
| `touchObject` | `TouchObject` | 10 | 该历史点的完整触点信息 |
| `size` | number，单位未注明 | 10 | 手指与屏幕接触面积大小，默认 0 |
| `force` | number，[0,65535) | 10 | 压力原始值 |
| `timestamp` | number，ns | 10 | 距系统启动的纳秒数 |

`TouchEvent.getHistoricalPoints(): Array<HistoricalPoint>`（`@since 10`，只能在 `onTouch` 内调用）返回当前帧所有历史点。SDK 说明：

- `onTouch` 通常每帧调用一次；一帧内收到多个 `TouchEvent` 时，最后一个点通过 `onTouch` 返回，其余进入历史点。
- 非事件注入场景下，`changedTouches` 是**按屏幕刷新率重采样**的点，`touches` 是**按设备上报率**的点，两者可能不同。

> 这对 InkCanvas 很重要：要高采样率笔迹，不能只看 `changedTouches`，必须把 `getHistoricalPoints()` 按时间顺序并入笔迹；同时注意最后一帧的重复点。

#### `TouchEvent`

| 字段/方法 | 说明 |
|---|---|
| `type: TouchType` | `Down / Up / Move / Cancel` |
| `touches: TouchObject[]` | 所有当前触点（多指）；使用前判空 |
| `changedTouches: TouchObject[]` | 触发本事件的触点；使用前判空 |
| `stopPropagation()` | 阻止冒泡 |
| `preventDefault()` | 仅 Hyperlink 等少数组件支持；不支持会抛 100017 |
| `getHistoricalPoints()` | 当前帧历史点 |
| `eventHandleId?` | API 24+，事件注入时使用，业务一般不需要 |

#### 枚举值要点

- `TouchType.Cancel` 的 SDK 注释明确给出触发例子：**“手写笔操作发生在手指触摸期间时，手指触摸操作收到 Cancel 事件”**。这是平台层面的“笔优先取消手指”行为（B 级；U：需在目标设备/API 24/26 上实测是否稳定）。
- `SourceType`：`Mouse / TouchScreen / KEY(22+) / JOYSTICK(22+)`。手写笔的 `source` 通常是 `TouchScreen`，靠 `sourceTool` 区分。
- `SourceTool`：`Finger / Pen / MOUSE(12+) / TOUCHPAD(12+) / JOYSTICK(12+)`。**没有 `RUBBER / PENCIL / BRUSH`**，因此 ArkUI TS 层无法区分笔尾橡皮。
- `MouseButton`：`Left / Right / Middle / Back / Forward / None`。
- `MouseAction`：`Press / Release / Move / Hover`（Hover 注释“无效果”）、`ENTER_WINDOW(23+) / LEAVE_WINDOW(23+) / CANCEL`。
- `KeyType`：`Down / Up / CANCEL(26+)`；`KeySource`：`Unknown / Keyboard / JOYSTICK(15+)`。

### 2.3 压感/倾斜/笔尖类型：具体怎么取

```typescript
// C 级示例：ArkUI 自绘 Canvas 的触点归一化骨架
@Entry
@Component
struct InkSurface {
  private ctx: CanvasRenderingContext2D = new CanvasRenderingContext2D(new RenderingContextSettings(true));
  private strokeStartNs: number = 0;
  private penInRange: boolean = false;

  build() {
    Canvas(this.ctx)
      .width('100%')
      .height('100%')
      .onTouch((e: TouchEvent) => this.handleTouch(e))
      .onHover((isHover: boolean, e: HoverEvent) => {
        this.penInRange = isHover && e.sourceTool === SourceTool.Pen;
      })
      .onHoverMove((e: HoverEvent) => {
        // e.x / e.y（vp，可能 undefined）、e.tiltX / e.tiltY、e.pressure
        // 可用于笔悬停光标、笔倾角预览
      })
      .onReady(() => { /* 初始化画布 */ })
  }

  private handleTouch(e: TouchEvent): void {
    if (e.type === TouchType.Down) {
      this.strokeStartNs = e.timestamp;
    }

    // 1) 历史点：同一帧内的高频采样
    const historical: HistoricalPoint[] = e.getHistoricalPoints();
    for (const hp of historical) {
      const t = hp.touchObject;
      // t.id, t.x, t.y, hp.force, hp.size, hp.timestamp
      // 注意：hp.size 单位未注明；hp.timestamp 为 ns
    }

    // 2) 本帧变更点
    for (const t of e.changedTouches) {
      const kind: 'pen' | 'touch' | 'mouse' =
        e.sourceTool === SourceTool.Pen ? 'pen'
        : (e.sourceTool === SourceTool.MOUSE || e.sourceTool === SourceTool.TOUCHPAD) ? 'mouse'
        : 'touch';

      // 压感：优先用事件级归一化 pressure；退化到触点原始值
      let p: number = e.pressure;                 // [0,1]，可能 >1
      if (!(p > 0) && t.pressure !== undefined) {
        p = t.pressure / 65535;                   // 触点原始 [0,65535)
      }
      if (!(p > 0)) p = 0.5;                      // 与 InkCanvas 现有约定一致
      p = Math.min(Math.max(p, 0), 1);

      // 倾斜：事件级，单位度
      const tiltX: number = e.tiltX;
      const tiltY: number = e.tiltY;

      // 接触面积：vp；手指可能有效，笔可能为 0
      const contactW: number = t.width ?? 0;
      const contactH: number = t.height ?? 0;

      // 时间：ns -> 相对笔迹起点的 ms
      const timeMs: number = (e.timestamp - this.strokeStartNs) / 1e6;

      // 送入 Core 的 OverlayPointer / InkPoint
      // ...
    }

    if (e.type === TouchType.Up || e.type === TouchType.Cancel) {
      // 结束笔迹；Cancel 必须走与 Up 相同的收尾，不能丢弃
    }
  }
}
```

注意事项：

1. **`e.pressure` 与 `t.pressure` 不是同一量纲**：前者 [0,1]，后者 [0,65535)。不要混用；必须显式归一化。
2. **`e.tiltX/tiltY` 是事件级**；`TouchObject` 本身没有 tilt。多指场景下，事件级 tilt 只对笔有意义。
3. **`timestamp` 是 ns，不是 µs/ms**。Windows 版 `InkSurface` 传的是 `PointerPoint.Timestamp / 1000`（µs→ms）；HarmonyOS 必须 `/1e6`，否则速度笔锋会异常。
4. **`TouchType.Cancel` 必须收尾**：系统可能在笔落下时取消手指触点，也可能因 Home 键/旋转取消。
5. **`getHistoricalPoints()` 只能在 `onTouch` 内调用**；异步回调里再取会无效。

### 2.4 鼠标/键盘/触控板（PC/2in1）

| 能力 | 入口 | 关键字段 | 备注 |
|---|---|---|---|
| 鼠标按下/释放/移动 | `onMouse` | `button`、`action`、`x/y`、`windowX/Y`、`displayX/Y`、`pressedButtons`、`rawDeltaX/Y` | `rawDeltaX/Y` 自 API 15；**API 26 起语义改为硬件原始位移**（之前被显示缩放比缩减） |
| 鼠标悬停 | `onHover` | `isHover`、`HoverEvent.x/y` | 鼠标或笔进入/离开 |
| 滚轮/触控板轴 | `onAxisEvent` / `BaseEvent.axis*` | `scrollStep`、`axisHorizontal/Vertical`、`axisPinch` | 可用于 PPT 翻页/缩放 |
| 键按下/释放 | `onKeyEvent` | `type`、`keyCode`、`keyText`、`keySource`、`deviceId`、`metaKey`、`intentionCode`、`unicode`、锁状态 | **需要焦点**；`keyCode` 见 `@ohos.multimodalInput.keyCode:KeyCode` |
| 键预处理 | `onKeyPreIme` / `onKeyEventDispatch` | 返回 boolean 消费 | API 12/15 |
| 窗口级拦截 | `UIContext.addLocalInputEventMonitor` | `InputEventSubTypeMask`、`RawInputEventWrapper` | **API 26 专属**；仅当前 UIContext/窗口；同步 UI 线程 |

**鼠标左键会被转换成触摸事件触发 `onTouch`**（SDK 原文）。因此：

- 若 InkCanvas 允许鼠标绘制，应把 `sourceTool === MOUSE` 的 `onTouch` 映射为 `OverlayPointerKind.Mouse`，而不是 `Touch`。
- 若不允许鼠标绘制，必须在 `onTouch` 里过滤掉 `source === SourceType.Mouse` 或 `sourceTool === MOUSE`。
- 触控板单指输入被当作鼠标操作（`SourceTool.TOUCHPAD` 注释），也应映射为 Mouse。

### 2.5 API 26 窗口级本地输入监听（PC/2in1 重点）

```typescript
import { UIContext } from '@kit.ArkUI';
import {
  InputEventSubTypeMask,
  InputEventInterceptAction,
  RawInputEventWrapper
} from '@kit.ArkUI';

const monitor = uiContext.addLocalInputEventMonitor(
  InputEventSubTypeMask.LEFT_MOUSE_DOWN | InputEventSubTypeMask.LEFT_MOUSE_UP |
  InputEventSubTypeMask.KEY_DOWN | InputEventSubTypeMask.KEY_UP,
  (wrapper: RawInputEventWrapper) => {
    if (wrapper.isMouseEvent()) {
      const me = wrapper.asMouseEvent();
      // me?.windowX / windowY / button
      return { action: InputEventInterceptAction.CONTINUE };
    }
    if (wrapper.isKeyEvent()) {
      const ke = wrapper.asKeyEvent();
      // ke?.keyCode
      return { action: InputEventInterceptAction.CONTINUE };
    }
    return { action: InputEventInterceptAction.BLOCK };
  }
);
uiContext.removeLocalInputEventMonitor(monitor);
```

事实与限制（B 级）：

- `@since 26.0.0`；`SystemCapability.ArkUI.ArkUI.Full`；普通应用 API，无需特殊权限（SDK 未标 permission）。
- 只在**当前 UIContext（当前窗口）**生效，不监听其他窗口/其他应用。
- 回调**同步运行在 UI 线程**，会阻塞事件处理；只能做轻量判断，不能做复杂计算/网络。
- `InputEventSubTypeMask` 只有 `LEFT/RIGHT/MIDDLE_MOUSE_DOWN/UP/DRAGGING`、`TOUCH_DOWN/UP`、`KEY_DOWN/UP`；**没有 TOUCH_MOVE**。因此它不适合做绘制采样，只适合拦截按下/抬起/按键。
- `asTouchEvent()` 返回的 `TouchEvent` 中 `getHistoricalPoints/stopPropagation/preventDefault/getCurrentLocalPosition` **无效**（事件尚未分发到组件）。
- `asKeyEvent()` 的 `metaKey/getModifierKeyState` **无效**。

> 结论：这个 API 适合 PC/2in1 上做“窗口级快捷键/右键菜单/笔键拦截”，不能替代 `onTouch` 做笔迹采样。API 24 没有此能力。

---

## 3. Pen Kit 提供什么（A+B 级）

### 3.1 Kit 与真实组件

- 知识库：`import { HandWriteComponent } from '@kit.PenKit'`。
- 本机 SDK：Kit 描述文件为 `@kit.Penkit`；组件实际为 `HandwriteComponent`，来自 `@hms.stylus.HandwriteComponent`，`@syscap SystemCapability.Stylus.Handwrite`，`@stagemodelonly`，`@since 5.0.0(12)`。
- 设备：phone/tablet 自 5.0.0(12)，2in1 自 5.0.1(13)（SDK API 元数据）。
- **大小写与导入路径以编译为准**；知识库的 `HandWriteComponent`/`canvasConfig` 示例不能直接使用。

### 3.2 `HandwriteComponent` 属性（SDK 声明）

| 属性 | 类型 | 起始 API | 说明 |
|---|---|---:|---|
| `handwriteController` | `HandwriteController` | 5.0.0(12) | 必填，功能入口 |
| `onInit?` | `() => void` | 5.0.0(12) | 初始化完成 |
| `onScale?` | `(scale: number) => void` | 5.0.0(12) | 画布缩放回调 |
| `onDidScroll?` | `(yOffset: number) => void` | 6.1.0(23) | 长画布滚动位置，单位 vp |
| `defaultPenType?` | `PenType` | 5.1.0(18) | 默认笔型 |
| `defaultPenInfo?` | `PenHspInfo[]` | 5.1.0(18) | 每种笔的默认宽度；`penWidth` 范围见 SDK 注释 |
| `widthRatio?` / `heightRatio?` | number，[0,1] | 6.0.0(20) | 画布宽/高比例，默认 1 |
| `maxCanvasHeight?` | number，vp，>0 | 6.1.0(23) | 长画布最大高度 |
| `scaleDisabled?` | boolean | 6.1.0(23) | true 禁用双指缩放 |
| `hiddenTools?` | `HiddenConfig` | 26.0.0 | `hiddenOptionalTools?: HiddenToolType[]`、`hiddenArcBox?: boolean` |

`PenType` 枚举：`PEN=1`、`BALLPOINT_PEN=2`、`PENCIL=3`、`MARKER=4`、`HIGHLIGHTER_BRUSH=5`、`MOSAIC=7`、`RUBBER=8`、`LASSO=9`、`LASER=10`。

`HiddenToolType`：`PEN=1`、`PENCIL=3`、`MARKER=4`、`HIGHLIGHTER_BRUSH=5`、`MOSAIC=7`、`LASSO=1048576`、`LASER=3145728`、`GRAPHICS_TOOLS=4194304`。

### 3.3 `HandwriteController` 方法

| 方法 | 返回 | 起始 API | 说明 |
|---|---|---:|---|
| `save(path: string)` | `Promise<void>` | 5.0.0(12) | 保存到指定路径；失败 1010400002 |
| `load(path: string)` | `void` | 5.0.0(12) | 加载；失败 1010400001 |
| `onLoad(callback: AsyncCallback<string>)` | `void` | 5.0.0(12) | 加载完成回调，返回页面 id |
| `getContentRange()` | `Rect` | 6.0.0(20) | 内容范围；Rect 单位是组件宽度的 1/1000 |
| `getThumbnail(rect)` | `Promise<PixelMap>` | 6.0.0(20) | 缩略图 |
| `scrollTo(yOffset: number)` | `void` | 6.1.0(23) | 长画布绝对滚动位置 |

### 3.4 辅助算法接口

| 接口 | 签名 | 起始 API | 用途 |
|---|---|---:|---|
| `PointPredictor.getPredictionPoint(event: TouchEvent): TouchPoint` | 输入 ArkUI TouchEvent，返回 `TouchPoint {x, y}` | 5.0.0(12) | 笔迹预测/延迟补偿 |
| `InstantShapeGenerator.processTouchEvent(event: TouchEvent): void` | 必须在组件的 `onTouch` 回调内调用 | 5.0.0(12) | 图形识别 |
| `InstantShapeGenerator.getPathFromString(shapeString, penSize): Path2D` | 字符串→Path2D | 5.0.0(12) | 识别结果重绘 |
| `InstantShapeGenerator.notifyAreaChange(width, height)` | void | 5.0.0(12) | 组件尺寸变化 |
| `InstantShapeGenerator.setPauseTime(timeMs)` | void | 5.0.0(12) | 停顿多久触发识别 |
| `InstantShapeGenerator.onShapeRecognized(callback)` | 返回自身 | 5.0.0(12) | 识别结果回调 |
| `InstantShapeGenerator.release()` | void | 5.0.0(12) | 释放 |
| `stylusInteraction.on('squeeze'/'doubleTap', cb)` | void | 5.1.1(19) | 捏笔/双击；`SystemCapability.Stylus.StylusService` |
| `stylusInteraction.off(...)` | void | 5.1.1(19) | 取消订阅 |
| `stylusInteraction.isSensorSupported(): boolean` | boolean | 26.0.0 | 笔身传感器是否支持 |
| `stylusInteraction.onAccelerometer/onGyroscope/onSensor(...)` | void | 26.0.0 | 笔身加速度/陀螺仪数据 |

关键限制：

- `InstantShapeGenerator`/`PointPredictor` 需要你**自己拥有 `onTouch` 回调**；`HandwriteComponent` 内部不暴露 `onTouch`。这进一步证明：Pen Kit 的成品画布与自绘管线是两条路。
- `stylusInteraction` 的捏笔/双击依赖笔硬件；没有 `isSqueezeSupported()` 这类查询（只有传感器查询）。无事件时只能降级。
- `InstantShapeGenerator` 的图形识别结果与 InkCanvas 自己的 `ShapeGenerator` 不是同一套算法。**为保持跨架构黄金向量一致，不应把 InkCanvas 的图形识别替换成 Pen Kit 的识别**；可以只用作对照/实验。

### 3.5 API 26 跟手性加速：两套签名与门控

#### A. ArkTS `StylusFrameBoost`

```typescript
import { StylusFrameBoost } from '@kit.Penkit'; // 大小写以 SDK kit_configs 为准

// @permission ohos.permission.STYLUS_FRAME_BOOST
// @syscap SystemCapability.Stylus.Handwrite
// @since 26.0.0
// action: 0=down, 2=move, 1=up, 3=cancel
// 返回：0=成功，1=失败；权限拒绝抛 201
forceRefreshOneFrame(action: number): number;
```

事实：

- 类 `StylusFrameBoost` 在 `@hms.stylus.handwrite.d.ts` 中声明，无显式构造函数；**实例化方式（是否 `new StylusFrameBoost()`、是否有工厂）SDK 未给出**，需官方示例/实测。
- 权限 `ohos.permission.STYLUS_FRAME_BOOST`：`grantMode: system_grant`、`availableLevel: normal`、`availableType: NORMAL`、`since 26.0.0`、设备 phone/tablet/2in1、`provisionEnable: false`。普通应用可在 `module.json5` 声明（无需用户弹窗），但需实测授权结果。
- `isSystemApi: false`（SDK API 元数据），三方可用。
- API 24 没有此类/方法；调用前必须门控。

#### B. Native `HMS_HandWrite_SetRefreshDelayOff`

```c
// native_handwrite_api.h, @library libhandwrite_ndk.z.so
// @permission ohos.permission.STYLUS_FRAME_BOOST
// @syscap SystemCapability.Stylus.Handwrite
// @since 26.0.0
int32_t HMS_HandWrite_SetRefreshDelayOff(const char* xcomponentId, const bool enable);
// 返回 0 成功；201 权限失败；1010400001 内部错误
```

面向“自绘类型”的 XComponent：传入 XComponent 的 id，开启/关闭刷新延迟优化。**这是 InkCanvas 自绘渲染更贴合的一条路**，因为它直接绑定 XComponent，而不是逐事件强制刷帧。

#### C. Native 预测 `HMS_HandWrite_GetPredictPoint`

```c
// @since 6.0.0(20)
typedef struct {
    float x;
    float y;
    int64_t timeStamp;
    float force;
} HandWrite_HistoricalPoint;

int32_t HMS_HandWrite_GetPredictPoint(
    const HandWrite_HistoricalPoint* event,
    int32_t size,
    float* predictPointX,
    float* predictPointY);
```

API 20 起可用，API 24 也有；可用于自绘 XComponent 的预测点。

#### D. 门控建议（C 级）

```typescript
import { deviceInfo } from '@kit.BasicServicesKit';

const hasStylus = canIUse('SystemCapability.Stylus.Handwrite'); // canIUse(syscap: string): boolean
const api26 = deviceInfo.sdkApiVersion >= 26;
const frameBoost = api26 && hasStylus; // 再用 try/catch 包住调用
```

- `canIUse` 签名来自 SDK `global.d.ts`：`export declare function canIUse(syscap: string): boolean;`。
- `deviceInfo.sdkApiVersion: number`（API 6+）；`sdkMinorApiVersion/sdkPatchApiVersion` 仅 API 26+，用于区分 26.0.1，但项目策略是不使用 26.0.1 Beta API。
- **静态 import 风险（U）**：在 API 24 设备上，`@kit.Penkit` 本身存在（Pen Kit 自 API 12），但 `StylusFrameBoost` 符号可能不存在。顶层 `import` 是否会在 API 24 启动时失败，需实机验证。保守做法：把 26 专属符号的 import 与调用隔离在 `platform/compat` 的单独模块，并用动态加载/特性探测（若 ArkTS 支持）或确保工程只面向 API 26 目标设备；具体方案需编译+实机验证。
- **OpenHarmony 兜底构建**：Pen Kit 位于 SDK 的 `hms` 目录，不在 `openharmony` 公共 SDK。用 OpenHarmony SDK 构建时 `@kit.Penkit` 不存在，必须把 Pen Kit 代码隔离在可选模块/条件编译中，否则 OH 兜底构建会失败。

### 3.6 Pen Kit 不提供什么（重要）

| 不提供 | 影响 |
|---|---|
| 逐点笔迹回调 / 原始点流 | `HandwriteComponent` 是黑盒画布，无法把点流喂给 InkCanvas 的 `InkStroke` |
| 公共笔迹数据模型 / `.icink` 兼容导出 | 只能 `save(path)` 成 Pen Kit 自己的格式；无法与 `.icink`/`Settings.json` 互认 |
| 手掌判定/误触开关 | 无公开 API；是否内部处理未知，不可配置 |
| 笔尾橡皮的 ArkUI 层工具类型 | `SourceTool` 只有 `Pen`；需 XComponent 原生 `GetTouchPointToolType` |
| 压力/倾斜的原始采样保证 | 组件不暴露；只能通过自绘 `onTouch` 获取 |

---

## 4. 六个问题的直接回答

### Q1. ArkUI 如何拿到笔的压感/倾斜/笔尖类型？

**压感**：

- `TouchEvent`（继承 `BaseEvent`）`.pressure`：归一化 [0,1]，`@since 9`；某些设备可能 >1，需 clamp。
- `TouchObject.pressure`：原始 [0,65535)，`@since 15`；适合逐点。
- `HistoricalPoint.force`：原始 [0,65535)，`@since 10`；适合高频历史点。
- 设备不支持压感时可能为 0；InkCanvas 现有约定 `<=0 → 0.5` 应保留。

**倾斜**：

- `TouchEvent.tiltX/tiltY`：度，`@since 9`；事件级。
- `TouchEvent.rollAngle`：度，`@since 17`；笔身旋转。
- `HoverEvent` 继承 `BaseEvent`，悬停时同样有 `tiltX/tiltY`。
- XComponent 原生：`OH_NativeXComponent_GetTouchPointTiltX/Y(component, pointIndex, float*)`，`@since 9`；历史点结构里也有 `titlX/titlY`（SDK 拼写如此）。

**笔尖类型**：

- ArkUI TS：`SourceTool.Pen / Finger / MOUSE / TOUCHPAD / JOYSTICK`；**无法区分橡皮/铅笔/笔刷**。
- XComponent 原生：`OH_NativeXComponent_GetTouchPointToolType()`，枚举 `OH_NativeXComponent_TouchPointToolType`：`UNKNOWN / FINGER / PEN / RUBBER / BRUSH / PENCIL / AIRBRUSH / MOUSE / LENS`（`@since 9`）。这是三方应用区分笔尾橡皮的**唯一公开路径**。
- `OH_NativeXComponent_GetTouchEventSourceType()` 可区分 `MOUSE / TOUCHSCREEN / TOUCHPAD / JOYSTICK / KEYBOARD`。

**结论**：纯 ArkUI 只能做“笔 vs 手指 vs 鼠标”的粗分流；要笔尖类型/橡皮，走 XComponent 原生。

### Q2. Pen Kit 提供什么？跟手性加速 API 26 的签名与门控？

见第 3 节。摘要：

- 成品画布 `HandwriteComponent` + `HandwriteController`（保存/加载/内容范围/缩略图/滚动）。
- 辅助算法 `PointPredictor`、`InstantShapeGenerator`、`stylusInteraction`。
- API 26 加速：
  - `StylusFrameBoost.forceRefreshOneFrame(action: number): number`；权限 `STYLUS_FRAME_BOOST`；`@since 26.0.0`。
  - `HMS_HandWrite_SetRefreshDelayOff(const char* xcomponentId, bool enable)`；同权限；`@since 26.0.0`。
- 门控：`canIUse('SystemCapability.Stylus.Handwrite') && deviceInfo.sdkApiVersion >= 26`；在 `module.json5` 声明 `ohos.permission.STYLUS_FRAME_BOOST`（normal/system_grant）；API 24 回退到 `PointPredictor`（API 20）或不用。
- 未验证：`StylusFrameBoost` 的实例化方式、API 24 上符号缺失时静态 import 的行为、实际降延迟幅度。

### Q3. 手掌误触抑制平台提供什么？应用层要自己做什么？

**平台提供**：

- 系统在笔操作发生时会向手指触摸发送 `TouchType.Cancel`（SDK `TouchType.Cancel` 注释原文）。这是一种“笔优先”的取消行为。
- 没有公开的 palm rejection 开关/分类 API；在 SDK 公开 API 中检索 `palm` 无相关结果（只有蓝牙等无关命中）。
- `HandwriteComponent` 是否内部做手掌抑制：未知，未文档化，不可配置。
- Input Kit 只做输入归一化/分发，不做手掌分类。

**应用层必须自己做**：

1. 用 `TouchObject.width/height`（vp）或 `HistoricalPoint.size` 判定大面积触点。
2. 维护 `penActive` 状态：笔 `Down` 置位、`Up/Cancel` 清除；有笔时拒绝手指绘制（或按设置）。
3. 手掌触点按设置转为“区域擦除”/“忽略”，其余手指按设置绘制或做 PPT 手势。
4. 处理多指：用 `touches` 跟踪所有活动触点；`Cancel` 时收尾。
5. 特殊屏分档：沿用 `TouchContactClassifier`（QuadIR 几何平均、`TouchMultiplier`、nib/finger 双阈值）。
6. 若 `width/height` 为 0/undefined：降级为“笔优先 + 手指绘制开关 + 不做手掌擦除”，并在设置中明示。

InkCanvas 现有 `PalmRejector` + `TouchContactClassifier` 已是纯逻辑，可直接移植；缺口只在平台适配层把 `ContactWidth/ContactHeight` 填进去。

### Q4. 触摸接触面积/尺寸信息可得吗？特殊屏分档移植需要什么？

**可得，但来源不同、单位不同**：

| 来源 | 字段 | 单位 | 起始 API | 三方可用 | 备注 |
|---|---|---|---:|---|---|
| ArkUI `TouchObject` | `width` / `height` | vp | 15 | 是 | 最推荐；手指接触区；可选，可能 0 |
| ArkUI `HistoricalPoint` | `size` | 未注明 | 10 | 是 | 默认 0；量纲需标定 |
| XComponent 原生 | `size`（double） | 未注明 | 8 | 是 | “finger pad 与屏幕接触面积” |
| Input Kit 系统 `touchEvent` | `width` / `height` | px | 9 | 否（无事件源） | `inputMonitor` 不在公开 SDK |
| Input Kit 系统 `touchEvent` | `toolWidth/toolHeight` | px | 9 | 否 | 工具区域 |

**特殊屏分档移植要点**：

- Windows 版 `ContactRect.Width/Height` 是 DIP；ArkUI `width/height` 是 vp，二者可近似 1:1（DIP↔vp 同值，项目契约也要求如此）。但 **必须实测** 目标设备是否按 vp 上报。
- `TouchContactClassifier.MeasureContactWidth(width, height, isQuadIR)`：QuadIR 用 `sqrt(width*height)`；移植时直接喂 ArkUI 的 width/height。
- 如果某设备只给 `size` 而不给 width/height：
  - 若 `size` 是“直径/宽度”，可直接 `ContactWidth=ContactHeight=size`（几何平均不变）。
  - 若 `size` 是“面积”，需 `sqrt(size)` 才能近似宽度；这是工程假设，**必须实机标定**。
- 笔的 `width/height` 可能为 0；手掌判定只对 `sourceTool === Finger` 生效。
- 设置键名保持 `isSpecialScreen`、`isQuadIR`、`touchMultiplier`、`nibModeBoundsWidth`、`fingerModeBoundsWidth`、`nibModeBoundsWidthThresholdValue`、`fingerModeBoundsWidthThresholdValue`、`nibModeBoundsWidthEraserSize`、`fingerModeBoundsWidthEraserSize`，与 `Settings.json` 一致。
- 建议新增调试页：实时显示 `sourceTool / pressure / width / height / size / tilt / timestamp`，用于逐机型标定。

### Q5. 鼠标/键盘事件（PC/2in1）？

见 2.4/2.5。要点：

- `onMouse` 提供 `button/action/x/y/windowX/windowY/rawDelta/pressedButtons`；`MouseAction.ENTER_WINDOW/LEAVE_WINDOW` API 23+。
- `onHover`/`onHoverMove` 提供悬停；`HoverEvent` 有坐标与 `BaseEvent` 的 pressure/tilt/sourceTool。
- `onKeyEvent` 需要焦点；`KeyEvent` 有 `keyCode/keyText/keySource/deviceId/metaKey/intentionCode/unicode`；`getModifierKeyState` 手写笔场景不支持。
- `onAxisEvent` 提供滚轮/触控板轴值；`scrollStep`、`axisHorizontal/Vertical`、`axisPinch`。
- API 26 窗口级本地监听可拦截鼠标/触摸/按键的 down/up，但**不含 move**，且只限当前窗口。
- Input Kit：`inputDevice` 可列设备（无特殊权限）；`pointer` 可设光标样式/自定义光标；`inputEventClient` 注入需要 system_basic 权限，普通应用不可用。
- 设计规范：平板及更宽设备应做基础键鼠适配（悬停、快捷键、右键菜单），触控热区 ≥44×44 dp。

### Q6. API 24 与 API 26 各有什么？

见第 5 节矩阵。

---

## 5. API 24 vs API 26 差异矩阵

> `api24` = HarmonyOS 6.1.1 / API 24；`api26` = HarmonyOS 26.0.0 / API 26 Release。所有“可用”结论基于 SDK `@since`；运行期仍需实测。

| 能力 | API 24 | API 26 | 门控/备注 |
|---|---|---|---|
| ArkUI `onTouch` + `pressure/tiltX/tiltY/sourceTool` | 可用（`@since 9`） | 可用 | `SystemCapability.ArkUI.ArkUI.Full`；无需权限 |
| `TouchObject.pressure/width/height/pressedTime` | 可用（`@since 15`） | 可用 | 可选字段；判空 |
| `TouchEvent.getHistoricalPoints()` | 可用（`@since 10`） | 可用 | 仅 `onTouch` 内 |
| `TouchObject.getCurrentLocalPosition()` | 不可用 | 可用（26.0.0） | 用 x/y + 组件位置自行换算 |
| `TouchEvent.eventHandleId` | 可用（24） | 可用 | 事件注入场景 |
| `onHover` / `onHoverMove` | 可用（8 / 15） | 可用 | 手写笔悬停位置需实测 |
| `SourceType.KEY/JOYSTICK` | 可用（22） | 可用 | — |
| `SourceTool.MOUSE/TOUCHPAD` | 可用（12） | 可用 | — |
| `SourceTool` 橡皮/铅笔/笔刷 | 不支持 | 不支持 | 走 XComponent 原生 toolType |
| `onMouse` / `onAxisEvent` | 可用（8 / 17） | 可用 | — |
| `MouseAction.ENTER_WINDOW/LEAVE_WINDOW` | 可用（23） | 可用 | — |
| `MouseEvent.rawDeltaX/Y` | 可用（15），语义为缩放后 | 可用，语义改为硬件原始位移 | 版本差异需注意 |
| `onKeyEvent` boolean 回调 | 可用（15） | 可用 | 需焦点 |
| `KeyType.CANCEL` | 不可用 | 可用（26.0.0） | 兼容层按 Down/Up 处理 |
| `UIContext.addLocalInputEventMonitor` | 不可用 | 可用（26.0.0） | 仅当前窗口；无 move；无权限 |
| Pen Kit `HandwriteComponent` | 可用（12） | 可用 | `SystemCapability.Stylus.Handwrite`；HMS |
| Pen Kit `scaleDisabled/maxCanvasHeight/scrollTo/onDidScroll` | 可用（23） | 可用 | API 24 已包含 23 |
| Pen Kit `hiddenTools` | 不可用 | 可用（26.0.0） | 只影响工具栏 |
| `PointPredictor` / `InstantShapeGenerator` | 可用（12） | 可用 | 需要自绘 onTouch |
| `stylusInteraction` 捏笔/双击 | 可用（19） | 可用 | `SystemCapability.Stylus.StylusService` |
| `stylusInteraction` 传感器 / `isSensorSupported` | 不可用 | 可用（26.0.0） | 需笔硬件 |
| `StylusFrameBoost.forceRefreshOneFrame` | 不可用 | 可用（26.0.0） | 权限 `STYLUS_FRAME_BOOST` |
| `HMS_HandWrite_SetRefreshDelayOff` | 不可用 | 可用（26.0.0） | 同权限；XComponent 自绘 |
| `HMS_HandWrite_GetPredictPoint` | 可用（20） | 可用 | Native |
| Input Kit `inputDevice` 列表 | 可用（8） | 可用 | `SystemCapability.MultimodalInput.Input.InputDevice` |
| Input Kit `pointer` 光标样式 | 可用（9/10） | 可用 | `SystemCapability.MultimodalInput.Input.Pointer` |
| Input Kit `inputEventClient` 注入 | 不可用 | 可用（26.0.0） | `CONTROL_DEVICE`，system_basic，仅 2in1；普通应用不可用 |
| 窗口多模事件过滤 C API | 不可用 | 可用（26.0.0） | 仅 C API；三方可用性/签名未在 SDK 头文件核对（U） |
| 鼠标光标/光标类型外设管理 | 不可用 | 26.0.1 Beta1 | Beta 不可上架；不纳入基线 |
| `DisplaySync.create/setExpectedFrameRateRange` | 可用（11） | 可用 | 知识库示例函数名错误 |
| TaskPool / Worker / Sendable | 可用 | 可用 | 起始 API 未在知识库标注；按基础能力使用 |
| 容器对象跨线程拷贝 | 不可用 | 可用（26.0.0） | 序列化成本 |
| 多线程检测可配置参数 | 不可用 | 可用（26.0.0） | `setMultithreadingDetectionEnabled` |
| `JSON.parseSendable` / Map 增强 | 不可用 | 26.0.1 Beta1 | 不使用 |
| 窗口 `TYPE_FLOAT` 全局悬浮窗 | 存在（9） | 存在 | 创建需 `SYSTEM_FLOAT_WINDOW`（system_basic）；普通应用不可用 |
| 主窗 `WINDOW_TOPMOST` | 可用（13） | 可用 | normal；只影响主窗 Z 序，不是跨应用绘制 |

---

## 6. 门控与 compat 层设计（C 级建议）

### 6.1 精确 SystemCapability 字符串（B 级，来自 SDK）

| 能力 | SystemCapability |
|---|---|
| ArkUI 触摸/鼠标/键盘/悬停 | `SystemCapability.ArkUI.ArkUI.Full` |
| Pen Kit 画布/算法/帧加速 | `SystemCapability.Stylus.Handwrite` |
| 笔捏合/双击/笔身传感器 | `SystemCapability.Stylus.StylusService` |
| 输入设备列表 | `SystemCapability.MultimodalInput.Input.InputDevice` |
| 鼠标光标 | `SystemCapability.MultimodalInput.Input.Pointer` |
| 系统触摸事件模型 | `SystemCapability.MultimodalInput.Input.Core` |
| 输入注入 | `SystemCapability.MultimodalInput.Input.InputSimulator` |
| 设备信息 | `SystemCapability.Startup.SystemInfo` |
| 窗口核心/全局悬浮窗 | `SystemCapability.WindowManager.WindowManager.Core` |
| 窗口 Z 序/置顶（26.0.1） | `SystemCapability.Window.SessionManager` |

> 知识库只在 AR Engine 示例中出现 `canIUse("SystemCapability.AREngine.Core")` 的用法，没有 Pen/Input 的字符串；上表来自本机 SDK 声明。

### 6.2 能力探测骨架

```typescript
// platform/compat/Capabilities.ets（建议）
import { deviceInfo } from '@kit.BasicServicesKit';

export interface Capabilities {
  readonly api24: boolean;
  readonly api26: boolean;
  readonly stylusHandwrite: boolean;
  readonly stylusService: boolean;
  readonly frameBoost: boolean;
  readonly localInputMonitor: boolean;
  readonly globalFloatWindow: boolean; // 预期 false（无 SYSTEM_FLOAT_WINDOW）
}

export function probeCapabilities(): Capabilities {
  const api = deviceInfo.sdkApiVersion;
  const handwrite = canIUse('SystemCapability.Stylus.Handwrite');
  return {
    api24: api >= 24,
    api26: api >= 26,
    stylusHandwrite: handwrite,
    stylusService: canIUse('SystemCapability.Stylus.StylusService'),
    frameBoost: api >= 26 && handwrite,
    localInputMonitor: api >= 26,
    globalFloatWindow: false, // 普通应用无 system_basic 权限；需实测/企业签名
  };
}
```

约定（与项目设计文档一致）：

- 业务层禁止直接判断版本号/直接 import 26 专属 Kit；统一走 `platform/compat`。
- `useApi26()` 由 `deviceInfo.sdkApiVersion >= 26` 与 `canIUse` 组合，不用 `deviceType` 硬编码。
- 26.0.1 Beta API 一律不用（不可上架）。
- Pen Kit import 必须隔离，保证 OpenHarmony 公共 SDK 兜底构建不引用 HMS 模块。

### 6.3 Pen Kit 调用隔离示意

```typescript
// platform/pen/PenBoost.ets —— 只在商业 SDK 构建中包含
import { StylusFrameBoost } from '@kit.Penkit';

export interface FrameBoost {
  onTouchAction(action: number): void;
  dispose(): void;
}

export function createFrameBoost(): FrameBoost | null {
  if (!canIUse('SystemCapability.Stylus.Handwrite')) return null;
  if (deviceInfo.sdkApiVersion < 26) return null;
  try {
    // 实例化方式待官方示例确认；以下为接口形状示意，不是已验证代码
    // const b = new StylusFrameBoost();
    // return { onTouchAction: (a) => b.forceRefreshOneFrame(a), dispose: () => {} };
    return null;
  } catch (e) {
    return null;
  }
}
```

> 这段代码故意不写成“可运行”，因为 `StylusFrameBoost` 的构造方式与 API 24 符号缺失行为尚未验证。落地前必须先做探针。

---

## 7. 针对 InkCanvas 的落地建议

### 7.1 两条自绘路线，先用探针选型

| 路线 | 组成 | 优点 | 风险 |
|---|---|---|---|
| A. ArkUI Canvas + `onTouch` | `Canvas`/`CanvasRenderingContext2D` + `TouchEvent` + `getHistoricalPoints` + `DisplaySync`; 可选 `PointPredictor`/`StylusFrameBoost` | 纯 ArkTS，移植成本低；事件字段齐全（压感/倾斜/接触面积）；与 UI 同栈 | 采样/渲染延迟未知；`SourceTool` 无橡皮；高刷下达不到笔输入延迟目标的风险 |
| B. XComponent + Native | `XComponent(type:'surface')` + `DispatchTouchEvent` + `OH_NativeXComponent_GetTouchEvent/GetTouchPointToolType/GetTouchPointTiltX/Y` + `OH_Drawing` + `HMS_HandWrite_SetRefreshDelayOff/GetPredictPoint` | 工具类型最全（RUBBER/PENCIL/...）；原生采样与绘制；API 26 官方加速面向此路径 | NDK 开发量大；坐标单位/线程/生命周期复杂；Pen Kit 仍是 HMS 依赖；OH 兜底构建隔离 |
| C. Pen Kit `HandwriteComponent` | 成品画布 + 控制器 | 开箱即用、手写体验由华为调优 | 黑盒，无逐点数据/无 `.icink` 互认；无法承载 InkCanvas 的笔锋/撤销/图形/PPT 批注模型；不适合作为核心 |

**建议**：以 A 为第一优先（满足 1:1 逻辑互认与快速移植），同时做一个 B 的最小探针（只做“笔迹 + 压感 + 橡皮 + 延迟”），用真实设备对比端到端延迟与手感。C 只作为“纯笔记模式”的实验，不作为 InkCanvas 核心。

### 7.2 `OverlayPointer` 字段映射（Core 不改，平台适配层填）

| Core 字段 | ArkUI 来源 | 单位/换算 | 备注 |
|---|---|---|---|
| `Id` | `TouchObject.id` | — | 多指/笔各自独立 |
| `X`,`Y` | `TouchObject.x/y` | vp（≈DIP） | 若画布不是全窗口，需叠加组件偏移；或用 `windowX/Y` 再减画布原点 |
| `Pressure` | `BaseEvent.pressure` 优先；否则 `TouchObject.pressure / 65535` | [0,1] | 0→0.5；clamp |
| `ContactWidth` | `TouchObject.width ?? 0` | vp | 手指有效；笔可能 0 |
| `ContactHeight` | `TouchObject.height ?? 0` | vp | 同上 |
| `Kind` | `sourceTool === Pen ? Pen : (MOUSE/TOUCHPAD ? Mouse : Touch)` | — | 鼠标左键在 `onTouch` 中也会出现，必须用 sourceTool 判 |
| `TimeMs` | `(event.timestamp - strokeStartNs) / 1e6` | ns→ms | 不能用 `pressedTime` 代替事件时间；历史点用各自 `timestamp` |

历史点补充：

- 每个 `HistoricalPoint.touchObject` 可构造一个 `OverlayPointer`，`TimeMs` 用 `hp.timestamp`。
- 同一帧的 `changedTouches` 最后一点与历史点可能重叠；按时间/坐标去重，避免速度笔锋被“零距离段”拉低。
- `touches` 用于多指/手掌状态跟踪；`changedTouches` 用于驱动状态变化。

### 7.3 手掌判定移植方案

**保持 Core 纯逻辑不变**：

- `PalmRejector`：`PalmContactThreshold`、`PenHasPriority`、`IsPalmContact`、`ShouldDraw`、`ShouldErase`。
- `TouchContactClassifier`：`MeasureContactWidth`、`BoundsWidth`、`Classify`。
- 新增 ArkTS 端 `@parity` 锚点，指向 C# 文件；用黄金向量钉住：
  - `ContactWidth/Height` → QuadIR 几何平均 → `TouchMultiplier` → `TouchIntent`。
  - 笔/手指/手掌的 `OverlayPointerKind` 分流。
  - `pressure <= 0 → 0.5`。

**平台适配层**：

- `penActive`：收到 `sourceTool === Pen` 的 Down/Up/Cancel 时维护；悬停可用时用 `onHover` 提前置位（可选）。
- `palm` 追踪：`sourceTool === Finger && width/height >= threshold` 的触点加入集合；Up/Cancel 移除。
- `TouchType.Cancel`：若系统因笔落下取消了手指触点，按“手指结束”收尾，不产生笔迹。
- 若 `width/height` 不可用：`PalmContactThreshold` 逻辑退化；设置页提示“该设备不支持手掌面积判定”，仅保留笔优先。

### 7.4 特殊屏分档

- Windows 版设置键与默认值保持不变（`isSpecialScreen=true`、`isQuadIR=false`、`touchMultiplier=0.25`、nib 10、finger 30、阈值 2.5、橡皮系数 0.8）。
- ArkUI width/height 是 vp，与 Windows DIP 近似同值；但**首版必须在目标红外屏/希沃类设备上采一组 finger/palm/pen 的 width/height 分布**，再决定阈值。
- 若某设备只给 `HistoricalPoint.size`：先按“size=接触宽度”试标定；若是面积，用 `sqrt`。两种都要在设置里可切换/可调。
- 特殊屏模式与“笔尖模式（nib mode）”是两套阈值；不要合并。

### 7.5 延迟与帧同步

1. **采样**：`onTouch` + `getHistoricalPoints()` 全量取点；不要只取 `changedTouches`。
2. **渲染合并**：不要在每次 `onTouch` 里直接重绘；把点写入缓冲，由 `DisplaySync` 的 `frame` 回调按显示节拍合并绘制。
   - 正确 API：`displaySync.create()`、`setExpectedFrameRateRange({min,max,expected})`、`on('frame', cb)`、`start()/stop()`；`@since 11`。
3. **预测**：`PointPredictor.getPredictionPoint(event)`（API 12+）或 native `HMS_HandWrite_GetPredictPoint`（API 20+）；预测点只用于临时渲染，落笔数据仍用真实点。
4. **帧加速**：API 26 且权限允许时，用 `StylusFrameBoost.forceRefreshOneFrame(action)` 或 `HMS_HandWrite_SetRefreshDelayOff(xcomponentId, true)`；必须实测收益与功耗。
5. **测量**：用 `HiAppEvent` 记录 down/up 时间戳、帧回调时间戳；或用高速相机拍“笔尖-屏幕墨迹”延迟。不要只凭肉眼。

### 7.6 线程与 TaskPool

- `onTouch` 回调按 UI 框架惯例在 UI 线程（SDK 未显式声明 `onTouch` 线程，U：需实测）；本地输入监听明确“同步 UI 线程”。
- UI 线程只做：字段提取、归一化、入环形缓冲、轻量手掌判定。
- 重计算（轮廓生成、命中测试、平滑/笔锋 settle）可放 `TaskPool`，但：
  - 不要传 `TouchEvent`/带方法的对象；先抽取 `number[]`/`ArrayBuffer`。
  - `@Concurrent` 函数只接收可序列化/Sendable 数据。
  - API 26 支持容器对象跨线程拷贝（序列化），安全但有成本；高频小批次不要每点一次 `taskpool.execute`，按 4–16 ms 批量提交。
- 渲染必须在 UI 线程/渲染线程；worker 只产出几何数据。
- `Sendable` 适合长期共享的配置/只读表；每帧点数组用 `ArrayBuffer`/普通数组拷贝更可控。

### 7.7 设备分档

| 形态 | 笔输入策略 |
|---|---|
| 平板 | 全量：笔+手指分流、手掌判定、特殊屏分档、悬停、笔预测；Pen Kit 加速可选 |
| PC/2in1 | 笔+鼠标+键盘+触控板；`onMouse/onAxisEvent/onKeyEvent`；悬停光标；右键菜单；API 26 窗口级监听（仅当前窗） |
| 手机 | 基本：手指绘制/擦除；无笔、无悬停、无倾斜；关闭手掌面积判定与特殊屏分档；触摸目标 ≥44×44 dp；不做全屏覆盖批注 |

能力探测优先于 `deviceType` 硬编码。

### 7.8 与黄金向量/`@parity` 的衔接

建议新增纯逻辑与向量（不依赖 SDK）：

1. `InputNormalizer`：输入原始字段（source/sourceTool/pressure/width/height/timestamp）→ `OverlayPointer`；覆盖：
   - pressure 0 / 0.5 / 1 / >1 / 65535；
   - timestamp ns→ms、相对笔迹起点；
   - mouse-as-touch 过滤；
   - pen/finger/touchpad 分类；
   - width/height=0 的降级。
2. `TouchContactClassifier`：现有 C# 逻辑 1:1 移植；向量覆盖 QuadIR、特殊屏、nib/finger、橡皮/手势分流。
3. 时间戳去重：历史点与 changedTouches 重叠的合并规则。
4. 单位换算：vp/DIP 1:1；native px→vp 只在平台层做，需 density 输入。

这些向量可由 C# 生成、ArkTS 消费，沿用 `parity/vectors/*.json` 机制。

### 7.9 验证清单（本机无设备，全部未执行）

| 编号 | 验证项 | 方法 | 状态 |
|---|---|---|---|
| P1 | `TouchEvent.pressure` 与 `TouchObject.pressure` 量纲/范围 | 平板+笔，打印原始值 | 未执行 |
| P2 | `width/height` 是否按 vp 上报、笔是否为 0 | 手指/手掌/笔对比，和组件尺寸换算 | 未执行 |
| P3 | `HistoricalPoint.size` 单位/含义 | 已知接触物对比 | 未执行 |
| P4 | `sourceTool` 对笔尾橡皮/侧键/悬停的表现 | 真实 M-Pencil | 未执行 |
| P5 | 笔落下时手指是否收到 `Cancel` | 多指+笔同时操作 | 未执行 |
| P6 | `onHover/onHoverMove` 是否支持笔悬停与 tilt | 平板/2in1 | 未执行 |
| P7 | 鼠标左键在 `onTouch` 中的 `source/sourceTool` | 2in1 鼠标 | 未执行 |
| P8 | `StylusFrameBoost` 实例化/API 24 符号缺失行为 | 26/24 设备各跑一次 | 未执行 |
| P9 | `HMS_HandWrite_SetRefreshDelayOff` 实际延迟收益 | 高速相机/时间戳 | 未执行 |
| P10 | `TYPE_FLOAT` 在普通签名应用上是否失败 | 创建窗口并捕获 201 | 未执行（预期失败） |
| P11 | 浮动窗口/无焦点窗口的键事件 | 2in1 | 未执行 |
| P12 | `HandwriteComponent` 保存格式能否导入 `.icink` | 保存后解析 | 未执行（预期不能） |
| P13 | API 24 设备 `canIUse('SystemCapability.Stylus.Handwrite')` 结果 | 6.1.1 设备 | 未执行 |
| P14 | DisplaySync 实际帧率与渲染节拍 | `on('frame')` 时间戳 | 未执行 |

---

## 8. 第三方应用硬约束（结论）

1. **不能做跨应用全屏透明覆盖层**：`TYPE_FLOAT` 全局悬浮窗创建需要 `ohos.permission.SYSTEM_FLOAT_WINDOW`（system_basic）。普通三方应用默认不可获得；`WINDOW_TOPMOST` 只是主窗 Z 序，不是覆盖其他应用。
2. **不能全局监听/注入输入**：`inputMonitor` 不在公开 SDK；`inputEventClient` 需要 `ohos.permission.CONTROL_DEVICE`（system_basic、manual_settings、仅 2in1、API 26）。回放/自动化不能依赖注入。
3. **不能管理手写笔系统事件**：`ohos.permission.MANAGE_STYLUS_EVENT` 为 system_basic（API 12+），三方不可用。
4. **没有手掌误触系统 API**：只能应用层用接触面积+笔优先实现；`HandwriteComponent` 内部行为未知且不可配置。
5. **ArkUI TS 层拿不到橡皮/铅笔等笔尖类型**：只有 `Pen`；要精细工具类型必须走 XComponent 原生。
6. **Pen Kit 是 HMS 能力**：位于 SDK `hms` 目录，OpenHarmony 公共 SDK 没有；必须隔离，OH 兜底构建不能引用。
7. **`HandwriteComponent` 是黑盒**：无逐点数据/公共模型；其保存格式不能直接与 `.icink` 互认。不能作为 InkCanvas 核心。
8. **压感/接触面积不保证全设备可用**：字段可能为 0/undefined；必须有降级（0.5、关闭面积判定）。
9. **键事件依赖焦点**：无全局快捷键 API；浮动窗口/覆盖层的焦点行为需实测。
10. **26.0.1 Beta API 不可上架**：鼠标光标管理、`JSON.parseSendable` 等不得进入基线；只用 26.0.0 Release。
11. **模拟器不可靠**：Input Kit 模拟器与真机有差异；ArkTS 并发不支持模拟器（知识库 arkts.md）。笔/压感/接触面积必须真机。
12. **Driver Kit 不是三方应用通道**：知识库只把它描述为外设厂商的高阶 HID 能力（压感/绘图区域设置），不是普通应用读取笔数据的 API。

---

## 9. 未知/需实机验证清单

1. `TouchObject.width/height` 在目标平板/2in1/手机上是否真的有值；单位是否严格 vp；笔是否恒 0。
2. `HistoricalPoint.size` 的量纲（宽度/直径/面积）与是否可用于手掌判定。
3. `BaseEvent.pressure` 与 `TouchObject.pressure` 在真实笔上的映射关系；是否所有设备都归一化。
4. 笔尾橡皮在 ArkUI `onTouch` 中是否仍为 `SourceTool.Pen`；是否触发 `Cancel`/不同 `source`。
5. `TouchType.Cancel` 的“笔落取消手指”行为在 API 24/26、各厂商设备上是否一致。
6. `onHover/onHoverMove` 是否对第三方 M-Pencil 生效；是否带 tilt/pressure；是否需要额外权限。
7. `onTouch` 的实际线程与调用频率；`changedTouches`/`touches`/`getHistoricalPoints` 的去重与顺序。
8. `StylusFrameBoost` 的实例化方式（是否有构造函数/工厂/静态方法）；API 24 上符号缺失时静态 import 是否崩溃；实际降延迟幅度与功耗。
9. `HMS_HandWrite_SetRefreshDelayOff` 的 xcomponentId 获取方式；是否只对 `type:'surface'` 生效；与 ArkUI Canvas 是否可混用。
10. `HandwriteComponent` 是否内部做手掌抑制；是否有未公开的笔迹数据导出；保存格式与 `.icink` 的差异。
11. XComponent 原生 `x/y`、`size` 的单位（px/vp/归一化）；`DispatchTouchEvent` 与 ArkUI `onTouch` 同时挂载是否重复/冲突。
12. `UIContext.addLocalInputEventMonitor` 是否能收到窗口外/无焦点窗口的事件；`TOUCH_DOWN/UP` 是否包含笔；能否拦截系统手势。
13. 普通签名应用创建 `TYPE_FLOAT` 的实际错误码与是否有企业/教育白名单通道；`闪控窗` 是否可用于浮动栏（不是全屏批注）。
14. 无焦点浮动窗口的 `onKeyEvent` 行为；是否存在三方可用的全局快捷键 API。
15. API 24 设备上 `canIUse('SystemCapability.Stylus.Handwrite')` 的返回值；无笔设备是否返回 false。
16. 触控板单指/多指在 `onTouch` 与 `onMouse` 中的重复分发；`SourceTool.TOUCHPAD` 的实际值。
17. `DisplaySync` 在 120Hz 屏幕上的实际回调频率；与 `StylusFrameBoost` 同用是否冲突。
18. 26.0.0 Release 与本机 26.0.1 Beta1 SDK 在 Pen Kit/输入字段上的差异（本报告 B 级证据来自 Beta1 SDK）。

---

## 10. 参考来源

### 10.1 vendored 知识库（A 级，必读）

- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\pen-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\system\references\input-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\system\references\input-multimodal-kit.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\ui-development.md`
- `D:\InkCanvas\.claude\skills\harmony-os\dev\references\arkts.md`

### 10.2 vendored 知识库（旁证）

- `dev/references/ndk-development.md`（XComponent surface/触屏事件）
- `samples/references/graphics.md`（Canvas/XComponent/DisplaySync 示例）
- `design/references/hmi-system-features.md`（输入方式分类、交互事件归一）
- `design/references/multi-device.md`（手写笔压感、键鼠适配、44dp 热区）
- `design/references/ux-best-practices.md`（键鼠适配）
- `system/references/test-kit.md`（UITest 手写笔模拟）
- `system/references/device-info.md`（deviceInfo 基础字段）
- `dev/references/multi-device.md`（deviceType 分流）
- `VENDOR.md`（它把“触摸事件”指向 ui-development.md，但该文件没有触摸事件内容，属于知识库缺口）

### 10.3 本机 SDK 26.0.1 Beta1（B 级，只读核对）

- `...\openharmony\ets\component\common.d.ts` — `BaseEvent`、`TouchObject`、`TouchEvent`、`HistoricalPoint`、`HoverEvent`、`MouseEvent`、`KeyEvent`、`RawInputEventWrapper`、`onTouch/onHover/onMouse/onKeyEvent/onAxisEvent`
- `...\openharmony\ets\component\enums.d.ts` — `TouchType`、`SourceType`、`SourceTool`、`MouseButton`、`MouseAction`、`KeyType`、`KeySource`、`InputEventSubTypeMask`
- `...\openharmony\ets\api\@ohos.arkui.UIContext.d.ts` — `addLocalInputEventMonitor` / `removeLocalInputEventMonitor`
- `...\openharmony\ets\api\@ohos.multimodalInput.inputDevice.d.ts` — `InputDeviceData`、`getDeviceList/getDeviceInfo`
- `...\openharmony\ets\api\@ohos.multimodalInput.pointer.d.ts` — 光标样式/自定义光标
- `...\openharmony\ets\api\@ohos.multimodalInput.inputEventClient.d.ts` — 输入注入
- `...\openharmony\ets\api\@ohos.multimodalInput.touchEvent.d.ts` — 系统触摸事件字段
- `...\openharmony\ets\api\@ohos.deviceInfo.d.ts` — `sdkApiVersion` 等
- `...\openharmony\ets\api\@ohos.graphics.displaySync.d.ts` — `create/setExpectedFrameRateRange/on('frame')`
- `...\openharmony\ets\api\@ohos.window.d.ts` — `WindowType.TYPE_FLOAT`、`createWindow` 权限
- `...\openharmony\native\sysroot\usr\include\ace\xcomponent\native_interface_xcomponent.h` — XComponent 原生 touch/mouse/hover、toolType、tilt、size/force
- `...\hms\ets\api\@hms.stylus.HandwriteComponent.d.ets` — `HandwriteComponent`、`HandwriteController`、`PenType`
- `...\hms\ets\api\@hms.stylus.handwrite.d.ts` — `PointPredictor`、`InstantShapeGenerator`、`StylusFrameBoost`
- `...\hms\ets\api\@hms.officeservice.stylusInteraction.d.ts` — 捏笔/双击/笔身传感器
- `...\hms\native\sysroot\usr\include\handwrite\native_handwrite_api.h` — `HMS_HandWrite_*`
- `...\openharmony\toolchains\lib\PermissionDefinitions.json` — 权限等级
- `...\sdk\default\sdk-pkg.json` — SDK 版本 26.0.1 Beta1

### 10.4 项目内旁证

- `D:\InkCanvas\src\InkCanvas.Platform\Input\PalmRejector.cs`
- `D:\InkCanvas\src\InkCanvas.Core\Input\TouchContactClassifier.cs`
- `D:\InkCanvas\src\InkCanvas.Platform\Windows\LayeredInkWindow.cs`（`OverlayPointer` 字段）
- `D:\InkCanvas\src\InkCanvas.Core\Settings\AppSettings.cs`（特殊屏设置键）
- `D:\InkCanvas\docs\superpowers\specs\2026-11-18-harmonyos-port-design.md`（compat 层约定）

---

## 11. 一句话结论

**ArkUI 已经能提供 InkCanvas 所需的压感、倾斜、接触面积与高频率历史点（API 24/26 均可）；Pen Kit 的成品画布不适合作为核心，但其 `PointPredictor`、`InstantShapeGenerator` 与 API 26 帧加速可用于自绘管线；真正需要产品决策的是“跨应用覆盖批注”在 HarmonyOS 三方权限模型下不可行，以及手掌误触必须应用层自建。**
