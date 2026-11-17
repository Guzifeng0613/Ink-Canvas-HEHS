# setup-harmony-toolchain.ps1 — 安装/验证 HarmonyOS 命令行工具（含内嵌 SDK）
# 用法：
#   pwsh -File tools/setup-harmony-toolchain.ps1                 # 自动在常见目录找 zip
#   pwsh -File tools/setup-harmony-toolchain.ps1 -ZipPath <zip>  # 指定 zip
#   pwsh -File tools/setup-harmony-toolchain.ps1 -SkipExtract    # 已解压，仅验证
# 约定安装位置：D:\dev-cache\harmony\command-line-tools（保护 C 盘，沿用 D12 缓存约定）
# 不修改系统 PATH；构建脚本一律用绝对路径调用工具。
param(
    [string]$ZipPath,
    [string]$InstallRoot = 'D:\dev-cache\harmony',
    [switch]$SkipExtract
)
$ErrorActionPreference = 'Stop'

function Find-Zip {
    $candidates = @(
        'D:\dev-cache\harmony', 'D:\dev-cache', 'D:\', 'D:\tmp',
        "$env:USERPROFILE\Downloads", 'C:\Users\Administrator\Downloads', 'D:\InkCanvas'
    )
    foreach ($d in $candidates) {
        if (-not (Test-Path $d)) { continue }
        $z = Get-ChildItem -Path $d -Filter '*.zip' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match 'command.?line.?tools' } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($z) { return $z.FullName }
    }
    return $null
}

$clt = Join-Path $InstallRoot 'command-line-tools'

if (-not $SkipExtract) {
    if (-not $ZipPath) { $ZipPath = Find-Zip }
    if ($ZipPath) {
        Write-Host "解压 $ZipPath -> $InstallRoot"
        if (-not (Test-Path $InstallRoot)) { New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null }
        Expand-Archive -LiteralPath $ZipPath -DestinationPath $InstallRoot -Force
    } elseif (-not (Test-Path (Join-Path $clt 'bin'))) {
        throw "未找到 command-line-tools zip，也未找到已解压的 $clt\bin。请把 zip 放到 D:\dev-cache\harmony\ 或用 -ZipPath 指定。"
    }
}

if (-not (Test-Path $clt)) {
    # 有的包解压后直接是 bin/sdk/...（没有 command-line-tools 外层）
    if (Test-Path (Join-Path $InstallRoot 'bin')) { $clt = $InstallRoot }
    else { throw "安装目录布局异常：$InstallRoot 下没有 command-line-tools 或 bin" }
}

Write-Host "`n=== 目录布局（深度 2） ==="
Get-ChildItem $clt | ForEach-Object {
    Write-Host ("  " + $_.Name + $(if ($_.PSIsContainer) { '/' }))
    if ($_.PSIsContainer) {
        Get-ChildItem $_.FullName -ErrorAction SilentlyContinue | Select-Object -First 12 | ForEach-Object {
            Write-Host ("    " + $_.Name + $(if ($_.PSIsContainer) { '/' }))
        }
    }
}

Write-Host "`n=== 工具版本验证 ==="
$bin = Join-Path $clt 'bin'
foreach ($tool in @('codelinter', 'ohpm', 'hvigorw')) {
    $exe = Get-ChildItem $bin -Filter "$tool*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $exe) { Write-Host "  [缺失] $tool"; continue }
    try {
        $out = & $exe.FullName -v 2>&1 | Select-Object -First 3
        Write-Host ("  [{0}] {1}" -f $tool, ($out -join ' | '))
    } catch {
        Write-Host ("  [{0}] 调用失败：{1}" -f $tool, $_.Exception.Message)
    }
}

# 记录工具链位置，供仓库构建脚本读取（此文件 gitignore）
$record = [ordered]@{
    commandLineTools = $clt
    bin              = $bin
    recordedAt       = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
}
$sdkDir = Join-Path $clt 'sdk'
if (Test-Path $sdkDir) {
    $record.sdk = $sdkDir
    $record.sdkLayout = @(Get-ChildItem $sdkDir -Recurse -Depth 2 -Directory | ForEach-Object { $_.FullName.Substring($clt.Length + 1) })
}
$recordPath = Join-Path $PSScriptRoot 'toolchain.local.json'
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $recordPath -Encoding UTF8
Write-Host "`n工具链记录已写入 $recordPath"
Write-Host "如需交互式使用，可手动把 $bin 加入 PATH（本脚本不改系统 PATH）。"