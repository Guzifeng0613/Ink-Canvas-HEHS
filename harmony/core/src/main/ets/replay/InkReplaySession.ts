// @parity src/InkCanvas.Core/Replay/InkReplaySession.cs
import { InkStroke } from '../ink/InkStroke';
import { StrokeAttributes } from '../ink/StrokeAttributes';

export class InkReplaySession {
  private readonly source: InkStroke[];
  private readonly cumulative: number[] = [];
  private readonly totalMs: number;
  private partial: InkStroke | null = null;
  private partialIndex: number = -1;

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

  writeVisibleAt(elapsedMs: number, result: InkStroke[]): void {
    result.length = 0;
    for (let i = 0; i < this.source.length; i++) {
      const start = i === 0 ? 0 : this.cumulative[i - 1];
      if (elapsedMs < start) {
        break;
      }
      const source = this.source[i];
      const end = this.cumulative[i];
      if (elapsedMs >= end) {
        result.push(source);
        continue;
      }
      if (elapsedMs <= 0) {
        break;
      }
      const take = Math.min(Math.max(Math.trunc(source.Count * (elapsedMs - start) / Math.max(1, end - start)), Math.min(1, source.Count)), source.Count);
      if (this.partialIndex !== i) {
        this.partial = new InkStroke(source.Attributes.clone());
        this.partialIndex = i;
      }
      if (this.partial !== null && this.partial.Count !== take) {
        this.partial.restorePrefix(source.Points, take);
      }
      if (this.partial !== null) {
        result.push(this.partial);
      }
      break;
    }
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

export interface ReplayFrameProbeResult {
  Counts: number[][];
  PartialReused: boolean;
  SourceUnchanged: boolean;
}

export function replayFrameProbe(pointCount: number, msPerPoint: number, times: number[]): ReplayFrameProbeResult {
  const stroke = new InkStroke(new StrokeAttributes());
  for (let i = 0; i < pointCount; i++) {
    stroke.addPoint(i, 0);
  }
  stroke.complete();
  const session = new InkReplaySession([stroke], msPerPoint);
  const frame: InkStroke[] = [];
  const counts: number[][] = [];
  let lastPartial: InkStroke | null = null;
  let reused = true;
  for (let t = 0; t < times.length; t++) {
    session.writeVisibleAt(times[t], frame);
    const row: number[] = [];
    for (let i = 0; i < frame.length; i++) {
      row.push(frame[i].Count);
    }
    counts.push(row);
    const isPartial = frame.length === 1 && frame[0] !== stroke;
    if (isPartial) {
      if (lastPartial !== null && frame[0] !== lastPartial) {
        reused = false;
      }
      lastPartial = frame[0];
    } else {
      lastPartial = null;
    }
  }
  return { Counts: counts, PartialReused: reused, SourceUnchanged: stroke.Count === pointCount };
}
