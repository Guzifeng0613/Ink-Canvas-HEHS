# 移植工程 — 验证工具链笔记

日期：2026-11-18（会话重启后确认）

## 已核实事实

| 编号 | 结论 | 证据 |
|---|---|---|
| H1 | Node v24.21.0 默认类型剥离**不支持 enum**（SyntaxError） | `node t.ts` 失败于 `enum Kind` |
| H2 | `node --experimental-transform-types t.ts` 可完整执行 interface/enum/class/泛型/浮点运算 | 输出 `{"n":2,"k":0,"t":0.6000000000000001,...}` |
| H3 | 因此共享黄金向量运行器**零依赖**：`node --experimental-transform-types parity/run-vectors.mjs`（被测逻辑用 .ts 导入，或 runner 直接 import .ts） | H2 |
| H4 | npm registry 可达（typescript 返回 200），但非必需 | curl |
| H5 | 本机暂无 DevEco / hvigor / ohpm / HarmonyOS SDK；用户自行下载商业版 Command Line Tools（内嵌 SDK），约定放置 `D:\dev-cache\harmony\` | 目录检查 + 用户消息 |
| H6 | 华为云镜像 `repo.huaweicloud.com/openharmony/os/7.0-Release/` 有 OpenHarmony 7.0 公共 SDK（API 26 底座），可作为兜底编译验证；`harmonyos/` 目录仅 OH 工具，无商业 CLI | 镜像探查 |
| H7 | 实际工具链：`E:\EcoStudioCLI\command-line-tools` 26.0.1.290（**beta**），SDK 26.0.1 Beta1（apiVersion 26，`sdk/default/{hms,openharmony}`）；ohpm 26.0.1.220 / codelinter 6.0.240 / hvigorw 6.26.21，自带 Node 24.14.1 | `version.txt` + 版本命令 |
| H8 | `compileSdkVersion: "26.0.0"` 会被工具链拒绝（00303313：须与内置 SDK 26.0.1 一致）；**省略该字段**即用内置 SDK，`compatibleSdkVersion: "6.1.1(24)"` 与 `targetSdkVersion: "26.0.0"` 均接受 | 探针构建错误 → 修正后成功 |
| H9 | 探针工程 `D:\tmp\harmony-probe`（phone/tablet/2in1，Stage 模型）`hvigorw assembleHap` **BUILD SUCCESSFUL**（34 tasks，26s，仅未签名告警） | 构建日志 |
| H10 | E 盘缓存生效：`HVIGOR_USER_HOME=E:\harmony-cache\hvigor`（pnpm 包装器/全局缓存随行），`hvigor.cacheDir` 指向 E 盘 | 构建日志 "Installing pnpm" 落盘 |
| H11 | `HVIGOR_USER_HOME` 是 hvigor 官方支持的环境变量（绝对路径、自动创建）；ohpm 无同等 home 环境变量，包缓存仍在 `~/.ohpm` | hvigorw.js 源码 |

## 待用户放置后执行的验证

```powershell
pwsh -File tools/setup-harmony-toolchain.ps1   # 自动找 zip、解压、列布局、验证 codelinter/ohpm/hvigorw 版本
```

安装记录写入 `tools/toolchain.local.json`（需加入 .gitignore）。商业 SDK 就位后：
`hvigorw assembleHap`（debug）→ 记入验证声明；无设备时明确「未实机运行」。
