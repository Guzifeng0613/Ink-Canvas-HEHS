<div align="center">

<img src="./Images/Ink Canvas Artistry.png" style="width:96px;"/>

# Ink Canvas · WinUI 3

[![LICENSE](https://img.shields.io/badge/License-GPL--3.0-red.svg)](./LICENSE)

基于 [InkCanvas/Ink-Canvas-Artistry](https://github.com/InkCanvas/Ink-Canvas-Artistry)
（其上游为 [WXRIW/Ink-Canvas](https://github.com/WXRIW/Ink-Canvas)）的 **WinUI 3 迁移版**。

</div>

## 这个分支做了什么

把原来的 WPF / .NET Framework 4.7.2 应用整体迁移到 **WinUI 3（Windows App SDK）**，
界面按 Fluent 重做并完整适配触摸与动画，同时修掉了**笔锋闪烁**。

- **目标框架**：`net10.0-windows10.0.26100.0`，x64，**非打包**部署（无需 MSIX、无需 VS UWP 工作负载）。
- **墨迹引擎重写**：WinUI 3 没有 `InkCanvas` / `System.Windows.Ink`，改用 Win2D 自绘可变宽度笔画。
- **图形识别自研**：以托管几何拟合替换 Tablet PC `InkAnalyzer`，因此不再被锁在 x86。
- **PPT / WPS**：COM `dynamic` 后期绑定，不再依赖 Office PIA。
- 构建与最小环境说明见 **[BUILDING.md](./BUILDING.md)**；架构与取舍见
  **[设计文档](./docs/superpowers/specs/2026-11-13-winui3-migration-design.md)**。

## 笔锋闪烁是怎么修的

原实现在 `StrokeCollected`（**抬笔之后**）才计算笔锋，并整体替换 `StylusPoints`：
`InkStyle=0` 把末尾 10 点的压感从 0.5 斜坡降到 0.1，`InkStyle=1` 按速度重算**每一个点**。
于是抬笔瞬间笔尾（或整笔）宽度突变。另外手掌误触判定会把整个墨迹层 `Opacity` 压到 0.1 再拉回 1，
造成第二处明暗跳变。

本分支的做法：

1. **宽度逐点增量计算并落库**。`InkPoint.Width` 是存储值，渲染是存储数据的纯函数。
2. **笔锋只跟移动的笔尖走**。末尾 N 点的滚动窗口内收窄，笔尖移过即冻结为全宽；
   某点宽度随时间单调不减，所以视觉变化只发生在笔尖处，读起来是笔锋而不是跳变。
3. **速度笔锋改为因果**：只用已到达的点做后向差分，绝不引用未来点。
4. **抬笔不重算**，提交是「完成 + 入文档 + 记历史」的原子操作，只触发一次重绘。
5. **手掌判定不再改墨迹层透明度**，只影响输入路由。

判据被测试钉住：`TaperTests.LastLiveFrameEqualsCommittedGeometry`，
以及运行时 `--selftest` 里的 `widthsIdentical` / `outlineIdentical`。

## 模块划分

```
src/InkCanvas.Core        纯域模型（无 UI 依赖）：墨迹、笔锋曲线、几何、撤销、设置、图形生成与识别
src/InkCanvas.Platform    Win32：分层透明覆盖层窗口、笔/触摸输入、手掌判定、热键、置顶、自启
src/InkCanvas.Ink         Win2D 渲染 + InkController（对外唯一墨迹入口）+ 双指手势
src/InkCanvas.Controls    WinUI 3 控件、主题 token、动画、触摸尺寸
src/InkCanvas.Features    画板分页 / PPT / 倒计时 / 抽奖 / 截图 / 回放 / 持久化
src/InkCanvas.App         应用头：窗口外壳、模式切换、依赖装配
tests/InkCanvas.Core.Tests  单元测试（含笔锋回归）
```

依赖单向：`App → Features → Controls → Ink → Platform → Core`。

## 为什么覆盖层不是 WinUI 3 窗口

**WinUI 3 的 XAML 窗口无法逐像素透明。** 实测五种手法（空 backdrop + 透明根、
`WS_EX_NOREDIRECTIONBITMAP` 在 XAML 初始化前/后、DWM blur-behind、两种 accent 政策）
输出与不透明对照组**逐像素一致**。

因此屏幕画笔的墨迹面用 **Win32 分层窗口 + Win2D 预乘 alpha**（实测背景保留率 97.65%、
半透明笔画正常），而**其余所有界面（工具栏、画板、设置、对话框）都是 WinUI 3 窗口**。

## 快速开始

```powershell
.\build.ps1 build
.\build.ps1 test
.\build.ps1 run
```

## 已知兼容性中断

- **旧 `.isf` 墨迹存档无法读取**。ISF 依赖 `System.Windows.Ink`，WinUI 3 无此栈。
  新格式为 `.icink`（JSON，逐点存宽度）。
- 设置文件 `Settings.json` **保持原 JSON 键名**，可直接沿用。
- 图形识别为自研拟合，判定边界与 `InkAnalyzer` 不完全一致。
- 上游 502 KB 单体设置 XAML 未逐像素复刻，改为按真实设置模型重建的分组设置页。

## 尚未完成 / 验证边界（如实记录）

主要功能缺口已在 3.1.0 补齐（选区变换、墨迹回放、自动更新、操作指南与更新日志、
边缘手势抑制、覆盖层回读优化）。仍然存在的限制：

| 项 | 现状 |
|---|---|
| 特殊屏 / QuadIR / `touchMultiplier` / 笔尖-手指接触阈值微调 | 设置项保留，但**行为未移植**。这些是针对特定红外屏硬件的标定，没有对应设备无法确认语义，不做猜测实现 |
| 真实压感笔与触摸屏 | **未在硬件上验证**。开发机无数位板；压感、手掌接触矩形与双指手势是通过真实输入管线的合成样本验证的，希沃设备上的压感曲线与手掌阈值仍需实机复核 |
| 旧 `.isf` 存档 | 读不了（见上） |
| 设置页外观 | 按真实设置模型重建，非上游逐像素复刻 |

覆盖层性能：已提交层缓存 + 只回读活动笔画包围盒。实测**连续书写时每帧回读约 205 KB，
相当于整屏 8.29 MB 的 2.5%（约 40× 降低）**；整轮自检（含多次提交，每次提交需重建缓存）
平均为整屏逐帧方案的 10.4%。数据见 `docs/superpowers/evidence/runtime-selftest.json`。

## 许可与致谢

GPL-3.0，见 [LICENSE](./LICENSE)。原项目作者与贡献者保留全部署名；
`MW_SimulatePressure&InkToShape.cs` 中 WXRIW 的版权声明所述算法在本分支被重写，
其笔锋意图（末尾收细、速度影响粗细）被保留并以因果方式重新实现。