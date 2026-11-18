# sign-hap.ps1 — 用 SDK 自带 OpenHarmony 调试证书对未签名 HAP 做本地签名（模拟器/开发机安装用）
# 注意：本脚本为最佳尝试。实测 hap-sign-tool 要求 appCertFile 为完整证书链（.cer），
# 直接导出 p12 叶子证书不被接受；需先用 generate-app-cert 以 CA 签发应用证书链，
# 或直接在 DevEco Studio 用华为账号自动签名（推荐）。HarmonyOS 商用设备需 AGC 调试 Profile。
param(
    [string]$HapPath = 'D:\InkCanvas\harmony\entry\build\default\outputs\default\entry-default-unsigned.hap',
    [string]$OutPath = 'D:\InkCanvas\harmony\entry\build\default\outputs\default\entry-default-signed.hap',
    [string]$BundleName = 'com.inkcanvas.harmony'
)
$ErrorActionPreference = 'Stop'
$clt = 'E:\EcoStudioCLI\command-line-tools'
$java = Join-Path $clt 'tool\jre\bin\java.exe'
$keytool = Join-Path $clt 'tool\jre\bin\keytool.exe'
$lib = Join-Path $clt 'sdk\default\openharmony\toolchains\lib'
$signTool = Join-Path $lib 'hap-sign-tool.jar'
$keystore = Join-Path $lib 'OpenHarmony.p12'
$profileCert = Join-Path $lib 'OpenHarmonyProfileDebug.pem'
$work = 'D:\dev-cache\harmony\sign'
New-Item -ItemType Directory -Force $work | Out-Null

if (-not (Test-Path $HapPath)) { throw "未找到未签名 HAP：$HapPath（先运行 hvigorw assembleHap）" }

# 1) 导出应用证书（PEM）
$appCert = Join-Path $work 'app-cert.pem'
& $keytool -exportcert -rfc -alias 'openharmony application release' -keystore $keystore -storetype PKCS12 -storepass 123456 -file $appCert | Out-Null
$caCert = Join-Path $work 'ca-cert.pem'
& $keytool -exportcert -rfc -alias 'openharmony application ca' -keystore $keystore -storetype PKCS12 -storepass 123456 -file $caCert | Out-Null
Get-Content $caCert | Add-Content $appCert

# 2) 准备调试 Profile 模板（替换 bundle-name）
$template = Get-Content (Join-Path $lib 'UnsgnedDebugProfileTemplate.json') -Raw | ConvertFrom-Json
$template.'bundle-info'.'bundle-name' = $BundleName
$unsignedProfile = Join-Path $work 'profile-unsigned.json'
$template | ConvertTo-Json -Depth 10 | Set-Content $unsignedProfile -Encoding UTF8

# 3) 签名 Profile
$signedProfile = Join-Path $work 'profile-signed.p7b'
& $java -jar $signTool sign-profile `
    -keyAlias 'openharmony application profile debug' `
    -signAlg 'SHA256withECDSA' `
    -mode 'localSign' `
    -profileCertFile $profileCert `
    -inFile $unsignedProfile `
    -keystoreFile $keystore `
    -keystorePwd '123456' `
    -keyPwd '123456' `
    -outFile $signedProfile

# 4) 签名 HAP
& $java -jar $signTool sign-app `
    -keyAlias 'openharmony application release' `
    -signAlg 'SHA256withECDSA' `
    -mode 'localSign' `
    -appCertFile $appCert `
    -profileFile $signedProfile `
    -inFile $HapPath `
    -keystoreFile $keystore `
    -keystorePwd '123456' `
    -keyPwd '123456' `
    -outFile $OutPath

Write-Host "signed: $OutPath"
Write-Host '安装：hdc install -r <signed.hap>（hdc 位于 sdk\default\openharmony\toolchains）'
