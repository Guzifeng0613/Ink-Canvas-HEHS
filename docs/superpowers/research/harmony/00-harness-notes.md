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

## 待用户放置后执行的验证

```powershell
pwsh -File tools/setup-harmony-toolchain.ps1   # 自动找 zip、解压、列布局、验证 codelinter/ohpm/hvigorw 版本
```

安装记录写入 `tools/toolchain.local.json`（需加入 .gitignore）。商业 SDK 就位后：
`hvigorw assembleHap`（debug）→ 记入验证声明；无设备时明确「未实机运行」。
