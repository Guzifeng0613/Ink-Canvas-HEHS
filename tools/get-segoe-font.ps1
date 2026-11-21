# Segoe 图标字体获取（本机构建输入，不入库——见 .gitignore 与 docs/harmony-porting-notes.md）
# SegoeFluentIcons.ttf / segmdl2.ttf 为微软授权字体，随 Windows 分发；
# 仓库不随源码分发字体（PR #4 既定决策）。本脚本从本机 Windows 字体目录复制构建输入。
# 注意：嵌入了该字体的 HAP 不可公开分发（授权边界，与 Beta 工具链产物同口径）。
param(
    [string]$OutDir = "$PSScriptRoot\..\harmony\entry\src\main\resources\rawfile\fonts"
)

$ErrorActionPreference = 'Stop'
$fonts = @("$env:WINDIR\Fonts\segfluenticons.ttf", "$env:WINDIR\Fonts\segmdl2.ttf")
$found = $fonts | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $found) {
    Write-Error "本机未找到 Segoe 图标字体（segfluenticons.ttf / segmdl2.ttf）。`n图标将回退 SymbolGlyph/文字兜底，但 HAP 构建会因缺 rawfile 资源失败。`n请从装有 Windows 授权的机器复制其一至 $OutDir\SegoeFluentIcons.ttf。"
    exit 1
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Copy-Item $found "$OutDir\SegoeFluentIcons.ttf" -Force
Write-Host "已复制 $(Split-Path $found -Leaf) -> $OutDir\SegoeFluentIcons.ttf（MDL2 与 Fluent 共享 E7xx 经典码点；个别扩展区码点可能缺字形，运行时按码点回退兜底）。"