# 13 — 媒体与系统服务能力（HarmonyOS 知识库提取）

- 日期：2026-11-18
- 角色：只读研究员（仅提取 `D:\InkCanvas\.claude\skills\harmony-os\` vendored 知识库事实）
- 版本基线：HarmonyOS 26.0.0 / API 26（Release，2026-08-29）为主；HarmonyOS 6.1.1 / API 24（Release，2026-05-26）向下兼容。**26.0.1 Beta1（2026-11-10）不在生产基线内**，且 KB 明确写明 Beta 版 DevEco Studio 开发的应用不能上架应用市场。
- 指定范围：
  - `dev/references/pdf-kit.md`
  - `media/references/avplayer.md`
  - `media/references/audio-kit.md`
  - `media/references/image-kit.md`
  - `media/references/photo.md`
  - `dev/references/notification.md`
  - `dev/references/live-view-kit.md`
  - `system/references/background-task.md`
  - `system/references/share-kit.md`
  - `dev/references/account-kit.md`
  - `agc/references/distribution.md`
- 补充核对（同属 vendored KB，仅用于补齐指定文件未展开的交叉引用）：`dev/references/app-service-kits.md`（Preview Kit / Reader Kit）、`dev/references/pen-kit.md`、`media/references/graphics2d.md`、`system/references/file-management.md`（FilePicker）、`samples/references/media.md`、`dev/references/app-framework.md`、`dev/references/permissions.md`、`system/references/basic-services.md`、`system/references/device-info.md`、`dev/references/industry-practices.md`、根 `SKILL.md` / `dev/SKILL.md` / `media/SKILL.md` / `system/SKILL.md`。
- 证据标记：
  - **【KB】** 知识库原文明确写明；
  - **【推断】** 基于 KB 事实的工程判断，不是 KB 承诺；
  - **【未知】** KB 未写明，必须查官方 API 参考或实机验证。
- 验证边界：本次为静态阅读，**未安装/未调用 HarmonyOS SDK，未编译 HAP，未实机运行**；所有性能、时延、内存结论均为待验证假设。

---

## 0. 结论摘要（先看这里）

1. **PPT COM 不可行后的最佳替代是「应用内课件渲染 + 独立墨迹层」，不是 PdfView 直接叠加。** KB 的 PDF Kit 能 `renderPage(pageIndex, {width,height})` 得到 `image.PixelMap`（`pdf-kit.md:97-108`），这让我们可以把每一页当作位图自己管理翻页、缩放和坐标，墨迹层完全自绘，最接近现有白板/PPT 批注的心智模型。`PdfView` 组件只暴露 `onPageChanged` / `onLoadComplete`（`pdf-kit.md:115-135`），**没有文档化的滚动/缩放偏移、页面矩形、触摸事件或自定义叠层接口**，把它与墨迹层像素对齐风险高。
2. **PPT/PPTX 本身不能被 PDF Kit 打开。** PDF Kit 只处理 PDF；KB 没有设备端 PPT→PDF 转换 API。Preview Kit 能预览 `ppt/pptx`（`app-service-kits.md:136`），但它是「拉起系统预览界面」，KB 没有页面位图导出或嵌入批注接口。因此课件进入 HarmonyOS 的路径必须是：**用户在 PC 端导出 PDF/PNG 序列 → 应用内渲染**，或**服务端转换（KB 未涉及）**；不能假设端上直接解析 PPT。
3. **PDF Kit 的内置批注在 API 26 Release 仍不可用。** `enablePen`（自由绘制/橡皮擦）与 `setHighlightRectsWithColors`（多色临时高亮）只在 **26.0.1 Beta1** 出现（`pdf-kit.md:146-151`），Beta 不能上架；且 KB 没有笔迹数据模型、颜色/笔宽、保存/导出、撤销接口。生产方案必须是**自绘墨迹**。
4. **API 26 的 PDF 坐标转换是「锦上添花」，不是前提。** API 26 新增「视图坐标 ↔ PDF 页面坐标」双向点/矩形转换、二进制读取 PDF、自定义渲染风格（根 `SKILL.md:488-492`），但 **API 名与签名 KB 未给出**。API 24 完全没有这些能力，因此坐标变换矩阵必须由我们自己的 `harmony/platform` 维护，API 26 只做可选加速/校准。
5. **视频位置保持的可靠事实只有 AVPlayer 的 `currentTime` / `seek(ms)`。** `avPlayer.currentTime`、`avPlayer.duration`、`await avPlayer.seek(50000)` 有明确示例（`avplayer.md:50-59`）；`Video` 组件有 `onPrepared/onFinish` 和 `controller`（`samples/references/media.md:96-108`），但 KB **没有** `VideoController` 的 `currentTime/seek` 签名，也**没有** AVPlayer 的 `setSurfaceId`/离屏渲染说明。跨页保持位置需实机 spike。
6. **截图必须降级为「本应用内容/画布快照」，不能复刻 Alt+C 全屏抓取。** KB 没有任何三方应用可用的全屏/跨应用静默截图 API；`screenCapture` 只出现在 TestKit（`test-kit.md:100`、`testing.md:233-234`），`@ohos.screenshot` 不在 KB。可用积木是：自绘内容快照（API 名 **未知**）、XComponent surface → `image.createPixelMapFromSurfaceWithTransformation`（API 23，`image-kit.md:100-109`）、`image.createImagePacker().packToFile`（`image-kit.md:130-140`）、系统 Picker 保存/相册保存（`file-management.md:263-294`、`photo.md:58-74`）、Share Kit 分享出去（`share-kit.md:34-67`）。
7. **倒计时结束提醒用代理提醒（Reminder Agent）最可靠。** `reminderAgentManager.publishReminder(ReminderRequestTimer{triggerTimeInSeconds,...})` 由系统代理触发，应用不需要在后台运行（`background-task.md:153-206`）。API 26 给倒计时实例增加 `repeatInterval` / `repeatCount`（根 `SKILL.md:538,810`）。后台持续跑秒表则不可靠：短时任务最多 3 分钟（`background-task.md:18-24`），长时任务需前台通知 + `KEEP_BACKGROUND_RUNNING`，且没有专门的「计时」后台类型（`background-task.md:48-58`）。
8. **实况窗不适合做倒计时。** Live View 样式只有 `TRANSPORT / SPORTS / FOOD_DELIVERY / TAXI`（`live-view-kit.md:100-116`），没有计时/倒计时样式；权限表要求 `ohos.permission.NOTIFICATION_CONTROLLER`（`live-view-kit.md:165-179`），而 Notification 文档把该权限标为 `system_core`，仅系统应用（`notification.md:678-683`）——**两份 KB 冲突**，三方应用可用性必须实机验证。倒计时建议用「常驻通知 + 代理提醒」。
9. **自动更新只能交给应用市场。** 不能下载并安装 HAP 自更新；Windows 的 `UpdateService` / `ReleaseFeed` 整体作废。KB 记录 AppGallery Kit 有「应用市场更新功能：查询应用是否有可更新版本，应用内显示更新提醒」，支持 Phone/PC/2in1/Tablet/Wearable/TV（`monetization.md:215`），但 **API 名/签名/起始版本均未给出**，且仅中国境内、模拟器不支持（`monetization.md:262-265`）。
10. **本范围的能力没有任何可用的 `SystemCapability.*` 字符串。** KB 只在 AR Engine 给出 `canIUse("SystemCapability.AREngine.Core")`（`ar-engine.md:82`），在 XEngine 提到 `SystemCapability.Graphic.XEngine`（`arkgraphics-3d.md:187`），在 Enterprise Foundation 给出 `SystemCapability.PCService.EnterpriseDeployRestore`（`enterprise-foundation-kit.md:27`）。PDF/AVPlayer/Audio/Image/Photo/Notification/LiveView/Background/Share/Account/AppGallery **一个都没有**。因此本领域的运行时门控不能写 `canIUse`，只能用 API 存在性/try-catch + `deviceInfo.osFullName`/`deviceType`（格式与取值见 §15）。

---

## 1. 知识库覆盖度与证据等级

| 能力域 | 指定 KB 文件 | KB 明确写到 | 关键缺口 |
|---|---|---|---|
| PDF 渲染 | `pdf-kit.md` | `pdfService.init`、`createPdfViewManager`、页数/翻页/当前页、`renderPage`→PixelMap、`PdfView` 组件、API 23 文本/缩放、API 26 坐标转换、26.0.1 `enablePen` | 起始 API、页面尺寸/DPI、缩放/滚动偏移、渲染区域、透明背景、缓存/释放、批注保存/导出、PdfView 叠层与触摸 |
| 视频 | `avplayer.md`、`samples/references/media.md` | AVPlayer 创建/播放/暂停/停止/释放/seek/倍速/currentTime；`Video` 组件属性与 `onPrepared/onFinish`；API 23 软解/缩略图 | 视频 surface 绑定、离屏解码、`VideoController` 签名、无缝切源、后台音频策略 |
| 音频 | `audio-kit.md`、`samples/references/media.md` | AudioManager/VolumeManager、AudioRenderer/Capturer、音频焦点、SoundPool 短音频、API 23 系统音效/时延 | StreamUsage 全枚举、音量类型、SoundPool 起始版本、rawfile 播放的系统限制 |
| 图片 | `image-kit.md` | ImageSource/PixelMap/变换/元数据/打包、Surface→PixelMap（API 23）、格式表 | GPU 纹理、离屏绘制、PixelMap 生命周期/内存、包体限制 |
| 相册 | `photo.md`、`samples/references/media.md`、`file-management.md` | PhotoAccessHelper 查询/建相册/createAsset、PhotoViewPicker 选择/save、DocumentViewPicker save、grantUriPermission | 安全控件 SaveButton、批量保存、权限拒绝后的降级 |
| 通知 | `notification.md` | 授权/发布/渠道/级别/WantAgent/进度/角标/实况窗通知（系统）/API 23/26 变更、频次限制 | 自定义铃声 URI 要求、rawfile、通知点击冷启动参数 |
| 实况窗 | `live-view-kit.md` | 启动/更新/停止、4 种样式、API 23 地理触发、API 26 进度环、权限 | 计时样式、三方资格、API 24 可用性、状态机/限制 |
| 后台 | `background-task.md` | 短时 3 分钟、长时模式、requestContinuousTask、WorkScheduler、代理提醒、权限 | 三方长时任务审核口径、TASK_KEEPING 时长、提醒上限/精度 |
| 分享 | `share-kit.md` | ShareData/ShareController.show、文本/图片/文件、定向分享、API 23 读取分享记录 | 接收分享的 Want action/skills、截图监听、URI 转换 |
| 账号 | `account-kit.md` | 一键登录/静默登录/头像昵称/手机号、OpenID/UnionID、设备支持、AGC 配置 | 具体 ArkTS 方法签名、登出、隐私合规 |
| 分发/更新 | `distribution.md`、`monetization.md` | 上架流程/版本/审核/灰度/回滚、AppGallery Kit 更新能力（能力名） | 更新 API 名/签名/起始版本、应用市场详情页跳转 API |
| 预览/手写（补充） | `app-service-kits.md`、`pen-kit.md` | Preview Kit 支持 ppt/pptx/pdf 等；Pen Kit HandWriteComponent；API 23/26 变更 | 预览能否嵌入/导页、Pen Kit 数据导出与背景图 |

---

## 2. PDF Kit：课件批注的核心

### 2.1 明确 API 与签名

| API | 签名/用法 | 起始版本 | 证据 |
|---|---|---|---|
| 初始化 | `await pdfService.init()` | 【未知】 | `pdf-kit.md:13-19` |
| 创建视图管理器 | `await pdfService.createPdfViewManager(filePath): Promise<PdfViewManager>` | 【未知】 | `pdf-kit.md:25-32` |
| 总页数 | `await viewManager.getPageCount(): Promise<number>` | 【未知】 | `pdf-kit.md:41-43` |
| 跳页 | `await viewManager.goToPage(0)`；`goToNextPage()`；`goToPrevPage()` | 【未知】 | `pdf-kit.md:45-49` |
| 当前页 | `await viewManager.getCurrentPage(): Promise<number>` | 【未知】 | `pdf-kit.md:51-52` |
| 文本提取 | `await viewManager.getTextContent(pageIndex): Promise<string>` | API 23 | `pdf-kit.md:56-77` |
| 缩放范围 | `setMaxZoom(5.0)`、`setMinZoom(0.5)`、`setZoom(2.0)` | API 23 | `pdf-kit.md:79-91` |
| 页面渲染 | `await viewManager.renderPage(pageIndex, {width, height}): Promise<image.PixelMap>` | 【未知】 | `pdf-kit.md:93-108` |
| 组件 | `PdfView({ uri })` + `.onPageChanged((p)=>{})` + `.onLoadComplete((n)=>{})` | 【未知】 | `pdf-kit.md:110-136` |
| 二进制读取 | 新增支持从二进制数据读取 PDF | API 26 | 根 `SKILL.md:488-491` |
| 自定义渲染风格 | 新增自定义渲染风格 | API 26 | 根 `SKILL.md:488-490` |
| 坐标转换 | 视图坐标→页面索引；视图坐标↔PDF 页面坐标的点/矩形双向转换 | API 26 | 根 `SKILL.md:491-492` |
| 多区域拼图 | 多张页面指定区域转化为一张图片 | API 26（Beta1 收录） | 根 `SKILL.md:556,838` |
| 手写/擦除 | `enablePen`：在 PDF 界面自由绘制曲线、橡皮擦除 | 26.0.1 Beta1 | `pdf-kit.md:146-151` |
| 多色高亮 | `setHighlightRectsWithColors`：同时添加多个不同颜色临时高亮矩形 | 26.0.1 Beta1 | `pdf-kit.md:146-151` |

权限：PDF Kit 本身无特殊权限；访问文件需要对应的文件读取权限（`pdf-kit.md:138-144`）。

### 2.2 基本代码模式

```ts
import { pdfService, PdfViewManager } from '@kit.PDFKit';
import { image } from '@kit.ImageKit';

async function openDeck(path: string): Promise<PdfViewManager> {
  await pdfService.init();
  return await pdfService.createPdfViewManager(path);
}

async function renderPageAt(
  vm: PdfViewManager, page: number, w: number, h: number
): Promise<image.PixelMap> {
  return await vm.renderPage(page, { width: w, height: h });
}

async function readDeck(vm: PdfViewManager): Promise<void> {
  const n = await vm.getPageCount();
  for (let i = 0; i < n; i++) {
    const text = await vm.getTextContent(i); // API 23+
    console.info(`page ${i}: ${text.length} chars`);
  }
}
```

`PdfView` 组件模式：

```ts
PdfView({ uri: 'file:///data/storage/el2/base/files/lesson.pdf' })
  .width('100%')
  .height('100%')
  .onPageChanged((currentPage: number) => { /* 只给页号 */ })
  .onLoadComplete((pageCount: number) => { /* 只给页数 */ })
```

### 2.3 坐标转换（决定墨迹能否对齐）

KB 只给出 API 26 的「能力描述」，**没有方法名、参数、返回值**：

- 根据视图坐标点获取 PDF 页面索引；
- 视图坐标与 PDF 页面坐标的双向点转换；
- 视图坐标与 PDF 页面坐标的双向矩形转换；
- 多张页面的指定区域合并为一张图片。

来源：根 `SKILL.md:488-492`、`556`；`dev/SKILL.md:415`。

**工程含义**

- **API 26**：若官方转换 API 可用，可由它给出「点击/笔迹点 → 页面点」与「页面矩形 → 视图矩形」，减少我们维护缩放/滚动矩阵的误差；但签名未知，必须查官方 API 参考。
- **API 24**：完全没有这些 API。必须自己维护 `scale / offsetX / offsetY / rotation`，并把笔迹持久化在**页面坐标系**而不是屏幕坐标系。PDF 页面尺寸（pt）KB 未提供获取方式 → **【未知】**；若拿不到，只能用「固定渲染框 + 等比 letterbox」的近似，旋转/裁剪页会失真。

### 2.4 批注能力矩阵（Release vs Beta）

| 能力 | API 24 | API 26 Release | 26.0.1 Beta1 |
|---|---|---|---|
| 打开/翻页/页数 | ✅（起始版本未知） | ✅ | ✅ |
| 文本提取 | ✅（API 23+） | ✅ | ✅ |
| 缩放范围 | ✅（API 23+） | ✅ | ✅ |
| 页 → PixelMap | ✅（起始版本未知） | ✅ | ✅ |
| 视图↔页面坐标转换 | ❌ | ✅（签名未知） | ✅ |
| 二进制 PDF | ❌ | ✅（签名未知） | ✅ |
| 自定义渲染风格 | ❌ | ✅（签名未知） | ✅ |
| 多区域拼图 | ❌ | ✅（签名未知） | ✅ |
| 内置手写/擦除 | ❌ | ❌ | ✅ `enablePen` |
| 内置多色临时高亮 | ❌ | ❌ | ✅ `setHighlightRectsWithColors` |
| 笔迹保存/导出/撤销 | 【未知】 | 【未知】 | 【未知】 |

**结论**：把 `enablePen` 当作生产依赖是不可接受的——它不在 Release 基线、Beta 不能上架、且 KB 未说明笔迹如何落盘。**自绘墨迹层是唯一跨 API 24/26 一致的方案。**

### 2.5 两种应用内课件架构

#### 方案 A（推荐）：逐页 `renderPage` + 自绘墨迹层

```
PDF file
  → PdfViewManager.renderPage(i, {w,h}) → PixelMap
  → ArkUI Image / Canvas.drawImage 显示底图
  → 独立透明 Ink 层（Canvas 或 XComponent + ArkGraphics 2D drawing）
  → 笔迹存页面坐标（.icink 扩展 pageIndex/pageWidth/pageHeight）
```

优点：

- 翻页、缩放、缓存策略完全可控；不依赖 PdfView 的内部滚动状态；
- 墨迹与底图同一套变换，坐标一致性可单元测试（黄金向量）；
- API 24/26 同一套代码；API 26 的坐标 API 只作为可选校准。

风险/待验证：

- `renderPage` 的 `width/height` 是否会拉伸页面、是否保持宽高比、是否支持透明背景、是否能只渲染区域 —— **【未知】**；
- 高分辨率页面的渲染耗时与 PixelMap 内存 —— **【未知】**，需要页缓存（当前页 ±1/±2，LRU）；
- 页面尺寸/旋转信息如何获得 —— **【未知】**；
- PixelMap 交给 `Image` 显示时的缩放质量与 `objectFit` 语义 —— KB 未涉及。

#### 方案 B（不推荐）：`PdfView` 上叠透明墨迹层

只有在以下问题都能实机验证通过时才成立：

1. `PdfView` 的滚动/缩放/页切换能否被外部感知（KB 只有 `onPageChanged`）；
2. 叠层是否与 PDF 渲染同一坐标空间、是否会被 `PdfView` 的 surface 遮挡；
3. 手势冲突（单指画 vs 双指缩放 vs 滚动）如何仲裁；
4. 页面边界/留白/旋转如何获得。

在验证前，方案 B 不应进入设计。

#### 方案 C（兜底）：PPT 导出 PNG 序列

- 用户在 PC 端「另存为图片」或由课件生产流程导出 PNG；
- 应用内按图片序列翻页 + 墨迹层（复用白板图片层，`BoardImageLayer` 的 ArkTS 对应物）；
- 完全绕开 PDF Kit，API 24 可用，页面像素与墨迹天然对齐；
- 代价：无文本提取/搜索、文件体积大、矢量放大模糊。

### 2.6 与现有 WinUI3 代码的映射

- `PptInteropService`（COM、放映窗口句柄、翻页）→ **整体删除**；
- `PptInkSession` 的平台无关状态机（`SlideID` 缓存、上次页/放映位置、截图抑制、通知）→ **保留**，把 `SlideID` 换成 `(deckId, pageIndex)`；
- `PptScreenshotRequest`（同步抓屏、path、reason）→ **语义降级**：改为「页面渲染快照」而不是抓屏；
- `PptInkFileStore` / `.icink` → 增加 `deckId`、`pageIndex`、`pageWidth/pageHeight`、`transform` 字段；两侧 schema 同步。

---

## 3. AVPlayer / Video：视频播放与位置保持

### 3.1 明确 API

| API | 用法 | 起始版本 | 证据 |
|---|---|---|---|
| 创建 | `await avPlayer.createAVPlayer()` | API 14+（文件头） | `avplayer.md:19-24` |
| 播放源 | `avPlayer.url = '...'`；文件头提到 `fdSrc` | 同上 | `avplayer.md:25-27` |
| 事件 | `avPlayer.on('playbackEvent', (errCode, errMsg)=>{})` | 同上 | `avplayer.md:28-31` |
| 准备/播放 | `await avPlayer.prepare()`；`await avPlayer.play()` | 同上 | `avplayer.md:33-36` |
| 暂停/停止/释放 | `pause()` / `stop()` / `release()` | 同上 | `avplayer.md:37-45` |
| 跳转 | `await avPlayer.seek(50000)`（毫秒） | 同上 | `avplayer.md:49-52` |
| 倍速 | `setPlaybackSpeed(avPlayer.PlaybackSpeed.Speed_Forward_2_00_X)` | 同上 | `avplayer.md:53-55` |
| 进度 | `avPlayer.currentTime`、`avPlayer.duration` | 同上 | `avplayer.md:56-59` |
| 完成事件 | `avPlayer.on('playbackCompleted', ...)` | 同上 | `samples/references/media.md:70-76` |
| 软解格式 | AV1/VP9/VP8/RV30/RV40/WVC1/DVVIDEO/RAWVIDEO/MPEG1 | API 23 | `avplayer.md:87-98` |
| 批量缩略图 | `avMetadataExtractor.fetchFramesByTimes([1000,5000])` | API 23 | `avplayer.md:100-111` |
| HDR→SDR | HDR VIVID/HLG/HDR10 转 SDR | API 23 | `avplayer.md:101-104` |

### 3.2 `Video` 组件（ArkUI）

```ts
Video({
  src: 'https://example.com/video.mp4',
  currentProgressRate: PlaybackRate.Speed_Forward_1_00,
  controller: this.controller
})
  .width('100%')
  .height(300)
  .autoPlay(true)
  .muted(false)
  .onPrepared((event) => { console.info('duration=' + event.duration) })
  .onFinish(() => { console.info('finished') })
```

来源：`samples/references/media.md:96-108`。KB 没有给出 `VideoController` 的 `start/pause/seek/currentTime` 方法签名 → **【未知】**。

### 3.3 位置保持设计（PPT 内嵌视频 / 白板视频）

【推断】推荐状态机：

```
Idle → Prepared(url, positionMs, wasPlaying) → Playing → Paused
```

- 切页/切白板前：若正在播放，读取 `avPlayer.currentTime`（毫秒），记录 `(uri, positionMs, wasPlaying)` 到页面模型；
- 返回该页：重新 `createAVPlayer` → `url` → `prepare` → `seek(positionMs)` → `play`（若 `wasPlaying`）；
- 释放旧实例 `release()`，避免多个播放器同时持有音频焦点；
- 音频焦点：`audioSessionManager.requestAudioFocus({streamUsage: STREAM_USAGE_MUSIC, contentType: CONTENT_TYPE_MUSIC, pauseWhenDucked: true})`，失去焦点时暂停，获得后恢复（`audio-kit.md:84-97`）。

**必须实机验证**：

1. AVPlayer 的视频画面如何上屏（`setSurfaceId` / XComponent / 内部渲染）——KB **无**说明；
2. `Video` 组件是否支持本地沙箱文件 URI、是否支持 `rawfile`；
3. 切页时销毁/重建播放器的首帧延迟；
4. 后台/分屏时音频是否继续、是否被系统打断；
5. `seek` 在未 `prepare` 完成时的行为、`seek` 精度。

---

## 4. Audio Kit：计时结束音与音频焦点

### 4.1 明确 API

| API | 用法 | 起始版本 | 证据 |
|---|---|---|---|
| 音频管理器 | `audio.getAudioManager()` | API 14+ | `audio-kit.md:14-17` |
| 音量 | `getVolumeManager()`；`getVolume(MEDIA)`；`setVolume(MEDIA, 50)`；`on('volumeChange')` | 同上 | `audio-kit.md:15-25` |
| 渲染器 | `audio.createAudioRenderer({streamInfo, rendererInfo})`；`start/write/stop/release` | 同上 | `audio-kit.md:32-51` |
| 采集器 | `audio.createAudioCapturer({streamInfo, capturerInfo})`；`read(buffer)` | 同上 | `audio-kit.md:57-77` |
| 焦点 | `requestAudioFocus({streamUsage, contentType, pauseWhenDucked})`；`abandonAudioFocus()` | 同上 | `audio-kit.md:84-97` |
| 系统音效 | `systemSoundManager.getSystemSoundManager()` | API 23 | `audio-kit.md:107-115` |
| 路由时延 | `audioRenderer.getLatency()` | API 23 | `audio-kit.md:117-124` |
| 录音降噪 | 设置/获取录音流降噪模式 | API 26 | `audio-kit.md:131-140` |
| 短音频 | `new soundPool.SoundPool({audioRendererInfo:{usage:1, rendererFlags:0}})`；`load('entry/resources/rawfile/click.mp3', 0, cb)`；`play(soundId, {})`；`stop`；`release` | 【未知】 | `samples/references/media.md:111-138` |

### 4.2 计时结束音的三条路径

| 路径 | 适用 | 事实/风险 |
|---|---|---|
| **SoundPool** | 1–3 秒短提示音，低延迟、可并发 | KB 示例完整（`samples/references/media.md:111-138`）；`usage: 1` 注释为 `STREAM_USAGE_MEDIA`，是否适合通知类提示音 **【未知】**；起始 API **【未知】** |
| **AVPlayer** | 较长铃声/音乐（>5 秒） | 有完整控制；但初始化 `prepare` 有延迟，结束音可能晚于 0 点 |
| **AudioRenderer** | 需要 PCM 级控制/自定义波形 | 需自己解码或生成 PCM，复杂度最高；`write(buffer)` 的缓冲/欠载语义 KB 未说明 |

【推断】课堂倒计时结束音首选 **SoundPool 预加载 rawfile**，在计时开始时就 `load`，到点 `play`；若设备不支持或音效需要更长，则回退 AVPlayer。

### 4.3 后台到点提醒不能只靠应用内音频

应用被挂起后，AudioRenderer/SoundPool 是否继续播放 **【未知】**。可靠做法是**代理提醒 + 通知渠道铃声/震动**（见 §7、§9）：

- `NotificationSlot` 的 `sound: string` 是「通知铃声 URI」（`notification.md:268-281`），能否指向应用 `rawfile` **【未知】**；
- `SlotType.SERVICE_INFORMATION` + `SlotLevel.LEVEL_HIGH` 可获得横幅、锁屏、铃声/振动、状态栏图标、自动亮屏（`notification.md:257-266`）；
- 代理提醒由系统发布，应用不需要后台运行（`background-task.md:153-158`）。

### 4.4 音频焦点与课堂场景

- 播放视频/铃声前请求焦点；被其他应用打断（来电、系统提示）时暂停；
- `pauseWhenDucked: true` 表示被压低音量时暂停还是继续 —— KB 只给字段，语义未展开；
- `STREAM_USAGE_MUSIC` / `CONTENT_TYPE_MUSIC` 是 KB 唯一示例；课堂提示音是否应改用通知类 `StreamUsage` **【未知】**，不要臆造枚举值。

---

## 5. Image Kit：PixelMap、Surface 与 PNG 编码

### 5.1 明确 API

| API | 用法 | 起始版本 | 证据 |
|---|---|---|---|
| 从 fd | `fs.openSync(path, READ_ONLY)` → `image.createImageSource(file.fd)` | 【未知】 | `image-kit.md:17-23` |
| 从 buffer | `image.createImageSource(buffer)` | 【未知】 | `image-kit.md:24-26` |
| 从资源 | `image.createImageSource($r('app.media.example'))` | 【未知】 | `image-kit.md:28-30` |
| 解码 | `createPixelMap({editable, desiredSize, desiredPixelFormat: RGBA_8888})` | 【未知】 | `image-kit.md:40-47` |
| 变换 | `scale/rotate/translate/flip/crop` | 【未知】 | `image-kit.md:49-55` |
| 信息 | `getImageInfo()` → `size.width/height/pixelFormat` | 【未知】 | `image-kit.md:57-61` |
| 像素读写 | `readPixels(area, data)` / `writePixels(area, data)` | 【未知】 | `image-kit.md:63-68` |
| 元数据 | `readImageMetadata()` / `modifyImageMetadata()` | API 23 | `image-kit.md:71-93` |
| Surface→PixelMap | `image.createPixelMapFromSurfaceWithTransformation({surfaceId, autoRotate:true})` | API 23 | `image-kit.md:96-109` |
| 编码 | `image.createImagePacker()`；`packing(pixelMap, {format:'image/jpeg', quality:90})`；`packToFile(pixelMap, fd, {...})` | 【未知】 | `image-kit.md:112-140` |
| 格式 | JPEG/PNG/GIF/BMP/WebP/HEIF/SVG 解码；JPEG/PNG/BMP/WebP/HEIF 编码 | — | `image-kit.md:143-153` |
| 新元数据 | GIF/JFIF/TIFF/PNG/AVIS 元数据类、XMP | API 26 | 根 `SKILL.md:551,832` |

无特殊权限；访问文件需要文件读写权限（`image-kit.md:155-161`）。

### 5.2 用 Image Kit 做截图落盘

```ts
import { image } from '@kit.ImageKit';
import { fileIo as fs } from '@kit.CoreFileKit';

async function savePng(pixelMap: image.PixelMap, path: string): Promise<void> {
  const packer = image.createImagePacker();
  const opts: image.PackingOption = { format: 'image/png', quality: 100 };
  const f = fs.openSync(path, fs.OpenMode.CREATE | fs.OpenMode.WRITE_ONLY);
  await packer.packToFile(pixelMap, f.fd, opts);
  fs.closeSync(f);
  await packer.release();
}
```

用途：把「页面渲染结果 + 墨迹合成结果」编码成 PNG，写入应用沙箱；再走系统保存/相册/分享。

---

## 6. 相册与系统 Picker：截图保存与素材选取

### 6.1 PhotoAccessHelper

```ts
import { photoAccessHelper } from '@kit.MediaLibraryKit';
import { fileIo } from '@kit.CoreFileKit';

const ph = photoAccessHelper.getPhotoAccessHelper(context);

// 保存图片到相册
const file = fileIo.openSync(localPath, fileIo.OpenMode.READ_ONLY);
const uri = await ph.createAsset(
  photoAccessHelper.MediaType.IMAGE,
  { title: 'ink_snapshot', displayName: 'ink_snapshot.png' } as photoAccessHelper.CreateOptions,
  file
);
fileIo.closeSync(file);
```

来源：`photo.md:22-24, 58-74`。查询相册：

```ts
const fetchOptions: photoAccessHelper.FetchOptions = {
  fetchColumns: ['uri', 'display_name', 'size', 'date_added'],
  predicates: /* photo.md 的示例使用了 MediaFetchOptions.create，疑似示例简化 */
};
const result = await ph.getAssets(fetchOptions);
```

**注意**：`photo.md:26-35` 的 predicates 写法（`MediaFetchOptions.create`）在 KB 内没有其他佐证，可能是文档简化/错误；不要照抄，需查官方 API。分页/游标/排序也只能以官方为准。

权限（`photo.md:9-17`）：

```json
"requestPermissions": [
  { "name": "ohos.permission.READ_MEDIA_IMAGES" },
  { "name": "ohos.permission.READ_MEDIA_VIDEO" },
  { "name": "ohos.permission.WRITE_MEDIA_IMAGES" }
]
```

`READ_MEDIA_IMAGES` 在权限总表中是 `user_grant`（`permissions.md:58-65`）。

### 6.2 系统 Picker（无需权限）

`system/references/file-management.md:200-330` 给了完整模式：

```ts
import { picker } from '@kit.CoreFileKit';

// 选择图片（导入课件/截图）
let so = new picker.PhotoSelectOptions();
so.MIMEType = picker.PhotoViewMIMETypes.IMAGE_TYPE;
so.maxSelectNumber = 5;
let selected = await new picker.PhotoViewPicker().select(so);
// selected.photoUris: Array<string>

// 保存文件（截图另存为）
let saveOpts = new picker.DocumentSaveOptions();
saveOpts.newFileNames = ['Screenshot-101500.png'];
let uris = await new picker.DocumentViewPicker().save(saveOpts);

// 相册式保存
let ps = new picker.PhotoSaveOptions();
ps.newFileNames = ['saved_image.jpg'];
let paths = await new picker.PhotoViewPicker().save(ps);
```

- Picker 返回的 URI 默认仅本次有效；跨会话访问需 `picker.grantUriPermission(uriObj, 'r')`（`file-management.md:299-326`）。
- 选择/保存文件**无需配置权限**（`core-file-kit.md:69`）。

### 6.3 截图保存的产品路径

| 目标 | 路径 | 权限 |
|---|---|---|
| 存到用户可见位置 | `DocumentViewPicker.save`（用户选目录） | 无 |
| 存到相册 | `PhotoAccessHelper.createAsset` | `WRITE_MEDIA_IMAGES`（user_grant） |
| 分享给其他应用 | Share Kit `contentInfo.filePath` | 无（文件读权限） |
| 导入已有截图 | `PhotoViewPicker.select` → Image Kit 解码 | 无（Picker 授权） |

---

## 7. Notification Kit：本地通知与计时提醒

### 7.1 授权流程

```ts
import { notificationManager } from '@kit.NotificationKit';
import { common } from '@kit.AbilityKit';
import { BusinessError } from '@kit.BasicServicesKit';

async function ensureNotify(ctx: common.UIAbilityContext): Promise<void> {
  const enabled = await notificationManager.isNotificationEnabled();
  if (enabled) return;
  try {
    await notificationManager.requestEnableNotification(ctx); // 首次弹窗
  } catch (err) {
    const e = err as BusinessError;
    if (e.code === 1600004) {
      await notificationManager.openNotificationSettings(ctx); // API 13+
    }
  }
}
```

事实：`isNotificationEnabled`、`requestEnableNotification(context)`、`openNotificationSettings(context)`（API 13+）；用户拒绝后不能再次弹窗，只能拉起通知设置（`notification.md:31-63`）。通知能力默认关闭；`requestEnableNotification` 不需要在 `module.json5` 声明权限（`notification.md:674-679`）。

### 7.2 发布/更新/取消

| API | 说明 | 证据 |
|---|---|---|
| `publish(request)` | 发布；相同 `id`+`label` 覆盖旧通知 | `notification.md:85-117, 697-718` |
| `cancel(id, label?)` | 取消指定 | 同上 |
| `cancelAll()` | 取消全部 | 同上 |
| `setBadgeNumber(n)` | 角标（API 10+） | `notification.md:655-667` |
| `getBadgeNumber()` | 读角标（API 22+） | 同上 |

`NotificationRequest` 关键字段：`id`、`label`、`content`、`wantAgent`、`actionButtons`、`notificationSlotType`、`badgeNumber`、`isUnremovable`、`isOngoing`（`notification.md:71-84`）。

### 7.3 计时结束通知模板

```ts
const req: notificationManager.NotificationRequest = {
  id: 1001,
  content: {
    notificationContentType: notificationManager.ContentType.NOTIFICATION_CONTENT_BASIC_TEXT,
    normal: {
      title: '倒计时结束',
      text: '本节课的 45 分钟计时已到',
      additionalText: '点击返回课堂'
    }
  },
  notificationSlotType: notificationManager.SlotType.SERVICE_INFORMATION,
  isOngoing: false,
  isUnremovable: false,
  wantAgent: /* 指向 EntryAbility 的 WantAgent */
};
await notificationManager.publish(req);
```

渠道级别：`SERVICE_INFORMATION` 默认有横幅/锁屏/铃声/振动/状态栏/自动亮屏（`notification.md:257-266`）；`SlotLevel.LEVEL_HIGH` 可显式提高（`notification.md:283-291`）。若要「进行中」的常驻计时通知，用 `isOngoing: true` + `isUnremovable: true`，并按秒/分更新同一 `id`（`notification.md:697-718`）。

**硬限制**（`notification.md:17-22`）：

- 单应用通知中心最多留存 **24 条**；
- 单条通知 **≤200KB**；
- 单应用新通知 **≤10 条/秒**，更新 **≤20 条/秒**；
- 三方便应用合计新通知 **≤15 条/秒**，更新 **≤30 条/秒**。

每秒更新一次计时通知远低于上限，但会增加功耗；【推断】建议只在分钟边界或关键节点更新。

### 7.4 API 24 / 26 差异

- API 23+：`priorityNotificationType`（优先显示）、`overlayIcon`（重叠图标）、`NotificationFlags` 控制横幅/锁屏显示（`notification.md:720-732`）→ API 24 可用。
- API 26：锁屏通知设置、半模态方式拉起通知设置界面（根 `SKILL.md:534,824`）；**方法名 KB 未给出**。
- 实况窗通知内容 `NOTIFICATION_CONTENT_SYSTEM_LIVE_VIEW` 仅系统应用；三方走 Live View Kit（`notification.md:550-556`）。

---

## 8. Live View Kit：倒计时实况窗（结论：不适用）

### 8.1 明确 API

| API | 签名 | 起始版本 | 证据 |
|---|---|---|---|
| 管理器 | `liveViewManager.getInstance()` | 【未知】 | `live-view-kit.md:11-17` |
| 启动 | `manager.startLiveView(liveViewInfo): Promise<string>` | 【未知】 | `live-view-kit.md:19-38` |
| 更新 | `manager.updateLiveView(liveViewId, liveViewInfo): Promise<void>` | 【未知】 | `live-view-kit.md:41-60` |
| 停止 | `manager.stopLiveView(liveViewId): Promise<void>` | 【未知】 | `live-view-kit.md:63-75` |
| 地理触发 | `manager.startLiveViewByTrigger(triggerInfo)` | API 23 | `live-view-kit.md:77-98` |
| 样式 | `LiveViewStyle.TRANSPORT / SPORTS / FOOD_DELIVERY / TAXI` | 【未知】 | `live-view-kit.md:100-116` |
| 进度环 | 实况窗卡片辅助区模板，支持百分比进度环 | API 26 | 根 `SKILL.md:555,837` |

`LiveViewInfo` 字段示例：`liveViewStyle`、`title`、`content`、`icon`、`progressValue`、`progressMaxValue`（`live-view-kit.md:26-35`）。

### 8.2 为什么不能用作倒计时

1. **没有计时/倒计时样式**：只有交通、运动、外卖、打车。用 `SPORTS` 语义不符，审核有风险。
2. **没有倒计时时间字段**：`LiveViewInfo` 只有 `progressValue/progressMaxValue`，KB 未给 `initialTime`/`isCountDown`（这些字段只出现在系统级 `NotificationSystemLiveViewContent.time`，`notification.md:556-609`）。
3. **权限冲突**：Live View Kit 要求声明 `ohos.permission.NOTIFICATION_CONTROLLER`（`live-view-kit.md:165-179`）；Notification 文档明确该权限是 `system_core`、仅系统应用（`notification.md:678-683`）。三方能否获得该权限 **【未知/需实机验证】**。
4. **KB 只有「三方应用需通过 Live View Kit 接入」一句**（`notification.md:554`），没有接入资格、审核、配额、API 24 可用性说明。

### 8.3 可用替代

- 常驻通知（`isOngoing`）+ 每 30/60 秒更新文本；
- 到点代理提醒（系统级提醒，见 §9）；
- 应用在前台时，应用内自绘倒计时窗口（与 WinUI3 的 CountdownWindow 同构）。

---

## 9. Background Tasks Kit：倒计时后台与结束提醒

### 9.1 四类后台任务

| 类型 | 典型时长 | 适用 | 证据 |
|---|---|---|---|
| 短时任务 | 最长 **3 分钟**，每日配额有限，到期强制挂起 | 临时保存/收尾 | `background-task.md:18-24` |
| 长时任务 | 持续运行，必须有前台通知 | 音乐、导航、运动、传输等 | `background-task.md:44-58` |
| 延迟任务 | 系统调度，非实时 | 同步、上传 | `background-task.md:116-151` |
| 代理提醒 | 系统代理触发，应用无需运行 | 定时提醒、日历、闹钟 | `background-task.md:153-206` |

短时任务：

```ts
let info: backgroundTaskManager.DelaySuspendInfo =
  backgroundTaskManager.requestSuspendDelay('SaveData', () => { /* 到期 */ });
backgroundTaskManager.cancelSuspendDelay(info.requestId);
```

长时任务：

```ts
// module.json5: "backgroundModes": ["audioPlayback", "location"]
backgroundTaskManager.startBackgroundRunning(
  context,
  backgroundTaskManager.BackgroundMode.AUDIO_PLAYBACK,
  wantAgentObj
);

// API 14+
const taskId: number = await backgroundTaskManager.requestContinuousTask({
  bgMode: backgroundTaskManager.BackgroundMode.AUDIO_PLAYBACK,
  wantAgent: wantAgentObj,
  taskTitle: 'Music Playing',
  taskDesc: 'App is playing music in background'
});
backgroundTaskManager.cancelContinuousTask(taskId);
```

`BackgroundMode`：`DATA_TRANSFER`、`AUDIO_PLAYBACK`、`AUDIO_RECORDING`、`LOCATION`、`BLUETOOTH_INTERACTION`、`MULTIDEVICE_CONNECTION`、`TASK_KEEPING`（`background-task.md:48-58`）。

权限：`ohos.permission.KEEP_BACKGROUND_RUNNING`（system_grant）、`ohos.permission.PUBLISH_AGENT_REMINDER`（system_grant）（`background-task.md:208-213`）。注意 `notification.md:678-683` 把 `PUBLISH_AGENT_REMINDER` 标为 `system_basic`；两处 APL 不一致，**三方 normal 应用能否申请必须核实**。

### 9.2 代理提醒（倒计时结束的关键）

```ts
import { reminderAgentManager } from '@kit.ReminderAgentKit';

const timer: reminderAgentManager.ReminderRequestTimer = {
  reminderType: reminderAgentManager.ReminderType.REMINDER_TYPE_TIMER,
  triggerTimeInSeconds: 45 * 60,
  title: '倒计时结束',
  content: '课堂计时已到',
  wantAgent: wantAgentObj
};
const reminderId = await reminderAgentManager.publishReminder(timer);
// 取消
reminderAgentManager.cancelReminder(reminderId);
```

事实：`ReminderRequestTimer`、`ReminderRequestCalendar`、`ReminderRequestAlarm`、`publishReminder`、`cancelReminder`、`cancelAllReminders`（`background-task.md:153-206`）。API 26 给「倒计时实例」新增 `repeatInterval` / `repeatCount`（根 `SKILL.md:538,810`）——但 KB 未说明该「倒计时实例」是否就是 `ReminderRequestTimer`，**映射关系【未知】**。

### 9.3 倒计时方案的工程结论

| 场景 | 方案 | 可靠性 |
|---|---|---|
| 前台倒计时 UI | 应用内计时器（ArkTS 定时器，KB 未在本范围展开） | 高（前台） |
| 切后台短时间（<3 分钟） | 短时任务 | 中（配额/到期挂起） |
| 切后台整节课 | 长时任务 `TASK_KEEPING` + 前台通知 | 低/待验证（三方资格、审核、时长均未知） |
| 到点必须提醒 | **代理提醒**（系统代理） | 高（不依赖进程） |
| 已在后台时的每秒刷新 | 不可依赖 | — |

【推断】课堂倒计时产品策略：

- 计时期间保持应用前台/分屏可见；后台时停止刷新 UI，但**始终注册代理提醒**作为兜底；
- 到点同时：代理提醒（系统）+ 若进程仍活着则 SoundPool 播放提示音 + 本地通知；
- 取消/重置时同时 `cancelReminder` + `cancel` 通知 + 停音频。

---

## 10. Share Kit：分享与「分享进入应用」

### 10.1 分享出去

```ts
import { systemShare } from '@kit.ShareKit';

const data = new systemShare.ShareData({
  plainText: '分享图片',
  contentInfo: { mimeType: 'image/png', filePath: imageUri }
});
const controller = new systemShare.ShareController(data);
controller.show(context, {
  previewMode: systemShare.SharePreviewMode.DETAIL,
  selectionMode: systemShare.ShareSelectionMode.SINGLE,
  targetBundleName: undefined // 可选，定向分享
});
```

事实：`systemShare.ShareData`、`ShareController`、`ShareOptions{previewMode, selectionMode, targetBundleName}`、`SharePreviewMode.DETAIL/DEFAULT`、`ShareSelectionMode.SINGLE/MULTIPLE`；分享文本/图片/文件/多类型；访问文件需要读权限，系统分享面板本身无特殊权限（`share-kit.md:11-131`）。

### 10.2 分享进入应用（API 23+）

```ts
import { shareCore } from '@kit.ShareKit';
const recordData = await shareCore.readingExtendedShareRecordData(shareRecordId);
```

KB 只说「可获取其他应用分享过来的数据」（`share-kit.md:93-103`）。**如何获得 `shareRecordId`、应用如何声明为分享目标（skills/actions/entities）、接收回调在哪个生命周期，KB 均未写明 → 【未知】**。

### 10.3 系统截图分享

- KB 的行业实践列表里有「截屏事件监听及分享」（`industry-practices.md:156`），但**没有 API 名、公共事件名或代码**；
- 因此「用户按系统截屏 → 分享到 InkCanvas」只能作为产品设想，必须查官方「截屏事件监听及分享」实践文档；
- 不能假设三方应用可以静默读取系统截图。

### 10.4 API 24/26

- API 23：读取扩展分享记录；
- API 26：碰一碰分享获取轻碰位置信息（PC/2in1/Tablet）（根 `SKILL.md:549,829`）——与课堂场景无关；
- 基础分享面板在 API 24 可用（起始版本未知）。

---

## 11. Account Kit：华为账号（非必需）

### 11.1 事实

- 包：`@kit.AccountKit`；核心：`HuaweiIDProvider`、`authentication`、`HuaweiIdButton`、`LoginPanel`、`getUserInfo`（`account-kit.md:57-72`）。
- 能力：一键登录、静默登录、头像昵称、手机号、收货地址、发票抬头、风险等级、实名年龄段、未成年人模式（`account-kit.md:11-23`）。
- 标识：OpenID（应用维度）、UnionID（开发者维度）、GroupUnionID（账号组维度）；区分大小写（`account-kit.md:44-53, 101-105`）。
- 权限：`ohos.permission.GET_NETWORK_INFO`（系统授权）；需在 AGC 配置 Client ID 与签名证书指纹（`account-kit.md:75-82`）。
- 设备：Phone、Tablet、PC/2in1、TV 等（`account-kit.md:85-96`）。
- 模拟器仅支持登录/授权与 `HuaweiIdButton`（`account-kit.md:101-105`）。

### 11.2 对 InkCanvas 的建议

【推断】

- v1 **不引入账号体系**：课堂画笔/白板/计时/抽奖/回放都可以本地完成；引入登录会增加隐私合规、AGC 配置、审核成本。
- 若未来做「课件云同步 / 跨设备墨迹」再评估 Account Kit；届时 OpenID 作为应用内用户主键，UnionID 用于同一开发者多应用打通。
- 未成年人模式与教育场景相关，但 KB 只给能力名，无接入细节；不作为近期范围。

---

## 12. AppGallery 分发与「自动更新」

### 12.1 分发事实

- 流程：创建应用 → 填写信息 → 上传包 → 提交审核 → 上架（`distribution.md:13-38`）。
- 包：`.app`（HarmonyOS）；必须发布证书签名；App Pack ≤4GB；`versionCode` 必须递增（`distribution.md:66-79`）。
- 审核：首次 1–3 个工作日，更新 1–2 个工作日；常见驳回：功能异常、隐私合规、内容违规、侵权、UI 不规范、签名问题（`distribution.md:83-113`）。
- 发布：全量/灰度/分阶段；灰度 5%→10%→25%→50%→100%；可暂停、回滚（`distribution.md:117-149`）。
- 鸿蒙应用要求 Stage 模型、星盾安全访问机制、不得使用未声明系统权限、隐私政策齐全（`distribution.md:108-113`）。

### 12.2 更新能力

KB 对 AppGallery Kit 的「应用市场更新功能」只有一句能力描述：

> 查询应用是否有可更新版本，应用内显示更新提醒。支持 Phone、PC/2in1、Tablet、Wearable、TV。

来源：`monetization.md:201-225`。约束：大部分能力仅中国境内（港澳台除外）；模拟器不支持更新功能（`monetization.md:262-265`）。26.0.1 Beta1 给该能力新增 Car 设备（`distribution.md:157-169`）。

**KB 没有给出的**：模块名/包名、方法名、签名、返回值、起始 API、是否需要 AGC 配置、是否能拉起更新流程、是否有强制更新能力 —— 全部 **【未知】**。

### 12.3 Windows `UpdateService` 的替代设计

| Windows 现状 | HarmonyOS 替代 | 可行性 |
|---|---|---|
| 检查 `ReleaseFeed`（GitHub/自建） | 无自更新；改由 AppGallery 检查 | AppGallery 托管 |
| 下载 `.exe` / `.msi` | 不能下载安装 HAP | 禁止 |
| 启动安装程序 | 无 `Process.Start` | 禁止 |
| 静默更新窗口/时段 | 系统更新策略（用户/市场控制） | 仅提示 |
| 版本比较/强制更新策略 | 应用内 `versionCode` 与市场返回版本比较（API 未知） | 待验证 |

【推断】落地策略：

1. **主路径**：依赖 AppGallery 自动更新（用户在应用市场设置中开启）；
2. **辅路径**：应用内「检查更新」入口，调用 AppGallery Kit 更新能力（API 待查），发现新版本显示提醒，点击跳转应用市场详情页（AppGallery「应用市场推荐」能力，API 名同样未知）；
3. **企业/校园批量部署**：KB 本范围没有 MDM/企业分发细节；若需要，另查 `system/references/enterprise-kits.md`/`mdm-kit.md`，本次不展开；
4. `pluginBundleManager`（API 26）是「自分发插件」的安装/卸载，**不是应用自更新**，且三方插件分发政策未知（根 `SKILL.md:398` 附近、`app-framework.md:253-259`）。

---

## 13. 相邻能力：Preview Kit / Pen Kit / ArkGraphics 2D

### 13.1 Preview Kit（文件预览）

- 包：`@kit.PreviewKit`；能力：文件预览、文件打开加速、通用文件缓存加速；`canPreview`、预览 API、预加载、缓存（`app-service-kits.md:103-127`）。
- 支持：图片、视频、音频、文本、html、pdf、**doc/docx/xls/xlsx/ppt/pptx/csv/ofd**（`app-service-kits.md:130-137`）。
- 约束：仅中国境内；文件预览支持 Phone/Tablet/2in1；文件打开加速仅 2in1；模拟器不支持 pdf/pptx/xlsx/docx 预览（`app-service-kits.md:143-147`）。
- **不能**替代批注：KB 没有「嵌入预览组件」「导出页面位图」「叠加自定义层」的 API；它更像「拉起系统预览界面」。
- 用途：应用内「查看原始课件」、打开 PDF 原文、快速检查文件是否可预览。

### 13.2 Pen Kit

- `HandWriteComponent({canvasConfig:{width,height,backgroundColor}})`（`pen-kit.md:9-24`）。
- API 23：禁用画布缩放、`ScrollTo` 与长画布滚动监听、自定义最大高度（`pen-kit.md:26-35`）。
- API 26：手写笔跟手性加速接口（根 `SKILL.md:791`、`pen-kit.md` 未列）。
- 限制：黑盒组件；KB 没有背景图、笔迹导出、图层、擦除、颜色/笔宽 API → **不适合作为 PDF 批注层**。可作为「纯手写便签」候选，但会把墨迹锁死在组件内，违背 `.icink` 互认目标。

### 13.3 ArkGraphics 2D / drawing（墨迹渲染依赖）

- `@ohos.graphics.drawing`：`Canvas`、`Pen`、`Brush`、`Path`、`Typeface`、`Image`；Pen 描述轮廓、Brush 填充（`graphics2d.md:88-101`）。
- NDK Drawing 可在 surface 上绘制（`ndk-development.md`，由 04/10 报告展开）。
- 本报告不重复渲染选型；只强调：PDF 墨迹层应与白板共用同一渲染后端和坐标契约。

---

## 14. API 24（6.1.1）与 API 26（26.0.0）差异总表

> 说明：API 24 = HarmonyOS 6.1.1 Release；API 26 = 26.0.0 Release。26.0.1 Beta1 单列，不进入生产。

| 能力 | API 24 | API 26 | 差异来源 |
|---|---|---|---|
| PDF 打开/页数/翻页/当前页 | ✅（起始版本未知） | ✅ | `pdf-kit.md` |
| PDF → PixelMap（`renderPage`） | ✅（起始版本未知） | ✅ | `pdf-kit.md:97-108` |
| PDF 文本提取 | ✅（API 23+） | ✅ | `pdf-kit.md:56-77` |
| PDF 自定义缩放范围 | ✅（API 23+） | ✅ | `pdf-kit.md:79-91` |
| PDF 二进制读取 | ❌ | ✅（签名未知） | 根 `SKILL.md:488-491` |
| PDF 自定义渲染风格 | ❌ | ✅（签名未知） | 同上 |
| PDF 视图↔页面坐标转换 | ❌ | ✅（签名未知） | 根 `SKILL.md:491-492` |
| PDF 多区域拼图 | ❌ | ✅（签名未知） | 根 `SKILL.md:556,838` |
| PDF 内置手写/擦除 | ❌ | ❌ | 26.0.1 Beta1 才有 |
| PDF 多色临时高亮 | ❌ | ❌ | 26.0.1 Beta1 才有 |
| AVPlayer 基础播放/seek/倍速 | ✅（API 14+） | ✅ | `avplayer.md` |
| AVPlayer 软解 AV1/VP9 等 | ✅（API 23+） | ✅ | `avplayer.md:87-98` |
| 批量视频缩略图 / HDR→SDR | ✅（API 23+） | ✅ | `avplayer.md:100-111` |
| 录屏暂停/恢复、指定应用窗口录屏 | ❌ | ✅ | 根 `SKILL.md:462-467` |
| AudioRenderer / 焦点 / 音量 | ✅（API 14+） | ✅ | `audio-kit.md` |
| 系统音效管理 / `getLatency` | ✅（API 23+） | ✅ | `audio-kit.md:101-127` |
| 录音降噪模式 | ❌ | ✅ | `audio-kit.md:131-140` |
| SoundPool 短音频 | 【未知】 | 【未知】 | 仅示例 |
| ImageSource/PixelMap/变换/打包 | ✅（起始版本未知） | ✅ | `image-kit.md` |
| 图片元数据 / Surface 自动旋转 | ✅（API 23+） | ✅ | `image-kit.md:71-109` |
| GIF/JFIF/TIFF/PNG/AVIS 元数据、XMP | ❌ | ✅ | 根 `SKILL.md:551,832` |
| PhotoAccessHelper 查询/保存 | ✅（起始版本未知） | ✅ | `photo.md` |
| PhotoViewPicker / DocumentViewPicker | ✅（起始版本未知） | ✅ | `file-management.md` |
| 通知发布/渠道/WantAgent/进度 | ✅（API 14+） | ✅ | `notification.md` |
| `openNotificationSettings` | ✅（API 13+） | ✅ | `notification.md:41` |
| 角标 get/set | ✅（API 10+/22+） | ✅ | `notification.md:655-667` |
| 通知优先显示/重叠图标/Flags | ✅（API 23+） | ✅ | `notification.md:720-732` |
| 锁屏通知设置、半模态通知设置 | ❌ | ✅（方法名未知） | 根 `SKILL.md:534,824` |
| Live View 启动/更新/停止 | 【未知】（可能可用） | ✅ | `live-view-kit.md` |
| Live View 地理触发 | ✅（API 23+） | ✅ | `live-view-kit.md:77-98` |
| Live View 辅助区/进度环 | ❌ | ✅ | 根 `SKILL.md:555,837` |
| 实况窗倒计时样式 | ❌ | ❌ | 无此样式 |
| 短时/长时/延迟任务 | ✅（API 14+） | ✅ | `background-task.md` |
| `requestContinuousTask` | ✅（API 14+） | ✅ | `background-task.md:104-114` |
| 代理提醒 Timer/Calendar/Alarm | ✅（API 14+） | ✅ | `background-task.md:153-206` |
| 提醒重复 `repeatInterval/repeatCount` | ❌ | ✅（映射未知） | 根 `SKILL.md:538,810` |
| Share 文本/图片/文件分享 | ✅（起始版本未知） | ✅ | `share-kit.md` |
| Share 读取扩展分享记录 | ✅（API 23+） | ✅ | `share-kit.md:93-103` |
| Share 碰一碰位置 | ❌ | ✅ | 根 `SKILL.md:549,829` |
| Account Kit 登录/资料 | ✅（API 14+） | ✅ | `account-kit.md` |
| AppGallery 更新查询 | 【未知】（能力存在） | ✅（Car 支持在 26.0.1） | `monetization.md:215` |
| Preview Kit 文件预览 | ✅（起始版本未知） | ✅ | `app-service-kits.md:103-152` |
| Preview Kit C API 加速 | ❌ | ✅（26.0.0 Beta1） | 根 `SKILL.md:557` |
| Pen Kit HandWriteComponent | ✅（API 14+） | ✅ | `pen-kit.md` |
| Pen Kit 禁用缩放/ScrollTo/长画布 | ✅（API 23+） | ✅ | `pen-kit.md:26-35` |
| Pen Kit 跟手性加速 | ❌ | ✅ | 根 `SKILL.md:791` |

---

## 15. 运行时门控与 compat 层

### 15.1 先否定一个常见做法

`canIUse('SystemCapability.XXX')` 是 HarmonyOS 的标准 syscap 门控，但**本报告范围内 KB 没有给出任何一个对应字符串**。不要自行拼写 `SystemCapability.PDFKit`、`SystemCapability.Media.AVPlayer` 之类的名字——KB 没有依据，拼错会在运行时静默返回 false 或编译失败。

KB 里仅有的 `canIUse`/SystemCapability 事实：

| 能力 | 字符串 | 证据 |
|---|---|---|
| AR Engine | `SystemCapability.AREngine.Core` | `ar-engine.md:82` |
| XEngine | `SystemCapability.Graphic.XEngine` | `arkgraphics-3d.md:187` |
| Enterprise Foundation | `SystemCapability.PCService.EnterpriseDeployRestore` | `enterprise-foundation-kit.md:27` |

这些都和本报告的主题无关。**结论：本领域不能写 `canIUse`，必须用其他门控。**

### 15.2 可用门控信号

| 信号 | 用法 | 证据/限制 |
|---|---|---|
| API 版本 | `deviceInfo.osFullName`（系统版本字符串） | `device-info.md:10-13`；格式 KB 未给，解析需容错 |
| 设备类型 | `deviceInfo.deviceType === 'phone' / 'tablet'` | `multi-device.md:66-77`；2in1 的取值 **【未知】** |
| 模块/方法存在性 | `typeof obj.method === 'function'` 或 `try/catch` | 【推断】ArkTS 静态类型下可能需 `as any`/反射封装，需验证 |
| 业务错误码 | `BusinessError.code`（通知授权 1600004 是唯一已知码） | `notification.md:55-60` |
| 编译期隔离 | 把 API 26 调用放进独立 HAR/动态 import | 【推断】静态 import 不存在的模块在 API 24 可能加载失败 |
| 能力清单 | 自建 `harmony/platform` 的 `Capabilities` 表 | 项目契约要求 |

### 15.3 建议的 compat 形态（推断，不是 KB）

```ts
// harmony/platform/src/main/ets/compat/FeatureGate.ets

import { deviceInfo } from '@kit.BasicServicesKit';

export interface MediaCaps {
  pdfRenderPage: boolean;
  pdfText: boolean;          // API 23+
  pdfCoordTransform: boolean; // API 26
  pdfBinary: boolean;         // API 26
  avPlayer: boolean;
  liveView: boolean;
  reminder: boolean;
  appGalleryUpdate: boolean;
}

function osMajor(): number {
  const s: string = deviceInfo.osFullName ?? '';
  const m: RegExpMatchArray | null = s.match(/(\d+)\.(\d+)/);
  return m ? Number(m[1]) : 0;
}

export function mediaCaps(): MediaCaps {
  const major = osMajor();
  return {
    pdfRenderPage: true,            // 基础能力，API 24 假定可用（需实机确认）
    pdfText: major >= 6,
    pdfCoordTransform: major >= 7,  // API 26 对应 HarmonyOS 7.0 基线（推断）
    pdfBinary: major >= 7,
    avPlayer: true,
    liveView: false,                // 权限冲突，默认关闭
    reminder: true,
    appGalleryUpdate: false         // API 未知，默认关闭
  };
}

export async function tryRenderPage<T>(
  fn: () => Promise<T>, fallback: T
): Promise<T> {
  try {
    return await fn();
  } catch (e) {
    return fallback;
  }
}
```

注意：

- `osFullName` 的具体格式（`HarmonyOS 6.1.1` / `OpenHarmony 7.0` / 带 Build 号）KB **未给**，上面的 `major >= 7` 是【推断】，必须实机打印确认；
- 更稳妥的是「**功能探测优先于版本判断**」：先尝试调用，捕获 `BusinessError` 后降级；
- 所有版本判断必须集中在 `harmony/platform`，业务层禁止散落（项目 CLAUDE.md 的硬性要求）。

### 15.4 编译基线

CLAUDE.md 给定：`compileSdkVersion 26.0.0`、`compatibleSdkVersion 6.1.1(24)`、`targetSdkVersion 26.0.0`。这意味着：

- API 26 的符号**可以编译通过**；
- 在 API 24 设备上运行时可能 `undefined`/抛错，**必须运行时降级**；
- 26.0.1 Beta1 的 `enablePen` 等符号**不应进入代码**（compileSdk 26.0.0 不含它们），更不能作为发布依赖。

---

## 16. 针对 InkCanvas 的落地建议

### 16.1 分层映射

| WinUI3 层/文件 | HarmonyOS 落点 | 本报告相关结论 |
|---|---|---|
| `Features/Ppt/PptInteropService.cs` | **删除** | 无 COM/无跨应用窗口；由应用内课件渲染替代 |
| `Features/Ppt/PptInkSession.cs` | `features` HAR 纯逻辑 | 保留状态机；`SlideID` → `(deckId, pageIndex)` |
| `Features/Tools/Tools.cs`（倒计时） | `features` 计时模型 + `App` 视图 | 前台计时 + 代理提醒兜底 |
| `ScreenshotService` | `platform` 截图能力 | 降级为「本应用画布/页面快照」；API 名待验证 |
| `ReplayWindow`（音视频） | `App` + AVPlayer/Video | 位置保持用 `currentTime/seek` |
| 计时结束音 | `platform/media` 封装 SoundPool | 预加载 rawfile；后台由通知/提醒发声 |
| `.icink` | `core` 序列化 | 增加课件页坐标字段，两侧 schema 同步 |
| 设置项（自动更新/静默时段） | `core` Settings | 更新策略改为「应用市场 + 提示」，静默时段可保留用于提示频率 |

### 16.2 PPT/课件批注的推荐路线（按优先级）

1. **P0：PDF 逐页渲染 + 自绘墨迹层**（§2.5 方案 A）。需要先做三个 spike：
   - `renderPage` 的宽高比/清晰度/耗时/内存；
   - 页面尺寸与旋转信息如何获得；
   - 墨迹层与底图的坐标一致性（用黄金向量逐点比对）。
2. **P1：PNG 序列兜底**（方案 C），保证 API 24 和无 PDF 工具链时仍可用。
3. **P2：API 26 坐标转换校准**，在拿到官方签名后再接入；API 24 不依赖。
4. **不做**：`PdfView` 叠层（未验证）、`enablePen`（Beta）、Preview Kit 内嵌批注（无 API）。
5. **产品话术**：明确「本应用内打开课件并批注」，不要承诺「在 PowerPoint 放映窗口上直接画」——HarmonyOS 三方应用没有跨应用覆盖层（与 10 号报告结论一致）。

### 16.3 截图功能的重定义

| Windows 行为 | HarmonyOS 可行替代 | 用户可见差异 |
|---|---|---|
| Alt+C 全屏区域截图 | 无 | **移除**或改为「截取当前页面」 |
| PPT 切页前抓屏 | `renderPage` 渲染当前页 + 合成墨迹 | 不包含 PPT 动画/视频帧 |
| 鼠标模式自动截图 | 保存画布快照 | 语义一致（仅本应用） |
| 截图存日期目录 | 应用沙箱目录 + Picker/相册导出 | 目录选择由用户完成 |
| 截图分享 | Share Kit | 一致 |

### 16.4 倒计时的产品重设计

- 前台：保留秒级 UI；
- 后台：停止秒级刷新，保留代理提醒；
- 到点：代理提醒 + 本地高优先级通知 + 若前台则播放 SoundPool 提示音；
- 支持「重复计时」（API 26 的 `repeatInterval/repeatCount` 若映射到 `ReminderRequestTimer`，否则应用内循环重新注册）；
- 到点后提供「再来一轮 / 延长 5 分钟」的通知按钮（`NotificationActionButton` + WantAgent，`notification.md:499-546`）。

### 16.5 自动更新的产品重设计

- 删除 `ReleaseFeed` 网络检查与 `.exe` 下载/启动；
- 设置页保留「检查更新」，调用 AppGallery Kit（待查 API），失败静默；
- 更新提示频率/静默时段可复用现有设置键，但不再自动安装；
- 版本号以 `versionCode` 为准（`distribution.md:66-79`）。

---

## 17. 第三方应用硬约束

1. **没有 PPT COM/进程互操作**：不能启动或控制 PowerPoint/WPS，不能读取放映窗口句柄。
2. **没有文档化的跨应用覆盖窗**：不能在别的应用（含系统预览器）上画；窗口 API 只作用于本应用（与 10 号报告一致）。
3. **没有三方全屏/跨应用静默截图**：KB 的 `screenCapture` 属于 TestKit（测试框架），不是产品 API；`@ohos.screenshot` 不在 KB。截图只能是本应用内容或用户经 Picker/分享提供。
4. **PDF Kit 不解析 PPT/PPTX**：只处理 PDF；端上 PPT→PDF 转换无 API。
5. **PDF 内置批注不可用于生产**：`enablePen`/`setHighlightRectsWithColors` 仅 26.0.1 Beta1，Beta 不能上架。
6. **实况窗倒计时不可依赖**：无计时样式；`NOTIFICATION_CONTROLLER` 与「仅系统应用」冲突，三方资格未知。
7. **后台长时任务受限**：短时任务 ≤3 分钟；长时任务需前台通知 + `KEEP_BACKGROUND_RUNNING`；没有「倒计时」后台类型；`TASK_KEEPING` 的三方审核口径未知。
8. **代理提醒的 APL 存疑**：`PUBLISH_AGENT_REMINDER` 在两份 KB 中分别标为 `system_grant` 与 `system_basic`；normal APL 应用能否使用需核实。
9. **通知受用户授权与频次限制**：默认关闭；24 条留存；10/20 条每秒；三方便应用共享 15/30 条每秒。
10. **不能自更新 HAP**：没有下载/安装应用的 API；更新必须经 AppGallery。`pluginBundleManager` 是插件管理，不是应用自更新。
11. **AppGallery 更新能力仅中国境内**：港澳台除外；模拟器不支持；API 名未知。
12. **Preview Kit 也不能内嵌批注**：它是预览界面，不提供页面位图或叠层；且文档预览在模拟器不支持。
13. **相册写入需要用户授权**：`WRITE_MEDIA_IMAGES` 为 user_grant；Picker 保存则无需权限但需要用户交互。
14. **26.0.1 Beta 特性不能上架**：任何依赖 `enablePen`、`WindowFocusState`、`ImageReceiver.setMemoryName` 等的方案都只能做技术预研。

---

## 18. 未知 / 需实机验证清单

### 18.1 PDF / 课件

1. PDF Kit 的最低 API 版本与设备支持（Phone/Tablet/2in1）——KB 未给。
2. `renderPage` 是否保持宽高比、是否支持透明背景、能否只渲染区域、DPI/缩放语义。
3. 如何获取 PDF 页面尺寸（pt）、旋转、裁剪框；`getPageCount` 之外是否有页面信息 API。
4. API 26 坐标转换 API 的准确名称、参数、返回；是否支持滚动/缩放后的视图坐标。
5. `PdfView` 是否暴露滚动/缩放偏移、页面矩形、触摸回调；能否被透明层覆盖。
6. PDF 内置批注能否保存/导出/回写 PDF；`enablePen` 的笔迹是否可通过 API 读取。
7. 设备端是否存在 PPT/PPTX→PDF 或页面位图导出能力；Preview Kit 是否能被嵌入或导出页面。
8. 大文档（100+ 页）的渲染/缓存/内存表现。

### 18.2 视频 / 音频

9. AVPlayer 视频上屏方式（`setSurfaceId`/XComponent）与离屏解码。
10. `VideoController` 的 `currentTime/seek/pause` 签名；`Video` 是否支持沙箱 URI/rawfile。
11. 切源/重建播放器的首帧延迟与音频焦点恢复。
12. SoundPool 的起始 API、支持的格式、最大并发、与 `usage` 枚举的对应。
13. 通知渠道自定义铃声能否使用应用 `rawfile`；代理提醒的铃声由谁决定。
14. 后台/锁屏时应用内音频是否继续；被系统打断后的恢复策略。

### 18.3 截图 / 图片 / 相册

15. 应用内容快照 API（`componentSnapshot`/`window.snapshot`/UIContext）的名称、API 版本、是否包含 XComponent/叠层/子窗。
16. `createPixelMapFromSurfaceWithTransformation` 在 API 24 的可用性、surface 来源、是否包含叠加层。
17. 是否能直接把绘制结果渲染到离屏 PixelMap（而非先上屏再抓）。
18. 系统截图事件监听（公共事件名/权限）与「分享到应用」的 skills 声明。
19. `PhotoAccessHelper.createAsset` 的权限要求与 SaveButton 安全控件方案。
20. `photo.md` 的 `getAssets` predicates 示例是否正确。

### 18.4 通知 / 实况窗 / 后台

21. Live View Kit 三方资格、API 24 可用性、`NOTIFICATION_CONTROLLER` 的实际 APL。
22. 是否存在计时/倒计时实况窗模板（KB 无）。
23. API 26 锁屏通知设置/半模态设置的方法名。
24. `ReminderRequestTimer` 的最大时长、提醒数量上限、精度、被系统省电策略影响的程度。
25. API 26 `repeatInterval/repeatCount` 属于哪个类（是否 `ReminderRequestTimer`）。
26. `TASK_KEEPING` 的三方审核要求与最长运行时间。
27. `PUBLISH_AGENT_REMINDER` 的 APL 与申请方式。

### 18.5 分享 / 账号 / 分发

28. Share Kit 接收分享的 Want action/skills 配置与 `shareRecordId` 获取方式。
29. AppGallery Kit 更新能力的模块名、方法名、签名、起始 API、AGC 配置。
30. 应用市场详情页跳转 API（「应用市场推荐」）。
31. 2in1/PC 上 AppGallery 的更新行为与企业分发策略。
32. Account Kit 的具体 ArkTS 方法签名、登出、隐私政策要求。

---

## 19. 参考

### 19.1 KB 文件

- `dev/references/pdf-kit.md`
- `media/references/avplayer.md`
- `media/references/audio-kit.md`
- `media/references/image-kit.md`
- `media/references/photo.md`
- `dev/references/notification.md`
- `dev/references/live-view-kit.md`
- `system/references/background-task.md`
- `system/references/share-kit.md`
- `dev/references/account-kit.md`
- `agc/references/distribution.md`
- 补充：`dev/references/app-service-kits.md`、`dev/references/pen-kit.md`、`media/references/graphics2d.md`、`system/references/file-management.md`、`samples/references/media.md`、`dev/references/app-framework.md`、`dev/references/permissions.md`、`system/references/basic-services.md`、`system/references/device-info.md`、`dev/references/industry-practices.md`、`agc/references/monetization.md`、根 `SKILL.md`、`dev/SKILL.md`、`media/SKILL.md`、`system/SKILL.md`。

### 19.2 KB 内官方链接（本次未访问）

- PDF Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pdf-kit-overview-0000001820880921
- PDF Kit API：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/pdf-service-0000001774121286
- AVPlayer：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/avplayer-guidelines-V5
- Audio Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/audio-overview-V5
- Image Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/image-overview-0000001774121218
- PhotoAccessHelper：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/photoaccesshelper-overview-V5
- Notification Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/notification-overview
- Live View Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/live-view-kit-overview-0000001820880925
- Background Tasks：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/background-task-overview-V5
- Share Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/share-kit-overview-0000001774280634
- Account Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/account-introduction
- AGC 分发：https://developer.huawei.com/consumer/cn/doc/app/agc-help-app-0000002235710234
- AppGallery Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/store-introduction
- Preview Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/preview-introduction
- Pen Kit：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/pen-suite

---

## 20. 验证边界声明

- 本报告**只做了静态阅读**：没有安装 HarmonyOS SDK，没有编译 HAP，没有在平板/2in1/手机实机运行，没有调用 PDF Kit、AVPlayer、Notification、Reminder Agent、Share Kit、AppGallery Kit 的任何 API。
- 所有「✅」只表示 **KB 写明该能力存在**，不表示本机验证通过。
- 所有「【推断】」是工程判断，不是平台承诺；所有「【未知】」必须查官方 API 参考或实机验证后才能进入实现。
- 本报告不修改任何源码；唯一写入是本文档。
