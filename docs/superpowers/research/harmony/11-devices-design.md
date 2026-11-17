# 11 — 多设备、响应式断点与 HarmonyOS 设计语言（含图标体系 / 深色模式）

- 日期：2026-11-18
- 仓库：`D:\InkCanvas`，分支 `feat/winui3-migration`
- 性质：**只读研究**。仅新增本报告文件，未修改任何其他文件；未构建 HAP、未启动 DevEco、未使用模拟器/真机。
- 知识库：`D:\InkCanvas\.claude\skills\harmony-os\`（vendored，基线 API 26.0.0 Release / 兼容 API 24 = HarmonyOS 6.1.1）
- 指定范围（均已完整阅读）：`dev/references/multi-device.md`、`design/references/multi-device-design.md`、`design/references/multi-device.md`、`design/references/layout.md`、`design/references/controls.md`、`design/references/animation.md`、`design/references/color.md`、`design/references/typography.md`、`design/references/icon.md`、`design/references/design-changes.md`、`design/references/overview.md`、`design/references/ux-best-practices.md`、`templates/references/arkui-components.md`、`dev/references/localization-kit.md`。为补齐版本/控件事实，另阅读了 `dev/references/ui-development.md`、`dev/references/ui-design-kit.md`、`dev/references/app-framework.md`、`dev/references/other.md`、`dev/references/industry-practices.md`、`design/references/resources.md`、`design/references/hmi-system-features.md`、`templates/references/design-resources.md`、`system/references/basic-services.md`、`system/references/device-info.md`、`system/references/input-kit.md`、`system/references/input-multimodal-kit.md`、`media/references/graphics2d.md`、`samples/references/graphics.md`。
- 证据等级：**事实**＝知识库原文明确写出；**推断**＝由多条事实组合得出；**未知/需实机验证**＝知识库没有写或只有名称没有签名。本报告不把“名称存在”当作“签名已知”。

---

## 0. 一页结论（给移植决策者）

1. **断点阈值（vp，按窗口宽度而非设备宽度）**：`xs < 320`、`sm 320–600`、`md 600–840`、`lg 840–1200`、`xl > 1200`。知识库同时明确：2025-06 起布局基础把“栅格/断点”的描述由**设备**修正为**窗口**（`design/references/design-changes.md:226`）。PC/2in1 自由窗口必须按窗口宽度选布局，不能按 `deviceType` 硬编码。
2. **三档设备策略与知识库一致**：平板＝完整、PC/2in1（知识库现称“电脑”）＝完整、手机＝初步；折叠屏按展开/折叠态与悬停态适配。项目设计文档的分档不需要改，但必须补“窗口宽度优先”的判定顺序。
3. **API 26 的 `ContainerReader`（容器断点）与 `WithEnv`/`@CustomEnv`（响应式环境变量）只有名称/方向，没有签名**。知识库对二者的全部描述只有：`ContainerReader` 基于**容器尺寸**而非窗口尺寸实现自适应（API 26 Beta1）；`WithEnv` 容器 + `@CustomEnv` 自定义环境变量（API 26 Beta2）。**不得按猜测写参数**。
4. **API 24 回退路径在知识库里没有 `mediaquery` 记录**：全库 grep `mediaquery / 媒体查询 / matchMedia` 零命中。可用的、有原文依据的回退是：根容器 `.onSizeChange((oldValue, newValue) => …)`（API 12+，`dev/references/ui-development.md:307-315`）+ `LocalStorage`/`@StorageProp`（`dev/references/other.md`、`dev/references/multi-device.md:50`）+ `window.getLastWindow()/getWindowProperties()/resize()`（`system/references/basic-services.md`）。如果坚持用 mediaquery，必须查官方 API 参考并实机验证，不能写进“已确认”清单。
5. **设计语言的核心数值**：主色宇宙蓝 `#007DFF`，功能色成功 `#36D1A1` / 警告 `#F7CE46` / 错误 `#E84026` / 信息 `#478EDC`；字体 HarmonyOS Sans（系统内置）；正文 16 fp、副文本 14 fp、注释 12 fp；动效快速 100–200 ms、通用 300–500 ms；12 栅格、4/8 vp 基准、gutter 8/12/16/24 vp、安全边距 12/16/24 vp。
6. **跨平台一致性策略应当是“语义 token 同源 + 平台组件本地化”**，不是“把 Fluent 颜色换成宇宙蓝”。InkCanvas 的品牌色是 `#0067C0`（浅色）/`#4CC2FF`（深色），与 HarmonyOS 主色不同；为保持两平台界面一致，**保留项目 accent**，只在系统控件（Toggle/Slider/Progress 等）无法改色时接受系统蓝，或改为自绘。
7. **浮条/调色盘/图形预览/计时环/画板工具条必须自绘**：它们是“仪器型”高密度 UI，ArkUI 标准控件（Button/Tabs/ActionBar）无法复刻 Fluent 密度与浮条形态。设置页、列表、输入框、开关、滑动条可用系统控件以保证无障碍与键盘行为。
8. **Symbol 图标体系可替代 Segoe Fluent 的“通用语义图标”**（1500+、分层着色、7 种动效、与 HarmonyOS Sans 字重同步），但**知识库没有给出 SymbolGlyph 的起始 API 版本，也没有图标名目录**；映射表必须逐项实机核对。Segoe Fluent Icons / Segoe MDL2 Assets 是微软字体，**不能随 HAP 分发**；缺失图标应使用自绘 Path/SVG 或自有授权图标集。
9. **API 24 与 API 26 的设计差异不是“视觉规范变了”，而是“材质与响应式容器 API 的可用面变了”**：API 23（6.1.0，API 24 继承）已有 HDS 组件的 `systemMaterialEffect`、`HdsMaterial`、`HdsTabs` 悬浮/迷你栏、`easyGo` 平行视界；API 26 才把 `systemMaterial` 扩展到“所有支持通用属性的组件”，并新增 `ContainerReader`、`WithEnv`、`HdsColorPicker`、材质等级/`ImmersiveMaterial` 判定。API 26 的“圆角参数/间隔参数/鸿蒙黑体”首先是**设计文档**新增，不等于运行时 token API。
10. **第三方应用最大的硬约束在“窗口/覆盖层”而不是颜色字体**：知识库只给出了 `getLastWindow`、`findWindow`、`resize`、系统栏控制等基础窗口 API；**没有**创建任意置顶/跨应用悬浮窗口的 API，也没有 `闪控窗` 的申请条件与权限。把 WinUI 的“独立浮条窗口 + 覆盖 PPT”直接照搬存在根本性风险，必须先用真机验证替代方案（应用内覆盖 / 多 UIAbility / 闪控窗）。
11. **触摸目标存在规范冲突**：项目契约是 ≥40 DIP（`Tokens.xaml: IcTouchTargetMin=40`），HarmonyOS 设计规范写的是 ≥44×44 dp（`multi-device.md`、`icon.md`）。建议 token 增加 `hitTargetMin=44` 作为“交互热区”，视觉尺寸仍可 40/41.67，但这不是纯文档问题，需在真机上验证误触率。
12. **设置面板 440 与“自定义弹窗最宽 400vp”冲突**：知识库明确“自定义弹出框在 8 栅格及以上设备上最宽不超过 400vp，宽屏设备弹出框物理高度不超过手机上的 1.5 倍”（`ux-best-practices.md:53-54`）。设置面板 440 若实现为模态弹窗会超标；建议平板/2in1 用侧边面板或独立窗口（440 可保留），手机用全屏/半模态。

---

## 1. 版本与文档边界

### 1.1 版本基线

| 版本 | 关系 | 本报告中的意义 |
|---|---|---|
| HarmonyOS 26.0.0 / API 26 | Release，2026-08-29，底座 OpenHarmony 7.0 | 主基线；API 26 新组件/新材质在此可用 |
| HarmonyOS 6.1.1 / API 24 | Release，2026-05-26；6.1.0(23) 的维护版 | 兼容下限；继承 API 23 的全部设计能力 |
| HarmonyOS 6.1.0 / API 23 | 2026-04-20 | HDS 组件、沉浸材质、`easyGo` 的引入版本 |
| HarmonyOS 26.0.1 / API 26.0.1 | Beta1，2026-11-10 | **不作为基线**；Beta 工具开发的应用不能上架；新增字体管理等能力不可依赖 |

> 本机实际工具链是 DevEco Studio 26.0.1.290（Beta）/ SDK 26.0.1 Beta1（见 `00-harness-notes.md` H7）。这意味着“能编译 API 26 代码”不等于“能上架”；上架仍需 Release 工具链。报告中的 API 26 结论按 Release 文档写，Beta 只作为编译验证手段。

### 1.2 知识库的“设计文档时间”不等于“API 可用时间”

`design/references/design-changes.md` 记录的是设计指南的发布日期：

- 2026-06-12（HDC，API 26 周期）新增 14 篇设计文档，包括“鸿蒙黑体、沉浸光感、圆角参数、间隔参数、色彩选择器、闪控球和闪控窗、桌面快捷方式、平行视界”。其中只有一部分对应新的运行时 API（如 `systemMaterial`、`HdsColorPicker`、`ContainerReader`、闪控窗）；**圆角/间隔参数是设计指导，不是新的 token API**。
- 2025-06-19 的变更把“多设备响应式设计”拆成“应用设计最佳实践”，并明确“布局基础：栅格描述由设备修正为窗口；新增手机、折叠设备、平板、智能穿戴系统安全区内容”。
- 2025-11-10 的 UX 标准把智能穿戴纳入适用范围；本项目不涉及穿戴/TV/车机，`module.json5` 不应声明这些 `deviceTypes`。

### 1.3 术语与单位

| 术语 | 含义 | 本报告的使用方式 |
|---|---|---|
| vp | 虚拟像素，随屏幕密度缩放 | 布局尺寸、间距、圆角、控件尺寸 |
| fp | 字体像素，`1fp = 1vp × 缩放系数`（`ux-best-practices.md:145`） | 字号；会跟随用户字体大小设置 |
| dp | 设计规范中的密度无关像素 | 引用官方规范原文时保留 dp，落地写 vp/fp |
| 窗口宽度 | 应用窗口内容区宽度 | 断点判定基准，不是物理屏幕宽度 |
| 电脑 / PC / 2in1 | 同一类桌面形态；2025-06 起官方设计文档把“PC/2in1”改称“电脑” | 代码 `module.json5` 仍写 `2in1` |

---

## 2. 断点系统与响应式布局

### 2.1 断点表（已核实）

`design/references/layout.md:22-30`：

| 断点 | 窗口宽度 | 知识库标注的设备类型 |
|---|---|---|
| xs | < 320 vp | 小屏手机 |
| sm | 320–600 vp | 手机 |
| md | 600–840 vp | 大手机 / 小平板 |
| lg | 840–1200 vp | 平板 |
| xl | > 1200 vp | 大平板 / 二合一设备 |

落地时建议用**左闭右开**区间并只保留 4 个主档（`sm/md/lg/xl`），`xs` 作为 `<320` 的极端保护档；不要出现 600 同时属于 sm 和 md 的双重匹配。

> 注意：知识库的 `sm=320–600`、`md=600–840`、`lg=840–1200` 与官方常见的三段断点（sm 320 / md 600 / lg 840）一致；`xl>1200` 与 `xs<320` 是本知识库表格的补充。若后续官方 API 参考给出不同阈值，以官方为准，并把差异记入 `parity/api-matrix.md`。

### 2.2 判定基准：窗口，不是设备

- `design/references/design-changes.md:226`：“补充断点判断中的窗口定义说明；栅格描述由设备修正为窗口”。
- `dev/references/multi-device.md:39-45` 把断点、栅格、窗口宽高都归入“响应式布局”。
- 因此：**PC/2in1 上把窗口拖到 500vp 宽时，应使用 sm 布局**，即使用户在“电脑”上；平板上分屏到 600vp 也应使用 md 布局。`deviceType` 只用于输入方式/能力差异，不用于布局档位。

### 2.3 自适应布局的 7 种能力（`dev/references/multi-device.md:25-37`）

| 能力 | 知识库描述 | ArkUI 落地 |
|---|---|---|
| 拉伸 | 组件按固定比例拉伸（`layoutWeight`） | `layoutWeight`（已点名） |
| 均分 | 容器内均分 | `Flex`/`Row` + `layoutWeight(1)` |
| 占比 | 按百分比设宽 | 百分比字符串宽度 |
| 缩放 | 按设计稿比例缩放 | `scale` 属性（属性动画章节列为可动画属性） |
| 延伸 | 列表等容器自动延伸内容 | `List`/`Scroll` |
| 隐藏 | 内容过多时隐藏次要信息 | 条件渲染 + 断点 |
| 折行 | 换行继续排列 | `Flex` wrap（具体枚举名知识库未列） |

### 2.4 12 栅格与间距

`design/references/layout.md:5-17`：

| 参数 | 值 |
|---|---|
| 列数 | 12 |
| 最小单位 | 4 vp / 8 vp |
| Gutter | 8 / 12 / 16 / 24 vp |
| 安全边距 | 12 / 16 / 24 vp |

间距规范（`layout.md:44-54`）：

| 名称 | 值 | 用途 |
|---|---|---|
| xs | 4 vp | 紧凑元素内部 |
| sm | 8 vp | 相关元素之间 |
| md | 12 / 16 vp | 区块内部 |
| lg | 24 vp | 区块之间 |
| xl | 32 / 48 vp | 大区块之间 |
| xxl | 64 vp | 页面级留白 |

**InkCanvas 例外**：浮条/调色盘/图形菜单是“仪器型”高密度控件，项目现有度量是 WPF `S=50/36` 换算后的 2.78 / 5.56 / 6.94 / 11.11 / 22.22 DIP 等（`05-controls.md` §1）。这些值不应被 HarmonyOS 的 4/8 vp 间距体系“规范化”，否则浮条会变高、变松，破坏两平台一致性。建议明确两类布局体系：

- **A 类：仪器型自绘 chrome**（浮条、调色盘、图形目录、画板工具条、PPT 导航浮牌）→ 用项目 token 原值（vp 与 DIP 1:1）。
- **B 类：页面/表单型 UI**（设置、关于、错误提示、列表、对话框）→ 用 HarmonyOS 4/8/12/16/24/32 间距与 12 栅格。

### 2.5 容器与布局 API 的事实

已核实：

- `Column`、`Row`、`Stack`、`Flex`、`List`、`Grid`、`Tabs`、`Navigation` 的示例见 `dev/references/ui-development.md:240-260`、`templates/references/arkui-components.md`。
- `Grid` 用 `columnsTemplate('1fr 1fr 1fr')`、`rowsTemplate(...)`；`List` 用 `listDirection(Axis.Vertical)`。
- `RelativeContainer` 被点名存在，但知识库没有给出 `alignRules` 等细节。
- **`GridRow` / `GridCol` 在知识库中没有记录**（全库 grep 零命中）。如果团队计划用官方 12 栅格组件，必须查 API 参考；不要假设 API 名。
- `onSizeChange((oldValue, newValue) => …)` 是知识库明确写出的组件尺寸变化事件（API 12+，`ui-development.md:307-315`），但**没有写 `newValue` 的字段名**（是 `width`/`height` 还是 `SizeOptions`），需在 SDK d.ts 核对。

### 2.6 断点状态的组织模式

知识库给出的唯一代码模式是（`dev/references/multi-device.md:49-58`）：

```typescript
@StorageProp('currentBreakpoint') bp: string = 'sm'

Column() {
  if (this.bp === 'sm') {
    this.PhoneLayout()
  } else {
    this.TabletLayout()
  }
}
```

**关键缺口**：知识库没有写谁负责写入 `currentBreakpoint`，也没有写系统是否自动维护这个 key。因此这只能当作“消费端模式”，不能当作“系统已提供断点服务”。

推荐的自建结构（属推断/工程建议，不是 API 事实）：

```
platform/compat/Breakpoints.ets     // 唯一写入者：窗口/容器尺寸 -> Breakpoint
  ├─ windowStage 事件 / getLastWindow 尺寸
  ├─ 根容器 onSizeChange（API 24 回退）
  └─ ContainerReader（API 26 可选增强）
controls/state/BreakpointState.ets  // LocalStorage/AppStorage + @StorageProp 消费
```

业务组件只读 `Breakpoint` 枚举，不直接读窗口、不判断版本。

### 2.7 资源限定符的边界

`dev/references/multi-device.md:88-97` 明确列出的目录：

```
resources/
├── base/           # 默认
├── zh-CN/          # 中文
├── sm/             # 小屏幕设备
├── lg/             # 大屏幕设备
└── dark/           # 深色模式
```

事实：知识库只写了 `sm`、`lg`、`dark`、`zh-CN` 四类限定符；**没有写 `md`、`xl`、`tablet`、`2in1` 资源目录**。结论：

- 图片/布局资源可按 `sm/lg/dark` 提供少量差异化；
- 不要把布局分支寄托在 `resources/md` 或 `resources/xl` 上，除非官方文档确认；
- 断点分支应在 ArkTS 运行时做，不是资源目录做。

---

## 3. 各设备形态的适配策略

### 3.1 设备事实（`design/references/multi-device.md`、`multi-device-design.md`）

| 形态 | 屏幕/交互 | 知识库要点 |
|---|---|---|
| 手机 | 4–7 寸，触摸，竖屏优先，观看距离约 30 cm | 触控优先；单手操作；信息密度适中；避免两指以上的多指手势；底部页签导航；支持横屏、挖孔适配 |
| 平板 | 8–13 寸，手指/手写笔 | 多窗口/分屏；手写笔压感；横竖屏都要适配；侧边页签/侧边栏导航；键鼠基础适配 |
| 折叠屏 | 展开/折叠态，悬停态 | 展开态信息量不少于折叠态 3/4；字号/弹窗不超过折叠态 1.2 倍；开合平滑过渡；悬停适配 |
| 电脑 / PC / 2in1 | 可变形态，触摸+键鼠 | 自由窗口、左右分屏、分屏比例可调；悬停、快捷键、右键菜单；应用状态栏接入；窗口框架 |
| 智慧屏/手表/车机 | 大屏远距离/小屏/驾驶 | 本项目不覆盖；`deviceTypes` 不声明 |

### 3.2 平板（完整功能）

- **布局**：横屏优先；`lg`（840–1200）为主，`xl`（>1200）用于大平板/外接键盘。建议画板三栏：页缩略图 / 画布 / 工具；非画板页面用 `HdsSidebar` 或自定义侧栏。
- **笔**：知识库只写“支持手写笔压感”（`multi-device.md:56`），没有 API 细节；输入/笔能力见 `pen-kit.md` 与 04/05 报告，本报告不展开。
- **多窗口**：`ux-best-practices.md:47` 要求平板及更宽设备适配**自由窗口、左右分屏、分屏比例自由调节**。布局必须响应“窗口变窄”，不能假设全屏。
- **键鼠**：`ux-best-practices.md:58` 要求基础鼠标、键盘适配；`multi-device.md:55` 要求悬停状态、快捷键、右键菜单。ArkUI 的 hover/右键/快捷键绑定接口知识库未列，需查 API。
- **手写笔与触摸分流**：这是 InkCanvas 的功能核心，不是设计层能解决的；但 UI 上必须给“笔模式/手指模式/鼠标模式”明确状态（项目已有），并且在平板横屏时把模式切换放在拇指可达区。

### 3.3 电脑 / PC / 2in1（完整功能）

- **窗口**：知识库明确要求自由窗口、左右分屏、比例可调；应用内可用“分屏”按钮触发任务分屏（`ux-best-practices.md:47,102`）。窗口 API 只能确认 `resize`、`findWindow`、`getLastWindow`、系统栏控制（`basic-services.md`），**窗口最小尺寸、窗口位置记忆、置顶/层级 API 未记录**。
- **键鼠**：
  - 悬停：设计规范要求（`multi-device.md:55`），知识库无 ArkUI hover 事件签名。
  - 右键：`hmi-system-features.md:16` 明确“电脑设备：单击鼠标右键打开该菜单”；`design-changes.md:135` 说交互事件归一覆盖“上下文菜单”。ArkUI 的右键菜单绑定 API 未列。
  - 快捷键：`Input Kit` 提供键盘输入事件（`@ohos.multimodalInput.keyEvent`）与输入设备列表（`input-multimodal-kit.md`）；`IME Kit` 提供物理键盘按键监听（`ime-kit.md:13`）。具体“应用级快捷键注册”接口未在知识库出现。
  - 鼠标光标：Input Kit 能力列表明确“改变鼠标光标样式”（`input-kit.md:21`），API 细节（`@ohos.multimodalInput.pointer`）未给出签名；26.0.1 Beta 还新增“鼠标光标管理”，但 Beta 不可用。
- **窗口形态**：API 26 新增“窗口管理支持设置主窗或子窗支持的窗口模式”（`SKILL.md:407`），但没有签名；`WindowStageEventType`、`windowStage.on('windowStageEvent')` 有示例（`basic-services.md:92`），可用于前后台/焦点切换。
- **建议**：MVP 在 2in1 上把应用当“一个可缩放的主窗口 + 应用内浮层”处理；不要依赖“独立置顶浮条窗口”作为首发能力。等窗口创建/置顶/点击穿透 API 验证后再做增强。

### 3.4 手机（初步适配）

- **导航**：`ux-best-practices.md:29` 明确手机/折叠屏折叠态用底部页签；宽屏才用侧边页签/侧栏。InkCanvas 没有传统页签，可把“笔/选择/橡皮/更多”做成底部主操作条，把设置/画板/计时/抽奖放入“更多”全屏页。
- **单手**：核心操作靠近底部；单手可达区优先（`multi-device-design.md:45`）。
- **手势**：`multi-device-design.md:46` “尽量不使用两指以上的多指手势”。InkCanvas 的双指平移/缩放是允许的两指手势；三指及以上手势在手机上应禁用或提供替代按钮。
- **信息密度**：浮条在手机上应简化（减少按钮、增大按钮、允许换行/分页），不能把平板的 20+ 按钮塞进一行。
- **横屏**：支持横屏但竖屏优先；挖孔区不能遮挡核心内容；左右留白要一致（`ux-best-practices.md:35-41`）。
- **定位**：手机是“初步”，不建议首发就支持全功能画板/视频插入/多窗口；核心书写、计时、设置优先。

### 3.5 折叠屏

- 展开态信息量 ≥ 折叠态 3/4；字号 ≤ 1.2 倍；弹窗 ≤ 1.2 倍（`layout.md:64-71`、`multi-device.md:39-45`）。
- 展开态/平板要同时适配横竖屏；折叠态按竖屏（`ux-best-practices.md:37-38`）。
- 悬停态：`ux-best-practices.md:119-122` 要求悬停布局适配与悬停创新；知识库没有悬停状态 API/姿态 API 名称。API 26 提到“窗口姿态模式查询与监听（当前支持识别桌面模式）”（`ui-development.md` Release 新增），但只识别桌面模式，不能当作折叠悬停 API。
- 平行视界（`easyGo`）是 API 23 新增的系统级分栏兼容方案，可用于折叠屏/平板；配置见 §8。

### 3.6 多窗口、方向、安全区、导航条

已核实的硬性要求：

- 手机/折叠折叠态：适配竖向悬浮窗、上下分屏；折叠展开态额外适配上下分屏；平板及更宽：自由窗口、左右分屏、比例自由调节（`ux-best-practices.md:43-48`）。
- 手机/折叠折叠态竖屏布局；折叠展开态、平板同时适配横竖屏；接近 1:1 时横竖屏用相同/相近布局（`ux-best-practices.md:35-39`）。
- 核心内容/重要交互不要被挖孔遮挡；避免因避让挖孔导致左右留白不一致（`:40-41`）。
- 应用内底部元素自动抬高避让底部导航条；可滚动内容可显示在导航条下方，滚到底部时避让；沉浸式页面超过 2 秒自动隐藏导航条（`hmi-system-features.md:72-77`）。
- 系统栏 API 已核实：`setWindowSystemBarProperties`、`setWindowLayoutFullScreen(true)`、`setSpecificSystemBarEnabled('status'|'navigation', boolean)`、`on('systemBarTintChange')`（`basic-services.md:47-76`）。这些可用于画板/批注全屏，但退出时必须恢复。

**安全区 API**：知识库只在设计文档中提到“系统安全区”，没有 `expandSafeArea` / `getWindowAvoidArea` 等 API 名称。落地时需查 SDK；不要把“避让导航条”写成猜测的 API 调用。

### 3.7 InkCanvas 逐窗口的分档建议

| 窗口/组件 | 平板 | 2in1 | 手机（初步） |
|---|---|---|---|
| 浮条 FloatingBar | 单行完整按钮；可两行；贴边 | 单行完整；可自由拖动/记忆位置 | 精简为 5–7 个核心按钮 + “更多”；大按钮 |
| 调色盘 PenPalette | 两行预设 + 自定义 | 同平板；支持悬停预览 | 全屏半模态；大色块 |
| 图形目录 ShapePreview | 网格/分组 | 网格 + 右键/悬停 | 分页网格 |
| 画板 Board | 三栏（页/画布/工具） | 三栏 + 可缩放 | 单栏 + 底部页条；工具抽屉 |
| 设置 Settings | 侧边面板 440 或分栏 | 独立窗口/侧边面板 440 | 全屏页/半模态 |
| PPT 导航 | 底部浮牌 | 底部浮牌 + 快捷键 | 精简上/下一页 |
| 计时/抽奖 | 可浮动卡片 | 独立窗口/卡片 | 全屏页 |
| 回放 | 画布 + 底部控制条 | 同平板 + 键鼠 | 全屏画布 + 精简控制 |

---

## 4. `ContainerReader` / `WithEnv`（API 26）与 API 24 回退

### 4.1 API 26 新能力（名称已确认，签名未知）

| 能力 | 知识库原文 | 起始版本 | 能确认的语义 | 不能确认的 |
|---|---|---|---|---|
| `ContainerReader` | “ContainerReader 容器断点组件：基于容器尺寸而非窗口尺寸实现自适应布局”（`SKILL.md:516`、`:808`；`ui-development.md` API 26 Beta1） | API 26.0.0 Beta1（2026-06-12） | 断点依据是**容器**尺寸，不是窗口/屏幕；适合面板/分栏内部自适应 | 组件构造参数、断点配置类型、回调签名、是否要求 API 26 编译 |
| `WithEnv` / `@CustomEnv` | “响应式环境变量组件新增 `WithEnv` 容器和 `@CustomEnv` 自定义环境变量”（`SKILL.md:399`、`ui-development.md:50`） | API 26.0.0 Beta2 | 提供“环境变量”容器与自定义环境变量装饰器；用于向下传递响应式环境 | 环境变量类型、键名、注入方式、读取 API、与 `@StorageProp` 的关系 |

**结论**：在拿到 API 参考前，`platform/compat` 只能暴露“是否可用”的布尔能力和自研回退，不能在业务层直接写 `ContainerReader({...})` 或 `@CustomEnv`。

### 4.2 API 24 回退：知识库有依据的路径

#### 路径 A：组件尺寸事件（推荐，API 12+）

`dev/references/ui-development.md:307-315` 明确写出：

```typescript
Text('Resize Me')
  .onSizeChange((oldValue, newValue) => {
    console.info(`Old: ${JSON.stringify(oldValue)}, New: ${JSON.stringify(newValue)}`)
  })
```

落地建议：把每个需要“容器断点”的区域包一层 `Column`/`Stack`，在它上面挂 `onSizeChange`，用新宽度算 `Breakpoint`。这能覆盖 API 24，且语义与 `ContainerReader` 最接近（容器而非窗口）。**待核实**：`newValue` 的字段名与单位（vp）。

#### 路径 B：窗口尺寸/窗口属性（API 24 可用，但字段未知）

`system/references/basic-services.md` 已确认：

```typescript
let mainWindow: window.Window | undefined = undefined;
window.getLastWindow(getContext(this), (err, data) => { ... });
mainWindow.resize(720, 1280, (err) => { ... });
mainWindow.on('windowStageEvent', ...); // 实际为 windowStage.on
```

`app-framework.md:177` 只演示了 `getWindowProperties().displayId`，**没有 width/height 字段**。因此“从窗口属性拿宽度”需查 SDK d.ts；在确认前，优先用路径 A。

#### 路径 C：`LocalStorage` + `@StorageProp`（API 24 可用）

`dev/references/other.md` 与 `multi-device.md:50` 已确认：

```typescript
let storage = new LocalStorage({ 'currentBreakpoint': 'sm' })

@LocalStorageProp('currentBreakpoint') bp: string = 'sm'
```

这是消费端；写入端仍由 `BreakpointService` 负责。`@StorageProp` 是单向同步（父→子/存储→组件），适合“全局断点”这种只读状态。

#### 路径 D：mediaquery —— **知识库没有**

全库检索 `mediaquery`、`媒体查询`、`matchMedia` 均无命中。结论：

- 不能把 `@ohos.mediaquery` 写成“知识库确认的 API”；
- 如果团队按任务方向采用 mediaquery，需在官方 API 参考确认模块名、`matchMediaSync` 的查询语法、监听回调与 `matches` 的读取方式，并在 API 24 真机上验证；
- 在验证前，`parity/api-matrix.md` 应把它标为“待验证/不使用”，而不是“回退方案已定”。

### 4.3 推荐的 `platform/compat` 能力接口（工程建议，不是已验证 API）

```typescript
// platform/compat/DevicesCompat.ets —— 示意；实现需按 SDK 核对
export enum Breakpoint { XS, SM, MD, LG, XL }

export interface DeviceCapabilities {
  readonly apiLevel: number;          // 来源待核实（deviceInfo.sdkApiVersion 未在知识库出现）
  readonly containerBreakpoints: boolean; // API 26 且 ContainerReader 可用
  readonly responsiveEnv: boolean;        // API 26 且 WithEnv 可用
  readonly systemMaterial: boolean;       // API 26
  readonly hdsColorPicker: boolean;       // API 26
  readonly hdsMaterial: boolean;          // API 23+
}

export interface BreakpointSource {
  current(): Breakpoint;
  observe(cb: (bp: Breakpoint) => void): () => void;
}
```

```typescript
// 业务层只这样用
if (this.caps.containerBreakpoints) {
  // 26 路径：ContainerReader（参数待 API 参考确认）
} else {
  // 24 路径：容器 onSizeChange + BreakpointService
}
```

**纪律**：`core`/`ink`/`controls`/`features`/`entry` 不直接判断 API 版本；所有版本分支集中在 `platform/compat`，并在 `parity/api-matrix.md` 登记。

---

## 5. HarmonyOS 设计语言的事实清单

### 5.1 色彩（`design/references/color.md`）

| 类型 | 值 | 用途 |
|---|---|---|
| 宇宙蓝 Primary | `#007DFF` | 主色调、关键操作、重点信息 |
| 深蓝 Dark Primary | `#003BB3` | 深色模式主色 |
| 浅蓝 Light Primary | `#99CCFF` | 背景色、辅助信息 |
| 成功 | `#36D1A1` | 正向操作/成功 |
| 警告 | `#F7CE46` | 需注意的提示 |
| 错误 | `#E84026` | 错误/危险 |
| 信息 | `#478EDC` | 提示性信息 |

Token 分层（`color.md:39-53`）：全局级（`brand.color.primary`）→ 语义级（`button.background.primary`）→ 平台级（`button.bg.blue`）。浅/深色自动切换；全局改一处，引用自动更新。

深色模式建议（`color.md:65-72`）：

| 元素 | 浅色 | 深色 |
|---|---|---|
| 背景 | `#FFFFFF` | `#000000` / `#1C1C1E` |
| 卡片 | `#F5F5F5` | `#2C2C2E` |
| 主文字 | `#000000` | `#FFFFFF` |
| 次文字 | `#666666` | `#8E8E93` |

注意：`color.md:62` 明确“卡片背景在深色模式下避免纯黑（#000000），建议使用深灰”。背景可以是纯黑，卡片不要纯黑。

### 5.2 字体（`design/references/typography.md`、`templates/references/design-resources.md`）

- HarmonyOS Sans 是系统默认字体，系统内置无需安装；支持简繁中文、拉丁、西里尔、希腊、阿拉伯等 5 大书写系统、105+ 语言；可变字重（资源文档写 10 档，常列 Thin/Light/Regular/Medium/Bold/Black）。
- 特性：动态字重（用户可选，系统按观看距离/设备尺寸推荐）、深色模式使用更细字重以保持视觉粗细、等宽/变宽数字、时钟数字。
- 字号层级（手机/折叠/平板）：

| 层级 | 字号 | 用途 |
|---|---|---|
| 大标题 | 36–40 fp | 页面大标题 |
| 标题1 | 28–32 fp | 区块标题 |
| 标题2 | 24 fp | 卡片标题 |
| 标题3 | 20 fp | 次级标题 |
| 正文 | 16 fp | 主体 |
| 副文本 | 14 fp | 辅助说明 |
| 注释 | 12 fp | 标签/备注 |

- 智慧屏字号比手机大 1.5–2 倍；手表更大/更粗（本项目不涉及）。
- 折展态文本单屏不超过 40 字，推荐约 36 字；展开态字号 ≤ 折叠态 1.2 倍。
- `fontVariant(['tabular-nums'])` 可用于等宽数字（倒计时、页码）——已在 `design-resources.md:56-57` 给出示例。

### 5.3 布局、间距、圆角

- 12 栅格；4/8 vp 基准；gutter 8/12/16/24；安全边距 12/16/24（见 §2.4）。
- 圆角：知识库只在 `design-changes.md:17` 提到 2026-06-12 新增“圆角参数”文档，**没有给出任何官方圆角阶梯数值**；唯一出现的具体值是 `HdsMaterial` 示例里的 `cornerRadius: 16`（`ui-design-kit.md:301`），不能当作设计 token。`ui-development.md:305` 只说明“圆角”属于可动画的外观属性。
- 结论：**不要凭空造一套 HarmonyOS 圆角 token**。自绘 chrome 沿用项目 8/14/6.94 等值；系统组件用其默认圆角。

### 5.4 动效（`design/references/animation.md`）

核心原则：有意义、自然流畅、快速响应、性能优先。

| 类型 | 推荐时长 |
|---|---|
| 快速反馈 | 100–200 ms |
| 通用过渡 | 300–500 ms |
| 强调动效 | 500–800 ms |
| 复杂转场 | 800–1200 ms |

- 帧率推荐 60 FPS（≈16.67 ms/帧）。
- 曲线：Linear、Ease、Ease-in、Ease-out、Ease-in-out、Fast-out-slow-in、Friction。
- 转场：前后相邻界面**必须**有转场，避免一帧跳转；建议 300–500 ms、ease-in-out。
- 微动效：加载、反馈、氛围。
- 性能：避免布局重排、优先 transform/opacity、控制同屏动画数量、复杂动画考虑帧动画。

**ArkUI 动画 API 缺口**：知识库没有给出 `animateTo`、`animation`、`transition`、`curves` 的具体签名；“属性动画（API 14+ 增强）”只列出可动画属性类别（`ui-development.md:297-305`）。落地前需查 API 参考。

### 5.5 控件（`design/references/controls.md`）

知识库把控件分为六大类（导航、展示、操作、输入、选择、容器），并列出约 50 个名词（导航 4、展示 15、操作 8、输入 4、选择 7、容器 3，另有跨类）。设计原则：

1. **优先使用系统控件**，保证基础体验，减少设计与开发量；
2. 必要时自定义样式/大小以体现品牌，但需遵循交互规范；
3. 所有控件适配 Phone、Tablet、PC/2in1、Wearable；
4. 穿戴需关注圆形/方形布局（本项目不涉及）。

常用控件（ArkUI）事实：`Text`、`Button`（`ButtonType.Normal/Circle/Capsule`）、`Image`、`Span`、`Column/Row/Stack`、`List`、`Grid`、`Tabs`、`Navigation`、`Video`、`XComponent`、`AlertDialog/ActionSheet/CustomDialog`、`TextInput/TextArea`、`Checkbox/Radio/Toggle`、`Slider`、`Circle/Rect/Shape/Path`。均见 `templates/references/arkui-components.md`。

### 5.6 材质与“沉浸光感”

知识库把两个名字分开：

- **API 23（6.1.0，API 24 继承）**：HDS 组件支持 `systemMaterialEffect`，取值 `AUTO`（系统按算力自适应 SMOOTH/EXQUISITE/GENTLE）、`SMOOTH`、`EXQUISITE`、`GENTLE`；可通过 `hdsMaterial.getSystemMaterialTypes()` 查询设备支持的材质类型，判断是否包含 `'IMMERSIVE'`（`animation.md:99-128`）。`HdsMaterial` 组件支持 `MaterialType.BLUR`、`MaterialLevel.LEVEL_2`、`cornerRadius`（`ui-design-kit.md:286-311`）。
- **API 26（26.0.0 Release）**：所有支持通用属性的组件支持 `systemMaterial`；新增组件级沉浸光感；新增材质等级枚举、获取全局材质等级、判断是否支持沉浸式系统材质 `ImmersiveMaterial` 的接口；弹窗类（Tips/Toast/对话框/操作菜单/自定义弹窗/半模态/Popup）支持系统材质（`SKILL.md:508-518`、`ui-development.md` Release）。
- 命名冲突：`animation.md` 写 `@hms.hds.hdsMaterial`，`ui-design-kit.md` 写 `@kit.UIDesignKit` 的 `HdsMaterial`。这可能是同一能力的不同封装层级，也可能文档版本不一致；**以 SDK 类型定义为准**。

**设计判断**：沉浸光感是“系统观感增强”，会随设备算力、壁纸、明暗变化；InkCanvas 的浮条是功能仪器，首要目标是可读性与一致对比度。建议**默认关闭材质**，用项目半透明底色 + 边框；仅在“关于/设置/瞬时提示”等非核心浮层上可选开启，并提供性能降级。

---

## 6. Fluent token → HarmonyOS 的映射策略

### 6.1 单一来源的现实状态

项目设计文档（`docs/superpowers/specs/2026-11-18-harmonyos-port-design.md` §5.5）计划用 `parity/tokens.json` 作为唯一来源，驱动 C# `Tokens.xaml` 与 ArkTS `ThemeTokens.ets`。**本工作树当前没有 `parity/` 目录**（`ls parity` 失败），因此：

- 现在的事实来源是 `src/InkCanvas.Controls/Themes/Tokens.xaml`、`Theming/PopupBrushes.cs`、`FloatingBar.cs` 的 S 换算常量，以及 `05-controls.md` 的盘点。
- 在动 UI 代码前，应先建立 `parity/tokens.json`，否则两侧会继续漂移。已知漂移例子：`Tokens.xaml` 的 accent 是 `#0067C0`，`PopupBrushes.Accent` 是 `#0066BF`，差 1 个蓝通道单位；浮条选中态 `#801296DB` 与 token accent 也不是同一色。

### 6.2 颜色映射

| InkCanvas token | Light | Dark | HarmonyOS 参考 | 决策 |
|---|---|---|---|---|
| `IcSurfaceColor` | `#F6F7F9` | `#202124` | 背景 `#FFFFFF`/`#000`，卡片 `#F5F5F5`/`#2C2C2E` | **保留项目值**；深色接近官方卡片灰 |
| `IcSurfaceAltColor` | `#FFFFFF` | `#2B2C2F` | 卡片 `#F5F5F5`/`#2C2C2E` | 保留；与官方深色几乎一致 |
| `IcStrokeColor` | `#1F000000`（12% 黑） | `#24FFFFFF`（14% 白） | 官方未给描边 token | 保留；自绘边框 |
| `IcTextPrimaryColor` | `#E4000000`（89% 黑） | `#F2FFFFFF`（95% 白） | `#000000`/`#FFFFFF` | 保留；对比度足够，避免纯黑过硬 |
| `IcTextSecondaryColor` | `#9E000000`（62% 黑≈`#616161`） | `#A8FFFFFF`（66% 白≈`#A8A8A8`） | `#666666`/`#8E8E93` | 保留；与官方灰阶接近 |
| `IcAccentColor` | `#0067C0` | `#4CC2FF` | 宇宙蓝 `#007DFF`/深蓝 `#003BB3` | **保留 Fluent accent**，不换系统蓝；系统控件改不了色时接受或自绘 |
| `IcAccentSoftColor` | `#1A0067C0` | `#264CC2FF` | 无直接对应 | 保留 alpha tint |
| `IcBoardBlackColor` | `#1F3B2C` | `#16281E` | 内容色，非 UI token | 保留；深色模式不改变画板内容色 |
| `IcBoardWhiteColor` | `#FAFAFA` | `#1B1B1B` | 内容色 | 保留 |
| `PopupBrushes.Surface` | `#F2F0F3F9` | `#F21F1F1F` | 官方卡片灰 | 保留（95% alpha） |
| `PopupBrushes.Accent` | `#0066BF` | 同左 | 与 token 差 1 | **统一到 `#0067C0`** 或反之，进 tokens.json 决策 |
| `FloatBarBackground` | 白 75% `#BFFFFFFF` | 黑 60% `#99000000` | 官方材质/模糊 | 保留半透明；可选 `backdropBlur(20)`（API 14+） |
| `FloatBarBorder` | 黑 80% `#CC000000` | 白 60% `#99FFFFFF` | 无 | 保留；保证浮条在任意背景上的边界 |
| `Danger` | `#E32A34` | 同左 | 错误 `#E84026` | 保留项目红，或统一到官方错误色；建议项目内统一后再决定 |
| `Success`/`YesGreen` | `#15803D` | 同左 | 成功 `#36D1A1` | 保留项目绿；官方绿更亮，混用会不一致 |

**原则**：HarmonyOS 色彩是“系统品牌色”，不是 InkCanvas 品牌色。跨平台一致性优先于“像 HarmonyOS 原生应用”。但系统控件（Toggle/Slider/Progress/Checkbox/Radio）若使用系统默认色，会出现“项目蓝 + 系统蓝”并存；必须在以下三者中选一：

1. 这些控件全部自绘（成本高，但一致）；
2. 系统控件允许改色时统一改为 `IcAccentColor`（需查各控件 API）；
3. 接受少量系统蓝（设置页等低频页面），核心 chrome 自绘。

建议：**核心 chrome 自绘 + 低频设置页系统控件**，在 `parity/api-matrix.md` 记录“系统控件颜色不可控”的已知差异。

### 6.3 字体映射

| 角色 | 项目值 | HarmonyOS 参考 | 决策 |
|---|---|---|---|
| 浮条图标 | 22.22 DIP | 图标字号可任意；Symbol 随 `fontSize` | 22.22 fp/vp；SymbolGlyph 用 `fontSize(22)` 量级 |
| 浮条标签 | 11.11 DIP | 注释最小 12 fp | **冲突**；建议手机 `max(12, 11.11×scale)`，平板/2in1 可用 11.11 但需真机可读性验证 |
| 更多设置标签 | 13.89 DIP | 副文本 14 fp | 映射到 14 fp |
| 设置正文 | 项目设置页字号（需另查） | 16 fp | 设置页正文用 16 fp |
| 对话框标题 | — | 标题3 20 fp | 用 20 fp |
| 对话框正文 | — | 正文 16 / 副文本 14 | 用 14–16 fp |
| 页码/倒计时 | — | 等宽数字 | `fontVariant(['tabular-nums'])`，避免数字跳动 |
| 字体家族 | Segoe UI / 系统 | HarmonyOS Sans（内置） | 用 HarmonyOS Sans；不要打包 Segoe |

**字号单位**：ArkUI `fontSize` 的数字默认按 fp 处理（知识库示例均直接写数字，未显式标单位）。用 fp 会尊重用户字体缩放，但会破坏“像素级一致”；用 vp 可锁死视觉尺寸但损害无障碍。建议：

- 正文/标签/对话框：**fp**，接受用户缩放；
- 浮条图标与固定尺寸按钮：可用 vp（图标不随字体缩放，避免撑破 50 高浮条），但需给标签留自适应空间；
- 在 `tokens.json` 中区分 `fontRole` 与 `iconSize`，不要混为一个数值。

### 6.4 间距、圆角、触摸目标

| 项目 token | 值 | HarmonyOS 参考 | 决策 |
|---|---|---|---|
| `IcTouchTargetMin` | 40 | ≥44×44 dp | **冲突**；增加 `hitTargetMin=44`，视觉 40、热区 44 |
| `IcTouchTargetComfortable` | 48 | 44+ | 保留 |
| `IcFloatingBarIcon` | 22 | 图标可任意 | 保留 |
| `IcControlCorner` | 8 | 官方圆角阶梯未给 | 保留 |
| `IcBarCorner` | 14 | 官方圆角阶梯未给 | 保留 |
| `PartCorner` | 6.94 | — | 保留（S 换算） |
| `PartGap` | 2.78 | 官方 4/8 | 保留（浮条密度） |
| `IcBarPadding` | 8,6,8,6 | 官方 8/12/16 | 保留 |
| `IcCardPadding` | 16,12,16,12 | 官方 16 | 保留 |
| 设置面板宽 | 440 | 自定义弹窗 ≤400vp（8 栅格以上） | **若为弹窗则冲突**；改为侧面板/独立窗口或压到 400 |
| 浮条高 | 50（契约 55） | 官方无 | 保留项目值；55 若指含阴影/外边距，需在 tokens 里拆开 |

**建议**：在 `tokens.json` 增加平台覆盖字段，例如：

```json
{
  "touchTargetMin": { "default": 40, "harmony": 44 },
  "settingsPanelWidth": { "default": 440, "harmonyDialogMax": 400 }
}
```

这样既保留 C# 侧契约，又能在 HarmonyOS 侧满足 44 热区/400 弹窗上限，不污染业务代码。

### 6.5 动效映射

| 项目 | 值 | HarmonyOS 参考 | 决策 |
|---|---|---|---|
| `Motion.Fast` | 140 ms | 快速反馈 100–200 | 保留 |
| `Motion.Normal` | 240 ms | 通用过渡 300–500 | 组件内微交互保留 240；页面级转场用 300 |
| `Motion.Slow` | 380 ms | 通用过渡 300–500 | 保留 |
| 入场 | 240 ms 淡入 + 12 px 上移 | 通用 300–500 | 可保留 240；若做页面转场用 300 |
| Pulse | spring damping 0.55/0.7，period 60/80 ms，delay 120 ms | 曲线列表有 Friction/弹性语义，无 spring 签名 | 保留参数意图；ArkUI spring API 需查 |
| 减少动效 | `UISettings.AnimationsEnabled` 每 5 秒缓存 | 知识库无对应 API | **待验证**；必须找到系统“减少动效”开关，否则不能声称无障碍等价 |

### 6.6 哪些用系统组件、哪些自绘

| 组件 | 选择 | 理由 |
|---|---|---|
| 浮条 FloatingBar / 折叠把手 | **自绘** | 高密度、贴边、拖动、形态独特；系统 ActionBar/Toolbar 无法复刻 |
| TouchIconButton | **自绘**（基于 `Stack/Column` + `onClick`） | 需要 ≥40/44 热区、选中 wash、图标+标签双行、悬停/按下态 |
| 调色盘 PenPalette | **自绘** | 预设色 + 最近色 + 自定义；系统 ColorPicker 是通用选择器，形态不符 |
| 图形目录/预览 | **自绘**（`Canvas`/`Shape`/`Path`） | 24 种图形矢量预览、长按固定；系统无对应 |
| 画板画布 | **自绘**（`Canvas`/`XComponent`） | 墨迹渲染必须自控；`Canvas`/`XComponent` 已核实存在 |
| 计时环 | **自绘**（`Canvas` 圆弧） | 系统 `Progress` 无环形成熟示例；需精确控制 |
| 抽奖滚动/名单 | **自绘 + 系统 Text/List** | 动画与布局自控 |
| 设置页列表 | **系统 `List` + `HdsListItem`（基础版本待查；API 23 有滑动删除等增强）或自定义行** | 无障碍、键盘、滑动删除由系统提供；样式用项目 token 包壳 |
| 设置页开关/滑杆/输入 | **系统 `Toggle`/`Slider`/`TextInput`** | 行为、无障碍、键盘焦点成熟；接受系统色或查改色 API |
| 对话框/确认框 | **`CustomDialog` + 项目 token** | 需要 Fluent 外观；但注意 400vp 上限；系统 `AlertDialog` 可用于低频确认 |
| 半模态面板 | **`BindSheet`**（系统） | 手机上的颜色选择/更多设置；系统手势与避让成熟 |
| 提示/撤销 | **`HdsSnackbar`（基础版本待查；自动高度 API 23+）或自定义 Toast** | 系统提示条有无障碍与自动高度；API 26 才有图标/标题样式增强 |
| 导航（设置/关于） | **`Navigation`/`HdsNavigation`（基础版本待查；API 23 有分栏/标题增强）** | 分栏/标题/返回由系统处理；核心画板不用 |
| 侧栏（平板画板页缩略图） | **`HdsSidebar`（基础版本待查；API 23 有滑动手势增强）或自绘** | HdsSidebar 有滑动手势；但视觉需评估 |
| 底部页签（手机） | **`Tabs`/`HdsTabs`（HdsTabs 基础版本待查；悬浮/迷你栏 API 23）** | 系统导航规则；InkCanvas 无传统页签，可只用于“更多”页 |
| 颜色选择（自定义色） | **自绘色轮/色板**；API 26 可选用 `HdsColorPicker` | 26 以下不可用；且通用选择器与调色盘交互不同 |

**HDS 组件的版本事实**：`dev/SKILL.md:154-166` 在 HarmonyOS 6.1.0（API 23）下列出 UI Design Kit 的新增项：底部页签悬浮样式及迷你栏、导航双栏/占位/标题字号、底部页签与导航的沉浸材质、列表卡片无障碍、材质模块（类型/等级）。这说明 **API 23 是这些“增强/材质”的起始点**；但 `ui-design-kit.md` 对 `HdsSidebar` 写“API 23 新增滑动手势开关”、对 `HdsActionBar` 写“API 23 新增边距设置”、对 `HdsSnackbar` 写“API 23 新增自动高度适配”，说明这些组件的**基础版本早于 API 23**。知识库没有逐个组件列出“基础版本起始 API”，落地前需查 API 参考。`HdsColorPicker` 明确 API 26 新增；`HdsSnackbar` 的图标/标题/关闭按钮样式 API 26。API 24 设备可用 API 23 的这批增强。

**签名不一致提醒**：
- `templates/references/arkui-components.md:130-149` 的 `HdsTabs` 用 `controller` + `barFloatingStyle` + `TabContent` 子组件；
- `dev/references/ui-design-kit.md:68-78` 的 `HdsTabs` 用 `tabs: [...]` + `tabStyle`。
两者不可能同时是同一个 API 形态。落地前必须以 SDK d.ts/官方 API 参考为准，不能照抄任一段。

### 6.7 深色模式

已核实：

- 设计规范要求“跟随系统深色模式实时切换”（`multi-device-design.md:59,67`）。
- 深色背景/卡片/文字建议见 §5.1；卡片避免纯黑。
- 深色模式下文字对比度需符合无障碍；状态栏颜色跟随；避免硬编码颜色，统一 Token（`color.md:58-63`）。
- 资源限定符目录含 `dark/`（`multi-device.md:96`）。
- 字体在深色模式使用更细字重（`typography.md:17`）。

知识库**没有**给出：

- 读取当前颜色模式的 API（如 `configuration.colorMode`）；
- 应用级强制浅色/深色的配置键；
- 监听颜色模式变化的回调字段（`AbilityStage.onConfigurationUpdated(env)` 存在，但 `env` 字段未列）。

落地建议：

1. **系统跟随**：用 `resources/dark/` 提供深色资源；这是知识库唯一明确的机制。
2. **手动主题（项目 theme=0/1/2）**：不要依赖系统 API；在 `ThemeService` 中直接选择 `ThemeTokens.light`/`dark` 对象，组件通过 `@StorageProp('themeMode')` 读取。这样无需知道颜色模式 API，也能实现“浅色/深色/跟随系统”三态；但“跟随系统”仍然需要系统颜色模式输入，标注为待验证。
3. **画板内容不随 UI 深色模式变化**：BoardBlack/BoardWhite、笔色、荧光笔色是内容语义色，必须保持原值。
4. **状态栏**：用 `setWindowSystemBarProperties({ statusBarColor, statusBarContentColor, navigationBarColor, navigationBarContentColor })` 按主题设置；这是已核实 API（`basic-services.md:47-63`）。

### 6.8 本地化（`dev/references/localization-kit.md`）

事实：

- 包名 `@kit.LocalizationKit`；关键模块 `@ohos.i18n`（区域、时区、日历）、`@ohos.intl`（日期/数字/排序格式化）、`@ohos.resourceManager`（多语言资源加载）。
- **无特殊权限**；支持模拟器。
- 设计准则：不对文化习惯做假设（如数字分组分隔符）；UI 元素作为资源与代码分离；只翻译资源，不改逻辑。
- 26.0.1 Beta1 新增字体管理（第三方字体安装/卸载/查询/状态监听）——**不在 Release 基线，不可依赖**。

对 InkCanvas 的落地：

- 所有用户可见字符串进 `resources/base` 与语言限定符目录；不要在 ArkTS 里硬编码中文。
- `.icink` / `Settings.json` 的键名和数值格式**不随语言变化**；时间戳/自动保存文件名 `{HHmmss}-{reason}.icink` 必须用固定格式（不要用本地化数字），否则两侧文件互认会坏。
- 倒计时显示可用本地化数字，但保存文件名/日志用 invariant。
- 浮条标签是短文本，翻译成德语/俄语可能膨胀；按钮宽度要允许 1.2–1.3 倍或换行/省略。
- 中文/英文由 HarmonyOS Sans 覆盖，无需打包字体；若未来支持阿拉伯语 RTL，需单独验证布局镜像（知识库只在 UX 标准里提到 LTR/RTL 定义，没有 ArkUI RTL 细节）。

---

## 7. 图标体系：HarmonyOS Symbol vs 自带图标字体

### 7.1 Symbol 的事实（`design/references/icon.md`、`templates/references/design-resources.md`）

- HarmonyOS Symbol 是华为与汉仪 2024 年 6 月联合发布的图标字库；**1500+** 矢量图标。
- 特性：任意分辨率清晰；分层结构（每个图层独立颜色/灰度）；与 HarmonyOS Sans 字重同步；**7 种预设动效**（消失、出现、弹跳等），可与点击、长按、输入、数据传输等操作绑定。
- 渲染策略：`SINGLE`（单色，默认）、`MULTIPLE`（多色/分层着色）；`design-resources.md:26` 还写了 `HIERARCHICAL`（分层灰度）。三者的准确枚举以 SDK 为准。
- 代码：

```typescript
SymbolGlyph($r('sys.symbol.ohos_folder_badge_plus'))
  .fontSize(96)
  .renderingStrategy(SymbolRenderingStrategy.SINGLE)
  .fontColor([Color.Black, Color.Green, Color.White])
```

- 动效示例：`new SymbolSweepEffect()`、`SymbolBounceEffect`、`SymbolFadeEffect`、`SymbolScaleEffect`（`design-resources.md:31-34`）。
- 图标热区：最小触控热区不低于 44×44 dp；与其他元素留间距（`icon.md:65-71`）。
- 应用图标：多尺寸、PNG 推荐、HMOS 角标、品牌一致（`icon.md:74-85`）；这些是**桌面图标**，不是应用内图标。

### 7.2 版本缺口

知识库**没有**写 `SymbolGlyph` 的起始 API 版本，也没有写 `sys.symbol.*` 的完整名称目录。可确认的只有“HarmonyOS Symbol 2024-06 发布”和“API 26 文档仍在使用”。因此：

- 不能假设 API 24 一定支持全部 Symbol；必须在一台 API 24 真机上验证 `SymbolGlyph` 与目标图标名；
- 不能凭名字猜 `sys.symbol.ohos_*`；必须用 DevEco 的 Symbol 资源浏览器/官方图标库核对；
- 若 API 24 不支持，回退到自绘 Path/SVG 图片（`Image($r('app.media.ic_xxx'))`）。

### 7.3 与项目 `Glyphs.cs` 的映射

项目图标来源（`src/InkCanvas.Controls/Controls/Glyphs.cs`）：

- 绝大多数是 **Segoe Fluent Icons / Segoe MDL2 Assets** 的私用区码点；
- `Shapes` 是 `Segoe UI Symbol` 的 `△`；
- 包含上游 WPF 浮条的精确码点（`PenWpf`、`HighlightWpf`、`EraserOverlayCircle` 等）。

映射策略（建议）：

1. 建立 `parity/glyphs.json`：`{ "Pen": { "segoe": "\uE70F", "symbol": "sys.symbol.xxx", "fallback": "path:pen" }, ... }`。
2. 通用语义图标（设置、相机、计时、人员、画板、上下左右、锁、关闭、播放、暂停、重播、帮助、信息、下载）优先映射到 Symbol。
3. 笔画/工具专用图标（笔、荧光笔、橡皮、选择、形状、插入媒体、手势、折叠）若 Symbol 中没有语义完全一致的，使用**自绘矢量**（`Path`/`Canvas`）而不是硬凑一个近似图标；否则用户会误读工具。
4. 同一界面最多两种图标风格、线宽统一（`icon.md:56-63`）。
5. **不要打包 Segoe 字体**：它是微软授权字体，随 GPL 应用分发有法律风险，且 HarmonyOS 工程也没有“自定义字体加载”的已核实 API。Symbol + 自绘是更安全的路径。
6. 图标尺寸与热区分离：Symbol 视觉 22，热区 44；浮条紧凑场景下用透明容器扩大热区。

### 7.4 Symbol 的着色与深色模式

- `fontColor([...])` 可给分层图标逐层着色；单色模式用数组第一个颜色。
- 深色模式用 `IcTextPrimaryColor`/`IcTextSecondaryColor` 对应值；不要给图标写死黑色。
- `MULTIPLE` 多色图标在深色背景上可能对比不足；核心工具图标建议统一 `SINGLE`，品牌/状态图标才用多色。
- Symbol 与 HarmonyOS Sans 字重同步：如果浮条整体加粗，图标会跟着变粗；这是优点，但也会改变视觉密度，需在 50 高浮条上验证。

---

## 8. API 24 / API 26 设计相关差异总表

> 说明：`API 24 = 6.1.1` 继承 `6.1.0 = API 23` 的设计能力；下表“API 24”列包含 API 23 已提供的部分。**“设计文档新增”与“运行时 API 可用”分开看**。

| 能力 | API 24（6.1.1） | API 26（26.0.0） | 门控方式 |
|---|---|---|---|
| `ContainerReader` 容器断点 | 不可用（知识库只列在 26 Beta1） | 可用（名称/方向已确认，签名未知） | API 级别；无已记录 SystemCapability 字符串 |
| `WithEnv` / `@CustomEnv` | 不可用（26 Beta2） | 可用（签名未知） | API 级别 |
| `systemMaterial`（所有通用属性组件） | 不支持通用组件；仅 HDS 组件有 `systemMaterialEffect` | 所有支持通用属性的组件支持 | API 级别 + 可选降级 |
| 沉浸光感材质等级 / `ImmersiveMaterial` 判定 | 无材质等级枚举；HDS 可用 `AUTO/SMOOTH/EXQUISITE/GENTLE` | 新增材质等级枚举、全局等级、`ImmersiveMaterial` 判定 | API 级别；`hdsMaterial.getSystemMaterialTypes()` 仅能查 HDS |
| `HdsTabs` 悬浮页签 + 迷你栏 | 可用（API 23 新增该样式；基础 HdsTabs 起始版本未列） | 可用 | 按 API 23 增强；基础版本待查 |
| `HdsMaterial`（BLUR/LEVEL_2） | 可用（API 23 新增材质模块） | 可用 | 无需门控（≥24） |
| `HdsNavigation`/`HdsSidebar`/`HdsActionBar`/`HdsListItem` | 基础版本早于 23（知识库未列起始 API）；API 23 有增强；部分增强在 26 | 可用 + 更多增强 | 组件级按 API 参考 |
| `HdsColorPicker` | 不可用 | 可用（网格/光谱/滑块 + 收藏） | API 26；回退自绘调色盘 |
| `HdsSnackbar` 图标/标题/关闭按钮样式 | 基础 HdsSnackbar 起始版本未列；自动高度 API 23 | 样式选项可用 | API 26 局部门控 |
| `easyGo` 平行视界 | 可用（API 23） | 可用 | `module.json5` 配置 + API 23 |
| `SymbolGlyph` / `sys.symbol.*` | **起始版本未知，需验证** | 文档使用中 | 实机验证；回退自绘/图片 |
| `fontId`（系统字体唯一标识） | 可用（API 14+） | 可用 | 无需门控 |
| `onSizeChange` 组件尺寸事件 | 可用（API 12+） | 可用 | 无需门控 |
| `backdropBlur` 背景模糊 | 可用（API 14+） | 可用 | 无需门控 |
| 闪控球/闪控窗 | 不可用（26 新能力） | 能力存在，但**权限/第三方资格未知** | 未知；不能作为首发依赖 |
| 圆角参数 / 间隔参数 / 鸿蒙黑体 | 设计指导同样适用（文档级） | 2026-06 新增文档 | 非运行时门控 |
| 字体管理（第三方字体安装/查询） | 不在 Release 基线 | 仅 26.0.1 Beta1 | **不可使用** |

### 8.1 `easyGo` 平行视界配置（API 23，API 24 可用）

`dev/references/multi-device.md:150-171`：

```json
// profile/easy_go.json
{
  "deviceType": "common",
  "displayModeOptions": {
    "landscape": { "splitRatio": "1:1", "primaryWindow": "start" }
  }
}
```

```json
// module.json5
"easyGo": "./profile/easy_go.json"
```

支持 `common`、`phone`、`tablet` 三种设备类型，可配置分栏比例和主页位置。InkCanvas 是否启用需评估：画板本身就是全屏画布，强行双页分栏可能挤压画布；可作为“设置 + 画板”并排的可选增强，不作为默认。

---

## 9. 运行时门控与 `canIUse`

### 9.1 已核实的 SystemCapability 字符串

全库只找到 3 个明确的 SysCap 字符串：

| 字符串 | 来源 | 用途 |
|---|---|---|
| `SystemCapability.AREngine.Core` | `media/references/ar-engine.md:82` | AR Engine 调用前 `canIUse` |
| `SystemCapability.Graphic.XEngine` | `media/references/arkgraphics-3d.md:187` | 3D 引擎能力查询 |
| `SystemCapability.PCService.EnterpriseDeployRestore` | `system/references/enterprise-foundation-kit.md:27` | 企业部署恢复（2in1） |

**没有**找到与 `ContainerReader`、`WithEnv`、`systemMaterial`、`HdsMaterial`、`SymbolGlyph`、`HdsColorPicker` 对应的 SystemCapability 字符串。因此：

- **禁止臆造** `SystemCapability.ArkUI.ArkUI.Full` 等字符串；
- 这些 UI 组件的可用性应按 **API 级别** 判断（编译期 `compileSdkVersion` + 运行期系统版本）；
- `canIUse` 只用于知识库/官方 SysCap 清单明确列出的能力。

### 9.2 可用但需核对的版本探测

项目设计文档（`2026-11-18-harmonyos-port-design.md` §6）计划用 `deviceInfo.sdkApiVersion` / `canIUse` 实现 `useApi26()`。事实核查：

- `system/references/device-info.md` 只列 `deviceInfo.uuid`、`deviceType`、`osFullName`；**没有 `sdkApiVersion`**。
- `dev/references/multi-device.md:68-75` 只演示 `deviceInfo.deviceType`。
- `system/references/basic-services.md:99-104` 演示 `systemParameter.getSync('const.ohos.devicetype')` 与 `const.ohos.fullname.version`；这是备选，但值格式未说明。

结论：`deviceInfo.sdkApiVersion` 是一个**合理的工程假设，但知识库未证实**。落地前：

1. 在 SDK 的 `@ohos.deviceInfo.d.ts` 中确认字段；
2. 在 API 24 与 API 26 真机各打印一次；
3. 把结果写进 `parity/api-matrix.md` 的“门控方式”列。

### 9.3 建议的门控顺序

```
1. 编译期：compileSdkVersion 省略（工具链内置，API 26）; compatibleSdkVersion 6.1.1(24); targetSdkVersion 26.0.0
2. 运行期：platform/compat 读取 apiLevel（字段待核实）
3. 能力布尔：containerBreakpoints = apiLevel >= 26 && 已实现探测
4. 组件选择：业务层只拿布尔/枚举，不写 if (api >= 26)
5. 记录：每个差异写 parity/api-matrix.md（能力 / API24 / API26 / 门控 / 备注）
```

---

## 10. 第三方应用的硬约束（设计相关）

以下均为知识库明确写出或“知识库没有能力记录”的结论；后者标“未知/需验证”。

1. **没有跨应用覆盖层 API**：知识库没有“在其他应用上绘制/悬浮”的 API、权限或窗口类型；`闪控窗` 只有“悬浮在桌面/应用界面上的小型窗口”一句，没有申请条件、权限、是否对第三方开放。**不能假设 InkCanvas 可以像 WinUI 版一样覆盖 PPT/任何应用**。
2. **没有任意置顶/点击穿透窗口的创建 API**：窗口章节只覆盖 `getLastWindow`、`findWindow`、`resize`、系统栏、`WindowStageEvent`。独立浮条窗口、置顶、Z 序、点击穿透均未记录，需实机验证。
3. **导航条避让是硬要求**：应用内底部元素自动抬高；沉浸页面 >2 秒自动隐藏导航条；从底部上滑/顶部下滑恢复（`hmi-system-features.md:72-77`）。
4. **自定义弹窗尺寸上限**：8 栅格及以上设备最宽 400vp；宽屏弹窗物理高度不超过手机上的 1.5 倍（`ux-best-practices.md:53-54`）。
5. **触摸目标 ≥44×44 dp**（设计规范）；与项目 40 冲突，需平台覆盖。
6. **手机避免两指以上手势**；双指平移/缩放可用，三指及以上需替代按钮。
7. **折叠屏信息/字号/弹窗 3/4 与 1.2 倍规则**；旋转平滑过渡。
8. **平板及更宽设备必须做键鼠基础适配**：悬停、快捷键、右键菜单（能力要求明确，API 未明）。
9. **深色模式跟随系统**；状态栏颜色跟随；卡片避免纯黑。
10. **UI Design Kit 无特殊权限**（`ui-design-kit.md:344-348`）；但这不等于跨应用窗口能力。
11. **上架工具链**：Beta 版 DevEco Studio 开发的应用不能上架（知识库 README/版本说明）；当前 26.0.1.290 Beta 只能用于构建验证。
12. **字体授权**：HarmonyOS Sans 系统内置可放心用；Segoe Fluent/MDL2 不能随应用分发；自定义字体加载 API 知识库未记录，26.0.1 字体管理是 Beta 不可用。

---

## 11. InkCanvas 落地建议（按层）

### 11.1 `platform/compat`（唯一版本/设备判断层）

建议文件与职责：

| 文件 | 职责 |
|---|---|
| `platform/compat/DevicesCompat.ets` | `apiLevel`、`deviceClass`（phone/tablet/2in1）、能力布尔（containerBreakpoints/responsiveEnv/systemMaterial/hdsColorPicker） |
| `platform/compat/Breakpoints.ets` | 窗口/容器宽度 → `Breakpoint`；订阅/取消订阅 |
| `platform/compat/WindowCompat.ets` | 系统栏、全屏、窗口尺寸、前景/后台事件；封装 API 24/26 差异 |
| `platform/compat/ThemeCompat.ets` | 颜色模式读取（待验证）、系统“减少动效”读取（待验证）、字体缩放 |
| `platform/compat/MaterialCompat.ets` | `HdsMaterial`（23+）/`systemMaterial`（26）可选封装；默认关闭 |

要求：

- 业务层禁止 `if (api >= 26)`、禁止直接 `import` 26 专属组件；
- 所有 `canIUse` 字符串必须来自官方 SysCap 清单，不臆造；
- 每个能力在 `parity/api-matrix.md` 有“API24 行为 / API26 行为 / 门控 / 验证状态”。

### 11.2 `controls/theme`（token 落地）

```
controls/theme/
  ThemeTokens.ets        // 由 parity/tokens.json 生成/校验；light/dark 两套
  ThemeService.ets       // 0 跟随系统 / 1 浅色 / 2 深色；@StorageProp('themeMode')
  Metrics.ets            // 浮条 S 换算常量、44 热区、440 面板宽等
  Motion.ets             // Fast 140 / Normal 240 / Slow 380 + 页面 300；减少动效开关
```

- `ThemeTokens.ets` 的字段名与 C# `Tokens.xaml` 的 key 一一对应（`IcSurfaceColor`→`surface` 等），由工具校验数值。
- 不要在组件里写 `#0067C0`；全部经 token。
- 内容色（笔色、画板黑/白、荧光笔）单独命名空间 `ContentColors`，不随主题切换。

### 11.3 `controls` 组件清单与实现路线

| 组件 | 路线 | 关键点 |
|---|---|---|
| `FloatingBar` | 自绘 `Column/Row` + 自定义按钮 | 50 高、S 换算间距、贴边/拖动、选中 wash、折叠把手；手机精简 |
| `TouchIconButton` | 自绘 | 视觉 40/41.67，热区 44；图标+标签；hover/pressed（API 待查） |
| `PenPalette` | 自绘 | 预设/最近/自定义色；不随深色模式变色；手机半模态 |
| `ShapePreview` | `Canvas`/`Shape`/`Path` | 24 图形；长按固定；与墨迹渲染共用几何 |
| `CountdownRing` | `Canvas` 圆弧 | 等宽数字；暂停/继续；性能 60FPS |
| `BoardChrome` | 自绘 + 系统 `List`/`Button` | 三栏/单栏；页缩略图；工具抽屉 |
| `SettingsPage` | 系统 `List`/`Toggle`/`Slider`/`TextInput` + 项目 token | 手机全屏，平板侧面板/窗口；若弹窗压到 400 |
| `Dialogs` | `CustomDialog` + token | 标题 20/正文 14–16；按钮 ≥44 热区；≤400vp |
| `Snackbar` | `HdsSnackbar`（23+）或自定义 | 保存/撤销反馈；26 可用图标样式 |

### 11.4 布局落地

```
entry/pages/
  Index.ets                 // 主窗口外壳：根据窗口宽度选择 Phone/Tablet/Desktop shell
  BoardPage.ets             // 画板：sm 单栏 / md 两栏 / lg+ 三栏
  SettingsPage.ets          // sm 全屏 / md+ 侧面板
```

- `Index` 根容器挂 `onSizeChange`（API 24 路径），把宽度写入 `BreakpointState`；API 26 可在需要“面板内部断点”的地方加 `ContainerReader`。
- PC/2in1 的窗口初始尺寸可用 `mainWindow.resize(720, 1280, ...)` 只是示例；实际按设备屏幕/上次记忆设置，且 `resize` 在大屏上的可用性需验证。
- 全屏批注时用 `setWindowLayoutFullScreen(true)` + `setSpecificSystemBarEnabled('navigation', false)`；退出恢复；沉浸 >2 秒自动隐藏导航条由系统/应用共同保证。
- 手机横屏/挖孔：核心内容避开挖孔；用安全区 API（待查）或系统默认布局避让。

### 11.5 图标落地

```
controls/icons/
  GlyphMap.ets              // parity/glyphs.json 的运行时映射：语义名 -> sys.symbol 或 Path
  IconRenderer.ets          // SymbolGlyph 优先；缺失走 Image/Path
  content/                  // 自绘 SVG/Path（笔、荧光笔、橡皮、图形、手势……）
```

- 先做 20–30 个高频图标的映射与实机对比，再做全量；
- 每个图标记录：语义名、Segoe 码点、Symbol 名、fallback、验证状态；
- 深色/浅色各截图一次，确认对比度；
- 不打包 Segoe。

### 11.6 分阶段建议

| 阶段 | 目标 | 设计层工作 |
|---|---|---|
| M0 | 工程骨架 | 建 `parity/tokens.json`、`glyphs.json`；定 `ThemeTokens.ets` 字段；定断点枚举；不写 UI |
| M1 | 平板全屏书写 | 自绘浮条/调色盘/画布；系统设置页；断点 sm/md/lg；深色资源 |
| M2 | 2in1 窗口 | 窗口尺寸驱动布局；键鼠 hover/右键/快捷键（API 验证后）；窗口记忆 |
| M3 | 手机初步 | 精简浮条/全屏设置/半模态调色盘；去掉 3+ 指手势；字体最小 12 |
| M4 | 增强 | API 26 `ContainerReader`/`WithEnv`/`HdsColorPicker`/`systemMaterial`（默认关）；折叠悬停；easyGo 可选 |

---

## 12. 验证清单（必须在真机/模拟器上做，当前未验证）

> 本报告**没有**运行任何 HarmonyOS 代码。以下每一项都应在 API 24 与 API 26 设备上各做一次，并把结果写入 `parity/api-matrix.md`。

1. `SymbolGlyph` 在 API 24 是否可用；目标 `sys.symbol.*` 名称是否存在；`SINGLE/MULTIPLE/HIERARCHICAL` 枚举实际值。
2. `ContainerReader` 的构造签名、断点配置类型、是否按容器宽度触发；在 API 24 引用是否编译失败/运行崩溃。
3. `WithEnv`/`@CustomEnv` 的键/读取方式；与 `@StorageProp` 的差异。
4. `onSizeChange` 的 `newValue` 字段名、单位（vp）、触发频率；旋转/分屏/窗口拖动是否都触发。
5. `deviceInfo.deviceType` 在 2in1 上的实际字符串（`'2in1'`? `'pc'`?）；`deviceInfo.sdkApiVersion` 是否存在。
6. 窗口创建/置顶/点击穿透/多 UIAbility 的可用性；`闪控窗` 的权限与第三方资格。
7. `HdsMaterial` 在 API 24 的渲染效果、性能、对比度；`systemMaterial` 在 API 26 的默认值。
8. 深色模式跟随/手动切换；`resources/dark` 是否自动生效；`AbilityStage.onConfigurationUpdated(env)` 的 `env` 字段。
9. 系统“减少动效”开关的 API；关闭后所有动画是否归零。
10. 鼠标 hover/右键/快捷键在 2in1 的 ArkUI 绑定方式。
11. 自定义字体加载 API；Segoe 替代方案的授权。
12. 浮条 44 热区在平板/手机上的误触率；11.11 fp 标签在手机上的可读性。
13. 设置页 440 作为弹窗在 8 栅格设备上的实际限制（是否被系统压缩/告警）。
14. `backdropBlur` 在低端平板上的帧率；是否与墨迹渲染争 GPU。
15. 折叠屏展开/折叠/悬停的窗口尺寸与断点变化；1.2 倍字号规则。

---

## 13. 证据索引（关键结论 → 文件/行）

| 结论 | 证据 |
|---|---|
| 断点 xs/sm/md/lg/xl 阈值 | `design/references/layout.md:22-30` |
| 断点/栅格按窗口而非设备 | `design/references/design-changes.md:226` |
| 12 栅格、gutter、安全边距、间距阶梯 | `design/references/layout.md:5-17,44-54` |
| 自适应 7 能力 | `dev/references/multi-device.md:25-37` |
| `@StorageProp('currentBreakpoint')` 模式 | `dev/references/multi-device.md:49-58` |
| 资源限定符 base/zh-CN/sm/lg/dark | `dev/references/multi-device.md:88-97` |
| `deviceInfo.deviceType` | `dev/references/multi-device.md:68-75`；`system/references/device-info.md:8-13` |
| `fontId` API 14+ | `dev/references/multi-device.md:77-86`；`dev/references/other.md:172` |
| API 26 `ContainerReader` | `SKILL.md:516,808`；`dev/references/ui-development.md:50` 附近 |
| API 26 `WithEnv`/`@CustomEnv` | `SKILL.md:399`；`dev/references/ui-development.md:50` |
| API 26 `systemMaterial` 全组件 | `SKILL.md:508-518`；`dev/references/ui-development.md` Release 段 |
| API 23 沉浸光感/HDS 材质 | `design/references/animation.md:99-128` |
| `HdsMaterial`/`HdsTabs`/`HdsSidebar`/`HdsActionBar` | `dev/references/ui-design-kit.md:9-312`；`dev/SKILL.md:154-166` |
| `HdsColorPicker` API 26 | `dev/references/ui-design-kit.md:314-342` |
| `HdsTabs` 两种不一致签名 | `templates/references/arkui-components.md:125-150` vs `dev/references/ui-design-kit.md:56-109` |
| `easyGo` 平行视界 API 23 | `dev/references/multi-device.md:145-171` |
| 手机/平板/折叠/电脑设计要点 | `design/references/multi-device.md`、`multi-device-design.md` |
| 折叠 3/4、1.2 倍 | `design/references/layout.md:64-71`；`multi-device.md:39-45` |
| 多窗口/横竖屏/挖孔/键鼠 | `design/references/ux-best-practices.md:27-58` |
| 导航条避让/沉浸 2 秒 | `design/references/hmi-system-features.md:72-77` |
| 弹窗 400vp/1.5 倍 | `design/references/ux-best-practices.md:50-54` |
| 触摸 44×44 | `design/references/multi-device.md:33`；`icon.md:65-71` |
| 颜色值/Token 分层/深色建议 | `design/references/color.md` |
| 字体 HarmonyOS Sans/字号层级/fp | `design/references/typography.md`；`ux-best-practices.md:145` |
| 动效时长/曲线/转场 | `design/references/animation.md` |
| 控件分类/系统优先 | `design/references/controls.md` |
| Symbol 1500+/分层/动效/SymbolGlyph | `design/references/icon.md`；`templates/references/design-resources.md` |
| `SymbolGlyph` 代码与策略 | `design/references/icon.md:30-52`；`templates/references/design-resources.md:15-35` |
| 设计变更时间线 | `design/references/design-changes.md` |
| 本地化 Kit/无权限 | `dev/references/localization-kit.md` |
| 窗口/系统栏 API | `system/references/basic-services.md:31-100` |
| `onSizeChange` API 12+ | `dev/references/ui-development.md:307-315` |
| `backdropBlur` API 14+ | `dev/references/ui-development.md:320-326` |
| Canvas/XComponent/DisplaySync | `samples/references/graphics.md`；`media/references/graphics2d.md` |
| Input Kit 鼠标/键盘能力 | `system/references/input-kit.md`；`input-multimodal-kit.md` |
| 已记录 SysCap 仅 3 个 | `media/references/ar-engine.md:82`；`media/references/arkgraphics-3d.md:187`；`system/references/enterprise-foundation-kit.md:27` |
| 项目 token 实际值 | `src/InkCanvas.Controls/Themes/Tokens.xaml`；`Theming/PopupBrushes.cs`；`05-controls.md` §2–§3 |
| 项目动效值 | `src/InkCanvas.Controls/Animation/Motion.cs` |
| 项目图标码点 | `src/InkCanvas.Controls/Controls/Glyphs.cs` |

---

## 14. 开放问题（需要官方 API 参考或实机）

1. `ContainerReader` 的 API 形态：是组件、构建器还是 `@Builder`？断点如何配置？是否只按容器宽度？
2. `WithEnv`/`@CustomEnv` 的语法与运行时语义；能否替代 `LocalStorage` 做全局断点/主题/设备环境？
3. 知识库没有 `mediaquery`；官方 `@ohos.mediaquery` 是否仍是 API 24 的推荐回退？`matchMediaSync` 的查询字符串语法与监听签名是什么？
4. `deviceInfo.sdkApiVersion` 是否存在；2in1 的 `deviceType` 字符串是什么；`systemParameter` 的 `const.ohos.devicetype` 返回值是否稳定。
5. 第三方应用能否创建独立浮条窗口、置顶窗口、点击穿透窗口？`闪控窗` 的权限/申请条件/是否只对系统应用开放？
6. `SymbolGlyph` 的起始 API 版本；`sys.symbol.*` 完整目录；`SINGLE/MULTIPLE/HIERARCHICAL` 的准确枚举。
7. 自定义字体加载 API 是否存在；若存在，能否打包自有图标字体；Segoe 替代方案的授权。
8. 读取系统颜色模式/字体缩放/减少动效的 API；`AbilityStage.onConfigurationUpdated(env)` 的 `env` 字段。
9. ArkUI 的 hover/右键/键盘快捷键/鼠标光标 API；Input Kit 的 `pointer`/`keyEvent` 具体签名。
10. 官方圆角参数/间隔参数的数值阶梯（知识库只收录了文档名）；是否需要把项目 8/14/6.94 映射到官方阶梯。
11. `HdsTabs` 的正确签名（`controller+barFloatingStyle` vs `tabs[]+tabStyle`）；`HdsMaterial` 的导入路径（`@kit.UIDesignKit` vs `@hms.hds.hdsMaterial`）。
12. API 24 设备上 `HdsMaterial`/HdsTabs 的实际可用性（API 23 引入，但设备可能裁剪）。
13. 设置面板 440 在平板/2in1 上作为弹窗是否被系统限制；是否必须改为侧面板/独立窗口。
14. 40 vs 44 触摸目标的最终决策；11.11 fp 标签是否必须提升到 12 fp。
15. 折叠屏悬停/姿态 API；`easyGo` 是否适合画板类应用。
16. 26.0.0 Release 工具链是否可用于上架（当前本机是 26.0.1 Beta，需换 Release）。

---

## 15. 给下游实现者的最短行动清单

1. **先建 `parity/tokens.json`**（颜色/字号/间距/圆角/尺寸/动效/触摸热区），把 `Tokens.xaml`、`PopupBrushes`、`Motion`、`FloatingBar` 常量收进去；修掉 accent `#0067C0` vs `#0066BF` 的漂移。
2. **写 `Breakpoints.ets`**：窗口/容器宽度 → sm/md/lg/xl；API 24 用 `onSizeChange`，API 26 再评估 `ContainerReader`；业务只读枚举。
3. **写 `DevicesCompat.ets`**：`deviceType`、`apiLevel`（字段待核实）、能力布尔；禁止业务层版本判断。
4. **浮条/调色盘/图形/计时环自绘**，保留项目 S 换算度量；设置页用系统控件。
5. **颜色保留 Fluent accent**；系统控件蓝作为已知差异记录；深色用 `resources/dark` + 项目 token。
6. **图标先做 20–30 个 Symbol 映射 + 自绘 fallback**；不打包 Segoe；每个图标实机截图验证。
7. **浮层架构先按“应用内覆盖”做**；独立置顶窗口/闪控窗作为 M4 研究项，不作为 M1 依赖。
8. **所有“未验证”写进 `parity/api-matrix.md` 与移植说明**，不要在代码注释里写“已支持”。

---

*本报告只覆盖多设备/设计语言/图标/深色/API 24-26 设计差异；未覆盖笔输入、墨迹渲染算法、PPT 互操作、覆盖层可行性等专题，见 01–10 号报告。*
