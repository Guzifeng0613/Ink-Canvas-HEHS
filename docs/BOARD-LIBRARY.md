# 文稿库与资源管理器预览

文稿库是独立的原生 WinUI 3 功能插件 `inkcanvas.library`。安装到程序的 `Modules` 目录后，工具菜单出现“文稿库”，白板打开、保存和另存为也会使用文稿库。可以在设置的原生功能插件页面禁用，重启后生效。

文稿库需要本次构建提供的新宿主接口，随附的可运行版本已包含该插件。其他原生功能插件保持独立安装和开关。

界面使用项目共享的标题栏与 Mica 背景，支持系统深浅色、触摸滚动、文件夹导航、递归名称搜索、按名称/修改时间/大小排序、最近使用、收藏、新建文件夹、重命名、移动和制作副本。保存时选择目标文件夹并填写名称，同名文稿需要确认替换。可以通过“浏览文件”和“其他位置”使用系统文件选择器。

打开文稿前可以查看封面，并使用预览区左右按钮查看其他页面。预览按整个页面对象范围等比缩放，包含墨迹、图片和视频封面。新保存的 `.icink` 内嵌 `preview/cover.png` 和小型 `preview/info.json`，保留全部可编辑数据和原有 ZIP 格式的兼容性。

旧文稿会按需渲染预览。较大的旧版 JSON、超过 32 MB 的页面清单或超过 64 MB 的页面预览媒体，提示打开文稿查看。预览跳过视频主体数据。磁盘预览缓存以文件路径、大小、修改时间和页码为键，上限 128 MB。仅为可见卡片加载图片，滚出视图后释放，文件改变时自动使用新缓存键。

文稿库的最近记录、收藏和缓存位于应用设置目录的 `PluginData/inkcanvas.library`。文稿仍保存在选定的普通文件夹中。原有自动保存与退出保存设置继续生效。

安装新版 AI 助教插件后，详情区出现“AI 命名与描述”。它可以针对现有存档执行一次多图分析，完成后同步新文件名与描述。保存后的自动命名可在“保存与备份”手动开启，配置方法见 [AI 助教插件](AI-ASSISTANT.md)。

## 安装资源管理器插件

资源管理器插件为独立的 x64 原生 Shell 扩展包 `inkcanvas.shell-preview-1.0.0-win-x64.zip`。解压后，在 64 位 PowerShell 中执行 `./Install.ps1`。安装只写当前用户的注册表，并复制 DLL 到 `%LOCALAPPDATA%/InkCanvas/ShellExtension`。资源管理器打开预览窗格后可显示白板首页。缩略图跟随资源管理器图标尺寸调整。

卸载可运行安装目录中的 `Uninstall.ps1`。脚本会恢复原有的缩略图和预览关联。DLL 仍被 Windows 使用时，注销后可以删除残留 DLL。

处理器实现 `IInitializeWithStream`、`IThumbnailProvider`、`IPreviewHandler` 等原生接口，使用 Windows 的隔离宿主。按 ZIP 目录直接定位未压缩的 PNG 封面，解码图像最大边为 1024 像素。单封面限制 2 MB，PNG 尺寸和 ZIP 目录大小均有限制。无须启动 Ink Canvas、加载 .NET、WinUI、全部笔画或媒体数据。

资源管理器当前预览首页。旧文稿、缺少封面或 ZIP64 文稿会使用文档图标或提示重新保存。使用本版 Ink Canvas 重新保存后可生成封面。封面损坏或显卡设备不可用时，保存引擎仍优先保存完整的可编辑文稿。

## 构建

文稿库使用 `tools/pack-extension.ps1 -Project src/InkCanvas.Modules.Library/InkCanvas.Modules.Library.csproj -Kind Module` 打包。

Shell 插件使用 `tools/build-shell-extension.ps1` 构建与打包，需要 Visual Studio C++ v143 和 Windows SDK。生成的 DLL 使用静态 C++ 运行库，安装脚本无需管理员权限。原生验证程序工程为 `src/InkCanvas.ShellExtension/ShellExtensionCheck.vcxproj`，参数依次为 DLL 和带封面的 `.icink` 样本路径。

相关 Windows 接口说明见 [缩略图接口](https://learn.microsoft.com/en-us/windows/win32/api/thumbcache/nn-thumbcache-ithumbnailprovider) 和 [预览处理器接口](https://learn.microsoft.com/en-us/windows/win32/shell/building-preview-handlers)。
