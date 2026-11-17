# HarmonyOS 移植源码盘点 06 — Features 层（Board / PPT / Persistence / Tools / Update）

日期：2026-11-18
工作树：`feat/winui3-migration`，HEAD `0d26d63` + 未提交的 3.4.0 改动（本文一律以工作树内容为准）
范围：`src/InkCanvas.Features/` 全部 7 个源文件（BoardService.cs、Stores.cs、PptModels.cs、PptInkFileStore.cs、PptInkSession.cs、PptInteropService.cs、Tools.cs、UpdateService.cs）。为说明消费方行为，少量引用了 `src/InkCanvas.App/Views/*` 与 `src/InkCanvas.Core/*`，均单独标注。
验证边界：**本文是静态源码阅读，没有运行 Features 单测（实际也不存在），没有启动应用，没有 PowerPoint/WPS COM、截图、音视频、安装包实测。** 文中“事实”均可在所列文件行号处复核；未验证项会明确写“未验证”。

---

## 0. 给下游移植者的 12 条硬结论

1. `.icink` 有**两种容器**：单表面文件（`InkFileStore`，只有 `Version + Strokes`）与画板包（`BoardService`，`Format/Version/Background/GridSpacing/Pages[]`，页内含 `Strokes/Images/Media`）。二者扩展名都是 `.icink`，靠顶层 `Format == "inkcanvas-board"` 区分。
2. 画板包的 `Format` 嗅探是**大小写敏感**的 `JsonDocument.GetProperty("Format")`；反序列化本身大小写不敏感。
3. **画板 Stroke 子格式缺少 `HighlighterAlpha`**。同一笔画经画板保存/加载后高亮透明度回落到默认 96；单表面 `.icink` 则完整保存该字段。这是当前真实行为，移植时必须决定“保留 bug”还是“升版本修格式”。
4. `BoardService.SavePage` 只在**当前页没有图片**时写单表面 `.icink`；此时 **media（视频）不会保存**。有图片时才走画板包（包含 media）。移植时不要想当然认为“页保存一定含媒体”。
5. 画板最多 **99 页**；`GridSpacing` 默认 **40 DIP**，加载时强制夹到 **12..160**；背景是**全板共享**（Black/White/Grid/Lined），不是页属性。
6. 画板元素的 `Transform` 是 6 个 row-major 浮点 `[M11,M12,M21,M22,M31,M32]`，约定 `p' = p * M`（WPF/System.Numerics 行向量）。`Append(existing, m) = Read(existing) * m`；`RenderMatrix` 的局部偏移为 `M*t - t`。这些公式必须 1:1 移植。
7. PPT 会话（`PptInkSession`）本身是**平台无关状态机**：以稳定 `SlideID` 缓存每页墨迹、保存上次页/放映位置、抑制重复截图、发出通知。真正 Windows-only 的只有 `PptInteropService` 的 COM 适配。
8. PPT 轮询节奏：会话建议 **放映中 200ms / 非放映 1000ms**；但 `ShellController` 实际非放映用 **900ms**（初始也是 900ms）。移植时两者只能选一个作为契约，建议按 200/900 或 200/1000 明确写死。
9. 导航翻页前截图后有 **5 秒抑制窗口**，防止外部 SlideChange 再截一张；放映开始时抑制被重置。截图条件是“当前墨迹数 **严格大于** `MinimumAutomationStrokeNumber`”。
10. 自动保存快照命名 `{HHmmss}-{reason}.icink`（24 小时制），可选 `yyyy-MM-dd` 日期子目录；清理按文件 `LastWriteTime < now - max(1, 阈值天数)`，递归扫描整个根目录。
11. `CountdownTimer`（Features）**没有任何消费方**，当前计时器窗口自己实现了一套 WPF 1:1 计时逻辑（自适应 20/30/50/100ms，而 Features 类固定 100ms）。移植时不要把它当作现行契约。
12. 更新服务的 GitHub 仓库默认 `QiXinia/Ink-Canvas-HEHS`，下载后只校验**字节长度**，然后 `Process.Start` 运行 `.exe`。HarmonyOS 没有 `.exe` 安装器，此功能必须整体重新设计或放弃。

---

## 1. 文件、公开类型与消费方

### 1.1 源文件清单

| 文件 | 行数（工作树） | 命名空间 | 主要内容 |
|---|---:|---|---|
| `src/InkCanvas.Features/Board/BoardService.cs` | 592 | `InkCanvas.Features.Board` | 背景枚举、图片/视频资产、元素矩阵数学、分页画板、画板 JSON 读写 |
| `src/InkCanvas.Features/Persistence/Stores.cs` | 195 | `InkCanvas.Features.Persistence` | `SettingsStore`、`InkFileStore`（.icink）、`AutoSaveService` |
| `src/InkCanvas.Features/Ppt/PptModels.cs` | 78 | `InkCanvas.Features.Ppt` | 全部 PPT 记录/枚举/存档接口 |
| `src/InkCanvas.Features/Ppt/PptInkFileStore.cs` | 141 | `InkCanvas.Features.Ppt` | 每演示文稿的墨迹存档（manifest + 每页 .icink + Position） |
| `src/InkCanvas.Features/Ppt/PptInkSession.cs` | 368 | `InkCanvas.Features.Ppt` | PPT 墨迹状态机、通知、截图请求、存档构建 |
| `src/InkCanvas.Features/Ppt/PptInteropService.cs` | 562 | `InkCanvas.Features.Ppt` | PowerPoint/WPS COM 晚绑定适配、快照、导航、放映设置 |
| `src/InkCanvas.Features/Tools/Tools.cs` | 179 | `InkCanvas.Features.Tools` | 倒计时、抽奖、GDI+Win2D 截图 |
| `src/InkCanvas.Features/Update/UpdateService.cs` | 112 | `InkCanvas.Features.Update` | GitHub Release 检查、安装包下载/校验/启动 |

项目引用：`InkCanvas.Features.csproj` 引用 **Core、Platform、Ink**，并引用 `Microsoft.Graphics.Win2D 1.4.0` 与 `Microsoft.WindowsAppSDK.WinUI 1.8.260803003`。也就是说 Features 名义上在 Controls 之下，但**直接使用了 Win2D**（截图 PNG 编码）；分层文档写的是 `App→Features→Controls→Ink→Platform→Core`，而 Features 并不引用 Controls。

### 1.2 公开类型总表

| 命名空间 | 类型 | 种类 | 公开成员/用途 |
|---|---|---|---|
| Board | `BoardBackground` | `enum` | `Black=0, White=1, Grid=2, Lined=3` |
| Board | `BoardImageAsset` | class | `Id(Guid)`, `Name`, `Data(byte[])`, `ContentType`, `Left/Top/Width/Height(float)`, `Transform(float[6]?)`, `Copy()` |
| Board | `BoardImageGeometry` | `readonly record struct` | `(Left, Top, Width, Height)`，用于移动/缩放撤销 |
| Board | `BoardMediaAsset` | class | 同图片资产，另默认 `Width=360, Height=240`，`Copy()` |
| Board | `BoardMediaGeometry` | `readonly record struct` | 同上 |
| Board | `BoardElementMath` | static class | `Read/Write/Append/Same/TransformedBounds/RenderMatrix` |
| Board | `BoardService` | sealed class | 分页、页导航、图片/视频增删、撤销、保存/加载、`MaximumPages=99` |
| Persistence | `SettingsStore` | sealed class | `Directory`, `SettingsPath`, `Load()`, `Save(AppSettings)` |
| Persistence | `InkFileStore` | static class | `Extension=".icink"`, `Save(path, strokes)`, `Load(path)` |
| Persistence | `AutoSaveService` | sealed class | `Settings`, `SaveSnapshot(strokes, reason)`, `Prune()` |
| Ppt | `PptConnectionState` | enum | `Detached=0, Editing=1, SlideShow=2` |
| Ppt | `PptSlideShowStart` | enum | `PresentationSettings=0, FromBeginning=1, FromCurrentSlide=2` |
| Ppt | `PptPresentationInfo` | record | `Identity, Name, FullName, HasSavedPath, SlideCount, SlideWidth, SlideHeight, ApplicationProgId` |
| Ppt | `PptSlideInfo` | record | `SlideId, Index(1-based), Name, Hidden?, AdvanceOnTime?, AdvanceTime?` |
| Ppt | `PptSnapshot` | record | `State, Presentation, Slide, WindowHandle, ShowPosition, ShowState?` + `Detached` + `IsAttached/IsSlideShowRunning` |
| Ppt | `PptPresentationDetails` | record | `Presentation, Slides, AdvanceMode?` + `HasHiddenSlides/HasSlideTimings` |
| Ppt | `PptFailure` | record | `Operation, Message, ErrorCode(HRESULT)` |
| Ppt | `PptInkTransitionKind` | enum | `Begin, Slide, End` |
| Ppt | `PptScreenshotReason` | enum | `BeforeNavigation, ExternalSlideChange, Explicit` |
| Ppt | `PptInkTransition` | record | `Kind, Previous, Current, Outgoing, Incoming`（笔画均为克隆） |
| Ppt | `PptScreenshotRequest` | record | `Snapshot, InkSlide, Strokes, Reason, SuggestedPath, SaveInkAlongside` |
| Ppt | `PptPresentationNotice` | record | `Details, PreviousSlideIndex?, OfferRestoreHiddenSlides, OfferDisableTimings, LegacyInkFilesDetected` |
| Ppt | `PptStoredSlide` | record | `SlideId, SlideIndex, Strokes` |
| Ppt | `PptInkArchive` | record | `Presentation, LastSlideIndex?, LastShowPosition?, Slides, LegacyInkFilesDetected=false` |
| Ppt | `IPptInkStore` | interface | `Load(PptPresentationInfo)`, `Save(PptInkArchive)` |
| Ppt | `PptInkFileStore` | sealed class | 文件存档实现 |
| Ppt | `PptInkSession` | sealed class : IDisposable | 状态机；回调 + 4 个事件 |
| Ppt | `PptInteropService` | sealed class : IDisposable | COM 适配；`ProgIds`, `AllowWps`, `TryAttach/Poll/...` |
| Tools | `CountdownTimer` | sealed class : IDisposable | 100ms 池线程计时器（**无消费方**） |
| Tools | `LotteryService` | sealed class | `Names`, `PeopleCount=60`, `NoRepeat`, `Pick()` 等 |
| Tools | `ScreenshotService` | static class | `Capture`, `SavePngAsync`, `CaptureToFileAsync` |
| Update | `UpdateService` | sealed class : IDisposable | `Repository`, `CurrentVersion`, `CheckAsync`, `DownloadAsync`, `LaunchInstaller` |

### 1.3 消费方（工作树内实际引用）

| 消费方 | 使用的 Features API | 用途 |
|---|---|---|
| `App/Services/AppServices.cs` | `SettingsStore`, `BoardService`, `PptInteropService`, `AutoSaveService`, `LotteryService` | 组合根；`Board.Background` 由 `ThemeResolver.GetBoardTheme` + `UsingWhiteboard` 初始化；`Lottery.PeopleCount/NoRepeat` 来自设置 |
| `App/Services/ShellController.cs` | `PptInkSession`, `PptInkFileStore`, `ScreenshotService`, `InkFileStore`, `AutoSaveService` | PPT 轮询/通知/截图；保存/打开 `.icink`；清屏/退出/鼠标模式自动保存与清理 |
| `App/Services/UpdateCoordinator.cs` | `UpdateService` | 启动检查、手动检查、下载、启动安装器 |
| `App/Views/BoardWindow.cs` | `BoardService`（含 `MaximumPages`, `BoardElementMath`, 背景处理） | 画板 UI、页导航、插入图片/视频、保存/打开、选区变换、纸张绘制 |
| `App/Views/BoardImageLayer.cs` | `BoardImageAsset`, `BoardElementMath` | 图片视图、移动/缩放、选区 |
| `App/Views/BoardMediaLayer.cs` | `BoardMediaAsset`, `BoardElementMath` | 视频视图、MediaPlayerElement、移动/缩放 |
| `App/Views/LotteryWindow.cs` | `LotteryService` | 抽奖 UI、名单导入、NoRepeat 下调用 `Pick()` |
| `App/Views/CountdownWindow.cs` | **不引用** `CountdownTimer` | 自己实现计时窗口 |
| `App/Views/SettingsWindow.cs` | `AppSettings`、`_services.Ppt.AllowWps` | 设置页 |
| `App/Views/PptNavigationWindow.cs` | `PptSnapshot` | 四个放映导航面板 |

---

## 2. 持久化格式全表

### 2.1 单表面 `.icink`（`InkFileStore`）

实现：`Persistence/Stores.cs:66-140`。扩展名常量 `.icink`；格式版本 `FormatVersion = 1`（只写，不在读取时校验）。

**序列化选项**：`JsonSerializer.Serialize(model)` 使用 **默认选项** → 无缩进、PascalCase 属性名、大小写敏感读取、`byte` 数字、`uint` 数字、`float` JSON number。

**JSON schema**

```json
{"Version":1,"Strokes":[{"Color":4278190080,"Width":2.5,"Highlighter":false,"HighlighterAlpha":96,"Taper":1,"Shape":false,"Points":[[10.0,20.0,0.5,2.5,0.0]]}]}
```

| 字段 | 类型 | 默认/说明 |
|---|---|---|
| `Version` | int | 写入 1；读取时**完全不校验** |
| `Strokes` | array | 笔画数组 |
| `Strokes[].Color` | uint32 (decimal) | 打包 ARGB，`A<<24 | R<<16 | G<<8 | B`；黑色 = 4278190080 |
| `Strokes[].Width` | float | `StrokeAttributes.BaseWidth`，全宽 DIP |
| `Strokes[].Highlighter` | bool | 是否荧光笔 |
| `Strokes[].HighlighterAlpha` | byte | 默认 96；荧光笔透明度（设置范围 20–240，步长 20） |
| `Strokes[].Taper` | int | `TaperKind`：`None=0, Tail=1, Velocity=2` |
| `Strokes[].Shape` | bool | `InkStroke.IsRecognizedShape` |
| `Strokes[].Points` | array of float[5] | 每行 `[x, y, pressure, width, timeMs]` |

**读取算法（精确）**

1. 文件不存在 → 返回空列表（不抛异常）。
2. `JsonSerializer.Deserialize<FileModel>`；`Strokes == null` → 空列表。**文件损坏会抛 `JsonException` 给调用方**（本方法不 catch；与 `SettingsStore.Load` 的容错行为不同）。
3. 每笔：`Color = InkColor.FromPackedArgb(Color)`, `BaseWidth = Width`, `IsHighlighter = Highlighter`, `HighlighterAlpha = HighlighterAlpha`, `TaperKind = (TaperKind)Taper`（**不校验枚举值**，非法值会在后续 `TaperProfile.Create` 中表现为未知；与画板路径不同）。
4. 每行 `Where(row.Length >= 4)`：`InkPoint(row[0], row[1], row[2], row[3], row.Length > 4 ? row[4] : 0f)`。**宽度原样恢复，绝不重跑 taper**（注释明确：重算会改变历史渲染）。
5. `stroke.Complete()`；返回列表。

**写入算法**：逐笔填 `StrokeModel`（含 `HighlighterAlpha`）；每点写 5 个 float；`Directory.CreateDirectory(dir)`（若路径有目录）；`File.WriteAllText`。**没有临时文件、没有原子替换**。路径为空目录时直接写当前目录。

**需要移植的隐含契约**

- 字段名使用 C# 属性名，不是 camelCase。ArkTS 不能依赖自动命名策略，必须显式映射。
- 读取不校验 `Version`，因此未来版本字段要向前兼容处理。
- 笔画 `Id` 不落盘；加载后每笔重新生成 `Guid.NewGuid()`。
- JSON 数值用浮点文本（System.Text.Json 最短往返格式）；黄金向量必须用宽松浮点比较（建议 1e-4 或逐位打印后比较）。

### 2.2 画板包 / 页面包（`BoardService`）

实现：`BoardService.cs:136-592`。常量：

- `MaximumPages = 99`
- `BoardFormat = "inkcanvas-board"`
- `BoardFormatVersion = 1`
- JSON 选项：`WriteIndented = true`，`PropertyNameCaseInsensitive = true`
- 顶层背景默认 `Black`；`GridSpacing = 40f`

**JSON schema**

```json
{
  "Format": "inkcanvas-board",
  "Version": 1,
  "Background": "Black",
  "GridSpacing": 40,
  "Pages": [
    {
      "Strokes": [
        {"Color":4278190080,"Width":2.5,"Highlighter":false,"Taper":1,"Shape":false,
         "Points":[[10,20,0.5,2.5,0]]}
      ],
      "Images": [
        {"Id":"0f8fad5b-d9cb-469f-a165-70867728950e","Name":"a.png","Data":"iVBORw0KGgo...",
         "ContentType":"image/png","Left":100,"Top":100,"Width":640,"Height":360,
         "Transform":[1,0,0,1,0,0]}
      ],
      "Media": [
        {"Id":"...","Name":"v.mp4","Data":"AAAAIGZ0eXA...","ContentType":"video/mp4",
         "Left":200,"Top":200,"Width":360,"Height":240,"Transform":null}
      ]
    }
  ]
}
```

| 层级 | 字段 | 类型 | 说明/默认 |
|---|---|---|---|
| 顶层 | `Format` | string | 固定 `"inkcanvas-board"`；嗅探大小写敏感 |
| 顶层 | `Version` | int | 必须 == 1，否则 `InvalidDataException` |
| 顶层 | `Background` | string | `Background.ToString()`：`Black/White/Grid/Lined`；读取 `Enum.TryParse(ignoreCase:true)` |
| 顶层 | `GridSpacing` | float | 写入当前值；读取仅当 `> 0` 才采用；最终夹到 12..160 |
| 顶层 | `Pages` | array | 最多 99；空数组会补一页 |
| 页 | `Strokes` | array | 字段同 2.1，但**无 `HighlighterAlpha`** |
| 页 | `Images` | array | `BoardImageAsset` |
| 页 | `Media` | array | `BoardMediaAsset` |
| 资产 | `Id` | Guid string | 保留，加载后用于视图对账 |
| 资产 | `Name` | string | 默认 `"Image"` / `"Video"` |
| 资产 | `Data` | base64 string | `byte[]` 由 System.Text.Json 自动 base64 |
| 资产 | `ContentType` | string | 默认 `"image/png"` / `"video/mp4"`；媒体必须 `video/` 前缀才合法 |
| 资产 | `Left/Top/Width/Height` | float | DIP；图片默认 0/0/0/0，视频默认宽 360 高 240 |
| 资产 | `Transform` | float[6] 或 null | 行主序 `[M11,M12,M21,M22,M31,M32]`；null = 单位阵；**图片/视频都有** |

**保存算法（`SaveBoardFile`）**

1. 组 `BoardFile`：`Format/Version/Background(ToString)/GridSpacing/Pages`。
2. 每页索引 → `Strokes = Document.Strokes.Select(ToStrokeFile)`，`Images/Media = Copy()`（深拷贝）。
3. `Directory.CreateDirectory(dir)`（若路径有目录）。
4. `temporary = path + ".tmp"`；`File.WriteAllText(tmp, Serialize(file, Options))`；`File.Move(tmp, path, overwrite: true)`。
5. 失败向上抛，由 UI 捕获提示。

`SavePage(path)` 分支（重要）：**当前页 `Images.Count == 0` 时只调用 `InkFileStore.Save(path, Current.Document.Strokes)`**；否则 `SaveBoardFile(path, new[]{CurrentIndex})`。因此：
- 只有墨迹 → 纯 `.icink`（无 Format 字段）。
- 无图片但有视频 → 仍走纯 `.icink`，**视频丢失**。
- 有图片 → 画板包（含视频）。

`SaveBoard(path)` = `SaveBoardFile(path, 0..PageCount-1)`，总是画板包。

**读取算法**

`LoadPage(path)`：
1. `TryReadBoardFile(path, out model)`：
   - 文件不存在 → false。
   - `JsonDocument.Parse(File.ReadAllText(path))`；`RootElement.GetProperty("Format")` **大小写敏感**，值不等于 `"inkcanvas-board"` → false。
   - 反序列化；null → `InvalidDataException("The board file is empty or invalid.")`。
   - `Version != 1` → `InvalidDataException($"Unsupported board file version {model.Version}.")`。
2. 是画板包 → 取 `model.Pages?.FirstOrDefault() ?? new BoardPageFile()`；`ReplacePage(CurrentIndex, page)`。**忽略 Background/GridSpacing/后续页**。
3. 否 → 按单表面 `.icink` 读 `Strokes`，包成单页。
4. `ReplacePage`：清空文档/历史/选区，`ViewTransform = Identity`；加入 strokes；images/media 过滤非法项后深拷贝替换。
5. 触发 `PageChanged`、`ImagesChanged`；背景不变。

`LoadBoard(path)`：
1. 是画板包：
   - `loadedPages = model.Pages ?? new()`；为空补一页。
   - `loadedPages.Count > 99` → `InvalidDataException("A board can contain at most 99 pages.")`（**在清空当前画板前抛出**）。
   - `Enum.TryParse(model.Background, true)` 成功才覆盖背景。
   - `model.GridSpacing > 0` 才覆盖网格间距。
2. 否 → 单页（`InkFileStore.Load`）。
3. Dispose 所有旧页；清空三个列表；逐页新建 `InkController`，`AddRange(strokes)`，images/media 过滤后深拷贝。
4. `CurrentIndex = 0`；`GridSpacing = Clamp(gridSpacing, 12f, 160f)`。
5. 背景与当前不同才置位并发 `BackgroundChanged`；随后 `PageChanged`、`ImagesChanged`。

**画板笔画往返差异（必须决策）**

- `ToStrokeFile` 不写 `HighlighterAlpha`；`FromStrokeFile` 不读它 → 默认 96。
- `FromStrokeFile` 对 `Taper` 做 `Enum.IsDefined` 校验，非法值回退 `None`（单表面 `.icink` 不做）。
- `FromStrokeFile` 每行仅接受长度 ≥ 4；第 5 个元素缺失时 `timeMs = 0`。
- 加载后 `Complete()`；`Id` 重新生成。

### 2.3 `Settings.json`（`SettingsStore` + `AppSettings`）

实现：`Stores.cs:7-55` + `Core/Settings/AppSettings.cs`。

- 默认目录：`Environment.GetFolderPath(SpecialFolder.ApplicationData)/InkCanvas`（Windows = `%APPDATA%\InkCanvas`）。
- 路径：`{Directory}/Settings.json`。
- 读取：文件不存在 → `new AppSettings()`；否则 `JsonSerializer.Deserialize<AppSettings>(json, Options)`，`Options = { WriteIndented = true, PropertyNameCaseInsensitive = true }`；**任何异常都回落到全新默认设置**。
- 写入：`CreateDirectory` → 写 `Settings.json.tmp` → `File.Move(tmp, Settings.json, overwrite:true)`；**任何异常吞掉**（磁盘满/被锁时继续用内存设置）。
- 所有字段均有 `[JsonPropertyName]`，顶层键为小写/驼峰；枚举以**数字**序列化（没有 `JsonStringEnumConverter`）。
- `AppSettings.Clone()` 用默认 `JsonSerializer` 序列化再反序列化（属性名特性同时生效）。

#### 顶层键

| JSON 键 | C# 属性 | 类型 |
|---|---|---|
| `advanced` | `Advanced` | AdvancedSettings |
| `appearance` | `Appearance` | AppearanceSettings |
| `automation` | `Automation` | AutomationSettings |
| `behavior` | **`PowerPoint`** | PowerPointSettings（历史键名，勿改） |
| `canvas` | `Canvas` | CanvasSettings |
| `gesture` | `Gesture` | GestureSettings |
| `inkToShape` | `InkToShape` | InkToShapeSettings |
| `startup` | `Startup` | StartupSettings |
| `randSettings` | `Rand` | RandSettings |

#### `advanced`

| JSON 键 | C# | 类型 | 构造默认 | “重置为推荐” |
|---|---|---|---:|---:|
| `isSpecialScreen` | IsSpecialScreen | bool | false | true |
| `isQuadIR` | IsQuadIR | bool | false | false |
| `touchMultiplier` | TouchMultiplier | double | 0.25 | 0.3 |
| `nibModeBoundsWidth` | NibModeBoundsWidth | int | 10 | 5 |
| `fingerModeBoundsWidth` | FingerModeBoundsWidth | int | 30 | 20 |
| `nibModeBoundsWidthThresholdValue` | NibModeBoundsWidthThresholdValue | double | 2.5 | 2.5 |
| `fingerModeBoundsWidthThresholdValue` | FingerModeBoundsWidthThresholdValue | double | 2.5 | 2.5 |
| `nibModeBoundsWidthEraserSize` | NibModeBoundsWidthEraserSize | double | 0.8 | 0.8 |
| `fingerModeBoundsWidthEraserSize` | FingerModeBoundsWidthEraserSize | double | 0.8 | 0.8 |
| `isEnableEdgeGestureUtil` | IsEnableEdgeGestureUtil | bool | false | false |
| `isLogEnabled` | IsLogEnabled | bool | true | true |
| `isSecondConfimeWhenShutdownApp` | IsSecondConfimeWhenShutdownApp | bool | false | false |

#### `appearance`

| JSON 键 | C# | 类型 | 构造默认 | 重置 |
|---|---|---|---:|---:|
| `isEnableDisPlayFloatBarText` | IsEnableDisPlayFloatBarText | bool | false | false |
| `isEnableDisPlayNibModeToggler` | IsEnableDisPlayNibModeToggler | bool | true | false |
| `isColorfulViewboxFloatingBar` | IsColorfulViewboxFloatingBar | bool | false | false |
| `enableViewboxFloatingBarScaleTransform` | FloatingBarScale | double | 100.0 | 100.0 |
| `enableViewboxBlackBoardScaleTransform` | BlackboardScale | double | 100.0 | 100.0 |
| `floatingBarBottomMargin` | FloatingBarBottomMargin | double | 100.0 | 100.0 |
| `isTransparentButtonBackground` | IsTransparentButtonBackground | bool | true | true |
| `isShowExitButton` | IsShowExitButton | bool | true | true |
| `isShowEraserButton` | IsShowEraserButton | bool | true | true |
| `isShowHideControlButton` | IsShowHideControlButton | bool | false | false |
| `isShowLRSwitchButton` | IsShowLRSwitchButton | bool | false | false |
| `isShowModeFingerToggleSwitch` | IsShowModeFingerToggleSwitch | bool | true | true |
| `theme` | Theme | int | 0 | 0 |

#### `canvas`

| JSON 键 | C# | 类型 | 构造默认 | 重置 |
|---|---|---|---:|---:|
| `inkWidth` | InkWidth | double | 2.5 | 2.5 |
| `inkAlpha` | InkAlpha | int | 140 | 80 |
| `isCompressPicturesUploaded` | IsCompressPicturesUploaded | bool | false | false |
| `isShowCursor` | IsShowCursor | bool | false | false |
| `inkStyle` | InkStyle | int（0 Tail / 1 Velocity / 2 None） | 0 | 0 |
| `eraserSize` | EraserSize | int | 2 | 1 |
| `eraserType` | EraserType | int（0 图标切换 / 1 面积 / 2 墨迹） | 0 | 0 |
| `hideStrokeWhenSelecting` | HideStrokeWhenSelecting | bool | true | false |
| `usingWhiteboard` | UsingWhiteboard | bool | false | false |
| `hyperbolaAsymptoteOption` | HyperbolaAsymptoteOption | enum `OptionalOperation`（Yes=0/No=1/Ask=2） | Ask | Yes |

消费：`AppServices.ApplySettings` 把 `InkWidth` 夹到 0.5..60、`InkAlpha` 夹到 20..240（`byte`），`EraserSize` 索引到 `45*{0.5,0.8,1,1.25,1.8}`。

#### `gesture`

| JSON 键 | C# | 类型 | 默认 | 重置 |
|---|---|---|---:|---:|
| `isEnableMultiTouchMode` | IsEnableMultiTouchMode | bool | true | true |
| `isEnableTwoFingerZoom` | IsEnableTwoFingerZoom | bool | true | false |
| `isEnableTwoFingerTranslate` | IsEnableTwoFingerTranslate | bool | true | true |
| `AutoSwitchTwoFingerGesture` | AutoSwitchTwoFingerGesture | bool | true | true |
| `isEnableTwoFingerRotation` | IsEnableTwoFingerRotation | bool | false | false |
| `isEnableTwoFingerRotationOnSelection` | IsEnableTwoFingerRotationOnSelection | bool | false | true |
| `matrixTransformCenterPoint` | MatrixTransformCenterPoint | enum 0 画布中心 / 1 选区中心 / 2 手势中心 | 0 | 0 |

注意：`AutoSwitchTwoFingerGesture` 的 JSON 键首字母大写（历史遗留）；`IsEnableTwoFingerGesture` 与 `IsEnableTwoFingerGestureTranslateOrRotation` 标了 `[JsonIgnore]`，**不落盘**。

#### `behavior`（PowerPointSettings）

| JSON 键 | C# | 类型 | 默认 | 重置 |
|---|---|---|---:|---:|
| `isShowPPTNavigationBottom` | IsShowPPTNavigationBottom | bool | true | false |
| `isShowPPTNavigationSides` | IsShowPPTNavigationSides | bool | true | true |
| `isShowBottomPPTNavigationPanel` | IsShowBottomPPTNavigationPanel | bool | true | true |
| `isShowSidePPTNavigationPanel` | IsShowSidePPTNavigationPanel | bool | true | true |
| `powerPointSupport` | PowerPointSupport | bool | true | true |
| `isShowCanvasAtNewSlideShow` | IsShowCanvasAtNewSlideShow | bool | true | true |
| `isNoClearStrokeOnSelectWhenInPowerPoint` | IsNoClearStrokeOnSelectWhenInPowerPoint | bool | true | true |
| `isShowStrokeOnSelectInPowerPoint` | IsShowStrokeOnSelectInPowerPoint | bool | false | false |
| `isAutoSaveStrokesInPowerPoint` | IsAutoSaveStrokesInPowerPoint | bool | true | true |
| `isAutoSaveScreenShotInPowerPoint` | IsAutoSaveScreenShotInPowerPoint | bool | false | true |
| `isNotifyPreviousPage` | IsNotifyPreviousPage | bool | false | false |
| `isNotifyHiddenPage` | IsNotifyHiddenPage | bool | true | false |
| `isNotifyAutoPlayPresentation` | IsNotifyAutoPlayPresentation | bool | true | true |
| `isEnableTwoFingerGestureInPresentationMode` | IsEnableTwoFingerGestureInPresentationMode | bool | false | false |
| `isEnableFingerGestureSlideShowControl` | IsEnableFingerGestureSlideShowControl | bool | true | false |
| `isSupportWPS` | IsSupportWPS | bool | true | true |

#### `automation`

| JSON 键 | C# | 类型 | 默认 | 重置 |
|---|---|---|---:|---:|
| `isAutoFoldInEasiNote` | IsAutoFoldInEasiNote | bool | false | true |
| `isAutoFoldInEasiNoteIgnoreDesktopAnno` | … | bool | false | true |
| `isAutoFoldInEasiCamera` | … | bool | false | true |
| `isAutoFoldInEasiNote3C` | … | bool | false | false |
| `isAutoFoldInSeewoPincoTeacher` | … | bool | false | false |
| `isAutoFoldInHiteTouchPro` | … | bool | false | false |
| `isAutoFoldInHiteCamera` | … | bool | false | false |
| `isAutoFoldInWxBoardMain` | … | bool | false | false |
| `isAutoFoldInOldZyBoard` | … | bool | false | false |
| `isAutoFoldInMSWhiteboard` | … | bool | false | false |
| `isAutoFoldInPPTSlideShow` | … | bool | false | false |
| `isAutoKillPptService` | IsAutoKillPptService | bool | false | false |
| `isAutoKillEasiNote` | IsAutoKillEasiNote | bool | false | false |
| `isSaveScreenshotsInDateFolders` | IsSaveScreenshotsInDateFolders | bool | false | false |
| `isAutoSaveStrokesAtScreenshot` | IsAutoSaveStrokesAtScreenshot | bool | false | true |
| `isAutoSaveStrokesAtClear` | IsAutoSaveStrokesAtClear | bool | false | true |
| `isAutoClearWhenExitingWritingMode` | IsAutoClearWhenExitingWritingMode | bool | false | false |
| `minimumAutomationStrokeNumber` | MinimumAutomationStrokeNumber | int | 0 | 0 |
| `autoSavedStrokesLocation` | AutoSavedStrokesLocation | string | `D:\Ink Canvas` | `D:\Ink Canvas` |
| `autoDelSavedFiles` | AutoDelSavedFiles | bool | false | **保留原值** |
| `autoDelSavedFilesDaysThreshold` | AutoDelSavedFilesDaysThreshold | int | 15 | **保留原值** |

`IsEnableAutoFold` 为 `[JsonIgnore]` 计算属性（任一 auto-fold 开关为真）。

#### `inkToShape` / `startup` / `randSettings`

| 节 | JSON 键 | C# | 类型 | 默认 | 重置 |
|---|---|---|---|---:|---:|
| `inkToShape` | `isInkToShapeEnabled` | IsInkToShapeEnabled | bool | true | true |
| `startup` | `isAutoUpdate` | IsAutoUpdate | bool | true | true |
| `startup` | `isAutoUpdateWithSilence` | IsAutoUpdateWithSilence | bool | false | true |
| `startup` | `isAutoUpdateWithSilenceStartTime` | AutoUpdateWithSilenceStartTime | string | `"00:00"` | `"18:20"` |
| `startup` | `isAutoUpdateWithSilenceEndTime` | AutoUpdateWithSilenceEndTime | string | `"00:00"` | `"07:40"` |
| `startup` | `isEnableNibMode` | IsEnableNibMode | bool | false | false |
| `startup` | `isFoldAtStartup` | IsFoldAtStartup | bool | false | false |
| `randSettings` | `peopleCount` | PeopleCount | int | 60 | 60 |
| `randSettings` | `isNotRepeatName` | IsNotRepeatName | bool | false | false |

### 2.4 PPT 墨迹存档（`PptInkFileStore`）

目录：`{AutoSaveRoot}/Auto Saved - Presentations/{SafeFilePart(Presentation.Name)}_{Max(0, SlideCount)}/`。
`SafeFilePart`：空白 → `"Presentation"`；把 `Path.GetInvalidFileNameChars()`、`/`、`\` 全部替换为 `_`。

文件：

| 文件 | 内容 |
|---|---|
| `InkCanvas-Ppt.json` | manifest（缩进 JSON） |
| `slide-{SlideId}.icink` | 该页笔画，格式同 2.1（含 `HighlighterAlpha`） |
| `Position` | 纯文本整数，**旧版兼容**的最后页索引 |

manifest schema：

```json
{
  "Version": 1,
  "LastSlideIndex": 5,
  "LastShowPosition": 8,
  "Slides": [
    {"SlideId": 256, "SlideIndex": 1, "File": "slide-256.icink"}
  ]
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `Version` | int | 写 1；**读取不校验** |
| `LastSlideIndex` | int? | 上次的**演示文稿页索引**（1-based） |
| `LastShowPosition` | int? | 上次的**放映位置**（1-based，自定义放映可能不同于页索引） |
| `Slides` | array | 已缓存页 |
| `Slides[].SlideId` | int | 稳定页 ID |
| `Slides[].SlideIndex` | int | 保存时的页索引 |
| `Slides[].File` | string | 必须是不含路径的 `*.icink` 文件名 |

读取算法：
1. 目录不存在 → `PptInkArchive(presentation, null, null, empty)`。
2. 检测旧格式：目录顶层存在 `*.icstk` 或 `*.icart` → `hasLegacy = true`。
3. manifest 存在则尝试反序列化；`Slides != null` 时取 `LastSlideIndex/LastShowPosition`，逐条：
   - `SafeManifestFile`：非空、无路径分隔、以 `.icink` 结尾、`GetFullPath` 后仍位于目录内，否则跳过。
   - 文件不存在跳过；单页读取异常只跳过该页（其余页仍可用）。
4. 若 manifest 没给出 `LastSlideIndex`（为 null），且 `Position` 文件存在、`int.TryParse` 且 > 0 → `lastIndex = lastPosition = 该值`（旧语义：位置即页索引）。
5. 返回 archive，`LegacyInkFilesDetected = hasLegacy`。

写入算法：
1. 创建目录；尽力读取旧 manifest（失败当作没有）。
2. 新 manifest：`Version=1`，写入 `LastSlideIndex/LastShowPosition`。
3. 逐条 `PptStoredSlide`：**跳过** null / `SlideId <= 0` / `SlideIndex <= 0` / `Strokes == null || Count == 0`；其余写 `slide-{SlideId}.icink` 并加入 manifest。
4. manifest 写 `.tmp` 后 `File.Move(overwrite:true)`。
5. 若 `LastSlideIndex > 0`，直接写 `Position`（非原子）。
6. 从旧 manifest 中删除“本次未保留”的页文件（`SafeManifestFile` 防目录穿越）；旧 manifest 不可读时**不删除**任何文件；旧 manifest 未列出的孤儿文件也永远不清理。

已知碰撞：目录只用 `Name + SlideCount` 作键，**同名同页数的两个演示文稿会共享墨迹**（注释承认与 WPF 一致）。`HasSavedPath/FullName` 不参与目录键。

### 2.5 自动保存快照（`AutoSaveService`）

- 阈值：`list.Count < Math.Max(0, Settings.MinimumAutomationStrokeNumber)` 时返回 null；**等于阈值会保存**。
- 根目录：`Settings.AutoSavedStrokesLocation`；空白 → null。
- 子目录：`IsSaveScreenshotsInDateFolders ? root/yyyy-MM-dd : root`。
- 文件名：`{DateTime.Now:HHmmss}-{reason}.icink`；`reason` 直接拼入（调用方传 `screenshot`、`mouse-mode`、`ppt-mouse-mode`、`exit`、`board-next`、`board-add`、`board-delete`、`board-clear`、`board-clone`、`board-exit` 等）。
- 写入：`InkFileStore.Save`（非原子）；异常返回 null。
- `Prune()`：`AutoDelSavedFiles` 为假 → 0；根目录不存在 → 0；`cutoff = DateTime.Now.AddDays(-Math.Max(1, DaysThreshold))`；递归枚举 `*.icink`，`File.GetLastWriteTime(file) < cutoff` 时删除；单个文件异常跳过；返回删除数。

### 2.6 窗口位置持久化：不存在

任务描述提到“窗口位置持久化格式”，但工作树中**没有任何窗口位置落盘代码**。`App/Services/WindowChrome.cs` 只有内存中的 `ConditionalWeakTable<Window, Placement>`：

- `Kind 0` = 已应用的物理像素矩形；
- `Kind 1` = DIP 矩形，首次激活时按真实 DPI 重放；
- `Kind 2` = 以 DIP 指定尺寸居中到主屏工作区。

进程退出即丢失。HarmonyOS 若需要窗口/Stage 位置恢复，需要**新增**持久化设计，不能从本层直接移植。参见开放问题 Q4。

---

## 3. 画板（Board）功能

### 3.1 数据模型与常量

- 每页拥有独立 `InkController`（文档、撤销栈容量 **200**、选区、`ViewTransform`）。
- 页列表、图片列表、媒体列表三者**按索引平行**；任何增删页必须同步三者。
- `HasContent` = 任一页有笔画 **或** 任一页有图片 **或** 任一页有媒体。
- `CurrentImages/CurrentMedia` 返回当前页集合。
- 事件：`PageChanged`、`BackgroundChanged`、`ImagesChanged`（图片与视频共用同一个“内容变化”事件）。

### 3.2 页导航算法（逐行语义）

| 方法 | 精确行为 |
|---|---|
| `NextPage()` | 非末页：`CurrentIndex++`，发 `PageChanged`；末页且 `PageCount >= 99`：什么都不做；末页且 < 99：在末尾插入新页，`CurrentIndex = PageCount-1`，发 `PageChanged` |
| `PreviousPage()` | `CurrentIndex <= 0` 返回；否则 `--` 并发事件 |
| `GoTo(index)` | 越界或与当前相同返回；否则设置并发事件 |
| `InsertPageAfterCurrent()` | `PageCount >= 99` 返回 false；在 `CurrentIndex+1` 插入，`CurrentIndex++`，发事件，返回 true |
| `RemoveCurrentPage()` | 只有一页：`Current.Clear()`、清选区、清历史、清图片、清媒体，发 `ImagesChanged` + `PageChanged`（**历史在 Clear 后又被清空，所以没有可撤销项**）；多页：`Dispose` 当前 controller，三列表同索引删除，`CurrentIndex = Clamp(0, Count-1)`，发 `PageChanged` + `ImagesChanged` |
| `SetBackground(bg)` | 相同则返回；否则赋值并发 `BackgroundChanged` |

`Next` 在末页变成“加页”的 UI 逻辑在 `BoardWindow.UpdatePageNavigation`，不在 Features：`atLast ? GlyphAddPage : GlyphNext`，标签 `"加页"` / `"下一页"`；`canNext = !atLast || PageCount < 99`。

### 3.3 图片/视频与撤销（精确）

所有元素撤销都推入**当前页** `InkController.History`，因此页撤销同时覆盖笔画与元素。

- `AddImage(image)`：校验 `image != null && Data.Length > 0 && Width > 0 && Height > 0`；否则 false。加入当前页列表；推 `DelegateAction("insert image")`：Apply = 在记录索引处插入 + `ImagesChanged`；Revert = 移除 + `ImagesChanged`。最后发 `ImagesChanged`，返回 true。
- `RemoveImage(image)`：null/不在当前页 → 返回；先移除；推 `"delete image"`：Apply = 移除，Revert = 在记录索引处插入。
- `RecordImageTransform(image, before, after)`：null/不在当前页/`before == after`（record struct 精确比较）→ 返回；推 `"transform image"`，Apply/Revert 调 `SetImageGeometry`（只改 `Left/Top/Width/Height`，不含 Transform）。
- `AddMedia/RemoveMedia/RecordMediaTransform`：同上，名称分别为 `"insert media"/"delete media"/"transform media"`；**`AddMedia` 额外要求 `IsValidMedia`**（Data 非空、宽高 > 0、`ContentType` 以 `video/` 开头，忽略大小写）。
- `RecordImageChange(image, before, name)`：`before` 是调用方在手势开始时 `Copy()` 的快照；`after = image.Copy()`；若 `SameImageState(before, after)` 直接返回。`SameImageState` 只比较 `Left/Top/Width/Height` 与 `Transform`（`BoardElementMath.Same`，逐分量容差 **1e-4f**），**不比较 Name/Data/ContentType**。推 `DelegateAction(name)`：Apply/Revert 调 `ApplyImageState`（含 `Transform = snapshot.Transform?.ToArray()`）。
- `RecordMediaChange`：同上。

注意：`AddImage`/`RemoveImage` 的 Apply 闭包捕获的是**添加时的列表与索引**，撤销/重做顺序依赖 `UndoStack` 的后进先出语义；不要在移植时改成“按对象查找后插入”而破坏重复元素行为。

### 3.4 元素变换数学（必须 1:1）

约定：行向量，`p' = p * M`；`Matrix3x2` 存储顺序 `(M11,M12,M21,M22,M31,M32)`。

| 函数 | 公式/行为 |
|---|---|
| `Read(t)` | `t` 长度 == 6 → `new Matrix3x2(t[0],t[1],t[2],t[3],t[4],t[5])`；否则 `Identity` |
| `Write(m)` | `[M11,M12,M21,M22,M31,M32]` |
| `Append(existing, m)` | `Write(Read(existing) * m)`；即新变换在已有变换之后作用（对应 WPF `TransformGroup.Children.Add`）；`existing == null` 时结果就是 `m` |
| `Same(a,b)` | 都为 null → true；一个 null → false；长度不同 → false；任一分量 `Abs(diff) > 1e-4f` → false；否则 true |
| `TransformedBounds(left,top,w,h,t)` | 四角 `(l,t),(l+w,t),(l+w,t+h),(l,t+h)` 逐个 `Vector2.Transform(c, M)`，取 AABB 返回 `Rect.FromLTRB(minX,minY,maxX,maxY)` |
| `RenderMatrix(left,top,t)` | `m = Read(t)`；`tvec=(left,top)`；`offset = Transform(tvec,m) - tvec`；返回 `new Matrix3x2(m.M11,m.M12,m.M21,m.M22,offset.X,offset.Y)`。含义：视图放在 `Canvas.Left/Top=t` 时，局部 RenderTransform（origin 0）取 M 的线性部分、偏移 `M*t - t`，最终 `final = M*(p_local + t)` |

UI 使用方式（App 层）：
- `BoardImageLayer.ApplyGeometry`/`BoardMediaLayer.ApplyGeometry`：`view.Width/Height = asset.Width/Height`；`Canvas.SetLeft/Top`；`RenderTransform = RenderMatrix(...)`。
- 拖动：`asset.Transform = Append(_snapshotAtStart.Transform, CreateTranslation(dx,dy))`（dx/dy 为指针相对拖动起点的 DIP 差）。
- 旋转/翻转/双指：`BoardWindow.TransformSelectionMatrix(local, ...)` 计算 `matrix = Translate(-center) * local * Translate(center)`，对选区内笔画 `Transform(matrix, scaleWidth)`，对图片/视频 `Transform = Append(Transform, matrix)`；整组一个 `DelegateAction`。
- 导出图片：`ElementExportTransform = Translate(left,top) * Read(transform) * offset`。

### 3.5 保存/加载（见 2.2）

补充操作语义：

- `SavePage`/`SaveBoard` 不弹确认；UI 负责文件选择。
- `LoadPage`/`LoadBoard` 会**清空当前页/整板的历史和选区**；`LoadPage` 把 `ViewTransform` 重置为单位阵。
- `LoadBoard` 失败（版本/页数）在清空前抛出，当前画板保持原状；这点对移植很重要。
- 读写文件异常由 UI 捕获并提示；Features 不自带日志。

### 3.6 画板 UI 消费方（给移植者的行为/度量）

- 三条底部浮条：WPF 行高 **55 DIP**；左右条 `Opacity=0.75`；单元格典型 **60×55**（退出/加页为 55 宽），图标区 **26×26**，中文标签字号 **12**，页码指示器 **58×55**（页码 15，标题 12）；dock 边距 `3,0,3,0`。
- 背景：暗板 `#1F1F1F`（ARGB 255,31,31,31），亮板 `#F2F2F2`（255,242,242,242）。
- 纸张：仅 `Grid/Lined` 绘制；间距 `Clamp(GridSpacing,12,160)`；线色 `ARGB(55,92,113,121)`；线宽 1。
- 画板 UI 缩放：设置 `BlackboardScale` 夹到 **50..150**（百分比）；视图缩放 `SetViewScale` 夹到 **0.5..2.0**。
- 图片层：边框 2 DIP；右下角手柄 **12×12**；缩放热区为右下 **20×20**；最小宽高 **32**；缩放保持宽高比（比较 |dx| 与 |dy| 决定主导轴）。
- 视频层：`MediaPlayerElement` `MinWidth=96, MinHeight=72`；手柄 **14×14**；热区 **22×22**；最小 **96×72**；边框 2。
- 插入上限（UI 层）：图片 > **32 MiB** 拒绝；视频 > **512 MiB** 拒绝；图片扩展名 `.png/.jpg/.jpeg/.bmp/.gif/.tif/.tiff`；视频 `.mp4/.avi/.wmv/.mov/.m4v`。
- 图片压缩：`IsCompressPicturesUploaded` 且原尺寸超过 **1920×1080** 时，按 `min(1920/w, 1080/h)` 等比缩小。
- 插入定位 `CenterAndScaleElement`：`scale = min((screenW/2)/baseW, (screenH/2)/baseH)`（**不限制上限 1，小图会放大**）；宽高至少 1；居中到画板表面。视频以 **256×256** 为基准再套同一函数。
- 选区面板：高 **104**，上边距 **50**，内边距 4，圆角 5，边框 1；默认宽 695；`top = view.Bounds.Bottom + 15`；底边预留 60；最大宽度 `max(240, 根宽度-16)`；水平居中并夹在窗口内。
- 选中颜色：九色（黑/白/红 220,38,38 / 橙 234,88,12 / …，详见 BoardWindow）。
- 图标码点（Segoe Fluent Icons，BoardWindow）：见第 7.1 节。

### 3.7 平台依赖

`BoardService` 自身：`System.Text.Json`、`System.Numerics.Matrix3x2`、`File/Directory/Path`、`InkController`（Ink 层，内部有 Platform 的手掌判定/分层窗口类型引用，但 Board 只用 Document/History/Selection/ViewTransform）。**没有直接 Win32/COM/Win2D 调用**。

消费方依赖：WinUI 3（`Canvas`、`CanvasControl`/Win2D 2D 绘制、`MediaPlayerElement`、`FileOpenPicker/FileSavePicker`、`BitmapImage`、`InMemoryRandomAccessStream`）、`Windows.Media.Playback`。HarmonyOS 对应：`Canvas`/`CanvasRenderingContext2D` 或 XComponent、`AVPlayer`/`Video`、`picker`、`fileIo`、`image`。

### 3.8 移植分类

| 子功能 | 分类 | 理由 |
|---|---|---|
| 页模型/导航/撤销动作 | **pure-logic** | 不依赖 Windows；只需把 `InkController` 换成等价的页文档接口；建议黄金向量钉 99 页、末页加页、删唯一页等 |
| 元素矩阵数学 | **pure-logic** | 纯 `Matrix3x2`；高价值黄金向量（Append/RenderMatrix/TransformedBounds/Same） |
| 画板/页 JSON 格式 | **pure-logic** | Schema + 读写规则；文件 IO 是薄适配 |
| 图片/视频模型（字节、几何、Transform） | **pure-logic** | 字节处理与平台无关 |
| 图片/视频播放与选择器 | **platform-bound**（在 App 层） | WinUI MediaPlayerElement / WinRT 流；HarmonyOS 需 AVPlayer + 文件选择 |
| 大文件 base64 内嵌 | **adapt** | 512 MiB 视频 → JSON base64 膨胀约 33%；HarmonyOS 内存/性能需评估 |

---

## 4. PPT / WPS 功能

### 4.1 模型与记录（`PptModels.cs`）

- `PptConnectionState`：`Detached=0, Editing=1, SlideShow=2`。
- `PptSlideShowStart`：`PresentationSettings=0`（F5/按文档设置）、`FromBeginning=1`（F5 从第一页）、`FromCurrentSlide=2`（Shift+F5/当前页）。
- `PptPresentationInfo.Identity`：**连接内 COM 身份**，不是文件名或页数；格式见 4.2。
- `PptSlideInfo`：`SlideId` 在重排后稳定；`Index` 是 1-based 的 `GotoSlide` 参数。
- `PptSnapshot`：
  - `State`；
  - `Presentation`/`Slide`（null 表示暂时无页，如结束画面，不代表放映结束）；
  - `WindowHandle`（放映窗口 HWND）；
  - `ShowPosition`（1-based 放映位置，自定义放映可不同于 `Slide.Index`）；
  - `ShowState`：PowerPoint `PpSlideShowState`：**1 running, 2 paused, 3 black, 4 white, 5 done**（注释）；
  - `Detached` 静态实例；`IsAttached = State != Detached`；`IsSlideShowRunning = State == SlideShow`。
- `PptPresentationDetails.HasHiddenSlides` = 任一 `Hidden == true`；`HasSlideTimings` = 任一 `AdvanceOnTime == true && AdvanceTime > 0`（**故意不看 `AdvanceMode`**，与上游检测一致）。
- `PptFailure` = `(Operation, Message, ErrorCode)`；会话层的托管异常 ErrorCode 填 0。
- `PptInkTransition` = `(Kind, Previous, Current, Outgoing, Incoming)`；所有笔画都是**脱离控制器/缓存的克隆**。
- `PptScreenshotRequest` = `(Snapshot, InkSlide, Strokes, Reason, SuggestedPath, SaveInkAlongside)`；宿主必须在替换/导航**之前同步**抓屏。
- `PptPresentationNotice` = `(Details, PreviousSlideIndex?, OfferRestoreHiddenSlides, OfferDisableTimings, LegacyInkFilesDetected)`；**不会自动执行任何动作**。
- `PptStoredSlide` = `(SlideId, SlideIndex, Strokes)`。
- `PptInkArchive` = `(Presentation, LastSlideIndex?, LastShowPosition?, Slides, LegacyInkFilesDetected=false)`。
- `IPptInkStore.Load/Save` 只接收托管快照，**永不接触 COM 对象**。

### 4.2 `PptInteropService`（Windows/COM 适配）

#### P/Invoke 与常量

```csharp
[DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
static extern int CLSIDFromProgID(string progId, out Guid clsid);
[DllImport("oleaut32.dll", PreserveSig = true)]
static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, out IntPtr punk);
```

- 默认 `ProgIds = { "PowerPoint.Application", "KWPP.Application" }`。
- `AllowWps` 默认 true。
- STA 错误码：`0x8001010E`（RPC_E_WRONG_THREAD）。
- 断连 HRESULT 集合：`0x80010108, 0x800401FD, 0x800706BA, 0x80010007, 0x80010012`。
- COM 三态：`-1` = true，`0` = false（`SafeTriState` 只把 -1 映射 true）。
- `ppShowAll = 1`；`ppShowSlideRange = 2`；`ppSlideShowManualAdvance = 1`。
- 当前页放映命令：`CommandBars.ExecuteMso("SlideShowFromCurrent")`。
- `SlideShowWindows[1]`、`Presentation.Windows[1]`、`ActiveWindow` 回退。
- `WPS` 进程名：`wpp`（`Process.GetProcessesByName("wpp")`）。

#### 线程/生命周期

- 构造时记录 `_ownerThreadId` 与 `_attachmentId = Guid.NewGuid()`。
- `VerifyThread()`：任何公开实例方法（`Snapshot`、`Poll`、`GetPresentationDetails`、导航、Detach/Dispose）都要求同一托管线程；否则 `InvalidOperationException("PptInteropService must only be used on the thread that created it.")`；已 Dispose → `ObjectDisposedException`。
- `TryAttach()` 额外要求 `Thread.CurrentThread.GetApartmentState() == STA`，否则 `Fail("Attach", ..., 0x8001010E)` 并返回 false。
- `ComScope` 收集 `Marshal.IsComObject` 的对象，Dispose 时**逆序** `Marshal.ReleaseComObject`；`Publish` 只把托管 record 暴露给宿主。

#### 附着算法

1. 已附着 → `Poll()` 后 true。
2. `!AllowWps` 且 `wpp` 进程存在 → 发布 `Detached`，false（理由：WPS 可能占用 `PowerPoint.Application` 的 ROT 项）。
3. 非 STA → 失败。
4. 遍历 `ProgIds`：跳过空白；`!AllowWps` 时跳过以 `KWPP` 开头的；`CLSIDFromProgID` 失败继续；`GetActiveObject` 失败或空指针继续；`Marshal.GetObjectForIUnknown(punk)` 得到 `_app`，释放 `punk`；设置 `_progId`；轮询一次，若 `_app` 仍非空则成功。
5. 全部失败 → 发布 `Detached`，false。

#### 快照算法（`CaptureSnapshot`）

1. `SlideShowWindows.Count > 0` → 取 `[1]` 作为放映窗口，`inShow=true`；否则编辑态。
2. `presentation = showWindow.Presentation` 或 `app.ActivePresentation`；null → `PptSnapshot.Detached`。
3. `count = presentation.Slides.Count`。
4. `identity = $"{_attachmentId:N}:{_progId}:{punk.ToInt64():X}"`，`punk = Marshal.GetIUnknownForObject(presentation)`，随后 Release。
5. `name` 每次实时读取（一次 COM 调用），保证另存为后缓存失效。
6. 若 `_cachedInfo` 的 `Identity/SlideCount/Name` 都匹配 → 复用；否则重新读 `FullName`、`Path`（非空 → `HasSavedPath`）、`PageSetup.SlideWidth/Height`，构造新 `PptPresentationInfo` 并缓存。
7. `indexKey = identity:count`；变化则清空 `_slideIndexById`。
8. 放映态：
   - `HWND = window.HWND`；
   - `View.CurrentShowPosition` → `ShowPosition`；
   - `View.State` → `ShowState`；
   - `View.Slide` 非空 → `BuildSlideInfo(..., slideId, 0, cache)`（索引从缓存/全表扫描）。
9. 编辑态：
   - `app.ActiveWindow`；`window.View.Slide`；若为空，回退 `window.Selection.SlideRange[1]`；
   - `SlideID` 与 `SlideIndex` 都可用，`BuildSlideInfo(..., indexHint=SlideIndex, cache)`；
   - `HWND = window.HWND`。
10. 返回 `PptSnapshot { State = inShow ? SlideShow : Editing, Presentation, Slide, WindowHandle, ShowPosition, ShowState }`。

#### 公开操作

| 方法 | 语义 |
|---|---|
| `Poll()` | 未附着 → `Detached`；否则 `CaptureSnapshot`；异常 → `Fail("Poll",...)`；断连 HRESULT → 释放 `_app`、清缓存、发布 `Detached`；其他异常返回旧快照 |
| `PollSlideShowState(ref bool lastKnown)` | 兼容旧壳：轮询，若 `IsSlideShowRunning` 变化则更新 ref 并返回 true |
| `IsSlideShowRunning()` / `CurrentSlideIndex()` / `SlideCount()` | 每次都 `Poll()`；后两者非放映时返回 0 |
| `GetPresentationDetails()` | 放映态用放映 presentation，否则 ActivePresentation；遍历 `Slides[1..Count]` 读 `SlideID/Name/SlideShowTransition.Hidden/AdvanceOnTime/AdvanceTime`；最后读 `SlideShowSettings.AdvanceMode`；失败 `Fail("Read presentation details")` 返回 null |
| `Next()` | `SlideShowWindows[1].Activate()`（尽力），`View.Next()` |
| `Previous()` | 同上，`View.Previous()` |
| `ExitSlideShow()` | `View.Exit()`（不激活窗口） |
| `ShowSlideNavigationPane()` | `View.SlideNavigation.Visible = true`，激活窗口 |
| `GoToSlide(index)` | `index < 1` 或未附着 → false；放映态 `SlideShowWindows[1].View.GotoSlide(index)`；编辑态优先 `ActivePresentation.Windows[1].View`（上游 MW_PPT.cs:155），回退 `ActiveWindow.View`，再 `GotoSlide` |
| `RunSlideShow(start)` | 见下 |
| `RestoreHiddenSlides()` | 遍历页，`Hidden == -1` → `Hidden = 0`（只取消隐藏，不删页） |
| `DisableSlideTimings()` | `SlideShowSettings.AdvanceMode = 1` |
| `Detach()/Dispose()` | 释放 COM，清缓存，发布 `Detached` |

`RunSlideShow` 精确流程：
- 取 `ActivePresentation`；null → false。
- `FromCurrentSlide` 先尝试 `CommandBars.ExecuteMso("SlideShowFromCurrent")`；无异常即返回 true。
- `PresentationSettings`：直接 `settings.Run()`。
- `FromBeginning`：保存 `RangeType/StartingSlide/EndingSlide`；`RangeType=1`；`Run()`；`finally` 恢复三值。
- `FromCurrentSlide` 回退：取编辑态当前页 `GetEditingSlideIndex`（`ActiveWindow.View.Slide.SlideIndex`，失败 0）；`<1` 返回 false；`RangeType=2`，`StartingSlide=current`，`EndingSlide=slides.Count`；`Run()`；`finally` 恢复。

### 4.3 `PptInkSession` 状态机

#### 构造与默认回调

构造参数：`PptInteropService interop`（非空）、`IPptInkStore store`（可空）；订阅 `interop.Failed` 并转发。

| 回调 | 默认返回值 | 宿主绑定（ShellController） |
|---|---|---|
| `CaptureCurrentInk` | 空列表 | `OverlayInk.Document.Strokes` |
| `ApplyCurrentInk` | null | 清空文档、加笔画、清选区/历史、刷新浮动条 |
| `MinimumScreenshotStrokeCount` | 0 | `Automation.MinimumAutomationStrokeNumber` |
| `AutoSavePresentationInk` | true | `PowerPoint.IsAutoSaveStrokesInPowerPoint` |
| `AutoSavePptScreenshots` | false | `PowerPoint.IsAutoSaveScreenShotInPowerPoint` |
| `NotifyPreviousPage` | false | `PowerPoint.IsNotifyPreviousPage` |
| `NotifyHiddenSlides` | true | `PowerPoint.IsNotifyHiddenPage` |
| `NotifySlideTimings` | true | `PowerPoint.IsNotifyAutoPlayPresentation` |
| `AutoSaveRoot` | `""` | `Automation.AutoSavedStrokesLocation` |
| `UseScreenshotDateFolders` | false | `Automation.IsSaveScreenshotsInDateFolders` |
| `SaveInkWithScreenshot` | false | `Automation.IsAutoSaveStrokesAtScreenshot` |

事件：`InkTransition`、`PresentationNotice`、`ScreenshotRequested`、`Failed`。

#### 轮询（`Poll`）

1. `_interop.IsAttached` 为假 → `TryAttach()`（每 tick 都尝试）。
2. `current = _interop.Poll()`。
3. `SameSlideState(_last, current)` 时：
   - 若已附着且 `SlideCount` 与上次不同 → `RefreshSlideIndexes(GetPresentationDetails())`（页增删但当前页未动）；
   - 清除过期的截图抑制；
   - `_last = current`，返回。
4. 否则：若已附着且（`Presentation.Identity` 变化 **或** `SlideCount` 变化）→ `RefreshSlideIndexes(...)`；然后 `ProcessTransition(current)`。

`SameSlideState` 比较：`State`、`Presentation.Identity`、`Slide.SlideId`、`Slide.Index`、`ShowPosition`、`WindowHandle`。**不比较 `ShowState`、`SlideCount`、`Slide.Name`**。

#### `ProcessTransition(current)`（按顺序）

记 `previous = _last`，`wasShow = _showActive`，`isShow = current.IsSlideShowRunning`，`samePresentation = SamePresentation(_loadedPresentation, current.Presentation)`，`newPresentation = current.IsAttached && !samePresentation`。

1. **结束旧放映**：若 `_showActive && (!isShow || newPresentation)`：
   - `CaptureOutgoing(previous)`；
   - 若 `AutoSavePresentationInk()` 且 `previous.IsSlideShowRunning && previous.Slide != null`：`_lastSlideIndex = previous.Slide.Index > 0 ? Index : null`，`_lastShowPosition = previous.ShowPosition > 0 ? ShowPosition : null`；
   - `Persist()`；
   - 若 `newPresentation || !isShow`：`ApplyCurrentInk(empty)`；`RaiseTransition(End, previous, current, empty)`；`_showActive=false`。
2. **新演示文稿**：若 `newPresentation`：
   - `_loadedPresentation = current.Presentation`；清 `_slides`、`_slideIndexes`、`_noticeIdentity`、`_lastSlideIndex`、`_lastShowPosition`、`_legacyFilesDetected=false`；
   - 有 store 时 `store.Load(current.Presentation)`（异常 → `Failed("Load presentation ink", msg, 0)`）；
   - 仅当 `AutoSavePresentationInk()` 才把 archive 的页载入 `_slides`（`Clone(slide.Strokes)`），并在 `SlideIndex > 0 && !存在` 时写 `_slideIndexes`；
   - 无论是否载入笔画，都取 archive 的 `LastSlideIndex/LastShowPosition/LegacyInkFilesDetected`。
3. **放映中**：
   - `began = !_showActive || newPresentation`；
   - `slideChanged = previous.IsSlideShowRunning && isShow && (previous.Slide?.SlideId != current.Slide?.SlideId || previous.Presentation?.Identity != current.Presentation?.Identity)`；
   - `began`：`_showActive=true`；**抑制时间重置为 `DateTime.MinValue`**；`ApplySlide(Begin)`；`OfferPresentationNotice(current)`；
   - 否则 `slideChanged`：`suppressDuplicateScreenshot = UtcNow <= _suppressTransitionScreenshotUntilUtc`；清零抑制；若未抑制且 `AutoSavePptScreenshots()` 且 `ShouldSaveScreenshot()` → `RaiseScreenshot(ExternalSlideChange, current)`；`ApplySlide(Slide)`。
4. **非放映的新文稿**：若 `newPresentation && !wasShow` → `OfferPresentationNotice(current)`。
5. `_last = current`。

#### `ApplySlide(kind, current, previous)`

1. `CaptureOutgoing(previous)`；
2. `incoming = current.Slide != null && _slides.TryGetValue(SlideId) ? Clone(...) : empty`；
3. `ApplyCurrentInk(Clone(incoming))`（宿主替换当前屏幕墨迹）；
4. `Slide.Index > 0` → 更新 `_slideIndexes[SlideId]`；
5. `_lastSlideIndex = Slide.Index > 0 ? Index : 旧值`；`_lastShowPosition = ShowPosition > 0 ? ShowPosition : 旧值`；
6. `RaiseTransition(kind, previous, current, incoming)`（`outgoing` 从 `previous.Slide.SlideId` 的缓存克隆）。

`CaptureOutgoing(snapshot)` 仅在 `snapshot.IsSlideShowRunning && Slide != null && Presentation != null && _showActive` 时执行：更新索引映射，`_slides[SlideId] = Clone(CaptureCurrentInk())`；异常 → `Failed("Capture outgoing slide ink", msg, 0)`。

#### 截图请求

- `RequestScreenshot()`：仅当 `_last.IsSlideShowRunning && _last.Slide != null`；`RaiseScreenshot(Explicit)`。**不检查** `AutoSavePptScreenshots` 和笔画阈值。
- `NavigateWithScreenshot(reason, navigate)`（`Next/Previous/GoToSlide` 使用）：若放映中且 `AutoSavePptScreenshots()` 且 `ShouldSaveScreenshot()` → **先** `RaiseScreenshot(reason)` 再 `navigate()`；若导航成功，`_suppressTransitionScreenshotUntilUtc = UtcNow + 5s`；返回导航结果。
- `ShouldSaveScreenshot()`：`CaptureCurrentInk().Count > Math.Max(0, MinimumScreenshotStrokeCount())`（**严格大于**）；异常 → false。
- `RaiseScreenshot(reason, snapshot?)`：
  - `snapshot ??= _last`；无 `Slide` 或无 `Presentation` → 返回；
  - `root = AutoSaveRoot() ?? ""`；非空时：
    - `folder = root/"Auto Saved - PPT Screenshots"`；
    - `UseScreenshotDateFolders()` → 追加 `yyyy-MM-dd`；
    - `presentationPart = SafeFilePart(Presentation.Name)`；
    - `position = ShowPosition > 0 ? ShowPosition : Slide.Index`；
    - `filePosition = (reason == ExternalSlideChange && _last.ShowPosition > 0) ? _last.ShowPosition : position`；
    - `imageName = reason == Explicit ? $"{position}_{Now:HH-mm-ss}" : filePosition.ToString("0")`；
    - `path = folder/presentationPart/imageName + ".png"`；
  - `inkSlide = reason == ExternalSlideChange ? _last.Slide : snapshot.Slide`（外部切换时把**上一页**的墨迹/位置与截图配对）；
  - 触发 `ScreenshotRequested(new PptScreenshotRequest(snapshot, inkSlide, Clone(CaptureCurrentInk()), reason, path, SaveInkWithScreenshot()))`。

宿主（`ShellController`）行为：同步抓主屏 → 若 `SuggestedPath` 空则回退 `root/"Auto Saved - PPT Screenshots"/yyyy-MM-dd/{ShowPosition}_{HH-mm-ss}.png`；`Directory.CreateDirectory`；异步写 PNG；`SaveInkAlongside` 时 `Path.ChangeExtension(path, ".icink")` 写同目录墨迹。

#### 通知（`OfferPresentationNotice`）

- 未附着或 `_noticeIdentity == current.Presentation.Identity` → 返回（每个演示文稿身份只发一次）。
- `previousEnabled = NotifyPreviousPage()`；
- `details = interop.GetPresentationDetails()`；null → 返回；
- `_noticeIdentity = identity`；
- `resume = previousEnabled ? _lastSlideIndex : null`；若 `resume > 0` 但 `details.Slides` 中不存在该 `Index` → `resume = null`；
- `hidden = NotifyHiddenSlides() && details.HasHiddenSlides`；
- `timings = NotifySlideTimings() && details.HasSlideTimings`；
- 触发 `PresentationNotice(details, resume, hidden, timings, _legacyFilesDetected)`。

宿主文案（精确，来自 ShellController）：
- `$"上次播放到了第 {notice.PreviousSlideIndex} 页, 是否立即跳转"`（逗号是半角 `,`）；
- `"检测到此演示文档中包含隐藏的幻灯片，是否取消隐藏？"`；
- `"检测到此演示文档中自动播放或排练计时已经启用，可能导致幻灯片自动翻页，是否取消？"`；
- 旧格式：标题 `"发现旧版墨迹存档"`，正文 `"发现 .icstk/.icart 存档。该 WPF ISF 格式无法在 WinUI 3 直接读取；当前仅保留文件，不会覆盖或删除。"`。
- 对话框为 450×160 DIP 顶置是/否窗（`YesOrNoWindow`），是按钮宽 100，绿色 `#15803d`/hover `#166534`/pressed `#306534`，图标 U+E786。

#### 存档（`CreateArchive` / `Save` / `Persist`）

- `CreateArchive()`：无 `_loadedPresentation` → null；`CaptureOutgoing(_last)`；若放映中且有页，更新 `_lastSlideIndex/_lastShowPosition`（仅 `>0` 才覆盖）；返回 `BuildArchive()`。
- `Save()`：store 为空、无已加载文稿、或 `AutoSavePresentationInk()` 为假 → 返回；否则同上 + `Persist()`。
- `Persist()`：`store.Save(BuildArchive())`，异常 → `Failed("Save presentation ink", msg, 0)`。
- `BuildArchive()`：`(_loadedPresentation, _lastSlideIndex, _lastShowPosition, _slides.Select(kv => new PptStoredSlide(kv.Key, _slideIndexes.TryGetValue(kv.Key, out i) ? i : 0, Clone(kv.Value))).ToList(), _legacyFilesDetected)`。**索引未知的页会以 0 进入存档，随后被 `PptInkFileStore.Save` 跳过并删除旧文件**。
- `Dispose()`：先 `Save()`，再退订，置 `_disposed`。

#### 演示文稿同一性（`SamePresentation`）

1. 任一方 null → 只有都为 null 才 true。
2. `Identity` 序数相等 → true。
3. 双方都 `HasSavedPath`、`FullName` 非空 → `FullName` 忽略大小写相等。
4. 双方都无保存路径 → `SlideCount` 相等 **且** `Name` 序数相等 **且** `SlideWidth/Height` 精确相等。

### 4.4 脱离 COM 可保留的语义（HarmonyOS 关键决策）

**可原样保留（纯托管）**：

- 全部记录/枚举（4.1）；
- 每 `SlideId` 的笔画缓存与 `SlideIndex` 映射；
- `LastSlideIndex`/`LastShowPosition` 的双轨“上次位置”语义（比 WPF 单 `Position` 更精确）；
- `Begin/Slide/End` 转换事件与“开始/切页/结束”宿主时序；
- 5 秒截图抑制、显式/导航前/外部切换三种截图原因；
- 截图路径/文件名、`MinimumAutomationStrokeNumber` 严格大于阈值；
- 通知的“仅提示、不自动执行”策略与每个演示文稿只提示一次；
- 存档格式/目录/旧格式检测；
- `SamePresentation`/`SameSlideState` 判定。

**必须替换（Windows COM 专属）**：

- ROT 附着、`CLSIDFromProgID/GetActiveObject`、STA/RCW、HRESULT 断连处理；
- 读取 PowerPoint/WPS 的 presentation/slide/transition/page setup 属性；
- `Next/Previous/Exit/GotoSlide/RunSlideShow/RestoreHiddenSlides/DisableSlideTimings/SlideNavigation.Visible`；
- 放映窗口 HWND（HarmonyOS 窗口/窗口句柄语义不同）。

**HarmonyOS 可选路线**：若 HarmonyOS 上没有可编程的演示文稿宿主，则 PPT 功能只能降级为“手动分页墨迹板”（用户按页切换），或彻底放弃。应在架构决策中明确，而不是假装 COM 可以适配。

### 4.5 平台依赖清单

- P/Invoke `ole32.dll!CLSIDFromProgID`、`oleaut32.dll!GetActiveObject`。
- `System.Runtime.InteropServices.Marshal`：`GetObjectForIUnknown`、`GetIUnknownForObject`、`GetHRForException`、`Release`、`ReleaseComObject`、`IsComObject`。
- C# `dynamic` 晚绑定 COM dispatch（`Microsoft.CSharp`）。
- `Process.GetProcessesByName("wpp")`。
- `Thread.CurrentThread.GetApartmentState()` / `ApartmentState.STA`。
- 消费方：WinUI 窗口、`ContentDialog`/`YesOrNoWindow`、屏幕抓取 GDI、Win2D PNG、`File`/`Path`。

### 4.6 移植分类

| 子功能 | 分类 | 说明 |
|---|---|---|
| PPT 模型/记录/事件 DTO | **pure-logic** | 1:1，ArkTS interface/class + 显式 JSON 映射（若落盘） |
| PPT 会话状态机 | **pure-logic** | 用 `IPptHost` 接口隔离 COM；可直接写纯逻辑测试（假 host 驱动转换） |
| PPT COM 适配 | **platform-bound** | Windows-only；HarmonyOS 无对应 Office COM，建议 **drop-candidate**（若产品接受） |
| PPT 存档 | **adapt** | 文件系统/路径适配；manifest schema 可 1:1 |
| PPT 截图请求 | **pure-logic**（路径/阈值）+ **platform-bound**（抓屏） | 抓屏改用 HarmonyOS 截屏能力 |
| PPT 宿主导航面板 UI | **platform-bound** | WinUI 无激活窗口/浮层；HarmonyOS 需窗口/浮层能力 |

---

## 5. Tools

### 5.1 `CountdownTimer`（无消费方）

- 构造：`System.Threading.Timer(OnTick, null, Infinite, Infinite)`。
- `Total`：最近一次 `Start` 的时长。
- `Remaining`：运行中 `Max(0, _endTime - DateTime.UtcNow)`；暂停时 `_pausedRemaining`。
- `Start(duration)`：`Total = _pausedRemaining = duration; Resume()`。
- `Resume()`：已运行或剩余 ≤ 0 → 返回；`_endTime = UtcNow + _pausedRemaining`；`_running=true`；`_timer.Change(0, 100)` → **固定 100ms**。
- `Pause()`：未运行 → 返回；`_pausedRemaining = Remaining`；`_running=false`；停表。
- `Stop()`：`_running=false`；`_pausedRemaining=0`；停表。
- `OnTick()`：未运行返回；发 `Tick(remaining)`；`<=0` 时 `Stop()` 并发 `Finished`。
- 事件在**线程池线程**触发，调用方需自行 marshal。
- **`grep` 全仓无消费方**（CountdownWindow 自己实现）。分类：**drop-candidate**（若要 headless 计时器可作为纯逻辑参考，但当前不是契约）。

### 5.2 `LotteryService`（纯逻辑）

字段：`Names: List<string>`、`PeopleCount=60`、`NoRepeat`、`_used: HashSet<int>`、`_random: Random`。

- `Reseed(seed)`：替换 `Random`（无消费方）。
- `Reset()`：清空已用集合（无消费方）。
- `LoadNames(names)`：清空后逐个 `Trim()`，非空才加入；最后清空 `_used`。
- `Pick()`：
  1. `poolSize = Names.Count > 0 ? Names.Count : Math.Max(1, PeopleCount)`；
  2. `NoRepeat && _used.Count >= poolSize` → null；
  3. 循环：`index = _random.Next(poolSize)`，`guard++`，条件 `NoRepeat && _used.Contains(index) && guard < poolSize * 8`；
  4. `NoRepeat` → `_used.Add(index)`；
  5. 返回 `Names.Count > 0 ? Names[index] : (index + 1).ToString()`。
- 注意：`guard` 上限是 `pool*8`，极端情况下可能返回已用索引；`NoRepeat` 下 `_used` 可能超过 `poolSize`（Set 去重但不再拒绝）。移植时若要严格不重复，应改成“剩余集合随机取”，同时保留旧行为用于黄金向量。

消费方 `LotteryWindow.Roll` 的实际抽奖算法（**与 `Pick` 不同**）：
- 闪烁：5 次，每次 `Random.Next(1, pool+1)`，在 `pool>1` 时与本次闪烁集合去重；`flicker.Count >= pool` 时清空；显示到 `_out1`；`await Task.Delay(150)`。
- 最终：
  - `NoRepeat`：循环 `_totalCount` 次调用 `_lottery.Pick()`，null 提前 break；
  - 非 `NoRepeat`：新 `Random`，每次 `Next(1,pool+1)`，在本次抽奖 `used` 集合内去重，`DisplayName(r)`。
- 输出拆分：0 个 → `"已抽完"`；`_totalCount <= 5` → 单标签换行拼接；`<=10` → 两份（`split=(n+1)/2`）；`>10` → 三份（`a=(n+1)/3`, `b=(n+1)*2/3`）。
- `autoClose`：加载后 100ms 自动 `Roll()`；结果后 1500ms 关闭。
- 名单文件：`%APPDATA%\InkCanvas\Names.txt` 每行一人；`Replace.txt` 规则 `old-->new` 整行匹配、按顺序应用；空行跳过；文件缺失/不可读 → 数字模式 1..PeopleCount。

分类：**pure-logic**。抽奖 UI 度量见 8.4。

### 5.3 `ScreenshotService`

`Capture(x,y,width,height)`：
1. `width <= 0 || height <= 0` → `(empty,0,0)`。
2. `screenDc = GetDC(NULL)`；`memDc = CreateCompatibleDC(screenDc)`；`bmp = CreateCompatibleBitmap(screenDc,width,height)`；`old = SelectObject(memDc,bmp)`。
3. `BitBlt(memDc,0,0,width,height,screenDc,x,y,0x00CC0020 /* SRCCOPY */)`。
4. `BITMAPINFO { biSize=sizeof, biWidth=width, biHeight=-height, biPlanes=1, biBitCount=32, biCompression=0 }`（负高度 = 自顶向下）。
5. `pixels = new byte[width*height*4]`；`GetDIBits(memDc,bmp,0,height,pixels,ref bi,0)`。
6. 清理：`SelectObject(memDc,old)`、`DeleteObject(bmp)`、`DeleteDC(memDc)`、`ReleaseDC(NULL,screenDc)`。
7. 返回 `(BGRA bytes, width, height)`。

`SavePngAsync(path,bgra,w,h)`：创建目录；`CanvasDevice.GetSharedDevice()`；`CanvasBitmap.CreateFromBytes(device,bgra,w,h, DirectXPixelFormat.B8G8R8A8UIntNormalized)`；`bitmap.SaveAsync(path, CanvasBitmapFileFormat.Png)`。

`CaptureToFileAsync(directory,x,y,w,h,dateFolders=false)`：`Capture`；宽 0 → null；目录可选 `yyyy-MM-dd`；文件名 `Screenshot-{HHmmss}.png`；`SavePngAsync`；返回路径。

分类：**platform-bound**（Win32 GDI + Win2D + WinRT 编码）。HarmonyOS 需 `@ohos.screenCapture`/`AVScreenCapture` 或 `display` 截图 + `image` 编码；权限/用户授权是平台差异。

---

## 6. Update

### 6.1 精确行为

- 构造：`Repository = "QiXinia/Ink-Canvas-HEHS"`；`HttpClient { Timeout = 30s }`；`User-Agent: InkCanvas-Updater`；`Accept: application/vnd.github+json`。
- `CurrentVersion`：`Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3,0,0)`；`Build/Revision < 0` → 0；转 `AppVersion(Major,Minor,Patch,Build)`。
- `CheckAsync`：
  - URL `https://api.github.com/repos/{Repository}/releases?per_page=20`；
  - `using response = await _http.GetAsync(url, cancellation)`；非 2xx → null；
  - `ReleaseFeed.ParseNewestStable(json)`；
  - 捕获 `HttpRequestException/TaskCanceledException/OperationCanceledException` → null。
- `ReleaseFeed.ParseNewestStable`（Core）：必须是 array；跳过 `draft==true` 或 `prerelease==true`；每个 release 要求 `tag_name` 可解析为 `AppVersion`，且有 `.exe` 资产；优先名字含 `setup`（忽略大小写）的资产，否则第一个 `.exe`；取最高版本。
- `DownloadAsync(release, progress, cancellation)`：
  - release 为空/`InstallerUrl` 空 → null；
  - 目录 `%TEMP%/InkCanvas/update`；
  - 文件名取 URL 路径最后一段；空白 → `InkCanvas-{Tag}-Setup.exe`；
  - GET `ResponseHeadersRead`；非 2xx → null；
  - `total = ContentLength ?? InstallerSize`；
  - 80 KiB（**81920**）缓冲循环写文件；`total > 0` 时 `progress.Report((double)read/total)`；
  - flush；
  - `InstallerSize > 0 && read != InstallerSize` → 删除并返回 null（**只校验长度，无哈希/签名**）；
  - 返回本地路径。
- `LaunchInstaller(path)`：路径为空/不存在 → false；`Process.Start(new ProcessStartInfo(path){ UseShellExecute = true })`；异常 → false。**调用方必须先取得用户同意或静默策略允许**。

### 6.2 策略（Core `UpdatePolicy`，有测试）

`MayInstallSilently(settings, now)`：
- `IsAutoUpdate` 或 `IsAutoUpdateWithSilence` 为假 → false；
- 时间解析失败 → false（询问用户）；
- `start == end` → true（无限制，默认 00:00–00:00）；
- 分钟数 `m = now.Hour*60 + now.Minute`；
- `start < end`：`m >= start && m < end`；跨午夜：`m >= start || m < end`。

### 6.3 宿主流程（`UpdateCoordinator`）

1. `CheckAsync`；null → 交互式提示 `"无法获取更新信息，请检查网络或稍后再试。"`。
2. 不新于当前 → 交互式 `"已是最新版本（{CurrentVersion}）。"`。
3. `silent = UpdatePolicy.MayInstallSilently(Settings.Startup, DateTime.Now)`；非静默 → 确认框，消息包含 `发现新版本 {Tag}（当前 {Version}）。\n\n{Trim(Notes)}\n\n下载并安装？安装前会关闭本程序。`；`Trim` 把换行统一为 `\n`、trim、最多 600 字符 + `…`。
4. 下载失败 → `"安装包下载失败或校验不通过，未执行任何安装。"`。
5. 启动失败 → `"无法启动安装程序。"`。
6. 成功 → `ExitForInstall()`（退出应用）。

### 6.4 平台依赖与分类

依赖：`HttpClient`、`System.Reflection.Assembly`、`Process.Start(UseShellExecute)`、`Path.GetTempPath`、GitHub REST。HarmonyOS：`@ohos.net.http` / `request`、`bundleManager` 版本、`cacheDir`，**没有 `.exe` 安装器**；应改为 AppGallery/应用市场更新或自更新 HAP（受系统限制）。

分类：**adapt**（检查/解析/下载/长度校验可移植；安装器启动必须替换或放弃）。若产品不做自更新，则整块 **drop-candidate**。

---

## 7. UI 度量、颜色、图标（与本层功能直接相关的消费方）

> 这些数值在 App/Views 中，但直接钉住 Features 数据的呈现语义；移植 UI 时必须保持。

### 7.1 画板（BoardWindow / BoardImageLayer / BoardMediaLayer）

| 项目 | 值 |
|---|---|
| 底部浮条行高 | 55 DIP |
| 单元格 | 60×55（加页/退出 55 宽）；图标区 26×26；标签字号 12 |
| 页码指示器 | 58×55；页码字号 15；标题 `"页码"` 字号 12 |
| dock 边距 | `3,0,3,0`；左右条透明度 0.75 |
| 暗板背景 | ARGB(255,31,31,31) = `#1F1F1F` |
| 亮板背景 | ARGB(255,242,242,242) = `#F2F2F2` |
| 暗板浮条背景/边框 | ARGB(191,0,0,0) / ARGB(153,255,255,255) |
| 亮板浮条背景/边框 | ARGB(191,255,255,255) / ARGB(204,0,0,0) |
| 标题底 | 暗 ARGB(128,33,86,201)；亮 ARGB(64,88,139,252) |
| 悬停/按下/选中 | 暗 ARGB(54,0,205,205) / ARGB(85,0,205,205) / ARGB(90,0,102,191)；亮对应 ARGB(34/68/90,...) |
| 纸张线 | ARGB(55,92,113,121)，线宽 1 |
| 网格/横线间距 | `Clamp(GridSpacing, 12, 160)` DIP |
| 画板 UI 缩放 | `BlackboardScale` 50–150% |
| 视图缩放 | 0.5–2.0 |
| 图片边框/手柄/热区/最小 | 2 / 12×12 / 20×20 / 32 |
| 视频边框/手柄/热区/最小 | 2 / 14×14 / 22×22 / 96×72 |
| 插入上限 | 图片 32 MiB；视频 512 MiB |
| 图片压缩上限 | 1920×1080（按设置） |
| 视频基准尺寸 | 256×256 后居中缩放 |
| 选区面板 | 高 104；上边距 50；内边距 4；圆角 5；边框 1；默认宽 695；下移 15；底预留 60；最大宽 `max(240, 根宽-16)` |
| 画板格式建议名 | `Page-{CurrentIndex+1}.icink`、`InkCanvas-Board.icink` |

图标码点（Segoe Fluent Icons）：

| 语义 | 码点 |
|---|---|
| 上一页 | U+E760 |
| 下一页 | U+E761 |
| 加页 | U+ECC8 |
| 退出 | U+E944 |
| 手势 | U+F271 |
| 画布/纸型 | U+EF1F |
| 选择 | U+E958 |
| 选择角标 | U+F8AF |
| 笔 | U+EE56 |
| 荧光笔 | U+E7E6 |
| 橡皮 | U+E75C |
| 面积擦角标 | U+F0B6 |
| 墨迹擦角标 | U+F128 |
| 图形 | U+F158 |
| 插入图片 | U+EE71 |
| 撤销 | U+E7A7 |
| 重做 | U+E7A6 |
| 更多 | U+E8A9 |
| 清除/删除 | U+E74D |
| 克隆模式 | U+E8C8 |
| 衍至新页 | U+F413 |
| 粗细 | U+E70F |
| 加粗 | U+E948 |
| 变细 | U+E949 |
| 重置粗细 | U+E72C |
| 存为图片 | U+E91B |
| 颜色 | U+E790 |
| 翻转基座 | U+E71A |
| 翻转叠加 | U+F464 |
| 翻转箭头 | U+E80C |
| 旋转 | U+E7AD |
| 更多功能：倒计时 | U+EA8F |
| 随机抽选/单次抽选 | U+E8B1 |
| 保存墨迹 | U+E74E |
| 打开墨迹 | U+ED25 |
| 播放墨迹 | U+EC57 |
| 截屏 | U+E722 |
| 指南 | U+E82D |
| 设置 | U+E713 |
| 视频展台 | U+E714 |
| Desmos | U+E8EF |
| 插入视频 | U+E8B2 |
| 文件 | U+E8A5 |
| 全屏/窗口 | U+E740 |
| 多指书写 | U+E716 |
| 双指移动 | U+E7C2 |
| 双指旋转 | U+E7AD |
| 双指手势作用于选区 | U+E8B3 |

### 7.2 PPT 导航面板（PptNavigationWindow）

WPF 是 42 DIP 内容放进 40 DIP Viewbox，故统一比例 `S = 40/42 ≈ 0.95238`：

| 项目 | 值（DIP） |
|---|---|
| 单元（上一页/下一页） | 40*S = 38.095×38.095 |
| 页码单元短边 | 38*S = 36.190 |
| 图标盒 | 26*S = 24.762；字号 20*S = 19.048 |
| 页码字号 | 9*S = 8.571 |
| 分隔线长/粗 | 25*S = 23.810 / 1*S = 0.952 |
| 边框/圆角 | 1*S = 0.952 / 6*S = 5.714 |
| 面板整体 | 水平：高 40*S；宽 `(counter?122:82)*S`；垂直：宽 40*S；高 `(counter?120:82)*S` |
| 边距 | 侧边 6；左下 2；右下右边 0；底部垂直 2；侧边垂直居中 |
| 透明度 | 0.4 |
| 页码格式 | `$"{position}/{count}"`（无空格；position 取 ShowPosition > 0，否则 Slide.Index） |
| 图标 | 底部 prev/next U+EDD5/U+EDD6；侧边 U+EDD7/U+EDD8 |
| 窗口样式 | 顶置、无激活（`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`）、不抢放映焦点 |

### 7.3 PPT 提示窗（YesOrNoWindow）

- 450×160 DIP，顶置、不可缩放；标题 `"演示文档设置 - Ink Canvas Artistry"`。
- 图标 U+E786，字号 20，图标盒高 30、边距 10；文字左边距 60、右边距 10、自动换行。
- 是/否按钮宽 100；是底色 `#15803D`，hover `#166534`，pressed `#306534`，白字。
- 弹出时 900ms 巡检置顶（防止 overlay 盖住）。

### 7.4 计时器（CountdownWindow，当前真正使用的实现）

- 窗口 1100×700 DIP，最小 300×200；紧凑模式 400×250。
- 内容边距 20、圆角 10、边框 1；大视图 Grid 200×180。
- 圆环 150×150；轨道固定 `#EAEAEA`。
- 数字行高 28.5，列宽 `28,8,28,8,28`，每个数字格 14 宽，字号 26 SemiBold；冒号字号 26，上移 2。
- 调节层高 88.5；步进按钮 29×13、字号 8、圆角 2；上下两组 `+5/+1` 与 `-1/-5`，间距 2。
- 默认 00:01:00；编辑确认时若 0 则秒置 1；小时回绕 100、分/秒 60（clamp 到 0/99、0/59）。
- 圆形按钮直径 20（开始/重置/检查），右侧紧凑/全屏/关闭也是 20，字号 9/10；关闭底色用 Danger。
- 结束时间 pill 高 18、圆角 9、边框 0.5、字号 9、时钟图标 10、内边距 6/上下 23。
- 紧凑时间字号 56 Black；拖动 pill 64×15、圆角 8、底距 5。
- 计时 tick 自适应：总秒数 `<=10` → 20ms；`<=60` → 30ms；`<=120` → 50ms；否则 100ms。
- 剩余时间显示向上取整到整秒；结束播放 `Assets/TimerDownNotice.wav`。
- 结束时间文本用当前区域文化 `"t"` 格式。

### 7.5 抽奖（LotteryWindow）

- 窗口 900×500；内容边距 10、圆角 10、边框 1；列宽 `1.8* : 1*`。
- 结果标签字号 70（1/2/3 列由数量决定）；左列 Viewbox 边距 `20,0,20,0`，最小高 470，间距 50。
- 数量步进：圆形按钮直径 70；数字字号 55、宽 80、边距 30；右列间距 40。
- 抽奖按钮 200×70、圆角 10、底色 Accent；图标字号 30、文字 `"抽奖"` 字号 35；灰色滚动遮罩同尺寸。
- 导入 pill：高 40、圆角 20、边框 1、边距 `10,10,60,10`、内边距 15、间距 6、文字 16、图标 18。
- 关闭圆钮直径 40，图标字号为 18（0.45×）。
- 滚动 5×150ms；单次抽选模式 100ms 后自动开始、结果后 1.5s 关闭。
- 名单文件：`%APPDATA%\InkCanvas\Names.txt`、`Replace.txt`；导入对话框 400×500。

### 7.6 截图

- 手动截图目录：`%USERPROFILE%\Pictures\InkCanvas`（`SpecialFolder.MyPictures` + `InkCanvas`）。
- 文件名 `Screenshot-{HHmmss}.png`；可选 `yyyy-MM-dd` 子目录。
- 截图同时（设置开启时）调用 `AutoSave.SaveSnapshot(..., "screenshot")`。
- 鼠标模式/PPT 鼠标模式：若笔画数 `> MinimumAutomationStrokeNumber`，放映中请求 PPT 截图，否则手动截图；并保存 `ppt-mouse-mode` / `mouse-mode` 快照。

---

## 8. 平台依赖总表

| 文件/功能 | Windows API / 框架 | HarmonyOS 对应/风险 |
|---|---|---|
| BoardService | `System.Text.Json`、`System.Numerics.Matrix3x2`、`System.IO` | 纯逻辑可替换；JSON 手写映射；矩阵自实现或使用 `@ohos.matrix4`（语义不同，建议自写 3x2） |
| Board UI | WinUI `Canvas`、`CanvasControl`(Win2D)、`MediaPlayerElement`、`BitmapImage`、Picker、`InMemoryRandomAccessStream` | ArkUI `Canvas`/`XComponent`、`Video`/`AVPlayer`、`image`、`picker`、`fileIo` |
| Stores | `Environment.SpecialFolder.ApplicationData`、`System.IO` | `context.filesDir`/`preferences`；路径与原子替换策略需重写 |
| AutoSave | 文件系统、本地时间 | `fileIo`；时间用系统时区 |
| PptModels | 无 | 纯 ArkTS |
| PptInkSession | 托管计时/回调，无平台 API | 纯逻辑；计时用 `setInterval`/`setTimeout` 或 `@ohos.reminderAgent` 不需要 |
| PptInteropService | `ole32/oleaut32` P/Invoke、COM RCW、`dynamic`、`Process`、STA | **无对应**；必须替换或放弃 |
| PptInkFileStore | `System.IO`、`System.Text.Json` | `fileIo` + JSON 手写映射 |
| CountdownTimer | `System.Threading.Timer` | 无消费方；如需可用 `setInterval` |
| LotteryService | `System.Random` | ArkTS `Math.random()` 语义不同；黄金向量应只钉分布无关的索引算法，或允许注入 RNG |
| ScreenshotService | user32/gdi32（GetDC/CreateCompatibleDC/CreateCompatibleBitmap/SelectObject/BitBlt/GetDIBits/DeleteObject/DeleteDC/ReleaseDC）、Win2D `CanvasDevice/CanvasBitmap`、`DirectXPixelFormat.B8G8R8A8UIntNormalized` | `@ohos.screenCapture` / `display` + `image.PixelMap` + PNG 编码；权限与用户授权不同 |
| UpdateService | `HttpClient`、`Assembly`、`Process.Start(UseShellExecute)` | `@ohos.net.http`、`bundleManager`、无 `.exe`；需应用市场/自更新方案 |

---

## 9. 钉住行为的测试（诚实清单）

**没有 Features 层测试。** `grep -rn "InkCanvas.Features|BoardService|PptInk|Lottery|InkFileStore|SettingsStore|AutoSave" tests` 没有任何命中。现有 124 项测试全部在 `tests/InkCanvas.Core.Tests`。

与本层间接相关、可作为移植回归基线的测试：

| 测试文件 | 覆盖内容 | 对本层的意义 |
|---|---|---|
| `DomainTests.cs` `SettingsTests.RoundTripsThroughJson` | `AppSettings` JSON 往返（InkWidth/Theme/路径） | 证明 Settings 字段名/类型契约；但未覆盖全部字段 |
| `DomainTests.cs` `SettingsTests.ReadsLegacyWpfSettingsJson` | 旧 WPF 小写键/字段形状可读 | 兼容性证据 |
| `UpdateTests.cs`（AppVersion/ReleaseFeed） | 版本解析/排序、GitHub release 解析、draft/prerelease 跳过、setup 优先 | UpdateService 的上游纯逻辑 |
| `UpdatePolicyTests.cs` | 静默时间窗（同日/跨午夜/边界/非法时间） | 更新安装授权策略 |
| 其他 Core 测试（Taper/Geometry/Selection/Replay 等） | 墨迹/几何 | 画板笔画往返的间接依赖 |

**结论：画板 JSON、PPT 状态机、`.icink`、自动保存、抽奖、截图、更新下载都没有自动化测试。** 移植时应优先补黄金向量（见 11）。

---

## 10. 逐功能移植分类

| id | 功能 | 分类 | 依据/移植建议 |
|---|---|---|---|
| `board-page-model` | 分页、导航、页级图片/视频增删、撤销动作 | **pure-logic** | 去掉 `InkController` 具体依赖，抽象为 `IPageInk { document, history, selection, viewTransform }`；99 页上限、末页加页、删唯一页语义用黄金向量钉死 |
| `board-element-transform` | `BoardElementMath` 六函数 | **pure-logic** | 建议优先做黄金向量（见 11.1）；矩阵行向量约定必须一致，否则旋转/翻转全部镜像 |
| `board-file-format` | 画板包/页包 schema 与读写规则 | **pure-logic** | 字段名 PascalCase；`Format` 嗅探大小写敏感；版本 1 严格校验；`HighlighterAlpha` 缺失需决策 |
| `icink-format` | 单表面 `.icink` schema | **pure-logic** | 字段名/`Version`/点行格式；读取不校验版本；损坏抛错；高亮 alpha 完整 |
| `settings-persistence` | `Settings.json` 全字段 | **pure-logic** | 9 个顶层键、全部子键按 2.3 表；枚举数字；大小写不敏感读取；容错回落默认；重置值单独一张表 |
| `autosave-snapshots` | 快照命名、日期目录、清理 | **adapt** | 文件系统/时间/路径；`HHmmss-reason.icink`、`max(1,days)`、递归删除 |
| `ppt-models` | 记录/枚举/DTO | **pure-logic** | 1:1；`ShowState` 的 1–5 语义要保留 |
| `ppt-ink-session` | 轮询/转换/缓存/通知/截图路径 | **pure-logic** | 用 `IPptHost` 隔离；5s 抑制、严格大于阈值、SlideId 缓存、LastSlideIndex/ShowPosition 双轨必须保留 |
| `ppt-com-interop` | PowerPoint/WPS COM | **platform-bound**（建议 **drop-candidate**） | Windows-only；HarmonyOS 无 Office COM；若不做 PPT 集成，整块删除并保留模型/存档 |
| `ppt-ink-file-store` | manifest + 每页 .icink + Position | **adapt** | schema 可 1:1；目录键 Name_SlideCount 碰撞、旧格式检测、孤儿文件清理策略需决策 |
| `countdown-timer` | Features 倒计时类 | **drop-candidate** | 无消费方；现行 UI 计时器是另一套 20/30/50/100ms 逻辑 |
| `lottery-service` | 抽奖池/Pick/名单 | **pure-logic** | `Pick` 的 guard=pool*8 与 UI 的两种抽法都要钉；RNG 需可注入以便黄金向量 |
| `screenshot-service` | GDI 抓屏 + Win2D PNG | **platform-bound** | HarmonyOS 用系统截屏/图像编码；权限与合成层（overlay）差异大 |
| `update-service` | GitHub 检查/下载/启动安装器 | **adapt**（或 **drop-candidate**） | HTTP/解析可移植；`.exe` 安装器不可移植；需应用市场或 HAP 自更新方案 |

---

## 11. 建议的黄金向量（下游直接使用）

CLAUDE.md 约定的 `parity/vectors/*.json` 目录当前**尚不存在**（工作树无 `parity/`）；以下是本层应补的最小集合。

### 11.1 `board-element-math.json`

对固定输入钉输出（float 允许 1e-4）：

- `Read(null) = [1,0,0,1,0,0]`；`Read([..5 个..]) = identity`；`Read(6 个)` 原样。
- `Append(null, Translate(10,20)) = [1,0,0,1,10,20]`。
- `Append(Translate(10,20), Scale(2,2))`：按行向量应为 `[2,0,0,2,20,40]`（验证顺序：先平移后缩放）。
- `RenderMatrix(left=100, top=50, t=Translate(10,20))` → 线性单位、offset = `(110,70)-(100,50) = (10,20)`。
- `RenderMatrix(100,50, Scale(2,2))` → offset = `(200,100)-(100,50) = (100,50)`。
- `TransformedBounds(0,0,10,20, Rotate90)` → AABB `(-20,0,0,10)`（按行向量旋转方向验证）。
- `Same(null,null)=true`；`Same([1,0,0,1,0,0],[1,0,0,1,0,1e-5])=true`；`1e-3` → false。

### 11.2 `cink-format.json`

- 典型三笔：普通黑笔、荧光笔（alpha 80）、形状（Shape=true, Taper=0）。
- 边界：0 点笔（Points 空）、单点笔、5 元素行与 4 元素行（缺 time）、非法 Taper（99）。
- 断言：颜色打包/解包、宽度逐点保留、`Complete`、空/缺失文件、损坏 JSON 抛错。
- 额外：**画板包路径**同一笔画应丢 `HighlighterAlpha`（当前 bug），单表面路径应保留——如果决定修，需新版本号并双读旧格式。

### 11.3 `board-file.json`

- 1 页/99 页/100 页（应抛）。
- 背景四种字符串 + 非法字符串（保持当前背景）。
- `GridSpacing = 5` → 加载后 12；`= 500` → 160；`= 0` → 保持当前。
- 图片/视频：合法/空 Data/零宽高/非 video ContentType（过滤）。
- `SavePage` 三分支：无图无视频（纯 .icink）、有视频无图（视频丢失）、有图有视频（完整包）。
- `LoadPage` 从多页包只取第一页、忽略背景/网格。
- 版本 0/2 → 抛；`Format` 大小写错误 → 按单表面路径处理。

### 11.4 `ppt-session.json`

用一个假 `IPptHost` 脚本驱动快照序列，断言事件序列与存档：

- Detached → Editing → SlideShow(Begin) → Slide 1→2→3 → Editing(End)；
- 同一页重复轮询不产生事件；`ShowState` 变化不产生事件；
- 页数变化时刷新索引；
- 导航前截图 → 5s 抑制 → 外部切页不重复截图；
- 阈值：`count == threshold` 不截图，`count == threshold+1` 截图；
- 通知：上次页有效/无效、隐藏页、计时、旧格式各一次；
- `SamePresentation` 的四种分支；
- 存档：SlideIndex 未知（0）的页被 `PptInkFileStore` 跳过并删除旧文件。

### 11.5 `lottery.json`

固定种子（注入 RNG）下：

- 无名单、PeopleCount=60，连续 Pick 的索引序列；
- NoRepeat 池耗尽返回 null；
- LoadNames 去空白/清空 used；
- UI 两种抽法（flicker 与 final）的拆分数量公式（1/2/3 列）。

### 11.6 `settings.json`

- 全字段默认对象序列化快照（键名/类型/枚举数字）；
- 旧 WPF 小写片段可读；
- 缺字段时构造默认；
- 损坏 JSON → 全新默认；
- `ResetToRecommendation` 全字段期望（尤其 InkAlpha 80、EraserSize 1、HyperlinkOption Yes、静默窗口 18:20–07:40、AutoDel 保留）。

---

## 12. 移植风险与下游容易踩的坑

1. **行/列向量约定**：WPF/System.Numerics 是行向量 `p*M`。ArkTS 的矩阵库（如 `matrix4`）常是列向量或 4×4；直接套会把旋转方向/平移位置弄反。建议自写 `Matrix3x2` 并只用本报告的六元组公式。
2. **`Append` 的顺序**：`existing * m` 表示“新变换在已有之后”。旋转后再拖动、拖动后再旋转，结果不同；不要写成 `m * existing`。
3. **`RenderMatrix` 的 `M*t - t`**：漏掉这个偏移会让旋转/缩放绕画布原点而不是元素左上角，表现是“旋转时元素飞出屏幕”。
4. **画板高亮 alpha 丢失**：`.icink` → 画板 → `.icink` 往返会把 80 变成 96；若做跨平台互认测试会失败。
5. **`SavePage` 的视频丢失**：无图片页保存后视频消失；不要把它当“保存成功”静默通过。
6. **`Format` 嗅探大小写**：Windows 文件读取对键名大小写敏感（`GetProperty`），而反序列化不敏感；这会造成“看起来能读、实际走错分支”。
7. **PPT 的 5 秒抑制**：只抑制 `ExternalSlideChange`；显式截图和导航前截图不受影响。`DateTime.UtcNow > 抑制时间` 才清零；边界相等时仍抑制。
8. **`ShouldSaveScreenshot` 严格大于**：等于阈值不截；调用方另有一处 `<=` 提前返回，二者容易混淆。
9. **`BuildArchive` 的索引 0**：没见过的页会以 0 进存档，随后被 store 丢弃；如果先 Save 后 Load，可能静默丢页。
10. **PPT 目录只用 Name+SlideCount**：同名同页数文档共享墨迹；HarmonyOS 若要加 FullName 哈希，会破坏与 Windows 侧目录的互认。
11. **`PptInkFileStore` 不清理孤儿文件**：旧 manifest 不可读时不删除；长期使用会积累 `slide-*.icink`。
12. **自动保存 `reason` 直接进文件名**：移植时若传入含 `:`/`/` 的原因会抛错并被吞成 null；建议在 ArkTS 侧做白名单。
13. **`SettingsStore` 吞异常**：设置写入失败用户无感知；HarmonyOS 应至少记录日志。
14. **`UpdateService` 只校验长度**：无哈希/签名；HarmonyOS 若继续下载二进制，必须补完整性校验（SHA-256 + 签名）。
15. **截图坐标**：`Capture` 接收物理像素；在 125%/150% 缩放下若传 DIP 会截错区域。HarmonyOS 的 density 换算必须显式。
16. **`CountdownWindow` 与 `CountdownTimer` 两套逻辑**：不要根据 Features 类写 UI；以 7.4 的窗口逻辑为准。
17. **大视频 base64**：512 MiB 上限是 UI 层；JSON 内嵌后内存可达 ~683 MiB，ArkTS 解析更容易 OOM。建议 HarmonyOS 侧评估改用旁路 blob 文件（但会破坏单文件互认）。
18. **没有窗口位置持久化**：不要假设 `Settings.json` 含窗口坐标；需要新增设计。

---

## 13. 开放问题（需要架构/产品决策）

1. **PPT 功能在 HarmonyOS 的去留**：没有 Office/WPS COM 时，是放弃 PPT 集成、仅保留“手动分页墨迹板”，还是接入某个鸿蒙演示宿主/投屏协议？这决定 `ppt-com-interop` 是 platform-bound 还是整体删除。
2. **`.icink` 画板笔画是否补 `HighlighterAlpha`**：补则需 `BoardFormatVersion=2` 并双读 v1；不补则跨平台往返会丢高亮透明度。需要明确“互认”是字节级还是可视化级。
3. **`SavePage` 的视频丢失是否修复**：修复会改变现有 Windows 行为；不修则 HarmonyOS 必须复刻 bug。
4. **窗口/Stage 位置是否要持久化**：当前 Windows 侧完全没有；HarmonyOS 是否新增（ArkUI `windowStage` 位置/尺寸）？若新增，字段放 `Settings.json` 还是独立文件？
5. **画板目录键是否加入稳定身份**：`Name_SlideCount` 碰撞是已知问题；HarmonyOS 若引入 FullName 哈希会与 Windows 目录不互认。
6. **PPT 轮询非放映间隔**：会话建议 1000ms，壳实际 900ms；HarmonyOS 以哪个为准？建议把 `RecommendedPollInterval` 改为唯一真相并在壳里直接使用。
7. **截图权限**：HarmonyOS 截屏需要用户授权/系统能力，且 overlay 内容是否进入截图取决于合成层；需要实机确认。
8. **更新渠道**：HarmonyOS 应用市场是否允许自更新？若不，`UpdateService` 整体删除或改为“检查版本 + 跳转市场”。
9. **`Settings.json` 的枚举序列化**：当前是数字（`OptionalOperation`、`MatrixTransformCenterPoint`）。ArkTS 若写成字符串会互不兼容；需要黄金向量钉住。
10. **`AppSettings.ResetToRecommendation` 的语义**：当前是“原地改全部字段 + 保留 AutoDel 两项”。HarmonyOS 是否保持“原地”语义（会影响已持有引用的服务）？
11. **PPT 存档 `Position` 文件的读写时机**：manifest 缺失时才读；写入却总是随 `LastSlideIndex > 0`。是否需要对齐为“始终以 manifest 为准”？
12. **抽奖 RNG**：`System.Random` 与 ArkTS `Math.random()` 序列不同；若要求跨平台同一结果，必须注入种子算法（如 xorshift/PCG），并明确 UI 闪烁也用同一 RNG 还是独立。
13. **大媒体存储**：是否允许 HarmonyOS 版把 `Data` 外置为文件并在 `.icink` 中存相对路径？这会改变格式，需要版本协商。
14. **`parity/` 目录尚不存在**：CLAUDE.md 描述的 `parity/manifest.json`、`parity/vectors/*.json`、`run-vectors.mjs` 在当前工作树均无文件。需要先建立基线，否则“两侧互认”没有可执行校验。

---

## 14. 验证声明

- 本文基于工作树静态阅读；**未运行 `dotnet build`/`dotnet test`，未启动 InkCanvas.App，未做 PowerShell/WinUI 运行时验证**。
- 未在真实 PowerPoint/WPS 上验证 COM 行为；未验证 `.icstk/.icart` 检测、截图、媒体编解码、更新下载/安装。
- 文中所有常量、公式、字段名均来自源码；若与运行时行为不符，以源码为准并另开验证任务。
- 引用行号以工作树为准（`BoardService.cs` 592 行、`Stores.cs` 195 行、`PptModels.cs` 78 行、`PptInkFileStore.cs` 141 行、`PptInkSession.cs` 368 行、`PptInteropService.cs` 562 行、`Tools.cs` 179 行、`UpdateService.cs` 112 行）。
