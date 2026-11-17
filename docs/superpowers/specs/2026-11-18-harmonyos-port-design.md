# Ink Canvas → HarmonyOS（ArkTS）移植设计

日期：2026-11-18
状态：草案 v0.1（研究回填中；架构/契约/版本策略已定稿）
上游基线：`feat/winui3-migration` 工作树（3.4.0 轮）
知识库：`.claude/skills/harmony-os/`（vendored，上游 commit `4758f3c`）

## 1. 目标与验收口径

把 WinUI3 版 Ink Canvas 移植为 HarmonyOS ArkTS 版，**覆盖 WinUI3 基线功能清单的 98%**，
并让两套实现在源码层面互认、可长期并行维护；界面保持一致。

| # | 用户要求 | 验收口径 |
|---|---|---|
| G1 | 配置 Harmony OS SKILL | `.claude/skills/harmony-os/` 已 vendored 并注册（`/harmony-os`），含版本基线与更新流程 |
| G2 | 迁移重构，覆盖 98% 功能 | `parity/manifest.json` 功能矩阵：每项标注 已移植/适配移植/明确不移植+原因；不移植项 ≤2%，逐项列出 |
| G3 | 重点：桌面触摸、笔、平板；手机初步 | 平板与 PC/2in1 为完整功能面；手机为初步（核心书写/画板/计时/设置），差异写入 `parity/api-matrix.md` 与移植说明 |
| G4 | WinUI3 ↔ ArkTS 逻辑互认与连接 | 四机制：双向 `@parity` 锚点 + manifest 校验、共享黄金向量、共享数据格式、统一设计 token |
| G5 | 界面一致 | token 单一来源；布局度量 DIP↔vp 同值；图标语义一致（码点↔Symbol 映射表）；逐窗口对照 |
| G6 | API 26 主语言 + API 24（6.1.1）兼容 | 单代码库；compileSdk 26.0.0 / compatibleSdk 6.1.1(24) / targetSdk 26.0.0；差异走能力层与 api-matrix |

## 2. 已核实事实

| 编号 | 结论 | 证据 |
|---|---|---|
| F1 | 知识库基线：API 26.0.0 Release（2026-08-29，OpenHarmony 7.0 / HarmonyOS 7.0.0.105）；API 24（6.1.1 Release，2026-05-26）为存量兼容（设备占比 84.93%）；API 26.0.1 Beta1 不可上架，不作为基线 | 根 SKILL.md 版本历史 |
| F2 | 项目无任何现存 ArkTS/.ets 代码，移植为全新实现 | Glob `**/*.ets` 为空 |
| F3 | WinUI3 基线为 6 层单向下依赖（App→Features→Controls→Ink→Platform→Core），Core 无 UI 依赖可单测 | 设计文档 §4 + 目录 |
| F4 | Node 24 默认类型剥离不支持 enum；`--experimental-transform-types` 可执行 interface/enum/class/泛型 | `docs/superpowers/research/harmony/00-harness-notes.md` |
| F5 | 本机暂无 HarmonyOS SDK；商业版 Command Line Tools（内嵌 SDK）由用户下载，约定置于 `D:\dev-cache\harmony\` | 用户消息 + 目录检查 |
| F6 | 兜底可用 OpenHarmony 7.0 公共 SDK（API 26 底座）做编译验证 | 华为云镜像探查 |
| F7 | 实际工具链：`E:\EcoStudioCLI\command-line-tools` 26.0.1.290（beta），SDK 26.0.1 Beta1（apiVersion 26）；探针工程 `hvigorw assembleHap` 构建成功（34 tasks / 26s，未签名） | `D:\tmp\harmony-probe` 构建日志 |
| F8 | `compileSdkVersion` 须省略（自动取内置 SDK；写死 26.0.0 被 00303313 拒绝）；`compatibleSdkVersion "6.1.1(24)"`、`targetSdkVersion "26.0.0"` 可用 | 探针实测 |

## 3. 平台能力边界与移植策略

（研究回填：全屏覆盖层、全局热键、PPT COM、系统截图、开机自启、应用更新等逐项结论与文档依据。）

## 4. 架构：同构分层

```
harmony/                          DevEco 工程（API 26 工具链）
  AppScope/                       app.json5、全局资源
  entry/                          应用头：UIAbility、窗口外壳、模式切换、页面路由
  core/        HAR @inkcanvas/core      纯域模型：墨迹/笔锋/几何/图形/识别/撤销/设置/回放/选区
  platform/    HAR @inkcanvas/platform  系统适配 + 版本兼容能力层（compat）
  ink/         HAR @inkcanvas/ink       渲染管线 + InkController + 输入路由 + 双指手势
  controls/    HAR @inkcanvas/controls  ArkUI 组件（浮动栏/调色盘/图标按钮/计时环）、主题 token、动效
  features/    HAR @inkcanvas/features  画板 / 课件(PPT→PDF) / 计时 / 抽奖 / 回放 / 截图 / 持久化
```

依赖方向与 C# 侧完全同构：`entry → features → controls → ink → platform → core`。
`core` 不 import 任何 `@kit.*`（保持纯逻辑、可 Node 验证）；`platform` 是唯一允许触达系统 Kit 的分层（`ink` 经 `platform` 暴露的渲染接口工作）。

### 模块 ↔ C# 映射（初始）

| ArkTS 模块 | C# 对应 | 说明 |
|---|---|---|
| `core` | `InkCanvas.Core` | 1:1 语义移植，严格 ArkTS∩TS 子集 |
| `platform` | `InkCanvas.Platform` | Win32 能力 → OHOS Kit 适配；新增 compat 层 |
| `ink` | `InkCanvas.Ink` | Win2D → ArkGraphics2D/Canvas；手势数学 1:1 |
| `controls` | `InkCanvas.Controls` | WinUI 控件 → ArkUI 组件；token 同源 |
| `features` | `InkCanvas.Features` | 画板/课件/计时/抽奖/回放/持久化 |
| `entry` | `InkCanvas.App` | 窗口外壳、模式状态机、依赖装配 |

## 5. 跨架构互认契约（G4 的实现）

1. **锚点互指**：成对文件头部与关键算法处写 `@parity` 注释，C# 指向 ArkTS 路径、ArkTS 指向 C# 路径；
   `tools/parity-check.mjs` 校验：锚点目标存在、双向成对、manifest 覆盖全部锚点文件。
2. **权威功能清单** `parity/manifest.json`：
   ```json
   { "features": [ { "id": "ink-taper-causal", "name": "笔锋因果增量计算",
       "status": "ported|adapted|not-ported", "csharp": ["src/InkCanvas.Core/Ink/TaperProfile.cs"],
       "arkts": ["harmony/core/src/main/ets/ink/TaperProfile.ets"],
       "vectors": ["parity/vectors/taper.json"], "notes": "…" } ] }
   ```
   `status` 三态 + `notes` 必填（不移植项说明原因）；覆盖率 = ported+adapted 项 / 总项，目标 ≥98%。
3. **共享黄金向量**（可执行互认）：`parity/vectors/*.json` 由 C# 生成器（`tools/VectorGen`）产出，
   两侧测试共同消费；`node --experimental-transform-types parity/run-vectors.mjs` 零依赖执行。
   向量覆盖：笔锋两型全序列、24 图形几何、识别判定集、选区变换、撤销、设置往返、回放时钟、触摸分档、像素合成。
4. **共享数据格式**：`.icink` 与 `Settings.json` 的 JSON Schema 在 `parity/schemas/`，两侧实现同字段同语义；
   任何一侧改格式必须先改 schema 与向量。
5. **UI 单一 token 源**：`parity/tokens.json`（颜色/字号/间距/圆角/尺寸/动画时长曲线），
   C# `Tokens.xaml` 与 ArkTS `ThemeTokens.ets` 均由它校验（`tools/parity-check.mjs` 对比数值）；
   图标码点表 `parity/glyphs.json` 映射 Segoe Fluent → HarmonyOS Symbol（缺失项自绘/Bundled 字体）。

## 6. API 版本策略（G6）

- 单代码库、单 HAP：**省略 `compileSdkVersion`**（自动使用工具链内置 SDK；Release 工具链→26.0.0，Beta 工具链→26.0.1，均为 API 26）、`compatibleSdkVersion 6.1.1(24)`、`targetSdkVersion 26.0.0`（SemVer 格式自 API 26 起）。
- `platform/compat/` 能力层：`Capabilities` 接口 + `useApi26(): boolean`（基于 `deviceInfo.sdkApiVersion` / `canIUse('SystemCapability...')`）；
  API 26 专属（沉浸光感 systemMaterial、Pen Kit 跟手性加速、ContainerReader、OverlayManager 层级、闪控窗等）全部包在接口后，API 24 回退实现。
- 业务层（core/ink/controls/features/entry）**禁止**直接判断版本号或直接 `import` 26 专属 Kit；由 `platform` 暴露统一能力。
- 差异登记：`parity/api-matrix.md` 逐能力列 API 24 行为 / API 26 行为 / 门控方式 / 维护注意事项。
- 不使用 26.0.1 Beta API（不可上架），后续升级按矩阵扩展。

## 7. 设备形态策略（G3）

| 形态 | 定位 | 关键差异 |
|---|---|---|
| 平板 (tablet) | 完整功能 | 横屏优先；浮动栏+三栏画板；双指手势；笔+触摸分流 |
| PC/2in1 桌面触摸 | 完整功能 | 自由窗口/子窗承载浮动栏与面板；鼠标独立指针模式；键盘快捷键；窗口记忆 |
| 手机 (phone) | 初步适配 | 单列响应式；核心书写/画板/计时/抽奖/设置；浮动栏简化；不做全覆盖批注 |

`module.json5` deviceTypes 声明 `tablet`/`2in1`/`phone`；布局按断点（sm/md/lg/xl）响应式；
能力降级以**能力探测**为准，不以设备类型硬编码。

## 8. 笔与渲染方案

（研究回填：笔事件源/压感/倾斜/悬停、Pen Kit 跟手性、手掌判定移植、渲染选型与离屏缓存。）

## 9. 功能覆盖矩阵

（研究回填：以 WinUI3 基线清单为分母，逐项 status 与移植要点。）

## 10. 验证策略（分层，诚实声明）

| 层 | 手段 | 本机可执行性 |
|---|---|---|
| L1 逻辑互认 | 黄金向量双向跑（C# xunit + `node --experimental-transform-types`） | 可执行 |
| L2 编译与静态检查 | `hvigorw assembleHap`（debug）+ `codelinter` | 工具链就位后可执行；否则记录「未编译」 |
| L3 运行与 UI | DevEco 预览器/模拟器/真机 | 本机不可用 → 记录「未实机验证」，给出核对步骤 |
| L4 基线回归 | WinUI3 `.\build.ps1 test` 保持通过 | 可执行 |

验证声明统一写入 `docs/harmony-porting-notes.md` 与 CHANGELOG，未验证项显式标注。

## 11. 风险与取舍（初始）

（研究回填：覆盖层不可行度、COM 缺失、商业 Kit 在 OH 兜底构建中的隔离、ArkTS 严格性风险、98% 分母口径。）

---

### 决策记录（自主决策授权下）

- 2026-11-18：技能采用 vendoring（去掉上游 .git，随仓库提交），仅本地改 `name: harmony-os`，见 VENDOR.md。
- 2026-11-18：向量运行器零依赖（Node transform-types），不引入 npm 依赖。
- 2026-11-18：单代码库 + 能力层，不做 API 24/26 双分支代码库；差异集中在 `platform/compat` 与 `api-matrix.md`。
- 2026-11-18：当前分支继续（不新建分支），提交仅包含移植新增文件，不触碰用户未提交的 3.4.0 改动。
- 2026-11-18：工具链实际位于 `E:\EcoStudioCLI\command-line-tools`（商业版，内嵌 SDK，`sdk/default` 含 hms+openharmony 双栈）；构建缓存统一 `E:\harmony-cache\{hvigor,ohpm,npm}`，会话环境由 `tools/harmony-env.ps1` 提供。
