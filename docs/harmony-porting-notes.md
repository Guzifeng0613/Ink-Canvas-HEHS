# HarmonyOS 移植说明（进行中）

更新时间：2026-11-18（Phase A 执行中）
设计规格：`docs/superpowers/specs/2026-11-18-harmonyos-port-design.md`
实施计划：`docs/superpowers/plans/2026-11-18-harmonyos-phase-a-foundation.md`

## 验证状态（随任务推进更新）

| 层 | 手段 | 当前结果 |
|---|---|---|
| L1 逻辑互认 | C# 生成黄金向量 + Node 运行 ArkTS | **Phase A：339 用例 / 28 个向量文件全部通过** |
| L2 编译 | `hvigorw assembleHap`（26.0.1 Beta1 SDK） | 通过（未签名 debug HAP，73 tasks） |
| L3 运行/UI | 真机 | **未实机验证**（本机无设备；模拟器不可靠） |
| L4 基线回归 | `dotnet test`（向量子集） | 28/28 通过；完整 WinUI3 套件未在本轮重跑 |

### Phase A 已交付（纯逻辑核心）

`harmony/core`：墨迹模型/颜色/属性、因果笔锋（Tail/Velocity）、胶囊并集轮廓、线条擦、
撤销栈、矩阵（行向量）、选区几何、双指手势、枢轴、25 种图形、形状识别、触摸分档、手掌判定、
回放时钟/会话、预乘 BGRA 合成、主题判定、触摸设置模型与 Settings 编解码、.icink/画板包/PPT manifest 编解码
（含 ObjectFrame 与画板 HighlighterAlpha 保留、99 页上限）、InkDocument 命中/面积擦/边界、InkStroke bounds 膨胀与 setter 重算、
版本/Release 解析/更新静默窗口策略。

- 功能单元：manifest 中 14 个 Phase A 单元状态 `ported`；`system-autostart` 唯一 `not-ported`。
- 锚点：29 个 ArkTS→C# 锚点（C# 侧反向锚点待用户工作树清理后补齐）。
- 覆盖率（Phase A 范围）：14/14。
- 独立评审修复轮：1 Critical（ObjectFrame/画板 HighlighterAlpha）+ 7 Important（bounds/setter、面积擦与文档命中、99 页上限、manifest/schema 收口、checker 假绿）已修复并回归。

### 模拟器环境（2026-11-18）

- 镜像与实例已就绪：API 26 平板镜像下载成功；实例 `InkPad26`（E:\harmony-cache\emu-instances，MatePad Pro 13）。
- **启动失败**：`ErrorCode 00801001 未开启 Hyper-V`。宿主 `systeminfo` 显示 hypervisor 存在但 Windows 可选功能不可用（`Get-WindowsOptionalFeature: 没有注册类`），本机无法启用 Hyper-V/嵌套虚拟化。
- 结论：本机**无法进行模拟器运行验证**；L1 向量与 L2 构建继续为准，模拟器/真机验证待具备虚拟化的宿主执行。
- HAP 安装另需签名（OpenHarmony 调试证书材料已在 SDK `toolchains/lib`，待模拟器可用时配置）。

### 仍需后续阶段（B–G）

- 已完成：platform compat/输入归一化/双层 Canvas 渲染、controls token/浮条/调色盘/图形菜单、entry 主页面壳。
- 进行中：InkController 状态机（B2/B3）。
- 待完成：画板/课件/计时/抽奖/回放、设置/指南、入口模式机与多形态、manifest 其余单元、api-matrix 全量、模拟器/真机验证。

## 与 WinUI3 基线的已知差异（设计决定）

- 跨应用全屏批注不可行（`TYPE_FLOAT` 需 system_basic）→ 应用内批注 + 课件/图片/截图导入。
- PPT/WPS COM 不可行 → PDF 课件模式（逐页渲染 + 自绘墨迹）。
- 全局热键 → 应用内快捷键；静默截图 → 应用内快照；自更新 → 应用市场提示（Deferred）。
- 开机自启无三方 API → 唯一 not-ported。
- Segoe 图标不可分发 → Symbol 映射/自绘（Phase D 完成映射表）。

## 复刻的基线缺陷（互认优先，后续双端同步修复）

- 画板 Stroke 子格式缺 `HighlighterAlpha`（保存/加载后荧光笔透明度回落）。
- 仅视频无图片的页保存会丢媒体（§6 报告）。
- 窗口位置完全不落盘（WinUI3 基线即无；HarmonyOS 首版不新增，避免格式分叉）。
- PPT 轮询契约取 ShellController 实际值 200/900ms。
- 选区增粗/恢复笔宽只改 BaseWidth、不重算逐点 Width（无视觉反馈）。
- 三角识别被 InkController 忽略。

## 需真机验证清单

压感/倾斜/接触面积取值与单位；笔尾橡皮；悬停；`TouchType.Cancel` 稳定性；
Canvas `fillRule` 非零环绕与自交并集；双层 Canvas 提交闪烁；`StylusFrameBoost` 延迟与功耗；
PDF `renderPage` 分辨率/耗时/内存；AVPlayer 首帧；通知/代理提醒 APL；AppGallery 更新 API；
SymbolGlyph 图标名与起始版本；自由窗口/分屏断点；2in1 鼠标/键盘/焦点；深色与减少动效读取。

## 工具链

- Command Line Tools：`E:\EcoStudioCLI\command-line-tools`（26.0.1.290 beta，SDK 26.0.1 Beta1）。
- 构建缓存：`E:\harmony-cache`；会话环境：`tools/harmony-env.ps1`。
- 提交配置：省略 `compileSdkVersion`；`compatibleSdkVersion 6.1.1(24)`；`targetSdkVersion 26.0.0`；`modelVersion 26.0.0`（两处一致）。
- **上架限制**：Beta 工具链产物不可上架；发布前须换 26.0.0 Release 工具链重编译。
