# InkCanvas — 项目指引

Ink Canvas Artistry 的 WinUI 3 迁移版（GPL-3.0），并进行 HarmonyOS（ArkTS）移植。
上游：InkCanvas/Ink-Canvas-Artistry ← WXRIW/Ink-Canvas。课堂屏幕画笔 / 白板 / 课件批注应用。

## 两套实现，一套逻辑

| 平台 | 位置 | 分层 |
|---|---|---|
| WinUI 3（Windows） | `src/` | Core / Platform / Ink / Controls / Features / App，依赖单向 App→Features→Controls→Ink→Platform→Core |
| HarmonyOS（ArkTS） | `harmony/` | entry + core / platform / ink / controls / features HAR，同构分层、同向依赖 |

**跨架构维护契约（改任一侧都要遵守）**：
- `parity/manifest.json`：功能 ↔ 两侧文件的双向映射权威清单；增删功能文件时同步更新，`node tools/parity-check.mjs` 校验锚点与映射完整性。
- 成对源码用 `@parity` 锚点互指：C# 写 `// @parity harmony/core/src/main/ets/...`；ArkTS 写 `// @parity src/InkCanvas.Core/...`。
- 纯逻辑用 `parity/vectors/*.json` 黄金向量钉住：C# 测试生成/消费，ArkTS 逻辑测试消费同一文件（`node parity/run-vectors.mjs`）。算法改动必须两侧同步并双向跑向量。
- 共享数据格式：`.icink`（JSON 墨迹存档）与 `Settings.json` 键名两侧完全一致；schema 在 `parity/schemas/`。
- HarmonyOS API 差异一律走 `harmony/platform` 的 compat 能力层 + `parity/api-matrix.md`（API 24 vs 26 行为表）；业务代码禁止散落版本判断。
- UI 一致性：设计 token 由 `parity/tokens.json` 单一来源驱动两侧主题；布局度量（触摸目标 ≥40、浮条 55、设置面板 440 等）两侧同值（DIP↔vp）。

## HarmonyOS 知识库

`/harmony-os` skill（`.claude/skills/harmony-os/`，vendored 上游知识库，见其 VENDOR.md）。
基线：**API 26.0.0 Release（HarmonyOS 7 / OpenHarmony 7.0）为主，API 24（HarmonyOS 6.1.1）向下兼容**。
写 ArkTS 前先查对应 references：笔→`dev/references/pen-kit.md`，渲染→`media/references/graphics2d.md` 与 `dev/references/ui-development.md`，窗口→`dev/references/app-framework.md`，多设备→`dev/references/multi-device.md`，工程配置→`templates/references/deveco-templates.md`。

## 构建与验证

- Windows：`.\build.ps1 build|test|run`（dotnet 10 SDK）。**构建前先结束运行中的 InkCanvas.App 进程**（文件锁会导致构建失败）。
- HarmonyOS 纯逻辑测试：`node --experimental-transform-types --experimental-loader ./parity/ts-resolve-loader.mjs parity/run-vectors.mjs`（逻辑层写成兼容 TS 的 ArkTS 严格子集，无需 SDK 即可验证；loader 负责把 ArkTS 风格的无扩展名导入解析为 .ts）。
- HAP 构建需 DevEco Studio 26.0.0（compatibleSdkVersion 6.1.1(24)，targetSdkVersion 26.0.0；**不要写 compileSdkVersion**，否则报 00303313；`modelVersion` 在 `hvigor/hvigor-config.json5` 与根 `oh-package.json5` 中必须一致，统一 26.0.0）。命令行工具（内嵌 SDK）位于 `E:\EcoStudioCLI\command-line-tools`；**构建缓存在 `E:\harmony-cache`**（hvigor/ohpm/npm，会话环境用 `tools/harmony-env.ps1`）。构建命令：`. tools/harmony-env.ps1; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon`（未签名 HAP，编译验证够用）。本机 SDK 为 26.0.1 Beta1（唯一可得），生产上架须换 26.0.0 Release 工具链——如实记录。
- 运行时验证 harness（SendInput / Z-order / 截图）在 `D:\tmp\inkrepro`；PowerShell 5.1 注意 ANSI 转义输出陷阱。

## 诚实记录验证边界

沿用 WinUI3 轮惯例：未运行的测试、未启动的应用、未验证的硬件，明确标注「未验证」，不声称通过。
验证声明写入 docs/ 对应 porting notes 与 CHANGELOG；设计文档在 `docs/superpowers/specs/`。