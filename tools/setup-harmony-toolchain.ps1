# setup-harmony-toolchain.ps1 — 验证 HarmonyOS 命令行工具（内嵌 SDK）并配置 E 盘缓存
# 实际安装位置：E:\EcoStudioCLI\command-line-tools（用户解压）
# 构建缓存统一放 E 盘：E:\harmony-cache\{hvigor,ohpm,npm}
# 不修改系统 PATH / 不写用户级环境变量；构建脚本用绝对路径 + 会话级 env 调用。
param(
    [string]$CliRoot = 'E:\EcoStudioCLI',
    [string]$CacheRoot = 'E:\harmony-cache',
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'

function Resolve-ToolsRoot([string]$root) {
    if (Test-Path (Join-Path $root 'command-line-tools\bin')) { return (Join-Path $root 'command-line-tools') }
    if (Test-Path (Join-Path $root 'bin')) { return $root }
    throw "在 $root 下找不到 command-line-tools\bin 或 bin"
}

$clt = Resolve-ToolsRoot $CliRoot
$bin = Join-Path $clt 'bin'
$required = @('bin', 'sdk', 'hvigor', 'ohpm', 'codelinter')
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $clt $_)) })

if ($missing.Count -gt 0) {
    Write-Host ("[未就绪] 缺少目录：{0}（可能解压未完成）" -f ($missing -join ', '))
    Get-ChildItem $clt | Select-Object Name, LastWriteTime | Format-Table | Out-String | Write-Host
    exit 2
}

if (-not $Quiet) {
    Write-Output '=== 目录布局 ==='
    Get-ChildItem $clt | Select-Object Name | Format-Table | Out-String | Write-Host
    Get-ChildItem (Join-Path $clt 'sdk\default') -ErrorAction SilentlyContinue | Select-Object Name | Format-Table | Out-String | Write-Host
    Write-Output '=== 工具版本 ==='
    foreach ($tool in @('ohpm', 'codelinter', 'hvigorw')) {
        $bat = Join-Path $bin "$tool.bat"
        if (-not (Test-Path $bat)) { Write-Host "  [缺失] $tool"; continue }
        try { $out = & $bat -v 2>&1 | Select-Object -First 2; Write-Host ("  {0}: {1}" -f $tool, ($out -join ' ')) }
        catch { Write-Host ("  {0}: 调用失败 {1}" -f $tool, $_.Exception.Message) }
    }
}

# --- E 盘缓存目录 ---
foreach ($sub in @('hvigor', 'ohpm', 'npm')) {
    $p = Join-Path $CacheRoot $sub
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Force -Path $p | Out-Null }
}

# 生成会话环境脚本（构建前 dot-source）
$envScript = Join-Path $PSScriptRoot 'harmony-env.ps1'
@"
# 由 setup-harmony-toolchain.ps1 生成 — 会话级 HarmonyOS 构建环境
`$env:PATH = "$bin;" + `$env:PATH
`$env:HVIGOR_USER_HOME = "$CacheRoot\hvigor"
`$env:OHPM_HOME = "$CacheRoot\ohpm"
`$env:NPM_CONFIG_CACHE = "$CacheRoot\npm"
`$env:OHOS_BASE_SDK_HOME = "$clt\sdk"
"@ | Set-Content -LiteralPath $envScript -Encoding UTF8

$record = [ordered]@{
    commandLineTools = $clt
    bin              = $bin
    sdk              = Join-Path $clt 'sdk'
    sdkDefault       = Join-Path $clt 'sdk\default'
    cacheRoot        = $CacheRoot
    envScript        = $envScript
    recordedAt       = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
}
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchain.local.json') -Encoding UTF8

Write-Host "[就绪] 工具链 $clt"
Write-Host "[缓存] $CacheRoot\{hvigor,ohpm,npm}"
Write-Host "[环境] 构建前执行：. `"$envScript`""
