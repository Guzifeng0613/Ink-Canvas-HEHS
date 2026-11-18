# API 24 / API 26 差异矩阵

> 维护规则：任何 API 24/26 行为差异必须先在这里登记，再在 `harmony/platform/compat` 实现门控。
> 门控只允许：`deviceInfo.sdkApiVersion`、`canIUse('SystemCapability....')`（仅下列已核实字符串）、try/catch 方法存在性探测。
> 禁止臆造 SystemCapability。真机未验证的条目必须标注「需实机」。

## 已核实 SystemCapability

| 字符串 | 来源 | 用途 |
|---|---|---|
| `SystemCapability.ArkUI.ArkUI.Full` | SDK d.ts | ArkUI 组件/触摸/Canvas/DisplaySync/输入监听 |
| `SystemCapability.Stylus.Handwrite` | SDK d.ts | Pen Kit HandwriteComponent / StylusFrameBoost |
| `SystemCapability.Stylus.StylusService` | SDK d.ts | 笔身传感器/捏笔/双击 |
| `SystemCapability.MultimodalInput.Input.Pointer` | SDK d.ts | 鼠标光标样式 |
| `SystemCapability.OfficeService.PDFService.Core` | SDK d.ts（@hms.officeservice.pdfservice） | PDF Kit 课件渲染 |

## 已核实 API（B–G 实测/d.ts 核对）

| 能力 | API | 备注 |
|---|---|---|
| PDF 打开/页数/取页 | `pdfService.PdfDocument.loadDocument/getPageCount/getPage` | ParseResult.PARSE_SUCCESS；重复 load 前须 releaseDocument |
| PDF 页渲染 | `PdfPage.getPagePixelMap()/getWidth()/getHeight()` | 另有 getCustomPagePixelMap/getAreaPixelMap（矩阵/区域） |
| 组件快照 | `componentSnapshot.get(id): Promise<PixelMap>` | 仅本应用组件；无跨应用截图 |
| 文件选择/保存 | `picker.DocumentViewPicker.select/save`、`picker.PhotoViewPicker.select` | MIME 枚举为 `PhotoViewMIMETypes`；URI 需导入沙箱 |
| 键盘修饰键 | `KeyEvent.getModifierKeyState?(keys)` | 可选方法；`metaKey` 仅 Meta 键；需焦点 |
| 沉浸式 | `window.setWindowLayoutFullScreen(true)`、`setWindowSystemBarEnable([])` | 系统手势仍由系统管理 |
| Base64/图像 | `util.Base64Helper.encodeSync/decodeSync`、`image.createImageSource/createPixelMapSync`、`ImagePacker.packing` | 画板图片/视频 base64 与格式互认 |
| 视频 | `Video({ src })` | 沙箱路径；进度保持待补 |
| 启动模式 | `Want.parameters['mode']` + AppStorage | 替代 CLI 启动参数 |

## 差异表（Phase A 种子）

| 能力 | API 24（6.1.1） | API 26（7.0） | 门控 / 备注 |
|---|---|---|---|
| ArkUI `onTouch` 压感/倾斜 | 可用（pressure [0,1]、tiltX/Y，API 9+） | 相同 | 无需门控；0/缺省 → 压感 0.5 |
| `TouchObject.width/height`（接触面积） | 可用（API 15+，可能为 0） | 相同 | 0 时关闭面积手掌判定，降级笔优先 + Cancel |
| `getHistoricalPoints()` | 可用（API 10+，仅 onTouch 内） | 相同 | ns 时间戳；/1e6 转 ms |
| `SourceTool` 细分笔尖 | 仅 Finger/Pen/Mouse/Touchpad/Joystick | 相同 | RUBBER/BRUSH 仅 XComponent 原生 toolType；首版不依赖 |
| `onHover` / `onHoverMove` | API 8+ / API 15+ | 相同 | 需实机验证第三方笔 |
| `TouchType.Cancel`（笔落取消手指） | 有（行为依设备） | 相同 | 手掌判定辅助信号；需实机 |
| `StylusFrameBoost.forceRefreshOneFrame` | 无 | 有（26.0.0；权限 `ohos.permission.STYLUS_FRAME_BOOST`） | canIUse(Stylus.Handwrite) && sdkApiVersion>=26 |
| `HMS_HandWrite_SetRefreshDelayOff`（NDK） | 无 | 有（26.0.0） | 仅 XComponent 路径；Phase 2 |
| Pen Kit `HandwriteComponent` | 可用（API 12+） | 可用（26 增 hiddenTools） | 黑盒画布，不用于 .icink 模型；只作实验 |
| `systemMaterial`（通用属性） | 无 | 有（26.0.0） | 能力层可选；API 24 用 blur/HDS 降级 |
| HDS 材质（systemMaterialEffect/HdsMaterial） | 可用（API 23+） | 可用 | 无需门控 |
| `ContainerReader` / `WithEnv` | 无 | 有（26） | API 24 用 onSizeChange + LocalStorage |
| `OverlayManager` 层级浮层 | 未文档化 | 有（26 Beta2） | 首版不用；浮条走主窗 Stack |
| 闪控窗/闪控球/子窗（三方） | 无记录 | 无文档化资格 | 不作为依赖 |
| PDF Kit 视图↔页面坐标/二进制读取/自定义渲染 | 无 | 有（26） | try/catch + 方法存在性；失败降级 PNG 序列 |
| PDF Kit 内置手写 `enablePen` | 无 | **不可用**（仅 26.0.1 Beta1） | 禁止进入生产代码 |
| AVPlayer / Audio / 通知 / 相册 / Picker | 可用（基础 API 14+） | 相同 | 直接调用（无 syscap 字符串，用 try/catch） |
| 代理提醒 / 后台长时任务 | 可用（权限 APL 待核实） | 相同 | 需实机核实 PUBLISH_AGENT_REMINDER APL |
| Live View 倒计时 | 无计时模板 | 无计时模板 | 改用常驻通知 + 代理提醒 |
| 应用市场更新（AppGallery Kit） | 能力存在，API 未知 | API 未知 | 首版 Deferred，不伪装可用 |
| 跨应用悬浮窗 `TYPE_FLOAT` | 需 `SYSTEM_FLOAT_WINDOW`（system_basic） | 相同 | 三方不可用 → 应用内方案 |
| 全局输入监听/注入 | 无 | `inputEventClient` 需 `CONTROL_DEVICE`（system_basic） | 三方不可用 |
| 全局热键 | 无 | 无 | 应用内快捷键 |
| 深色模式/减少动效读取 | 应用内主题开关已接；系统跟随待接 | 同 | 能力探测 |
| 系统材质/blur | 未接（半透色卡片替代） | systemMaterial 可用 | 能力层待补 |
| 断点（窗口宽度 vp） | onSizeChange 可用（API 12+） | ContainerReader 可选 | 首版统一 onSizeChange |
