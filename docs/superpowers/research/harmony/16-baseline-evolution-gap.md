# 16 — Baseline evolution port-gap report（未提交 WinUI3 WIP → HarmonyOS）

- 日期：2026-11-19。方法：`git diff`（工作树 vs HEAD 51e3a98）+ 新增（untracked）文件通读 + harmony/ 侧逐点验证。
- 行号引用当前工作树（未提交基线）。范围：任务指定的 6 个区域 + 相关新增 Core/Ink 逻辑文件。
- 按约定**不重复报告**已在同步清单内的事项：TaperKind+4 / SimulatedPressureTaper / OneEuroFilter / StablePointCount / InkStroke 状态化 taper / AppSettings highlighterWidth / OriginalMenuCatalog 扩展 / 橡皮半径表 / 调色盘笔尖菜单。

## 0. harmony 侧已验证存在（勿当缺口上报）

StrokeEraser.eraseDisc（core/ink/StrokeEraser.ts:41）；PenNib/NibRotation/nibTransform/HighlighterAlpha（core/ink/StrokeAttributes.ts:5-27）；ObjectFrame 导入导出（core/ink/InkStroke.ts:157-179）；定向选区框 OrientedFrame 含旋转命中/八向柄/旋转柄（ink/InkController.ts:233-292；core/selection/SelectionGeometry.ts:14-26 枚举含 Top/Right/Bottom/Left，:129-176 边柄单轴缩放）；Pan 工具、MultiTouchEnabled 反转语义（真=多指书写）、marquee 视图变换命中（InkController.ts:723,1084,1693-1697）、additive 框选、延迟写点/PreviewStrokes、CloneMode+克隆拖拽、面积擦快照撤销、onPointerCanceled、selectAll、StickyShape/selectShape、两步体（generateCuboid/generateHyperbolaBranches）；BoardDocument.Media + 往返（features/board/BoardDocument.ts:37-43,167-171）；BoardElementMath；BoardElementHistory.recordTransform；回放窗口（entry Index.ets:2282-2283 InkReplaySession+ReplayClock）；InkDocument.insertRange/replace（core/ink/InkDocument.ts:85,96）；应用内 yesNo；设置 88 字段（含 eraserType/eraserSize）。
- harmony/ink/InkController.ts:8-13 自述**未移植**：Lasso 擦、SelectedElements/SelectionFrame 元素桥、TransformSelectionUndoable。:15-18 自述保留 3 个基线缺陷（选区双指每 move 一个撤销单元、两步体无条件回笔、单笔提交双 Invalidated）——新 C# 基线已全部修复，成为对齐项。

## 1. 各区域行为变更清单

### 1.1 InkController（src/InkCanvas.Ink/InkController.cs，+1235）

新增公共 API（一行一个）：

- `InkTool.Pan`（:17）— 拖拽平移 ViewTransform 的独立工具。
- `InkEraserMode {Area,WholeStroke,Lasso}`（:18）— 橡皮三模式枚举。
- `Playback/PlaybackChanged/BeginPlayback()/SeekPlayback(ms)/EndPlayback()`（:28-50）— 画布内 display-only 回放（可只回放选区），期间拒绝输入，切工具/改文档自动结束。
- `DotAnimations/DotAnimationVersion/DotAnimationChanged/IsAnimatingDot/AdvanceDotAnimations(ms)/FinishDotAnimations()`（:60-92）— 落点 120ms 圆点成形动画状态。
- `ToolChanged` 事件 + Tool setter 全量清理（:129-158）：结束回放、取消套索、离开 Select 清选区+克隆、离开 Shape 重置两步体+粘滞、笔/荧光笔宽度互换。
- `ConfigurePenWidths(pen, highlighter)`（:165-170）— 分别记忆两宽度并在工具切换时换 BaseWidth。
- `ShapeKind` setter 重置两步体；`StickyShape`；`SelectShape(kind, sticky)`（:172-200）。
- `CloneMode`（:202-208）。
- 橡皮预览：`EraserPreviewChanged/IsEraserPreviewVisible/EraserPreviewCenter/EraserPreviewRadius/EraserVisualBounds/OnPointerHover/HideEraserPreview(id?)`（:222-261）— 悬停跟随圆（双描边）。
- `EraserMode` setter（:263-276）带取消语义。
- 套索擦：`IsLassoErasing/LassoPath/LassoVisualBounds/CancelLassoErase()`（:278-284,1009-1077）— 4096 点抽稀、抬指自动闭合、命中整项删除、恢复保叠放序。
- `OnPointerCanceled(pointer)`（:1080-1092）— 取消输入不删套索、不生成落点。
- `IsViewManipulating`（:292-293）。
- 元素选区：`SelectedElements/GetSelectableElements/HasSelection/IsMarqueeSelecting/MarqueeBounds/IsRotatingSelection/SelectionRotationDegrees/SelectionVisualBounds`（:310-331）。
- 延迟写点：`PreviewStrokes/SelectionPreviewTransform/IsPreviewStroke`（:367-371）。
- `SelectionFrame`（:373-408，含 RememberFrame 缓存与 RestoreSelectionFrame）。
- `ClearSelection()/SelectAll()`（:410-428）。
- `BeginSelectionGesture()/ApplySelectionGesture(delta)/EndSelectionGesture()`（:1405-1442）— 双指选区操作整体一个撤销单元。
- `TransformSelectionUndoable(m, name, scaleWidth)`（:1473-1497）。
- `RecolorSelection/ScaleSelectionWidth/RestoreSelectionWidth`（:1501-1508）— 选区工具栏命令。
- `Undo/Redo` 现先 EndPlayback+CancelLassoErase+PruneSelection（:1531-1552）。

行为逻辑：

- 两步图形（:814-943）：立方体系（第一步前面保持预览、第二步带符号深度，`ShapeGenerator.GenerateSolid/SolidBaseEnd/IsTwoStepSolid`，新文件 Core/Shapes/SolidShapes.cs）与双曲线（第一步虚渐近线锁 k，第二步分支 `GenerateHyperbolaBranches`）；图形墨迹统一 `TaperKind.None`（:837-842）；完成后非粘滞回笔。
- 识别门控（:963-977）：直线只认刚提交的单笔；需按住端点 ≥450ms（`LineHold` 3px，:96-97,749-753）；识别结果统一宽度；圆改为中心+半径生成（CircleFromCenter）。
- 落点确认 `ConfirmTap`（:759-798）：屏幕空间容差把单击收拢为单点圆 + InkDotAnimation 过渡；SmoothPen..Calligraphy 笔迹用相对时间（:806-808）。
- 面积擦改为逐笔切割 `StrokeEraser.EraseDisc`+`Document.Replace`，快照级撤销 "area erase"（:1106-1176）；整笔擦保持原语义；橡皮手势绑定指针 id。
- 选区交互（:1211-1394）：Ctrl/Shift 增选、Ctrl 单击切换、元素+墨迹混合命中（元素 Frame.Contains、墨迹容差随视图缩放）、旋转框选框、克隆拖拽生成副本（preserveId:false、元素 Duplicate）单撤销单元、Move/Rotate/手势期间只更新 `_selectionPreview` 矩阵。
- 触摸路由（:480-533）：显式橡皮保持模式/尺寸不被重分类为手掌；视图手势用屏幕空间复合（`TwoFingerManipulator.Update(..., screenSpace:true)`）+ 设置驱动轴心。

### 1.2 BoardService（src/InkCanvas.Features/Board/BoardService.cs，+361）

- `BoardImageAsset.Transform`（float[6] 累积画布矩阵，:16）与 `Copy(preserveId, copyData)`（:18-31，克隆共享字节）。
- 新 `BoardMediaAsset`/`BoardMediaGeometry`（:36-59）页内嵌视频。
- 新 `BoardElementMath`（:66-122）Read/Write/Append/Same/TransformedBounds/RenderMatrix（harmony 已有，含中心锚点换算）。
- `CurrentMedia` + `ImagePlacementChanged/MediaPlacementChanged`（:150-153）。
- `GetImageIndex/GetMediaIndex`、`SetImagePresent/SetMediaPresent(page, asset, present, restoreIndex)`（:156-192）— 元素在场/离场+按原序恢复。
- 翻页携带绘制状态 `CarryDrawingState`（:242-259）：属性/橡皮/图形/工具带过去，清选区/套索/克隆。
- `CloneSelectionToNewPage()`（:262-287，衍至新页，副本共享字节）；`ClearCurrentPage()`（:289-301，墨迹+图片+视频原子清空单撤销）。
- `AddMedia/RemoveMedia/RecordMediaTransform/RecordImageChange/RecordMediaChange`（:330-393）。
- 持久化（:704-710,536）：StrokeFile 增 `Nib/NibRotation/HighlighterAlpha/ObjectFrame`；BoardPageFile 增 `Media`。

### 1.3 渲染（OverlayInkPresenter/StrokeRenderer/InkSceneRenderer）

算法/行为性：

- InkSceneRenderer：回放期间隐藏源笔画只画 `playback.VisibleStrokes`（:31-37）；预览笔画用 `SelectionPreviewTransform*view`（:20）；新增套索覆盖（闭合虚线多边形+35α 填充，:73-103）、选区装饰（框选虚线矩形、旋转框 8 柄、圆形箭头旋转柄、旋转中角度气泡，:105-171）、橡皮预览双描边圆（:49-65）。
- StrokeRenderer：缓存键扩为 引用+GeometryVersion+**NibTransform**（:47-49）；LRU 100k 点/2048 条（:46-47,75-80）；live 笔画按 `StablePointCount` 冻结 64 段分块增量建几何，整组一次 Winding 填充（荧光笔交叉不加深，:82-105）；每笔单 path，段轮廓直接带 nib 矩阵写入 scratch（:109-141）。
- OverlayInkPresenter：行为性部分为 CommittedInkLayer 历史瓦片（平移复用光栅，新文件 Rendering/CommittedInkLayer.cs）、PointerCanceled/Hover/Exited 接线（:143-148）、空场景保持 α=1 只画一次（:249-260）、`Suspend()/ReleaseRenderBuffers()`；其余为 UpdateLayeredWindow/DIB/延迟统计等 Win32 管道与性能（:207-280）。

### 1.4 新 UI 文件（功能 → 行为性 or 纯窗口 chrome）

- `App/Views/SelectionToolbarWindow.cs` — 选区旁随动工具条窗口（非激活、DPI/缩放、FloatingToolbarLayout 放置）。**内容行为性（命令见下）；窗口机制 chrome**。
- `Controls/Controls/SelectionToolbar.cs` — 选区动作条：克隆开关、样式（9 色 Recolor + 加粗/变细/恢复粗细）、变换、更多（存为图片/衍至画板/取消选择）、删除。**行为性**。
- `App/Views/BarPanelWindow.cs` — 更多设置/手势选项/橡皮 弹出面板独立窗口（BarPanelKind）。**纯 chrome**。
- `App/Views/FoldedBarHandleWindow.cs` — 工具栏收纳后屏幕边缘把手。**纯 chrome**（harmony fold 按钮已在目录）。
- `App/Views/BoardMediaLayer.cs` — 页内视频层：MediaPlayerElement+传输控件、RenderMatrix 应用、刷新保播放器/进度、选中视觉。**行为性（视频功能本体）**。
- `App/Views/YesOrNoWindow.cs` — 顶层是/否通知窗（X 不触发任一动作）。harmony 已有应用内 yesNo → **行为等价已存在；仅窗口形态 chrome**。
- `App/Services/ChromeBackdrop.cs` — 透明 DesktopAcrylic 背板。**纯 chrome**。
- `App/Services/TransientWindowDismissal.cs` — Win32 前台跟踪的点击外部/Esc 关闭。**基本纯 chrome**（ArkUI 面板有原生失焦关闭语义）。
- `Controls/Controls/BoardGesturePanel.cs` — 手势设置面板（多指书写开关联动禁用手势项、变换中心点）。设置本身行为性（harmony 88 字段已有）；**面板形态 chrome**。
- `Controls/Controls/BoardPenPalette.cs` — 画板笔墨选项（笔迹风格/笔尖/粗细/墨迹识别/明暗色板 + 荧光笔粗细/透明度/色板 + 预览）。**行为性 UI**，多数映射已在 harmony PenPalette/设置中。
- `Controls/Controls/EraserPalette.cs` — 橡皮二级菜单：面积擦/墨迹擦/**套索擦** + 大小 + 可选清空。**行为性（套索入口）**。
- `Core/Selection/FloatingToolbarLayout.cs` / `BoardDockLayout.cs` — 纯逻辑：工具条候选位评分放置 / 三条 55DIP 底栏自适应两行。**逻辑，随对应 UI 移植**。
- 其余新 Controls（PenStrokePreview/ShapePreview/PenStylePicker/PointerSlider/SegmentedSelector/ToolPageLayout/WindowWorkspace/CountdownRing）— 通用原语/预览；PenStylePicker、CountdownRing 已移植/同步。

### 1.5 PptInterop（src/InkCanvas.Features/Ppt/PptInteropService.cs，+102）

- AllowWps=false 且 wpp.exe 在跑时拒绝附着（:82-88,94-104）。
- `PptInkSession.RecommendedPollInterval`（200ms 放映中/1000ms 空闲，PptInkSession.cs:50-56）。
- 幻灯片数变化即刷新索引；完整清单仅在 identity/数量变化时重读；SlideID→索引缓存 + presentation 信息缓存（:62-68,361-406）。
- 放映外跳转先 `presentation.Windows[1].View` 再 ActiveWindow（:199-221）；放映开始重置截图抑制（PptInkSession.cs:217-221）。
- 均为 Windows COM 专属；harmony 走 WPS 跳转已移植，仅轮询节奏/页数变化语义可对齐 CoursewareSession。

### 1.6 Core 小改

- InkDocument：`InsertRange((Index,Stroke))` 按原序恢复；`Replace` 原位换碎片；`HitTest(rect, worldToView)` 中心线 Liang-Barsky 裁剪命中（长直线稀疏点不漏选）（InkDocument.cs:59-97）。
- InkStroke：`RestorePrefix`（回放部分重写）；`GeometryVersion`；`ObjectFrame`+导出导入；Bounds 随 nib 各向异性（rx/ry）；Ellipse45 的 NibRotation 随 Transform 旋转；`Clone(preserveId)`（InkStroke.cs:44-77,157-175,216-221,241-259）。
- StrokeAttributes：`PenNib` 枚举、`Nib/NibRotation/NibTransform`（2:1 椭圆，-45°+rot）（StrokeAttributes.cs:4,20-23）。
- StrokeGeometry：段轮廓在 nib 空间构造再变换（WriteSegmentOutline 重载，StrokeGeometry.cs:36-78）。
- ShapeRecognizer：直线仅单笔；TryLine 增最大偏差、路径/直线长比 ≥0.97、<3px 抖动跳过（防手写被直线替换）（ShapeRecognizer.cs:55,119-136）。
- SelectionGeometry：+Top/Right/Bottom/Left 四边柄；边柄单轴缩放、keepAspect 仅角柄（SelectionGeometry.cs:6,69-75）。
- TwoFingerManipulator：`screenSpace` 复合序参数（:32,72）。
- PalmRejector：仅性能（默认设置静态化），无行为变化。

## 2. 移植缺口表

| # | 缺口 | C# 依据 | harmony 归属模块 | 类型 | 量级 |
|---|---|---|---|---|---|
| G1 | 套索擦端到端（LassoGeometry + 控制器状态 + 橡皮模式 UI + 虚线覆盖渲染 + 取消语义） | InkController.cs:1009-1092; LassoGeometry.cs; EraserPalette.cs:26 | core/selection + ink + controls + entry | 行为 | M |
| G2 | 元素选区桥：SelectedElements/ISelectionElement/BoardSelectionElement、在场 SetPresent 按序恢复、元素框选/命中/删除/克隆/套索联动 | InkController.cs:310-331,1211-1394; BoardSelectionElement.cs; BoardService.cs:156-192 | features/board + ink | 行为 | M |
| G3 | 选区命令 API + 工具条 UI：Recolor/ScaleWidth/RestoreWidth/TransformSelectionUndoable + SelectionToolbar + 放置 | InkController.cs:1473-1529; SelectionToolbar.cs; FloatingToolbarLayout.cs | ink + controls + entry | 行为 | M |
| G4 | 撤销语义对齐（新基线已修 3 缺陷）：手势单撤销单元、两步体尊重 StickyShape、单次 Invalidated | InkController.cs:940-942,1319-1376；harmony InkController.ts:15-18 自述保留 | ink | 行为 | S |
| G5 | 识别加固：单笔直线优先、450ms 端点按住门控、TryLine 偏差/路径比、识别统一宽度、中心锚定圆 | InkController.cs:963-977,749-753; ShapeRecognizer.cs:55,119-136 | core/recognition + ink | 行为 | S |
| G6 | 落点确认 + 圆点动画（ConfirmTap/InkDotAnimation/DotAnimationClock + 渲染） | InkController.cs:759-798; InkDotAnimation.cs; InkSceneRenderer DrawLive | core/ink + ink + entry | 行为 | S-M |
| G7 | 橡皮悬停预览圆（EraserPreview API + Hover/Exited 接线 + 双描边渲染） | InkController.cs:222-261; InkSceneRenderer.cs:49-65 | ink + entry（ArkUI 无 hover 时可映射最后触点/笔悬浮） | 行为 | S |
| G8 | ConfigurePenWidths：笔/荧光笔独立宽度 + 切换互换（衔接已同步的 highlighterWidth） | InkController.cs:139-170 | ink + entry 接线 | 行为 | S |
| G9 | 控制器级回放（BeginPlayback/Seek/End + InkPlayback + 渲染过滤）；entry 已有独立回放窗，属融合项 | InkController.cs:28-58; InkPlayback.cs | ink + entry | 行为 | M |
| G10 | 画板页语义：翻页携带绘制状态、衍至新页、原子清空当前页 | BoardService.cs:242-301 | features/board | 行为 | M |
| G11 | 媒体/元素变更记录并入页撤销栈（AddMedia/RemoveMedia/RecordMediaChange；harmony recordTransform 为独立栈） | BoardService.cs:330-393; BoardElementHistory.ts:51-92 | features/board | 行为 | M |
| G12 | 视频插入 + BoardMediaLayer 等价（entry 目录 insertVideo 目前 DefaultEnabled:false） | BoardMediaLayer.cs; FloatingBarCatalog.ts:23 | entry + features/board | 行为 | M |
| G13 | 画板包/.icink schema：Nib/NibRotation 持久化（HighlighterAlpha/ObjectFrame 已有） | BoardService.cs:704-710; harmony BoardDocument.ts:87-100 | features/board + parity/schemas | 行为 | S |
| G14 | 选区装饰补齐：小对象隐藏重叠边柄（VisibleResizeHandles）、旋转圆形箭头+角度气泡、框选虚线样式 | SelectionFrame.cs:37-48; InkSceneRenderer.cs:105-171 | entry 渲染 | 视觉/行为 | S |
| G15 | 浮条/画板栏入口：克隆按钮、橡皮菜单加套索擦、画板底栏工具格（选择/橡皮/撤销重做/手势/更多） | FloatingBar.cs diff（_cloneButton、BarPanelKind.Eraser）; BoardWindow.cs:252-265; FloatingBarCatalog.ts:13-33 | controls + entry | 行为 UI | S |
| G16 | 纯逻辑随行：FloatingToolbarLayout（工具条放置评分）、BoardDockLayout（55DIP 三条两行） | FloatingToolbarLayout.cs; BoardDockLayout.cs | core/selection | 逻辑 | S |
| G17 | 课件会话轮询节奏 + 页数变化即刷新（对齐 RecommendedPollInterval 200/1000ms 语义） | PptInkSession.cs:50-56,67-80 | features/courseware | 行为（宿主） | S |

COM 细节（WPS ROT 拒绝、Windows[1] 跳转、SlideID 缓存）为 Windows 专属，不构成 harmony 缺口。Platform 新文件 SoftwareLauncher.cs/NonActivatingWindow.cs 属平台层，未在本次 6 区域内深查，建议单独核。

## 3. 纯 WinUI3 窗口 chrome（无需 ArkTS 行为对应）

- **BarPanelWindow / FoldedBarHandleWindow / SelectionToolbarWindow（窗口壳）**：解决 HWND 无法内嵌弹出/非激活/DPI 的问题；ArkUI 用同页面 bindSheet/自定义浮层天然获得，仅需内容（G3/G15）。
- **ChromeBackdrop**：WinUI DesktopAcrylic 透明背板；ArkUI 背景模糊体系独立。
- **TransientWindowDismissal**：Win32 GetForegroundWindow 轮询为"演示进程持焦点时仍可关闭"；ArkTS 应用内面板失焦/点外关闭是平台原生语义，无等价需求（Esc 键语义可顺带实现，非必须）。
- **YesOrNoWindow（窗口形态）**：harmony 已有应用内 yesNo 对话框，行为等价；"浮条收纳时仍可用"在应用内无意义。
- **OverlayInkPresenter 的 DIB 直写/延迟统计/跳帧、CommittedInkLayer 瓦片、StrokeRenderer LRU+分块缓存、PalmRejector 静态默认、LiveStrokes 类型收紧、TryGetTouchPair/CancelTouchInk 免分配**：全部为 Win2D/UpdateLayeredWindow 性能管道，不改变墨迹语义；harmony Canvas 路径无需复刻（荧光笔 Winding 单次填充与 nib 渲染的**语义**已由 SceneBuilder.nibTransform/现有填充承担）。
- **BoardGesturePanel / BoardPenPalette 面板形态**：其设置项在 harmony 设置面板已覆盖；弹出形态差异非缺口。

## 4. 移植优先级

- **P0（正确性、代价小，先做）**：G4（撤销语义 3 处对齐）、G5（识别加固——防手写被替换，纯 core）、G13（schema 补 Nib 字段，保双向存档兼容）、G8（宽度互换，依赖已同步的 highlighterWidth）。
- **P1（SELECTION.md 主特性集）**：G1（套索擦）→ G2（元素选区桥）→ G3+G15+G16（选区命令与工具条 + 入口按钮）→ G10（画板页语义）。G2 是 G1/G3 在图片/视频上的放大器，宜同批。
- **P2**：G6（落点动画，书写手感）、G7（橡皮预览）、G11（元素撤销入栈）、G12（视频插入，依赖 G2 的元素桥）、G14（装饰细节）。
- **P3**：G9（控制器回放——已有独立回放窗可用，融合优化）、G17（轮询节奏对齐）。

## 5. 附注：对向量化移植契约的输入

本轮新增 Core 测试是天然的 parity/vectors 黄金向量来源（CLAUDE.md 契约）：LassoGeometryTests、SelectionFrameTests、StrokeEraserTests、LineRecognitionTests、TapDotTests、ToolbarLayoutTests、SolidShapeTests、ShapeDrawingSemanticsTests、StableInkTests、InteractionAndNibTests、StrokeCacheVersionTests、EraserPreviewTests、WritingStyleTests（tests/InkCanvas.Core.Tests/）。G1-G6 落地时应同步抽向量双向跑；docs/SELECTION.md 与 docs/PERFORMANCE.md 是行为规格的一手描述。
