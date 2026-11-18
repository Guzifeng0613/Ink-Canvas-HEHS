# device-install.ps1 — 真机安装/启动/截图/日志（HarmonyOS）
# 用法：
#   pwsh -File tools/device-install.ps1 -HapPath <signed.hap>
#   pwsh -File tools/device-install.ps1 -HapPath <unsigned.hap>   # 自动尝试本地签名（需证书链，推荐 DevEco 自动签名）
param(
    [string]$HapPath = 'D:\InkCanvas\harmony\entry\build\default\outputs\default\entry-default-unsigned.hap',
    [string]$BundleName = 'com.inkcanvas.harmony',
    [string]$Ability = 'EntryAbility',
    [string]$OutDir = 'D:\InkCanvas\artifacts\device',
    [switch]$SkipSign
)
$ErrorActionPreference = 'Stop'
$hdc = 'E:\EcoStudioCLI\command-line-tools\sdk\default\openharmony\toolchains\hdc.exe'
if (-not (Test-Path $hdc)) { throw "hdc 不存在：$hdc" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# 1) 设备在线
$targets = & $hdc list targets 2>&1
if (-not $targets -or $targets -match 'Empty') { throw '未检测到设备：请 USB 连接并开启开发者模式/USB 调试，或 hdc tconn <ip:port>' }
Write-Host "设备: $targets"

# 2) 签名（未签名包尝试本地签名）
$installPath = $HapPath
if (-not $SkipSign -and $HapPath -match 'unsigned') {
    $signed = $HapPath -replace 'unsigned', 'signed'
    try {
        & pwsh -NoProfile -File 'D:\InkCanvas\tools\sign-hap.ps1' -HapPath $HapPath -OutPath $signed -BundleName $BundleName
        if (Test-Path $signed) { $installPath = $signed }
    } catch {
        Write-Host '本地签名失败（需完整证书链）。请在 DevEco Studio 用华为账号自动签名后，用签名包重试 -SkipSign。'
        throw
    }
}

# 3) 安装
Write-Host "安装: $installPath"
& $hdc install -r $installPath
if ($LASTEXITCODE -ne 0) { throw '安装失败（检查签名/包名/设备架构）' }

# 4) 启动
& $hdc shell aa start -a $Ability -b $BundleName
Start-Sleep -Seconds 4

# 5) 截图
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
& $hdc shell snapshot_display -f /data/local/tmp/inkcanvas.jpeg
& $hdc file recv /data/local/tmp/inkcanvas.jpeg "$OutDir\screen-$stamp.jpeg"
Write-Host "截图: $OutDir\screen-$stamp.jpeg"

# 6) 日志（最近 InkCanvas 相关）
& $hdc shell hilog -x > "$OutDir\hilog-$stamp.txt" 2>&1
Select-String -Path "$OutDir\hilog-$stamp.txt" -Pattern 'InkCanvas|JsError|AppFreeze|CppCrash' | Select-Object -Last 40 | ForEach-Object { Write-Host $_.Line }
Write-Host "日志: $OutDir\hilog-$stamp.txt"
Write-Host '安装启动完成。运行验证清单见 tools/device-verify.ps1。'
