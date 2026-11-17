// @parity src/InkCanvas.Core/Replay/ReplayClock.cs
export class ReplayClock {
  Speed: number = 1;
  IsPlaying: boolean = false;
  ElapsedMs: number = 0;
  private readonly durationMs: number;

  constructor(durationMs: number) {
    this.durationMs = Math.max(0, durationMs);
  }

  get DurationMs(): number {
    return this.durationMs;
  }

  get IsComplete(): boolean {
    return this.ElapsedMs >= this.durationMs;
  }

  get Progress(): number {
    return this.durationMs <= 0 ? 1 : this.ElapsedMs / this.durationMs;
  }

  play(): void {
    if (this.IsComplete) {
      this.ElapsedMs = 0;
    }
    this.IsPlaying = true;
  }

  pause(): void {
    this.IsPlaying = false;
  }

  toggle(): void {
    if (this.IsPlaying) {
      this.pause();
    } else {
      this.play();
    }
  }

  seek(ms: number): void {
    this.ElapsedMs = Math.min(Math.max(ms, 0), this.durationMs);
  }

  advance(realDeltaMs: number): void {
    if (!this.IsPlaying) {
      return;
    }
    this.ElapsedMs = Math.min(this.durationMs, this.ElapsedMs + realDeltaMs * Math.max(0, this.Speed));
    if (this.ElapsedMs >= this.durationMs) {
      this.IsPlaying = false;
    }
  }
}

export interface ReplayClockSimResult {
  elapsed: number;
  playing: boolean;
  complete: boolean;
  progress: number;
}

export function simulateReplayClock(durationMs: number, speed: number, ops: string[]): ReplayClockSimResult {
  const clock = new ReplayClock(durationMs);
  clock.Speed = speed;
  for (let i = 0; i < ops.length; i++) {
    const op = ops[i];
    if (op === 'play') {
      clock.play();
    } else if (op === 'pause') {
      clock.pause();
    } else if (op === 'toggle') {
      clock.toggle();
    } else if (op.startsWith('seek:')) {
      clock.seek(Number(op.substring(5)));
    } else if (op.startsWith('advance:')) {
      clock.advance(Number(op.substring(8)));
    }
  }
  return {
    elapsed: clock.ElapsedMs,
    playing: clock.IsPlaying,
    complete: clock.IsComplete,
    progress: clock.Progress
  };
}
