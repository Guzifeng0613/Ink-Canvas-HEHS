// @parity src/InkCanvas.Core/Ink/InkPoint.cs
// 可变点（C# 为 struct；ArkTS 用 class，所有复制点必须显式 clone）。
export class InkPoint {
  X: number;
  Y: number;
  Pressure: number;
  Width: number;
  TimeMs: number;

  constructor(x: number = 0, y: number = 0, pressure: number = 0.5, width: number = 0, timeMs: number = 0) {
    this.X = x;
    this.Y = y;
    this.Pressure = pressure;
    this.Width = width;
    this.TimeMs = timeMs;
  }

  clone(): InkPoint {
    return new InkPoint(this.X, this.Y, this.Pressure, this.Width, this.TimeMs);
  }

  static fromArray(row: number[]): InkPoint {
    if (row.length < 4) {
      throw new Error('InkPoint row needs at least 4 values');
    }
    return new InkPoint(row[0], row[1], row[2], row[3], row.length > 4 ? row[4] : 0);
  }
}
