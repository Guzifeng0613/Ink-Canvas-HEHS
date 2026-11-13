## 3.3.0 —— WPF 菜单、白板与 PPT 适配补齐（行为未验证）

### 新增与修正
- 鼠标模式使用独立鼠标指针矢量图标，不再用锁图标；从颜色/形状菜单选笔、荧光笔或图形会回到标注模式。
- 画笔、荧光笔、图形选择改为独立窗口，避免 WinUI Flyout 被短浮动工具栏裁切；调色盘恢复上游色序、主题色、笔宽和透明度范围。
- 将上游 Segoe Fluent Icons 字体随应用打包，避免其他客户端缺少字体导致图标缺失。
- 浮动工具栏拖动使用 Windows 原生 caption 拖动循环；尺寸、位置和菜单弹出位置按窗口 DPI 在 DIP/物理像素间换算。
- DWM 统一设置窗口圆角与非客户区绘制策略，不再扩展玻璃帧覆盖 XAML 内容，避免白色圆角光晕。
- 自交笔画轮廓用同向 capsule 段组成 Winding union；同一笔交叉处不再按 even-odd 规则挖空，半透明笔迹自身重叠只合成一次。
- 白板重建 WPF 底部三栏工具、分页/删除确认、选择与变换、颜色/粗细/图形/橡皮、页级撤销、图像对象、`.icink` 页/整板保存加载；窗口化标题栏、全屏和 taskbar work area 适配。
- PPT/WPS 接入 `PptInkSession`：COM 状态轮询、稳定 SlideID 分页墨迹缓存、前进/后退/退出/跳页、F5/Shift+F5、截图与墨迹存档、上次页/隐藏页/计时提示确认；恢复原设置控制的底部/侧边导航箭头与页码面板，页码按钮打开 PowerPoint 导航窗格并切到鼠标模式。
- `Canvas.InkAlpha` 进入 `StrokeAttributes.HighlighterAlpha`，并被克隆、`.icink` 保存/加载和 `AppServices.ApplySettings` 应用。

### 验证状态与差异
- 按用户要求，**未运行单元测试、未启动应用、未在真实触摸屏/压感笔/PowerPoint/WPS 上做行为验证**；只做 Release solution 编译检查（0 warning / 0 error）。
- 例外：自交笔画改动专门运行了 3 项纯几何回归测试（3/3 通过）；Win2D GPU 实际栅格化仍未验证。
- 旧 WPF `.isf/.icstk/.icart` 不转换到 `.icink`；旧 PPT ISF 文件只检测，不导入、不删除。
- 白板与 DWM 阴影/圆角为 WinUI 重建，不声称像素级一致；PPT/WPS COM 需在目标 Office/WPS 版本实机复核。
- 对照清单：`docs/PORTING-NOTES.md`、`docs/menu-porting-notes.md`、`docs/board-porting-notes.md`、`docs/ppt-porting-notes.md`。

## 3.1.0

### 新增
- 选区变换：拖动内部移动、拖四角缩放、拖上方圆点旋转，整段拖动记为一次撤销。
- 选中笔画后工具栏出现删除按钮。
- 双指手势可切换为只作用于选区。
- 墨迹回放窗口：按绘制顺序重放，支持播放/暂停、0.5–4 倍速与进度拖动。
- 自动更新：检查 GitHub 发行版、下载并校验安装包长度后再请求确认安装；
  静默安装受「静默时间窗」策略约束（支持跨零点窗口）。
- 操作指南与更新日志窗口。
- 边缘手势抑制（全屏时暂时停用 Windows 边缘滑动）。
- 按窗口标题查找顶层窗口的能力，替代原 UI Automation 实现。

### 优化
- 透明覆盖层不再每帧整屏回读 GPU：已提交层缓存，逐帧只回读活动笔画的包围盒并做预乘 alpha 合成。
  1080p 下每帧回读量从约 8 MB 降到几十 KB。

### 修复
- 笔锋闪烁：笔锋改为逐点增量因果计算并落库，抬笔不再重算几何；
  手掌判定不再调节墨迹层透明度。

## 3.0.0

- 从 WPF / .NET Framework 4.7.2 迁移到 WinUI 3（Windows App SDK），
  目标 net10.0-windows10.0.26100.0、x64、非打包部署。
- 墨迹引擎以 Win2D 重写；图形识别改为托管几何拟合，解除 x86 限制。
- PPT / WPS 改用 COM 后期绑定，不再依赖 Office PIA。
- 界面按 Fluent 重做，完整适配触摸与动画。