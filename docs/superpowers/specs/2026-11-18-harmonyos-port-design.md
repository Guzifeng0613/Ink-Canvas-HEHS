# Ink Canvas → HarmonyOS（ArkTS）移植设计

日期：2026-11-18
状态：**v1.0（研究完成，设计定稿）**
上游基线：`feat/winui3-migration` 工作树（3.4.0 轮）
知识库：`.claude/skills/harmony-os/`（vendored，上游 commit `4758f3c`）
研究附录：`docs/superpowers/research/harmony/01–13-*.md`（8 份源码盘点 + 5 份能力映射，共约 1.0 MB）

## 1. 目标与验收口径

把 WinUI3 版 Ink Canvas 移植为 HarmonyOS ArkTS 版，**覆盖 WinUI3 基线功能清单的 98%**，
并让两套实现在源码层面互认、可长期并行维护；界面保持一致。

| # | 用户要求 | 验收口径 |
|---|---|---|
| G1 | 配置 Harmony OS SKILL | `.claude/skills/harmony-os/` 已 vendored 并注册（`/harmony-os`），含版本基线与更新流程 |
| G2 | 迁移重构，覆盖 98% 功能 | `parity/manifest.json` 功能矩阵；覆盖率口径见 §9；不移植项逐项列出原因与替代路径 |
| G3 | 重点：桌面触摸、笔、平板；手机初步 | 平板与 PC/2in1 为完整功能面；手机为初步（核心书写/画板/计时/设置）；差异写入 `parity/api-matrix.md` |
| G4 | WinUI3 ↔ ArkTS 逻辑互认与连接 | 五机制：双向 `@parity` 锚点 + manifest 校验、共享黄金向量、共享数据格式、统一设计 token、同构分层 |
| G5 | 界面一致 | token 单一来源；布局度量 DIP↔vp 同值；图标语义映射；逐窗口对照 |
| G6 | API 26 主语言 + API 24（6.1.1）兼容 | 单代码库；targetSdk 26.0.0 / compatibleSdk 6.1.1(24)；差异走能力层与 api-matrix |

## 2. 已核实事实

| 编号 | 结论 | 证据 |
|---|---|---|
| F1 | 知识库基线：API 26.0.0 Release（2026-08-29，OpenHarmony 7.0）；API 24（6.1.1 Release）为存量兼容（84.93%）；26.0.1 Beta1 不可上架，不作基线 | 根 SKILL.md 版本历史 |
| F2 | 项目无任何现存 ArkTS/.ets 代码，移植为全新实现 | Glob `**/*.ets` 为空 |
| F3 | WinUI3 基线为 6 层单向下依赖，Core 无 UI 依赖可单测 | 设计文档 §4 + 目录 |
| F4 | Node 24 默认类型剥离不支持 enum；`--experimental-transform-types` 可执行 interface/enum/class/泛型 | 00-harness-notes.md H1/H2 |
| F5 | 本机工具链：`E:\EcoStudioCLI\command-line-tools` 26.0.1.290（beta），SDK 26.0.1 Beta1（apiVersion 26，hms+openharmony 双栈）；ohpm 26.0.1.220 / codelinter 6.0.240 / hvigorw 6.26.21 | `version.txt` + 版本命令 |
| F6 | 兜底可用 OpenHarmony 7.0 公共 SDK（API 26 底座） | 华为云镜像探查 |
| F7 | 探针工程单模块 `hvigorw assembleHap` 构建成功（34 tasks / 26s） | `D:\tmp\harmony-probe` 日志 |
| F8 | `compileSdkVersion` 须省略（写死 26.0.0 被 00303313 拒绝）；`compatibleSdkVersion "6.1.1(24)"`、`targetSdkVersion "26.0.0"` 可用 | 探针实测 |
| F9 | **HAR 多模块 + `.ts` 纯逻辑被 `.ets` 导入** 编译成功（41 tasks / 28s），`file:../core` 依赖离线可解析 | 探针二轮实测 |
| F10 | `HVIGOR_USER_HOME` 为官方环境变量（绝对路径、自动创建）；构建缓存可全部落 E 盘 | hvigorw.js + 构建日志 |
| F11 | 研究阶段共 225 个功能条目、106 项 API 24/26 差异，来源见研究附录 | workflow journal |
| F12 | `modelVersion 26.0.0` 在 Beta 工具链可构建；`hvigor-config.json5` 与根 `oh-package.json5` 的 modelVersion **必须一致**（否则 00303027） | 探针三轮实测 |

## 3. 平台能力边界与移植策略（研究结论）

| # | WinUI3 能力 | HarmonyOS 结论 | 移植形态 |
|---|---|---|---|
| B1 | **跨应用全屏透明批注覆盖层** | **不可行**。`WindowType.TYPE_FLOAT` 创建需 `ohos.permission.SYSTEM_FLOAT_WINDOW`（system_basic）；`WINDOW_TOPMOST`（normal）仅本应用主窗 Z 序；窗口 API 全部作用于本应用 | **应用内全屏批注画布**（画板/课件 PDF/图片/视频/截图画布）；不再覆盖第三方应用。此为最大语义变化，README 明示 |
| B2 | 全局热键（Alt/Ctrl 组合、Shift+Esc） | 无三方全局热键 API；键盘事件需组件焦点 | 应用内快捷键（PC/2in1 主窗聚焦时）+ 浮动栏按钮；键位表复用 |
| B3 | 全局输入监听/注入 | `inputMonitor` 不在公开 SDK；`inputEventClient` 需 CONTROL_DEVICE（system_basic） | 回放改为**模型驱动虚拟时钟重放**（本就与真实输入解耦）；自动化不依赖注入 |
| B4 | PPT/WPS COM 互操作 | 无 COM/进程互操作；PDF Kit 只处理 PDF，无 PPT/PPTX 解析 | **课件模式**：PDF 逐页 `renderPage → PixelMap` + 独立自绘墨迹层 + 页码导航；用户需先导出 PDF（或图片序列）。PPT 导航面板语义保留为课件导航 |
| B5 | GDI 静默全屏截图 | 无三方跨应用截图 API | 应用内内容快照（`componentSnapshot`/UIContext）+ 系统分享/相册 Picker 导入截图 |
| B6 | GitHub 自更新（下载 .exe 安装） | 无 HAP 自更新 API；仅 AppGallery 更新（方法名/签名未文档化） | 首版：版本提示 + 跳转应用市场说明（Deferred，不伪装可用） |
| B7 | HKCU 开机自启 | 无三方自启通道 | **不移植**（唯一 not-ported 功能单元，替代路径：用户手动创建桌面快捷方式/服务卡片） |
| B8 | 前台进程识别与自动收纳 | 无法获取其他应用前台状态 | 移除自动收纳设置项（UI 显示不可用原因），保留手动折叠/收纳 |
| B9 | 系统边缘手势抑制 | 无对应 API | 沉浸式全屏（系统手势行为由系统决定），在指南中说明 |
| B10 | Segoe Fluent/MDL2 图标字体 | 微软授权字体不可随 HAP 分发；SymbolGlyph 起始版本与图标名需实机核对 | 建立 `parity/glyphs.json`：码点→Symbol 映射；无法映射者自绘 Path/SVG（保留语义与选中态） |
| B11 | 外部启动器（希沃视频展台/Desmos） | 无注册表/进程启动 | Desmos 用 URI/Want 打开网页；希沃不可达则隐藏入口并说明 |
| B12 | 多顶层窗口/子窗口/弹窗（12+ 窗口） | 三方独立置顶浮窗能力未文档化；闪控窗/子窗仅 API 26 记录且资格未知 | 首版**单主窗 + 应用内浮层**（Stack/ZIndex/OverlayManager 能力层）；设置/调色板/图形菜单为页内浮层或侧栏；不做独立窗口 |
| B13 | 亚克力半透明 | `backdropBlur`/`foregroundBlurStyle`（API 14+）；系统材质 26 专属 | 模糊+半透色卡片；低算力/API 24 回退不透明圆角卡片 |
| B14 | 鼠标模式（穿透） | 无跨应用穿透；应用内可隐藏墨迹层并让内容交互 | “鼠标模式”= 隐藏墨迹层 + 内容交互模式（画板/课件内） |
| B15 | 双指/多指手势 | onTouch 原始事件齐全；系统手势存在冲突 | 自实现双指配对（保持阈值/惯性/枢轴语义）；三指及以上在手机不承诺 |
| B16 | 实时性能（输入→呈现） | API 26 有 `StylusFrameBoost`（权限 STYLUS_FRAME_BOOST）与原生 `SetRefreshDelayOff`；API 24 无 | 能力层封装；API 24 走常规帧率；真机验证前不宣称延迟指标 |

**硬约束（不可协商）**：system_core 仅系统应用；system_basic 需签名证书；第三方只能用 normal + user_grant 权限；沙箱外文件必须经 Picker；模拟器不支持 ArkTS 基础库/并发，不能替代真机验证。

## 4. 架构：同构分层（已由探针验证）

```
harmony/                          DevEco 26 工程
  AppScope/                       app.json5、全局资源
  entry/                          应用头：UIAbility、窗口外壳、模式切换、页面路由
  core/        HAR @inkcanvas/core      纯域模型（纯 .ts 逻辑，可 Node 验证）
  platform/    HAR @inkcanvas/platform  系统适配 + API 24/26 兼容能力层
  ink/         HAR @inkcanvas/ink       渲染管线 + InkController + 输入路由 + 手势
  controls/    HAR @inkcanvas/controls  ArkUI 组件、主题 token、动效、图标映射
  features/    HAR @inkcanvas/features  画板 / 课件(PDF) / 计时 / 抽奖 / 回放 / 截图 / 持久化
```

依赖方向同构：`entry → features → controls → ink → platform → core`。
`core` 只含 `.ts`、不 import 任何 `@kit.*`；`platform` 是唯一触达系统 Kit 的分层；`ink` 经 `platform` 暴露的渲染/输入接口工作。

| ArkTS 模块 | C# 对应 | 关键内容 |
|---|---|---|
| `core` | `InkCanvas.Core` | 墨迹模型、笔锋、轮廓、橡皮、撤销、图形、识别、选区、回放、设置、触摸分档、像素合成、主题判定、版本/策略、格式化 |
| `platform` | `InkCanvas.Platform` | 输入路由适配、手掌判定接线、屏幕/窗口/DPI、文件与偏好存储、日志、截图快照、更新检查、compat 能力层 |
| `ink` | `InkCanvas.Ink` | InkController 状态机、渲染器接口与 Canvas 实现、双指手势、选区交互、回放驱动 |
| `controls` | `InkCanvas.Controls` | 浮条、调色盘、图形菜单、图标按钮、计时环、动画、token、Symbol 映射 |
| `features` | `InkCanvas.Features` | 画板服务、课件(PDF)会话、.icink/设置/画板包持久化、抽奖、回放、快照 |
| `entry` | `InkCanvas.App` | 组合根、Shell 状态机、各页面/浮层、自检入口 |

## 5. 跨架构互认契约（G4 的实现）

1. **锚点互指**：成对文件头部与关键算法处写 `@parity` 注释，C# → ArkTS 路径、ArkTS → C# 路径；
   `tools/parity-check.mjs` 校验：目标存在、双向成对、manifest 覆盖全部锚点文件。
2. **权威功能清单** `parity/manifest.json`（schema 见 §5.1）：
   每项 `status ∈ {ported, adapted, not-ported}`（开发期允许 `planned`，`--final` 校验拒绝）+ `notes` 必填；覆盖率 = (ported+adapted)/分母，见 §9。
3. **共享黄金向量**：`parity/vectors/*.json` 由 C# 生成器（`tools/InkCanvas.VectorGen`，新增独立项目）产出，
   两侧测试共同消费；`node --experimental-transform-types parity/run-vectors.mjs` 零依赖执行。
   向量集（首版）：`taper-tail`、`taper-velocity`、`stroke-geometry`、`stroke-eraser`、`undo`、
   `shapes`、`recognizer`、`selection`、`two-finger`、`touch-classifier`、`palm`、`pixel-compositing`、
   `settings-roundtrip`、`icink-roundtrip`、`replay-clock`、`board-format`、`update-policy`、`app-version`。
   浮点容差：整数/字段名/schema 严格相等；几何 float 序列 `1e-4`（C# float32 与 ArkTS double 差异）。
4. **共享数据格式**：`.icink`（单表面/画板包/PPT 墨迹）、`Settings.json`、PPT manifest 的 JSON Schema 在
   `parity/schemas/`；**PascalCase 字段名原样保留**（包括已知缺陷：画板 Stroke 子格式缺 `HighlighterAlpha`，
   本轮不改格式，缺陷与修复建议记录在 notes）。
5. **UI 单一 token 源**：`parity/tokens.json`（颜色/字号/间距/圆角/尺寸/动画曲线）+ `parity/glyphs.json`
   （Segoe 码点 → Symbol/自绘映射）；C# `Tokens.xaml` 与 ArkTS `ThemeTokens.ts` 数值一致性由检查器校验。

### 5.1 manifest 条目示例

```json
{
  "id": "ink-taper-causal",
  "name": "因果增量笔锋",
  "status": "ported",
  "csharp": ["src/InkCanvas.Core/Ink/TaperProfile.cs"],
  "arkts": ["harmony/core/src/main/ets/ink/TaperProfile.ts"],
  "vectors": ["parity/vectors/taper-tail.json", "parity/vectors/taper-velocity.json"],
  "tests": ["tests/InkCanvas.Core.Tests/TaperTests.cs"],
  "notes": "Tail 10/4/0.2；Velocity 距离/20 与 0.05/0.25/1.5 分段；抬笔不重算。"
}
```

## 6. API 版本策略（G6）

- 单代码库、单 HAP：**省略 `compileSdkVersion`**（工具链自适应；Release→26.0.0 / Beta→26.0.1，均为 API 26）；
  `compatibleSdkVersion "6.1.1(24)"`；`targetSdkVersion "26.0.0"`；`modelVersion "26.0.0"`（`hvigor-config.json5` 与根 `oh-package.json5` 必须一致；Beta 工具链实测可构建）。
- `platform/compat/` 能力层：
  - 版本判据：`deviceInfo.sdkApiVersion >= 26`（SDK d.ts 已核实；知识库未收录该字段）；
  - 能力判据：仅使用**已核实**的 syscap 字符串 —— `SystemCapability.ArkUI.ArkUI.Full`、
    `SystemCapability.Stylus.Handwrite`、`SystemCapability.Stylus.StylusService`、
    `SystemCapability.MultimodalInput.Input.Pointer`；其余能力（PDF/AVPlayer/通知/相册等）用
    **try/catch + 方法存在性探测**，禁止臆造 syscap。
  - 26 专属能力（StylusFrameBoost、systemMaterial、OverlayManager 层级、ContainerReader/WithEnv、
    WindowFocusState 等）全部包在接口后；API 24 有回退实现或缺省。
- 业务层禁止直接判断版本号/直接 import 26 专属 Kit。
- 差异登记：`parity/api-matrix.md` 逐能力列 API 24 行为 / API 26 行为 / 门控方式 / 维护注意。
- 不使用 26.0.1 Beta 专属 API（不可上架）。

## 7. 设备形态策略（G3）

| 形态 | 定位 | 关键差异 |
|---|---|---|
| 平板 (tablet) | 完整功能 | 横屏优先；浮条+三栏画板；双指手势；笔+触摸+手掌分流 |
| PC/2in1 桌面触摸 | 完整功能 | 应用内多面板布局；鼠标独立指针模式；键盘快捷键；窗口缩放/自由窗口适配 |
| 手机 (phone) | 初步适配 | 单列响应式；核心书写/画板/计时/抽奖/设置；浮条简化；不做课件多面板/PPT 导航 |

- 断点（按**窗口宽度** vp）：`xs<320`、`sm 320–600`、`md 600–840`、`lg 840–1200`、`xl>1200`；
  实现用 `onSizeChange` + LocalStorage/@StorageProp（API 24 可用）；API 26 可选 ContainerReader/WithEnv（能力层，非必需）。
- `deviceTypes: ["phone","tablet","2in1"]`；能力降级以能力探测为准，不以设备类型硬编码。
- **触摸热区**：视觉尺寸保持 40vp（与 WinUI 一致），命中区扩到 ≥44vp（透明内边距/`hitTestBehavior`），同时满足 HarmonyOS 设计规范与项目 ≥40 契约。
- **弹窗宽度**：HarmonyOS 8 栅格以上自定义弹窗最宽 400vp，与 440 设置面板冲突 → 设置改为**页内侧栏/全屏页**（内容 440vp 不变），不用系统模态弹窗。
- 深色模式跟随系统；卡片避免纯黑；状态栏颜色跟随；尊重“减少动效”（API 若不可得则提供应用内开关）。

## 8. 笔与渲染方案

**输入（API 24/26 均可用）**：`onTouch` → `TouchEvent`：
- 压感 `pressure ∈ [0,1]`（缺省/0 → 0.5 回退）；倾斜 `tiltX/tiltY`（度）；`source/sourceTool` 区分
  Finger/Pen/Mouse/Touchpad（**鼠标左键会触发 onTouch，必须过滤**）；`TouchObject.width/height`（vp，接触面积，API 15+，可能为 0）；
  `getHistoricalPoints()`（仅 onTouch 内，ns 时间戳）；`onHover/onHoverMove` 笔悬停（需实机验证）。
- 时间戳 **ns → ms（/1e6）**；与 Windows 的 µs 不同，不可照搬。
- 笔尖细分类型（RUBBER/BRUSH）ArkUI 不提供，仅 XComponent 原生 `OH_NativeXComponent_GetTouchPointToolType`；
  **首版不依赖笔尾橡皮**（橡皮由浮动栏/工具栏切换），XComponent 路径列为性能增强（Phase 2，需真机）。

**手掌判定**：沿用 `max(接触宽,高) >= 30vp` 的纯几何判定（仅 down 时一次）+ 笔优先 + 平台 `TouchType.Cancel`（笔落取消手指）；
接触面积缺失设备自动关闭面积判定（降级为笔优先+取消事件），`api-matrix.md` 登记降级行为。

**渲染**：`InkRenderer` 接口（`platform`/`ink` 边界）：
- **v1 基线：ArkUI Canvas 双层**（已提交层 + 活动层，`Stack` 叠放）。
  - 已提交层：全部笔迹；活动层：当前笔画。提交 = 画入已提交层 + 清空活动层（原子提交，无中间帧）。
  - 轮廓：`Path2D` 逐段 capsule 多边形（24 段圆/12 步半圆），一次 `fill()`；若 `fill(path, 'nonzero')` 不可用，
    降级为逐段填充（半透明重叠加深）并在 api-matrix 标注；优先验证非零环绕。
  - 颜色：半透明荧光笔按 Win2D 语义以属性 alpha 合成；预乘 BGRA 合成公式仅用于快照/像素级向量，不用于 Canvas。
- **Phase 2（API 26 且真机验证后）：XComponent + NDK Drawing** 高性能路径，接口不变。
- 跟手性：API 26 `StylusFrameBoost.forceRefreshOneFrame(action)`（0=down/1=up/2=move/3=cancel；权限
  `ohos.permission.STYLUS_FRAME_BOOST`；门控 `canIUse('SystemCapability.Stylus.Handwrite') && sdkApiVersion>=26`）；
  API 24 无 → 不调用。原生 `HMS_HandWrite_SetRefreshDelayOff` 仅 XComponent 路径。

**性能验证边界**：本机无设备/模拟器不可靠；延迟、压感、接触面积、悬停、笔尖类型必须真机验证，结论写入 porting notes。

## 9. 功能覆盖矩阵与覆盖率口径

**分母（权威）**：WinUI3 基线报告中 62 个顶层功能单元，**排除**：
- 5 项基线本就“明确未移植”的项（通知条/循环进度条/窗口位置记忆/旧 ISF/设置历史缺口中的未实现部分）；
- 2 项无消费方死代码（Features.CountdownTimer、WindowEnumerator）。
→ **分母 = 55 个用户可见/可验证功能单元**。

**覆盖率** = `(ported + adapted) / 55`，**目标 ≥ 98%**（即至少 54/55）。

**not-ported 初始清单（1 项 = 1.8%）**：
| id | 原因 | 替代路径 |
|---|---|---|
| `system-autostart` | 无三方自启 API（系统限制） | 用户手动快捷方式/服务卡片；设置项显示原因 |

**adapt 重点（形态改变，功能意图保留）**：跨应用覆盖层（B1）→ 应用内批注；PPT COM（B4）→ PDF 课件模式；
全局热键（B2）→ 应用内快捷键；截图（B5）→ 应用内快照；更新（B6）→ 应用市场提示；
边缘手势（B9）→ 沉浸式；自动收纳（B8）→ 手动收纳；图标字体（B10）→ Symbol 映射/自绘；独立窗口（B12）→ 单窗浮层。

**逐项状态**：写入 `parity/manifest.json`（由研究报告生成后人工校订），并同步 `docs/harmony-porting-notes.md`。

## 10. 验证策略（分层，诚实声明）

| 层 | 手段 | 本机可执行性 |
|---|---|---|
| L1 逻辑互认 | 黄金向量双向跑（C# xunit + `node --experimental-transform-types`） | **可执行**（已实证） |
| L2 编译与静态检查 | `hvigorw assembleHap`（debug，未签名）+ `codelinter` | **可执行**（已实证：探针 41 tasks / 28s） |
| L3 运行与 UI | 真机/模拟器（模拟器不可靠） | **本机不可用** → 记录「未实机验证」，给出核对清单 |
| L4 基线回归 | `.\build.ps1 test`（WinUI3 124 用例） | 可执行 |

- 构建命令：`. tools/harmony-env.ps1; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon`
- 缓存：`HVIGOR_USER_HOME=E:\harmony-cache\hvigor`，`hvigor.cacheDir=E:/harmony-cache/hvigor-cache`，SDK 在 E 盘。
- 生产构建须换 26.0.0 Release 工具链；当前 26.0.1 Beta 产物不可上架 —— 每次验证声明明示。

## 11. 风险与取舍

| 风险 | 影响 | 缓解 |
|---|---|---|
| 跨应用批注不可行（B1） | 产品形态最大变化：不能覆盖 PPT/WPS 直接书写 | 改为应用内批注 + PDF/截图导入；README/指南明示；UI 文案不误导 |
| 无 PPT 解析（B4） | “PPT 适配”降级为 PDF 课件 | 明确导出流程；PDF Kit 渲染路径 + 自绘墨迹；页码导航语义保留 |
| Segoe 图标授权（B10） | 图标外观不一致 | Symbol 映射优先；缺失重绘；`glyphs.json` 纳入一致性检查 |
| 40 vs 44 热区 | 布局轻微差异 | 视觉 40 + 命中 44；token 记录 |
| 400vp 弹窗上限 | 设置面板形态变化 | 改页内侧栏/全屏页，内容宽度 440 不变 |
| ArkTS 严格性 | 纯逻辑可能需改写 | core 用 ArkTS∩TS 子集；codelinter + 编译双重校验 |
| 真机能力未知（压感/面积/悬停/性能） | 体验与算法阈值不确定 | 能力探测 + 降级；api-matrix 标注「需真机」；不宣称未验证指标 |
| Beta SDK 产物不可上架 | 发布受阻 | 提交配置保持 Release 兼容；发布前换 26.0.0 工具链重编译 |
| 已知基线缺陷（HighlighterAlpha 丢失等） | 跨端文件语义偏移 | 保持格式一致并记录；修复需两侧同步（后续版本） |

## 12. 自主决策记录（研究开放问题的裁决）

| # | 问题 | 裁决 |
|---|---|---|
| D1 | 覆盖率分母 | 62 − 5 未移植 − 2 死代码 = 55；not-ported 仅自启 = 98.2% |
| D2 | ShapeKind 数量口径 | 以枚举 25 为准；菜单可见 24 个预览按 UI 目录移植 |
| D3 | float32 vs double | 模型/几何向量容差 1e-4；整数/字段/JSON 严格 |
| D4 | 画板包缺 HighlighterAlpha | 本轮保持格式 v1 不变（互认优先），缺陷记录；修复留待双端同步版本 |
| D5 | PPT 轮询口径 | 200/900ms（ShellController 实际行为）作为契约 |
| D6 | MultiTouchEnabled 语义 | 保留键名与“true=多指书写/禁手势”语义（Settings.json 互读）；UI 文案解释 |
| D7 | 选区每次 move 一撤销单元 | 复刻基线行为（parity 优先），标注为体验待优化 |
| D8 | 三角识别被忽略 | 复刻忽略行为 |
| D9 | 倒计时实现口径 | 以 CountdownWindow（20/30/50/100ms 自适应）为准；Features 死类不移植 |
| D10 | Windows 路径默认值 | 保留键名；首启检测到 Windows 绝对路径则迁移到沙箱并回写（迁移函数 + 向量） |
| D11 | 自动更新 | 首版 Deferred：版本检查不可用则仅显示当前版本与市场提示；不伪装 |
| D12 | 亚克力 | `backdropBlur` + 半透色；不支持则回退不透明圆角卡片 |
| D13 | 浮条宿主 | 主窗内 Stack 浮层（API 24 基线）；API 26 OverlayManager 作为可选增强 |
| D14 | 设置面板形态 | 页内侧栏（平板/2in1）/全屏页（手机），内容 440vp |
| D15 | 特殊屏/希沃 IR 设置 | 保留设置项与算法；接触面积缺失设备自动降级并在设置页注明 |

## 13. 需真机验证清单（交付时附）

压感/倾斜/接触面积取值与单位；笔尾橡皮行为；悬停；`TouchType.Cancel` 稳定性；Canvas `fillRule` 与自交并集效果；
双层 Canvas 提交是否闪烁；`StylusFrameBoost` 实际降延迟与功耗；PDF `renderPage` 分辨率/耗时/内存；
AVPlayer 首帧与切页；通知/代理提醒 APL；AppGallery 更新 API 名与签名；SymbolGlyph 图标名与起始版本；
自由窗口/分屏断点；深色/减少动效系统设置读取；2in1 鼠标/键盘事件与焦点。

---

### 参考

- 研究附录：`docs/superpowers/research/harmony/01-core-ink.md` … `13-media-services.md`、`00-harness-notes.md`
- 工具链脚本：`tools/setup-harmony-toolchain.ps1`、`tools/harmony-env.ps1`（生成）
- 现有 WinUI3 设计：`docs/superpowers/specs/2026-11-13-winui3-migration-design.md`
