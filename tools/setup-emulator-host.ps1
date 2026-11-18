# setup-emulator-host.ps1 — 在具备虚拟化能力的物理 Windows 机上部署 HarmonyOS 模拟器
# 适用：Windows 10/11 64 位（专业/企业/教育）或 Windows Server + 嵌套虚拟化/裸金属；BIOS 已开虚拟化。
# 用法（管理员 PowerShell）：
#   pwsh -File tools/setup-emulator-host.ps1 -CliRoot 'C:\command-line-tools' [-Drive D] [-DeviceType tablet]
param(
    [string]$CliRoot = 'C:\command-line-tools',
    [string]$Drive = 'D',
    [string]$DeviceType = 'tablet',
    [string]$OsVersion = 'HarmonyOS 7.0.0(26.0.0)',
    [string]$ScreenProfile = 'MatePad Pro 13',
    [string]$InstanceName = 'InkPad26'
)
$ErrorActionPreference = 'Stop'

Write-Host '=== 1. 宿主能力检查 ==='
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
Write-Host ("CPU: {0}  VirtualizationFirmwareEnabled={1}" -f $cpu.Name, $cpu.VirtualizationFirmwareEnabled)
$hv = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -ErrorAction SilentlyContinue
if ($hv -and $hv.State -ne 'Enabled') {
    Write-Host 'Hyper-V 未启用，正在启用（需重启后重跑本脚本）...'
    Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All -NoRestart | Out-Null
    Write-Host '请重启计算机后重新运行本脚本。'; exit 2
}
if (-not $hv) { Write-Host '警告：无法查询 Hyper-V 功能（可能为家庭版/不支持宿主）。模拟器可能无法启动。' }

$emu = Join-Path $CliRoot 'bin\Emulator.bat'
if (-not (Test-Path $emu)) { throw "未找到 Emulator：$emu（请安装 DevEco Command Line Tools 并指定 -CliRoot）" }

Write-Host '=== 2. 许可与镜像 ==='
& $emu -license accept | Out-Null
& $emu -install -deviceType $DeviceType -osVersion $OsVersion

Write-Host '=== 3. 创建实例（目标盘需 ≥12GB 空闲） ==='
$inst = "${Drive}:\harmony-emulator\instances"
$img = Join-Path $env:LOCALAPPDATA 'Huawei\Sdk'
New-Item -ItemType Directory -Force $inst | Out-Null
& $emu -create $InstanceName -deviceType $DeviceType -osVersion $OsVersion -screenProfile $ScreenProfile -instancePath $inst -imageRoot $img -memory 4096 -storage 8192

Write-Host '=== 4. 启动（无窗口） ==='
Start-Process -FilePath $emu -ArgumentList @('-start', $InstanceName, '-instancePath', $inst, '-imageRoot', $img, '-noWindow')

Write-Host '=== 5. 等待设备上线 ==='
$hdc = Join-Path $CliRoot 'sdk\default\openharmony\toolchains\hdc.exe'
for ($i = 0; $i -lt 60; $i++) {
    $t = & $hdc list targets 2>&1
    if ($t -and $t -notmatch 'Empty') { Write-Host "设备上线: $t"; break }
    Start-Sleep -Seconds 5
}
Write-Host '完成。后续：构建 → 签名 → tools/device-install.ps1 安装 → tools/device-verify.ps1 验证。'
