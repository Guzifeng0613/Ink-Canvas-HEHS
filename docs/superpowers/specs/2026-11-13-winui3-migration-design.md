# Ink Canvas → WinUI 3 迁移设计

日期：2026-11-13
上游：`InkCanvas/Ink-Canvas-Artistry`（GPL-3.0）
fork：`QiXinia/Ink-Canvas-HEHS`，工作分支 `feat/winui3-migration`

## 1. 目标

把 WPF / .NET Framework 4.7.2 的 Ink Canvas Artistry 完整迁移到 WinUI 3（Windows App SDK），
界面重做为 Fluent 风格并完整适配动画与触摸，修复笔锋闪烁，保持模块化；工具链只装最小必要件。

## 2. 已核实的事实（探针实测，非推断）

| 编号 | 结论 | 证据 |
|---|---|---|
| F1 | WinUI 3 **非打包**应用可在「.NET 10 SDK + VS BuildTools 17.14 + Windows SDK 26100」下构建运行，**无需 VS UWP 工作负载** | 最小探针构建 exit 0；本仓库 Release 全解决方案零错误 |
| F2 | `net10.0-windows10.0.26100.0` + WindowsAppSDK 1.8.260921001 + Win2D 1.4.0 可用，**无需安装 .NET 8 运行时** | 同上 |
| F3 | **WinUI 3 XAML 窗口无法逐像素透明**。`SystemBackdrop=null` + 透明根 + `WS_EX_NOREDIRECTIONBITMAP`（初始化前/后）+ DWM blur-behind + accent 政策，五种手法全部失败 | `evidence/verdict-t1.json`、`verdict-t4.json`、`verdict-t5.json` 与对照组 `verdict-opaque.json` 数值完全一致（whiteFloodRatio 0.9881）；截图 `evidence/winui3-xaml-opaque.png` |
| F4 | **原生分层窗口 + Win2D 预乘 alpha 可行**，含半透明 | `evidence/verdict-layered.json`：backgroundMatchRatio 0.9765、whiteFloodRatio 0；截图 `evidence/layered-overlay-alpha.png` 可见半透明笔画下透出桌面 |
| F5 | 本机全局 NuGet 配置**没有任何包源** | `%APPDATA%\NuGet\NuGet.Config` 的 `<packageSources>` 为空 |
| F6 | 上游 `MainWindow.xaml` 单文件 502 KB；全仓 68 个 .cs/.xaml 共 1.34 MB | 文件统计 |

### 笔锋闪烁的根因（两处，均已定位）

1. **`MainWindow_cs/MW_SimulatePressure&InkToShape.cs:363-499`** —— 笔锋在 `StrokeCollected`
   里、也就是**抬笔之后**才计算。WPF 书写过程中以均匀 pressure 0.5 渲染，抬笔时该处理器
   **整体替换** `e.Stroke.StylusPoints`：`InkStyle=0` 把末尾 10 点的 pressure 从 0.5 斜坡降到 0.1；
   `InkStyle=1` 按速度重算**每一个点**。于是抬笔瞬间笔尾（或整笔）宽度突变 —— 这就是闪烁。
2. **`MainWindow_cs/MW_TouchEvents.cs:241`** —— 手掌误触判定时 `inkCanvas.Opacity = 0.1`，
   随后在 220/308 与 `StrokeCollected:24` 三处拉回 1，整个墨迹层明暗跳变。

## 3. 架构决策

- **D1 目标框架**：`net10.0-windows10.0.26100.0`，`x64`，`WindowsPackageType=None`（非打包），
  `WindowsAppSDKSelfContained=true`。依据 F1/F2。非打包避免 MSIX 与 AppxPackage targets，磁盘最省。
- **D2 依赖最小化**：直接引用 `Microsoft.WindowsAppSDK.WinUI` 等子包，**不引用** monolith，
  从而跳过 AI / ML / Widgets / Search（实测 monolith 会多拉约 200 MB）。
- **D3 屏幕画笔覆盖层**：原生分层窗口（`WS_EX_LAYERED`）+ Win2D 预乘 alpha +
  `UpdateLayeredWindow`。依据 F3/F4 —— 这是唯一实测可行的逐像素透明路径。
- **D4 其余全部界面用 WinUI 3**：浮动条、侧栏、画板、设置、对话框各为独立 WinUI 3 窗口。
  上游把它们全塞在一个透明大窗里；拆开后每个窗口职责单一，也便于分层与触摸命中。
- **D5 笔锋改为增量因果计算**（核心修复，见 §5）。
- **D6 手掌误触不再调节墨迹层透明度**，改为不动墨迹的轻量提示。
- **D7 图形识别自研托管实现**，替换 `IACore/IALoader/IAWinFX`（Tablet PC InkAnalyzer，
  绑 .NET Framework 且仅 x86）。这同时解除上游的 x86 锁定。
- **D8 PPT 交互用 COM `dynamic` 互操作**，不依赖 Office PIA。
- **D9 设置用 `System.Text.Json`**，保留原属性名以兼容既有 `Settings.json`。
- **D10 墨迹存档改用自有 `.icink`（JSON）**。WPF 的 ISF 依赖 `System.Windows.Ink`，
  WinUI 3 无此栈 —— **这是一处明确的兼容性中断**，在 README 写清。
- **D11 仓库内声明 NuGet 源**（依据 F5），否则任何机器上还原都会 NU1100 失败。
- **D12 包缓存放 D 盘**（`D:\dev-cache\nuget`），保护紧张的 C 盘。

## 4. 模块划分

```
InkCanvas.sln
  src/InkCanvas.Core        纯域模型，无 UI 依赖：墨迹模型、笔锋曲线、几何、撤销栈、设置、图形生成与识别
  src/InkCanvas.Platform    Win32 互操作：分层覆盖层窗口、置顶同步、全局热键、手掌判定、开机自启、屏幕
  src/InkCanvas.Ink         Win2D 渲染 + 输入路由 + InkController（对外唯一墨迹入口）
  src/InkCanvas.Controls    WinUI 3 可复用控件、主题 token、动画helper、触摸尺寸
  src/InkCanvas.Features    画板 / PPT / 计时器 / 抽奖 / 截图 / 回放 / 持久化
  src/InkCanvas.App         WinUI 3 应用头：窗口外壳、模式切换、依赖装配
  tests/InkCanvas.Core.Tests  xunit：笔锋不变量、几何、识别、撤销、设置往返
```

依赖方向单向：`App → Features → Controls → Ink → Platform → Core`。
`Core` 不引用任何 Windows UI 类型，因此可单测。

## 5. 笔锋闪烁的修复设计

**不变量（可测）**：*提交后的笔画几何，必须与抬笔前最后一帧所渲染的几何逐点相同。*

实现：
- `InkPoint` 直接存 `Width`。渲染是「存储数据的纯函数」，不再在提交时重算。
- `ITaperProfile` 增量求值：维护末尾 N 点的滚动窗口，每帧只让**当前笔尖附近**的点带收窄；
  笔尖移过后该点宽度**冻结**为全宽。某点宽度随时间单调不减，故视觉变化只发生在移动的笔尖处，
  读起来是正常笔锋而非跳变。
- 速度型笔锋只用**因果**（已到达的）点做后向差分，绝不引用未来点，因此不会回溯改写历史点。
- 抬笔时**不重算**：把最后一帧的宽度直接固化进模型。
- 渲染分层：已提交笔画缓存到 `CanvasRenderTarget`，活动笔画每帧画在其上；
  提交是「加入模型 + 清空活动笔画」的**原子操作**，然后才 `Invalidate()`，
  因此不存在「两层都没有」或「两层都有」的中间帧。

回归测试：`TaperTests.LastLiveFrameEqualsCommittedGeometry` 直接断言该不变量。

## 6. 触摸与动画适配

- 所有可点目标 ≥ 40×40 px；图标按钮统一 `TouchIconButton`。
- 双指平移/缩放/旋转带惯性（`InkManipulator`）；笔书写、触摸操作、手掌擦除三路分流。
- 动画走 Composition 隐式动画 + `ThemeTransitions`；浮动条折叠用弹簧；
  设置页与浮动条之间用 `ConnectedAnimationService`。
- 尊重系统「关闭动画」辅助功能开关（`UISettings.AnimationsEnabled`）。

## 7. 验证方式

- `dotnet build` 全解决方案零错误。
- `dotnet test` Core 单测（含笔锋不变量回归）。
- 实机启动 + 截图核对：覆盖层透明、墨迹渲染、浮动条与设置页可用。
- 覆盖层透明沿用探针的量化判据（informative 像素匹配率 / whiteFloodRatio）。

## 8. 已知取舍

- ISF 旧存档不可读（D10）。
- 上游 502 KB 单体设置 XAML 不逐像素复刻，改为按真实设置模型重建的分组设置页。
- 图形识别为自研几何拟合，与 InkAnalyzer 的判定边界不会完全一致。