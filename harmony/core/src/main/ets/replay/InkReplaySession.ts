// @parity src/InkCanvas.Core/Replay/InkReplaySession.cs
import { InkStroke } from '../ink/InkStroke';
import { StrokeAttributes } from '../ink/StrokeAttributes';

export class InkReplaySession {
  private readonly source: InkStroke[];
  private readonly cumulative: number[] = [];
  private readonly totalMs: number;

  constructor(strokes: InkStroke[], msPerPoint: number = 12) {
    this.source = strokes;
    let acc = 0;
    for (let i = 0; i < strokes.length; i++) {
      acc += Math.max(1, strokes[i].Count) * msPerPoint;
      this.cumulative.push(acc);
    }
    this.totalMs = acc;
  }

  get DurationMs(): number {
    return this.totalMs;
  }

  get StrokeCount(): number {
    return this.source.length;
  }

  isComplete(elapsedMs: number): boolean {
    return elapsedMs >= this.totalMs;
  }

  visibleAt(elapsedMs: number): InkStroke[] {
    const result: InkStroke[] = [];
    for (let i = 0; i < this.source.length; i++) {
      const start = i === 0 ? 0 : this.cumulative[i - 1];
      if (elapsedMs < start) {
        break;
      }
      const end = this.cumulative[i];
      const src = this.source[i];
      if (elapsedMs >= end) {
        result.push(src);
        continue;
      }
      const fraction = (elapsedMs - start) / Math.max(1, end - start);
      const take = Math.max(1, Math.trunc(src.Count * fraction));
      const partial = new InkStroke(src.Attributes.clone());
      partial.restorePoints(src.Points.slice(0, take));
      result.push(partial);
    }
    return result;
  }
}

export interface ReplayVisibleResult {
  counts: number[];
  complete: boolean;
}

export function replayVisibleCounts(
  pointCounts: number[],
  msPerPoint: number,
  elapsedMs: number
): ReplayVisibleResult {
  const strokes: InkStroke[] = [];
  for (let i = 0; i < pointCounts.length; i++) {
    const stroke = new InkStroke(new StrokeAttributes());
    for (let j = 0; j < pointCounts[i]; j++) {
      stroke.addPoint(j, 0);
    }
    strokes.push(stroke);
  }
  const session = new InkReplaySession(strokes, msPerPoint);
  const visible = session.visibleAt(elapsedMs);
  const counts: number[] = [];
  for (let i = 0; i < visible.length; i++) {
    counts.push(visible[i].Count);
  }
  return { counts, complete: session.isComplete(elapsedMs) };
}
