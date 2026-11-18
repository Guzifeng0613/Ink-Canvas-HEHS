// @parity src/InkCanvas.Core/Ink/OneEuroFilter.cs
// 自适应低通滤波（DIP + 秒），每条笔画独立持有；模拟压感笔锋（SmoothPen..Calligraphy）
// 的输入位置先经此滤波，抑制抖动并限制高速/急转时的可见滞后（maxLag 2.5）。
export class OneEuroFilter {
  private previousX: number = 0;
  private previousY: number = 0;
  private filteredX: number = 0;
  private filteredY: number = 0;
  private derivativeX: number = 0;
  private derivativeY: number = 0;
  private time: number = 0;
  private initialized: boolean = false;

  update(x: number, y: number, timeMs: number): { X: number; Y: number } {
    if (!this.initialized) {
      this.initialized = true;
      this.previousX = this.filteredX = x;
      this.previousY = this.filteredY = y;
      this.time = timeMs;
      return { X: x, Y: y };
    }

    const elapsed = timeMs - this.time;
    // 缺失/重复时间戳无法描述速度：保留样本，但不假设采样间隔、不移动已接受的点。
    if (!Number.isFinite(elapsed) || elapsed <= 0 || elapsed > 200) {
      this.previousX = this.filteredX = x;
      this.previousY = this.filteredY = y;
      this.derivativeX = 0;
      this.derivativeY = 0;
      this.time = timeMs;
      return { X: x, Y: y };
    }

    const seconds = elapsed / 1000;
    const derivativeX = (x - this.previousX) / seconds;
    const derivativeY = (y - this.previousY) / seconds;
    const alphaD = OneEuroFilter.alpha(4, seconds);
    this.derivativeX += alphaD * (derivativeX - this.derivativeX);
    this.derivativeY += alphaD * (derivativeY - this.derivativeY);
    const derivativeLength = Math.sqrt(this.derivativeX * this.derivativeX + this.derivativeY * this.derivativeY);
    const cutoff = 3 + 0.04 * derivativeLength;
    const alphaF = OneEuroFilter.alpha(cutoff, seconds);
    this.filteredX += alphaF * (x - this.filteredX);
    this.filteredY += alphaF * (y - this.filteredY);
    // 高速/急转变向时限制可见滞后。
    const errorX = x - this.filteredX;
    const errorY = y - this.filteredY;
    const maximumLag = 2.5;
    const errorLengthSq = errorX * errorX + errorY * errorY;
    if (errorLengthSq > maximumLag * maximumLag) {
      const errorLength = Math.sqrt(errorLengthSq);
      this.filteredX = x - (errorX / errorLength) * maximumLag;
      this.filteredY = y - (errorY / errorLength) * maximumLag;
    }
    this.previousX = x;
    this.previousY = y;
    this.time = timeMs;
    return { X: this.filteredX, Y: this.filteredY };
  }

  private static alpha(cutoff: number, seconds: number): number {
    return 1 / (1 + 1 / (2 * Math.PI * cutoff * seconds));
  }
}