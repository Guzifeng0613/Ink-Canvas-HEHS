// @parity src/InkCanvas.App/Views/CountdownWindow.cs（计时引擎纯逻辑）
export function countdownInterval(totalSeconds: number): number {
  if (totalSeconds <= 10) {
    return 20;
  }
  if (totalSeconds <= 60) {
    return 30;
  }
  if (totalSeconds <= 120) {
    return 50;
  }
  return 100;
}

export function countdownDisplay(leftSeconds: number): number {
  return Math.ceil(leftSeconds);
}

export interface CountdownTickResult {
  Display: number;
  Ring: number;
  Finished: boolean;
}

export function countdownTick(totalSeconds: number, elapsedSeconds: number): CountdownTickResult {
  if (totalSeconds <= 0) {
    return { Display: 0, Ring: 0, Finished: true };
  }
  const spentPercent = elapsedSeconds / totalSeconds;
  const left = Math.max(0, totalSeconds - elapsedSeconds);
  if (spentPercent >= 1) {
    return { Display: 0, Ring: 0, Finished: true };
  }
  return {
    Display: countdownDisplay(left),
    Ring: Math.max(0, 1 - spentPercent),
    Finished: false
  };
}

export class CountdownEngine {
  ConfigSeconds: number = 60;
  TotalSeconds: number = 60;
  IsRunning: boolean = false;
  IsPaused: boolean = false;
  Display: number = 60;
  Ring: number = 1;
  private startMs: number = 0;
  private pauseMs: number = 0;

  configure(hours: number, minutes: number, seconds: number): void {
    this.ConfigSeconds = (hours * 60 + minutes) * 60 + seconds;
    if (!this.IsRunning) {
      this.TotalSeconds = this.ConfigSeconds;
      this.Display = this.ConfigSeconds;
      this.Ring = 1;
    }
  }

  tickIntervalMs(): number {
    return countdownInterval(this.TotalSeconds);
  }

  startPause(nowMs: number): void {
    if (this.IsRunning && this.IsPaused) {
      this.startMs += nowMs - this.pauseMs;
      this.IsPaused = false;
      return;
    }
    if (this.IsRunning) {
      this.pauseMs = nowMs;
      this.IsPaused = true;
      return;
    }
    this.TotalSeconds = this.ConfigSeconds;
    if (this.TotalSeconds <= 0) {
      return;
    }
    this.startMs = nowMs;
    this.IsRunning = true;
    this.IsPaused = false;
    this.Ring = 1;
  }

  reset(nowMs: number): void {
    if (!this.IsRunning) {
      this.TotalSeconds = this.ConfigSeconds;
      this.Display = this.ConfigSeconds;
      this.Ring = 1;
      return;
    }
    if (this.IsPaused) {
      this.IsRunning = false;
      this.IsPaused = false;
      this.TotalSeconds = this.ConfigSeconds;
      this.Display = this.ConfigSeconds;
      this.Ring = 0;
      return;
    }
    this.startMs = nowMs;
    this.tick(nowMs);
  }

  tick(nowMs: number): void {
    if (!this.IsRunning || this.IsPaused) {
      return;
    }
    const elapsed = (nowMs - this.startMs) / 1000;
    const result = countdownTick(this.TotalSeconds, elapsed);
    this.Display = result.Display;
    this.Ring = result.Ring;
    if (result.Finished) {
      this.IsRunning = false;
      this.IsPaused = false;
    }
  }
}
