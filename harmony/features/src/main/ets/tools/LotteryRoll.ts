// @parity src/InkCanvas.App/Views/LotteryWindow.cs（ReloadNames/RollAsync/DisplayName 纯逻辑）
// 抽奖 UI 层算法的 ArkTS 移植：替换名单规则、滚动闪烁、唯一抽选、输出分组。
// 与 LotteryEngine.Pick（持久不重复池）配合：NoRepeat 走引擎，非 NoRepeat 走本文件唯一抽选。
// 随机值由调用方按序提供 [0,1)，便于黄金向量确定性验证。

// Replace.txt 规则：整行 'old-->new' 精确匹配，按顺序应用（LotteryWindow.ReloadNames）。
export function applyReplaceRules(namesText: string, rulesText: string): string[] {
  const rules: string[] = [];
  const ruleLines = rulesText.split('\n');
  for (let i = 0; i < ruleLines.length; i++) {
    const line = ruleLines[i].trim();
    if (line.length > 0) {
      rules.push(line);
    }
  }
  const names: string[] = [];
  const lines = namesText.split('\n');
  for (let i = 0; i < lines.length; i++) {
    let s = lines[i].trim();
    for (let r = 0; r < rules.length; r++) {
      const idx = rules[r].indexOf('-->');
      if (idx >= 0 && s === rules[r].substring(0, idx)) {
        s = rules[r].substring(idx + 3);
      }
    }
    if (s.length > 0) {
      names.push(s);
    }
  }
  return names;
}

// 名单模式取 Names[number-1]，越界或数字模式回退号码字符串（LotteryWindow.DisplayName）。
export function lotteryDisplayName(names: string[], number: number): string {
  if (names.length > 0 && number <= names.length) {
    return names[number - 1];
  }
  return number.toString();
}

function drawNumber(pool: number, random: number): number {
  return 1 + Math.min(pool - 1, Math.floor(random * pool));
}

// 5 次闪烁，150ms 间隔（UI 计时），闪烁集合内唯一；集合满 pool 时清空（RollAsync flicker）。
// 返回每次闪烁显示的号码（1-based）。randoms 按序消耗，重复时继续取下一个。
export function lotteryFlicker(pool: number, randoms: number[]): number[] {
  const safePool = Math.max(1, pool);
  const displayed: number[] = [];
  const flicker: number[] = [];
  let cursor = 0;
  for (let i = 0; i < 5; i++) {
    let r = drawNumber(safePool, randoms[cursor++] ?? 0);
    if (safePool > 1) {
      let guard = 0;
      while (flicker.indexOf(r) >= 0 && guard < safePool * 8) {
        r = drawNumber(safePool, randoms[cursor++] ?? 0);
        guard++;
      }
    }
    flicker.push(r);
    if (flicker.length >= safePool) {
      flicker.length = 0;
    }
    displayed.push(r);
  }
  return displayed;
}

// 非 NoRepeat 的最终抽选：本次抽选内唯一（RollAsync else 分支）。返回 1-based 号码。
export function lotteryDrawUnique(pool: number, count: number, randoms: number[]): number[] {
  const safePool = Math.max(1, pool);
  const take = Math.min(count, safePool);
  const used: number[] = [];
  const outputs: number[] = [];
  let cursor = 0;
  for (let i = 0; i < take; i++) {
    let r = drawNumber(safePool, randoms[cursor++] ?? 0);
    if (safePool > 1) {
      let guard = 0;
      while (used.indexOf(r) >= 0 && guard < safePool * 8) {
        r = drawNumber(safePool, randoms[cursor++] ?? 0);
        guard++;
      }
    }
    used.push(r);
    outputs.push(r);
  }
  return outputs;
}

// 输出分组：≤5 单列，≤10 两列（split=(n+1)/2），>10 三列（a=(n+1)/3, b=(n+1)*2/3）。
// 整数除法与 C# 一致（Math.trunc）。返回各列换行拼接文本。
export function lotterySplitGroups(outputs: string[]): string[] {
  const n = outputs.length;
  if (n === 0) {
    return [];
  }
  if (n <= 5) {
    return [outputs.join('\n')];
  }
  if (n <= 10) {
    const split = Math.trunc((n + 1) / 2);
    return [outputs.slice(0, split).join('\n'), outputs.slice(split).join('\n')];
  }
  const a = Math.trunc((n + 1) / 3);
  const b = Math.trunc((n + 1) * 2 / 3);
  return [
    outputs.slice(0, a).join('\n'),
    outputs.slice(a, b).join('\n'),
    outputs.slice(b).join('\n')
  ];
}