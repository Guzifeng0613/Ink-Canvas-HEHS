# HarmonyOS 移植 — Phase A：地基与纯逻辑核心 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 `harmony/` 六模块工程与 `parity/` 互认基础设施，并把 WinUI3 纯逻辑核心（墨迹模型/笔锋/几何/擦除/撤销/选区/手势/图形/识别/触摸分档/回放/合成/设置与存档格式）以 1:1 语义移植为 ArkTS，包

**Architecture:** 同构分层（entry → features → controls → ink → platform → core）；core 只含 ArkTS∩TS 严格子集的 `.ts`，不 import 任何 `@kit.*`，可被 Node 24 `--experimental-transform-types` 直接执行；C# 侧新增独立 `tools/InkCanvas.VectorGen` 生成 `parity/vectors/*.json`，两侧测试共同消费。所有跨端格式（Settings.json、.icink、画板包、PPT manifest）字段名 PascalCase 原样保留。

**Tech Stack:** HarmonyOS API 26（DevEco Command Line Tools 26.0.1.290 beta / SDK 26.0.1 Beta1）、hvigor 6.26.21、ArkTS/TS、Node 24.21（零依赖向量运行器）、.NET 10 + xunit（现有）。

**Spec:** `docs/superpowers/specs/2026-11-18-harmonyos-port-design.md`（实现者必须同时阅读）

## Global Constraints

- 工具链：`E:\EcoStudioCLI\command-line-tools`；构建前 `. D:\InkCanvas\tools\harmony-env.ps1`；缓存 `E:\harmony-cache`。
- 构建命令：`hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon`（未签名 HAP）。
- `compileSdkVersion` **禁止写入** build-profile.json5（00303313）；`compatibleSdkVersion "6.1.1(24)"`、`targetSdkVersion "26.0.0"`。
- `modelVersion "26.0.0"` 必须同时出现在 `harmony/hvigor/hvigor-config.json5` 与 `harmony/oh-package.json5`（不一致报 00303027）。
- core 模块：仅 `.ts`，无 `@kit.*` import；禁止 `any`/`unknown`/对象展开/隐式 any；enum 允许；所有导出函数/方法显式类型。
- JSON 字段名与 C# 完全一致（PascalCase）；枚举按**数字**序列化；不新增/改名/删字段。
- 浮点比较：整数/字符串/枚举/字段集合严格相等；几何序列容差 `1e-4`（spec §5）。
- 禁止臆造 SystemCapability 字符串；已核实仅 `SystemCapability.ArkUI.ArkUI.Full`、`SystemCapability.Stylus.Handwrite`、`SystemCapability.Stylus.StylusService`、`SystemCapability.MultimodalInput.Input.Pointer`。
- API 26 专属能力不得进入 core；只能在后续 `platform/compat`。
- **不得修改用户未提交的 WinUI3 文件**（`git status` 中 M 的文件）。Phase A 只新增文件；C# 侧仅新增 `tools/InkCanvas.VectorGen/*` 与 `tests/InkCanvas.Core.Tests/VectorParityTests.cs`、`tests/.../GoldenVectorFixtures.cs`（新文件）。
- 锚点：本阶段 ArkTS 文件写 `// @parity src/...` 指向 C#；C# 侧 `// @parity harmony/...` 因工作树未清理而**延后**，由 `parity/anchors.json` 临时登记并在 manifest `notes` 标注（见 Task 10）。
- 向量调用契约：**所有向量目标必须是模块顶层导出函数**（runner 以 `mod[fn](...input)` 调用）；有状态类（UndoStack、TwoFingerManipulator 等）通过模块内薄包装函数暴露（如 `simulateUndo(ops: string[])`），包装函数仅做驱动、不含业务逻辑。
- 验证如实：本机无真机；L2 编译与 L1 向量必须通过；L3 一律标注“未实机验证”。
- 每个 Task 结束必须：`hvigorw assembleHap` 通过 + `node parity/run-vectors.mjs` 通过 + `node tools/parity-check.mjs` 通过 + 提交（只 add 本任务文件）。

## Review Focus（最可能伤人、必须被测试钉住的 5 类输入）

1. **Settings.json 中残留 Windows 绝对路径**（如 `D:\Ink Canvas`）→ 必须可解析、不崩溃，HarmonyOS 侧迁移到沙箱并保持其余字段逐字保留。
2. **WinUI3 生成的 .icink/画板包**（PascalCase、ARGB uint、float[][]、缺少 `HighlighterAlpha` 的画板包）→ 必须原样解析并字段级往返一致。
3. **接触宽度/高度为 0 或 undefined 的设备** → 手掌判定必须降级（笔优先/取消事件），不得把整屏手指都判为手掌或全部误擦。
4. **自交且半透明的笔画** → 轮廓并集不得在交叉处二次加深；若 Canvas 填充规则不可用，降级必须被向量/文档显式登记。
5. **枚举数值**（TaperKind、OptionalOperation、ThemeOptions 等）→ 必须按数字序列化并与 C# 完全一致，不得改为字符串枚举。

---

## Phase A 文件结构

```
harmony/
  build-profile.json5 / hvigorfile.ts / oh-package.json5 / hvigor/hvigor-config.json5
  AppScope/{app.json5,resources/...}
  entry/        （UIAbility + 占位页）
  core/         src/main/ets/{ink,settings,math,shapes,recognition,selection,input,replay,imaging,theming,versioning,update,persistence,diagnostics}/*.ts
  platform/     （占位：后续 Phase B/C）
  ink/          （占位）
  controls/     （占位）
  features/     （占位）
parity/
  manifest.json / anchors.json / api-matrix.md
  schemas/{settings,icink,board-pack,ppt-manifest}.schema.json
  tokens.json / glyphs.json          （由脚本从 C# 源生成）
  vectors/*.json                     （由 VectorGen 生成）
  run-vectors.mjs
tools/
  extract-tokens.mjs / extract-glyphs.mjs / parity-check.mjs
  InkCanvas.VectorGen/{InkCanvas.VectorGen.csproj,Program.cs,Generators/*.cs}
tests/InkCanvas.Core.Tests/
  VectorParityTests.cs / GoldenVectorFixtures.cs   （新增）
docs/harmony-porting-notes.md                     （新增，验证声明）
```

---

### Task 1: `harmony/` 六模块脚手架与首次构建

**Files:**
- Create: `harmony/build-profile.json5`, `harmony/hvigorfile.ts`, `harmony/oh-package.json5`, `harmony/hvigor/hvigor-config.json5`
- Create: `harmony/AppScope/app.json5`, `harmony/AppScope/resources/base/element/string.json`, `harmony/AppScope/resources/base/media/app_icon.png`
- Create: `harmony/entry/{build-profile.json5,hvigorfile.ts,oh-package.json5}`, `harmony/entry/src/main/{module.json5,ets/entryability/EntryAbility.ets,ets/pages/Index.ets,resources/base/element/{string,color}.json,resources/base/profile/main_pages.json,resources/base/media/app_icon.png}`
- Create: `harmony/{core,platform,ink,controls,features}/{build-profile.json5,hvigorfile.ts,oh-package.json5,Index.ets}` 与各自 `src/main/module.json5`
- Modify: 无（不得改 src/ 下任何文件）

**Interfaces:**
- Produces: 可构建的单 HAP 工程；HAR 包名 `@inkcanvas/core|platform|ink|controls|features`；`entry` 依赖全部五个 HAR（`file:../<module＞`）。
- Produces: 每个 HAR 的 `Index.ets` 至少导出一个占位符号（`export const MODULE = '<name>';`），供后续任务替换。

- [ ] **Step 1: 创建根配置**
  根 `build-profile.json5`：products 仅 `name/default`，`compatibleSdkVersion "6.1.1(24)"`、`targetSdkVersion "26.0.0"`、`runtimeOS "HarmonyOS"`，**无 compileSdkVersion**；modules 列 entry+5 HAR（`srcPath`、targets.applyToProducts）。
  `hvigor/hvigor-config.json5`：`modelVersion "26.0.0"`、`execution.daemon false`、`properties["hvigor.cacheDir"]="E:/harmony-cache/hvigor-cache"`。
  根 `oh-package.json5`：`modelVersion "26.0.0"`、空依赖。

- [ ] **Step 2: 创建 AppScope 与 entry 最小可运行页**
  `app.json5`：`bundleName "com.inkcanvas.harmony"`、`versionCode 1000000`、`versionName "3.4.0"`、icon/label 引用资源。
  `module.json5`：`deviceTypes ["phone","tablet","2in1"]`、EntryAbility、pages 指向 `pages/Index`。
  `EntryAbility.ets`：`onWindowStageCreate` → `windowStage.loadContent('pages/Index')`。
  `Index.ets`：居中 `Text('Ink Canvas HarmonyOS')`。

- [ ] **Step 3: 创建五个 HAR 占位模块**
  每个：`module.json5`（`"type":"har"`）、`hvigorfile.ts`（`harTasks`）、`oh-package.json5`（`"name": "@inkcanvas/<n>"`、`"main": "Index.ets"`）、`Index.ets`（`export const MODULE = '<n>';`）。
  entry `oh-package.json5` 增加五个 `file:../<n>` 依赖；entry `Index.ets` import 任一符号以防未使用告警。

- [ ] **Step 4: 安装依赖并构建**

Run: `. D:\InkCanvas\tools\harmony-env.ps1; cd D:\InkCanvas\harmony; ohpm install --all; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon`
Expected: `BUILD SUCCESSFUL`，仅有 `No signingConfig found` 告警。

- [ ] **Step 5: 提交**

```bash
git add harmony
git commit -m "harmony: 六模块脚手架与首次 HAP 构建通过"
```

---

### Task 2: `parity/` 互认基础设施与自检工具

**Files:**
- Create: `parity/manifest.json`, `parity/anchors.json`, `parity/api-matrix.md`
- Create: `parity/schemas/settings.schema.json`, `parity/schemas/icink.schema.json`, `parity/schemas/board-pack.schema.json`, `parity/schemas/ppt-manifest.schema.json`
- Create: `tools/extract-tokens.mjs`, `tools/extract-glyphs.mjs`, `tools/parity-check.mjs`, `parity/run-vectors.mjs`
- Create: `parity/tokens.json`, `parity/glyphs.json`（由脚本生成）

**Interfaces:**
- Produces: `manifest.json` 结构 `{features:[{id,name,status,csharp[],arkts[],vectors[],tests[],notes}]}`；`status ∈ planned|ported|adapted|not-ported`（`planned` 仅开发期，`--final` 拒绝）。
- Produces: `run-vectors.mjs` 读取 `parity/vectors/*.json`；单文件结构 `{id, entry, cases:[{id, fn, input, expected, tolerance?}]}`；`entry` 为仓库相对 `.ts` 路径；以 `await import(pathToFileURL(entry))` 加载，调用 `mod[fn](...input)`（`input` 为参数数组）。
- Produces: `parity-check.mjs` 退出码 0/1；校验 manifest 文件存在、ArkTS 文件含 `@parity <csharp path>`、vector 文件存在、tokens/glyphs 与生成器输出一致；`--final` 额外拒绝 `planned` 与空 `arkts`。

- [ ] **Step 1: 写 manifest 骨架（55 个单元）**
  以 `docs/superpowers/research/harmony/08-baseline.md` 的 62 项为源，排除 5 项 not-ported-* 与 2 项死代码，生成 55 条；`status:"planned"`、`csharp` 填基线路径、`arkts:[]`、`notes` 写明口径；唯一 `not-ported` 预置 `system-autostart`（原因+替代路径）。

- [ ] **Step 2: 写 anchors.json 与四份 JSON Schema**
  `anchors.json`：`{pairs:[{csharp, arkts, interim:true}]}`，Phase A 先登记本阶段全部 core 对（arkts 侧将新增）。
  四份 schema 字段名取自 `06-features.md` 与 `02-core-domain.md` 的字段全表；`additionalProperties:false` 仅用于生成测试夹具，不用于运行时校验。

- [ ] **Step 3: 写生成器与检查器**
  `extract-tokens.mjs`：正则解析 `src/InkCanvas.Controls/Themes/Tokens.xaml` 的 `x:Key`/Color 值 → `parity/tokens.json`（键名原样，值 `#AARRGGBB` 或 `{ThemeResource ...}` 原样）。
  `extract-glyphs.mjs`：解析 `src/InkCanvas.Controls/Controls/Glyphs.cs` 的常量 → `parity/glyphs.json`（`{name,codepoint,target:null}`）。
  `parity-check.mjs`：按 Interfaces 校验；`--final` 规则同前；`--self-test` 用内存夹具验证 3 条失败路径（缺文件/缺锚点/modelVersion 不一致检测仅提示）。

- [ ] **Step 4: 验证工具自检与零向量运行**

Run: `node tools/extract-tokens.mjs && node tools/extract-glyphs.mjs && node tools/parity-check.mjs && node parity/run-vectors.mjs`
Expected: 生成两文件；checker 退出 0（`planned` 允许）；runner 输出 `0 vectors` 退出 0。

- [ ] **Step 5: 提交**

```bash
git add parity tools/extract-tokens.mjs tools/extract-glyphs.mjs tools/parity-check.mjs
git commit -m "parity: 功能清单/锚点/生成器/向量运行器与检查器"
```

---

### Task 3: 墨迹与设置模型 + Settings 编解码 + VectorGen 基座

**Files:**
- Create: `harmony/core/src/main/ets/ink/{InkColor,InkPoint,StrokeAttributes,InkStroke,InkDocument}.ts`
- Create: `harmony/core/src/main/ets/settings/{AppSettings,OriginalMenuCatalog}.ts`
- Create: `harmony/core/src/main/ets/persistence/SettingsCodec.ts`
- Modify: `harmony/core/Index.ets`
- Create: `tools/InkCanvas.VectorGen/{InkCanvas.VectorGen.csproj,Program.cs,Generators/SettingsVectors.cs,JsonWriter.cs}`
- Create: `tests/InkCanvas.Core.Tests/VectorParityTests.cs`
- Create: `parity/vectors/settings-roundtrip.json`, `parity/vectors/ink-model.json`

**Interfaces:**
- Produces: `InkColor {A,R,G,B}`（每通道 0–255；`static fromArgb(n: number): InkColor`、`toArgb(): number`，按 C# `uint` 低 32 位语义）。
- Produces: `InkPoint {X:number; Y:number; Width:number; TimeMs:number; Pressure:number}`（**Width 为全宽存储值**；字段名以 `01-core-ink.md` §模型为准）。
- Produces: `StrokeAttributes`（含 `BaseWidth`、`TaperKind`、`HighlighterAlpha`、`Color`；`effectiveTaper()`/`effectiveColor()` 语义同 C#）。
- Produces: `AppSettings`（9 段全部键名/默认值，严格照 `02-core-domain.md`/`06-features.md`）与顶层函数 `parseSettings(json: string): AppSettings`、`stringifySettings(s: AppSettings): string`（显式字段映射；`SettingsCodec` 类仅作内部实现）。
- Produces: `VectorGen` 运行方式 `dotnet run --project tools/InkCanvas.VectorGen -- --out D:\InkCanvas\parity\vectors`；输出写入器固定 `\uXXXX` 关闭、UTF-8、2 空格缩进、`\n` 行尾。

- [ ] **Step 1: 写 C# 失败测试（设置往返与枚举数值）**
  `VectorParityTests.cs`：`SettingsRoundtripVectorsMatch()` 读取 `parity/vectors/settings-roundtrip.json`，对每个 case 反序列化→序列化→断言字段与枚举数值完全一致；`InkModelVectorsMatch()` 断言 Width/TimeMs/ARGB 精确。先运行确认因文件缺失 FAIL。
  Run: `dotnet test tests/InkCanvas.Core.Tests --filter "FullyQualifiedName~VectorParityTests"`
  Expected: FAIL（FileNotFoundException）。

- [ ] **Step 2: 写 VectorGen 并生成两个向量文件**
  `Program.cs` 解析 `--out`，调用 `SettingsVectors.Write(dir)`（构造包含 **Windows 绝对路径**、全部 9 段字段、`ThemeOptions=0/1/2`、`OptionalOperation=0/1` 的夹具，输出 input JSON 与 expected 字段表）与 `InkModelJson`（ARGB 边界 0x00000000/0xFFFFFFFF、Width 0.5–15、TimeMs 长序列）。
  Run: `dotnet run --project tools/InkCanvas.VectorGen -- --out D:\InkCanvas\parity\vectors`
  Expected: 生成两文件，`id` 唯一。

- [ ] **Step 3: 写 ArkTS 模型与编解码（最小实现）**
  按 Interfaces 实现；`SettingsCodec` 使用显式 key 列表（禁止 `JSON.parse` 直转类）；未知字段保留到 `extra` 以便原样写回；Windows 路径字段保留原值（迁移在 platform 层，本任务只保证不丢）。
  在 `Index.ets` 导出全部类型。

- [ ] **Step 4: 跑 Node 向量 + C# 测试**

Run: `node parity/run-vectors.mjs --only settings-roundtrip --only ink-model`
Expected: 全部 PASS。
Run: `dotnet test tests/InkCanvas.Core.Tests --filter "FullyQualifiedName~VectorParityTests"`
Expected: PASS。

- [ ] **Step 5: 构建 + 提交**

Run: `. D:\InkCanvas\tools\harmony-env.ps1; cd D:\InkCanvas\harmony; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon`
Expected: BUILD SUCCESSFUL。

```bash
git add harmony/core tools/InkCanvas.VectorGen tests/InkCanvas.Core.Tests/VectorParityTests.cs parity/vectors/settings-roundtrip.json parity/vectors/ink-model.json
git commit -m "core: 墨迹与设置模型、Settings 编解码、黄金向量基座"
```

---

### Task 4: 因果笔锋（Tail / Velocity）

**Files:**
- Create: `harmony/core/src/main/ets/ink/TaperProfile.ts`
- Modify: `harmony/core/Index.ets`
- Create: `tools/InkCanvas.VectorGen/Generators/TaperVectors.cs`
- Create: `parity/vectors/taper-tail.json`, `parity/vectors/taper-velocity.json`
- Modify（新增测试方法）: `tests/InkCanvas.Core.Tests/VectorParityTests.cs`

**Interfaces:**
- Produces: `TaperKind`（数值同 C#：`None/Tail/Velocity` 顺序）；顶层函数 `effectiveWidths(baseWidth: number, points: InkPoint[], kind: TaperKind, options?: TaperOptions): number[]`（`TaperProfile` 类仅作内部实现）；`TaperOptions {TailLength:number=10; TailStart:number=4; TailTip:number=0.2}`（常量以 `01-core-ink.md` §2 为准）。
- Invariant: 输出宽度单调不减；抬笔不重算；不引用未来点。

- [ ] **Step 1: 写 C# 向量生成器**
  从现有 `TaperProfile`（**只调用，不修改**）采样：随机游走 0/1/2/5/10/50 点、单点、短笔画、长笔画、时间跳变、压感 0/0.5/1；输出 `taper-*.json`（`entry:"harmony/core/src/main/ets/ink/TaperProfile.ts"`，`fn:"effectiveWidths"`，`input:[baseWidth, points, kind, options]`，容差 `1e-4`）。
  Run: 同 Task 3 Step 2。Expected: 两文件生成。

- [ ] **Step 2: 写失败测试并确认失败**

Run: `node parity/run-vectors.mjs --only taper-tail --only taper-velocity`
Expected: FAIL（模块不存在）。

- [ ] **Step 3: 实现 `TaperProfile.ts`**
  按 `01-core-ink.md` 的公式逐点增量实现（Tail：末尾 10 点滚动窗口、起点 4 点、尖端 0.2；Velocity：`distance/20` 分段曲线 `0.05/0.25/1.5`）；显式类型；无对象展开。

- [ ] **Step 4: 跑向量与构建**

Run: `node parity/run-vectors.mjs --only taper-tail --only taper-velocity` → PASS
Run: `. D:\InkCanvas\tools\harmony-env.ps1; cd D:\InkCanvas\harmony; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon` → BUILD SUCCESSFUL

- [ ] **Step 5: 提交**

```bash
git add harmony/core/src/main/ets/ink/TaperProfile.ts harmony/core/Index.ets tools/InkCanvas.VectorGen/Generators/TaperVectors.cs parity/vectors/taper-tail.json parity/vectors/taper-velocity.json tests/InkCanvas.Core.Tests/VectorParityTests.cs
git commit -m "core: 因果增量笔锋（Tail/Velocity）与黄金向量"
```

---

### Task 5: 笔画并集轮廓、线条擦、撤销栈

**Files:**
- Create: `harmony/core/src/main/ets/ink/{StrokeGeometry,StrokeEraser}.ts`, `harmony/core/src/main/ets/history/UndoStack.ts`
- Modify: `harmony/core/Index.ets`
- Create: `tools/InkCanvas.VectorGen/Generators/GeometryVectors.cs`
- Create: `parity/vectors/{stroke-geometry,stroke-eraser,undo}.json`

**Interfaces:**
- Produces: `StrokeGeometry.buildOutline(points: InkPoint[], options?: OutlineOptions): OutlineResult {points:number[]; subpaths:number[][]}`（每段 capsule 24 段圆/12 步半圆、同向 winding；顶点顺序与 C# 完全一致）；向量入口为顶层 `buildOutline(...)`。
- Produces: `StrokeEraser.eraseByPoint(stroke: InkStroke, center:{x:number;y:number}, radius:number): InkStroke[]`（二次方程中心线切割，碎片重建）；向量入口为顶层 `eraseByPoint(...)`。
- Produces: `UndoStack`（`push(undo,redo)`、`undo()`、`redo()`、`clear()`、`canUndo/canRedo`、容量上限同 C#）；向量入口为薄包装 `simulateUndo(ops: string[]): {canUndo:boolean; canRedo:boolean; last:string; count:number}`。

- [ ] **Step 1: 生成向量**（几何：直线/折线/自交/单点/零长段/大半径；擦除：中心/边缘/多切点/整段移除；撤销：混合 push/undo/redo/上限溢出）
- [ ] **Step 2: 失败测试** `node parity/run-vectors.mjs --only stroke-geometry --only stroke-eraser --only undo` → FAIL
- [ ] **Step 3: 实现三个模块**（算法细节照 `01-core-ink.md`；自交轮廓用同向并集，**不得 even-odd 挖空**）
- [ ] **Step 4: 向量 PASS + 构建 SUCCESSFUL**
- [ ] **Step 5: 提交**（同格式）

---

### Task 6: 矩阵、选区、双指手势、枢轴

**Files:**
- Create: `harmony/core/src/main/ets/math/Matrix3x2.ts`
- Create: `harmony/core/src/main/ets/selection/SelectionGeometry.ts`
- Create: `harmony/core/src/main/ets/ink/TwoFingerManipulator.ts`, `harmony/core/src/main/ets/ink/MatrixTransformCenter.ts`
- Create: `tools/InkCanvas.VectorGen/Generators/TransformVectors.cs`
- Create: `parity/vectors/{matrix,selection,two-finger,pivot}.json`

**Interfaces:**
- Produces: `Matrix3x2 {M11,M12,M21,M22,M31,M32}`，**行向量语义**（`v' = v * M`；`multiply(a,b)` 与 `System.Numerics` 一致）；`transformPoint`/`invert`/`createRotation/Scale/Translation`。
- Produces: `SelectionGeometry.hitTest(handles, p, tolerance=12): number`、`buildHandles(bounds, rotation): Handle[]`（句柄尺寸 6/7、旋转柄 28 等常量照 `04-ink.md`）。
- Produces: `TwoFingerManipulator`（平移/缩放/旋转/惯性，枢轴选项）；`MatrixTransformCenter.resolve(option: number, sceneBounds, selectionBounds): {x,y}`。

- [ ] **Step 1–5:** 同 Task 5 节奏：生成向量 → FAIL → 实现 → PASS + 构建 → 提交。
  额外断言：矩阵 `M11..M32` 与乘法顺序；`view→world` 共轭（`V·D·V⁻¹`）由向量钉住。

---

### Task 7: 25 种图形生成与形状识别

**Files:**
- Create: `harmony/core/src/main/ets/shapes/ShapeGenerator.ts`
- Create: `harmony/core/src/main/ets/recognition/ShapeRecognizer.ts`
- Create: `tools/InkCanvas.VectorGen/Generators/ShapeVectors.cs`
- Create: `parity/vectors/{shapes,recognizer}.json`

**Interfaces:**
- Produces: `ShapeKind`（枚举 25 项，数值顺序同 C#）、`ShapeGenerator.generate(kind: number, params: ShapeParams): StrokeSpec`（返回中心线与属性；常量照 `02-core-domain.md` §图形：dash 5/13.8、dot 8.28/0.8、椭圆 0.05π/0.083π、柱锥 2.646、箭头 30×10、坐标轴 20×7、四档角度吸附等）。
- Produces: `ShapeRecognizer.recognize(stroke: InkStroke): RecognitionResult {kind:number; confidence:number}`（阈值 40px、0.035、0.10/0.14、45°/20px）。

- [ ] **Step 1–5:** 标准节奏；向量覆盖每个 ShapeKind 至少 1 组参数（含两步式第一步、负方向拖拽），识别覆盖线/圆/椭圆/多边形/拒识。

---

### Task 8: 触摸分档、手掌拒绝、回放时钟、像素合成、主题/版本/更新策略

**Files:**
- Create: `harmony/core/src/main/ets/input/{TouchContactClassifier,PalmRejector}.ts`
- Create: `harmony/core/src/main/ets/replay/{ReplayClock,InkReplaySession}.ts`
- Create: `harmony/core/src/main/ets/imaging/PixelCompositing.ts`
- Create: `harmony/core/src/main/ets/theming/ThemeResolver.ts`
- Create: `harmony/core/src/main/ets/versioning/AppVersion.ts`
- Create: `harmony/core/src/main/ets/update/{ReleaseFeed,UpdatePolicy}.ts`
- Create: `tools/InkCanvas.VectorGen/Generators/MiscVectors.cs`
- Create: `parity/vectors/{touch-classifier,palm,replay,compositing,theme,update}.json`

**Interfaces:**
- Produces: `TouchContactClassifier.classify(width:number, height:number, tool:number, settings): ContactKind`（QuadIR 取 `sqrt(w*h)`；`TouchMultiplier=0` 禁用橡皮；阈值 30/5/20/0.25 等照 `02-core-domain.md`）。
- Produces: `PalmRejector`（**仅 down 判定一次**、无时间阈值：`max(w,h) >= threshold`；笔优先；`onPointerUp` 复位）。
- Produces: `ReplayClock`（12ms/点、33ms 帧）、`InkReplaySession`（按绘制顺序/暂停/倍速/进度）。
- Produces: `PixelCompositing.sourceOverPremultipliedBgra(dst,src)` 整数公式同 C#。
- Produces: `ThemeResolver.resolve(...)`、`AppVersion.parse/compare/format`、`ReleaseFeed.parse`、`UpdatePolicy.isWithinSilentWindow(...)`。

- [ ] **Step 1–5:** 标准节奏；**Review Focus 3**：向量须含 `width=0,height=0`、`undefined→0`、笔工具、鼠标工具四组，断言降级路径。

---

### Task 9: 持久化格式编解码（.icink / 画板包 / PPT manifest）

**Files:**
- Create: `harmony/core/src/main/ets/persistence/{IcinkCodec,BoardPackCodec,PptManifestCodec}.ts`
- Create: `tools/InkCanvas.VectorGen/Generators/PersistenceVectors.cs`
- Create: `parity/vectors/{icink-roundtrip,board-roundtrip,ppt-manifest}.json`

**Interfaces:**
- Produces: `IcinkCodec.parse(text:string): IcinkDocument` / `stringify(doc): string`（字段名/类型照 `06-features.md` 与 `01-core-ink.md`；`Strokes[].Points` 为 `float[][]` 或对象数组——以 C# 实际输出为准，向量钉住）。
- Produces: `BoardPackCodec`（99 页上限、页/元素矩阵字段；**保留缺少 `HighlighterAlpha` 的 v1 行为**，往返字段一致）。
- Produces: `PptManifestCodec`（manifest + Position 字段；轮询契约常量 200/900 由上层使用）。

- [ ] **Step 1–5:** 标准节奏；**Review Focus 2**：向量夹具必须包含真实 WinUI3 输出样本（可从 `artifacts/` 或测试构造），断言逐字段往返；无图片仅视频页的保存语义按基线复刻并在 notes 记录。

---

### Task 10: 锚点、manifest 状态、全量验证与提交

**Files:**
- Modify: `parity/manifest.json`（core 相关条目 `status: "ported"|"adapted"`，填 `arkts` 与 `vectors`）
- Modify: `parity/anchors.json`（登记全部 core 对，`interim:true`）
- Modify: 所有 `harmony/core/src/main/ets/**/*.ts`（文件头加 `// @parity src/...`）
- Create: `docs/harmony-porting-notes.md`（Phase A 验证声明：L1/L2 通过，L3 未实机）
- Modify: `harmony/core/Index.ets`（最终导出面）

**Interfaces:**
- Produces: `node tools/parity-check.mjs --final --scope core` 退出 0（允许 `interim` 锚点，禁止 `planned` 仅限已声明 scope）。

- [ ] **Step 1: 添加锚点**（每个 ArkTS core 文件首行 `// @parity <对应 C# 相对路径>`）
- [ ] **Step 2: 更新 manifest/anchors 状态**（core 条目全部 `ported`，平台相关在后续 Phase 填 `adapted`）
- [ ] **Step 3: 全量验证**

Run: `node tools/parity-check.mjs --final --scope core` → 0
Run: `node parity/run-vectors.mjs` → 全部 PASS（统计输出 case 数）
Run: `. D:\InkCanvas\tools\harmony-env.ps1; cd D:\InkCanvas\harmony; hvigorw assembleHap --mode module -p product=default -p buildMode=debug --no-daemon` → BUILD SUCCESSFUL
Run: `cd D:\InkCanvas; dotnet test tests\InkCanvas.Core.Tests --filter "FullyQualifiedName~VectorParityTests"` → PASS
Run: `E:\EcoStudioCLI\command-line-tools\bin\codelinter.bat --help` 确认可用（正式规则配置在 Phase B 接入）

- [ ] **Step 4: 写验证声明** `docs/harmony-porting-notes.md`：列明已通过项、未实机项（触摸/笔/性能）、已知缺陷复刻项（画板 HighlighterAlpha、窗口位置记忆缺失、PPT 轮询双口径）。
- [ ] **Step 5: 提交**

```bash
git add harmony/core parity docs/harmony-porting-notes.md
git commit -m "core: 纯逻辑核心移植完成，锚点/manifest/黄金向量全绿"
```

---

## Phase A 验收标准

1. `harmony/` 六模块工程 `hvigorw assembleHap` 成功（未签名 debug HAP）。
2. `parity/vectors/` 覆盖 Task 3–9 全部算法；`node parity/run-vectors.mjs` 全 PASS。
3. C# 侧 `VectorParityTests` 全 PASS（证明同一向量两侧一致）。
4. `node tools/parity-check.mjs --final --scope core` 退出 0。
5. `parity/manifest.json` 中 core 相关功能全部 `ported`；`system-autostart` 为唯一 `not-ported`。
6. 未触碰 `src/` 与用户未提交改动；所有提交只含本阶段新增/声明的文件。

## 后续阶段路线（各自单独成计划，A 落地后编写）

| 阶段 | 内容 | 产出 |
|---|---|---|
| B | `ink` 运行时：InkController 状态机、输入路由、Renderer 接口 + Canvas 双层渲染、手掌接线、选区交互、回放驱动 | 可编译的墨迹运行时 + 状态机向量 |
| C | `platform`：compat 能力层（API24/26）、输入适配、窗口/断点、存储/日志、快照、更新检查 | 能力探针与降级矩阵 |
| D | `controls`：token/主题、Symbol 图标映射、浮动栏、调色盘、图形菜单、计时环、动效 | 界面组件与度量向量 |
| E | `features`：画板、课件(PDF)、持久化接线、抽奖、自动保存 | 功能层 |
| F | `entry`：UIAbility、Shell 状态机、页面/浮层、多形态布局、快捷键、设置/指南/是-否窗 | 完整 HAP |
| G | 覆盖收口：manifest 55 项状态、api-matrix、porting notes、codelinter、最终验证与评审 | 98% 覆盖率证明与交付文档 |
