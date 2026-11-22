# AI 助教插件

WinUI 3 版的 AI 窗口、流式请求、两阶段识别、分步讲解和局部纠错由独立原生插件 `inkcanvas.ai-assistant` 提供。插件通过现有 `IApplicationModule` 生命周期运行，通过 `IAiAssistantHost` 访问画布与课堂能力，主程序通过 `IAiAssistantService` 调用插件。

## 安装与管理

将 `inkcanvas.ai-assistant-1.2.0.zip` 解压到程序的 `Modules` 目录，得到以下文件。

```text
Modules/
  inkcanvas.ai-assistant/
    module.json
    InkCanvas.Modules.AiAssistant.dll
    InkCanvas.Modules.AiAssistant.deps.json
```

在“设置 → 模块与插件 → 原生功能插件”查看状态或关闭 AI 助教，开关在重启后生效。卸载时退出程序并移除该目录。未安装、已停用或加载失败时，工具栏、课堂工具箱、聚光灯与放大镜会隐藏 AI 入口。

## 使用

选择画布上的题目后打开 AI 助教。窗口默认宽 380 DIP，靠屏幕右侧显示单列对话。顶部选区条支持追加和重新获取，点击缩略图可打开预览、重排选区或局部纠错。每条回答分别提供分步展开、复制、收藏和贴回白板，切换题目或继续追问后仍可操作先前回答。复制和贴回遵循该条回答已展开的步骤。

窗口使用共享 `WindowFrame`、WinUI 3 标题栏和桌面亚克力，随应用深浅色主题切换。顶部可切换置顶，点击右侧收起按钮可变为边缘悬浮条，长按或右键可选择停靠左侧或右侧。输入框支持 Enter 发送与 Shift+Enter 换行，并保留中文输入法选词操作。关闭窗口会取消请求并释放会话与缩略图。

流式显示合并为每 33 毫秒至多更新一次，并复用当前回答中已完成的内容块。教师向上回看时保持阅读位置，停留底部时跟随新内容。公式和代码块支持独立横向滚动。多选区大图在预览打开时解码，收起面板后延后正文渲染。

## AI 服务

保留现有的 Ink Canvas SSE 服务协议与环境变量配置。未配置端点时显示本地演示，并在窗口中提示。

| 环境变量 | 默认值 | 用途 |
| --- | --- | --- |
| `AI_ENDPOINT` | 空 | 接收 Ink Canvas 识别请求并返回 SSE 的服务地址 |
| `AI_MODEL` | `inkcanvas-assistant` | 模型名称 |
| `AI_TIMEOUT_MS` | `15000` | 纯墨迹阶段首内容超时，毫秒 |
| `AI_IMAGE_TIMEOUT_MS` | `20000` | 截图阶段首内容超时，毫秒 |

追问通过请求的 `question` 字段发送，`messages` 携带最近 24 条可见对话消息，按先后顺序包含 `role` 与 `content`。请求继续保留题目图片、笔画、讲题方式、局部纠错区域与课件上下文字段。

## Agent 接入

AI 插件导出 `IAssistantAgentBridge`。后续原生 Agent 模块在启动时调用 `Register(provider)`，并在停止时释放返回的注册对象。接口位于 `InkCanvas.Host.Contracts/AiAgent.cs`。注册后，面板顶部的 Agent 入口显示服务名称并启用模式切换。尚未注册时显示禁用原因。

`IAssistantAgentProvider.RunAsync` 接收本轮指令、可见对话、画布工具、进度回调与取消令牌。`IAssistantAgentCanvas` 提供墨迹和图片/媒体对象描述的快照、可撤销的图形创建与选区变换，以及用于识别的画布截图。工具调用自动调度到画布线程，写入前检查目标仍为当前画布且允许编辑。切换页面后旧目标会拒绝操作。停止生成、关闭窗口或注销 Agent 会取消当前任务。该版本预留接入接口，实际规划与识别由注册的 Agent 服务提供。

## 构建与分包

```powershell
dotnet publish src/InkCanvas.App/InkCanvas.App.csproj -c Release -p:Platform=x64 -o artifacts/app-with-ai
dotnet publish src/InkCanvas.App/InkCanvas.App.csproj -c Release -p:Platform=x64 -p:IncludeAiAssistantModule=false -o artifacts/app-without-ai
.\tools\pack-extension.ps1 -Project src\InkCanvas.Modules.AiAssistant\InkCanvas.Modules.AiAssistant.csproj -Kind Module
```

基础包使用全新输出目录。`IncludeAiAssistantModule=false` 排除 AI 插件的构建与复制，现有 AI 卡片仍可通过通用图片对象与兼容元数据打开、选择和编辑。

## 存档命名与描述

在“设置 → 保存与备份 → AI 存档命名与描述”中填写支持多图输入的模型接口完整地址和模型名称，保存配置后打开“保存后自动分析并命名”。默认关闭。文稿库中也可对选中的文件点击“AI 命名与描述”执行一次处理。

OpenAI 兼容模式使用 Chat Completions 多图格式，地址应包含 `/v1/chat/completions`。密钥保存在 Windows 凭据管理器中，可使用 `AI_API_KEY` 或 `OPENAI_API_KEY` 环境变量。选择现有 Ink Canvas 助教协议时，服务收到 `purpose=archive-caption`、多张 `images` 和 JSON 输出要求。接口留空时沿用 `AI_ENDPOINT`、`AI_MODEL` 和助教协议。存档命名要求真实配置的模型服务。相关多图协议见 [官方文档](https://developers.openai.com/api/docs/guides/images-vision?api-mode=chat)。

保存完成后，插件在后台从磁盘快照渲染，逐页读取图片和视频封面，跳过视频主体数据。单张 PNG 最长边限制为 3840 像素，总像素预算约 829 万。范围在预算内的屏幕截图与墨迹保持原像素尺寸。超过范围的批注生成等比缩小的总览，并附带最多两张墨迹密集位置的高清局部，缩小总览适当增强细线可见性。

白板存档的所有页面按顺序导出。长页或宽页按页面可视尺寸分段，相邻分段重叠 64 像素，跳过不含内容的空段，保留负坐标墨迹。每次最多发送 4 张、约 32 MB 的 PNG，超过后分批分析并传递前批摘要，最终生成整个文件的标题和描述。单个长页最多 1024 个分段，超出时提示拆分文稿。

`.icink` 的标题与描述写入 `archive.json`，实际文件按生成的标题重命名，重名时添加序号。原始笔画、笔锋、媒体和纸型仍可编辑。写回前检查保存时的大小和修改时间，文件在分析期间发生变化时跳过旧结果。旧 JSON 文稿成功命名时迁移为当前 ZIP 容器。

PDF 和解析几何插件通过适配接口提供各自原生渲染的 PNG，保存后的 AI 标题建议与描述写入相邻的 `.ai.json` 文件。它们的编辑窗口继续使用已打开的文件路径。文稿库会同步白板新文件名、最近使用与收藏，并在预览区显示描述。资源管理器继续使用存档内嵌封面。

自动处理采用串行队列，正常存档保存先完成。退出前尚未处理的原生白板路径会记录到插件私有队列，下次启动且功能开启时恢复。模型失败时原文件保留，可手动重试。关闭开关会取消当前自动任务并清空自动队列。

PNG 与分析结果存放在应用设置目录的 `PluginData/inkcanvas.ai-assistant/Documents/Images`。历史缓存上限约 256 MB，当前任务已完成分析的 PNG 超过约 128 MB 时优先回收较早图片，分析清单仍记录其页码与范围类型。

## 插件适配接口

`IAiDocumentService` 由 AI 插件实现和导出，提供存档分析与设置视图。`IAiDocumentBridge` 由宿主提供给 PDF、解析几何和文稿库，动态转发到已加载的 AI 插件。

`AiDocumentRequest` 包含源文件路径、来源插件 ID 和可选的 `ExportImages` 委托。自定义来源按需实现 `IAsyncEnumerable<AiDocumentImage>`，将 PNG 写到分配的目录，并提供原始页码、图像角色和像素尺寸。原生白板直接提供 `.icink` 路径，由 AI 插件负责渲染。调用 `Saved` 可参与保存后自动分析，调用 `DescribeAsync` 可主动执行一次。

图像角色可以使用 `page`、`segment`、`overview`、`detail` 或来源插件自己的标记。结果包含原路径、新路径、标题、描述和图片数。适配层通过 Host.Contracts 共享契约，调用方无须引用 AI 插件 DLL。此版本接口需要随附的新宿主构建。

## 验证

`DocumentNamingTests` 覆盖原像素导出规划、大幅批注局部图、长页覆盖、文件名清理及多图 HTTP 请求。`ArchiveDescriptionTests` 覆盖无损写回、同名避让、过期结果拒绝、旧版迁移和跳过视频主体。`--documentaicheck=<目录>` 使用本地模拟视觉模型执行本机 WinUI、Win2D 与保存流程验收，结果写入 `document-ai.json`。
