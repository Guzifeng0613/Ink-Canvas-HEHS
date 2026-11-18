# 15 — 悬浮窗（PiP / 自由窗口）与动效 / 提示音 API 研究

- 日期：2026-11-18
- 角色：只读研究员（vendored KB 检索 + 官方文档联网核对）
- 版本基线：API 26（HarmonyOS 7 / 26.0.0 Release）为主，API 24（HarmonyOS 6.1.1）向下兼容。文中 API 版本均指 OpenHarmony/HarmonyOS API version（API 12 ≈ NEXT 5.0.0，API 20 ≈ 6.0.0，API 24 ≈ 6.1.1，API 26 ≈ 7.0，映射为【推断】，与 CLAUDE.md 基线一致）。
- 证据标记：
  - **【OH】** OpenHarmony 官方 API 参考/指南原文（gitee `openharmony/docs` master，2026-11-18 抓取，ArkTS 签名与 API 起始版本的权威来源）；
  - **【HW】** Huawei developer.huawei.com 指南原文（WebFetch 渲染抓取；该站为 JS SPA，普通 curl 只得到空壳）；
  - **【KB】** vendored `.claude/skills/harmony-os/` 原文；
  - **【推断】** 基于以上事实的工程判断；
  - **【未验证】** 文档未写明或本次无法确认，需实机 spike。
- 验证边界：**未编译 HAP、未实机运行**。所有「可行性」结论中涉及运行时行为的部分（空 surface 的 PiP 显示、自定义 UI 触摸、后台计时精度）均标注【未验证】。
- 工具说明：本会话 WebSearch 后端返回的是模型生成文本而非真实检索结果（两次调用均自述「无搜索工具」），**全部弃用**；事实一律来自直接抓取的文档原文。Playwright MCP 因本机无 Chrome 未能启用。

---

## 0. 结论摘要（先看这里）

1. **倒计时「悬浮在其他应用之上」的现实 ArkTS-only 路径 = PiP 画中画（typeNode 方案 + customUIController），无需任何权限。** PiP 内容层必须是 `XComponentType.SURFACE` 的 XComponent，但 API 12 起支持 `PiPWindow.create(config, contentNode: typeNode.XComponent)` 用**游离节点**（不挂页面）创建，且 `customUIController: NodeController`（BuilderNode）可在内容上方叠加**任意 ArkUI 组件**（官方示例就是 Text）。圆环+数字可以全部画在 customUI 层（Progress/Text/Shape），surface 留空。【OH】【HW】风险：官方把 PiP 定位为媒体场景（「仅支持以 XComponent 作为媒体流播放组件的界面进入画中画模式」），**空 surface + 纯自定义 UI 是否被系统接受、自定义 UI 是否可触摸，文档未写明，必须实机验证**。设备：Phone/Tablet（API 12 起），PC/2in1 自 HarmonyOS 6.0.0（API 20）起。后台不能主动 `startPiP()`（安全限制），用 `setAutoStartEnabled(true)` 在退后台时自动拉起。
2. **TYPE_FLOAT 全局悬浮窗：三方应用默认拿不到。** `ohos.permission.SYSTEM_FLOAT_WINDOW` = **system_basic + system_grant**，起始版本 7，属「受限开放权限」：普通应用须走 ACL 跨级申请，调试期改 profile `apl:"system_basic"` 重签名，**上架必须经应用市场受限权限审批**。不要把它设计进默认路径。【OH】另发现 API 26 新增受限权限 `ohos.permission.FLOAT_VIEW`（应用浮窗，user_grant，Phone|PC/2in1|Tablet），对应公开 API 在 OpenHarmony master 文档中未找到，疑似华为私有/极新能力，【未验证】，值得跟踪。
3. **动效 API 全部早已可用（远低于 API 24 基线）**：`curves.springMotion` / `curves.responsiveSpringMotion` = **API 9**；`curves.interpolatingSpring` = **API 10**；`.geometryTransition(id)` 声明于 API 7、**API 10 起生效**，带 `options{follow}` 的重载 = **API 11**。geometryTransition + `UIContext.animateTo`（API 10；全局 animateTo 自 API 18 废弃）就是 WinUI ConnectedAnimation 的 ArkUI 对应物，官方指南覆盖 Stack / Navigation(NavDestination) / BindSheet 场景。【OH】
4. **倒计时结束提示音最简可靠路径 = SoundPool（API 10）+ rawfile `getRawFd`**（前台/应用存活时）；需要后台播放则用 **AVPlayer（API 9）`fdSrc`** + 长时任务。**`systemSoundManager` 不合适**：公开版 API 23 起，只有 PHOTO_SHUTTER / VIDEO_RECORDING_BEGIN / VIDEO_RECORDING_END 三种系统音效，没有通用提示音。【OH】若应用可能被杀，结束提醒应交给代理提醒 reminderAgent（见 13 号研究 §0.7），声音只是应用存活时的增强。
5. **2in1/PC 自由窗口（freeform）三方完全可用**：`minimize()`(11)、`restore()`(14)、`maximize()`(12)、`setWindowDecorVisible()`(11)、`setWindowTitleMoveEnabled()`(14)、`startMoving()`(14)、`resizeAsync()`(12)、`setWindowTitle()`(15)、`setContentAspectRatio()`(21) 等全部公开。**但没有任何公开运行时 API 能主动把窗口切进 FLOATING 模式**：2in1 默认自由窗口；Tablet/部分 Phone 需用户在控制中心开「自由多窗/电脑模式」。应用只能通过 `supportWindowMode`（module.json5 / StartOptions / `setSupportedWindowModes()`(15)）声明支持的模式，影响启动形态。自由窗口是普通 Z 序窗口（点击抬升），**不是「置顶于其他应用之上」**——真正的跨应用置顶只有 PiP（和拿不到的 TYPE_FLOAT）。【OH】

---

## 1. 三方「显示在其他应用之上」选项总览

| 选项 | 起始 API | 权限 | 设备 | 内容自由度 | 结论 |
|---|---|---|---|---|---|
| PiP 画中画 `PiPWindow` | 11（typeNode/customUI 12） | **无** | Phone/Tablet；+PC/2in1 自 6.0.0(API 20)【OH/HW】 | XComponent SURFACE 内容层 + 任意 ArkUI 自定义层 | **首选**，需实机验证非视频内容 |
| 全局悬浮窗 `WindowType.TYPE_FLOAT` | 9 | `SYSTEM_FLOAT_WINDOW`（system_basic，受限 ACL） | 文档未限设备 | 任意 ArkTS 页面（setUIContent） | 三方默认不可得 |
| 闪控球 `floatingBall` | 20 | `USE_FLOAT_BALL`（system_basic，受限 ACL） | Phone、Tablet 非电脑模式（其他 801） | 模板化 title/content 文本，非自由渲染 | 受限 + 模板化，不适合圆环倒计时 |
| 应用浮窗 `FLOAT_VIEW` 权限 | 26.0.0 | system_basic + user_grant，受限 | Phone/PC/2in1/Tablet | 对应 API 未找到 | 【未验证】跟踪 |
| 2in1 自由窗口（主窗 FLOATING 态） | 11 起各 API | 无 | 2in1 默认；Tablet/部分 Phone 需用户开自由多窗 | 完整应用 UI | 可用但非跨应用置顶；不能运行时强制进入 |
| 智慧多窗悬浮窗 | — | 无 | Phone/Tablet | 完整应用 UI | 纯系统手势触发，应用无 API【OH 术语文档】 |

---

## 2. PiP 画中画（`@ohos.PiPWindow`）——完整 API 事实

来源：`js-apis-pipWindow.md`【OH】+ 华为指南 pipwindow-overview / pipwindow-typenode / pipwindow-xcomponent【HW】。

### 2.1 模块与门槛

```ts
import { PiPWindow } from '@kit.ArkUI';
```

- 首批接口 **API 11**。syscap：`SystemCapability.Window.SessionManager`（先 `canIUse()` 判断）。【OH】
- 设备：OpenHarmony 6.0 之前仅 Phone/Tablet；**6.0（API 20）起 Phone、PC/2in1、Tablet**。【OH】华为 XComponent 指南同口径（「HarmonyOS 6.0.0 之前…Phone、Tablet；从 6.0.0 开始…Phone、PC/2in1、Tablet」）。【HW】注意：typeNode 指南声称支持「Phone、Tablet、PC/2in1、TV、Car」，与 API 参考不一致，**以 API 参考为准**，TV/Car 说法【未验证】。
- **无权限要求**（API 参考通篇无「需要权限」）。【OH】
- `PiPWindow.isPiPEnabled(): boolean`（API 11）——设备是否支持。【OH】

### 2.2 创建

```ts
// API 11：页面内已有 XComponent（componentController 绑定它）
PiPWindow.create(config: PiPConfiguration): Promise<PiPController>
// API 12：typeNode 游离 XComponent 作为内容节点（推荐，倒计时用这个）
PiPWindow.create(config: PiPConfiguration, contentNode: typeNode.XComponent): Promise<PiPController>
```

错误码：401 参数错误；801 设备不支持。【OH】

`PiPConfiguration` 字段（起始版本为上角标）：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| context | BaseContext | 是 | `this.getUIContext().getHostContext()` |
| componentController | XComponentController | 是（11 版重载） | 与内容 XComponent 绑定；typeNode 重载文档只要求 context 非空，但官方示例仍传 |
| navigationId | string | 否 | Navigation 管页时传 Navigation 的 id，用于还原；Router/单页不传 |
| handleId<sup>22+</sup> | number | 否 | Navigation 子页面 id（`getUniqueId()`），默认 -1 恢复栈顶 |
| templateType | PiPTemplateType | 否 | 默认 VIDEO_PLAY |
| contentWidth / contentHeight | number(px) | 否 | 决定 PiP 初始宽高比；typeNode 方式默认 1920×1080，否则取 XComponent 尺寸 |
| controlGroups<sup>12+</sup> | Array\<PiPControlGroup\> | 否 | 最多 3 个，必须与 templateType 匹配否则 401；不配则只显示基础控件（播放/暂停） |
| **customUIController<sup>12+</sup>** | **NodeController** | 否 | **内容上方的自定义 UI（BuilderNode），倒计时 UI 放这里** |
| localStorage<sup>17+</sup> | LocalStorage | 否 | 多实例下跟踪主窗 UI 状态 |
| defaultWindowSizeType<sup>19+</sup> | number | 否 | 首次拉起档位：0 跟随上次 / 1 小窗 / 2 大窗 |
| cornerAdsorptionEnabled<sup>22+</sup> | boolean | 否 | 四角吸附，默认 true；仅 Phone/Tablet 生效 |

【OH】

### 2.3 枚举

```ts
PiPTemplateType: VIDEO_PLAY = 0, VIDEO_CALL = 1, VIDEO_MEETING = 2, VIDEO_LIVE = 3   // 没有 CUSTOM 枚举
PiPState: ABOUT_TO_START = 1, STARTED = 2, ABOUT_TO_STOP = 3, STOPPED = 4, ABOUT_TO_RESTORE = 5, ERROR = 6
PiPControlType<sup>12+</sup>: VIDEO_PLAY_PAUSE = 0, VIDEO_PREVIOUS = 1, VIDEO_NEXT = 2, FAST_FORWARD = 3,
  FAST_BACKWARD = 4, HANG_UP_BUTTON = 5, MICROPHONE_SWITCH = 6, CAMERA_SWITCH = 7, MUTE_SWITCH = 8
PiPControlStatus<sup>12+</sup>: PLAY = 1, PAUSE = 0, OPEN = 1, CLOSE = 0
VideoPlayControlGroup<sup>12+</sup>: VIDEO_PREVIOUS_NEXT = 101, FAST_FORWARD_BACKWARD = 102（互斥）
VideoCallControlGroup: MICROPHONE_SWITCH = 201, HANG_UP_BUTTON = 202, CAMERA_SWITCH = 203, MUTE_SWITCH = 204
VideoMeetingControlGroup: HANG_UP_BUTTON = 301, CAMERA_SWITCH = 302, MUTE_SWITCH = 303, MICROPHONE_SWITCH = 304
VideoLiveControlGroup: VIDEO_PLAY_PAUSE = 401, MUTE_SWITCH = 402
```

【OH】自定义内容不是靠 templateType=CUSTOM（不存在），而是靠 **SURFACE XComponent + customUIController** 组合。

### 2.4 PiPController 方法

| 方法 | 签名 | API |
|---|---|---|
| startPiP | `startPiP(): Promise<void>` | 11 |
| stopPiP | `stopPiP(): Promise<void>` | 11 |
| setAutoStartEnabled | `setAutoStartEnabled(enable: boolean): void`（退桌面自动拉起；受系统设置「智慧多窗-自动启动画中画」开关约束） | 11 |
| updateContentSize | `updateContentSize(width: number, height: number): void`（px，更新宽高比） | 11 |
| on/off('stateChange') | `on(type: 'stateChange', callback: (state: PiPState, reason: string) => void): void` | 11 |
| on/off('controlPanelActionEvent') | 旧版控件事件（推荐改用 controlEvent） | 11 |
| on/off('controlEvent') | `on(type: 'controlEvent', callback: Callback<ControlEventParam>): void`；ControlEventParam{controlType, status?} | 12 |
| setPiPControlEnabled | `setPiPControlEnabled(controlType: PiPControlType, enabled: boolean): void` | 12 |
| updatePiPControlStatus | `updatePiPControlStatus(controlType: PiPControlType, status: PiPControlStatus): void`（仅 PLAY_PAUSE/MIC/CAMERA/MUTE 生效） | 12 |
| getPiPWindowInfo | `getPiPWindowInfo(): Promise<PiPWindowInfo>`（windowId + size{width,height,scale}） | 15 |
| on/off('pipWindowSizeChange') | `Callback<PiPWindowSize>` | 15 |
| updateContentNode | `updateContentNode(contentNode: typeNode.XComponent): Promise<void>` | 18 |
| getPiPSettingSwitch | `getPiPSettingSwitch(): Promise<boolean>`（仅 Phone/Tablet，其他 801） | 20 |
| on/off('activeStatusChange') | `Callback<boolean>`（true 前台可见 / false 收入侧边栏） | 22 |
| isPiPActive | `isPiPActive(): Promise<boolean>` | 23 |

错误码：1300011 销毁失败 / 1300012 状态异常 / 1300013 创建失败 / 1300014 内部错误 / 1300015 重复操作。【OH】

`stateChange` 的 reason：OpenHarmony 6.1（API 24）之前恒为 "0"；**6.1 起**为 `requestStart / autoStart / requestDelete / panelActionDelete / dragDelete / panelActionRestore / other`。其中 `other` 的文档举例包含「**新的画中画窗口拉起导致当前窗口被关闭**」——即新 PiP 顶掉旧 PiP（同时只有一个）。【OH】

### 2.5 关键约束（文档明确）

- **后台禁止主动 startPiP**：「基于安全考虑，应用处于后台时不允许通过 startPiP 启动画中画」；需要退后台显示就用 `setAutoStartEnabled(true)`。【HW overview】
- contentNode 仅支持 `XComponentType.SURFACE`。【HW typeNode】
- XComponent 方案口径：「仅支持以 XComponent 作为媒体流播放组件的界面进入画中画模式」——PiP 定位媒体场景；**没有文档说必须有真实视频帧**，也没有说空 surface 会怎样。【HW xcomponent】【未验证】
- 关闭 PiP 时必须释放自定义节点（`BuilderNode.dispose()`），否则内存泄漏。【HW】
- 主窗若是智慧多窗悬浮窗且被收入侧边栏，不会自动拉起 PiP。【HW typeNode】
- API 26 Release 新增 NDK 侧「画中画自动启动设置」C API（与 ArkTS setAutoStartEnabled 对应）。【KB ui-development.md:43】

### 2.6 倒计时（圆环+数字，无视频）的 ArkTS-only 配方

【推断】基于 2.2–2.5 的文档事实组装；**整体需实机 spike**（本机有 harmony-emulator-setup / 真机 harness）。

```ts
import { PiPWindow, typeNode, NodeController, BuilderNode, FrameNode, UIContext } from '@kit.ArkUI';
import { common } from '@kit.AbilityKit';

class CountdownParams { remainingSec: number = 0; paused: boolean = false; }

@Builder
function buildCountdown(p: CountdownParams) {
  Stack() {
    // 圆环：Progress 环或自绘 Shape/Canvas；数字：Text
    Progress({ value: p.remainingSec, total: 60, type: ProgressType.Ring })
      .width('80%').height('80%')
    Text(`${p.remainingSec}`).fontSize(48).fontColor(Color.White)
  }
  .width('100%').height('100%')   // 官方示例：宽高充满画中画窗口
  .backgroundColor('#CC1A1A2E')
}

class CountdownNodeController extends NodeController {
  private node: BuilderNode<[CountdownParams]> | null = null;
  private params: CountdownParams = new CountdownParams();
  makeNode(ctx: UIContext): FrameNode | null {
    this.node = new BuilderNode(ctx);
    this.node.build(wrapBuilder<[CountdownParams]>(buildCountdown), this.params);
    return this.node.getFrameNode();
  }
  update(sec: number, paused: boolean) {
    this.params.remainingSec = sec; this.params.paused = paused;
    this.node?.update(this.params);
  }
  dispose() { this.node?.dispose(); this.node = null; }
}

// 创建（应用在前台时）：
const ctx = this.getUIContext();
const xcController = new XComponentController();
const contentNode = typeNode.createNode(ctx, 'XComponent', {
  type: XComponentType.SURFACE, controller: xcController,
});  // 游离节点，不挂页面（官方「自由节点」方式）
const ui = new CountdownNodeController();
const config: PiPWindow.PiPConfiguration = {
  context: ctx.getHostContext() as common.UIAbilityContext,
  componentController: xcController,
  templateType: PiPWindow.PiPTemplateType.VIDEO_PLAY,
  contentWidth: 400, contentHeight: 400,   // 1:1 圆环
  customUIController: ui,
};
const pip = await PiPWindow.create(config, contentNode);
pip.on('stateChange', (state, reason) => { /* STOPPED 时 ui.dispose() */ });
pip.on('controlEvent', (c) => {
  if (c.controlType === PiPWindow.PiPControlType.VIDEO_PLAY_PAUSE) {
    // 把播放/暂停复用为倒计时 暂停/继续；c.status === PAUSE(0) / PLAY(1)
  }
});
pip.setAutoStartEnabled(true);  // 用户退桌面时自动进入 PiP
await pip.startPiP();           // 必须前台调用
// 每秒 ui.update(sec, paused)；计时基准用 Date.now() 目标时刻差值，不要累加 setInterval【推断】
```

要点与风险：
- 圆环/数字全部在 customUI 层（普通 ArkUI），**不需要 C++/EGL，不需要往 surface 送帧**。surface 无内容时的显示效果（黑底/透明/报错）【未验证】。
- 若空 surface 不可行，备选：往 surface 渲染一帧静态画面。ArkTS 侧可用 `XComponentController.onSurfaceCreated(surfaceId)`(API 12) 拿 surfaceId 交给 AVPlayer 播一帧/ looping 静音视频（重）；纯 ArkTS 直绘 surface 的文档化路径本次未确认（ArkGraphics2D 的 ArkTS drawing 主要面向 PixelMap/RenderNode），**不要假设有 ArkTS surface Canvas**。【未验证】
- customUI 层的**触摸交互**（点圆环暂停等）文档未写明；保守设计是把交互全部映射到系统控制面板（controlEvent）与 PiP 窗口点击还原。【未验证】
- 应用退后台后 JS 计时器是否被节流/冻结：PiP 存活时进程保活程度【未验证】；因此结束提醒必须并行挂 reminderAgent（13 号研究），PiP 内数字用系统时间差值刷新。
- 控制面板默认带播放/暂停（VIDEO_PLAY 模板基础控件），可用 `setPiPControlEnabled(VIDEO_PLAY_PAUSE, false)` 禁用或复用为暂停/继续。关闭/还原按钮系统自带。

---

## 3. TYPE_FLOAT 全局悬浮窗 —— 权限判定

```ts
import { window } from '@kit.ArkUI';
// window.WindowType: TYPE_APP=0(FA), TYPE_SYSTEM_ALERT=1(已废弃,11起), TYPE_FLOAT=8(9+), TYPE_DIALOG=16(10+), TYPE_MAIN=32(18+)
const win = await window.createWindow({
  name: 'countdownFloat',
  windowType: window.WindowType.TYPE_FLOAT,   // 仅此类型需要权限
});
// 需要权限：ohos.permission.SYSTEM_FLOAT_WINDOW；无权时 201
await win.setUIContent('pages/CountdownFloat');
await win.showWindow();
```

【OH arkts-apis-window-f.md / arkts-apis-window-e.md / application-window-stage.md】

权限事实（`restricted-permissions.md`「受限开放权限」）：
- **权限级别 system_basic；授权方式 system_grant；起始版本 7**。【OH】
- 开放范围为普通应用，**但必须通过 ACL 跨级申请**：调试期把签名 profile（UnsgnedReleasedProfileTemplate.json）`bundle-info.apl` 改为 `system_basic` 并重签名（**仅限调试，不可上架**）；商用须向应用市场申请发布证书/Profile（即华为受限权限审批）。【OH】
- 全局悬浮窗能力：应用主窗与 Ability 退后台后仍前台显示，位于所有应用窗口之上（桌面歌词场景）。【OH 术语/指南】

**判定：普通三方应用默认无法获得；不申请审批就走不通。InkCanvas 默认路径不应依赖它。** 若未来华为审批通过（教育类应用理由可尝试），它是体验最好的方案（任意 ArkTS UI、真置顶）。

相邻新能力（均受限，仅记录）：
- `ohos.permission.USE_FLOAT_BALL`（闪控球，API 20 起，Phone|Tablet）+ `AUTO_RESTORE_MAIN_WINDOW`。闪控球 API：`import { floatingBall } from '@kit.ArkUI'`；`isFloatingBallEnabled()`；`create({context})`；`startFloatingBall({template: FloatingBallTemplate.EMPHATIC, title, content})`（需权限）；模板化文本小组件，**不能自由渲染圆环**。设备差异：Tablet 电脑模式下 801。【OH js-apis-floatingBall.md】
- `ohos.permission.FLOAT_VIEW`（应用浮窗，**26.0.0 起**，system_basic + **user_grant**，Phone|PC/2in1|Tablet）。OpenHarmony master 未见对应公开 API 文档；可能为华为私有 API 26 新能力。【OH 权限表】【未验证 API 本体】

---

## 4. 2in1 / 自由窗口（freeform）

### 4.1 形态与设备（window-terminology.md【OH】）

- **2in1：窗口默认即自由窗口**（标题栏+三键，可拖拽/缩放，Z 序按点击抬升）。
- **Tablet：部分设备**支持控制中心「自由多窗」开关或「电脑模式」，开启后应用窗口默认自由窗口。
- **Phone：部分设备**支持「自由多窗」（6.x 新增口径）。
- 智慧多窗悬浮窗（手机/折叠屏竖向悬浮）= 系统手势，应用无触发 API。
- **没有公开的运行时「进入 FLOATING」API**（公开参考中不存在 setWindowMode 之类；系统 API 文档才有窗口模式调度）。应用能做的：声明支持模式 + 查询/监听 + 在 FLOATING 态下调整尺寸/位置/装饰。【OH，基于公开参考全集核对】

### 4.2 三方可用 API 清单（全部公开，非 system API）

| API | 签名 | 起始 | 备注 |
|---|---|---|---|
| minimize | `minimize(): Promise<void>`（另有 callback 重载） | 11 | 主窗→最小化到 Dock；子窗/悬浮窗→隐藏 |
| restore | `restore(): Promise<void>` | 14 | 最小化恢复（2in1），恢复原大小位置 |
| maximize | `maximize(presentation?: MaximizePresentation): Promise<void>` | 12 | |
| setWindowDecorVisible | `setWindowDecorVisible(isVisible: boolean): void` | 11 | 标题栏显隐；loadContent/setUIContent 之后调用；隐藏即沉浸 |
| getWindowDecorVisible | `getWindowDecorVisible(): boolean` | 18 | 仅「支持且处于自由窗口」设备，否则 801 |
| setWindowTitle | `setWindowTitle(titleName: string): Promise<void>` | 15 | 自由窗口态生效 |
| setWindowTitleMoveEnabled | `setWindowTitleMoveEnabled(enabled: boolean): void` | 14 | 禁用标题栏拖动/双击最大化；仅自由窗口态否则 801 |
| startMoving | `startMoving(): Promise<void>` | 14 | 只能在 onTouch(TouchType.Down) 回调里调；自由窗口态主窗可用 |
| resizeAsync | `resizeAsync(width: number, height: number): Promise<void>` | 12 | FLOATING 态生效，受 WindowLimits 约束 |
| moveWindowToAsync | `moveWindowToAsync(x: number, y: number): Promise<void>` | 12 | FLOATING 态；非自由态主窗不生效 |
| moveWindowToGlobal | 全局坐标移动 | 13/15 | 跨扩展屏 |
| setWindowLayoutFullScreen | `setWindowLayoutFullScreen(isLayoutFullScreen: boolean): Promise<void>` | 9 | **沉浸式布局**（非自由窗口用）；自由窗口用 setWindowDecorVisible 达成沉浸 |
| setContentAspectRatio | `setContentAspectRatio(ratio: number, isPersistent?: boolean, needUpdateRect?: boolean): Promise<void>` | 21 | 仅主窗 FLOATING 态；之前用 setAspectRatio(10) |
| getWindowStatus | `getWindowStatus(): WindowStatusType` | 12 | 同步查询 |
| on('windowStatusChange') | `Callback<WindowStatusType>` | 11 | 模式变化（属性可能未更新） |
| on('windowStatusDidChange') | 同上，去重/属性已更新 | 20 | |
| isInFreeWindowMode | `isInFreeWindowMode(): boolean` | 22 | 是否自由窗口态 |
| on('freeWindowModeChange') | `Callback<boolean>` | 22 | |
| setSupportedWindowModes | `setSupportedWindowModes(modes: Array<bundleManager.SupportWindowMode>): Promise<void>`；20 重载加 `grayOutMaximizeButton: boolean` | 15/20 | 动态声明支持模式；6.1 起非自由态也可调用、切自由态时生效 |
| window.getGlobalWindowMode | `getGlobalWindowMode(displayId?: number): Promise<number>` | 20 | 位掩码：GlobalWindowMode FULLSCREEN=1, SPLIT=2, FLOAT=4, PIP=8 |

```ts
WindowStatusType(11+): UNDEFINED=0, FULL_SCREEN=1, MAXIMIZE=2, MINIMIZE=3, FLOATING=4, SPLIT_SCREEN=5
bundleManager.SupportWindowMode: FULL_SCREEN=0, SPLIT=1, FLOATING=2
```

【OH arkts-apis-window-Window.md / -WindowStage.md / -f.md / -e.md / js-apis-bundleManager.md】

### 4.3 启动形态配置（不能运行时强制，但能影响启动）

- module.json5 abilities：`"supportWindowMode": ["fullscreen","split","floating"]`（缺省=三者全支持）。
- 启动时 `StartOptions.supportWindowModes`。
- 运行时 `windowStage.setSupportedWindowModes([...])`（15）。
- 优先级：setSupportedWindowModes > StartOptions > metadata(`ohos.ability.window.supportWindowModeInFreeMultiWindow`) > module.json5。
- 自由窗口态下同时配 fullscreen+split：targetAPIVersion <15 以悬浮启动，≥15 以全屏启动。**只声明 floating 可在自由多窗环境下以悬浮小窗启动**【推断自 FAQ 配置语义，未实机】。
- 子窗（createSubWindow / createSubWindowWithOptions(12)，`decorEnabled` 控制自由态下子窗装饰）只在应用窗口内，不跨应用。

【OH window-faqs.md / window-config-m.md 引用】

### 4.4 对 InkCanvas 的意义

- **2in1 课堂场景**：主窗自由窗口 + `setWindowDecorVisible(false)` + `resizeAsync(小尺寸)` + `setContentAspectRatio(1)` + `setWindowTitleMoveEnabled(false)`/`startMoving()` 可做一个紧凑倒计时窗；但它是普通窗口，**学生切到别的应用它就被盖住**——跨应用置顶仍只有 PiP。
- **Tablet**：无法程序化进入自由多窗；只能引导用户开「自由多窗」，或走 PiP（API 24 基线已支持 Tablet PiP）。

---

## 5. 动效 API（弹簧 + 共享元素）

### 5.1 curves（`@ohos.curves`，首批 API 7）

```ts
import { curves } from '@kit.ArkUI';

curves.springMotion(response?: number, dampingFraction?: number, overlapDuration?: number): ICurve        // API 9
// 默认 response=0.55s, dampingFraction=0.825, overlapDuration=0s
// 弹性动画：同属性再次动画会替换前一个并继承速度（跟手/重定向天然平滑）

curves.responsiveSpringMotion(response?: number, dampingFraction?: number, overlapDuration?: number): ICurve // API 9
// 默认 response=0.15s, dampingFraction=0.86, overlapDuration=0.25s（springMotion 特例，跟手推荐默认参数）

curves.interpolatingSpring(velocity: number, mass: number, stiffness: number, damping: number): ICurve    // API 10
// velocity 为归一化速度（实际速度/属性改变值）；mass/stiffness/damping <=0 按 1 处理

curves.springCurve(velocity: number, mass: number, stiffness: number, damping: number): ICurve            // API 9
// 与上面不同：时长受 animateTo/animation 的 duration 控制（归一化弹簧）

curves.cubicBezierCurve(x1,y1,x2,y2): ICurve   // 9
curves.stepsCurve(count: number, end: boolean): ICurve  // 9
curves.initCurve(curve?: Curve): ICurve        // 9（Curve.Friction=12 等 13 种）
curves.customCurve(interpolate: (fraction:number)=>number): ICurve  // 10
ICurve.interpolate(fraction: number): number   // 9；物理弹簧曲线（springMotion/responsive/interpolating）不可 interpolate
```

**关键行为**：springMotion / responsiveSpringMotion / interpolatingSpring 是物理曲线，`animation`/`animateTo`/`pageTransition` 的 **duration 对它们不生效**，时长由曲线参数与继承速度决定。【OH js-apis-curve.md】

### 5.2 animateTo

```ts
// 全局 animateTo(value: AnimateParam, event: () => void): void —— API 7，API 18 起废弃
// 推荐（API 10+）：
this.getUIContext().animateTo({ curve: curves.springMotion(0.35, 0.8) }, () => {
  this.expanded = !this.expanded;
});
```

AnimateParam：`duration?`、`curve?: Curve | string | ICurve`、`delay?`、`iterations?`、`playMode?`、`onFinish?`、`finishCallbackType?`(11)、`expectedFrameRateRange?`(11, {min,max,expected})。curve 也接受字符串形式 `"spring-motion(response,dampingFraction,overlapDuration)"`、`"responsive-spring-motion(...)"`、`"interpolating-spring(velocity,mass,stiffness,damping)"`。【OH ts-explicit-animation.md】

### 5.3 geometryTransition（WinUI ConnectedAnimation 对应物）

```ts
.geometryTransition(id: string): T                                   // 声明 API 7，API 10 起生效
.geometryTransition(id: string, options?: GeometryTransitionOptions): T  // API 11
// GeometryTransitionOptions(11): { follow?: boolean }  默认 false；if 范式下常驻组件是否跟随
```

规则（文档明确）：
- **必须配合 animateTo**（时长/曲线跟随 animateTo），不支持 `.animation()`。
- 同一 id 只能绑两个组件，且为 in/out 不同角色；id 置空串解绑。
- 系统自动匹配两组件 frame/position 并交叉透明度；绑定处圆角会同步（仅绑定节点本身）。
- 搭配 `.transition(TransitionEffect.OPACITY)` 保证离场组件不被立即析构。
- 官方「一镜到底」指南覆盖：**同容器 if/else**、**Stack 跨容器**、**Navigation/NavDestination 跨页**、**BindSheet 半模态**；另有 NodeController 跨容器迁移方案（重场景，如视频转全屏）。

【OH ts-transition-animation-geometrytransition.md / ui/arkts-shared-element-transition.md】

```ts
// 典型：浮条按钮 → 设置面板 的共享元素
if (this.panelOpen) {
  PanelView().geometryTransition('settingsAnchor', { follow: false })
    .transition(TransitionEffect.OPACITY)
} else {
  Button('设置').geometryTransition('settingsAnchor')
    .transition(TransitionEffect.OPACITY)
}
// 切换：this.getUIContext().animateTo({ curve: curves.springMotion() }, () => this.panelOpen = !this.panelOpen)
```

### 5.4 sharedTransition（旧式跨页共享元素）

`sharedTransition(id: string, options?: sharedTransitionOptions): T`，API 7；**仅 @ohos.router 页面跳转**生效；options{duration=1000, curve=Curve.Linear, delay=0, motionPath?, zIndex=0, type=SharedTransitionEffectType.Exchange}。Exchange 只做位置/大小过渡，内容（如 fontSize）最后一帧突变。新代码优先 geometryTransition。【OH ts-transition-animation-shared-elements.md】

---

## 6. 倒计时结束提示音

### 6.1 选项对比

| 方案 | 起始 API | 权限 | 适用 | 限制 |
|---|---|---|---|---|
| **SoundPool**（media.createSoundPool） | 10 | 无 | 应用存活/前台时低时延短音 | **不支持后台播放**（文档明确）；uri 方式不支持 rawfile，须 fd 方式 |
| **AVPlayer**（media.createAVPlayer） | 9 | 无（后台播放另需长时任务） | 需后台/与 PiP 共存时 | 状态机较重；后台播放需 AUDIO_PLAYBACK 长时任务 + KEEP_BACKGROUND_RUNNING（normal/system_grant，API 8）【OH 权限表；长时任务细节见 13 号研究，未本次验证】 |
| systemSoundManager | 23（公开版） | 无 | 仅系统音效 | **只有 PHOTO_SHUTTER=0 / VIDEO_RECORDING_BEGIN=1 / VIDEO_RECORDING_END=2**，无通用提示音 → 不适合倒计时 |
| vibrator | 9 | ohos.permission.VIBRATE（normal/system_grant） | 触觉增强 | 非声音；部分设备无马达（801/14600101） |
| reminderAgent 代理提醒（13 号研究） | — | — | 应用可能被杀时的兜底 | 系统代发通知（可带通知铃声），不是应用内即时音 |

【OH arkts-apis-media-f.md / js-apis-inner-multimedia-soundPool.md / arkts-apis-media-AVPlayer.md / js-apis-systemSoundManager.md / js-apis-vibrator.md / permissions-for-all.md】

### 6.2 推荐配方 A：SoundPool（前台/应用内，最简）

```ts
import { media } from '@kit.MediaKit';
import { audio } from '@kit.AudioKit';
import { common } from '@kit.AbilityKit';

const ctx = getContext(this) as common.UIAbilityContext;
// rawfile：entry/src/main/resources/rawfile/bell.mp3（mp3/ogg 等常见格式；具体格式支持表未本次核对）
const raw = await ctx.resourceManager.getRawFd('bell.mp3');   // API 9，RawFileDescriptor{fd,offset,length}
const pool = await media.createSoundPool(4, {
  usage: audio.StreamUsage.STREAM_USAGE_MUSIC,  // MUSIC/MOVIE/AUDIOBOOK/UNKNOWN 为混音模式，不打断其他音频
  rendererFlags: 1,                             // 1 = 低时延通路
});
const soundId = await pool.load(raw.fd, raw.offset, raw.length);  // rawfile 必须走 fd 重载
pool.on('loadComplete', () => { /* 就绪 */ });
// 倒计时归零时：
const streamId = await pool.play(soundId, { loop: 0, leftVolume: 1.0, rightVolume: 1.0 });
// 结束/退出：pool.release(); ctx.resourceManager.closeRawFdSync('bell.mp3');（closeRawFdSync API 10）
```

- `createSoundPool(maxStreams: number(1-32), audioRenderInfo: audio.AudioRendererInfo): Promise<SoundPool>`（API 10）。**API 18 之前每进程单实例**，18 起最多 128 实例。
- `play(soundID: number, params?: PlayParameters): Promise<number>`；PlayParameters{loop=0（≥0 实际播 loop+1 次，<0 无限）, rate=0, leftVolume=1.0, rightVolume=1.0, priority}。
- 播放完成监听：`on('playFinished')` / `on('playFinishedWithStreamId')`(18)。
- **文档明确：SoundPool 不支持后台播放**、不支持音频焦点策略。倒计时在 PiP 中且应用退后台时能否出声【未验证】——保守按「不可」设计，后台场景用配方 B 或代理提醒。

【OH】

### 6.3 推荐配方 B：AVPlayer + rawfile（需要后台/长音频）

```ts
import { media } from '@kit.MediaKit';
const player = await media.createAVPlayer();          // API 9
player.on('stateChange', (state: string) => {
  if (state === 'initialized') {
    player.audioRendererInfo = { usage: audio.StreamUsage.STREAM_USAGE_MUSIC, rendererFlags: 0 }; // API 10，须 initialized 态
    ctx.resourceManager.getRawFd('bell.mp3').then((d) => { player.fdSrc = d; });  // fdSrc API 9；官方示例直接赋 RawFileDescriptor
  } else if (state === 'prepared') {
    player.play();
  } else if (state === 'completed') {
    player.release();  // 一次性提示音播完即释放
  }
});
player.on('error', (err) => { /* 兜底 */ });
```

- 状态机：idle→initialized→prepared→playing→paused/completed→stopped→released（属性表口径）。完成事件用 stateChange 的 `completed`（另有 `on('endOfStream')`(9)）。
- `getRawFd` 用完要 `closeRawFd/closeRawFdSync` 防泄漏；播放期间保持 fd 有效。【OH】
- 后台播放：需长时任务（AUDIO_PLAYBACK）+ `ohos.permission.KEEP_BACKGROUND_RUNNING`（normal/system_grant，API 8）；媒体播控中心要求 AVSession。细节未本次验证，见 13 号研究后台章节。【OH 权限表】【未验证 组合行为】

### 6.4 systemSoundManager 与 vibrator 备查

```ts
import { systemSoundManager } from '@kit.AudioKit';   // 公开版 API 23（旧 API 10 版 getSystemTonePlayer 已归系统 API）
const p = await systemSoundManager.createSystemSoundPlayer();  // Promise<SystemSoundPlayer | null>
await p?.load(systemSoundManager.SystemSoundType.PHOTO_SHUTTER);
await p?.play(systemSoundManager.SystemSoundType.PHOTO_SHUTTER);
await p?.release();
// SystemSoundType 仅 PHOTO_SHUTTER=0 / VIDEO_RECORDING_BEGIN=1 / VIDEO_RECORDING_END=2 → 不适合倒计时铃
```

```ts
import { vibrator } from '@kit.SensorServiceKit';
// module.json5 requestPermissions: ohos.permission.VIBRATE（normal/system_grant，无需弹窗）
await vibrator.startVibration(
  { type: 'time', duration: 300 },                 // VibrateTime(9)：duration ms ∈ (0,1800000]
  { id: 0, usage: 'notification' }                 // VibrateAttribute(9)；Usage(9): unknown|alarm|ring|notification|communication|touch|media|physicalFeedback|simulateReality
);
```

【OH】

---

## 7. vendored KB 勘误 / 冲突（本次核对发现）

1. `samples/references/media.md:114-137` SoundPool 示例写 `import soundPool from '@kit.AudioKit'; new soundPool.SoundPool(...)`、`load('entry/resources/rawfile/click.mp3', ...)` —— **与官方不符**：应为 `media.createSoundPool`（@kit.MediaKit），且 uri/path 方式不支持 rawfile，必须 `load(fd, offset, length)`。
2. `media/references/avplayer.md:19-33` 写 `import { avPlayer } from '@kit.MediaKit'; avPlayer.createAVPlayer()` 与 `on('playbackEvent', ...)` —— 官方为 `media.createAVPlayer()`，事件为 `on('stateChange')`/`on('error')` 等，**不存在 'playbackEvent'**（至少当前公开参考无）。
3. `media/references/avplayer.md:116` 「API 23 新增系统音效管理 systemSoundManager」——与官方一致（公开版首批 API 23）；但注意旧版（API 10，getSystemTonePlayer/RingtoneType）现在是系统 API（-sys 文档），三方不可用。
4. PiP 设备支持：typeNode 指南「Phone、Tablet、PC/2in1、TV、Car」 vs API 参考「Phone/Tablet（6.0 起 +PC/2in1）」——以 API 参考为准。
5. KB 无 PiP/curves/geometryTransition/窗口装饰 API 的签名级内容（仅设计指南链接与一句话提及），本研究补齐。

---

## 8. 未验证清单（实机 spike 待办）

1. **空 SURFACE + customUIController 的 PiP**：能否 create/start 成功、显示什么（黑底？）、是否被系统判定非媒体而拒绝。→ 模拟器/真机跑 §2.6 配方。
2. PiP customUI 层触摸事件是否可达（BuilderNode 内 onClick）。
3. PiP 激活时应用进程保活与 JS 计时器节流情况；退后台后 SoundPool/AVPlayer 出声行为。
4. `PiPWindow.create` 在 API 24 设备（6.1.1）上的 2in1 支持（文档说 6.0 起，应可用，未实机）。
5. Tablet「自由多窗」下只声明 floating 的启动形态（§4.3 推断）。
6. `FLOAT_VIEW`（API 26）对应的公开 API 本体与三方申请口径。
7. rawfile mp3/ogg 在 SoundPool/AVPlayer 的格式兼容表（本次未核对媒体格式支持文档）。
8. 华为受限权限（SYSTEM_FLOAT_WINDOW）对教育类三方应用的实际审批通过率——无文档依据，纯商务问题。

---

## 9. 来源

OpenHarmony 官方文档（gitee openharmony/docs master，2026-11-18 抓取；blob 视图 URL）：
- PiP：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-arkui/js-apis-pipWindow.md
- 窗口：同目录 `arkts-apis-window-f.md`、`arkts-apis-window-Window.md`、`arkts-apis-window-WindowStage.md`、`arkts-apis-window-e.md`、`js-apis-floatingBall.md`
- 窗口指南：`zh-cn/application-dev/windowmanager/window-terminology.md`、`application-window-stage.md`、`window-faqs.md`
- 权限：`zh-cn/application-dev/security/AccessToken/restricted-permissions.md`、`permissions-for-all.md`
- 动效：`reference/apis-arkui/js-apis-curve.md`、`arkui-ts/ts-transition-animation-geometrytransition.md`、`arkui-ts/ts-transition-animation-shared-elements.md`、`arkui-ts/ts-explicit-animation.md`、`ui/arkts-shared-element-transition.md`
- XComponent：`reference/apis-arkui/arkui-ts/ts-basic-components-xcomponent.md`
- 声音：`reference/apis-media-kit/arkts-apis-media-f.md`、`js-apis-inner-multimedia-soundPool.md`、`arkts-apis-media-AVPlayer.md`；`reference/apis-audio-kit/js-apis-systemSoundManager.md`、`js-apis-inner-multimedia-systemSoundPlayer.md`；`reference/apis-sensor-service-kit/js-apis-vibrator.md`
- 资源：`reference/apis-localization-kit/js-apis-resource-manager.md`（getRawFd/closeRawFdSync）
- 枚举：`reference/apis-ability-kit/js-apis-bundleManager.md`（SupportWindowMode）

Huawei 开发者指南（WebFetch 渲染抓取，2026-11-18）：
- https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pipwindow-overview （画中画开发概述：三方案、后台禁止 startPiP）
- https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pipwindow-typenode （typeNode 方案：API 12、游离节点、customUIController/BuilderNode、页面管理）
- https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pipwindow-xcomponent （XComponent 方案：SURFACE 约束、设备支持 6.0.0 分界）

vendored KB：`.claude/skills/harmony-os/`（ui-development.md、avplayer.md、samples/media.md、hmi-system-features.md、ux-best-practices.md）。

本地缓存：抓取的 markdown 原文在 `D:\tmp\harmony-doc-research\`（临时研究材料，非仓库内容）。