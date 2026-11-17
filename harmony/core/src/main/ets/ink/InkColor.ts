// @parity src/InkCanvas.Core/Ink/InkColor.cs
// ARGB 颜色。C# 用 uint；JS 位运算为有符号 32 位，打包后必须 >>> 0。
export class InkColor {
  A: number;
  R: number;
  G: number;
  B: number;

  constructor(a: number, r: number, g: number, b: number) {
    this.A = a & 0xff;
    this.R = r & 0xff;
    this.G = g & 0xff;
    this.B = b & 0xff;
  }

  static black(): InkColor {
    return new InkColor(255, 0, 0, 0);
  }

  static white(): InkColor {
    return new InkColor(255, 255, 255, 255);
  }

  static red(): InkColor {
    return new InkColor(255, 220, 38, 38);
  }

  static transparent(): InkColor {
    return new InkColor(0, 0, 0, 0);
  }

  static fromPackedArgb(value: number): InkColor {
    const n = value >>> 0;
    return new InkColor((n >>> 24) & 0xff, (n >>> 16) & 0xff, (n >>> 8) & 0xff, n & 0xff);
  }

  packedArgb(): number {
    return (((this.A << 24) | (this.R << 16) | (this.G << 8) | this.B) >>> 0);
  }

  withAlpha(alpha: number): InkColor {
    return new InkColor(alpha, this.R, this.G, this.B);
  }

  equals(other: InkColor): boolean {
    return this.A === other.A && this.R === other.R && this.G === other.G && this.B === other.B;
  }
}

export function packArgb(a: number, r: number, g: number, b: number): number {
  return ((a << 24) | (r << 16) | (g << 8) | b) >>> 0;
}

export function unpackArgb(value: number): InkColor {
  return InkColor.fromPackedArgb(value);
}

export function withAlphaArgb(value: number, alpha: number): number {
  return InkColor.fromPackedArgb(value).withAlpha(alpha).packedArgb();
}
