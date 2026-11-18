# HarmonyOS 移植 — Phase B–G 路线与任务计划

日期：2026-11-18
前置：Phase A 完成（`docs/superpowers/plans/2026-11-18-harmonyos-phase-a-foundation.md`，339 向量 / 28 C# 测试 / HAP 构建通过）
设计规格：`docs/superpowers/specs/2026-11-18-harmonyos-port-design.md`

## 全局约束（沿用 Phase A，新增两条）

- 沿用 Phase A 的全部 Global Constraints（ArkTS 严格子集、JSON 字段/枚举、禁止臆造 syscap、只提交本阶段文件）。
- **悬浮窗逻辑统一**：浮动工具栏/面板在**平板与 PC/2in1 使用同一实现**（主窗内绝对定位浮层），不做设备分叉；断点只影响尺寸与布局，不改变浮窗机制。
- **验证以模拟器为准**：镜像 API 26（7.0.0）与 API 24（6.1.1）均可用；真机后续安排。模拟器不可用的能力（笔压感/接触面积/低延迟）继续标注「需真机」。
- 每个阶段结束必须：`hvigorw assembleHap` 通过 + 向量全过 + `parity-check --final --phase <X>` 通过；可用模拟器时附截图/运行记录。

## Phase B — ink 运行时（逻辑与接口）

| # | 任务 | 文件/接口 | 验收 |
|---|---|---|---|
| B1 | 场景构建与渲染接口 | `harmony/ink/src/main/ets/Renderer.ts`（`InkRenderer`、`InkScene`、`DrawCommand`、`Invalidation`）；`SceneBuilder.ts` 用 core `buildUnionContours`/`effectiveColor` 生成填充命令；`InkDocument` 增加 `Version` | 向量：场景命令（轮廓数/颜色/层）；构建通过 |
| B2 | InkController 状态机 | `InkController.ts`：工具/模式、指针 down/move/up/cancel 路由（笔/触摸/鼠标/手掌委托）、活动笔画提交（原子）、整笔擦/面积擦、图形工具、选区点选/拖拽/克隆/删除、视图变换、多指语义 | C# 驱动的控制器序列向量；构建 |
| B3 | 选区与手势接线 | 双指平移/缩放/旋转（枢轴、累计钳制、选区路径单帧 delta）、拖拽共轭、撤销单元语义 | 向量：矩阵/撤销序列；构建 |
| B4 | 回放驱动与放映翻页 | `ReplayDriver.ts`：按 `ReplayClock`/`InkReplaySession` 驱动可见前缀；`>60px` 手指翻页判定 | 向量：时间线/翻页边界 |
| B5 | 阶段收口 | manifest `ink-gestures`/`ink-slide-flip` 等状态；porting notes | 检查器/构建/向量 |

## Phase C — platform 适配与渲染实现

| # | 任务 | 说明 |
|---|---|---|
| C1 | compat 能力层 | `sdkApiVersion`/`canIUse`（仅已核实字符串）、API 26 专属能力封装与 API 24 回退 |
| C2 | ArkUI 输入适配 | `onTouch/onHover/onMouse/onKeyEvent` → 归一化事件（ns→ms、pressure 0→0.5、source/sourceTool、接触宽高、历史点、鼠标过滤） |
| C3 | Canvas 渲染器 | 双层 Canvas（已提交/活动）、Path2D 胶囊多边形一次 `fill`（nonzero 优先，降级登记）、选区框/句柄、脏区失效、快照（componentSnapshot） |
| C4 | 窗口/设备/存储/日志 | 主窗 + 断点（onSizeChange）、沉浸式、沙箱文件（Settings/.icink/画板包/快照）、FileLog |
| C5 | 浮窗宿主（统一逻辑） | 主窗内 `Stack` 绝对定位浮层承载浮条/面板；平板与 2in1 同一实现；API 26 可选 OverlayManager 仅作内部优化 |

## Phase D — controls 与界面组件

| # | 任务 | 说明 |
|---|---|---|
| D1 | token/主题/图标 | `parity/tokens.json` → `ThemeTokens.ts`；`parity/glyphs.json` 填充 Symbol/自绘映射；深浅色 |
| D2 | 浮动栏 | 19 主栏按钮顺序/glyph/尺寸/选中 wash、四卡布局、折叠把手、拖动阈值、更多/手势面板；**平板与 2in1 一致** |
| D3 | 调色盘/图形菜单/计时环/按钮/动效 | 色序/笔锋/粗细/透明度；24 图形预览与交互；≥40 视觉/44 热区；Motion 140/240/380ms |
| D4 | 设置/指南/是否窗 | 侧栏/全屏页（内容 440vp）；12 行键帽指南；450×160 是/否 |
| D5 | 模拟器视觉核对 | 截图与 WinUI 对照 |

## Phase E — features 功能层

| # | 任务 | 说明 |
|---|---|---|
| E1 | 画板 | 99 页、三浮条、选区面板、纸型/网格、图片/视频对象、撤销、保存/加载（复用 Phase A 编解码） |
| E2 | 课件（PDF） | PDF Kit `renderPage → PixelMap` + 自绘墨迹层、页码导航、缩放；PPT 需先导出 PDF（README 明示） |
| E3 | 计时/抽奖/回放 | CountdownWindow 语义（20/30/50/100ms）、铃声、紧凑/全屏；抽奖名单/滚动/单次；回放窗口 |
| E4 | 持久化/自动保存/快照 | 沙箱路径迁移（Windows 绝对路径→沙箱）、原子写、过期清理、应用内截图 |
| E5 | 模拟器功能核对 | 逐功能 smoke |

## Phase F — entry 外壳与整合

| # | 任务 | 说明 |
|---|---|---|
| F1 | UIAbility/Shell | 组合根、五态模式机（标注/鼠标/画板/课件/收纳）、单例服务、设置重应用 |
| F2 | 主页面与浮层 | 墨迹画布 + 浮条 + 各面板/弹窗；模式切换；快捷键（应用内）；鼠标模式 |
| F3 | 多形态布局 | 断点 xs/sm/md/lg/xl；平板/2in1 完整，手机初步；**同一浮窗实现** |
| F4 | 模拟器安装运行 | hdc 安装/启动/截图；API 26 与 API 24 各一轮 |

## Phase G — 覆盖收口

| # | 任务 | 说明 |
|---|---|---|
| G1 | manifest 56 项 | 逐项 status（ported/adapted/not-ported）+ notes；覆盖率 ≥98% |
| G2 | api-matrix / porting notes / README | API 24/26 行为表、验证边界、HarmonyOS 使用说明（含 PPT→PDF 流程） |
| G3 | 最终验证 | codelinter、全量向量、构建、模拟器两版本、独立评审一轮 |

## 模拟器环境（已确认）

- `Emulator.bat` 镜像：tablet/2in1 均有 **HarmonyOS 7.0.0(26.0.0)** 与 **6.1.1(24)**（Release）。
- `hdc.exe`：`E:\EcoStudioCLI\command-line-tools\sdk\default\openharmony\toolchains\hdc.exe`。
- 屏幕档案：MatePad Pro 13 / 12 / 11、MatePad Air 12、MatePad 11 S。
- 待办：签名（模拟器安装 HAP 需要签名配置）；镜像下载完成后创建实例。

## 环境限制（2026-11-18 实测）

- 本机模拟器启动失败：`00801001 未开启 Hyper-V`；宿主无可用的 Windows Hyper-V 功能（疑似虚拟化环境禁用嵌套虚拟化）。
- 因此 B–G 的验证以 **构建 + 黄金向量 + checker** 为准；模拟器/真机验证需在支持虚拟化的宿主或真机执行。镜像、实例与 `hdc` 已就绪，换宿主可直接复跑。

## 风险与验证边界

- 模拟器不提供真实笔压感/接触面积/低延迟，相关行为仍标记「需真机」。
- 跨应用批注/PPT COM/全局热键不可行（见规格 §3）。
- Beta 工具链产物不可上架；发布前换 26.0.0 Release 工具链。
