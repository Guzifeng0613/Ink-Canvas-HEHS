# 17 — 演进缺口第二轮（delta 2：报告 16 之后的新增工作树内容 → HarmonyOS）

- 日期：2026-11-19。方法：`git status -uall`（src/tests 全量未跟踪清单）+ `git diff`（工作树 vs HEAD 90b38a8，src 下 51 文件 / +11203−2346）与报告 16 覆盖集、已同步清单逐项差分；新增文件通读，harmony/ 侧逐点验证（含 harmony 工作树自身未提交改动）。
- 范围：**仅 delta**。报告 16 已分析的（InkController/BoardService 基础改动/三渲染器/PptInterop/识别/选区几何/InkDocument/StrokeGeometry/StrokeAttributes/TwoFinger/PalmRejector/AppSettings 主体/OriginalMenuCatalog/TaperProfile 及其 §1.4 全部新 UI 文件）与已同步清单（TaperKind 0..6 … PiP countdown）不再重复。
- 缺口编号 **D1…**，避免与 G1–G17 混淆。

## 1. Delta 清单

### 1.1 全新项目（10 个，均未跟踪）

| 项目 | 文件 | 性质 |
|---|---|---|
| `src/InkCanvas.AnalyticGeometry/` | MathExpression / PlotCompiler / PlotDefinition / PlotSampler | 纯数学：公式绘图内核（Desmos 类） |
| `src/InkCanvas.Modules.AnalyticGeometry/` | GeometryModule/Window、FormulaKeyboard、HandwritingInput、PlotPreview/Shapes、module.json | 解析几何模块壳 + WinUI 窗口 |
| `src/InkCanvas.Pdf/` | PdfAnnotationFile / PdfInkCodec / PdfPageGeometry / PdfPageRange | PDF 批注内核（读改存往返） |
| `src/InkCanvas.Modules.Pdf/` | PdfModule/Window/Rasterizer/Diagnostics + 5 份许可证 | PDF 模块壳（PdfSharp + PdfPig + Clipper2） |
| `src/InkCanvas.Modules.ScreenTracking/` | ScreenContentReader(579)、OfficeScreenView、ScreenAnnotationCoordinator/Memory、UnchangedRegionInk、ContentPageVariants | 屏幕内容跟随模块（UIA/COM/Win32） |
| `src/InkCanvas.Extensibility/` | Contracts / PluginWire | 扩展契约 + 管道线协议 |
| `src/InkCanvas.Extensions/` | PluginManager / ModuleRuntime / ExtensionPackages | 进程外插件管理 + 进程内模块运行时 + Extensions.json |
| `src/InkCanvas.Host.Contracts/` | CanvasTools / DocumentWorkspace / IScreenTrackingHost / ScreenViewSnapshot | 宿主导出给模块的 API 面 |
| `src/InkCanvas.PluginHost/` | Program.cs | 插件 worker 进程（命名管道 + ALC） |
| `tests/InkCanvas.PluginFixture/` | FixturePlugins.cs | 插件系统测试夹具 |

### 1.2 Core/Ink/Features 新逻辑文件（报告 16 未涉及）

- `Core/Imaging/SvgImage.cs`（79）— SVG 读入/命名空间修复/viewBox·单位换算（px/pt/pc/in/cm/mm）/svgz。
- `Core/Ink/ShapeSource.cs`（4）— `(GroupId, Provider, Definition)`：可选形状共享的公式来源（InkStroke 新字段，**.icink schema**）。
- `Core/ScreenTracking/ScreenInkClip.cs`（34）+ `Host.Contracts/ScreenViewSnapshot.cs`（Core.ScreenTracking 命名空间，72）— 视口/遮挡像素裁剪；快照有效性、ContentToScreen、ScrollOffset、FixedScreenRegions（页眉/侧栏判定）。纯逻辑，服务于 Windows 覆盖层。
- `Ink/InkLayers.cs`（151）— **图层 arrange 系统**：`LayerAction{BringToFront,MoveForward,MoveBackward,SendToBack}`、`DrawingObjects`（墨迹+元素统一背-前序）、`LayerVersion/NextLayerOrder`、`CanArrangeSelection/ArrangeSelection`（ShapeSource 组整体排名、单撤销单元）、`SetCloneLayerOrder`。
- `Ink/DrawingObject.cs`（18）— Stroke/Element 统一 Z 序条目（图片最底、视频次底、无序墨迹最顶的默认序）。
- `Ink/CanvasPreview.cs`（28）— `SetExtensionPreview(owner, strokes)` 扩展预览笔画 + `ShapeMembers/ExpandShapeSelection`（按 GroupId 扩选）。
- `Ink/Rendering/ImageSceneRenderer.cs`（140）— 嵌入图像共享缓存解码绘制（位图 EXIF/色彩管理 + CanvasSvgDocument 矢量路径 + 失效重试抑制）。
- `Features/Tools/ClassroomRoster.cs`（55）— 名单文件解码（UTF-8 失败回退 GB18030/936）+ CSV/TSV 解析（引号转义、姓名/名字/name 列识别）。
- `Ink/ISelectionElement.cs` 增长 — `LayerOrder`；新 `IImageSelectionElement { ImageData, ImageContentType, Opacity }`。

### 1.3 App/Controls/Platform 新文件（报告 16 §1.4 之外的 5 视图 / 6 服务 / 1 主题 / 2 平台）

- Services：`ImageImport`（17 种扩展名；SVG/svgz、WebP(Libwebp→PNG)、ICO 取最大帧、EXIF、压缩>1920×1080 降采样、非 PNG/JPEG 归一化、32MiB 上限、半视口居中放置）、`MediaImport`（13 种视频扩展、512MiB、海报帧、方向互换、ContentType 映射）、`ScreenTrackingHost`（IScreenTrackingHost 装配）、`ShellController.CanvasTools`（ICanvasToolsHost：CommitShape 提交解析几何笔画、LayerOrder 继承被替换组、"插入/修改解析几何"撤销）、`ShellController.Documents`（IDocumentWorkspaceHost：PDF 文档工作区接管浮条/覆盖层，board↔document 模式切换）、`ShellController.Extensions`（扩展命令面板 + 插件 canvas-write 笔画经逆视图变换提交、可撤销）。
- Views：`BoardDrawingLayers`（按 DrawingObjects 把墨迹 run 与原生视频视图按 ZIndex 交错；场景存图 SaveSceneAsync）、`ClassroomControls`（TouchIconButton 课堂按钮辅助）、`MinSizeHook`（WM_GETMINMAXINFO 子类化）、`NamesInputWindow`（名单编辑窗，**持久化 Names.txt**，实时 ClassroomRoster.Parse 计数）、`SettingsWindow.Extensions`（"模块与插件"设置页）。
- `Controls/Theming/PopupBrushes.cs` — 上游 WPF Light/Dark-PopupWindow.xaml 精确色值（窗口级主题解析）。
- Platform：`SoftwareLauncher`（EasiCamera 注册表双视图查找+sweclauncher 启动；Desmos 打开 URL）、`NonActivatingWindow`（WS_EX_NOACTIVATE+TOOLWINDOW 子类，拒绝激活但正常收输入）。
- Assets：Board/Desmos.png、EasiCamera.png、Licenses/{Libwebp,PhotoSauce*}.txt、SegoeMDL2Assets.ttf、TimerDownNotice.wav。

### 1.4 已跟踪文件超出报告 16 集合的新改动

- `Core/History/UndoStack.cs`（+17）— `Capture()/Restore(UndoHistoryState)` 整栈快照；消费者 `InkController.RestoreScreenContext`(:130) 与 ScreenAnnotationMemory（屏幕跟随每页记忆）。
- `Core/Replay/InkReplaySession.cs`（+26）— `WriteVisibleAt(elapsed, result)` 可复用回放帧（部分笔画保持身份、RestorePrefix 增量）。
- `Features/Persistence/Stores.cs`（+17）— .icink StrokeModel 增 `LayerOrder/ShapeSource`（Nib/NibRotation/ObjectFrame 在已同步项内）。
- `Features/Tools/Tools.cs`（+63）— LotteryService：`Draw(count)` 多人抽取（Fisher-Yates 抽取池）、`PoolSize/RemainingCount`、`History`（最近 20 次 `LotteryDraw`）、PeopleCount setter 语义、LoadNames 归一化并清历史。
- `Features/Board/BoardService.cs` 新增段 — 资产 `LayerOrder`/图片 `Opacity`、`InsertPagesAfterCurrent(批量页)`（PDF 导入内核：独立控制器+图片校验、原子插入）、`ReplaceCurrentImages`、`BoardService(initialController)` ctor + GetSelectableElements 桥（屏幕画布也挂 BoardService：AppServices.ScreenImages）、StrokeFile 增 LayerOrder/ShapeSource。
- `Core/Settings/AppSettings.cs` — 增 `experimental` 9 键（ScreenContentTracking/FollowScroll/FollowZoom/MaskFixedRegions/RememberPages/PreserveUnchangedRegions/Word/PowerPoint/Edge）；**删** hyperbolaAsymptoteOption（harmony 从未实现该键 → 无分歧）。harmony AppSettings.ts:141/257 **已含全部 9 键**（此前已同步，勿报缺口）。
- csproj/slnx — App 3.5.0、Libwebp 包、三模块条件引用 + Modules/PluginHost 拷贝目标、XBF/pri 发布修复；Controls 增 Extensibility 引用（浮条扩展命令）。
- 视图接线 — LotteryWindow（名单管理/一次多人/剩余人数/近 3 次历史）、SettingsWindow（"实验性设置"+"模块与插件"两类目）、FloatingBar（更多菜单追加 ExtensionCommands）、BoardWindow（BoardDrawingLayers + DrawingObjects 消费）、SelectionToolbar（**:180-183 四个图层按钮** 置于顶层/上移一层/下移一层/置于底层 — 报告 16 描述中无此项）。
- **harmony 工作树自身**（未提交）：`InkStroke.ts` +LayerOrder/ShapeSource、`IcinkCodec.ts` +LayerOrder/ShapeSource、`BoardPackCodec.ts` +LayerOrder（笔画与资产）——schema 层同步**已在飞行中**；BoardPackCodec 尚缺 ShapeSource，控制器/元素层未动。
- `parity/manifest.json`、`parity/schemas/`、`parity/vectors/` **零改动**——本轮全部新文件尚未入 parity 清单（CLAUDE.md 契约的维护欠账，见 §3 附注）。

### 1.5 Diagnostics 27 + tests 24 + PluginFixture（名称级）

- Diagnostics（App 内回归 harness）：Activation/BoardFeature/BoardPopup/BoardWindow/ClassroomWindow/EraserPreview/Eraser/Extensions/Geometry/ImageObjects/Interaction/LassoEraser/LayerOrder/MediaFormats/Pdf/PenOptions/PerformanceCheck/Playback/Rendering/ScreenTracking/SelectionDrag/Selection/SelectionWindow/Settings/ShapeTools/Workspace/WritingPerformance。
- Core.Tests 新增 24：AnalyticGeometry/ClassroomTools/DrawingLayer/EraserPreview/ExtensionRuntime/ImageSelection/InteractionAndNib/LassoGeometry/LineRecognition/MediaImport/PdfDocument/PdfImport/PdfTouch/ScreenTracking/SelectionDrag/SelectionFrame/ShapeDrawingSemantics/SolidShape/StableInk/StrokeCacheVersion/StrokeEraser/TapDot/ToolbarLayout/WritingStyle；csproj 增引 Modules.ScreenTracking/Extensions/Pdf/AnalyticGeometry/PluginFixture/PluginHost。全测试无 Win2D 依赖（纯逻辑可跑）。

## 2. 逐项分析

### 2.1 图层 arrange（D1 的本体）
`InkLayers/DrawingObject/LayerOrder`：墨迹与图片/视频共用一条背-前序（默认序：图片 < 视频 < 墨迹，见 DrawingObject.SortOrder long.MinValue 分档）；arrange 时 ShapeSource 组按组最值整体排名；撤销为 DelegateAction 重放序数组；克隆物追加到队尾。SelectionToolbar 四按钮 + BoardWindow/BoardDrawingLayers 消费。**行为性 + 共享契约**（.icink StrokeFile.LayerOrder、board-pack 资产 LayerOrder——C# BoardPageFile 已写）。harmony：schema 在飞行中（未提交），`ArrangeSelection/LayerAction/DrawingObjects` 无对应（grep 零命中）。

### 2.2 图像对象升级
- SVG：SvgImage 规范化（尺寸/单位）→ ImageSceneRenderer 矢量绘制（帧变换缩放，不失真）→ 持久化仅存规范化 XML + ContentType `image/svg+xml`。**行为性 + schema**（BoardImageAsset.ContentType 已有，harmony BoardPackCodec 亦解析 ContentType；harmony 无 SVG 路径）。
- WebP：Libwebp 解码转 PNG 后入库（对 harmony 无意义——ArkTS Image 天生支持 webp——**仅"入库即 PNG"这一归一化策略可对齐**）。
- `Opacity`（0..1）+ `IImageSelectionElement.Opacity`：图片透明度，**schema 新字段**，harmony BoardPackCodec 未见 Opacity。
- ImageImport 的降采样/EXIF/ICO 最大帧/32MiB/半视口放置是导入策略（行为性，harmony 插图入口应对齐策略值）。

### 2.3 课堂名单 + 抽奖升级
ClassroomRoster（编码回退 + 表格姓名列识别）+ LotteryService 多抽/历史/池量 + Names.txt（用户数据目录持久化）+ NamesInputWindow/LotteryWindow UI。**行为性**；harmony `features/tools/LotteryEngine.ts` 仍为单词 guard 循环单抽、loadNames 仅按行 trim，无 CSV/编码/多抽/历史/名单文件。纯逻辑全部在 Core/Features，宜向量化。

### 2.4 PDF 模块
PdfAnnotationFile：不可变源快照 → 提取 InkCanvas 墨迹（`/InkCanvasVersion 1` + FlateDecode 的 PdfInkCodec 载荷）→ 回存为 Stamp 批注（外观流 = 渲染器同一多边形胶囊经 Clipper2 NonZero 并集，矢量可打印）；密码/只读/数字签名防护、外部修改检测、书签(≤5000/深24)、PdfPig 全文搜索(≤500)。PdfPageGeometry：CropBox/UserUnit/旋转 ↔ 96DIP 双向映射。PdfPageRange："1-5,8" 有界解析。ImportToBoardAsync 经 `BoardService.InsertPagesAfterCurrent` 批量建页。**功能整体为 Windows 桌面功能**；harmony 已有 `platform/pdf/PdfPageSource.ets`（课件=PDF 逐页+每页墨迹，manifest 与 WinUI3 PPT 存档同格式）作为底座。harmony 侧批注回写 PDF 需 PDF Kit（@ohos.pdfService，API 20+）或三方库，可行性需另研。

### 2.5 解析几何模块
纯内核：MathExpression 有界文法（48 深度/512 节点/4096 字符、固定 22 函数、隐式乘法 `2x`、π/e/θ/上标归一化）；PlotCompiler 五类（FunctionY/FunctionX/Implicit/Parametric/Polar）+ 变量域校验；PlotSampler 640×480 自适应采样 + 隐函数根拼接（Stitch 按 1024 网格去重）。提交路径：ShellController.CanvasTools.CommitShape → 笔画带 `TaperKind.None` + LayerOrder + **ShapeSource(GroupId=Provider:"analytic-geometry", Definition=公式)**；选中后 `ExpandShapeSelection` 按组扩选、arrange 按组排名、再编辑重提交（"修改解析几何"撤销）。模块 UI（公式键盘/手写输入/预览窗）为 WinUI。**内核 100% 纯逻辑可向量化**；UI 需 ArkTS 重写。

### 2.6 扩展/模块运行时
Extensibility（契约 v1、能力位 Notifications/CanvasWrite、1MiB 消息上限、结果强校验）→ Extensions（ModuleRuntime 进程内 ALC+依赖序+导出+故障回滚；PluginManager 一插件一 worker 进程，命名管道 CurrentUserOnly，5s 超时，父进程监视）→ PluginHost worker → Host.Contracts 宿主面 → SettingsWindow.Extensions + FloatingBar 入口 + `Extensions.json`（DisabledModules/EnabledPlugins）。**Windows 桌面架构**：ArkTS 无运行时加载外部包机制（HSP/HAR 皆编译期），无等价物也无必要；Extensions.json 因无对应端**不构成共享契约**。

### 2.7 屏幕内容跟随
OfficeScreenView（Word `_WwG`/PPT `paneClassDC` AccessibleObjectFromWindow + ROT GetActiveObject + 放映 HWND 匹配）→ ScreenContentReader（UIA 滚动/缩放/锚点/遮挡）→ ScreenViewSnapshot/FixedScreenRegions → ScreenAnnotationCoordinator/Memory（**每文档每页墨迹记忆**，消费 UndoStack.Capture/Restore + RestoreScreenContext）→ UnchangedRegionInk/ScreenInkClip 像素保留/裁剪 → ScreenImages（屏幕画布 BoardService，图片也可上屏）。全部依赖跨应用窗口读取，HarmonyOS 沙箱下不存在此能力。experimental 9 键 harmony 已同步（开关恒 off 即可）。

## 3. 移植缺口表

| # | 缺口 | C# 依据 | harmony 归属 | 类型 | 量级 | 优先级 |
|---|---|---|---|---|---|---|
| D1 | 图层 arrange 端到端：DrawingObjects 统一 Z 序、ArrangeSelection 四命令（组排名/单撤销/克隆置顶）、LayerVersion、SelectionToolbar 四按钮、渲染按 LayerOrder 与视频交错 | InkLayers.cs; DrawingObject.cs; SelectionToolbar.cs:180; BoardDrawingLayers.cs | ink + core/selection + controls + entry | 行为 | M | **P1** |
| D2 | .icink/board-pack schema 收尾：stroke `ShapeSource` 补入 BoardPackCodec（IcinkCodec 已加）；资产/笔画 LayerOrder 落库与默认序语义；图片 `Opacity` 字段 | Stores.cs:79-99; BoardService.cs StrokeFile:784-798; harmony BoardPackCodec.ts 工作树 | core/persistence + features/board + parity/schemas | **契约** | S | **P0** |
| D3 | SVG 图像支持：SvgImage 等价规范化（单位/viewBox/svgz）+ ContentType=image/svg+xml 入库 + 矢量绘制路径 | SvgImage.cs; ImageImport.cs:59-64; ImageSceneRenderer.cs:54-60 | core/imaging(新) + features/board + entry | 行为+契约 | M | P1 |
| D4 | 图片透明度 Opacity + 导入策略对齐（EXIF/降采样阈值/半视口放置/归一化 PNG） | BoardImageAsset.Opacity; ImageImport.cs:99-143 | features/board + entry | 行为 | S | P2 |
| D5 | 抽奖/名单升级：Draw(count)/PoolSize/RemainingCount/History、ClassroomRoster CSV/编码、Names.txt、入口 UI | Tools.cs:82-154; ClassroomRoster.cs; LotteryWindow diff | features/tools + entry | 行为 | S-M | P2 |
| D6 | 画板批量导页 `InsertPagesAfterCurrent`（独立控制器+图片校验、原子插入）——PDF 导入内核，可独立用于任意页导入 | BoardService.cs InsertPagesAfterCurrent | features/board | 行为 | S | P2 |
| D7 | 解析几何：MathExpression/PlotCompiler/PlotSampler 内核 + ShapeSource 组选区（ExpandShapeSelection/组排名/CommitShape 语义）+ 简化提交入口 | AnalyticGeometry/4 文件; CanvasPreview.cs:19-27; ShellController.CanvasTools.cs:25-46 | 新 features 模块 + ink + entry | 行为(纯逻辑优先) | L | P3（纯逻辑+向量可先行 S） |
| D8 | PDF 批注/导入（提取-回写往返、PdfPageGeometry/PageRange、ImportToBoard）——依赖 PDF Kit 可行性调研 | InkCanvas.Pdf/4 文件; Modules.Pdf | platform/pdf + features | 行为(宿主) | L | P3 |
| D9 | InkReplaySession.WriteVisibleAt 帧复用（部分笔画身份保持 + RestorePrefix 增量）；语义上 harmony VisibleAt 已覆盖，此项为性能对齐 | InkReplaySession.cs:37-59 | core/replay | 性能/语义微差 | S | P3 |
| D10 | 模块/插件运行时与扩展命令面 | §2.6 全部 | — | Windows 架构 | — | **不移植**（见 §4） |

**parity 契约欠账（横切）**：本轮 10 个新项目 + 12 个新逻辑文件均未入 `parity/manifest.json`，`parity/schemas/` 未反映 LayerOrder/ShapeSource/Opacity；CLAUDE.md 规定增删功能文件须同步 manifest 并跑 `node tools/parity-check.mjs`。D2 落地时应一并补齐。

## 4. 无需 ArkTS 行动（附理由）

- **扩展/插件运行时全套**（Extensibility/Extensions/Host.Contracts/PluginHost/PluginFixture/SettingsWindow.Extensions/ShellController.Extensions/Controls.csproj 的 Extensibility 引用）：ALC、命名管道、进程隔离均无 ArkTS 等价物；HarmonyOS 扩展形态（如有）应为编译期 HSP，另行立项，非移植缺口。Extensions.json 不入共享契约。
- **屏幕跟随全套**（Modules.ScreenTracking 7 文件、ScreenTrackingHost、OfficeScreenView、ScreenInkClip、FixedScreenRegions、ScreenAnnotationMemory、UndoStack.Capture/Restore 的该消费方、AppServices.ScreenImages 屏幕图、OverlayHost/ShellController 相关接线、SettingsWindow"实验性设置"页）：跨应用 UIA/COM 读取在 HarmonyOS 沙箱不存在；experimental 9 键 harmony AppSettings.ts 已有（恒 off）。UndoStack.Capture/Restore 本体为纯 Core API，但当前唯一消费者是屏幕跟随——**随 D1 一并评估是否顺手移植**（供未来页级记忆复用），否则不单独立项。
- **SoftwareLauncher**：EasiCamera 为 Windows 注册表独占软件；Desmos 打开浏览器 harmony Index.ets:3051 已有等价。
- **NonActivatingWindow / MinSizeHook**：Win32 子类化解决 HWND 无 API 的问题；ArkUI 窗口原生具备非激活/最小尺寸语义。
- **PopupBrushes**：WPF 弹窗色值的 1:1 移植；harmony 主题由 parity/tokens.json 驱动——色值可作 tokens 输入素材，非行为缺口。
- **ClassroomControls**（TouchIconButton 装配辅助）、**BoardDrawingLayers 的 CanvasControl/run 缓存机制**（其*语义*——按 LayerOrder 与视频 Z 交错——随 D1 移植，Win2D 机制不复刻）、**ImageSceneRenderer 的解码/缓存机制**（ArkUI Image/PixelMap 天然承担；需要移植的是 D3/D4 的数据与策略）。
- **App.csproj 的模块拷贝/XBF/pri/字体/Libwebp 目标、Assets 资源与许可证、slnx、InkCanvas.slnx**：发布打包管道。WebP 解码包为 Windows 专属（harmony 原生支持 webp）。
- **27 个 Diagnostics + PluginFixture + ExtensionRuntimeTests + PdfTouchTests**（后者 import Platform.Windows）：Windows 回归 harness 与进程/管道测试。
- **LotteryWindow/NamesInputWindow/SettingsWindow.Extensions 的窗口形态**：内容随 D5 移植；独立 HWND 窗口壳无意义（ArkUI 面板/Sheet 原生）。

**黄金向量来源**（本轮新增纯逻辑测试，CLAUDE.md 契约；Core.Tests 无 Win2D 依赖可全跑）：AnalyticGeometryTests（D7 核心）、DrawingLayerTests（D1）、ClassroomToolsTests（D5）、ImageSelectionTests/SelectionDragTests（D2/D4）、MediaImportTests（ContentType/几何）、ScreenTrackingTests 的纯函数部分（ScrollOffset/FixedScreenRegions，若移植 D 系无关则仅存档）、ToolbarLayoutTests、PdfDocumentTests 中的 PdfInkCodec 编解码段（D8）；其余（LassoGeometry/SelectionFrame/StrokeEraser/LineRecognition/TapDot/SolidShape/ShapeDrawingSemantics/StableInk/InteractionAndNib/StrokeCacheVersion/EraserPreview/WritingStyle）报告 16 §5 已列，随对应 G 项落地。
