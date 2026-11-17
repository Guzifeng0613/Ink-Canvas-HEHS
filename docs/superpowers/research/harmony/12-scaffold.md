# 12 — 工程脚手架与运行时基线（ArkTS / HarmonyOS）

> 日期：2026-11-18
> 仓库：`D:\InkCanvas`，分支 `feat/winui3-migration`
> 性质：**只读研究**。本轮只读知识库与既有探针工程，未新建/修改任何 ArkTS 工程，未运行 HAP 构建，未实机验证。除本文件外未改动任何文件。
> 知识库：`.claude/skills/harmony-os/`（vendored，上游 commit `4758f3c`）
> 记号：**KB** = vendored 知识库原文；**探针** = 仓库既有 `D:\tmp\harmony-probe` 的实测配置（来自 2026-11-18 探针构建，非本轮复现）；**未知** = 知识库未写明，必须查官方 API 参考或实机/真工具链验证，本文不臆造。

---

## 0. 一句话结论

API 26 工具链下，InkCanvas 可以按「**单 HAP + 5 个 HAR + 能力层**」搭出与 C# 六层同构的工程：项目级 `build-profile.json5` 只声明 `compatibleSdkVersion "6.1.1(24)"` / `targetSdkVersion "26.0.0"`，**`compileSdkVersion` 省略**（探针实测写死会被 00303313 拒绝）；`AppScope/app.json5` 声明包名与版本；`entry/src/main/module.json5` 的 `deviceTypes` 写 `["phone","tablet","2in1"]`。**ArkTS 的完整 TS 限制清单不在本知识库内**（只记载 3 条硬限制），因此纯逻辑层只能按「ArkTS∩TS 严格子集」编写；该子集可用 Node 24 的 `--experimental-transform-types` 零依赖执行（本机已实测），而 hypium 单元测试按 KB 明文**需要连接硬件设备**，知识库未记载「无设备本地单测」。版本门控只能可靠地依据 `canIUse('SystemCapability...')`（KB 只给了一个用法示例）；设计草案里用的 `deviceInfo.sdkApiVersion` **在本知识库中查无此字段**，必须核实后才能作为 `useApi26()` 的依据。

---

## 1. 来源、版本基线与证据等级

### 1.1 本轮完整阅读的 references

| 文件 | 行数 | 本文使用的内容 |
|---|---:|---|
| `dev/references/app-framework.md` | 325 | Stage 模型、UIAbility/AbilityStage 生命周期、module.json5、Want、线程模型、API 23/26 增量 |
| `dev/references/arkts.md` | 94 | ArkTS 定位、装饰器/状态管理、TaskPool/Worker/Sendable、26 增量、模拟器限制 |
| `dev/references/arkdata.md` | 748 | Preferences、RelationalStore、KV-Store、DataShare、UDMF、备份恢复 |
| `dev/references/core-file-kit.md` | 89 | 应用/用户文件、沙箱、FilePicker、压缩（API 26）、文件权限 |
| `dev/references/permissions.md` | 147 | system_grant/user_grant、APL、声明与动态申请 |
| `dev/references/testing.md` | 561 | hypium、断言、Mock、ohosTest、`aa test`、专项测试 |
| `system/references/test-kit.md` | 124 | Test Kit 组成、Driver/ON/Component、约束 |
| `templates/references/deveco-templates.md` | 141 | 工程模板、SDK/DevEco 配套表、模板文件位置 |
| `dev/references/publish.md` | 112 | 签名、Debug/Release 产物、上架 |
| `dev/references/architecture.md` | 73 | 六大领域 Kit、Stage 模型概念 |
| `dev/references/best-practices.md` | 67 | 分层/模块化、性能与质量主题（指导性） |
| `dev/references/dev-preparation.md` | 167 | 环境、工程创建、app.json5/module.json5、deviceTypes |

补充交叉阅读（同属 vendored KB，用于补齐指定文件明确指向但未展开的细节）：

- `dev/SKILL.md`（工具链版本与 API 26 Release/Beta 变更）
- 根 `SKILL.md`（版本历史、配套工具链、设备占比）
- `system/references/file-management.md`（沙箱目录常量与 `fs` 用法；`core-file-kit.md` 只给模块清单）
- `dev/references/multi-device.md`（`deviceInfo.deviceType`、断点、三层工程）
- `dev/references/ui-development.md`（ArkTS vs TS 的 3 条限制）
- `samples/references/tool-test.md`（Hypium/HaMock 示例）

### 1.2 版本基线（KB 口径）

| 版本 | API | 状态 | 发布日期 | 设备占比（KB） |
|---|---|---:|---|---|
| HarmonyOS 26.0.0 | 26 | Release，主基线 | 2026-08-29 | 4.65% |
| HarmonyOS 26.0.1 | 26.0.1 | Beta1，增量尝鲜，**不可上架** | 2026-11-10 | — |
| HarmonyOS 6.1.1 | 24 | Release，存量主力，向下兼容目标 | 2026-05-26 | **84.93%** |

KB 明确：API 26 底座为 OpenHarmony 7.0，商用版 `HarmonyOS 7.0.0.105`；**自 API 26 起版本号采用语义化 `X.Y.Z` 格式**（根 `README.md`/`SKILL.md`）。API 24 的版本写法仍是 `6.1.1(24)` 这类「SemVer + 括号 API 号」。

### 1.3 工具链配套（KB 与探针并列）

| 组件 | API 26 Release（Windows/macOS） | API 26.0.1 Beta1 | API 26 Release（鸿蒙电脑版） | 探针实际 |
|---|---|---|---|---|
| DevEco Studio | 26.0.0.821（Patch 26.0.0.865） | 26.0.1.290 | 26.0.0.201 / Beta2 26.0.0.220 | 26.0.1.290 |
| SDK | 26.0.0.105 | 26.0.1 Beta1（OH SDK 26.0.1.120） | 26.0.0 | 26.0.1 Beta1（`apiVersion 26`） |
| Hvigor | 6.26.8 → Patch 6.26.9 | 6.26.20 | 7.26.8 / 7.26.11 | 6.26.21（CLI 自带） |
| ohpm | 26.0.0.630 | 26.0.1.220 | 6.0.1 | 26.0.1.220 |
| modelVersion | 26.0.0 | 26.0.1 | 26.0.0 | 26.0.1 |
| Node.js | 24.14.1 | 24.14.1 | — | 24.14.1 |
| codelinter | — | 6.0.240 | 6.0.140 | 6.0.240 |

**注意版本口径冲突**：`dev-preparation.md` 仍写 DevEco Studio 5.0.2 / 6.1.0、API 23，这是旧文；根 `SKILL.md`、`dev/SKILL.md`、`deveco-templates.md` 已统一到 API 26/24 双基线。本报告采用后者。另一处小差异：KB 写 Beta1 配套 Hvigor 6.26.20，探针 CLI 自带 `hvigorw 6.26.21`；以实际工具链为准。

### 1.4 证据等级

- **KB 明写**：API 名、签名、字段、版本、约束。下文直接引用。
- **探针实测**：仅限 `D:\tmp\harmony-probe` 的 5 个配置文件与 `00-harness-notes.md` 的 H8/H9（`compileSdkVersion` 省略、`assembleHap` 成功）。本轮**未复现**。
- **工程约定**：HAR 目录、`file:` 依赖、`.ts` 与 `.ets` 混编等，KB 未给 schema，标注「待验证」。
- **未知**：KB 未写，禁止当成事实使用。

---

## 2. 工程脚手架精确配置

### 2.1 推荐工程布局（与 C# 六层同构）

```
harmony/
├── AppScope/
│   ├── app.json5                     # 应用级：bundleName/versionCode/versionName/icon/label
│   └── resources/base/{element,media}/…
├── entry/                            # HAP：UIAbility、窗口外壳、模式状态机、依赖装配
│   ├── src/main/
│   │   ├── module.json5              # type=entry, deviceTypes phone/tablet/2in1
│   │   ├── ets/entryability/EntryAbility.ets
│   │   ├── ets/pages/Index.ets
│   │   └── resources/base/profile/main_pages.json
│   ├── build-profile.json5           # apiType: stageMode
│   ├── hvigorfile.ts                 # hapTasks
│   └── oh-package.json5              # 依赖 @inkcanvas/*
├── core/        # HAR @inkcanvas/core      纯域模型，禁止 import @kit.*
├── platform/    # HAR @inkcanvas/platform  唯一触达系统 Kit + compat 能力层
├── ink/         # HAR @inkcanvas/ink       渲染/输入路由
├── controls/    # HAR @inkcanvas/controls  ArkUI 组件 + token
├── features/    # HAR @inkcanvas/features  画板/课件/计时/抽奖/回放/持久化
├── build-profile.json5               # 项目级 products/modules
├── oh-package.json5                  # modelVersion、dependencies、devDependencies
├── hvigorfile.ts                     # appTasks
└── hvigor/hvigor-config.json5        # modelVersion、execution/logging/cacheDir
```

分层依赖方向：`entry → features → controls → ink → platform → core`。`core` 不 import 任何 `@kit.*`，从而可脱离 SDK 用 Node 验证（见 §8）。

### 2.2 项目级 `build-profile.json5`

KB 没有给出该文件的完整 schema（`deveco-templates.md` 只把它列为“构建配置”），但探针给出了在 26 工具链下可构建的最小形态：

```json5
{
  "app": {
    "signingConfigs": [],
    "products": [
      {
        "name": "default",
        "compatibleSdkVersion": "6.1.1(24)",
        "targetSdkVersion": "26.0.0",
        "runtimeOS": "HarmonyOS"
        // 注意：不写 compileSdkVersion
      }
    ],
    "buildModeSet": [
      { "name": "debug" },
      { "name": "release" }
    ]
  },
  "modules": [
    { "name": "entry", "srcPath": "./entry",
      "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "core", "srcPath": "./core",
      "targets": [{ "name": "default", "applyToProducts": ["default"] }] }
    // platform / ink / controls / features 同形
  ]
}
```

字段语义（KB + 探针）：

| 字段 | 值 / 格式 | 依据 |
|---|---|---|
| `app.products[].name` | 产品名，构建时 `-p product=default` | 探针 |
| `compatibleSdkVersion` | `"6.1.1(24)"`；KB 另给最低 `"5.0.0(12)"`（电脑版）/`"4.0.0(10)"`（Beta） | 探针 + KB 工具链行 |
| `targetSdkVersion` | `"26.0.0"`；KB 写可用区间 `5.0.0(12)~26.0.0`（电脑版）、`4.0.0(10)~26.0.1`（Beta） | 探针 + KP |
| `compileSdkVersion` | **省略**。写 `"26.0.0"` 会被 00303313 拒绝（须与内置 SDK 26.0.1 一致） | 探针 H8 |
| `runtimeOS` | `"HarmonyOS"` | 探针 |
| `buildModeSet` | `debug` / `release` | 探针 |

**SemVer 规则**：API 26 起 `compileSdkVersion`/`targetSdkVersion` 用 `X.Y.Z`（如 `26.0.0`）；`compatibleSdkVersion` 用 `X.Y.Z(API号)`（如 `6.1.1(24)`）。KB 没有逐字段说明何时可省略 `(NN)`，按「26 用三段、24 用带括号」执行最稳妥。

**为什么省略 `compileSdkVersion` 不是“绕开”**：省略后 hvigor 使用工具链内置 SDK。当前探针是 26.0.1 Beta1（`apiVersion 26`），生产上架必须换 26.0.0 Release 工具链（KB：Beta 工具开发的应用不能上架）。省略写法让同一份工程在两种工具链下都能构建，代价是**构建产物会随工具链变化**，需要在验证声明里记录实际工具链版本。

### 2.3 `hvigorfile.ts`

项目级（探针实测）：

```typescript
import { appTasks } from '@ohos/hvigor-ohos-plugin';

export default {
  system: appTasks,
  plugins: []
}
```

HAP 模块级（探针实测）：

```typescript
import { hapTasks } from '@ohos/hvigor-ohos-plugin';

export default {
  system: hapTasks,
  plugins: []
}
```

HAR 模块级按 DevEco 常规应为 `harTasks`，但**KB 未记载 `harTasks`**，需在 26 工具链创建 HAR 模板后确认（见 §2.6）。

### 2.4 `hvigor/hvigor-config.json5` 与 `oh-package.json5`

探针实测（两者都声明 `modelVersion`）：

```json5
// hvigor/hvigor-config.json5
{
  "modelVersion": "26.0.1",
  "dependencies": {},
  "execution": { "daemon": false },
  "logging": { "level": "info" },
  "debugging": { "stacktrace": true },
  "nodeOptions": { "maxOldSpaceSize": 8192 },
  "properties": { "hvigor.cacheDir": "E:/harmony-cache/hvigor-cache" }
}
```

```json5
// oh-package.json5（项目级）
{
  "modelVersion": "26.0.1",
  "description": "InkCanvas HarmonyOS",
  "dependencies": {},
  "devDependencies": {}
}
```

- `modelVersion` 必须与工具链匹配：Release 用 `26.0.0`，Beta1 用 `26.0.1`（KB 配套表 + 探针）。切换工具链时它是**必须同步修改**的字段。
- `hvigor.cacheDir` 指到非系统盘（探针 `E:/harmony-cache/hvigor-cache`）；`HVIGOR_USER_HOME` 是 hvigor 官方支持的环境变量（探针 H11）。
- `ohpm` 没有同等 home 变量，包缓存仍在 `~/.ohpm`（探针 H11）。

### 2.5 `AppScope/app.json5`

KB（`dev-preparation.md`）与探针一致：

```json5
{
  "app": {
    "bundleName": "com.inkcanvas.app",
    "vendor": "inkcanvas",
    "versionCode": 1000000,
    "versionName": "1.0.0",
    "icon": "$media:app_icon",
    "label": "$string:app_name"
  }
}
```

| 字段 | 说明 | 依据 |
|---|---|---|
| `bundleName` | 全局唯一；AGC 创建后**不可修改** | KB `publish.md` |
| `versionCode` | 整数，示例 `1000000` | KB |
| `versionName` | 字符串 | KB |
| `icon` / `label` | 资源引用 `$media:` / `$string:` | KB |

KB 未在 `app.json5` 中提到 `minAPIVersion`/`targetAPIVersion`；版本基线只出现在 `build-profile.json5`。不要臆造。

### 2.6 HAR 模块结构与依赖声明（KB 缺口，需验证）

KB 只明确了三件事：

1. `module.json5` 的 `type` 枚举是 `entry/feature/har/hsp`（`app-framework.md`）。
2. 行业实践常见「单 HAP + 多 HAR」架构（`industry-practices.md`）。
3. 模块级依赖写在 `oh-package.json5`（`testing.md` 以 `devDependencies` 为例）。

**KB 没有给出 HAR 的完整 `module.json5`、`hvigorfile.ts`、被依赖方的 `oh-package.json5` 写法**。以下是 DevEco 常规约定，**标注为待验证**，不作为 KB 事实：

```json5
// core/src/main/module.json5（待验证）
{
  "module": {
    "name": "core",
    "type": "har",
    "description": "$string:core_desc"
  }
}
```

```typescript
// core/hvigorfile.ts（待验证：harTasks 未在 KB 出现）
import { harTasks } from '@ohos/hvigor-ohos-plugin';
export default { system: harTasks, plugins: [] }
```

```json5
// core/oh-package.json5（待验证）
{
  "name": "@inkcanvas/core",
  "version": "0.1.0",
  "description": "InkCanvas pure domain model",
  "main": "Index.ets",
  "dependencies": {}
}
```

```json5
// entry/oh-package.json5（待验证：file: 引用语法未在 KB 出现）
{
  "name": "entry",
  "version": "1.0.0",
  "dependencies": {
    "@inkcanvas/core": "file:../core",
    "@inkcanvas/platform": "file:../platform",
    "@inkcanvas/ink": "file:../ink",
    "@inkcanvas/controls": "file:../controls",
    "@inkcanvas/features": "file:../features"
  }
}
```

落地时必须用 DevEco Studio 26.0.0 新建一个 HAR 模板，以其生成的 `module.json5`/`hvigorfile.ts`/`Index.ets` 为准，再回填本报告。**在验证之前不要把这套结构写进构建脚本当既定事实。**

### 2.7 `entry/src/main/module.json5`

KB（`app-framework.md`、`dev-preparation.md`）字段 + 探针实测值：

```json5
{
  "module": {
    "name": "entry",
    "type": "entry",
    "description": "$string:module_desc",
    "mainElement": "EntryAbility",
    "deviceTypes": ["phone", "tablet", "2in1"],
    "deliveryWithInstall": true,
    "installationFree": false,
    "pages": "$profile:main_pages",
    "abilities": [
      {
        "name": "EntryAbility",
        "srcEntry": "./ets/entryability/EntryAbility.ets",
        "description": "$string:EntryAbility_desc",
        "icon": "$media:app_icon",
        "label": "$string:EntryAbility_label",
        "startWindowIcon": "$media:app_icon",
        "startWindowBackground": "$color:start_window_background",
        "exported": true,
        "launchType": "singleton",
        "skills": [
          { "entities": ["entity.system.home"], "actions": ["action.system.home"] }
        ]
      }
    ]
  }
}
```

要点：

- `deviceTypes` 的合法值（KB `dev-preparation.md`）：`phone`、`tablet`、`tv`、`wearable`、`car`、`2in1`。InkCanvas 写 `["phone","tablet","2in1"]`。
- `mainElement` 必须对应真实存在的 UIAbility（`dev/SKILL.md` 关键规则 2）。
- `launchType`：`singleton`（默认）/`multiton`/`specified`（KB `app-framework.md`）。InkCanvas 主壳用 `singleton`。
- `pages` 指向 `resources/base/profile/main_pages.json`；KB 给的格式是对象数组：
  ```json
  { "src": [ { "src": "pages/Index", "name": "Index" } ] }
  ```
  （`dev-preparation.md`；这种对象数组写法与部分模板的纯字符串数组不同，需以 26 模板为准。）
- `orientationId`：API 14+ 若在业务中自行构造 `AbilityInfo`，必须补该必填属性；仅在 `targetSdkVersion >= 5.0.2(14)` 生效（`app-framework.md`）。InkCanvas target 26，会命中。
- `requestPermissions` 按需追加（见 §6），`extensionAbilities` 按需追加（备份、DataShare 等）。

### 2.8 模块级 `build-profile.json5`

探针实测：

```json5
// entry/build-profile.json5
{
  "apiType": "stageMode",
  "buildOption": {},
  "targets": [ { "name": "default" } ]
}
```

`apiType: "stageMode"` 对应 Stage 模型；FA 模型不在本项目范围。

### 2.9 与构建/发布相关的硬事实

- Debug 产物：`build/outputs/hap/debug/entry-debug.hap`；Release：`build/outputs/app-pack/entry-app.app`；**上架必须 App Pack**（KB `publish.md`）。
- 签名：生成 `.p12` + CSR → AGC 申请 `.cer` → 申请 `.p7b` Profile → Project Structure/Signing Configs。`bundleName` 必须与 AGC 完全一致，且不可改（KB `publish.md`）。
- 26.0.1 Beta1 工具链构建的应用**不能上架**；生产必须 26.0.0 Release（KB 根 `SKILL.md`）。
- 打包工具在 API 26 新增「扫描重复 so」能力（KB `publish.md`）；InkCanvas 若引入 native 渲染库需要关注。

---

## 3. 运行时版本门控（API 24 vs 26）

### 3.1 `canIUse`：KB 只给了一个用法

`media/references/ar-engine.md` 原文：

> 系统能力：调用前需 `canIUse("SystemCapability.AREngine.Core")` 检查设备支持

可确认的事实：

- 形态是**全局函数**，参数是 `SystemCapability.<Domain>.<Feature>` 字符串，语义是“当前设备是否具备该系统能力”。
- KB 没有给出 `import` 语句、返回值类型、在 HAR 中是否可用；按全局内置使用，需在 SDK `.d.ts` 核实类型声明。
- KB 中出现的完整 syscap 字符串只有三个：`SystemCapability.AREngine.Core`、`SystemCapability.Graphic.XEngine`、`SystemCapability.PCService.EnterpriseDeployRestore`。**InkCanvas 需要的 Pen Kit / 2D 图形 / Overlay 等 syscap 字符串不在 KB 内**，必须从 SDK 的 `syscap.json` 或 API 参考查，禁止猜写。

推荐封装（能力层唯一出口）：

```typescript
// platform/src/main/ets/compat/Capabilities.ets（示意，syscap 字符串待查）
export interface Capabilities {
  hasPenFollow(): boolean;
  hasOffscreenCanvas(): boolean;
  hasOverlay(): boolean;
}

export class DeviceCapabilities implements Capabilities {
  hasPenFollow(): boolean {
    // 字符串必须来自 SDK syscap 清单，此处仅示意
    return canIUse('SystemCapability.<PenKit>.<Feature>');
  }
  hasOffscreenCanvas(): boolean {
    return canIUse('SystemCapability.<Graphic>.<Feature>');
  }
  hasOverlay(): boolean {
    return canIUse('SystemCapability.<Window>.<Feature>');
  }
}
```

### 3.2 `deviceInfo`：KB 只记载 3 个字段，`sdkApiVersion` 查无

KB `system/references/device-info.md` 只写：

```typescript
import { deviceInfo } from '@kit.BasicServicesKit';
let deviceId = deviceInfo.uuid;
let deviceType = deviceInfo.deviceType;
let osFullName = deviceInfo.osFullName;
```

`dev/references/multi-device.md` 另给旧式导入：

```typescript
import deviceInfo from '@ohos.deviceInfo'
if (deviceInfo.deviceType === 'phone') { … }
```

**重要发现**：设计草案 §6 写的 `deviceInfo.sdkApiVersion` 与 `useApi26()`，在本知识库（全文检索）**没有任何记载**。因此：

- `deviceInfo.sdkApiVersion` 是否存在、返回的是 API Level（24/26）还是 SDK 版本串，**未知，必须查官方 `@ohos.deviceInfo` API 参考或在真机打印验证**。
- `deviceInfo.osFullName` 是“系统版本”（如 HarmonyOS 版本串），**不能直接当数字 API Level 比较**。
- 在核实前，`useApi26()` 不应作为唯一门控；应优先用 `canIUse` 做能力探测，再以 `deviceType` 做布局分流（布局不是能力门控）。

推荐的判定优先级：

1. **能力探测**（`canIUse`）——决定“这个 API 能不能调”。
2. **API 级别判断**（`deviceInfo.sdkApiVersion`，待核实）——只在没有 syscap 的纯版本行为差异上使用。
3. **设备类型**（`deviceInfo.deviceType` / 断点）——只决定布局与交互形态，不决定能力有无。

### 3.3 由 `targetSdkVersion` 触发的行为变更（编译期门控，不是运行时）

KB 明写的目标版本门控：

| 行为 | 触发条件 | 影响 |
|---|---|---|
| `AbilityInfo.orientationId` 必填 | `targetSdkVersion >= 5.0.2(14)` | 自行构造 `AbilityInfo` 时必须补 `orientationId` |
| blob 空数组行为 | `targetSdkVersion >= 5.0.2(14)` | 空 `Uint8Array` 插入 RDB 后返回空数组而非 null |
| 默认浏览器权限管控 | `targetSdkVersion >= 26.0.0` | 需 `ohos.permission.DEFAULT_WEB_BROWSER`，浏览器类应用才相关 |
| Agent Framework Kit 接口变更 | `targetSdkVersion >= 26.0.0` | `OnDataCallback.method` 由枚举变 string；`RequestContext.getClientSessionId` 删除（InkCanvas 不涉及） |

InkCanvas target 26.0.0，因此前两条会命中；后两条与业务无关。

### 3.4 API 24 / 26 能力差异（与脚手架相关的部分）

| 能力 | API 24（6.1.1） | API 26（26.0.0） | 门控方式 |
|---|---|---|---|
| `compileSdkVersion` 写法 | 可用 `(NN)` 形式 | 自 26 起 SemVer `X.Y.Z` | 构建期 |
| Core File Kit 压缩/解压 | **无**（KB 标注 API 26 新增） | 有；模块名/签名 KB 未给 | `canIUse` + 版本；本项目建议自实现或放弃 |
| 容器对象跨线程拷贝传递 | 无 | 有（ArkTS Release 新增） | 版本/能力 |
| `appMemoryOptimizer` | 无 | 有（API 26 Release） | 版本 |
| `pluginBundleManager`、`ModularObjectExtensionAbility` | 无 | 有（API 26） | 版本 |
| `JSON.parseSendable`、Map `put/putAll/remove/containsValue` | 无 | **仅 26.0.1 Beta1**，不可上架 | 不使用 |
| ArkUI V2 状态组件若干、`preloadPath`、动画 C API 等 | 无 | 有（API 26） | 不用则无关 |
| Pen Kit 跟手性加速 | 无 | 有（API 26 Release 新增，KB `dev/SKILL.md`） | `canIUse` |
| `LaunchParam.launchUTCTime/launchUptime`、`hnpPackages` | 有（API 23+） | 有 | 可直接用 |
| Preferences `flushSync` | 有（API 14+） | 有 | 可直接用 |
| RDB `cryptoParam` | 有（API 14+） | 有 | 可直接用 |
| `sendableRelationalStore` | 有（API 12+） | 有 | 可直接用 |
| HSP 声明多 UIAbility | 有（API 14+） | 有 | 可直接用 |

> 表中“无”只表示 KB 把它标为 API 26 新增；API 24 是否真的没有该符号，仍需以 24 SDK 的 `.d.ts` 为准。

### 3.5 门控落地的硬约束

ArkTS 是静态编译：**即使把 26 专属 API 包在 `if (canIUse(...))` 里，符号也必须在编译期存在**。因此：

- 用 26 SDK 编译时，26 专属符号能通过编译；运行在 24 设备上若不进分支则安全，进分支前必须 `canIUse` 为真。
- `canIUse` 只回答“设备有没有这个 syscap”，不回答“这个函数在 24 上语义是否相同”。后者需要 `api-matrix.md` 逐项登记。
- 业务层禁止直接 `import` 26 专属 Kit；全部经 `platform/compat` 的接口暴露（与设计草案 §6 一致）。

---

## 4. ArkData 数据持久化

### 4.1 Preferences（用户首选项）

适用：`Settings.json` 这类键值配置。KB 明确签名与约束：

```typescript
import { preferences } from '@kit.ArkData'

let options: preferences.Options = { name: 'settings' }
let p = preferences.getPreferencesSync(this.context, options)   // 同步获取
// 或 preferences.getPreferences(context, options).then(...)

p.putSync('key', 'value')
let v = p.getSync('key', 'default')
p.deleteSync('key')
p.flushSync()                    // API 14+ 同步落盘
p.on('change', (key: string) => { … })
p.off('change', observer)
preferences.deletePreferences(this.context, options, cb)
```

约束（KB）：

- Key 为 string，非空且 **≤1024 字节**。
- Value 为 string 时 **≤16MB（UTF-8）**；非标准 UTF-8 用 `Uint8Array` 存取。
- **不支持多进程并发安全**。
- 建议 **≤10000 条**，否则内存开销大。
- `deletePreferences` 后订阅自动取消、实例不可再用、数据不可恢复。

对 InkCanvas：设置项（笔色/笔宽/模式/主题/快捷键/窗口记忆）适合 Preferences；但 `.icink` 墨迹存档可能超过 16MB，**不要塞进 Preferences**，用沙箱文件（§5）。

### 4.2 RelationalStore（关系型数据库，SQLite）

适用：需要索引/查询/事务的元数据（回放索引、课件页标注索引、抽奖名单历史）。KB 关键点：

```typescript
const STORE_CONFIG: relationalStore.StoreConfig = {
  name: 'inkcanvas.db',
  securityLevel: relationalStore.SecurityLevel.S3,
  encrypt: false,
  isReadOnly: false
  // allowRebuild: true, cryptoParam: { iteration: 10000, keySize: 32 }  // 可选
}
const store = await relationalStore.getRdbStore(context, STORE_CONFIG)
if (store.version === 0) { store.executeSql(CREATE_SQL); store.version = 1 }
```

- 约束：默认 WAL + FULL 落盘；**4 读连接 + 1 写连接**；同一时间仅一个写操作；ArkTS 侧支持 number/string/二进制/boolean/bigint；**单条建议 ≤2MB**；**单次查询建议 ≤5000 条**，大数据量放 TaskPool。
- 安全级别 `S1`~`S4`；`S3` 适合“泄露影响较大”的课堂数据。
- `cryptoParam`（API 14+）仅在 `encrypt: true` 时生效。
- `allowRebuild: true`：错误码 `14800011` 时自动删库重建；重建为空库，需备份恢复。
- 备份/恢复：`backup('Backup.db', cb)` / `restore('Backup.db', cb)`。
- `sendableRelationalStore`（API 12+）：`toSendableValuesBucket` / `fromSendableValuesBucket` 支持跨线程。
- 版本升级用 `store.version` 阶梯判断 + `ALTER TABLE`（KB 示例）。

### 4.3 KV-Store / 分布式

```typescript
const kvManager = distributedKVStore.createKVManager({
  context: getContext(this), bundleName: 'com.inkcanvas.app'
})
const store = await kvManager.getKVStore('storeId', {
  createIfMissing: true, encrypt: true, backup: false, autoSync: false,
  kvStoreType: distributedKVStore.KVStoreType.SINGLE_VERSION,
  securityLevel: distributedKVStore.SecurityLevel.S1
})
```

适用：跨设备同步的简单键值（课堂多端同步设置）。KB 未说明分布式同步需要的权限与账号条件，**未知/需核实**。InkCanvas 首版不建议依赖。

### 4.4 DataShare / UDMF（了解即可）

- DataShare：Provider 用 `DataShareExtensionAbility` + `module.json5` 的 `extensionAbilities`（`type: "dataShare"`、`metadata` 指向 `$profile:data_share_config`）；Consumer 用 `dataShare.createDataShareHelper`。跨应用共享用，InkCanvas 首版不需要。
- UDMF：`uniformTypeDescriptor`（UTD）+ `unifiedDataChannel`；`ContentForm` API 14+。用于跨应用拖拽/分享，可在“导出图片/墨迹”时用到。

### 4.5 与 InkCanvas 数据格式的映射建议

| 数据 | 存储 | 理由 |
|---|---|---|
| `Settings.json` | Preferences（`name: 'settings'`） | 键值、小、需快速读 |
| `.icink` 墨迹存档 | 沙箱 `filesDir` 下的 JSON 文件（`fs`） | 可能 >16MB；需与 C# 同 schema；便于导入导出 |
| 课件页/回放索引/名单 | RelationalStore（可选） | 需要查询/排序/事务时 |
| 跨设备同步 | 首版不做；后续 KV-Store | KB 约束不足 |

`.icink`/`Settings.json` 的字段名两侧必须完全一致（CLAUDE.md 契约）；Preferences 的 key 与 `Settings.json` 的 key 可以同名，减少映射层。

### 4.6 API 24/26 差异

- 上述 Preferences/RDB/KV 的核心 API 在 API 24 均可用（KB 标注起始版本 ≤14/12）。
- 26 新增的 ArkData 能力（数据共享多值类型配置）与 InkCanvas 首版无关。
- `sendableRelationalStore` 的 `JSON.parseSendable` 只在 26.0.1 Beta1，**不得使用**。

---

## 5. Core File Kit、沙箱与文件访问

### 5.1 沙箱目录（KB `system/references/file-management.md`）

```
/data/storage/el2/base/
├── files/             # 持久化（应用卸载清除）
├── cache/             # 缓存（空间不足可能清理）
├── temp/              # 临时（退出可能清理）
├── preferences/       # 偏好
├── distributedFiles/  # 分布式
├── database/          # 数据库
└── bundleCodeDir      # 安装包资源
```

获取方式：

```typescript
export default class EntryAbility extends UIAbility {
  onCreate() {
    this.context.filesDir
    this.context.cacheDir
    this.context.tempDir
    this.context.preferencesDir
    this.context.databaseDir
    this.context.distributedFilesDir
    this.context.bundleCodeDir
  }
}
// UI 中：
private context = getContext(this) as common.UIAbilityContext;
```

### 5.2 文件读写 API（`@kit.CoreFileKit`）

```typescript
import { fileIo as fs } from '@kit.CoreFileKit';
let path = this.context.filesDir + '/board.icink';
let file = fs.openSync(path, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE);
fs.writeSync(file.fd, jsonText);
fs.closeSync(file);

fs.statSync(path).size
fs.accessSync(path)
fs.mkdirSync(dir); fs.rmdirSync(dir)
fs.copyFileSync(src, dst); fs.moveFileSync(src, dst)
fs.unlinkSync(path)
fs.listFileSync(this.context.filesDir)
```

异步版本：`await fs.open(...)` / `await fs.write(...)` / `await fs.close(...)`。KB 建议耗时 I/O 用异步，避免阻塞 UI 线程。

### 5.3 FilePicker（无需权限）

KB 明确：「使用 FilePicker 选择/保存文件无需配置权限」。`picker.PhotoViewPicker`、`DocumentViewPicker`、`AudioViewPicker` 属于 `ohos.file.picker`。InkCanvas 的 `.icink` 导入/导出、图片插入、课件 PDF 选择应走 FilePicker，而不是申请存储权限。

### 5.4 压缩/解压（API 26 新增，细节未知）

KB `core-file-kit.md` 只写：

> 压缩解压缩（API 26 新增）：为应用提供数据压缩和解压缩能力；适用文件打包分发、减少存储占用、加速网络传输；指南 `archive-overview`。

**没有模块名、函数签名、支持的格式、是否流式**。因此：

- API 24 无此能力（按 KB 表述）。
- 若 InkCanvas 需要导入/导出 `.icink` 压缩包，首版建议：单文件 JSON（不压缩）或自行实现 ZIP（纯逻辑、可 Node 验证），不要把 API 26 压缩当作既定依赖。
- 模块名/签名必须在 API 26 SDK 的 `@kit.CoreFileKit` `.d.ts` 中查证后再定。

### 5.5 文件相关权限

KB `core-file-kit.md` 列出：

| 权限 | 说明 |
|---|---|
| `ohos.permission.READ_WRITE_DOWNLOAD_DIRECTORY` | 读写下载目录 |
| `ohos.permission.READ_WRITE_DESKTOP_DIRECTORY` | 读写桌面目录 |
| `ohos.permission.READ_WRITE_DOCUMENTS_DIRECTORY` | 读写文档目录 |
| `ohos.permission.FILE_ACCESS_PERSIST` | 文件访问持久化 |

InkCanvas 只用应用沙箱 + FilePicker，**上述权限都不需要**。不要为了“方便”申请目录权限，会触发上架隐私审核。

### 5.6 备份恢复

`BackupExtensionAbility` + `module.json5` 的 `extensionAbilities`（`type: "backup"`、`metadata` 指向 `$profile:backup_config`），`onBackup()` / `onRestore(bundleVersion)`。InkCanvas 若要保留用户墨迹/设置，建议接入；KB 未给 `backup_config` 的完整 schema，需查模板。

---

## 6. 权限模型

### 6.1 分类与 APL

| 维度 | 取值 | 说明 |
|---|---|---|
| 授权方式 | `system_grant` | 安装时自动授予（INTERNET、GET_NETWORK_INFO、GET_WIFI_INFO、传感器等） |
|  | `user_grant` | 运行时动态申请（LOCATION、CAMERA、MICROPHONE、通讯录、日历、媒体读取） |
| APL | `normal` | 三方可直接申请 |
|  | `system_basic` | 需申请签名证书 |
|  | `system_core` | **仅系统应用** |

### 6.2 声明（`module.json5`）

```json5
{
  "module": {
    "requestPermissions": [
      {
        "name": "ohos.permission.CAMERA",
        "reason": "$string:camera_reason",
        "usedScene": { "abilities": ["EntryAbility"], "when": "inuse" }
      }
    ]
  }
}
```

- `reason` **必须**用 `$string:` 引用资源，且多语种；上架审核会展示给用户（KB `permissions.md`）。
- `when`：`always` / `inuse`。

### 6.3 动态申请（user_grant）

```typescript
import abilityAccessCtrl from '@ohos.abilityAccessCtrl'
import bundleManager from '@ohos.bundle.bundleManager'
import common from '@ohos.app.ability.common'

const atManager = abilityAccessCtrl.createAtManager()
const bundleInfo = bundleManager.getBundleInfoForSelfSync(
  bundleManager.BundleFlag.GET_BUNDLE_INFO_WITH_APPLICATION
)
const status = atManager.checkGrantAskPermission(
  bundleInfo.appInfo.accessTokenId, 'ohos.permission.CAMERA'
)
// status === abilityAccessCtrl.GrantStatus.PERMISSION_GRANTED

const ctx = getContext(this) as common.UIAbilityContext
const result = await atManager.requestPermissionsOnUser(ctx, ['ohos.permission.CAMERA'])
result.authResults[i] === abilityAccessCtrl.GrantStatus.PERMISSION_GRANTED
```

> KB 使用的 `checkGrantAskPermission` / `requestPermissionsOnUser` 是文档原文；不同 API 版本可能还有 `checkAccessToken`/`requestPermissionsFromUser` 等别名，需以 SDK `.d.ts` 为准，不要混用。

### 6.4 InkCanvas 权限清单（建议）

| 能力 | 权限 | 结论 |
|---|---|---|
| 应用沙箱读写 `.icink`/设置 | 无 | 不需要权限 |
| FilePicker 导入导出 | 无 | KB 明文无需权限 |
| 截图/插入图片 | 走 FilePicker 或 `PhotoViewPicker` | 无 |
| 网络（更新检查/帮助页） | `ohos.permission.INTERNET`（system_grant） | 按需 |
| 相机（拍课件） | `ohos.permission.CAMERA`（user_grant） | 若做才申请 |
| 麦克风（回放录音） | `ohos.permission.MICROPHONE`（user_grant） | 首版不建议 |
| 读写下载/桌面/文档目录 | 上述目录权限 | **不需要**，用 Picker |

### 6.5 安全编码（KB）

- 禁止硬编码密钥；网络用 HTTPS；外部输入校验；生产日志脱敏。
- 加解密用 `@ohos.security.cryptoFramework`。
- Release 编译自动混淆。

---

## 7. 测试：有设备与无设备

### 7.1 hypium 单元测试

KB：arkxtest = JsUnit + UiTest，ohpm 包 `@ohos/hypium`，示例版本 **1.0.25**。

```json5
// 模块级 oh-package.json5
{ "devDependencies": { "@ohos/hypium": "1.0.25" } }
```

目录：

```
entry/src/ohosTest/ets/
├── test/ability.test.ets
├── testrunner/OpenHarmonyTestRunner.ets
└── module.json5
```

核心 API：`describe` / `it` / `expect` / `beforeAll` / `beforeEach` / `afterEach` / `afterAll` / `xit` / `xdescribe`。断言：`assertEqual`、`assertNotEqual`、`assertTrue/False`、`assertNull/NotNull`、`assertUndefined/Defined`、`assertContain`、`assertThrowError`、`assertClose(value, margin)`、`assertDeepEquals`。用例级别：`TestType.FUNCTION|PERFORMANCE|POWER`、`Level.LEVEL0~4`、`Size.SMALLTEST|MEDIUMTEST|LARGETEST`。

测试模块 `module.json5`（KB 示例）：

```json5
{
  "module": {
    "name": "entry_test",
    "type": "feature",
    "deviceTypes": ["phone", "tablet"],
    "deliveryWithInstall": true,
    "installationFree": false
  }
}
```

### 7.2 执行方式

- DevEco Studio：连接设备 → 右键测试文件/测试套/用例 → Run Tests。
- 命令行：
  ```bash
  hdc shell aa test -b com.inkcanvas.app -m entry -s unittest OpenHarmonyTestRunner
  hdc shell aa test -b com.inkcanvas.app -m entry -s unittest OpenHarmonyTestRunner -s class CalculatorTest
  hdc shell aa test -b com.inkcanvas.app -m entry -s unittest OpenHarmonyTestRunner -s class CalculatorTest#case_name
  ```
  参数：`-s timeout`、`-s breakOnError`、`-s random`、`-s testType`、`-s level`、`-s size`、`-s stress`。
- 结果日志 `OHOS_REPORT_STATUS_CODE`（1 开始 / 0 通过 / -1 报错 / -2 失败）与 `OHOS_REPORT_RESULT`。

### 7.3 “无设备本地单元测试”在本 KB 中不存在

- `system/references/test-kit.md` 明确：「**测试脚本需连接硬件设备执行**」「UI 测试框架不支持并发调用」「命令行执行需 `hdc shell aa test`」。
- `dev/references/testing.md` 也只描述 `ohosTest`（仪器化）路径，没有 “Local Test / 主机侧 JsUnit” 章节。
- 结论：**KB 未记载无需设备的本地 hypium 单测**。DevEco Studio 某些版本有 Local Test，但本 KB 未覆盖，属于**未知/需在 DevEco 26 中验证**。
- 另有一条硬限制：`arkts.md` 写「ArkTS 基础库与 ArkTS 并发暂不支持模拟器」——**模拟器不能替代真机做 ArkTS 逻辑/并发验证**。

### 7.4 本机可行的无设备路线：Node 黄金向量

这是本项目既有且已实测的路线（`00-harness-notes.md` H1/H2/H3，本轮复核 Node 版本）：

- 本机 Node **v24.21.0**。
- 默认类型剥离**不支持 `enum`**；加 `--experimental-transform-types` 后，`interface/enum/class/泛型` 均可执行（本轮再次实测：`enum E{A,B}; interface I…` 输出 `1 2`，仅有 ExperimentalWarning）。
- 运行器零依赖：`node --experimental-transform-types parity/run-vectors.mjs`。

限制与建议：

1. 纯逻辑文件必须写成 **ArkTS∩TS 严格子集**（见 §8），且**不 import 任何 `@kit.*`**。
2. 不使用装饰器（Node 的类型剥离/transform-types 不转换装饰器；ArkUI 装饰器也不属于纯逻辑）。
3. 不使用 `namespace`、`const enum`、`import =` 等 KB 未确认 ArkTS 支持的 TS 语法；这些在 Node 侧也可能行为不同。
4. `.ets` 与 `.ts` 混编是否被 hvigor 接受，KB 未记载；建议纯逻辑文件用 `.ts` 后缀放在 HAR 的 `src/main/ets/` 下，`Index.ets` 再导出，**待 26 工具链构建验证**。
5. Node 侧只验证“算法/数据契约”，不验证 ArkUI、权限、文件系统、线程。

### 7.5 分层测试策略（与设计草案 L1~L4 对齐）

| 层 | 手段 | 本机可行性 | 证据 |
|---|---|---|---|
| L1 逻辑互认 | C# 测试 + Node 向量共同消费 `parity/vectors/*.json` | **可执行** | Node H2 实测 |
| L2 编译/静态 | `hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon` + `codelinter` | 工具链就位后可执行 | 探针 H9 |
| L3 运行/UI | DevEco 预览器/模拟器/真机 | 本机无设备 → **未验证** | KB 要求真机 |
| L4 基线回归 | `.\build.ps1 test`（WinUI3） | 可执行 | 项目脚本 |

---

## 8. ArkTS 相对 TypeScript 的硬限制

### 8.1 KB 明写的限制（只有 3 条）

`dev/references/ui-development.md`：

1. **强制使用静态类型**：必须声明变量类型。
2. **禁止 structural typing**：不支持对象展开等操作。
3. **禁止 any 类型隐式转换**。

`dev/references/arkts.md` 补充的定位：

- ArkTS 基于 TS 生态，但“通过规范定义强化开发期静态检查和分析”。
- 提供声明式 UI、状态管理、渲染控制、基础/容器类库、TaskPool/Worker/Sendable、TS/JS 互操作。
- 官方迁移规则链接：`https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/typescript-to-arkts-migration-guide`。

**KB 没有给出完整的“ArkTS 禁用 TS 特性”清单**（例如 `any/unknown`、索引签名、动态属性、`Function.prototype`、`eval`、`with`、原型改写、结构化类型、对象展开、解构限制、可选链限制等的逐条状态）。任何更细的清单都必须查上述官方迁移指南，**不能从本 KB 推断**。

### 8.2 状态管理：KB 只详细记载 V1

`arkts.md` 与 `ui-development.md` 列出的装饰器均为 V1：`@State`、`@Prop`、`@Link`、`@Provide`、`@Consume`、`@Watch`、`@Builder`、`@Extend`、`@Styles`、`@ObjectLink`、`@StorageProp`、`@StorageLink`、`@Entry`、`@Component`、`@Preview`。

V2（`@ComponentV2`、`@Local`、`@Param`、`@Event`、`@ObservedV2`、`@Trace` 等）在 KB 中**没有 API 级说明**，只在 API 26 变更里出现“V2 状态管理组件（ChipV2/CounterV2/…）”。因此：

- 脚手架阶段用 V1 装饰器最稳（KB 有完整表）。
- 若要用 V2，需查官方“状态管理 V2”文档，不能按 V1 经验类推。

### 8.3 Sendable 与并发

KB 只给概念：

- `TaskPool`：多线程任务池，支持调度/取消/优先级；`@Concurrent` 函数 + `taskpool.execute(fn, args)`。
- `Worker`：`new worker.ThreadWorker('entry/ets/workers/MyWorker.ts')` + `postMessage`/`onmessage`。
- `Sendable`：对象在并发实例间**引用传递**。
- API 26：容器类对象跨线程可**拷贝（序列化）**传递。
- API 26.0.1（Beta，不可用）：`JSON.parseSendable`、Map 新方法。

KB 未给 `@Sendable` 装饰器语法、Sendable 类的字段限制、`taskpool` 与 UI 线程的交互规则。墨迹渲染的并发方案必须在官方文档/真机确认后再定。

### 8.4 对“纯逻辑层能否用 Node 验证”的结论

**能，但有边界。**

可行的子集（KB + 实测）：

- `interface`、`class`、`enum`、泛型、`readonly`、基本类型标注、`import/export`、纯函数、数组/Map/Set（ArkTS 容器库存在，但 API 细节 KB 未给）。
- Node 24.21 + `--experimental-transform-types` 可执行上述语法（本轮实测）。

必须避免（否则 Node 或 ArkTS 任一侧会挂）：

- 对象展开/结构化赋值（ArkTS 禁止 structural typing）。
- 隐式 `any`、`as any`。
- 装饰器（Node 不转换；ArkUI 装饰器不属于纯逻辑）。
- 任何 `@kit.*` / `@ohos.*` import。
- 依赖 DOM/Node 内置模块（`fs`、`path`、`process`）——ArkTS 没有。
- `namespace`、`const enum`、`import x = require()` 等 KB 未确认语法。
- 动态属性访问 `obj[key]` 的任意字符串键（ArkTS 静态检查通常不允许；KB 未逐条说明，按最严格处理）。

建议在 `core` 内建立一份“允许语法清单”，CI 用 `codelinter` + Node 双侧卡口；任何新增语法先两侧各跑一遍再合入。

---

## 9. InkCanvas 脚手架落地建议

### 9.1 建议的目录与文件（最小可构建）

```
harmony/
├── build-profile.json5          # products: compatible 6.1.1(24), target 26.0.0, 无 compileSdkVersion
├── oh-package.json5             # modelVersion 与工具链一致
├── hvigorfile.ts                # appTasks
├── hvigor/hvigor-config.json5   # modelVersion + cacheDir
├── AppScope/app.json5           # bundleName com.inkcanvas.app
├── AppScope/resources/base/element/string.json
├── AppScope/resources/base/media/app_icon.png
├── entry/
│   ├── build-profile.json5      # apiType stageMode
│   ├── hvigorfile.ts            # hapTasks
│   ├── oh-package.json5         # 依赖 5 个 HAR
│   └── src/main/
│       ├── module.json5         # deviceTypes phone/tablet/2in1
│       ├── ets/entryability/EntryAbility.ets
│       ├── ets/pages/Index.ets
│       └── resources/base/profile/main_pages.json
├── core/     # HAR，无 @kit import
├── platform/ # HAR，唯一系统 Kit 出口 + compat/
├── ink/      # HAR
├── controls/ # HAR
└── features/ # HAR
```

### 9.2 建议的项目级 `build-profile.json5`（在探针基础上加 HAR 模块）

```json5
{
  "app": {
    "signingConfigs": [],
    "products": [
      {
        "name": "default",
        "compatibleSdkVersion": "6.1.1(24)",
        "targetSdkVersion": "26.0.0",
        "runtimeOS": "HarmonyOS"
      }
    ],
    "buildModeSet": [ { "name": "debug" }, { "name": "release" } ]
  },
  "modules": [
    { "name": "entry",    "srcPath": "./entry",    "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "core",     "srcPath": "./core",     "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "platform", "srcPath": "./platform", "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "ink",      "srcPath": "./ink",      "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "controls", "srcPath": "./controls", "targets": [{ "name": "default", "applyToProducts": ["default"] }] },
    { "name": "features", "srcPath": "./features", "targets": [{ "name": "default", "applyToProducts": ["default"] }] }
  ]
}
```

> HAR 模块条目是否需要在项目级 `modules` 中逐一声明、还是由 `oh-package` 依赖自动发现，KB 未说明。先用 DevEco 模板验证；若模板只生成 entry，则以模板为准。

### 9.3 能力层接口（建议形状）

```typescript
// platform/src/main/ets/compat/Capabilities.ets
export interface PlatformCapabilities {
  readonly apiLevel: number;          // 来源待核实（deviceInfo.sdkApiVersion 未在 KB）
  hasPenKitFollow(): boolean;         // canIUse(syscap)
  hasOffscreenCanvas(): boolean;      // canIUse(syscap)
  hasOverlayWindow(): boolean;        // canIUse(syscap) / 恒 false 的三方降级
  hasArchive(): boolean;              // API 26 压缩能力
}

export function createCapabilities(): PlatformCapabilities { … }
```

业务层只依赖该接口；`api-matrix.md` 逐项登记 `capability / api24 / api26 / gating`。

### 9.4 脚手架阶段的验证顺序（诚实声明）

1. **先建空工程**：用 DevEco Studio 26.0.0 新建 Empty Ability（phone/tablet/2in1），确认 `hvigorw assembleHap` 成功（探针已证明这条路可行，但那是 26.0.1 Beta1）。
2. **再加一个 HAR**：确认 `harTasks`、`type: "har"`、`file:` 依赖、`Index.ets` 导出四件事。KB 未记载，必须实测。
3. **再加 `core` 纯逻辑**：确认 `.ts`/`.ets` 混编、Node 侧 `run-vectors` 可跑。
4. **最后接 `platform` 的 compat**：逐个 syscap 查字符串，`canIUse` 断言。
5. 每步记录实际工具链版本与构建输出；未实机运行的一律写“未验证”。

---

## 10. 已知未知 / 需实机或官方文档核实

| # | 未知项 | 为什么重要 | 核实方式 |
|---|---|---|---|
| U1 | `deviceInfo.sdkApiVersion` 是否存在、语义 | 设计草案的 `useApi26()` 依赖它；KB 查无 | 官方 `@ohos.deviceInfo` API 参考；真机 `console.info` |
| U2 | `canIUse` 的类型声明、返回值、HAR 中可用性 | 能力层唯一门控手段 | SDK `.d.ts`；真机 |
| U3 | Pen Kit / ArkGraphics / Overlay 的 syscap 字符串 | 无法猜写 | SDK `syscap.json` |
| U4 | HAR 的 `module.json5`/`hvigorfile.ts`/依赖语法 | 六层同构的前提 | DevEco 26 新建 HAR 模板 |
| U5 | DevEco 26 是否有“无设备 Local Test” | 决定 L1 之外能否跑 hypium | DevEco 文档/菜单 |
| U6 | `.ts` 与 `.ets` 混编是否被 hvigor 接受 | 决定纯逻辑文件后缀 | 建一个 HAR 实测 |
| U7 | API 26 压缩模块名/签名/格式 | `.icink` 打包 | SDK `.d.ts` + `archive-overview` |
| U8 | API 24 运行时执行 target 26 构建的 HAP，26 专属符号未命中分支是否安全 | 向下兼容的核心 | 24 真机/模拟器 |
| U9 | Preferences 16MB 上限对 `.icink` 的实际余量 | 存储选型 | 真机写入测试 |
| U10 | 分布式 KV 的权限/账号/设备条件 | 多端同步 | 官方文档 |
| U11 | `main_pages.json` 的确切 schema | 路由 | 26 模板 |
| U12 | ArkTS 完整禁用清单 | 纯逻辑子集 | 官方 TypeScript→ArkTS 迁移指南 |

---

## 11. 第三方应用硬约束（脚手架相关）

1. **权限天花板**：`system_core` 仅系统应用；`system_basic` 需签名证书；三方只能用 `normal` + `user_grant`。
2. **文件边界**：只能自由访问应用沙箱；沙箱外必须 FilePicker 或目录权限（且上架审核严格）。
3. **Preferences 不支持多进程**；RDB 单写、单条 ≤2MB、单次查询 ≤5000。
4. **测试需真机**：KB 明文；模拟器还不支持 ArkTS 基础库与并发。
5. **Beta 工具链不可上架**；26.0.1 的 API 不可用于生产。
6. **bundleName 不可改**；上架必须 App Pack。
7. **全局覆盖/全局热键/前台进程**：本 KB 指定的 references 中没有对应能力条目；按三方应用模型，不能假设可覆盖其他应用（与既有 Platform 研究结论一致，需产品决策）。
8. **ArkTS 静态限制**：无 structural typing、无隐式 any、必须显式类型；决定纯逻辑不能直接照搬 C# 的动态写法。

---

## 12. 开放问题（需要架构/产品决策）

1. `useApi26()` 的判据最终用 `deviceInfo.sdkApiVersion` 还是纯 `canIUse`？在 U1/U2 核实前不要写死。
2. HAR 拆分粒度：5 个 HAR 是否都必要？HAR 间循环依赖是否被 hvigor 禁止（KB 未说明）？
3. `core` 纯逻辑用 `.ts` 还是 `.ets`？若 `.ets`，Node 侧是否改名/复制？
4. 是否接受“首版不做跨设备同步/压缩包”，以规避 U7/U10？
5. 26.0.0 Release 工具链何时到位？在此之前所有构建都应标注“26.0.1 Beta1，未签名，不可上架”。
6. 是否把 `api-matrix.md` 作为脚手架的一部分先建（即使内容多为“未知”）？

---

## 13. 参考来源

- KB：`dev/references/app-framework.md`、`arkts.md`、`arkdata.md`、`core-file-kit.md`、`permissions.md`、`testing.md`、`publish.md`、`architecture.md`、`best-practices.md`、`dev-preparation.md`
- KB：`system/references/test-kit.md`、`device-info.md`、`file-management.md`
- KB：`templates/references/deveco-templates.md`
- KB：根 `SKILL.md`、`dev/SKILL.md`、`samples/references/tool-test.md`、`dev/references/multi-device.md`、`ui-development.md`、`industry-practices.md`、`media/references/ar-engine.md`
- 项目：`docs/superpowers/research/harmony/00-harness-notes.md`（H1~H11）
- 项目：`docs/superpowers/specs/2026-11-18-harmonyos-port-design.md`（F7/F8、§6 版本策略）
- 探针：`D:\tmp\harmony-probe` 的 `build-profile.json5`、`oh-package.json5`、`hvigor/hvigor-config.json5`、`AppScope/app.json5`、`entry/src/main/module.json5`
- 本机复核：Node `v24.21.0` + `--experimental-transform-types`（enum/interface/class 执行成功）
