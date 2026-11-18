# device-verify.ps1 — 真机冒烟 + 人工验证清单
# 用法：pwsh -File tools/device-verify.ps1 [-HapPath <hap>]
param(
    [string]$HapPath = 'D:\InkCanvas\harmony\entry\build\default\outputs\default\entry-default-unsigned.hap',
    [string]$OutDir = 'D:\InkCanvas\artifacts\device'
)
$ErrorActionPreference = 'Continue'
$hdc = 'E:\EcoStudioCLI\command-line-tools\sdk\default\openharmony\toolchains\hdc.exe'

Write-Host '=== 自动冒烟 ==='
& pwsh -NoProfile -File 'D:\InkCanvas\tools\device-install.ps1' -HapPath $HapPath -OutDir $OutDir
$code = $LASTEXITCODE

Write-Host ''
Write-Host '=== 崩溃/异常检查 ==='
$log = Get-ChildItem "$OutDir\hilog-*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($log) {
    $bad = Select-String -Path $log.FullName -Pattern 'JsError|AppFreeze|CppCrash|FATAL|com.inkcanvas.harmony.*crash'
    if ($bad) { Write-Host '发现异常:'; $bad | Select-Object -Last 20 | ForEach-Object { Write-Host $_.Line } }
    else { Write-Host '未发现崩溃/冻屏关键字。' }
}

Write-Host ''
Write-Host '=== 人工验证清单（逐项在设备上操作并记录） ==='
@(
    '1 笔书写：压感/倾斜/笔锋（Tail/Velocity/None）是否正常，抬笔无闪烁',
    '2 手指/手掌：手掌误触抑制、接触面积分档（特殊屏设置）',
    '3 荧光笔：半透明、自交不加深、透明度 20–240',
    '4 橡皮：面积擦/整笔擦、碎片、撤销重做',
    '5 图形：38 种图形预览与绘制、两步长方体/双曲线、粘滞',
    '6 选区：点选/拖拽/旋转/缩放/克隆/删除、延迟写点抬指落位',
    '7 双指：平移/缩放/旋转、枢轴选项、累计钳制 0.2–8',
    '8 浮条：19 按钮/折叠/拖动/文字开关、点 UI 不落墨',
    '9 调色盘/图形菜单：色序/笔宽/透明度/笔锋、选色即关',
    '10 画板：分页/背景/网格/插图/视频/保存打开、坏文件不清空',
    '11 课件：PDF 导入/逐页渲染/分页墨迹/翻页/退出保存',
    '12 计时/抽奖/回放：开始暂停重置/不重复/倍速进度',
    '13 设置：持久化往返、主题、手势开关、自动保存',
    '14 截图/快捷键/沉浸式/是否窗/指南',
    '15 多形态：平板/2in1 浮窗一致、手机初步布局、旋转/分屏重绘'
) | ForEach-Object { Write-Host $_ }

Write-Host ''
Write-Host "结果与截图保存在: $OutDir"
if ($code -ne 0) { Write-Host "冒烟脚本退出码: $code" }
