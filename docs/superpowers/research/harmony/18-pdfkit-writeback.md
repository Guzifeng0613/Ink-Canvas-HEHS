# 18 — HarmonyOS PDF Kit（@kit.PDFKit）写回能力调研

- 日期：2026-11-19。对照基线：`src/InkCanvas.Pdf/`（PdfInkCodec / PdfAnnotationFile / PdfPageGeometry / PdfPageRange / PdfContinuousLayout）。
- 方法：① 本地 SDK 类型声明逐行通读（**权威**）：`E:\EcoStudioCLI\command-line-tools\sdk\default\hms\ets\api\@hms.officeservice.pdfservice.d.ts`（4082 行）、`...\hms\ets\api\@hms.officeservice.PdfView.d.ets`（1582 行）、`...\hms\ets\kits\@kit.PDFKit.d.ts`；② 官方指南/参考镜像（github.com/yonglunyao/harmonyos-docs-fetcher，快照 2026-03-01，覆盖至 26.0.0；26.0.1 Beta1 内容以本地 d.ts 为准）；③ 本仓 vendored KB `dev/references/pdf-kit.md`。
- 关键事实：PDF Kit 是 **HMS 专属**（openharmony ets/api 下无任何 pdf 声明，已 glob 验证）；`@kit.PDFKit` 导出 `pdfService`、`pdfViewManager`、`PdfView`（组件）。

## §1 本地 SDK pdfService 全量清单（SDK 26.0.1 Beta1，版本号=起始 API）

**PdfDocument**（5.0.0(12)，全部同步调用）：`loadDocument(path, password?, onProgress?)→ParseResult`；`releaseDocument()`；`saveDocument(path, onProgress?)→boolean`；`createDocument(w,h)`；`isEncrypted(path)`；`removeSecurity()`；`getPageCount()`；`getPage(i)→PdfPage`；`insertBlankPage(i,w,h)`；`insertPageFromDocument(doc,from,n,i)`（**注释不随页插入**）；`deletePage(i,n)`；`movePage(i,dest)`；`getFontWeight/setFontWeight`；`getMetadata()`；`convertToImage(path,format,onProgress?)`（PNG/BMP/JPEG）；书签 `getRootBookmark(s)/createBookmark/removeBookmark/insertBookmark`；页眉页脚 `add/get/has/removeHeaderFooter`；水印 `add/get/has/removeWatermark`；背景 `add/get/has/removeBackground`；`setPdfPassword(pw)`（5.0.5(17)，AES-256）；`searchKey(text,listener,options?)→Promise`（6.0.1(21)）；`getPixelMapWithPages(pageIndices,matrices,w,h,options?)`（26.0.0，多页拼图）。

**PdfPage**（12）：`getDocument()`；**`getAnnotations()→PdfAnnotation[]`**；**`addAnnotation(PdfAnnotationInfo)→PdfAnnotation`**；`setAnnotation(anno,info)`；`removeAnnotation(anno)`；`getIndex/getWidth/getHeight`；`setBox/getBox(BoxType)`（MEDIA/**CROP**/BLEED/TRIM/ART）；`setRotation/getRotation`（仅 0/90/180/270）；`getPagePixelMap()`；`getCustomPagePixelMap(matrix,isGray,drawAnnotations)`；`getAreaPixelMap(matrix,w,h,isGray,drawAnnotations)`；`getAreaPixelMapWithOptions(matrix,w,h,PixelOptions)`（5.1.0(18)：isGray/drawAnnotations/isTransparent）；`addTextObject(text,x,y,TextStyle)`；`addImageObject(path,x,y,w,h)`；`getGraphicsObjects()`；`deleteGraphicsObject(obj)`；`release()`；`getTextContent()→string`（6.1.0(23)）。**无 addPathObject——页面内容只能加文本/图片，不能加矢量路径。**

**PdfAnnotation**（12）：`type/uniqueId`；`getPdfPage/getAnnotationIndex/getAnnotationInfo`；`moveTo(x,y)`；`isMarkup()`。
**PdfAnnotationInfo 基类**（12）：`type`、`uniqueId`、`content?`、`modifiedTime?`、`border?(PdfBorder: style/width/color)`、`flag?(INVISIBLE/HIDDEN/PRINTED)`、`title?`、`opacity?(0..1)`、`subject?`、`creationDate?`、`action?`（5.1.0(18)，**链接只可获取不可添加/编辑**）。**字段封闭——无自定义键。**
**13 个子类**（12）：Text（iconName/state/x/y/color?）、Link（l/b/r/t）、FreeText（x/y/w/fillColor/textStyle/align）、Square、Oval、Polygon（vertexes）、Line（起止点+端样式）、Polyline、Highlight/Underline/Strikethrough（quadPoints）、**Ink（`inkPoints: PdfPoint[]` + `lineColor?`，仅此两项）**、Stamp（imagePath+l/b/r/t）。
**其余**：Bookmark CRUD + DestInfo（12）；Metadata（12）；GraphicsObject/TextObject/ImageObject（12）；FontInfo/HeaderFooterInfo/WatermarkInfo(文本/图片)/BackgroundInfo/PdfPoint/PdfRect/PdfMatrix(x,y,w,h,rotate)/PixelOptions(18)/PdfBorder/TextStyle/BookmarkInfo；PdfAction/GoTo/Hyperlink（18）；SearchResultData/SearchOptions（21）；枚举 ParseResult/PageLayout/PageFit/RotationAngle/BoxType/GraphicsObjectType/TextAnnotationState/HighlightMode/AlignmentType/LineEndStyle(10)/ImageFormat/**AnnotationType(UNKNOWN/TEXT/LINK/FREETEXT/LINE/SQUARE/OVAL/POLYGON/POLYLINE/HIGHLIGHT/UNDERLINE/STRIKETHROUGH/STAMP/INK=15/POPUP)**/BorderStyle/AnnotationFlag/FitMode/Watermark*/Background*/CharsetType（均 12）。

**pdfViewManager.PdfController**（PdfView.d.ets；异步）：`loadDocument(path,pw?,initPage?,onProgress?)→Promise<ParseResult>`；`loadDocumentFromMemory(ArrayBuffer,...)`（26.0.0）；`releaseDocument()`；`saveDocument(path,onProgress?)→Promise<number>`（1 成功/0 失败）；缩放 `setPageZoom/getPageZoom`、`set/Min/MaxZoom`（6.1.0(23)）；布局 `setPageLayout(单/双)`、`setPageContinuous/isPageContinuous`（**仅纵向连续**）、`setPageFit`、`setPageSpacing`；`goToPage/getPageIndex/getPageCount`；`setPageRotation/getPageRotation(pageIndex)`；`setDisplayDirection`（6.0.0(20)）；`setNestedScroll`（6.0.2(22)）；`setRenderMode(DEFAULT/DARKMODE)`（26.0.0）；高亮 `setHighlightRects/setHighlightText`、`setHighlightRectsWithColors(HighlightRect[])`（26.0.1）；批注 `enableAnnotation(type,color?)`/`addMarkupAnnotation`/`disableAnnotation`/`deleteSelectedAnnotation`/`updateMarkupAnnotation`；**手写 `enablePen(PenStyle{type?,lineColor?,lineWidth?,opacity?})`/`disablePen`/`enableEraser`/`disableEraser`/`changePenStyle`（全部 26.0.1）**；坐标互转 `viewPointToPDFPoint/pdfPointToViewPoint/viewRectToPDFRect/pdfRectToViewRect/getPageIndexFromViewPoint`（26.0.0）；`searchKey/clearSearch/get/setSearchIndex`；监听 scroll/pageChanged/scale/textSelected/annotationSelected/imageSelected/actionClick/**annotationChanged(ADD/MODIFY/DELETE)**/pageCountChanged；`getPagePixelMap(pageIndex)` 缩略图。**PdfView 组件**属性：controller/pageLayout/isContinuous/showScroll/pageFit。SupportedAnnotationType：12 起有 FREE_TEXT…STRIKETHROUGH，**STAMP 与 INK 为 26.0.1 新增**。

## §2 官方文档关键引文（镜像快照 + 当前官方 URL）

- 简介（https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pdf-kit-overview-0000001820880921）：「pdfService提供了加载和保存PDF文档、在PDF页面中添加文本内容、图片、批注、页眉页脚、水印、背景图片、书签、判断PDF文档是否加密及删除文档加密等相关的功能」；能力表：pdfService 支持「添加、删除批注」「管理书签」「添加、编辑、删除PDF页」等，PdfView 支持预览/搜索/批注/回调；约束：「当前PDF Kit仅支持中国境内（香港特别行政区、澳门特别行政区、中国台湾除外）」「支持ARM模拟器，不支持x86模拟器」。
- 批注指南（https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pdf-add-txt-img-annot）：「支持编辑PDF页面内容，包括：添加、删除文本。添加、删除图片。添加、修改、删除批注。…批注提供了多种风格样式…共13种。」示例流程 loadDocument→getPage→addAnnotation→saveDocument→releaseDocument。
- saveDocument 参考（https://developer.huawei.com/consumer/cn/doc/harmonyos-references/pdf-arkts-pdfservice）：「由于文档不可同时读写，如果需要覆盖回原文档，请创建临时文档作为过渡。」模块头注：「对PDF文件进行编辑操作后，需要调用saveDocument接口将PDF文件保存，确保编辑操作生效。」官方示例=copyFileSync 到 tempDir → loadDocument(临时) → saveDocument(原路径)——**与 C# 原子临时文件写回同构**。
- PdfView 批注指南：交互式批注「进入批注模式，目前支持高亮、下划线和删除线类型批注」（26.0.1 前）；26.0.1 d.ts 在 SupportedAnnotationType 增加 STAMP/INK 并新增 enablePen/eraser。手写保存产物：d.ts 将 INK 类型与 enablePen 同版本加入，推断**生成标准 PDF 墨迹批注、经 saveDocument 持久化**——[未验证]（无真机/26.0.1 文档原文）。
- (c) 自定义批注类型/自定义键：文档与 d.ts 均无——PdfAnnotationInfo 字段封闭，无任意字典/流写入。(d) 外观流（/AP）：无 API，由 Kit 保存时内部生成。[未验证] 生成质量。(e) 增量保存：无任何 API 或文档提及；saveDocument 输出到指定沙箱路径（内部是否增量未公开）[未验证]。
- 加密（…/pdf-arkts-pdfservice）：`isEncrypted(path)` / `removeSecurity()`；loadDocument 带 password，ParseResult 含 PARSE_ERROR_PASSWORD / **PARSE_ERROR_CERT**（证书错误，但无显式签名/SigFlags 查询 API）。

## §3 判定表：C# 能力 → HarmonyOS 等价

| C# 基线能力 | HarmonyOS PDF Kit | 判定 |
|---|---|---|
| PdfInkCodec：zlib 二进制墨迹状态（LayerOrder/ShapeSource/ObjectFrame/Nib 等） | 编解码纯逻辑可移植；内存级原始 deflate/inflate 可用：`@ohos.zlib` 的 `Zip` 类（API 12+，`deflate/inflate(strm,flush)` 于 ArrayBuffer/ZStream） | **可移植**（逻辑层） |
| 自定义批注对象 `/InkCanvasVersion`=1 + `/InkCanvasData`(FlateDecode 流) 往返 | 无自定义对象/流/键 API；InkAnnotationInfo 仅 inkPoints+lineColor（基类另有 content/subject/title/border/opacity 字符串与数值） | **不可用** |
| 打开源文件（密码、只读检测、签名检测 /SigFlags） | loadDocument(password?) + isEncrypted + PARSE_ERROR_PASSWORD/CERT；无只读位、无签名查询（removeSecurity 是删锁不是检测） | **部分**（密码✓，只读/签名✗） |
| 提取既有 InkCanvas 墨迹批注 | getAnnotations()+getAnnotationInfo() 可读 INK 类型点列/颜色 | **部分**（仅标准墨迹，扩展属性丢失） |
| 渲染 /AP 外观流（矢量、ExtGState 透明、Clipper2 胶囊并轮廓） | 无外观流控制；保存时 Kit 内部生成；替代：addImageObject 贴 PNG（有透明）；不能加矢量路径对象 | **不可用**（矢量外观）；栅格替代可行 |
| 原子临时文件保存 | 官方推荐同构模式：复制到 tempDir→load→saveDocument(原路径)；「文档不可同时读写」 | **可用** |
| 外部修改检测（length+mtime） | Kit 无；`@ohos.core.file` fs.stat 自行实现（沙箱内） | **可用**（Kit 外） |
| 页面几何 CropBox/UserUnit/rotation | getBox(BOX_CROP)/getWidth/getHeight/getRotation；**无 UserUnit**；旋转仅 0/90/180/270；PdfRect 单位 pt | **部分** |
| 书签/大纲 | 全 CRUD（create/remove/insert + DestInfo） | **可用** |
| 连续布局 | PdfView setPageContinuous(true)（纵向）；自绘则 getPagePixelMap/getAreaPixelMap + PdfContinuousLayout 数学可移植 | **可用** |
| PdfPageRange 页范围 | getPage(i)/getPageCount 逐页 | **可用**（逻辑可移植） |
| PdfView 手写批注+保存（系统能力） | enablePen/enableEraser/changePenStyle（**26.0.1 Beta1 专属**）+ saveDocument | **Beta-only**，API 24/26.0.0 Release 无 |

## §4 可行性阶梯

1. **今天就能做（API 24 min 全兼容，成员 ≤(23)）**：PdfInkCodec → harmony/core（@parity + 黄金向量，zlib 用 `zlib.createZipSync()` 流式 deflate/inflate，异步包装）；PdfPageGeometry（去 UserUnit 分支或按 1 处理）、PdfPageRange、PdfContinuousLayout 纯数学移植；读取：loadDocument（密码）/getPage/getBox/getRotation/getAnnotations；渲染：getPagePixelMap/getAreaPixelMapWithOptions（可带 drawAnnotations）成 PixelMap 供自绘层或导出。
2. **写回 MVP（API 24 可发布）**：每笔笔画一条标准墨迹批注 `InkAnnotationInfo{inkPoints, lineColor}`（宽度/透明度借基类 border.borderWidth / opacity），临时副本原子保存 + fs.stat 变更检测。**保真限制**：单路径等宽、无 Nib/Taper/ShapeSource/LayerOrder/ObjectFrame；外观由 Kit 生成 [未验证] 渲染保真。
3. **无损可编辑往返（推荐）**：以 **sidecar `.icink`**（本仓既有格式，存应用沙箱或 PDF 同名伴档，键=文件哈希/mtime）为权威可编辑状态；PDF 内标准墨迹批注作为「任何阅读器可见」的便携层。完全规避自定义键缺失。
4. **栅格兜底**：自渲染墨迹 → PixelMap → addImageObject 压页（烧进页面内容，非批注），适合「导出定稿」；convertToImage 可整档转图。
5. **需自建 PDF 写入器才有的**（自定义对象/流、/AP 控制、增量保存、矢量路径对象）：工程量大（xref/对象图/解析器），相对 sidecar 无收益——**不建议**。
6. **等待华为**：以上 5 的能力无公开路线图证据 [未验证]。26.0.1 Beta1 已给出方向（enablePen=系统手写→INK 批注），若 Release 化可直接复用。
7. **26.0.0+/Beta 专属需守卫**：坐标互转、loadDocumentFromMemory、getPixelMapWithPages、setRenderMode（26.0.0，targetSdk 26 可用、min 24 需 canIUse 守卫）；enablePen 系（26.0.1，**仅开发尝鲜，生产不上**，遵守 CLAUDE.md 26.0.0 Release 工具链规则）。

## §5 建议与工作量

- **P1（~3–5 天）**：harmony/core 落 PdfInkCodec（@parity 锚 + 向量双向跑）+ 几何/页范围/连续布局数学；harmony/platform 落 PdfFile 包装（load/isEncrypted/密码/页面枚举/几何/getAnnotations 读侧）。
- **P2（~3–4 天）**：写回：标准墨迹批注 + 临时副本原子保存 + stat 变更检测 + sidecar `.icink`（权威层）；`parity/api-matrix.md` 登记 API 12/18/21/23/26 分层与 canIUse 守卫。
- **P3（可选，~1–2 天 + 真机）**：26.0.1 enablePen 实验（Beta 分支，不进生产）；真机验证 Kit 生成墨迹外观的宽度/透明度保真、大课件 PDF saveDocument 耗时与体积（**全部未验证**）。
- **风险与注意**：① PDF Kit 仅中国大陆可用——功能需 `canIUse('SystemCapability.OfficeService.PDFService.Core')` 探测并降级（如仅 sidecar 模式）；② insertPageFromDocument 丢注释（影响页重组功能）；③ 无 UserUnit（CAD 类大图 PDF 换算会偏差）；④ vendored KB `dev/references/pdf-kit.md` 描述的 `pdfService.init()/createPdfViewManager()` **与真实 SDK 不符**（d.ts 无这些成员，实际为 `new pdfService.PdfDocument()` / `new pdfViewManager.PdfController()`）——该页建议修订，以本地 d.ts 为准。
