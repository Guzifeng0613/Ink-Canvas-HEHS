// @parity src/InkCanvas.Features/Tools/Tools.cs (LotteryService)
export class LotteryEngine {
  Names: string[] = [];
  PeopleCount: number = 60;
  NoRepeat: boolean = false;
  private used: number[] = [];

  loadNames(text: string): void {
    this.Names = [];
    const lines = text.split('\n');
    for (let i = 0; i < lines.length; i++) {
      const trimmed = lines[i].trim();
      if (trimmed.length > 0) {
        this.Names.push(trimmed);
      }
    }
    this.used = [];
  }

  reset(): void {
    this.used = [];
  }

  pickWith(random: () => number): string | null {
    const poolSize = this.Names.length > 0 ? this.Names.length : Math.max(1, this.PeopleCount);
    if (this.NoRepeat && this.used.length >= poolSize) {
      return null;
    }
    let index = 0;
    let guard = 0;
    do {
      index = Math.min(poolSize - 1, Math.floor(random() * poolSize));
      guard++;
    } while (this.NoRepeat && this.used.indexOf(index) >= 0 && guard < poolSize * 8);
    if (this.NoRepeat) {
      this.used.push(index);
    }
    return this.Names.length > 0 ? this.Names[index] : (index + 1).toString();
  }

  pick(): string | null {
    return this.pickWith((): number => Math.random());
  }

  // ---- D5 多次抽取（C# Draw(count)：部分 Fisher-Yates，本次抽取内唯一） -----------

  private history: LotteryDraw[] = [];

  get History(): LotteryDraw[] {
    return this.history;
  }

  get PoolSize(): number {
    return this.Names.length > 0 ? this.Names.length : Math.max(1, this.PeopleCount);
  }

  get RemainingCount(): number {
    if (!this.NoRepeat) {
      return this.PoolSize;
    }
    let remaining = 0;
    for (let index = 0; index < this.PoolSize; index++) {
      if (this.used.indexOf(index) < 0) {
        remaining += 1;
      }
    }
    return remaining;
  }

  draw(count: number): string[] {
    return this.drawWith(count, (): number => Math.random(), 0);
  }

  /** 确定性入口：randoms 依次供给 [0,1)；timeMs 仅进历史记录（0 时省略）。 */
  drawWith(count: number, random: () => number, timeMs: number): string[] {
    if (count <= 0) {
      return [];
    }
    const available: number[] = [];
    for (let index = 0; index < this.PoolSize; index++) {
      if (!this.NoRepeat || this.used.indexOf(index) < 0) {
        available.push(index);
      }
    }
    const take = Math.min(count, available.length);
    const result: string[] = [];
    for (let index = 0; index < take; index++) {
      const next = index + Math.min(available.length - 1 - index,
        Math.max(0, Math.floor(random() * (available.length - index))));
      const temp = available[index];
      available[index] = available[next];
      available[next] = temp;
      const picked = available[index];
      if (this.NoRepeat) {
        this.used.push(picked);
      }
      result.push(this.Names.length > 0 ? this.Names[picked] : (picked + 1).toString());
    }
    if (take > 0) {
      this.history.push({ TimeMs: timeMs, Names: result.slice() });
      if (this.history.length > 20) {
        this.history.shift();
      }
    }
    return result;
  }
}

/** 与 C# LotteryDraw record 对应（TimeMs=0 表示省略时间）。 */
export interface LotteryDraw {
  TimeMs: number;
  Names: string[];
}

// 确定性向量入口：两次等量抽取，randoms 耗尽后回落 0。
export interface DrawSequenceResult {
  Draws: string[][];
  Remaining: number;
  HistoryCount: number;
}

export function drawSequenceProbe(
  namesText: string,
  peopleCount: number,
  noRepeat: boolean,
  count: number,
  randoms: number[]
): DrawSequenceResult {
  const engine = new LotteryEngine();
  engine.loadNames(namesText);
  engine.PeopleCount = peopleCount;
  engine.NoRepeat = noRepeat;
  let cursor = 0;
  const next = (): number => {
    if (cursor < randoms.length) {
      return randoms[cursor++];
    }
    return 0;
  };
  const draws: string[][] = [engine.drawWith(count, next, 0), engine.drawWith(count, next, 0)];
  return { Draws: draws, Remaining: engine.RemainingCount, HistoryCount: engine.History.length };
}

// 确定性向量入口：randoms 依次提供 [0,1) 随机值。
export function lotterySequence(
  namesText: string,
  peopleCount: number,
  noRepeat: boolean,
  randoms: number[]
): (string | null)[] {
  const engine = new LotteryEngine();
  engine.loadNames(namesText);
  engine.PeopleCount = peopleCount;
  engine.NoRepeat = noRepeat;
  const result: (string | null)[] = [];
  let cursor = 0;
  for (let i = 0; i < randoms.length; i++) {
    const value = randoms[i];
    result.push(engine.pickWith((): number => value));
    cursor++;
  }
  void cursor;
  return result;
}
