# 移植对照与验证状态

> **验证状态声明**：本文件中标注「1:1 移植（未验证）」的部分，是按要求**跳过行为验证**完成的
> —— 没有为它们编写单元测试，也没有在真实红外屏 / 压感笔 / 触摸屏上实机验证。
> 仅确认**编译通过**。逻辑逐条对应原实现，但阈值手感与硬件相关行为仍需在实际希沃设备上复核。
>
> 与之相对，标注「已验证」的部分有单元测试与运行时自检证据，见
> [`superpowers/evidence/`](./superpowers/evidence/README.md)。

## 本轮 1:1 移植（未验证）

| 原实现 | 新位置 | 说明 |
|---|---|---|
| `MW_TouchEvents.cs` → `MainWindow_TouchDown` / `Main_Grid_TouchDown` 的触摸分档判定 | `Core/Input/TouchContactClassifier.cs` | 逐条对应：`TouchMultiplier==0 && IsSpecialScreen` 禁用橡皮；`boundsWidth > BoundsWidth` 进入橡皮候选；`> BoundsWidth * 阈值` 走面积擦并按系数缩放；其间走线条擦；否则书写 |
| `MW_TouchEvents.cs` → `GetTouchBoundWidth` | `TouchContactClassifier.MeasureContactWidth` | 四边红外（`IsQuadIR`）取 `sqrt(width*height)`，否则取 `width` |
| `MW_Settings.cs` → `BoundsWidth` 赋值 | `TouchContactClassifier.BoundsWidth` | `IsEnableNibMode ? NibModeBoundsWidth : FingerModeBoundsWidth` |
| `MW_MatrixTransform.cs` → `GetMatrixTransformCenterPoint` | `Core/Ink/MatrixTransformCenter.cs` | 三种枢轴选项；未 Loaded 时一律画布中心 |
| `Helpers/LogHelper.cs` | `Core/Diagnostics/FileLog.cs` | 同文件名 `Log.txt`、同行格式 `{ISO} [{Type}] {msg}`、同「失败即忽略」行为；日志目录改为 %APPDATA% 以适配只读安装目录 |
| `MW_AutoTheme.cs` → `GetMainWindowTheme` / `SetBoardTheme` | `Core/Theming/ThemeResolver.cs` | 非屏幕画笔模式跟随黑板/白板选择，否则跟随应用主题。WPF 的 ResourceDictionary 切换在 WinUI 3 由 `RequestedTheme` 承担，故只移植判定逻辑 |
| `Main_Grid_TouchDown` 的 `GestureOnly` 手指翻页 | `InkController.SlideFlipRequested` + `ShellController` 接线 | 触发条件 1:1（放映中 + 无墨迹 + 允许手指控制）；位移阈值 60px 为本移植自定，原实现由 `iniP` 比较得出 |

### 刻意未移植的一处

原 `Main_Grid_TouchDown` 中的 `inkCanvas.Opacity = 1`（及配套 `0.1` 压暗）**没有搬过来**。
那正是笔锋闪烁的第二个来源，本次迁移已修复；1:1 照搬会把 bug 带回来。
代码注释中已就地标注。

## 先前轮次（已验证）

| 原实现 | 新位置 | 状态 |
|---|---|---|
| `MW_SimulatePressure&InkToShape.cs` 笔锋 | `Core/Ink/TaperProfile.cs` | **重写并验证**：改为增量因果计算，单测 + 运行时自检钉住「提交几何 == 最后一帧」 |
| `MW_SelectionGestures.cs` 选区手势 | `Core/Selection/SelectionGeometry.cs` | 已验证（13 项单测 + 实机缩放/撤销） |
| `MW_TimeMachine.cs` / `Helpers/TimeMachine.cs` | `Core/Replay/*` + `Views/ReplayWindow.cs` | 已验证（13 项单测 + 实机回放） |
| `Helpers/AutoUpdateHelper.cs` | `Core/Update/*` + `Features/Update/UpdateService.cs` | 已验证（26 项单测；下载长度校验；安装前确认） |
| `Helpers/EdgeGestureUtil.cs` | `Platform/System/EdgeGestureService.cs` | 已验证属性写入成功；补上原实现缺失的 `Commit()` |
| `Helpers/WinTabWindowsChecker.cs` | `Platform/System/WindowEnumerator.cs` | 以 `EnumWindows` 替代 WPF-only 的 UI Automation |
| `MW_ShapeDrawing.cs` | `Core/Shapes/ShapeGenerator.cs` | 已验证（26 种图形均产出几何） |
| `Helpers/InkRecognizeHelper.cs`（InkAnalyzer） | `Core/Recognition/ShapeRecognizer.cs` | **重写**：托管几何拟合，判定边界与 InkAnalyzer 不一致 |
| `Resources/Settings.cs` | `Core/Settings/AppSettings.cs` | 已验证 JSON 键名兼容旧文件 |

## 仍未移植

| 项 | 原因 |
|---|---|
| `MW_ElementsControls.cs` / `Helpers/InkCanvasElementsHelper.cs`（图片等元素插入与变换） | 需要独立的元素层与命中模型，尚未实现 |
| `MW_Notification.cs` / `Windows/YesOrNoNotificationWindow.xaml` | 通知条 UI 未移植；当前用 `ContentDialog` 承担确认 |
| `Windows/CycleProcessBar.xaml`、`Helpers/SoftwareLauncher.cs`、`Helpers/WindowMemoryHelper.cs` | 辅助 UI / 启动器 / 窗口位置记忆，未移植 |
| 旧 `.isf` 墨迹存档读取 | 依赖 `System.Windows.Ink`，WinUI 3 无此栈；已改用 `.icink` |