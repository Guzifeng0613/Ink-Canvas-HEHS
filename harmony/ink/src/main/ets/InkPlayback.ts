// @parity src/InkCanvas.Ink/InkPlayback.cs
// 画布内 display-only 回放：源笔画与历史不动，只按时间窗显示可见前缀。
import { InkStroke, InkReplaySession, StrokeAttributes } from '@inkcanvas/core';

export class InkPlayback {
  private readonly targets: InkStroke[];
  private readonly session: InkReplaySession;
  private readonly visible: InkStroke[] = [];

  constructor(strokes: InkStroke[]) {
    this.targets = strokes.slice();
    const clones: InkStroke[] = [];
    for (let i = 0; i < this.targets.length; i++) {
      clones.push(this.targets[i].clone());
    }
    this.session = new InkReplaySession(clones, 12);
    this.seek(0);
  }

  get VisibleStrokes(): InkStroke[] {
    return this.visible;
  }

  get DurationMs(): number {
    return this.session.DurationMs;
  }

  Includes(stroke: InkStroke): boolean {
    return this.targets.indexOf(stroke) >= 0;
  }

  seek(elapsedMs: number): void {
    this.session.writeVisibleAt(elapsedMs, this.visible);
  }
}

// 确定性向量入口：按点数构建笔画 → 各次 Seek 后可见计数 + 总时长。
export function playbackProbe(pointCounts: number[], seeks: number[]): { Counts: number[]; DurationMs: number } {
  const strokes: InkStroke[] = [];
  for (let i = 0; i < pointCounts.length; i++) {
    const stroke = new InkStroke(new StrokeAttributes());
    for (let j = 0; j < pointCounts[i]; j++) {
      stroke.addPoint(j, 0);
    }
    strokes.push(stroke);
  }
  const playback = new InkPlayback(strokes);
  const counts: number[] = [];
  for (let i = 0; i < seeks.length; i++) {
    playback.seek(seeks[i]);
    counts.push(playback.VisibleStrokes.length);
  }
  return { Counts: counts, DurationMs: playback.DurationMs };
}