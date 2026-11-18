# 14 — WPS 鸿蒙版拉起与 AppGallery 更新集成（HarmonyOS NEXT 三方集成面）

- 日期：2026-11-18
- 角色：只读研究员（vendored KB 提取 + 官方文档/官方站点在线核验）
- 版本基线：HarmonyOS 26.0.0 / API 26（Release）为主；HarmonyOS 6.1.1 / API 24 向下兼容。26.0.1 Beta1 不在生产基线（Beta 工具链产物不能上架）。
- 任务范围：WPS 鸿蒙版 bundleName / 打开 .ppt/.pptx 的 Want / 放映意图 / 沙箱文件 URI 授权 / 安装检测；AppGallery 详情页跳转与更新流程；API 24 vs 26 差异。
- 证据标记：
  - **【官方】** 华为开发者文档或 WPS 官方站点/官方论坛答复原文；
  - **【上游】** OpenHarmony master 文档（gitee openharmony/docs，HarmonyOS 的同构上游，API 行为一致但华为侧可能有增删）；
  - **【三方】** 第三方下载站/社区帖子，仅作旁证，质量已标注；
  - **【推断】** 基于以上事实的工程判断；
  - **【未验证】** 无文档或本机无法确认，必须实机验证。
- 验证边界：**本次未连接任何 HarmonyOS 设备**（`hdc list targets` 返回 `[Empty]`），未编译/未安装 HAP，所有 Want 拉起、WPS 行为、市场弹框均为**文档级结论，未实机运行**。WebSearch 工具本会话不可靠（返回生成式摘要而非真实检索结果），全部关键结论已改用 curl/WebFetch 直接抓取原始页面核验；抓取缓存位于 `D:\tmp\harmony-research\`。

---

## 0. 结论摘要（先看这里）

1. **WPS 鸿蒙版 bundleName 不是 Android 的 `cn.wps.moffice_eng`。** WPS 官方鸿蒙站点 harmony.wps.cn 的前端代码明确区分两个包：**手机版 `cn.wps.mobileoffice.hap`**、**Pad 版 `cn.wps.office.hap`**【官方】。`cn.wps.moffice_eng` 是 Android 国际版包名（社区帖子把它误抄到 NEXT 场景）。
2. **打开 .ppt/.pptx 走系统标准「文件打开」隐式 Want**：`action: 'ohos.want.action.viewData'` + `uri: file://<本应用bundleName>/<沙箱路径>`（`fileUri.getUriFromPath` 生成）+ `type`（UTD 或 MIME）+ `flags: FLAG_AUTH_READ_URI_PERMISSION(0x1)`。要定向 WPS：设 `bundleName` 而**留空 `abilityName`**（= 限定包内的隐式匹配，上游匹配规则明确支持）。WPS 的 abilityName 无公开文档，**不要硬编码**（论坛流传的 `PreStartActivity2` 是 Android Activity 名，错误）。
3. **放映/slideshow 启动意图：无公开文档，按「不存在」处理。** 未找到 WPS 鸿蒙版任何公开的放映 want 参数、`wps://`/`kwps://` scheme 或开放平台 NEXT 集成文档。`.ppsx`（UTD `org.openxmlformats.presentationml.slideshow`）作为「幻灯片放映」文件格式存在，打开它*可能*自动进入放映【推断，未验证】，不能作为功能依赖。
4. **安装检测：`canOpenLink` 在 `bundleManager` 上（不是 UIAbilityContext），API 12+，且 scheme 必须先写进 entry 的 `querySchemes`。** WPS 没有公开 scheme，所以 canOpenLink 无法直接检测 WPS。`getLaunchWantForBundleSync`（API 24+）与 `getBundleInfo`（查他包）都要 `ohos.permission.GET_BUNDLE_INFO_PRIVILEGED`——不在全应用开放权限列表，普通三方应用拿不到。**实用检测 = 直接 startAbility 并捕获 16000019/16000001**（或 API 21+ 的 `openLink` `completionHandler.onRequestFailure`）。
5. **AppGallery 详情页有三条官方路径**（均已在华为文档核验，且 WPS 官网在生产使用 store:// 形式）：
   - 推荐：`productViewManager.loadProduct(context, { parameters: { bundleName: '<目标包名>' } }, callback)`（`@kit.AppGalleryKit`，API 11+）；
   - Deep Linking：`startAbility({ action: 'ohos.want.action.appdetail', uri: 'store://appgallery.huawei.com/app/detail?id=<bundleName>' })`（**action 是 `appdetail`，不是 viewData**）；
   - App Linking：`openLink('https://appgallery.huawei.com/app/detail?id=<bundleName>', { appLinkingOnly: false })`。
6. **更新有公开查询 API，但没有静默自更新 API。** `updateManager.checkAppUpdate(context)`（API 12+）查是否有新版；`updateManager.showUpdateDialog(context)` 弹市场升级框，下载安装由应用市场完成。约束：应用已上架、本地版本低于在架版本、签名一致、前台调用、不支持模拟器、仅中国境内。**「打开市场页/升级弹框」是唯一合规路径**，与 KB 13 的结论一致。
7. **API 24 vs 26：本主题所有核心能力两侧都可用**（openLink 12+、canOpenLink 12+、文件打开 Want、loadProduct 11+、updateManager 12+）。差异集中在设备扩展与少数新接口：`getLaunchWantForBundleSync` 仅 24+（但三方不可用）；loadProduct 的 TV 支持 22+；updateManager 的 Wearable 20+ / TV 19+；Car 支持要 26.0.1（Beta，不在基线）；`getInstalledBundleList` 26.0.0 但仅企业权限。

---

## 1. vendored KB 覆盖度

对 `D:\InkCanvas\.claude\skills\harmony-os\` 全量 grep（WPS / want / openLink / canOpenLink / fileShare / AppGallery / 应用市场 / market / store / 更新）：

| 主题 | KB 现状 |
|---|---|
| WPS | **零命中**。KB 无任何 WPS 集成内容。 |
| openLink / canOpenLink / fileShare | **零命中**。`dev/references/app-framework.md` 只有显式/隐式 Want 的最小示例（bundleName+abilityName、skills 配置），无 openLink、无 URI 授权、无 querySchemes。 |
| AppGallery 更新 | `agc/references/monetization.md:215` 列出「应用市场更新功能：查询应用是否有可更新版本，应用内显示更新提醒」，支持 Phone/PC/2in1/Tablet/Wearable/TV；`:224` 模拟器不支持、仅中国境内。**API 名/签名/起始版本均未给出**（KB 13 §0.9 已标记此缺口）。 |
| AppGallery 详情页跳转 | KB 未给出任何 URI/API（KB 13 §1 表格「分发/更新」行已标记缺口）。 |
| 应用市场 bundleName | 零命中（无 `com.huawei.appmarket` / `com.huawei.hmsapp.appgallery` / `store://` / `market://`）。 |

本文填补上述全部缺口。项目侧现状：`harmony/core/.../AppSettings.ts:75` 已有 `isSupportWPS` 键（Settings.json 两侧一致），`harmony/entry/.../Index.ets:1266,1276` 目前把 WPS 支持标记为「COM 互操作不可用；课件请导出 PDF」——本文给出 ArkTS 侧可行的替代集成面（只读查看/编辑跳转，非 COM 级控制）。

---

## 2. WPS 鸿蒙版（Kingsoft WPS for HarmonyOS NEXT）

### 2.1 bundleName（权威来源 + 证据链）

**WPS 官方鸿蒙站点 https://harmony.wps.cn/ 的前端 JS（`harmony.761d27de.js`，2026-11-18 抓取）中 `jumpToAppStore` 方法原文：**

```js
jumpToAppStore(t) {
  const e = "hiapplink://com.huawei.appmarket?appId=C121553&callType=AGDAPI",
        n = "store://appgallery.huawei.com/app/detail?id=cn.wps.office.hap",
        o = "store://appgallery.huawei.com/app/detail?id=cn.wps.mobileoffice.hap";
  if ("pad" === t) { window.open(n, "_self"); /* 1s 后回退 e */ }
  else             { window.open(o, "_self"); /* 1s 后回退 e */ }
}
```

站点按钮配置：`buttons.mobile.text = "鸿蒙手机版"` → `openHarmony("mobile")`；`buttons.pc.text = "鸿蒙Pad版"` → `openHarmony("pad")`。由此：

| 产品 | bundleName | 证据 |
|---|---|---|
| WPS Office 鸿蒙手机版（Phone） | **`cn.wps.mobileoffice.hap`** | 【官方】harmony.wps.cn JS；【三方】32r.com 下载页（v26.11.0，2026-11-18 更新，厂商珠海金山办公，包名字段一致） |
| WPS Office 鸿蒙 Pad 版（Tablet） | **`cn.wps.office.hap`** | 【官方】同上 |
| WPS Android 国际版（**不是 NEXT**） | `cn.wps.moffice_eng` | 【三方】downxia 下载页明确「系统要求：Android 7.0」；8688g 的「WPS鸿蒙版」页实为 2024-05 的 Android APK（权限列表全是 Android 权限），属误标 |
| WPS 的 AppGallery appId | `C121553`（hiapplink 回退参数） | 【官方】同上 JS |

冲突来源说明：itying 论坛帖（https://bbs.itying.com/topic/68fd494adf49280042643c18 ）声称 NEXT 用 `cn.wps.moffice_eng` + `abilityName: "cn.wps.moffice.documentmanager.PreStartActivity2"` + `action: "android.intent.action.VIEW"`——**三者均为 Android 概念误植**（PreStartActivity2 是 Android Activity；NEXT 的 action 命名空间是 `ohos.want.action.*`），该帖质量低，仅记录不采信。

**【未验证】**：
- 两个 bundle 的设备覆盖边界（`cn.wps.office.hap` 是否也覆盖 PC/2in1 鸿蒙电脑；手机版能否装在平板上）——站点只分 mobile/pad 两按钮，无 PC 按钮。InkCanvas 目标设备（平板/PC）应优先尝试 `cn.wps.office.hap`，失败再试 `cn.wps.mobileoffice.hap`【推断】。
- 实机确认方式：设置→应用管理查看包名，或 `hdc shell bm dump -n cn.wps.mobileoffice.hap`（需设备连接）。
- WPS 官方论坛答复（https://bbs.wps.cn/topic/56423 ，「请提供下鸿蒙WPS的HAP安装包」）官方账号「WPS反馈小助理小绿」回复：鸿蒙版到 https://harmony.wps.cn/ 下载——这是把 bundleName 证据锚定到官方域名的关键链接。

### 2.2 打开 .ppt/.pptx：标准文件打开 Want（代码就绪）

华为官方指南「拉起文件处理类应用（startAbility）」（https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/file-processing-apps-startup ，与上游 OpenHarmony `file-processing-apps-startup.md` 逐参数一致，已双侧核验）：

**Want 参数（调用方）：**

| 字段 | 值 | 说明 |
|---|---|---|
| `action` | `'ohos.want.action.viewData'` | 文件打开场景**固定值**【官方】 |
| `uri` | `file://<属主bundleName>/<沙箱路径>` | 用 `fileUri.getUriFromPath(path)` 生成，**不要手拼**（编解码不保证）【官方】 |
| `type` | UTD（推荐）或 MIME | 不传则系统按 uri 后缀推断；传了必须与文件一致；**不支持 `*/*`**【官方】 |
| `flags` | `wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION`（0x00000001）｜需要回写时再或上 `FLAG_AUTH_WRITE_URI_PERMISSION`（0x00000002） | **临时**授予接收方读/写该 URI 的权限【官方 wantConstant】 |
| `bundleName` | 可选：`'cn.wps.mobileoffice.hap'`（或 pad 包） | 设了 bundleName 且 **abilityName 留空** = 限定该包内的隐式匹配【上游 explicit-implicit-want-mappings.md：隐式匹配中 bundleName「匹配对应应用包内的目标应用组件」，abilityName「必须留空表示隐式匹配」】 |
| `abilityName` | **留空** | WPS 无公开 abilityName；显式匹配要求 abilityName 必填，故不要走显式 |
| `parameters` | 见下表 | 可选 |

**parameters 可选键（官方表 2）：**

| key | 类型 | 说明 |
|---|---|---|
| `ability.params.stream` | string[]（文件 URI 数组） | 主文件依赖的其他文件一并授权（如 html 依赖资源）；PPT 单文件场景不需要 |
| `ohos.ability.params.showDefaultPicker` | boolean | true = 强制弹「打开方式」选择框；缺省 false 由系统策略/默认应用决定 |
| `showCaller` | boolean | 调用方自己也匹配成功时是否出现在选择框，缺省 false |
| `linkFeature` | `'FileOpen'` | 目标方 skills 的 uris 里 `linkFeature: "FileOpen"`（大小写敏感）声明文件打开能力；隐式匹配优先按 linkFeature 匹配【上游】 |

**PPT 相关 UTD / MIME（上游 `uniform-data-type-list.md` 预置列表原文）：**

| 扩展名 | UTD（推荐写进 type） | MIME（兼容写法） |
|---|---|---|
| .pptx | `org.openxmlformats.presentationml.presentation` | `application/vnd.openxmlformats-officedocument.presentationml.presentation` |
| .ppt | `com.microsoft.powerpoint.ppt` | `application/vnd.ms-powerpoint` |
| .ppsx | `org.openxmlformats.presentationml.slideshow` | `application/vnd.openxmlformats-officedocument.presentationml.slideshow` |
| .potx | `org.openxmlformats.presentationml.template` | `application/vnd.openxmlformats-officedocument.presentationml.template` |
| .dps（WPS 演示） | `com.kingsoft.office.presentation.dps` | —（列表未给 MIME） |

**ArkTS 示例（InkCanvas 课件降级查看路径）：**

```ts
import { common, Want, wantConstant } from '@kit.AbilityKit';
import { fileUri } from '@kit.CoreFileKit';
import { BusinessError } from '@kit.BasicServicesKit';

// 沙箱内课件文件（el2/base 下可分享，见 §2.4）
const filePath = context.filesDir + '/courseware/lesson.pptx';
const uri = fileUri.getUriFromPath(filePath);
// => 'file://<InkCanvas bundleName>/data/storage/el2/base/files/courseware/lesson.pptx'

const want: Want = {
  action: 'ohos.want.action.viewData',
  uri: uri,
  type: 'org.openxmlformats.presentationml.presentation', // UTD；也可用 MIME
  flags: wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION,
  // 定向 WPS（可选）：留空 abilityName，按包限定隐式匹配。
  // 不写 bundleName 则交给系统选择框/默认应用（更稳健，推荐默认行为）。
  bundleName: 'cn.wps.mobileoffice.hap', // Pad 设备可先试 'cn.wps.office.hap'【未验证设备覆盖】
};
try {
  await context.startAbility(want);
} catch (err) {
  const e = err as BusinessError;
  // 16000019 = 无匹配 ability（WPS 未安装/不支持该类型）→ 降级：应用内 PDF 渲染或提示安装
  // 16000001 = 指定 ability 不存在
  console.error(`open courseware failed: ${e.code} ${e.message}`);
}
```

目标方（WPS）侧的匹配前提【官方/上游】：其 module.json5 skills 声明 `actions: ["ohos.want.action.viewData"]` + `uris: [{ scheme: "file", type: <UTD/MIME>, linkFeature: "FileOpen" }]`。WPS 作为默认办公应用必然声明了文件打开能力【推断，基于其系统默认应用地位；具体 skills 内容未验证】。

**【未验证】**：WPS 鸿蒙版对 .ppt/.pptx 的实际打开体验、是否回写保存到我的沙箱（只读授权下 WPS 通常另存到用户目录）、FLAG_AUTH_WRITE 下能否原地保存——均需实机。InkCanvas 主路径仍是「PC 端导出 PDF → 应用内 PDF Kit 渲染 + 自绘墨迹」（KB 13 结论），WPS 跳转只是辅助查看/编辑出口。

### 2.3 放映（slideshow）启动意图：**无公开文档**

- 华为文档、WPS 开放平台（open.wps.cn，文档 SPA 无 NEXT 拉起章节可公开抓取）、WPS 官方论坛、中英文检索均未发现 WPS 鸿蒙版的放映 want 参数、`wps://` / `kwps://` / `kso://` scheme 或「放映模式」intent。**按不存在处理，不要发明 scheme。**
- WinUI3 侧的 `KWPP.Application` COM 控制（`src/InkCanvas.Features/Ppt/PptInteropService.cs:30`，含 `ppShowSlideRange` 等放映控制）在 NEXT **没有对等物**：WPS 未开放 NEXT 的进程间控制 SDK（itying 帖亦称深度集成需联系 WPS 官方获取非开源 SDK——三方说法，未证实）。
- 唯一可尝试的「放映」语义载体是 **.ppsx 文件**（UTD `org.openxmlformats.presentationml.slideshow`，官方 UTD 列表描述为「Office Open XML 幻灯片放映类型」）：把课件另存为 .ppsx 再用 §2.2 的 Want 打开，*可能*直接进入放映【推断，未验证 WPS 行为】。
- 结论：**放映控制功能在 HarmonyOS 侧标记为「不可用/待 WPS 官方开放」**，与现有 Index.ets 的「PPT 专属项仅保留 Settings.json 值」降级口径一致。

### 2.4 沙箱文件 URI 授权细节

- **URI 规范**【官方 share-app-file】：`file://<bundleName>/<path>`，bundleName 是文件属主（即 InkCanvas 自己），path 是沙箱绝对路径。可分享目录：`/data/storage/el1/base`、`/data/storage/el2/base`、`/data/storage/el2/distributedfiles`。`context.filesDir`/`cacheDir`/`tempDir` 均在 el2/base 下，可分享。
- **授权方式**：startAbility 时 `want.flags` 带 `FLAG_AUTH_READ_URI_PERMISSION`/`FLAG_AUTH_WRITE_URI_PERMISSION`，是**临时授权**（随本次拉起生效）【官方 wantConstant：「临时授予接收方读取该URI指向的数据的权限」】。每次拉起都要带 flags。
- **`fileShare` 模块不是用来「授权给别人」的**（常见误解）：`@kit.CoreFileKit` 的 `fileShare.persistPermission / activatePermission / deactivatePermission / revokePermission / checkPersistentPermission`（API 11+/12+，需 `ohos.permission.FILE_ACCESS_PERSIST`）是把**本应用已获得的临时权限**（如 DocumentViewPicker 选中的用户文件）持久化，供自己后续访问；**不能**用它把自家沙箱文件授权给 WPS。给 WPS 授权只走 want.flags【官方 js-apis-fileShare.md + share-app-file.md】。
- 接收方（WPS）用 `fileIo.openSync(uri, ...)` 直接读 URI【官方目标方示例】。
- 若课件来自用户选择（picker uri，`file://docs/...`），把该 uri 原样转发给 WPS 时同样需要 flags 授权；picker 给的临时权限可通过 fileShare.persistPermission 持久化后再转发【推断组合用法，未验证】。

### 2.5 检测 WPS 是否安装

| 方案 | API/起始版本 | 三方 NEXT 可用性 | 结论 |
|---|---|---|---|
| `bundleManager.canOpenLink(link)` | **12+**（华为与上游一致） | 可用，但**scheme 必须先配置在 entry 模块 module.json5 的 `querySchemes` 数组**（每项 ≤128 字节；≤API 20 最多 50 个，API 21+ 最多 200 个）。错误码 17700055（link 无效）/17700056（scheme 不在 querySchemes）。 | **WPS 无公开 scheme → 无法直接检测 WPS**。只能检测已知 scheme 的应用（官方示例用 `welink://`）。注意：canOpenLink 在 **bundleManager** 上，**UIAbilityContext 没有 canOpenLink**（华为当前 UIAbilityContext 页全文无此方法，已核验）。 |
| `bundleManager.getLaunchWantForBundleSync(bundleName, userId?)` | **24+**（API 23 及以下无） | 需 `ohos.permission.GET_BUNDLE_INFO_PRIVILEGED`（跨用户再加 INTERACT_ACROSS_LOCAL_ACCOUNTS）。该权限**不在**全应用开放权限列表（permissions-for-all.md 仅含 normal 级的 GET_BUNDLE_INFO），属系统级权限，普通三方应用无法获得（ACL 跨级需华为审批的证书 profile）【未验证具体审批口径】。 | **三方不可用**。且它返回的是启动 Want，不是「是否安装」布尔。 |
| `bundleManager.getBundleInfo(bundleName, ...)` 查他包 | 14+ | 三方查他包同样要 GET_BUNDLE_INFO_PRIVILEGED（官方原文：「当调用方为三方应用时，需要申请 ohos.permission.GET_BUNDLE_INFO_PRIVILEGED 权限」）。 | **三方不可用**。 |
| `bundleManager.getInstalledBundleList` | 26.0.0 | 需 `ohos.permission.ENTERPRISE_GET_INSTALLED_BUNDLE_LIST`（企业/MDM 权限）。 | **普通三方不可用**。 |
| **隐式 startAbility + 错误码**（推荐） | 全版本 | 无需权限。无匹配 → `16000019`（No matching ability is found）；指定 ability 不存在 → `16000001`。 | **实用检测路径**：直接发起 §2.2 的打开请求，catch 16000019 即视为「无可用办公应用」，降级到应用内渲染/引导安装。缺点：检测与拉起合二为一，无法「静默预检」。 |
| `openLink` + `completionHandler`（API 21+） | 21+ | `OpenLinkOptions.completionHandler.onRequestFailure(elementName, message)` 可在拉起失败时回调；`hideFailureTipDialog: true`（21+，仅 appLinkingOnly=false 的 Deep Linking 流程生效）可抑制「暂无可用打开方式」系统弹框。 | 若走 Deep Linking（需已知 scheme），可做「静默预检 + 失败不弹框」。WPS 无公开 scheme，同样受限。 |

**工程建议**【推断】：InkCanvas 不做静默预检，采用「点击『用 WPS 打开』→ startAbility → catch 16000019 → 提示未安装并给 AppGallery 详情页跳转（§3.1，id=cn.wps.mobileoffice.hap / cn.wps.office.hap）」的闭环。`isSupportWPS` 设置项语义改为「允许跳转外部办公应用」，不再承诺 COM 级控制。

---

## 3. AppGallery（华为应用市场）

### 3.1 打开应用详情页（三条官方路径）

来源：华为官方指南「展示应用详情页面」（https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/appgallery-productview-loadproduct/ ，已全文核验）+ API 参考 store-productviewmanager。

**路径 A（官方推荐）：`productViewManager.loadProduct`**

```ts
import { common, Want } from '@kit.AbilityKit';
import { productViewManager } from '@kit.AppGalleryKit';
import { BusinessError } from '@kit.BasicServicesKit';

const context = this.getUIContext().getHostContext() as common.UIAbilityContext;
const want: Want = {
  parameters: {
    bundleName: 'cn.wps.mobileoffice.hap', // 必填：要展示详情页的目标应用包名
    // skExposure: {...}  // 可选：归因曝光数据（SKExposure，14+），InkCanvas 不需要
  }
};
productViewManager.loadProduct(context, want, {
  onError: (e: BusinessError) => { /* 1011 拉起/切前台失败；1012 切后台失败；1013 销毁失败 */ },
  onAppear: () => {},      // 5.0.2(14)+
  onDisappear: () => {},   // 5.0.2(14)+
});
```

- 签名：`loadProduct(context: common.UIAbilityContext, want: Want, callback?: ProductViewCallback): void`；模块起始 **4.1.0(11)**；SystemCapability.AppGalleryService.Distribution.Recommendations。
- 设备：≤6.0.1(21) 仅 Phone/Tablet/PC 2in1（其他设备返回 401）；6.0.2(22)+ 增加 TV；Car 要 26.0.1（Beta）。**不支持模拟器**（提示「无法获取内容，请点击屏幕重试」）。仅中国境内。
- 不传 callback 时失败拿不到错误码【官方】。

**路径 B：Deep Linking（store:// URI）**

```ts
const want: Want = {
  action: 'ohos.want.action.appdetail', // 官方示例原文：隐式指定 appdetail（不是 viewData！）
  uri: 'store://appgallery.huawei.com/app/detail?id=' + bundleName,
};
await context.startAbility(want); // catch 16000019 等
```

- **WPS 官网在生产环境使用的正是这个 URI 形式**（§2.1 的 harmony.wps.cn JS），双向印证。
- 网页侧等价写法：`window.open('store://appgallery.huawei.com/app/detail?id=<bundleName>')`【官方指南示例】。

**路径 C：App Linking（https）**

```ts
await context.openLink('https://appgallery.huawei.com/app/detail?id=' + bundleName,
  { appLinkingOnly: false });
```

- 网页侧：`<a href='https://appgallery.huawei.com/app/detail?id=<bundleName>'>`【官方】。
- 适合从 Web 页/二维码场景拉起；应用内优先 A。

**AppGallery 自身的 bundleName：**

| 场景 | bundleName | 证据 |
|---|---|---|
| HarmonyOS NEXT 应用市场 | `com.huawei.hmsapp.appgallery` | 【官方】store-productviewmanager 参考页 loadProduct 示例注释（"例如：bundleName: 'com.huawei.hmsapp.appgallery'"） |
| 旧 HarmonyOS/EMUI 应用市场 | `com.huawei.appmarket` | 【官方】WPS 官网 hiapplink 回退串 `hiapplink://com.huawei.appmarket?appId=C121553&callType=AGDAPI`（Web 回退路径，NEXT 上是否仍有效**未验证**） |

**【未验证】**：`market://details?id=` 是 Android 惯例，**华为 NEXT 文档未出现**，不要使用。`hiapplink://` scheme 的 NEXT 可用性未验证（WPS 把它作为 store:// 失败后的 Web 回退，且带的是旧包名）。

### 3.2 更新流程（公开查询 API + 市场弹框，无静默自更新）

来源：华为官方指南「应用市场更新功能」（https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/store-update ）+ API 参考 store-updatemanager（已全文核验）。

```ts
import { updateManager } from '@kit.AppGalleryKit';
import { common } from '@kit.AbilityKit';
import { BusinessError } from '@kit.BasicServicesKit';

const context = this.getUIContext().getHostContext() as common.UIAbilityContext;
try {
  const result: updateManager.CheckUpdateResult = await updateManager.checkAppUpdate(context);
  if (result.updateAvailable === updateManager.UpdateAvailableCode.LATER_VERSION_EXIST) { // 1
    const code: updateManager.ShowUpdateResultCode = await updateManager.showUpdateDialog(context);
    // SHOW_DIALOG_SUCCESS = 0；SHOW_DIALOG_FAILURE = 1
  }
  // LATER_VERSION_NOT_EXIST = 0
} catch (err) {
  const e = err as BusinessError;
  // 401 参数错误；1009400001 SA 连接错误；1009400002 请求服务错误；
  // 1009400003 网络错误；1009400004 应用不在前台；1009400005 未同意隐私协议；
  // 1009400006 受限（频控）；1009400007 其他
}
```

| 项 | 值 |
|---|---|
| 模块 | `import { updateManager } from '@kit.AppGalleryKit'`（**不是 @kit.StoreKit**） |
| 起始版本 | **5.0.0(12)**（API 24/26 均可用） |
| SystemCapability | `SystemCapability.AppGalleryService.Distribution.Update` |
| checkAppUpdate | `(context: common.UIAbilityContext): Promise<CheckUpdateResult>`；`updateAvailable: UpdateAvailableCode`；`versionName`/`versionCode` 字段 6.0.0(20)+ 且**仅元服务场景返回值，应用场景返回 undefined** |
| showUpdateDialog | `(context: common.UIAbilityContext): Promise<ShowUpdateResultCode>`；弹框文案**不可自定义**（官方 FAQ 标题即「向用户显示应用更新提示弹框文案能否自定义？」） |
| 元服务专用 | `on('updateChange')/off`（20+，监听更新/下载完成，限 6 次/天、1 次/30 分钟）——InkCanvas 是应用不是元服务，不适用 |
| 设备 | Phone/Tablet/PC 2in1 起始；TV 5.1.1(19)+；Wearable 6.0.0(20)+；Car 26.0.1+（Beta）。其他设备 checkAppUpdate 固定返回 LATER_VERSION_NOT_EXIST、showUpdateDialog 固定 SHOW_DIALOG_FAILURE |
| 硬约束 | 应用**已在应用市场上架**；本地版本**低于**在架版本；本地与在架**签名一致**；暂不支持邀请测试/公开测试；**不支持模拟器**；仅中国境内；**必须前台调用**（1009400004） |

**是否有「编程式查询/更新」的公开 API？**
- 查询：**有**（checkAppUpdate，上表）。
- 更新执行：**没有静默下载/安装 API**。showUpdateDialog 之后由应用市场接管下载安装；普通三方应用无 INSTALL_BUNDLE 类权限（系统级），不能自下载 HAP 自安装。合规路径 = checkAppUpdate + showUpdateDialog，或 §3.1 打开详情页让用户手动更新。Windows 侧 `UpdateService`/`ReleaseFeed` 自更新逻辑整体作废（KB 13 §0.9 同结论）。

---

## 4. API 24 vs 26 差异（本主题相关）

| 能力 | API 24（6.1.1） | API 26（26.0.0 Release） | 备注 |
|---|---|---|---|
| openLink / OpenLinkOptions | ✅ 12+ | ✅ | `hideFailureTipDialog`、`completionHandler` 均 21+，两侧都有 |
| bundleManager.canOpenLink | ✅ 12+ | ✅ | querySchemes 上限 200（21+），两侧相同 |
| 文件打开 Want + flags 授权 | ✅ | ✅ | UTD/MIME/flags 无版本差 |
| fileShare.persistPermission 等 | ✅ 11+/12+ | ✅ | CREATE/DELETE/RENAME_MODE 20+；媒体 URI 持久化 22+（两侧都有） |
| productViewManager.loadProduct | ✅ 11+ | ✅ | TV 支持 22+（两侧都有）；Car 26.0.1（**Beta，不在基线**） |
| updateManager.checkAppUpdate/showUpdateDialog | ✅ 12+ | ✅ | Wearable 20+、TV 19+（两侧都有）；Car 26.0.1（Beta） |
| bundleManager.getLaunchWantForBundleSync | ✅ **24+ 新增** | ✅ | API 23 及以下无此接口；但三方因权限不可用，差异无实际意义 |
| bundleManager.getInstalledBundleList | ❌ | ✅ 26.0.0 新增 | 仅企业权限 ENTERPRISE_GET_INSTALLED_BUNDLE_LIST，普通三方两侧都不可用 |
| bundleManager.getApplicationLabel | ❌ | ✅ 26.0.0 新增 | 查他包标签，同样受权限约束（未展开核验） |
| WPS 两个 bundleName | 【未验证】 | 【未验证】 | 与 OS 版本无关，取决于 WPS 安装版本；需实机确认 |

**结论：本主题不需要为 API 24/26 写分支逻辑**；唯一要避开的是 26.0.1 Beta 才有的 Car 设备扩展（与本项目无关）。

---

## 5. 对 InkCanvas 的工程建议（推断，待实机验证后落地）

1. **课件主路径不变**：PC 导出 PDF → PDF Kit 渲染 + 自绘墨迹（KB 13）。WPS 跳转是「用外部应用查看/编辑原始 PPT」的辅助出口，不是批注载体。
2. **`isSupportWPS` 语义重定义**：从「COM 互操作开关」改为「允许跳转外部办公应用」。Settings.json 键名保持不变（跨架构契约），两侧文案同步更新。
3. **跳转实现放 `harmony/platform` compat 层**：封装 `openInOfficeApp(filePath, utd)`：fileUri → want(viewData, flags=READ) → 不指定 bundleName 走系统选择（最稳健）；用户明确选 WPS 时再按设备类型试 `cn.wps.office.hap`（pad/PC）/`cn.wps.mobileoffice.hap`（phone）。catch 16000019 → 引导安装（loadProduct 到对应 WPS 详情页）。
4. **更新检查放 App 启动后前台时机**：checkAppUpdate → 有新版则 showUpdateDialog；失败静默降级（网络/频控/未上架都会报错，不能阻塞启动）。模拟器调试时该功能必然失败，属预期。
5. **放映控制、PPT 原生批注回写：标记不可用**，不写任何 `wps://` 猜测代码。
6. **实机验证清单**（需设备/模拟器 + 已安装 WPS）：
   - `bm dump -n cn.wps.mobileoffice.hap` / `cn.wps.office.hap` 确认包名与 abilityName、skills（file/UTD/linkFeature）；
   - §2.2 Want 打开 .pptx/.ppt，验证只读授权生效、WPS 是否另存；
   - .ppsx 是否自动放映；
   - 卸载 WPS 后 catch 16000019 路径；
   - loadProduct / store:// appdetail / https appLinking 三路径在平板上的实际表现；
   - checkAppUpdate 在已上架/未上架两种状态下的返回。

---

## 6. 未验证/风险清单（诚实记录）

| # | 事项 | 状态 |
|---|---|---|
| 1 | WPS 两个 bundleName 的设备覆盖（phone/pad/PC 边界）、当前在架版本 | 官方站点 JS 证据确凿，但**未实机 bm dump 确认** |
| 2 | WPS 对 viewData Want 的实际响应（打开/编辑/另存行为） | 未验证 |
| 3 | 放映意图 | **无公开文档，按不存在处理**；.ppsx 自动放映为推断 |
| 4 | GET_BUNDLE_INFO_PRIVILEGED 的 ACL 审批口径 | 未验证（结论「三方不可用」基于开放权限列表排除法） |
| 5 | hiapplink:// 与 com.huawei.appmarket 在 NEXT 的有效性 | 未验证（WPS Web 回退路径，可能仅旧 HarmonyOS 有效） |
| 6 | loadProduct/updateManager 真机表现 | 未验证（模拟器不支持，必须真机 + 已上架应用） |
| 7 | WPS 开放平台是否有 NEXT 集成文档 | open.wps.cn 文档为 SPA，公开可抓取的集合（guide/wps365）中未见 NEXT 拉起章节；**不排除有未公开/需登录的文档** |
| 8 | 本机 SDK 26.0.1 Beta1 与生产 26.0.0 Release 的工具链差异 | 沿用 CLAUDE.md 既有记录，上架须换 Release 工具链 |

---

## 7. 来源清单（全部于 2026-11-18 实际抓取核验）

**华为官方文档（developer.huawei.com，Googlebot SSR 全文抓取）：**
1. UIAbilityContext（openLink 12+、错误码、无 canOpenLink）：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/js-apis-inner-application-uiabilitycontext-V5
2. bundleManager（canOpenLink 12+、getLaunchWantForBundleSync 24+、getBundleInfo 权限、getInstalledBundleList 26）：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/js-apis-bundlemanager-V5
3. 拉起文件处理类应用（viewData/uri/type/flags/parameters 全表）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/file-processing-apps-startup
4. 应用文件分享（URI 规范、可分享目录）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/share-app-file
5. Deep Linking 应用间跳转（自定义 scheme、openLink appLinkingOnly=false）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/deep-linking-startup
6. AppGallery Kit 简介（能力矩阵、设备/地区/模拟器约束）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/store-introduction
7. 展示应用详情页面（loadProduct / store://appdetail / https App Linking 三路径）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/appgallery-productview-loadproduct/
8. productViewManager API 参考（11+、回调码、设备差异、示例含 com.huawei.hmsapp.appgallery）：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/store-productviewmanager
9. 应用市场更新功能指南（checkAppUpdate/showUpdateDialog、约束）：https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/store-update
10. updateManager API 参考（12+、枚举、错误码、元服务 on/off）：https://developer.huawei.com/consumer/cn/doc/harmonyos-references/store-updatemanager

**OpenHarmony 上游文档（gitee openharmony/docs master，API 逐条核验）：**
11. js-apis-inner-application-uiAbilityContext.md（openLink 12+ 原文）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-ability-kit/js-apis-inner-application-uiAbilityContext.md
12. js-apis-app-ability-openLinkOptions.md（appLinkingOnly/parameters/hideFailureTipDialog 21+/completionHandler 21+）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-ability-kit/js-apis-app-ability-openLinkOptions.md
13. js-apis-bundleManager.md（canOpenLink/querySchemes、getLaunchWantForBundleSync 权限原文）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-ability-kit/js-apis-bundleManager.md
14. js-apis-app-ability-wantConstant.md（FLAG_AUTH_READ/WRITE_URI_PERMISSION 值与「临时授予」语义）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-ability-kit/js-apis-app-ability-wantConstant.md
15. explicit-implicit-want-mappings.md（显式需 abilityName；隐式 abilityName 留空、bundleName 限定包）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/application-models/explicit-implicit-want-mappings.md
16. file-processing-apps-startup.md / share-app-file.md（与华为侧一致）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/application-models/file-processing-apps-startup.md 、https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/file-management/share-app-file.md
17. js-apis-fileShare.md（persistPermission 等是持久化「已获得的」权限，FILE_ACCESS_PERSIST）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-core-file-kit/js-apis-fileShare.md
18. js-apis-file-fileuri.md（getUriFromPath）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-core-file-kit/js-apis-file-fileuri.md
19. uniform-data-type-list.md（ppt/pptx/ppsx/dps UTD 与 MIME）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/database/uniform-data-type-list.md
20. module-configuration-file.md（querySchemes：entry 限定、128 字节、50/200 上限）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/quick-start/module-configuration-file.md
21. permissions-for-all.md（开放权限仅含 GET_BUNDLE_INFO normal 级，无 GET_BUNDLE_INFO_PRIVILEGED）：https://gitee.com/openharmony/docs/blob/master/zh-cn/application-dev/security/AccessToken/permissions-for-all.md

**WPS 官方：**
22. WPS 鸿蒙官网（bundleName 权威证据，jumpToAppStore 原文）：https://harmony.wps.cn/ （JS：https://volcengine-kdocs-cache.wpscdn.cn/kdocs/js/harmony.761d27de.js ）
23. WPS 官方论坛「请提供下鸿蒙WPS的HAP安装包」（官方答复指向 harmony.wps.cn）：https://bbs.wps.cn/topic/56423

**三方旁证（质量已标注）：**
24. 32r.com WPS 鸿蒙版下载页（v26.11.0，包名 cn.wps.mobileoffice.hap，与官方一致）：https://www.32r.com/app/76297.html
25. itying 论坛帖（Android 概念误植，**不采信**，仅记录冲突）：https://bbs.itying.com/topic/68fd494adf49280042643c18
26. downxia WPS Office 页（明确 Android 7.0，佐证 cn.wps.moffice_eng 为 Android 包）：https://m.downxia.com/downinfo/366606.html

**工具说明**：本会话 WebSearch 返回生成式摘要（无真实 URL，且与官方文档矛盾，如把 action 误作 viewData、把 Kit 误作 @kit.StoreKit），**全部弃用**；结论均以 curl（Googlebot UA 触发华为文档 SSR）/ gitee API / 官方站点 JS 抓取的原文为准。抓取缓存：`D:\tmp\harmony-research\`。