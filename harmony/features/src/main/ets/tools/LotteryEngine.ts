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
