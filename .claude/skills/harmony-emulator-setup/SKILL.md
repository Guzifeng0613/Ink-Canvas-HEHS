---
name: harmony-emulator-setup
description: |
  在具备虚拟化能力的真实物理机（Windows Hyper-V / macOS）上部署 HarmonyOS 模拟器，
  并对 InkCanvas HarmonyOS 移植版做构建→签名→安装→运行→截图→日志的联调测试；
  也覆盖真机（USB/无线 hdc）安装验证路径。适用：用户要求"跑模拟器/联调/真机验证/部署 emulator"时。
---

# HarmonyOS 模拟器部署与联调测试

本技能用于把 `D:\InkCanvas`（或任意 checkout）的 HarmonyOS 移植版在**真实可用宿主**上跑起来并联调。
项目根：`D:\InkCanvas`；ArkTS 工程：`harmony/`；包名 `com.inkcanvas.harmony`；Ability `EntryAbility`。

## 0. 宿主选择（先判定，避免白忙）

| 宿主 | 可行性 |
|---|---|
| Windows 10/11 64 位 专业/企业/教育 + BIOS 虚拟化 + Hyper-V | ✅ 模拟器可用 |
| Windows 家庭版 | ❌ 无 Hyper-V，模拟器不可用 |
| macOS（Intel/Apple Silicon） | ✅ 用 DevEco Studio 自带模拟器 |
| 普通云 VM（含阿里云 ECS） | ❌ 无嵌套虚拟化，报 `00801001` |
| 弹性裸金属/物理机 + Windows + Hyper-V | ✅ |
| Ubuntu/Linux | ❌ 无官方模拟器（仅命令行构建可用） |
| HarmonyOS 真机 | ✅ 最直接，跳过模拟器 |

**关键：模拟器需要 Hyper-V/嵌套虚拟化；普通云 VM 不行。** 若宿主不合格，直接走真机路径（§5）。

## 1. 工具链

- 推荐 DevEco Command Line Tools（内嵌 SDK）或 DevEco Studio 26.0.0 Release。
- 本仓库约定：CLI 在 `E:\EcoStudioCLI\command-line-tools`（构建机）；物理机自行安装后以 `-CliRoot` 指定。
- 环境脚本：`. tools/harmony-env.ps1`（设置 PATH/HVIGOR_USER_HOME/缓存）。
- 构建缓存建议放非系统盘（`E:\harmony-cache`）。

## 2. 部署模拟器（Windows 物理机，管理员 PowerShell）

```powershell
pwsh -File tools/setup-emulator-host.ps1 -CliRoot 'C:\command-line-tools' -Drive D
```
脚本步骤：检查 CPU 虚拟化/Hyper-V（未启用则启用并提示重启）→ 接受许可 → 下载镜像
（`HarmonyOS 7.0.0(26.0.0)`，tablet）→ 在 ≥12GB 空闲盘创建实例 → 无窗口启动 → 等待 hdc 上线。

手动等价命令：
```powershell
Emulator.bat -license accept
Emulator.bat -install -deviceType tablet -osVersion "HarmonyOS 7.0.0(26.0.0)"
Emulator.bat -create InkPad26 -deviceType tablet -osVersion "HarmonyOS 7.0.0(26.0.0)" `
  -screenProfile "MatePad Pro 13" -instancePath D:\harmony-emulator\instances `
  -imageRoot "$env:LOCALAPPDATA\Huawei\Sdk" -memory 4096 -storage 8192
Emulator.bat -start InkPad26 -instancePath D:\harmony-emulator\instances -imageRoot "$env:LOCALAPPDATA\Huawei\Sdk" -noWindow
hdc list targets
```
API 24 兼容验证：把 `-osVersion` 换为 `HarmonyOS 6.1.1(24)` 另建实例。

## 3. 构建

```powershell
. tools/harmony-env.ps1
cd harmony
ohpm install --all
hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon   # 未签名 HAP
hvigorw assembleApp --mode project -p product=default -p buildMode=debug --no-daemon   # 完整 APP
```
产物：`harmony/entry/build/default/outputs/default/entry-default-unsigned.hap`、
`harmony/build/outputs/default/harmony-default-unsigned.app`。
**不要写 compileSdkVersion**（00303313）；`modelVersion` 两处一致。

## 4. 签名

- 推荐：DevEco Studio → File > Project Structure > Signing Configs → 勾选 Automatically generate signature（华为账号登录），生成调试证书/Profile，再构建即得签名包。
- 命令行本地签名（最佳尝试）：`tools/sign-hap.ps1`。注意 hap-sign-tool 要求 **完整证书链 .cer**；直接导出 p12 叶子证书会被拒（`11013004`）。商用真机/上架需 AGC 调试/发布 Profile。
- 未签名包设备会拒装；`device-install.ps1` 会尝试本地签名并给出指引。

## 5. 安装与联调（模拟器或真机通用）

```powershell
pwsh -File tools/device-install.ps1 -HapPath <signed.hap>     # 安装+启动+截图+hilog
pwsh -File tools/device-verify.ps1  -HapPath <signed.hap>     # 冒烟+崩溃检查+15 项人工清单
```
真机连接：USB 开发者模式/USB 调试，或 `hdc tconn <ip:port>`（无线调试）。
hdc 路径：`<CLI>\sdk\default\openharmony\toolchains\hdc.exe`。

联调循环：改 ArkTS → `hvigorw assembleHap` → 签名 → `device-install.ps1` → 看截图/hilog → 修。
关键日志过滤：`InkCanvas|JsError|AppFreeze|CppCrash`。

## 6. 验证要点（对照 docs/harmony-porting-notes.md）

- 逻辑互认：`node --experimental-transform-types --experimental-loader ./parity/ts-resolve-loader.mjs parity/run-vectors.mjs`（应全绿）。
- 契约：`node tools/parity-check.mjs --final`。
- 真机重点：笔压感/倾斜/接触面积、手掌抑制、双指手势、点 UI 不落墨、课件 PDF、画板媒体、旋转/分屏重绘。
- 模拟器不提供真实笔压感/低延迟，相关项仍需真机。

## 7. 故障排查

| 现象 | 原因/处理 |
|---|---|
| `00801001 未开启 Hyper-V` | 宿主无 Hyper-V/嵌套虚拟化；换物理机/裸金属或走真机 |
| `00801002 磁盘空间不足` | 实例盘需 ≥12GB；换盘 `-Drive`/`-instancePath` |
| 安装失败 | 未签名/证书链不全/包名不符；用 DevEco 自动签名 |
| 快捷键无反应 | 需焦点（defaultFocus）；修饰键用 'Alt'/'Ctrl'（大写） |
| 点浮条落墨 | 触摸应挂在画布层而非根节点（已修） |
| 构建 00303313 | 删除 compileSdkVersion |
| 向量 import 失败 | 用 `--experimental-transform-types --experimental-loader ./parity/ts-resolve-loader.mjs` |

## 8. 清理（不需要模拟器的机器）

移除镜像/实例/配置/模拟器程序，保留构建工具链：
`Emulator.bat -delete <name> -instancePath <...> -force`，再删
`<LOCALAPPDATA>\Huawei\Sdk\system-image`、`<LOCALAPPDATA>\Huawei\Emulator26.0`、`<CLI>\emulator`、实例目录。
