# Vendored skill: Harmony-os-skills

- 来源：https://github.com/zz8127/Harmony-os-skills
- 上游 commit：`4758f3c981510c1e122089f49453bc710a106654`（master，浅克隆 vendoring 于 2026-11-18）
- 许可：MIT（见本目录 LICENSE），与本项目 GPL-3.0 兼容
- 内容：根 SKILL.md 总索引 + 8 个领域子技能（dev/system/media/ai-meta/design/samples/templates/agc）+ 138 篇 references
- 版本基线：HarmonyOS 26.0.0 / API 26 Release（OpenHarmony 7.0）为主；HarmonyOS 6.1.1 / API 24 Release 为存量兼容

## 本地修改（更新上游时需重新应用）

1. 根 SKILL.md frontmatter `name: harmonyos-master` → `name: harmony-os`（与目录名一致，便于 `/harmony-os` 调用）；
   description 追加一行本项目用途说明。其余内容与上游一致。
2. 删除了上游 `.git` 目录（vendoring，随本仓库提交）。

## 更新方法

```powershell
git clone --depth 1 https://github.com/zz8127/Harmony-os-skills $env:TEMP\harmony-os-skills
# 对比后覆盖本目录内容（保留本 VENDOR.md），重新应用上述本地修改，更新 commit 号与日期
```

## 本项目常用入口

| 需要 | 文件 |
|---|---|
| 手写笔（Pen Kit、跟手性加速） | `dev/references/pen-kit.md` |
| ArkUI / 触摸事件 / Canvas / XComponent | `dev/references/ui-development.md` |
| ArkTS 语言 / 状态管理 / 并发 | `dev/references/arkts.md` |
| Stage 模型 / 窗口管理 / 子窗 | `dev/references/app-framework.md` |
| 多设备 / 断点 / 平行视界 | `dev/references/multi-device.md`、`design/references/multi-device-design.md` |
| 2D 绘制（ArkGraphics 2D） | `media/references/graphics2d.md` |
| 输入（Input Kit / 多模输入） | `system/references/input-kit.md`、`input-multimodal-kit.md` |
| PC/2in1 桌面拓展 | `dev/references/desktop-extension-kit.md` |
| 工程模板 / SDK 版本配置 | `templates/references/deveco-templates.md` |
| PDF 课件批注 | `dev/references/pdf-kit.md` |
| 测试（hypium / DevEco Testing） | `dev/references/testing.md`、`system/references/test-kit.md` |
| 设计规范（色彩/字体/动效/控件） | `design/references/` |